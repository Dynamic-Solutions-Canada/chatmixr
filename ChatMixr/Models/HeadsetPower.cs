namespace ChatMixr.Models;

/// <summary>
/// When news about a headset's power counts as it turning on or off, the changes auto-switching acts on.
/// A receiver that is already connected when ChatMixr starts only reports the state the headset is in,
/// which is never a change. A receiver plugged in while ChatMixr runs (on its own, or on a hub) brings
/// a headset that wasn't usable a moment ago, so its first state counts as one.
/// </summary>
public static class HeadsetPower
{
    /// <summary>
    /// The state a power or status message is compared against.
    /// </summary>
    /// <param name="known">The headset's last known state, or null when none has arrived yet.</param>
    /// <param name="connectedLate">True when its receiver was plugged in after ChatMixr's first scan.</param>
    /// <returns>The known state; off for a receiver plugged in late; otherwise null (nothing to compare).</returns>
    public static bool? Before(bool? known, bool connectedLate) =>
        known ?? (connectedLate ? false : null);

    /// <summary>True when going from <paramref name="before"/> to <paramref name="isOn"/> is a change.</summary>
    public static bool IsChange(bool? before, bool isOn) =>
        before != null && before != isOn;
}
