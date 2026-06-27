using System.Diagnostics;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace TeamsMutePoc;

/// <summary>
/// Proof-of-concept: drive the *local* New Teams desktop client (WebView2) via UI Automation
/// to mute/unmute a single chat by name.
///
/// Usage:
///   TeamsMutePoc dump                 Dump the Teams accessibility tree to teams-uia-dump.txt
///   TeamsMutePoc find  "<chat name>"  Locate a chat list item by (partial) name and report it
///   TeamsMutePoc menu  "<chat name>"  Open that chat's context menu and list its items
///   TeamsMutePoc mute  "<chat name>"  Open the context menu and click "Mute"
///   TeamsMutePoc unmute "<chat name>" Open the context menu and click "Unmute"
///
/// This is intentionally verbose: New Teams' accessibility surface is inconsistent, so the
/// PoC's job is as much to *reveal what is reachable* as it is to perform the action.
/// </summary>
internal static class Program
{
    private const string TeamsProcessName = "ms-teams";

    private static int Main(string[] args)
    {
        var command = (args.Length > 0 ? args[0] : "dump").ToLowerInvariant();
        var target = args.Length > 1 ? args[1] : string.Empty;

        using var automation = new UIA3Automation();

        var teams = FindTeamsWindow(automation);
        if (teams is null)
        {
            Console.Error.WriteLine(
                "Could not find a New Teams window (process 'ms-teams.exe'). " +
                "Make sure New Teams is running and not minimized to tray only.");
            return 2;
        }

        Console.WriteLine($"Found Teams window: \"{Safe(teams.Name)}\" (pid {teams.Properties.ProcessId.ValueOrDefault})");

        switch (command)
        {
            case "dump":
                DumpTree(teams);
                return 0;

            case "list":
                return ListChats(teams) ? 0 : 1;

            case "click":
                RequireTarget(target);
                return ClickChat(teams, target) ? 0 : 1;

            case "find":
                RequireTarget(target);
                var found = FindChatItem(teams, target);
                if (found is null) { Console.Error.WriteLine($"No element found matching \"{target}\"."); return 1; }
                Console.WriteLine($"Match: {Describe(found)}");
                return 0;

            case "menu":
                RequireTarget(target);
                return OpenMenuAndList(teams, target) ? 0 : 1;

            case "mute":
                RequireTarget(target);
                return ToggleMute(teams, target, mute: true) ? 0 : 1;

            case "unmute":
                RequireTarget(target);
                return ToggleMute(teams, target, mute: false) ? 0 : 1;

            default:
                Console.Error.WriteLine($"Unknown command '{command}'. Use: dump | list | find | click | menu | mute | unmute");
                return 64;
        }
    }

    private static void RequireTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("This command needs a chat name, e.g.: mute \"Jane Doe\"");
            Environment.Exit(64);
        }
    }

    /// <summary>Find the top-level New Teams window among all desktop windows.</summary>
    private static Window? FindTeamsWindow(UIA3Automation automation)
    {
        var desktop = automation.GetDesktop();
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

        Window? best = null;
        foreach (var w in windows)
        {
            var pid = w.Properties.ProcessId.ValueOrDefault;
            string procName;
            try { procName = Process.GetProcessById(pid).ProcessName; }
            catch { continue; }

            if (!procName.Equals(TeamsProcessName, StringComparison.OrdinalIgnoreCase))
                continue;

            var window = w.AsWindow();
            // Prefer a window that actually has a title (the main app window).
            if (!string.IsNullOrWhiteSpace(window.Name))
                return window;

            best ??= window;
        }
        return best;
    }

    /// <summary>List the chat-list entries currently visible in Teams (raw + cleaned name).</summary>
    private static bool ListChats(AutomationElement root)
    {
        var rows = new List<(string Raw, string Clean)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Walk(root, maxDepth: 40, nodeCap: 25000, visit: (el, _) =>
        {
            if (el.Properties.ControlType.ValueOrDefault != ControlType.TreeItem)
                return;

            var raw = el.Properties.Name.ValueOrDefault ?? string.Empty;
            if (!raw.StartsWith("Chat ", StringComparison.Ordinal) || raw.Contains("(Ctrl"))
                return;

            var clean = CleanChatName(raw);
            if (clean.Length > 0 && seen.Add(clean))
                rows.Add((raw, clean));
        });

        if (rows.Count == 0)
        {
            Console.WriteLine("No chats found. Open the Chat tab in Teams so the list is rendered, then retry.");
            return false;
        }

        Console.WriteLine($"Found {rows.Count} chat(s):");
        foreach (var (raw, clean) in rows)
            Console.WriteLine($"  - \"{clean}\"   (raw: {Safe(raw)})");
        return true;
    }

    /// <summary>Click a chat to select/open it (verifies we can pick one programmatically).</summary>
    private static bool ClickChat(AutomationElement root, string target)
    {
        var item = FindChatItem(root, target);
        if (item is null) { Console.Error.WriteLine($"No chat matching \"{target}\"."); return false; }

        Console.WriteLine($"Clicking: {Describe(item)}");
        if (!Invoke(item))
            item.Click();
        Console.WriteLine($"Clicked \"{target}\" — it should now be the open/selected chat in Teams.");
        return true;
    }

    private static readonly string[] PresenceSuffixes =
    {
        "Available", "Away", "Busy", "Do not disturb", "Be right back",
        "Offline", "Presence unknown", "In a call", "In a meeting",
        "Out of office", "Focusing",
    };

    /// <summary>Strip the leading "Chat " prefix and trailing presence/unread tokens.</summary>
    private static string CleanChatName(string treeItemName)
    {
        var name = treeItemName.Trim();
        if (name.StartsWith("Chat ", StringComparison.Ordinal))
            name = name["Chat ".Length..];

        foreach (var suffix in PresenceSuffixes)
        {
            if (name.EndsWith(" " + suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^(suffix.Length + 1)];
                break;
            }
        }

        var unreadIdx = name.IndexOf(" Unread", StringComparison.OrdinalIgnoreCase);
        if (unreadIdx >= 0)
            name = name[..unreadIdx];

        return name.Trim();
    }

    /// <summary>
    /// Walk the tree (depth-limited, node-capped) looking for a list item whose name contains
    /// the target text. Returns the best (shallowest) match.
    /// </summary>
    private static AutomationElement? FindChatItem(AutomationElement root, string target)
    {
        AutomationElement? match = null;
        Walk(root, maxDepth: 40, nodeCap: 25000, visit: (el, depth) =>
        {
            var name = el.Properties.Name.ValueOrDefault ?? string.Empty;
            if (name.Contains(target, StringComparison.OrdinalIgnoreCase))
            {
                // Favor list-item-ish controls; otherwise accept first textual match.
                var ct = el.Properties.ControlType.ValueOrDefault;
                if (match is null || ct is ControlType.ListItem or ControlType.TreeItem)
                    match = el;
            }
        });
        return match;
    }

    private static bool OpenMenuAndList(Window teams, string target)
    {
        var item = FindChatItem(teams, target);
        if (item is null) { Console.Error.WriteLine($"No chat matching \"{target}\"."); return false; }

        Console.WriteLine($"Right-clicking: {Describe(item)}");
        item.RightClick();
        Thread.Sleep(600);

        var menuItems = CollectMenuItems(teams);
        if (menuItems.Count == 0)
        {
            Console.WriteLine("No menu items detected. The context menu may be a WebView popup not exposed to UIA.");
            Console.WriteLine("Run 'dump' right after right-clicking to inspect what (if anything) appears.");
            return false;
        }

        Console.WriteLine("Context menu items:");
        foreach (var mi in menuItems)
            Console.WriteLine($"  - {Describe(mi)}");
        return true;
    }

    private static bool ToggleMute(Window teams, string target, bool mute)
    {
        var item = FindChatItem(teams, target);
        if (item is null) { Console.Error.WriteLine($"No chat matching \"{target}\"."); return false; }

        Console.WriteLine($"Right-clicking: {Describe(item)}");
        item.RightClick();
        Thread.Sleep(600);

        var wanted = mute ? new[] { "Mute", "Mute chat", "Mute conversation" }
                          : new[] { "Unmute", "Unmute chat", "Unmute conversation" };

        var menuItems = CollectMenuItems(teams);
        var hit = menuItems.FirstOrDefault(mi =>
        {
            var n = mi.Properties.Name.ValueOrDefault ?? string.Empty;
            return wanted.Any(w => n.Equals(w, StringComparison.OrdinalIgnoreCase));
        });

        if (hit is null)
        {
            Console.Error.WriteLine($"Could not find a '{(mute ? "Mute" : "Unmute")}' item in the context menu.");
            if (menuItems.Count > 0)
            {
                Console.Error.WriteLine("Items that were visible:");
                foreach (var mi in menuItems) Console.Error.WriteLine($"  - {Safe(mi.Properties.Name.ValueOrDefault)}");
            }
            else
            {
                Console.Error.WriteLine("No menu items were exposed to UIA (likely a WebView popup).");
            }
            // Dismiss the menu.
            try { teams.Focus(); } catch { /* ignore */ }
            return false;
        }

        Console.WriteLine($"Clicking menu item: {Describe(hit)}");
        if (!Invoke(hit))
            hit.Click();

        Console.WriteLine($"Done: requested {(mute ? "MUTE" : "UNMUTE")} for \"{target}\".");
        return true;
    }

    /// <summary>Collect menu-item-like elements anywhere under the Teams window (New Teams renders menus in-app).</summary>
    private static List<AutomationElement> CollectMenuItems(AutomationElement root)
    {
        var items = new List<AutomationElement>();
        Walk(root, maxDepth: 40, nodeCap: 25000, visit: (el, depth) =>
        {
            var ct = el.Properties.ControlType.ValueOrDefault;
            if (ct is ControlType.MenuItem)
                items.Add(el);
        });
        return items;
    }

    private static bool Invoke(AutomationElement el)
    {
        try
        {
            var invoke = el.Patterns.Invoke.PatternOrDefault;
            if (invoke is not null) { invoke.Invoke(); return true; }
        }
        catch { /* fall through to click */ }
        return false;
    }

    // ---- Tree dump --------------------------------------------------------

    private static void DumpTree(AutomationElement root)
    {
        var sb = new StringBuilder();
        var count = 0;
        Walk(root, maxDepth: 40, nodeCap: 25000, visit: (el, depth) =>
        {
            count++;
            sb.Append(new string(' ', depth * 2));
            sb.AppendLine(Describe(el));
        });

        var path = Path.Combine(Environment.CurrentDirectory, "teams-uia-dump.txt");
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"Wrote {count} elements to {path}");
        Console.WriteLine("Search that file for the chat names / 'Mute' to see what is reachable.");
    }

    // ---- Tree walking helper ---------------------------------------------

    private static void Walk(AutomationElement root, int maxDepth, int nodeCap, Action<AutomationElement, int> visit)
    {
        var visited = 0;
        var stack = new Stack<(AutomationElement el, int depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (el, depth) = stack.Pop();
            if (++visited > nodeCap) break;

            try { visit(el, depth); } catch { /* element went away */ }

            if (depth >= maxDepth) continue;

            AutomationElement[] children;
            try { children = el.FindAllChildren(); }
            catch { continue; }

            // Push in reverse so the tree is visited roughly top-to-bottom.
            for (var i = children.Length - 1; i >= 0; i--)
                stack.Push((children[i], depth + 1));
        }
    }

    // ---- Formatting -------------------------------------------------------

    private static string Describe(AutomationElement el)
    {
        var ct = el.Properties.ControlType.ValueOrDefault;
        var name = Safe(el.Properties.Name.ValueOrDefault);
        var autoId = Safe(el.Properties.AutomationId.ValueOrDefault);
        var cls = Safe(el.Properties.ClassName.ValueOrDefault);
        return $"[{ct}] name='{name}' id='{autoId}' class='{cls}'";
    }

    private static string Safe(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 80 ? s[..80] + "…" : s;
    }
}
