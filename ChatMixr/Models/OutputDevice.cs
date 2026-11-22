namespace ChatMixr.Models;

/// <summary>What kind of device an output is, for its icon.</summary>
public enum OutputKind
{
    /// <summary>Speakers, a soundbar, or anything else.</summary>
    Speakers,

    /// <summary>Headphones or a headset.</summary>
    Headphones,

    /// <summary>A monitor or TV over HDMI or DisplayPort.</summary>
    Display,
}

/// <summary>
/// What the interface shows about one playback device. A snapshot: the mix engine replaces it when
/// anything changes, so views can keep it without locking.
/// </summary>
/// <param name="Id">Windows' endpoint id, stable while the device stays on the same port.</param>
/// <param name="Name">The name Windows shows, for example "Headphones (Arctis Nova 7X)".</param>
/// <param name="Kind">What kind of device it is, for its icon.</param>
/// <param name="IsDefault">True when Windows sends apps to it unless told otherwise.</param>
/// <param name="HasWheel">True when it belongs to a connected receiver whose ChatMix wheel sets its mix.</param>
/// <param name="Levels">The levels it is mixed at; null for a wheel device until its wheel moves.</param>
/// <param name="Playing">Programs playing sound through it now, sorted.</param>
/// <param name="Headset">For a device with a ChatMix wheel: whether its headset is on, and its battery; null otherwise.</param>
/// <param name="EndpointName">The endpoint's own name, the part before the brackets: "Headphones", "Soundbar - Office".</param>
/// <param name="AdapterName">The device it belongs to, the part in brackets: "Arctis Nova 7X Gen 2"; empty when unknown.</param>
/// <param name="IsCalls">True when calls go to it (the calls device the user chose).</param>
/// <param name="HasMicrophone">True when it has a microphone of its own (a headset's).</param>
/// <param name="IsMicrophone">True when its own microphone is Windows' default.</param>
public sealed record OutputDevice(string Id, string Name, OutputKind Kind, bool IsDefault, bool HasWheel, MixLevels? Levels, IReadOnlyList<string> Playing, HeadsetState? Headset, string EndpointName, string AdapterName,
    bool IsCalls = false, bool HasMicrophone = false, bool IsMicrophone = false)
{
    /// <summary>True when the two would draw the same (record equality compares the list by reference).</summary>
    public bool SameAs(OutputDevice other) =>
        Id == other.Id
        && Name == other.Name
        && Kind == other.Kind
        && IsDefault == other.IsDefault
        && HasWheel == other.HasWheel
        && Levels == other.Levels
        && Playing.SequenceEqual(other.Playing)
        && Headset == other.Headset
        && EndpointName == other.EndpointName
        && AdapterName == other.AdapterName
        && IsCalls == other.IsCalls
        && HasMicrophone == other.HasMicrophone
        && IsMicrophone == other.IsMicrophone;
}

/// <summary>A wireless headset's state, from its receiver.</summary>
/// <param name="On">True when on, false when off, null until the receiver has said.</param>
/// <param name="Battery">Its battery, 0 to 100, or null until the receiver has said.</param>
public sealed record HeadsetState(bool? On, int? Battery);

/// <summary>What the chat relay is doing, for the interface.</summary>
public enum RelayState
{
    /// <summary>No chat device chosen, or nowhere to play it.</summary>
    Off,

    /// <summary>Listening to the chat device and playing it.</summary>
    Running,

    /// <summary>A stream could not be opened or was lost; it is retried every few seconds.</summary>
    Failed,
}

/// <summary>The device in use's own volume, as Windows' volume control shows it.</summary>
/// <param name="Percent">0 to 100.</param>
/// <param name="Muted">True when muted.</param>
public sealed record DeviceVolume(int Percent, bool Muted);

/// <summary>A microphone to make the default.</summary>
/// <param name="Id">Its endpoint id, or null when there is no real microphone to suggest.</param>
/// <param name="Name">The name Windows shows, or null.</param>
public sealed record MicrophoneChoice(string? Id, string? Name);

/// <summary>Everything the interface shows about the mix, as one snapshot.</summary>
/// <param name="Devices">Active playback devices, in the user's order.</param>
/// <param name="SonarRunning">True while SteelSeries Sonar runs; it routes app audio to its own devices.</param>
/// <param name="HasScanned">False until the first look at the devices has finished.</param>
/// <param name="PlayingPrograms">Programs with audio on any device in the latest pass, sorted, for the chat app list.</param>
/// <param name="CableName">The virtual cable Separate chat routes through, or null when none is installed.</param>
/// <param name="Relay">What the chat relay is doing.</param>
/// <param name="TestingDeviceId">The device whose test sounds are playing, or null.</param>
/// <param name="Volume">The device in use's own volume, 0 to 100, and whether it's muted; null when unknown.</param>
/// <param name="MicrophoneFix">
/// Set while the cable's recording side is Windows' default microphone (nobody would be heard): the
/// real microphone to switch to, or a choice with no id when there is none.
/// </param>
public sealed record MixSnapshot(
    IReadOnlyList<OutputDevice> Devices,
    bool SonarRunning,
    bool HasScanned,
    IReadOnlyList<string> PlayingPrograms,
    string? CableName,
    RelayState Relay,
    string? TestingDeviceId,
    MicrophoneChoice? MicrophoneFix,
    DeviceVolume? Volume)
{
    /// <summary>The state before the engine has looked at anything.</summary>
    public static MixSnapshot Initial { get; } = new([], false, false, [], null, RelayState.Off, null, null, null);

    /// <summary>True when the two snapshots would draw the same.</summary>
    public bool SameAs(MixSnapshot other) =>
        SonarRunning == other.SonarRunning
        && HasScanned == other.HasScanned
        && Devices.Count == other.Devices.Count
        && Devices.Zip(other.Devices).All(pair => pair.First.SameAs(pair.Second))
        && PlayingPrograms.SequenceEqual(other.PlayingPrograms)
        && CableName == other.CableName
        && Relay == other.Relay
        && TestingDeviceId == other.TestingDeviceId
        && MicrophoneFix == other.MicrophoneFix
        && Volume == other.Volume;
}

/// <summary>
/// The user's device order. Windows lists devices in an order nobody chose and that can't be changed,
/// so ChatMixr keeps its own: devices in the saved order first, then any it hasn't seen, in Windows'
/// order. Saved ids of devices that are not connected now keep their place for when they return.
/// </summary>
public static class DeviceOrder
{
    /// <summary>Order the present devices by the saved order.</summary>
    /// <param name="presentIds">The devices present now, in Windows' order.</param>
    /// <param name="savedOrder">The user's saved order; may name devices that are not present.</param>
    public static List<string> Apply(IReadOnlyList<string> presentIds, IReadOnlyList<string> savedOrder)
    {
        var present = new HashSet<string>(presentIds, StringComparer.OrdinalIgnoreCase);
        var ordered = savedOrder.Where(present.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ordered.AddRange(presentIds.Where(id => !ordered.Contains(id, StringComparer.OrdinalIgnoreCase)));
        return ordered;
    }

    /// <summary>
    /// The saved order after the user put the present devices in <paramref name="newPresentOrder"/>.
    /// Devices not present keep their saved position relative to the others.
    /// </summary>
    public static List<string> Save(IReadOnlyList<string> newPresentOrder, IReadOnlyList<string> savedOrder)
    {
        var present = new HashSet<string>(newPresentOrder, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        int next = 0;
        foreach (string id in savedOrder.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Each slot a present device had is refilled from the new order, so absent devices stay put.
            result.Add(present.Contains(id) ? newPresentOrder[next++] : id);
        }

        result.AddRange(newPresentOrder.Skip(next));
        return result;
    }

    /// <summary>
    /// The device auto-switching picks: the first in <paramref name="order"/> that is ready, or null
    /// when none is.
    /// </summary>
    public static string? Preferred(IEnumerable<string> order, Func<string, bool> isReady) =>
        order.FirstOrDefault(isReady);

    /// <summary>The order after moving one device up (-1) or down (+1); unchanged at either end.</summary>
    public static List<string> Move(IReadOnlyList<string> order, string id, int step)
    {
        var result = order.ToList();
        int from = result.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        int to = from + step;
        if (from < 0 || to < 0 || to >= result.Count)
        {
            return result;
        }

        (result[from], result[to]) = (result[to], result[from]);
        return result;
    }
}
