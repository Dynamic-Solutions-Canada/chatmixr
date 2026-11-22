namespace ChatMixr.Models;

/// <summary>
/// Which apps are on the chat side of the mix. Apps are matched by their program name without
/// ".exe", ignoring case, so "Discord.exe" and "discord" are the same entry. Every other app playing
/// on the headset is on the game side.
/// </summary>
public static class ChatApps
{
    /// <summary>
    /// Common voice chat apps: their program name on Windows and the name people know them by. A new
    /// install starts with all of them on the chat list, and the Chat apps page offers any that were
    /// removed so they can be added back. Product names are never translated.
    /// </summary>
    public static IReadOnlyList<(string Program, string Name)> Presets { get; } =
    [
        ("Discord", "Discord"),
        ("DiscordPTB", "Discord PTB"),
        ("DiscordCanary", "Discord Canary"),
        ("ms-teams", "Microsoft Teams"),
        ("Teams", "Microsoft Teams (classic)"),
        ("Zoom", "Zoom"),
        ("slack", "Slack"),
        ("ts3client_win64", "TeamSpeak 3"),
        ("TeamSpeak", "TeamSpeak"),
        ("mumble", "Mumble"),
        ("Signal", "Signal"),
        ("WhatsApp", "WhatsApp"),
        ("Telegram", "Telegram"),
        ("Element", "Element"),
    ];

    /// <summary>The list a new install starts with: every preset's program name.</summary>
    public static IReadOnlyList<string> Defaults { get; } = Presets.Select(p => p.Program).ToList();

    /// <summary>A preset's known name for a program, or null when it isn't a preset.</summary>
    public static string? PresetName(string programName) =>
        Presets.FirstOrDefault(p => string.Equals(p.Program, Normalize(programName), StringComparison.OrdinalIgnoreCase)).Name;

    /// <summary>The presets that are not on the list, in preset order.</summary>
    public static List<(string Program, string Name)> MissingPresets(IEnumerable<string> chatApps)
    {
        var list = chatApps.ToList();
        return Presets.Where(p => !IsChat(p.Program, list)).ToList();
    }

    /// <summary>How many recently heard programs are remembered.</summary>
    public const int RecentLimit = 30;

    /// <summary>
    /// The recently heard list after hearing <paramref name="heard"/>: newly heard programs move to the
    /// front, the rest keep their order, and the list is cut to <see cref="RecentLimit"/>.
    /// </summary>
    /// <returns>The new list, or null when nothing changed (so the caller needn't save).</returns>
    public static List<string>? Remember(IReadOnlyList<string> recent, IEnumerable<string> heard)
    {
        var fresh = heard.Select(Normalize)
            .Where(p => p.Length > 0 && !recent.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (fresh.Count == 0)
        {
            return null;
        }

        return fresh.Concat(recent).Take(RecentLimit).ToList();
    }

    /// <summary>
    /// The form a program name is compared in: trimmed, without a trailing ".exe".
    /// </summary>
    /// <returns>The normalized name, or an empty string when nothing is left.</returns>
    public static string Normalize(string programName)
    {
        string name = programName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4].TrimEnd();
        }

        return name;
    }

    /// <summary>True when <paramref name="programName"/> is in <paramref name="chatApps"/>.</summary>
    public static bool IsChat(string programName, IEnumerable<string> chatApps)
    {
        string name = Normalize(programName);
        return chatApps.Any(entry => string.Equals(Normalize(entry), name, StringComparison.OrdinalIgnoreCase));
    }
}
