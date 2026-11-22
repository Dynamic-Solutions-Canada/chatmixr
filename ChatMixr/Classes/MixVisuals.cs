using ChatMixr.Models;
using WinUIAppKit.Localization;

namespace ChatMixr.Classes;

/// <summary>
/// The words and icons every view uses for the mix, so the tray tooltip, the popup and the main
/// window can't describe the same state differently.
/// </summary>
internal static class MixVisuals
{
    /// <summary>Segoe Fluent Icons glyphs for each kind of device.</summary>
    public static string Glyph(OutputKind kind) => kind switch
    {
        OutputKind.Headphones => "",
        OutputKind.Display => "",
        _ => "",
    };

    /// <summary>True when a device's mix turns either side down.</summary>
    public static bool IsBalancing(OutputDevice device) =>
        device.Levels is MixLevels levels && (levels.Game < 100 || levels.Chat < 100);

    /// <summary>One line summing up the whole mix, for the tray tooltip and the popup footer.</summary>
    public static string StatusLine(MixSnapshot snapshot)
    {
        if (!snapshot.HasScanned)
        {
            return UiText.Get("status.looking");
        }

        if (snapshot.SonarRunning && snapshot.Devices.Any(d => d.HasWheel))
        {
            return UiText.Get("status.sonar");
        }

        if (snapshot.Devices.Count == 0)
        {
            return UiText.Get("status.no-output");
        }

        int balancing = snapshot.Devices.Count(IsBalancing);
        return balancing > 0
            ? UiText.Plural("status.mixing", balancing)
            : UiText.Get("status.centered");
    }

    /// <summary>
    /// The line under a device's name: what its mix acts on (the programs playing through it), and
    /// for a wheel device whose wheel hasn't moved yet, that it is waiting for it.
    /// </summary>
    public static string Activity(OutputDevice device)
    {
        // A wireless headset's on/off and battery show in its badge (HeadsetBadge), not here.
        if (device.Headset is { On: false })
        {
            return "";
        }

        if (device.HasWheel && device.Levels == null)
        {
            return UiText.Get("device.wheel-unknown");
        }

        if (device.Playing.Count == 0)
        {
            return "";
        }

        return UiText.Format("device.playing", ("apps", string.Join(UiText.Get("device.playing-separator"), device.Playing)));
    }

    /// <summary>
    /// What to do while the virtual chat device is on: pick it, by its full name as games and chat
    /// apps list it, as the voice output.
    /// </summary>
    public static string VirtualChatInstruction(string cableName) =>
        UiText.Format("in-game-chat.instruction", ("cable", cableName));
}
