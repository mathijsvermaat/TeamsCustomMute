using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TeamsCustomMute;

/// <summary>Tray UI for the Playwright/Teams-web backed mute app.</summary>
public sealed class TrayAppContext : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "TeamsCustomMuteWeb";

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly TeamsWebController _controller;
    private readonly MuteManager _manager;
    private readonly SynchronizationContext _ui;
    private readonly System.Threading.Timer _recentRefreshTimer;

    public TrayAppContext()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _controller = new TeamsWebController();

        _icon = new NotifyIcon
        {
            Icon = AppIcon.Value,
            Text = "Teams Custom Mute (Web)",
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OnMuteChat();

        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => PopulateMenu();
        _icon.ContextMenuStrip = _menu;

        _manager = new MuteManager(_controller);
        _manager.Notify += (title, message) =>
            _ui.Post(_ => ShowBalloon(title, message), null);

        // Warm up the browser and confirm we're signed in (in the background).
        _ = CheckSignInAsync();

        // Keep the recent-chats cache warm so the Mute dialog opens instantly.
        _recentRefreshTimer = new System.Threading.Timer(
            _ => { _ = _controller.RefreshRecentChatsAsync(); },
            null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
    }

    private async Task CheckSignInAsync()
    {
        var signedIn = await _controller.IsSignedInAsync();
        if (!signedIn)
        {
            _ui.Post(_ => ShowBalloon("Sign in needed",
                "Right-click the tray icon \u2192 \u201cSign in to Teams web\u2026\u201d to connect."), null);
            return;
        }

        // Pre-fetch recent chats so the first “Mute a chat…” opens with no wait.
        try { await _controller.RefreshRecentChatsAsync(); }
        catch { /* best-effort warm-up */ }
    }

    private void PopulateMenu()
    {
        _menu.Items.Clear();

        _menu.Items.Add("Mute a chat\u2026", null, (_, _) => OnMuteChat());

        var favorites = FavoritesStore.Load();
        var favMenu = new ToolStripMenuItem("Mute a favorite");
        if (favorites.Count == 0)
        {
            favMenu.DropDownItems.Add(new ToolStripMenuItem("(no favorites yet)") { Enabled = false });
        }
        else
        {
            foreach (var fav in favorites)
            {
                var name = fav;
                // A favorite mutes straight away — pick a duration and it's done, no dialog.
                var favItem = new ToolStripMenuItem(name);
                foreach (var (label, span) in FavoriteDurations)
                {
                    var duration = span;
                    favItem.DropDownItems.Add(label, null, (_, _) => MuteFavorite(name, duration));
                }
                favMenu.DropDownItems.Add(favItem);
            }
        }
        _menu.Items.Add(favMenu);

        _menu.Items.Add("Manage favorites\u2026", null, (_, _) => OnManageFavorites());

        _menu.Items.Add(new ToolStripSeparator());

        var activeMenu = new ToolStripMenuItem("Active mutes");
        var pending = _manager.Pending;
        if (pending.Count == 0)
        {
            activeMenu.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
        }
        else
        {
            activeMenu.DropDownItems.Add(new ToolStripMenuItem("Click a chat below to unmute it now:") { Enabled = false });
            activeMenu.DropDownItems.Add(new ToolStripSeparator());
            foreach (var p in pending)
            {
                var remaining = p.ExpiresUtc - DateTime.UtcNow;
                var label = $"{p.ChatName}  \u2014  {FormatRemaining(remaining)} left  (unmute now)";
                var chat = p.ChatName;
                var item = new ToolStripMenuItem(label, null, (_, _) => OnUnmuteNow(chat))
                {
                    ToolTipText = "Click to unmute this chat now",
                };
                activeMenu.DropDownItems.Add(item);
            }
        }
        _menu.Items.Add(activeMenu);

        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add("Sign in to Teams web\u2026", null, (_, _) => OnSignIn());

        var startup = new ToolStripMenuItem("Run at Windows startup")
        {
            Checked = IsRunAtStartupEnabled(),
            CheckOnClick = true,
        };
        startup.Click += (_, _) => SetRunAtStartup(startup.Checked);
        _menu.Items.Add(startup);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitApp());
    }

    private static readonly (string Label, TimeSpan Span)[] FavoriteDurations =
    {
        ("1 hour", TimeSpan.FromHours(1)),
        ("4 hours", TimeSpan.FromHours(4)),
        ("8 hours", TimeSpan.FromHours(8)),
        ("1 day", TimeSpan.FromDays(1)),
        ("1 week", TimeSpan.FromDays(7)),
    };

    private void MuteFavorite(string chat, TimeSpan duration)
    {
        ShowBalloon("Muting\u2026", $"Asking Teams to mute \u201c{chat}\u201d.");
        _manager.MuteAsync(chat, duration).ContinueWith(t =>
            _ui.Post(_ => ShowMuteOutcome(chat, t), null));
    }

    private void OnMuteChat(string? prefillChat = null)
    {
        // Open immediately using the warm cache; only block on a live fetch the very
        // first time (before the background warm-up has populated the cache).
        var cached = _controller.CachedRecentChats;
        if (cached.Count > 0)
        {
            ShowMuteDialog(prefillChat, cached);
            _ = _controller.RefreshRecentChatsAsync(); // keep it fresh for next time
        }
        else
        {
            _ = ShowMuteDialogAsync(prefillChat);
        }
    }

    private async Task ShowMuteDialogAsync(string? prefillChat)
    {
        IReadOnlyList<string> recent = Array.Empty<string>();
        try
        {
            recent = await _controller.ListRecentChatsAsync(20);
        }
        catch
        {
            // Recent chats are a convenience; a failure shouldn't block muting.
        }

        _ui.Post(_ => ShowMuteDialog(prefillChat, recent), null);
    }

    private void ShowMuteDialog(string? prefillChat, IReadOnlyList<string> recentChats)
    {
        var favorites = FavoritesStore.Load();
        using var dialog = new MuteDialog(favorites, prefillChat, recentChats);
        dialog.Icon = AppIcon.Value;
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        var chat = dialog.ChatName;
        var duration = dialog.Duration;
        if (dialog.SaveToFavorites)
            FavoritesStore.Add(chat);

        ShowBalloon("Muting\u2026", $"Asking Teams to mute \u201c{chat}\u201d.");

        _manager.MuteAsync(chat, duration).ContinueWith(t =>
            _ui.Post(_ => ShowMuteOutcome(chat, t), null));
    }

    private void OnUnmuteNow(string chat)
    {
        _manager.UnmuteNowAsync(chat).ContinueWith(t =>
            _ui.Post(_ =>
            {
                var outcome = t.Status == TaskStatus.RanToCompletion ? t.Result : MuteOutcome.AutomationError;
                if (outcome is MuteOutcome.Success or MuteOutcome.AlreadyInDesiredState or MuteOutcome.ChatNotFound)
                    ShowBalloon("Unmuted", $"\u201c{chat}\u201d was unmuted.");
                else if (outcome == MuteOutcome.NotSignedIn)
                    ShowBalloon("Sign in needed", "Sign in to Teams web first.");
                else
                    ShowBalloon("Couldn't unmute", $"Teams didn't respond for \u201c{chat}\u201d. It will retry.");
            }, null));
    }

    private void OnManageFavorites()
    {
        using var dialog = new FavoritesDialog();
        dialog.Icon = AppIcon.Value;
        dialog.ShowDialog();
    }

    private void OnSignIn()
    {
        ShowBalloon("Sign in",
            "Opening Teams web\u2026 complete sign-in in the window that appears.");

        _controller.SignInInteractiveAsync(TimeSpan.FromMinutes(5)).ContinueWith(t =>
            _ui.Post(_ =>
            {
                var ok = t.Status == TaskStatus.RanToCompletion && t.Result;
                ShowBalloon(
                    ok ? "Signed in" : "Sign-in incomplete",
                    ok
                        ? "Teams web is connected. Muting now runs silently in the background."
                        : "Couldn't open the Teams web window. Close any open Microsoft Edge windows, then try \u201cSign in to Teams web\u2026\u201d again.");
            }, null));
    }

    private void ShowMuteOutcome(string chat, Task<MuteOutcome> task)
    {
        var outcome = task.Status == TaskStatus.RanToCompletion ? task.Result : MuteOutcome.AutomationError;
        switch (outcome)
        {
            case MuteOutcome.Success:
                ShowBalloon("Muted", $"\u201c{chat}\u201d is muted.");
                break;
            case MuteOutcome.AlreadyInDesiredState:
                ShowBalloon("Already muted", $"\u201c{chat}\u201d was already muted; timer set.");
                break;
            case MuteOutcome.ChatNotFound:
                ShowBalloon("Chat not found",
                    $"Couldn't find \u201c{chat}\u201d in Teams. Check the exact name.");
                break;
            case MuteOutcome.NotSignedIn:
                ShowBalloon("Sign in needed",
                    "Sign in to Teams web first (right-click \u2192 \u201cSign in to Teams web\u2026\u201d).");
                break;
            case MuteOutcome.MenuNotFound:
                ShowBalloon("Couldn't mute",
                    $"The mute option didn't appear for \u201c{chat}\u201d.");
                break;
            default:
                ShowBalloon("Couldn't reach Teams",
                    $"Something went wrong muting \u201c{chat}\u201d. Try again.");
                break;
        }
    }

    /// <summary>
    /// Shows a tray notification. Once <see cref="ToastBranding"/> has registered an explicit
    /// AppUserModelID, <see cref="NotifyIcon.ShowBalloonTip(int, string, string, ToolTipIcon)"/>
    /// is routed through the modern Windows toast pipeline, where the app icon and name come
    /// from the branded shortcut. A toast built with <see cref="ToolTipIcon.None"/> is delivered
    /// to the notification center but its banner is suppressed, so we pass
    /// <see cref="ToolTipIcon.Info"/> to make the banner appear reliably.
    /// </summary>
    private void ShowBalloon(string title, string text)
    {
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);
    }

    private static string FormatRemaining(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        return $"{(int)t.TotalMinutes}m";
    }

    private static bool IsRunAtStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    private static void SetRunAtStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(RunValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private void ExitApp()
    {
        _icon.Visible = false;
        _recentRefreshTimer.Dispose();
        try { _manager.DisposeAsync().AsTask().Wait(2000); } catch { }
        try { _controller.DisposeAsync().AsTask().Wait(5000); } catch { }
        _icon.Dispose();
        ExitThread();
    }
}
