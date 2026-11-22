namespace ChatMixr.Models;

/// <summary>What a switch to a device moves there.</summary>
public enum SwitchScope
{
    /// <summary>
    /// The plain "Switch to" click and auto-switching: games and media always; calls and the
    /// microphone only when they were on the device being replaced, so a split setup stays split.
    /// </summary>
    Follow,

    /// <summary>Games and media only.</summary>
    Output,

    /// <summary>Calls only (Windows' communications device).</summary>
    Calls,

    /// <summary>The device's own microphone only.</summary>
    Microphone,

    /// <summary>Games and media, calls, and the device's own microphone.</summary>
    All,
}

/// <summary>Windows' defaults as ChatMixr sees them, by endpoint id; null when there is none.</summary>
/// <param name="Output">The playback device games and media go to.</param>
/// <param name="Calls">
/// The playback device calls go to: the one the user chose, even while in-game voice chat lends
/// Windows' communications default to the cable.
/// </param>
/// <param name="Microphone">The default recording device.</param>
public sealed record Defaults(string? Output, string? Calls, string? Microphone);

/// <summary>What one switch changes: each id to make the default, or null to leave that one alone.</summary>
public sealed record SwitchPlan(string? Output, string? Calls, string? Microphone)
{
    /// <summary>True when the switch changes nothing.</summary>
    public bool IsEmpty => Output == null && Calls == null && Microphone == null;
}

/// <summary>
/// Windows keeps three defaults that a person thinks of as "my headset": where games and media play,
/// where calls play, and which microphone apps hear. Switching only the first breaks a headset that
/// was used for everything; switching all three breaks "speakers for games, headset for calls". So a
/// plain switch moves the others only when they were with the device being replaced
/// (<see cref="SwitchScope.Follow"/>), and the switch menu can move any one of them on its own.
/// </summary>
public static class DefaultRoles
{
    /// <summary>Work out what a switch to <paramref name="device"/> changes.</summary>
    /// <param name="scope">What the user asked to move.</param>
    /// <param name="device">The playback device switched to.</param>
    /// <param name="deviceMicrophone">That device's own microphone, or null when it has none.</param>
    /// <param name="now">The defaults before the switch.</param>
    /// <param name="microphoneOf">A playback device's own microphone, or null; used to tell whether the
    /// microphone in use belongs to the device being replaced.</param>
    /// <returns>Only what actually changes: a default already on the target is left out.</returns>
    public static SwitchPlan Plan(SwitchScope scope, string device, string? deviceMicrophone, Defaults now, Func<string, string?> microphoneOf)
    {
        bool output = scope is SwitchScope.Follow or SwitchScope.Output or SwitchScope.All;
        bool calls = scope switch
        {
            SwitchScope.Calls or SwitchScope.All => true,

            // Calls come along when they were with the device being replaced, or nowhere at all.
            SwitchScope.Follow => now.Calls == null || Same(now.Calls, now.Output),
            _ => false,
        };
        bool microphone = deviceMicrophone != null && scope switch
        {
            SwitchScope.Microphone or SwitchScope.All => true,

            // The microphone comes along when it was the replaced device's own (a headset's mic), or
            // there was none. A separate desk microphone stays where it is.
            SwitchScope.Follow => now.Microphone == null
                || (now.Output != null && Same(microphoneOf(now.Output), now.Microphone)),
            _ => false,
        };

        return new SwitchPlan(
            output && !Same(now.Output, device) ? device : null,
            calls && !Same(now.Calls, device) ? device : null,
            microphone && !Same(now.Microphone, deviceMicrophone) ? deviceMicrophone : null);
    }

    /// <summary>
    /// The calls device after something other than ChatMixr changed where games and media play (Windows'
    /// own picker, another app) while ChatMixr holds Windows' communications default for the cable and so
    /// can't see calls follow. Calls follow the same rule as a plain switch: they move with the output
    /// when they were with it (or nowhere), and stay put in a split setup.
    /// </summary>
    /// <param name="calls">The calls device before the change, or null.</param>
    /// <param name="oldOutput">Where games and media played before, or null.</param>
    /// <param name="newOutput">Where they play now, or null.</param>
    public static string? CallsAfterOutputChange(string? calls, string? oldOutput, string? newOutput)
    {
        if (newOutput == null || Same(oldOutput, newOutput))
        {
            return calls;
        }

        return calls == null || Same(calls, oldOutput) ? newOutput : calls;
    }

    private static bool Same(string? a, string? b) => a != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
