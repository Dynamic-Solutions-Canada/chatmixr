namespace ChatMixr.Models;

/// <summary>
/// When a playback device appearing or disappearing makes ChatMixr switch, with "Switch when a device
/// becomes available" on. A TV, monitor or speaker comes and goes as it is turned on, woken or
/// unplugged; a wireless headset is never judged here, since its receiver says when it turns on or off
/// (<see cref="HeadsetPower"/>). Between changes a manual pick sticks: a device appearing only switches
/// when it is the one the user's order now prefers.
/// </summary>
public static class DeviceChanges
{
    /// <summary>
    /// The device to switch to after some devices appeared, or null to leave things as they are.
    /// </summary>
    /// <param name="preferred">The first ready device in the user's order, devices just appeared included (<see cref="DeviceOrder.Preferred"/>).</param>
    /// <param name="arrived">The devices that appeared since the last scan, headsets left out.</param>
    /// <returns><paramref name="preferred"/> when it is one that just appeared, otherwise null.</returns>
    public static string? ArrivalTarget(string? preferred, IReadOnlyCollection<string> arrived)
    {
        if (preferred == null)
        {
            return null;
        }

        return arrived.Contains(preferred, StringComparer.OrdinalIgnoreCase) ? preferred : null;
    }

    /// <summary>
    /// True when the device in use went away: Windows' default moved off it without ChatMixr, and it is
    /// among the listed devices that disappeared. Windows then picks a replacement nobody chose, so
    /// ChatMixr switches to the first ready device in the user's order instead. Judged by what
    /// disappeared, not by what is listed now: a move off a device ChatMixr never lists (Sonar's, the
    /// cable) is the user's own pick, and must stick.
    /// </summary>
    /// <param name="left">The device the default last moved off without ChatMixr, or null.</param>
    /// <param name="removed">Listed devices that disappeared in the latest scans.</param>
    public static bool InUseWentAway(string? left, IEnumerable<string> removed) =>
        left != null && removed.Contains(left, StringComparer.OrdinalIgnoreCase);
}
