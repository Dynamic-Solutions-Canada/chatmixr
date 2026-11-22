using System.Text.Json.Nodes;

namespace ChatMixr.Classes;

/// <summary>
/// Upgrades for older settings files, one step per schema version (CONF-3). The kit's versioned
/// <see cref="WinUIAppKit.Settings.JsonSettingsStore{T}"/> backs the file up first, then runs the step at
/// index <c>n</c> to take it from version <c>n</c> to <c>n + 1</c>. A file with no
/// <c>SchemaVersion</c> is version 0. Steps are never removed or reordered once shipped.
/// </summary>
internal static class SettingsMigrations
{
    /// <summary>The steps, in order; the current schema version is their count.</summary>
    public static IReadOnlyList<Action<JsonObject>> Steps { get; } =
    [
        CableNamesBecomeOptIn,
    ];

    /// <summary>
    /// 0 to 1: renaming the cable became opt-in (it changes Windows itself), and the calls device
    /// (<c>CallsDevice</c>) was added, absent until ChatMixr first sees one. Version 0 always saved
    /// <c>RenameCable</c> as true, chosen or not, so it is kept on only where ChatMixr actually renamed
    /// or hid something: the switch then matches what Windows shows.
    /// </summary>
    private static void CableNamesBecomeOptIn(JsonObject settings)
    {
        bool renamed = settings["CableOriginalNames"] is JsonObject names && names.Count > 0;
        bool hid = settings["HiddenCableEndpoints"] is JsonArray hidden && hidden.Count > 0;
        settings["RenameCable"] = renamed || hid;
    }
}
