namespace ChatMixr.Models;

/// <summary>An audio endpoint as the chat device pairing sees it.</summary>
/// <param name="Id">Windows' endpoint id.</param>
/// <param name="EndpointName">The endpoint's own name, for example "CABLE Input".</param>
/// <param name="AdapterName">The device it belongs to, for example "VB-Audio Virtual Cable".</param>
/// <param name="ContainerId">Its physical device, or null.</param>
public sealed record AudioEndpoint(string Id, string EndpointName, string AdapterName, Guid? ContainerId);

/// <summary>
/// Finds the recording side of a virtual cable, so the chat relay can listen to what a game sends
/// to its playback side. Only virtual devices qualify: a real headset's playback side would pair
/// with its own microphone. Windows gives every software device the same "no physical device"
/// container, so the container alone can't pair them; the names do.
/// </summary>
public static class ChatDevicePairing
{
    /// <summary>What ChatMixr names the cable's playback side: the device games and Discord send voice chat to.</summary>
    public const string PlaybackName = "ChatMixr Voice Chat";

    /// <summary>
    /// What ChatMixr names the cable's recording side. It shows in every microphone list but is
    /// ChatMixr's own return path, so the name tells people not to pick it.
    /// </summary>
    public const string RecordingName = "ChatMixr internal, do not choose";

    /// <summary>VB-Cable's own names, put back by "Restore VB-Cable's names" when nothing better is remembered.</summary>
    public const string StockPlaybackName = "CABLE Input", StockRecordingName = "CABLE Output";

    /// <summary>The container Windows gives devices with no physical part (VB-Cable, Voicemeeter, Sonar).</summary>
    public static readonly Guid SoftwareContainer = new("00000000-0000-0000-ffff-ffffffffffff");

    /// <summary>True when a playback endpoint is a software device, so it can be offered as the chat device.</summary>
    public static bool IsVirtual(AudioEndpoint playback) => playback.ContainerId == SoftwareContainer;

    /// <summary>
    /// The recording endpoint to listen to for a virtual playback endpoint: the one in the same device
    /// whose name is the playback name with "Input" turned into "Output" ("CABLE Input" and "CABLE
    /// Output"), else the one ChatMixr named, else the device's only recording endpoint. Null when it isn't virtual or the choice
    /// is ambiguous (Sonar's chat device sits beside two microphones), so nothing wrong is ever played.
    /// </summary>
    public static AudioEndpoint? ListenSideOf(AudioEndpoint playback, IEnumerable<AudioEndpoint> recording)
    {
        if (!IsVirtual(playback))
        {
            return null;
        }

        var sameDevice = recording
            .Where(r => r.ContainerId == playback.ContainerId
                && string.Equals(r.AdapterName, playback.AdapterName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        string expected = playback.EndpointName.Replace("Input", "Output", StringComparison.OrdinalIgnoreCase);
        var named = sameDevice.FirstOrDefault(r => string.Equals(r.EndpointName, expected, StringComparison.OrdinalIgnoreCase));
        if (named != null && !string.Equals(expected, playback.EndpointName, StringComparison.OrdinalIgnoreCase))
        {
            return named;
        }

        var ours = sameDevice.FirstOrDefault(r => string.Equals(r.EndpointName, RecordingName, StringComparison.OrdinalIgnoreCase));
        if (ours != null)
        {
            return ours;
        }

        return sameDevice.Count == 1 ? sameDevice[0] : null;
    }

    /// <summary>
    /// How much a virtual playback endpoint is preferred as the cable: the one ChatMixr already named
    /// first, then VB-Cable's plain "CABLE Input", then anything else (its 16-channel twin).
    /// </summary>
    public static int Preference(AudioEndpoint playback)
    {
        if (string.Equals(playback.EndpointName, PlaybackName, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return string.Equals(playback.EndpointName, StockPlaybackName, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    /// <summary>
    /// The microphone to suggest instead of the cable: the real recording device in the same physical
    /// device as the output in use (a headset's own microphone), else the first real one. Null when
    /// there is none.
    /// </summary>
    public static AudioEndpoint? SuggestedMicrophone(IEnumerable<AudioEndpoint> recording, Guid? outputContainer)
    {
        var real = recording.Where(r => r.ContainerId != SoftwareContainer).ToList();
        return real.FirstOrDefault(r => outputContainer != null && r.ContainerId == outputContainer) ?? real.FirstOrDefault();
    }
}
