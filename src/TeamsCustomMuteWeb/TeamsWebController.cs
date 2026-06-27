using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace TeamsCustomMute;

/// <summary>Result of a single mute/unmute attempt against Teams web.</summary>
public enum MuteOutcome
{
    Success,
    AlreadyInDesiredState,
    ChatNotFound,
    NotSignedIn,
    MenuNotFound,
    AutomationError,
}

/// <summary>
/// Drives Teams web through a single long-lived, signed-in Edge profile via Playwright.
/// All public operations are serialized through a semaphore so the one browser context
/// is never used concurrently. The context is kept alive for the lifetime of the app
/// (relaunching per-operation is far too slow).
/// </summary>
public sealed class TeamsWebController : IAsyncDisposable
{
    private static readonly string UserDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamsCustomMute", "EdgeProfile");

    private const string TeamsUrl = "https://teams.microsoft.com/v2/";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _pw;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _headless = true;

    private volatile IReadOnlyList<string> _recentCache = Array.Empty<string>();

    /// <summary>
    /// Last known recent-chat list, refreshed in the background. Reads are instant (no browser
    /// round-trip) so the Mute dialog can open immediately instead of waiting on Teams web.
    /// </summary>
    public IReadOnlyList<string> CachedRecentChats => _recentCache;

    /// <summary>Re-reads the recent chats and updates <see cref="CachedRecentChats"/>.</summary>
    public Task RefreshRecentChatsAsync() => ListRecentChatsAsync(20);

    /// <summary>Returns true if the Teams chat list is reachable (i.e. the profile is signed in).</summary>
    public async Task<bool> IsSignedInAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureStartedAsync(_headless);
            return await IsReadyAsync(_page!, 10000);
        }
        catch
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Opens a visible Edge window for interactive sign-in and waits until the chat list
    /// appears (or the timeout elapses), then returns to background/headless operation.
    /// </summary>
    public async Task<bool> SignInInteractiveAsync(TimeSpan timeout)
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureStartedAsync(headless: false);
            await _page!.BringToFrontAsync();
            var ready = await IsReadyAsync(_page, (int)timeout.TotalMilliseconds);
            // Switch back to a silent background context for normal muting.
            await EnsureStartedAsync(headless: true);
            return ready;
        }
        catch
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Mute or unmute a single chat by display name.</summary>
    public async Task<MuteOutcome> SetMuteAsync(string chat, bool mute)
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureStartedAsync(headless: true);
            var page = _page!;
            if (!await IsReadyAsync(page, 15000))
                return MuteOutcome.NotSignedIn;
            return await SetMuteCoreAsync(page, chat, mute);
        }
        catch
        {
            return MuteOutcome.AutomationError;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads whether a chat is currently muted in Teams without changing it: returns
    /// <c>true</c> if muted, <c>false</c> if unmuted, or <c>null</c> if the state can't be
    /// determined (not signed in, chat not found, or a transient error). Used to detect
    /// mutes the user cleared manually inside Teams so the app can stay in sync.
    /// </summary>
    public async Task<bool?> IsMutedAsync(string chat)
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureStartedAsync(headless: true);
            var page = _page!;
            if (!await IsReadyAsync(page, 15000))
                return null;
            return await GetMuteStateCoreAsync(page, chat);
        }
        catch
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Enumerate the chat names currently visible in the rail.</summary>
    public async Task<IReadOnlyList<string>> ListChatsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureStartedAsync(headless: true);
            var page = _page!;
            if (!await IsReadyAsync(page, 15000))
                return Array.Empty<string>();

            var items = page.GetByRole(AriaRole.Treeitem);
            var count = await items.CountAsync();
            var names = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var label = await items.Nth(i).GetAttributeAsync("aria-label");
                if (!string.IsNullOrWhiteSpace(label))
                    names.Add(label.Trim());
            }
            return names;
        }
        catch
        {
            return Array.Empty<string>();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns up to <paramref name="max"/> chat titles from the recent "Chats" section of the
    /// rail, newest first. Falls back to the last non-system section, or a flat leaf list, so it
    /// still works when the user has no custom sections. Trailing unread badges are stripped so
    /// the returned title reliably matches the chat row when muting.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListRecentChatsAsync(int max = 20)
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureStartedAsync(headless: true);
            var page = _page!;
            if (!await IsReadyAsync(page, 15000))
                return Array.Empty<string>();

            const string js = @"(max) => {
  var clean = function(el){
    var c = el.cloneNode(true);
    var kids = c.querySelectorAll('[role=treeitem]');
    for (var i=0;i<kids.length;i++){ kids[i].remove(); }
    return (c.innerText||'').replace(/\s+/g,' ').replace(/\s+\d+(\s+\d+)*$/,'').trim();
  };
  var headers = Array.prototype.slice.call(document.querySelectorAll('[role=treeitem][aria-level=""1""]'))
    .filter(function(h){ return h.querySelector('[role=treeitem]'); });
  var section = null;
  for (var i=0;i<headers.length;i++){ if (/^chats$/i.test(clean(headers[i]))){ section = headers[i]; break; } }
  if (!section){
    var skip = /^(favorites|quick views|copilot|pinned)$/i;
    var cand = headers.filter(function(h){ return !skip.test(clean(h)); });
    section = cand.length ? cand[cand.length-1] : null;
  }
  var items;
  if (section){ items = section.querySelectorAll('[role=treeitem][aria-level=""2""]'); }
  else {
    items = Array.prototype.slice.call(document.querySelectorAll('[role=treeitem][aria-level=""1""]'))
      .filter(function(t){ return !t.querySelector('[role=treeitem]'); });
  }
  var res = []; var seen = {};
  for (var j=0;j<items.length;j++){
    var t = clean(items[j]);
    if (!t) continue;
    var k = t.toLowerCase();
    if (seen[k]) continue;
    seen[k]=1; res.push(t);
    if (res.length>=max) break;
  }
  return res;
}";
            var result = await page.EvaluateAsync<string[]>(js, max);
            var list = result ?? Array.Empty<string>();
            if (list.Length > 0)
                _recentCache = list;
            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- internals -------------------------------------------------------


    private async Task EnsureStartedAsync(bool headless)
    {
        if (_context is not null && _headless == headless)
            return;

        // A persistent context locks the user-data dir to a single instance, and the
        // headless flag can't be flipped at runtime — so tear down and recreate.
        await StopContextAsync();

        _pw ??= await Playwright.CreateAsync();
        Directory.CreateDirectory(UserDataDir);

        _context = await _pw.Chromium.LaunchPersistentContextAsync(UserDataDir, new BrowserTypeLaunchPersistentContextOptions
        {
            Channel = "msedge",
            Headless = headless,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            Args = new[] { "--disable-blink-features=AutomationControlled" },
        });
        _headless = headless;

        _page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
        _page.SetDefaultTimeout(20000);

        if (!(_page.Url ?? string.Empty).Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase))
            await _page.GotoAsync(TeamsUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    private async Task StopContextAsync()
    {
        try
        {
            if (_context is not null)
                await _context.DisposeAsync();
        }
        catch
        {
            // best-effort teardown
        }
        _context = null;
        _page = null;
    }

    private static async Task<bool> IsReadyAsync(IPage page, int timeoutMs)
    {
        try
        {
            if (!(page.Url ?? string.Empty).Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase))
                await page.GotoAsync(TeamsUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

            await page.GetByRole(AriaRole.Treeitem).First.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<MuteOutcome> SetMuteCoreAsync(IPage page, string chat, bool mute)
    {
        var nameRx = new Regex(Regex.Escape(chat), RegexOptions.IgnoreCase);
        var row = page.GetByRole(AriaRole.Treeitem, new PageGetByRoleOptions { NameRegex = nameRx }).First;

        if (!await WaitVisibleAsync(row, 5000))
        {
            // Not in the rail — try searching for it (best-effort).
            if (!await OpenViaSearchAsync(page, chat))
                return MuteOutcome.ChatNotFound;

            row = page.GetByRole(AriaRole.Treeitem, new PageGetByRoleOptions { NameRegex = nameRx }).First;
            if (!await WaitVisibleAsync(row, 5000))
                return MuteOutcome.ChatNotFound;
        }

        await row.ScrollIntoViewIfNeededAsync();
        await row.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });

        var wanted = mute ? "Mute" : "Unmute";
        var opposite = mute ? "Unmute" : "Mute";

        var wantedItem = page.GetByRole(AriaRole.Menuitem,
            new PageGetByRoleOptions { NameRegex = new Regex($"^{wanted}$", RegexOptions.IgnoreCase) });

        if (await WaitVisibleAsync(wantedItem, 4000))
        {
            await wantedItem.ClickAsync();
            return MuteOutcome.Success;
        }

        // The wanted item isn't there — check whether the chat is already in the desired state.
        var oppositeItem = page.GetByRole(AriaRole.Menuitem,
            new PageGetByRoleOptions { NameRegex = new Regex($"^{opposite}$", RegexOptions.IgnoreCase) });
        var alreadyThere = await oppositeItem.CountAsync() > 0;

        await page.Keyboard.PressAsync("Escape");
        return alreadyThere ? MuteOutcome.AlreadyInDesiredState : MuteOutcome.MenuNotFound;
    }

    /// <summary>
    /// Right-clicks a chat and reads its current mute state from the context menu without
    /// changing it: the menu shows "Unmute" when muted and "Mute" when not. Returns null if
    /// the chat or menu can't be found.
    /// </summary>
    private static async Task<bool?> GetMuteStateCoreAsync(IPage page, string chat)
    {
        var nameRx = new Regex(Regex.Escape(chat), RegexOptions.IgnoreCase);
        var row = page.GetByRole(AriaRole.Treeitem, new PageGetByRoleOptions { NameRegex = nameRx }).First;

        if (!await WaitVisibleAsync(row, 5000))
            return null; // Not visible in the rail; don't disturb the UI by searching.

        await row.ScrollIntoViewIfNeededAsync();
        await row.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });

        var unmuteItem = page.GetByRole(AriaRole.Menuitem,
            new PageGetByRoleOptions { NameRegex = new Regex("^Unmute$", RegexOptions.IgnoreCase) });
        var muteItem = page.GetByRole(AriaRole.Menuitem,
            new PageGetByRoleOptions { NameRegex = new Regex("^Mute$", RegexOptions.IgnoreCase) });

        bool? muted = null;
        if (await WaitVisibleAsync(unmuteItem, 3000))
            muted = true;
        else if (await muteItem.CountAsync() > 0)
            muted = false;

        await page.Keyboard.PressAsync("Escape");
        return muted;
    }

    /// <summary>Best-effort: use the Teams search/command box to surface a chat not shown in the rail.</summary>
    private static async Task<bool> OpenViaSearchAsync(IPage page, string chat)
    {
        try
        {
            // Ctrl+E focuses the Teams command/search box.
            await page.Keyboard.PressAsync("Control+E");
            await page.WaitForTimeoutAsync(600);

            ILocator search = page.GetByRole(AriaRole.Combobox).First;
            if (await search.CountAsync() == 0)
                search = page.GetByPlaceholder(new Regex("Search", RegexOptions.IgnoreCase));

            await search.FillAsync(chat);
            await page.WaitForTimeoutAsync(1500);

            var rx = new Regex(Regex.Escape(chat), RegexOptions.IgnoreCase);
            var option = page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { NameRegex = rx }).First;
            if (await option.CountAsync() == 0)
                option = page.GetByRole(AriaRole.Listitem, new PageGetByRoleOptions { NameRegex = rx }).First;

            await option.ClickAsync(new LocatorClickOptions { Timeout = 4000 });
            await page.WaitForTimeoutAsync(1500);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitVisibleAsync(ILocator locator, int timeoutMs)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopContextAsync();
            _pw?.Dispose();
            _pw = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
