using System.Reflection;
using Microsoft.Playwright;
using Xunit;

namespace TeamsCustomMute.Tests;

public sealed class NotificationMenuTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
        _page = await _browser.NewPageAsync();
        _page.SetDefaultTimeout(5000);
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Theory]
    [InlineData("all", false)]
    [InlineData("mute", true)]
    [InlineData("custom", false)]
    public async Task ReadsCheckedStateWithoutChangingSettings(string selection, bool expected)
    {
        await LoadMenuAsync(selection);

        Assert.Equal(expected, await ReadStateAsync());
        Assert.Equal(0, await ChangeCountAsync());
        await AssertMenusClosedAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangesAndVerifiesNotificationState(bool mute)
    {
        await LoadMenuAsync(mute ? "all" : "mute");

        Assert.Equal(MuteOutcome.Success, await SetStateAsync(mute));
        Assert.Equal(mute ? "mute" : "all", await _page.EvaluateAsync<string>("window.menuState.selection"));
        Assert.Equal(1, await ChangeCountAsync());
        await AssertMenusClosedAsync();
        Assert.Equal(mute, await ReadStateAsync());
    }

    [Theory]
    [InlineData("mute", true)]
    [InlineData("all", false)]
    [InlineData("custom", false)]
    public async Task AlreadyInDesiredStatePreservesSettings(string selection, bool mute)
    {
        await LoadMenuAsync(selection);

        Assert.Equal(MuteOutcome.AlreadyInDesiredState, await SetStateAsync(mute));
        Assert.Equal(selection, await _page.EvaluateAsync<string>("window.menuState.selection"));
        Assert.Equal(0, await ChangeCountAsync());
        await AssertMenusClosedAsync();
    }

    [Fact]
    public async Task ClickWithoutStateChangeIsNotReportedAsSuccess()
    {
        await LoadMenuAsync("all", ignoreClicks: true);

        Assert.Equal(MuteOutcome.AutomationError, await SetStateAsync(true));
        Assert.False(await ReadStateAsync());
        await AssertMenusClosedAsync();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MissingOrUnknownStateDoesNotChangeSettings(bool unknownState, bool missingMute)
    {
        await LoadMenuAsync("all", unknownState: unknownState, missingMute: missingMute);

        Assert.Null(await ReadStateAsync());
        Assert.Equal(MuteOutcome.MenuNotFound, await SetStateAsync(true));
        Assert.Equal(0, await ChangeCountAsync());
        await AssertMenusClosedAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyMenusStillSupportReadingAndChangingState(bool mute)
    {
        await LoadMenuAsync(mute ? "all" : "mute", legacy: true);

        Assert.Equal(!mute, await ReadStateAsync());
        Assert.Equal(MuteOutcome.Success, await SetStateAsync(mute));
        Assert.Equal(mute, await ReadStateAsync());
        Assert.Equal(MuteOutcome.AlreadyInDesiredState, await SetStateAsync(mute));
        Assert.Equal(1, await ChangeCountAsync());
        await AssertMenusClosedAsync();
    }

    [Theory]
    [InlineData("https://teams.cloud.microsoft/", true)]
    [InlineData("https://teams.microsoft.com/v2/", true)]
    [InlineData("https://teams.cloud.microsoft.evil.example/", false)]
    [InlineData("https://example.com/teams.microsoft.com", false)]
    [InlineData("about:blank", false)]
    public void RecognizesOnlyTeamsHosts(string url, bool expected)
    {
        Assert.Equal(expected, (bool)ControllerMethod("IsTeamsPage").Invoke(null, [url])!);
    }

    private static MethodInfo ControllerMethod(string name) =>
        typeof(TeamsWebController).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private Task<bool?> ReadStateAsync() =>
        (Task<bool?>)ControllerMethod("GetMuteStateCoreAsync").Invoke(null, [_page, "Test chat"])!;

    private Task<MuteOutcome> SetStateAsync(bool mute) =>
        (Task<MuteOutcome>)ControllerMethod("SetMuteCoreAsync").Invoke(null, [_page, "Test chat", mute])!;

    private Task<int> ChangeCountAsync() => _page.EvaluateAsync<int>("window.notificationChanges");

    private async Task AssertMenusClosedAsync() => Assert.Equal(0, await _page.GetByRole(AriaRole.Menu).CountAsync());

    private async Task LoadMenuAsync(string selection, bool legacy = false, bool ignoreClicks = false,
        bool unknownState = false, bool missingMute = false)
    {
        await _page.SetContentAsync("""
            <style>
              [hidden] { display: none !important; }
              #chat { width: 200px; height: 40px; }
              #context { position: absolute; top: 60px; left: 10px; }
              #notifications { position: absolute; top: 60px; left: 260px; }
              button { display: block; padding: 8px; }
              span { display: block; }
            </style>
            <div role="tree"><div role="treeitem" id="chat" tabindex="0">Test chat</div></div>
            <button role="menuitemradio" aria-checked="true">Mute unrelated setting</button>
            <div role="menu" id="context" hidden>
              <button role="menuitem" id="manage">Manage notifications</button>
              <button role="menuitem" id="legacy" hidden></button>
            </div>
            <div role="menu" aria-label="Manage notifications" id="notifications" hidden>
              <button role="menuitemradio" id="all">All new messages<span>New messages, all @mentions, and replies to followed threads</span></button>
              <button role="menuitemradio" id="mute">Mute<span>You'll still be notified for personal mentions and replies to followed threads</span></button>
              <button role="menuitem" id="custom">Customize</button>
            </div>
            <script>
              const contextMenu = document.getElementById('context');
              const notifications = document.getElementById('notifications');
              const manage = document.getElementById('manage');
              const legacy = document.getElementById('legacy');
              const mute = document.getElementById('mute');
              const all = document.getElementById('all');
              window.notificationChanges = 0;
              function render() {
                manage.hidden = window.menuState.legacy;
                legacy.hidden = !window.menuState.legacy;
                legacy.textContent = window.menuState.selection === 'mute' ? 'Unmute' : 'Mute';
                mute.hidden = window.menuState.missingMute;
                for (const option of [mute, all]) {
                  if (window.menuState.unknownState) option.removeAttribute('aria-checked');
                  else option.setAttribute('aria-checked', String(option.id === window.menuState.selection));
                }
              }
              function choose(selection) {
                window.notificationChanges++;
                if (!window.menuState.ignoreClicks) window.menuState.selection = selection;
                contextMenu.hidden = true;
                notifications.hidden = true;
              }
              document.getElementById('chat').addEventListener('contextmenu', event => {
                event.preventDefault();
                render();
                contextMenu.hidden = false;
              });
              manage.addEventListener('mouseenter', () => { notifications.hidden = false; });
              mute.addEventListener('click', () => choose('mute'));
              all.addEventListener('click', () => choose('all'));
              legacy.addEventListener('click', () => choose(window.menuState.selection === 'mute' ? 'all' : 'mute'));
              document.addEventListener('keydown', event => {
                if (event.key !== 'Escape') return;
                if (!notifications.hidden) notifications.hidden = true;
                else contextMenu.hidden = true;
              });
            </script>
            """);
        await _page.EvaluateAsync("state => window.menuState = state",
            new { selection, legacy, ignoreClicks, unknownState, missingMute });
    }
}