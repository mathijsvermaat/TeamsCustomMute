namespace TeamsCustomMute;

/// <summary>
/// Tracks pending mutes and auto-unmutes them when they expire. A repeating timer
/// fires immediately on startup (so chats whose mute lapsed while the machine was
/// asleep or powered off are unmuted as soon as the app runs again) and every 60s
/// thereafter. All Teams interaction is delegated to <see cref="TeamsWebController"/>.
/// </summary>
public sealed class MuteManager : IAsyncDisposable
{
    private readonly TeamsWebController _teams;
    private readonly object _gate = new();
    private readonly List<PendingMute> _pending;
    private readonly System.Threading.Timer _timer;
    private int _ticking;

    public event Action<string, string>? Notify;
    public event Action? Changed;

    public MuteManager(TeamsWebController teams)
    {
        _teams = teams;
        _pending = MuteStore.Load();
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    public IReadOnlyList<PendingMute> Pending
    {
        get { lock (_gate) return _pending.OrderBy(p => p.ExpiresUtc).ToList(); }
    }

    public async Task<MuteOutcome> MuteAsync(string chat, TimeSpan duration)
    {
        var outcome = await _teams.SetMuteAsync(chat, mute: true);
        if (outcome is MuteOutcome.Success or MuteOutcome.AlreadyInDesiredState)
        {
            lock (_gate)
            {
                _pending.RemoveAll(p => p.ChatName.Equals(chat, StringComparison.OrdinalIgnoreCase));
                _pending.Add(new PendingMute(chat, DateTime.UtcNow + duration));
                MuteStore.Save(_pending);
            }
            Changed?.Invoke();
        }
        return outcome;
    }

    public async Task<MuteOutcome> UnmuteNowAsync(string chat)
    {
        var outcome = await _teams.SetMuteAsync(chat, mute: false);
        if (outcome is MuteOutcome.Success or MuteOutcome.AlreadyInDesiredState or MuteOutcome.ChatNotFound)
        {
            lock (_gate)
            {
                _pending.RemoveAll(p => p.ChatName.Equals(chat, StringComparison.OrdinalIgnoreCase));
                MuteStore.Save(_pending);
            }
            Changed?.Invoke();
        }
        return outcome;
    }

    private async Task TickAsync()
    {
        // Prevent overlapping ticks (a slow browser op shouldn't be re-entered).
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
            return;

        try
        {
            List<PendingMute> due;
            lock (_gate)
                due = _pending.Where(p => p.ExpiresUtc <= DateTime.UtcNow).ToList();

            foreach (var p in due)
            {
                var outcome = await _teams.SetMuteAsync(p.ChatName, mute: false);
                if (outcome is MuteOutcome.Success or MuteOutcome.AlreadyInDesiredState or MuteOutcome.ChatNotFound)
                {
                    lock (_gate)
                    {
                        _pending.RemoveAll(x =>
                            x.ChatName.Equals(p.ChatName, StringComparison.OrdinalIgnoreCase) &&
                            x.ExpiresUtc == p.ExpiresUtc);
                        MuteStore.Save(_pending);
                    }
                    Notify?.Invoke("Teams unmuted", $"\u201c{p.ChatName}\u201d was unmuted (mute expired).");
                    Changed?.Invoke();
                }
                else
                {
                    // Not signed in / transient error — leave it pending and retry next tick.
                    break;
                }
            }

            await ReconcileManualUnmutesAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    /// <summary>
    /// Detects mutes the user cleared manually inside Teams: for each still-pending (not yet
    /// expired) mute, asks Teams for the real state and drops any that are already unmuted so
    /// the app's "Active mutes" list stays in sync.
    /// </summary>
    private async Task ReconcileManualUnmutesAsync()
    {
        List<PendingMute> active;
        lock (_gate)
            active = _pending.Where(p => p.ExpiresUtc > DateTime.UtcNow).ToList();

        foreach (var p in active)
        {
            var muted = await _teams.IsMutedAsync(p.ChatName);
            if (muted is null)
                continue; // Couldn't determine (not signed in / not visible) — leave as-is.

            if (muted == false)
            {
                lock (_gate)
                {
                    _pending.RemoveAll(x =>
                        x.ChatName.Equals(p.ChatName, StringComparison.OrdinalIgnoreCase) &&
                        x.ExpiresUtc == p.ExpiresUtc);
                    MuteStore.Save(_pending);
                }
                Notify?.Invoke("Mute cleared", $"\u201c{p.ChatName}\u201d was unmuted in Teams; removed from active mutes.");
                Changed?.Invoke();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _timer.DisposeAsync();
    }
}
