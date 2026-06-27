using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace TeamsWebSpike;

/// <summary>
/// Spike: drive Teams *web* (https://teams.microsoft.com) through your installed Edge against a
/// dedicated, persistent profile. You sign in once; the session is cached in a user-data-dir we
/// own, so later runs are silent. Muting happens via the DOM, so it never steals foreground or
/// moves your mouse — it's truly background.
///
/// Commands:
///   login              Open Teams web so you can sign in once (then press Enter to close).
///   list               Print the chat rows we can see (names + aria-labels) for selector tuning.
///   mute   "&lt;name&gt;"     Mute the first chat whose name contains &lt;name&gt;.
///   unmute "&lt;name&gt;"     Unmute it.
///   shot               Save a screenshot to spike-screenshot.png (debugging).
///
/// Flags:
///   --show             Run Edge visibly (default is minimized/offscreen-ish background).
/// </summary>
internal static class Program
{
    private static readonly string UserDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamsCustomMute", "EdgeProfile");

    private const string TeamsUrl = "https://teams.microsoft.com/v2/";

    private static async Task<int> Main(string[] args)
    {
        var show = args.Contains("--show", StringComparer.OrdinalIgnoreCase);
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var command = positional.ElementAtOrDefault(0)?.ToLowerInvariant() ?? "help";
        var target = positional.ElementAtOrDefault(1) ?? string.Empty;

        if (command == "help")
        {
            Console.WriteLine("Usage: dotnet run -- <login|list|mute|unmute|shot> [\"chat name\"] [--show]");
            return 0;
        }

        Directory.CreateDirectory(UserDataDir);

        using var playwright = await Playwright.CreateAsync();

        // "login" should always be visible so you can complete sign-in / MFA.
        var headless = command != "login" && !show;

        await using var context = await playwright.Chromium.LaunchPersistentContextAsync(UserDataDir, new()
        {
            Channel = "msedge",                 // use your installed Edge (compliant/managed Chromium)
            Headless = headless,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            Args = new[] { "--disable-blink-features=AutomationControlled" },
        });

        var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);

        await page.GotoAsync(TeamsUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        switch (command)
        {
            case "login":
                Console.WriteLine("Edge opened on Teams web. Sign in if prompted, wait until your chat");
                Console.WriteLine("list is visible, then press Enter here to save the session and exit.");
                Console.ReadLine();
                return 0;

            case "shot":
                await WaitForTeamsReady(page);
                await page.ScreenshotAsync(new() { Path = "spike-screenshot.png", FullPage = true });
                Console.WriteLine("Saved spike-screenshot.png");
                return 0;

            case "list":
                await WaitForTeamsReady(page);
                await ListChats(page);
                return 0;

            case "mute":
            case "unmute":
                if (string.IsNullOrWhiteSpace(target))
                {
                    Console.Error.WriteLine("Provide a chat name, e.g. dotnet run -- mute \"Ronny de Jong\"");
                    return 2;
                }
                await WaitForTeamsReady(page);
                var ok = await SetMute(page, target, mute: command == "mute");
                Console.WriteLine(ok ? "Done." : "Could not complete the action (see messages above).");
                return ok ? 0 : 1;

            default:
                Console.Error.WriteLine($"Unknown command '{command}'. Try: login | list | mute | unmute | shot");
                return 2;
        }
    }

    /// <summary>Wait until the Teams chat rail looks loaded.</summary>
    private static async Task WaitForTeamsReady(IPage page)
    {
        // The chat list renders rows as treeitems; wait for at least one to show up.
        try
        {
            await page.GetByRole(AriaRole.Treeitem).First.WaitForAsync(new() { Timeout = 30_000 });
        }
        catch
        {
            Console.Error.WriteLine("Teams chat list didn't appear within 30s. If this is a fresh profile,");
            Console.Error.WriteLine("run 'login' first to sign in, then retry.");
        }
    }

    /// <summary>Print visible chat rows so we can see exactly what selectors/names Teams exposes.</summary>
    private static async Task ListChats(IPage page)
    {
        var rows = page.GetByRole(AriaRole.Treeitem);
        var count = await rows.CountAsync();
        Console.WriteLine($"Found {count} treeitem rows:");
        for (var i = 0; i < count; i++)
        {
            var row = rows.Nth(i);
            var label = await row.GetAttributeAsync("aria-label") ?? string.Empty;
            var text = (await row.InnerTextAsync()).Replace("\n", " ").Trim();
            Console.WriteLine($"  [{i}] aria-label=\"{label}\"  text=\"{Truncate(text, 60)}\"");
        }
    }

    /// <summary>Mute/unmute the first chat row whose accessible name contains <paramref name="chat"/>.</summary>
    private static async Task<bool> SetMute(IPage page, string chat, bool mute)
    {
        var rx = new Regex(Regex.Escape(chat), RegexOptions.IgnoreCase);
        var row = page.GetByRole(AriaRole.Treeitem, new() { NameRegex = rx }).First;

        try
        {
            await row.WaitForAsync(new() { Timeout = 10_000 });
        }
        catch
        {
            Console.Error.WriteLine($"No chat row matching \"{chat}\" is currently rendered.");
            Console.Error.WriteLine("Tip: run 'list' to see available rows; the chat may need to be scrolled into view.");
            return false;
        }

        await row.ScrollIntoViewIfNeededAsync();
        // Open the row's context menu (DOM right-click — no real mouse movement).
        await row.ClickAsync(new() { Button = MouseButton.Right });

        var wanted = mute ? "Mute" : "Unmute";
        var opposite = mute ? "Unmute" : "Mute";

        var wantedItem = page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex($"^{wanted}$", RegexOptions.IgnoreCase) });
        try
        {
            await wantedItem.WaitForAsync(new() { Timeout = 4_000 });
            await wantedItem.ClickAsync();
            Console.WriteLine($"Clicked '{wanted}' for \"{chat}\".");
            return true;
        }
        catch
        {
            // Maybe it's already in the desired state (only the opposite item is present).
            var oppositeItem = page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex($"^{opposite}$", RegexOptions.IgnoreCase) });
            if (await oppositeItem.CountAsync() > 0)
            {
                Console.WriteLine($"Already {(mute ? "muted" : "unmuted")} (only '{opposite}' was offered). Closing menu.");
                await page.Keyboard.PressAsync("Escape");
                return true;
            }

            Console.Error.WriteLine($"Couldn't find a '{wanted}' menu item. Menu items seen:");
            var items = page.GetByRole(AriaRole.Menuitem);
            var n = await items.CountAsync();
            for (var i = 0; i < n; i++)
                Console.Error.WriteLine($"  - {await items.Nth(i).InnerTextAsync()}");
            await page.Keyboard.PressAsync("Escape");
            return false;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
