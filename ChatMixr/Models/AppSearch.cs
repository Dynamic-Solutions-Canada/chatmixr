namespace ChatMixr.Models;

/// <summary>An installed app the chat app search can offer.</summary>
/// <param name="Name">The name people know it by, from its Start menu shortcut, uninstall entry or package.</param>
/// <param name="Program">The program name its sound plays under, without ".exe": what the chat list stores.</param>
public sealed record InstalledApp(string Name, string Program);

/// <summary>
/// The portable rules behind the chat app search: which program an app's shortcut really runs, and
/// how matches are ranked.
/// </summary>
public static class AppSearch
{
    /// <summary>
    /// The program a shortcut really runs. Apps installed with Squirrel (Discord, Slack, Teams classic)
    /// point their shortcut at <c>Update.exe --processStart Discord.exe</c>; the program whose sound
    /// plays is the one after <c>--processStart</c>, not Update.exe.
    /// </summary>
    /// <param name="target">The shortcut's target path.</param>
    /// <param name="arguments">The shortcut's arguments, or null.</param>
    /// <returns>The program name without ".exe", or null when the target is not a program.</returns>
    public static string? ProgramOf(string target, string? arguments)
    {
        const string ProcessStart = "--processStart";
        if (arguments != null)
        {
            int at = arguments.IndexOf(ProcessStart, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                string rest = arguments[(at + ProcessStart.Length)..].Trim();
                string exe = rest.StartsWith('"')
                    ? rest[1..].Split('"')[0]
                    : rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return ChatApps.Normalize(exe);
                }
            }
        }

        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string file = target.Replace('/', '\\').Split('\\')[^1];
        return ChatApps.Normalize(file);
    }

    /// <summary>
    /// True for entries that are not apps a person chats in: installers, uninstallers, runtimes,
    /// SDKs and drivers. The same filter Little Launcher uses for its app list, plus uninstallers.
    /// </summary>
    public static bool IsNotAnApp(string name, string program)
    {
        string[] filters =
        [
            "SDK", "Runtime", "Redistributable", "Targeting Pack", "Manifest", "Toolset", "Template",
            "Hosting Bundle", "Driver", "Firmware", "Chipset", "Language Pack", "Visual C++",
            ".NET", "Uninstall", "Setup", "Installer", "Help", "Documentation", "Readme",
        ];

        if (filters.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return program.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            || program.Equals("Update", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith('{');
    }

    /// <summary>
    /// The apps matching what was typed, best first: names that start with it, then names with a
    /// word that starts with it, then names or program names that contain it. Apps already on the
    /// chat list are left out.
    /// </summary>
    /// <param name="apps">Every installed app.</param>
    /// <param name="typed">What the user typed.</param>
    /// <param name="chatApps">The chat list, to leave out.</param>
    /// <param name="limit">The most results to return.</param>
    public static List<InstalledApp> Find(IEnumerable<InstalledApp> apps, string typed, IReadOnlyList<string> chatApps, int limit = 8)
    {
        string query = typed.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        return apps
            .Where(a => !ChatApps.IsChat(a.Program, chatApps))
            .Select(a => (App: a, Rank: Rank(a, query)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.App.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.App)
            .ToList();
    }

    /// <summary>0 for a name prefix, 1 for a word prefix, 2 for a substring of name or program, -1 for no match.</summary>
    private static int Rank(InstalledApp app, string query)
    {
        if (app.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var words = app.Name.Split([' ', '-', '_', '.', '('], StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(query, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        bool contains = app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || app.Program.Contains(query, StringComparison.OrdinalIgnoreCase);
        return contains ? 2 : -1;
    }
}
