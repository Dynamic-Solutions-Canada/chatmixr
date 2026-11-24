namespace ChatMixr.Models;

/// <summary>What a switch to a device moves there.</summary>
public enum SwitchScope
{
    /// <summary>
    /// The plain "Switch to" click and auto-switching: games and media always; calls only when they
    /// were on the device being replaced, so a split setup stays split; the microphone when it was a
    /// headset's own (see <see cref="DefaultRoles.Plan"/>), so a desk microphone stays put.
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

/// <summary>A playback device that has its own microphone, as the pairing by container found them.</summary>
/// <param name="Device">The playback device.</param>
/// <param name="Microphone">Its own microphone.</param>
/// <param name="Container">The Windows container both are in, or null when unknown.</param>
public sealed record DeviceMicrophone(string Device, string Microphone, Guid? Container);

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
/// plain switch (<see cref="SwitchScope.Follow"/>) moves calls only when they were with the device
/// being replaced, and the microphone only when it was a headset's own and that headset isn't still
/// in use for sound or calls; the switch menu can move any one of them on its own.
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
    /// <param name="headsetOfMicrophone">The separate headset whose own microphone a recording device is,
    /// or null (<see cref="SeparateHeadsetOf"/>); used to find a headset's microphone left behind.</param>
    /// <returns>Only what actually changes: a default already on the target is left out.</returns>
    public static SwitchPlan Plan(
        SwitchScope scope,
        string device,
        string? deviceMicrophone,
        Defaults now,
        Func<string, string?> microphoneOf,
        Func<string, string?> headsetOfMicrophone)
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

            // The microphone comes along when there was none, when it was the replaced device's own (a
            // headset's mic), or when it is a headset's mic left behind. A separate desk microphone
            // stays where it is.
            SwitchScope.Follow => now.Microphone == null
                || (now.Output != null && Same(microphoneOf(now.Output), now.Microphone))
                || IsLeftBehind(now, headsetOfMicrophone),
            _ => false,
        };

        return new SwitchPlan(
            output && !Same(now.Output, device) ? device : null,
            calls && !Same(now.Calls, device) ? device : null,
            microphone && !Same(now.Microphone, deviceMicrophone) ? deviceMicrophone : null);
    }

    /// <summary>
    /// True when the microphone in use is a headset's own but that headset carries neither games and
    /// media nor calls: it was left there by a switch through a device with no microphone (speakers, a
    /// TV), so going back to a headset should bring the microphone along. A headset still used for calls
    /// keeps its microphone, so "speakers for games, headset for calls" stays split.
    /// </summary>
    private static bool IsLeftBehind(Defaults now, Func<string, string?> headsetOfMicrophone)
    {
        if (now.Microphone == null || headsetOfMicrophone(now.Microphone) is not { } headset)
        {
            return false;
        }

        return !Same(headset, now.Output) && !Same(headset, now.Calls);
    }

    /// <summary>
    /// The headset whose own microphone <paramref name="microphone"/> is, when that headset is a device
    /// of its own. Devices in the PC's own container never count: onboard audio can list a front-panel
    /// "Headphones" output whose paired microphone is the motherboard's mic jack, where a desk
    /// microphone is often plugged in, and that must stay put.
    /// </summary>
    /// <param name="microphone">A recording device.</param>
    /// <param name="pairs">Every listed playback device that has its own microphone.</param>
    /// <returns>The headset's playback device, or null.</returns>
    public static string? SeparateHeadsetOf(string microphone, IEnumerable<DeviceMicrophone> pairs)
    {
        var pair = pairs.FirstOrDefault(p => Same(p.Microphone, microphone) && p.Container != ChatDevicePairing.SoftwareContainer);
        return pair?.Device;
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
