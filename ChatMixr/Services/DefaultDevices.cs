using ChatMixr.Models;

namespace ChatMixr.Services;

/// <summary>
/// Windows' three defaults as a person thinks of them: where games and media play, where calls play,
/// and which microphone apps hear. Reads them from Windows, switches them (<see cref="Switch"/>, by the
/// rules in <see cref="DefaultRoles"/>), and remembers the calls device the user chose while in-game
/// voice chat lends Windows' communications default to the cable. Owned by the mix engine; call it from
/// its audio thread only.
/// </summary>
internal sealed class DefaultDevices
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <param name="savedCalls">The calls device from settings, or null when none was seen yet.</param>
    public DefaultDevices(string? savedCalls)
    {
        Calls = savedCalls;
    }

    /// <summary>The playback device games and media go to, or null.</summary>
    public string? Output { get; private set; }

    /// <summary>
    /// The playback device calls go to as the user chose it, or null. Not always Windows' own
    /// communications default: while in-game voice chat is on, that is the cable.
    /// </summary>
    public string? Calls { get; private set; }

    /// <summary>The default microphone, or null.</summary>
    public string? Microphone { get; private set; }

    /// <summary>Raised (on the audio thread) when the calls device changes, so it can be saved.</summary>
    public event Action<string>? CallsChanged;

    /// <summary>
    /// Read the defaults from Windows. Windows' communications default becomes the calls device unless
    /// it is the cable, which only stands in for it while in-game voice chat is on. Then, if games and
    /// media moved without ChatMixr (Windows' picker, another app), calls follow them as a plain switch
    /// would (<see cref="DefaultRoles.CallsAfterOutputChange"/>), since Windows can't show it then.
    /// </summary>
    /// <param name="cableId">The cable's playback side, or null when there is no cable.</param>
    public void Read(string? cableId)
    {
        Output = AudioSessions.DefaultOutputId();
        Microphone = AudioSessions.DefaultRecordingId(AudioSessions.RoleConsole);

        string? communications = AudioSessions.DefaultOutputId(AudioSessions.RoleCommunications);
        if (communications != null && !Same(communications, cableId))
        {
            RememberCalls(communications);
        }
        else if (DefaultRoles.CallsAfterOutputChange(Calls, _seenOutput, Output) is { } calls)
        {
            RememberCalls(calls);
        }

        _seenOutput = Output;
    }

    /// <summary>
    /// The output as of the last read, or as ChatMixr just set it: a later change from it was made
    /// outside ChatMixr. Null before the first read, which is never taken as a change.
    /// </summary>
    private string? _seenOutput;

    /// <summary>
    /// A playback device's own microphone: the real recording endpoint in the same physical device as a
    /// pair of headphones or a headset (its mic), or null for speakers, a TV, or anything without one.
    /// Never a virtual device.
    /// </summary>
    /// <param name="device">The playback device.</param>
    /// <param name="recording">Every active recording endpoint.</param>
    public static string? MicrophoneOf(OutputInfo device, IReadOnlyList<OutputInfo> recording)
    {
        // Headphones and headsets only: onboard audio and many monitors put their speakers and a mic or
        // line-in in one container, and switching to speakers mustn't take the microphone with it.
        if (device.Kind != OutputKind.Headphones || device.ContainerId is not Guid container || ChatDevicePairing.IsVirtual(device.ForPairing))
        {
            return null;
        }

        return recording.FirstOrDefault(r => r.ContainerId == container && !ChatDevicePairing.IsVirtual(r.ForPairing))?.Id;
    }

    /// <summary>
    /// Switch to a playback device: change what <paramref name="scope"/> and <see cref="DefaultRoles"/>
    /// say, then read the defaults back from Windows.
    /// </summary>
    /// <param name="deviceId">The playback device.</param>
    /// <param name="scope">What to move there.</param>
    /// <param name="microphoneOf">A playback device's own microphone, or null.</param>
    /// <param name="cableId">The cable while it holds Windows' communications default for in-game voice
    /// chat (calls then change only here, and reach Windows when voice chat is turned off); null otherwise.</param>
    public void Switch(string deviceId, SwitchScope scope, Func<string, string?> microphoneOf, string? cableId)
    {
        var plan = DefaultRoles.Plan(scope, deviceId, microphoneOf(deviceId), new Defaults(Output, Calls, Microphone), microphoneOf);
        if (plan.Output != null && AudioPolicy.SetOutput(plan.Output))
        {
            Logger.Info("Games and media now play on {Device} ({Scope})", plan.Output, scope);

            // ChatMixr's own change: the plan already decided calls, so the read below mustn't redo it.
            _seenOutput = plan.Output;
        }

        if (plan.Calls != null)
        {
            RememberCalls(plan.Calls);
            if (cableId == null && AudioPolicy.SetRole(plan.Calls, AudioSessions.RoleCommunications))
            {
                Logger.Info("Calls now play on {Device} ({Scope})", plan.Calls, scope);
            }
        }

        if (plan.Microphone != null && AudioPolicy.SetAllRoles(plan.Microphone))
        {
            Logger.Info("The microphone is now {Microphone} ({Scope})", plan.Microphone, scope);
        }

        Read(cableId);
    }

    private void RememberCalls(string id)
    {
        if (Same(Calls, id))
        {
            return;
        }

        Calls = id;
        CallsChanged?.Invoke(id);
    }

    private static bool Same(string? a, string? b) => a != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
