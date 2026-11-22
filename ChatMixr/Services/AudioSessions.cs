using System.Diagnostics;
using System.Runtime.InteropServices;
using ChatMixr.Models;
using static ChatMixr.Services.CoreAudio;

namespace ChatMixr.Services;

/// <summary>An active playback endpoint.</summary>
/// <param name="Id">Windows' endpoint id, stable while the device stays on the same port.</param>
/// <param name="Name">The name Windows shows, for example "Headphones (Arctis Nova 7X)".</param>
/// <param name="ContainerId">The physical device it belongs to (matches a receiver's HID container), or null.</param>
/// <param name="Kind">What kind of device it is.</param>
/// <param name="EndpointName">The endpoint's own name, for example "Headphones" or "CABLE Input".</param>
/// <param name="AdapterName">The device it belongs to, for example "Arctis Nova 7X".</param>
/// <param name="AdapterDevice">That device's Windows instance id, for example "ROOT\MEDIA\0001"; null when unknown.</param>
public sealed record OutputInfo(string Id, string Name, Guid? ContainerId, OutputKind Kind, string EndpointName, string AdapterName, string? AdapterDevice)
{
    /// <summary>The endpoint as the chat device pairing sees it.</summary>
    public Models.AudioEndpoint ForPairing => new(Id, EndpointName, AdapterName, ContainerId, AdapterDevice);
}

/// <summary>One app's audio on an endpoint, valid only inside <see cref="AudioSessions.ForEachSession"/>.</summary>
public interface IAppSession
{
    /// <summary>Windows' identifier for this session instance.</summary>
    string SessionId { get; }

    /// <summary>The program's name without ".exe", for example "Discord".</summary>
    string ProgramName { get; }

    /// <summary>The session's own volume, 0 to 1.</summary>
    float Volume { get; set; }

    /// <summary>True while the app is playing sound; false for a session it opened and left idle.</summary>
    bool IsPlaying { get; }
}

/// <summary>
/// Lists endpoints and reads and sets app and device volumes through Core Audio. Call it from the mix
/// engine's audio thread, except <see cref="OpenClient"/>, which the relay and test threads also use
/// for their own streams (each call makes its own COM objects). COM objects are released before each
/// call returns.
/// </summary>
internal static class AudioSessions
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Marks ChatMixr's own volume changes, so Windows' volume events can tell them apart.</summary>
    private static Guid _eventContext = new("6f0a8b2c-5a51-4d8e-9a0e-c7e1f63b2d44");

    private static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    /// <summary>ERole eMultimedia: where games and media play.</summary>
    public const int RoleMultimedia = 1;

    /// <summary>ERole eConsole: where games and system sounds play, and the default microphone.</summary>
    public const int RoleConsole = 0;

    /// <summary>ERole eCommunications: where calls and voice chat set to "Default" play.</summary>
    public const int RoleCommunications = 2;

    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    /// <summary>Every active playback endpoint, in Windows' order.</summary>
    public static List<OutputInfo> ListOutputs() => ListEndpoints(EDataFlow.Render);

    /// <summary>
    /// Every disabled playback endpoint: ones hidden in Windows' Sound settings, or by ChatMixr (the
    /// cable's extra entrance), so turning the cable names off can find what to show again.
    /// </summary>
    public static List<OutputInfo> ListDisabledOutputs() => ListEndpoints(EDataFlow.Render, DEVICE_STATE_DISABLED);

    /// <summary>Every active recording endpoint, for <see cref="ListenSideOf"/>.</summary>
    public static List<OutputInfo> ListRecording() => ListEndpoints(EDataFlow.Capture);

    /// <summary>
    /// The recording side of a virtual cable, for example "CABLE Output" for "CABLE Input"
    /// (<see cref="ChatDevicePairing"/>). Null when there is none, or for a real device.
    /// </summary>
    /// <param name="playback">The playback side.</param>
    /// <param name="recording">From <see cref="ListRecording"/>.</param>
    public static OutputInfo? ListenSideOf(OutputInfo playback, IReadOnlyList<OutputInfo> recording)
    {
        var match = ChatDevicePairing.ListenSideOf(playback.ForPairing, recording.Select(r => r.ForPairing));
        return match == null ? null : recording.First(r => r.Id == match.Id);
    }

    /// <summary>Every endpoint in one direction in the given states (active unless said), in Windows' order.</summary>
    private static List<OutputInfo> ListEndpoints(EDataFlow flow, uint states = DEVICE_STATE_ACTIVE)
    {
        var outputs = new List<OutputInfo>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.EnumAudioEndpoints(flow, states, out var collection) != 0)
            {
                return outputs;
            }

            try
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var device) != 0)
                    {
                        continue;
                    }

                    try
                    {
                        if (device.GetId(out string id) == 0)
                        {
                            string name = ReadString(device, PKEY_Device_FriendlyName) ?? id;
                            string endpoint = ReadString(device, PKEY_Device_DeviceDesc) ?? name;
                            string adapter = ReadString(device, PKEY_DeviceInterface_FriendlyName) ?? "";
                            string? adapterDevice = AdapterDeviceOf(device);
                            outputs.Add(new OutputInfo(id, name, ReadGuid(device, PKEY_Device_ContainerId), KindOf(device), endpoint, adapter, adapterDevice));
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }

        return outputs;
    }

    /// <summary>
    /// The device instance id of the endpoint's audio adapter ("ROOT\MEDIA\0001"), without the "{1}."
    /// prefix Windows stores it with; null when the property is missing or not in that shape.
    /// </summary>
    private static string? AdapterDeviceOf(IMMDevice device)
    {
        string? stored = ReadString(device, PKEY_Endpoint_AdapterDevice);
        if (stored == null || !stored.StartsWith('{'))
        {
            return null;
        }

        int prefixEnd = stored.IndexOf("}.", StringComparison.Ordinal);
        return prefixEnd > 0 && prefixEnd + 2 < stored.Length ? stored[(prefixEnd + 2)..] : null;
    }

    /// <summary>The endpoint's form factor, folded into the three kinds the interface draws.</summary>
    private static OutputKind KindOf(IMMDevice device)
    {
        uint? formFactor = ReadProperty(device, PKEY_AudioEndpoint_FormFactor, value => value.vt == VT_UI4 ? (uint?)value.p.ToInt64() : null);
        return formFactor switch
        {
            3 or 5 => OutputKind.Headphones,   // Headphones, Headset
            9 => OutputKind.Display,           // DigitalAudioDisplayDevice
            _ => OutputKind.Speakers,
        };
    }

    /// <summary>
    /// Open an endpoint's audio client, for the chat relay's streams. The caller releases it.
    /// </summary>
    /// <returns>The client, or null when the endpoint is gone.</returns>
    public static IAudioClient? OpenClient(string endpointId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDevice(endpointId, out var device) != 0)
            {
                return null;
            }

            try
            {
                var iid = IID_IAudioClient;
                return device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object client) == 0 ? (IAudioClient)client : null;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>A device's own volume (0 to 1) and mute, or null when it can't be read.</summary>
    public static (float Volume, bool Muted)? DeviceVolume(string endpointId)
    {
        (float, bool)? result = null;
        WithEndpointVolume(endpointId, volume =>
        {
            if (volume.GetMasterVolumeLevelScalar(out float level) == 0 && volume.GetMute(out bool muted) == 0)
            {
                result = (level, muted);
            }
        });
        return result;
    }

    /// <summary>Set a device's own volume (0 to 1), as Windows' volume slider does.</summary>
    public static void SetDeviceVolume(string endpointId, float level)
    {
        var context = _eventContext;
        WithEndpointVolume(endpointId, volume => volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context));
    }

    /// <summary>Mute or unmute a device, as Windows' volume button does.</summary>
    public static void SetDeviceMute(string endpointId, bool muted)
    {
        var context = _eventContext;
        WithEndpointVolume(endpointId, volume => volume.SetMute(muted, ref context));
    }

    /// <summary>Run an action with a device's volume control; nothing happens when the device is gone.</summary>
    private static void WithEndpointVolume(string endpointId, Action<IAudioEndpointVolume> action)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDevice(endpointId, out var device) != 0)
            {
                return;
            }

            try
            {
                var iid = IID_IAudioEndpointVolume;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object volume) != 0)
                {
                    return;
                }

                try
                {
                    action((IAudioEndpointVolume)volume);
                }
                finally
                {
                    Marshal.ReleaseComObject(volume);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// The playback endpoint Windows sends apps to by default for a role (multimedia unless asked,
    /// the one Sound settings shows), or null when there is none.
    /// </summary>
    public static string? DefaultOutputId(int role = RoleMultimedia) => DefaultEndpointId(EDataFlow.Render, role);

    /// <summary>The recording endpoint Windows uses by default for a role, or null when there is none.</summary>
    public static string? DefaultRecordingId(int role) => DefaultEndpointId(EDataFlow.Capture, role);

    /// <summary>The default endpoint in one direction for a role, or null.</summary>
    private static string? DefaultEndpointId(EDataFlow flow, int role)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(flow, role, out var device) != 0)
            {
                return null;
            }

            try
            {
                return device.GetId(out string id) == 0 ? id : null;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Visit every app session on an endpoint, leaving out Windows' system sounds and ChatMixr's own.
    /// A session whose program can't be identified (it exited a moment ago) is skipped.
    /// </summary>
    /// <param name="endpointId">From <see cref="OutputInfo.Id"/>.</param>
    /// <param name="visit">Called once per session; the session is only valid during the call.</param>
    public static void ForEachSession(string endpointId, Action<IAppSession> visit)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDevice(endpointId, out var device) != 0)
            {
                return;
            }

            try
            {
                VisitSessions(device, visit);
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// True when any program (ChatMixr's own test sound included) has a running stream on the endpoint.
    /// The chat relay records the cable only then, since any recording shows Windows' "microphone in
    /// use" indicator.
    /// </summary>
    public static bool AnyActiveSession(string endpointId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDevice(endpointId, out var device) != 0)
            {
                return false;
            }

            try
            {
                var iid = IID_IAudioSessionManager2;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object managerObject) != 0)
                {
                    return false;
                }

                var manager = (IAudioSessionManager2)managerObject;
                try
                {
                    if (manager.GetSessionEnumerator(out var sessions) != 0)
                    {
                        return false;
                    }

                    try
                    {
                        sessions.GetCount(out int count);
                        for (int i = 0; i < count; i++)
                        {
                            if (sessions.GetSession(i, out var control) != 0)
                            {
                                continue;
                            }

                            try
                            {
                                if (control.GetState(out int state) == 0 && state == 1)
                                {
                                    return true;
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(control);
                            }
                        }

                        return false;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sessions);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(manager);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static void VisitSessions(IMMDevice device, Action<IAppSession> visit)
    {
        var iid = IID_IAudioSessionManager2;
        if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object managerObject) != 0)
        {
            return;
        }

        var manager = (IAudioSessionManager2)managerObject;
        try
        {
            if (manager.GetSessionEnumerator(out var sessions) != 0)
            {
                return;
            }

            try
            {
                sessions.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var control) != 0)
                    {
                        continue;
                    }

                    try
                    {
                        if (AppSession.TryCreate(control) is { } session)
                        {
                            visit(session);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(control);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sessions);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }
    }

    /// <summary>A session's identity and volume control, read once when the visit starts.</summary>
    private sealed class AppSession : IAppSession
    {
        /// <summary>AudioSessionStateActive: the app has a stream running.</summary>
        private const int AudioSessionStateActive = 1;

        private readonly ISimpleAudioVolume _volume;

        private AppSession(string sessionId, string programName, ISimpleAudioVolume volume, bool isPlaying)
        {
            SessionId = sessionId;
            ProgramName = programName;
            _volume = volume;
            IsPlaying = isPlaying;
        }

        public bool IsPlaying { get; }

        public string SessionId { get; }

        public string ProgramName { get; }

        public float Volume
        {
            get => _volume.GetMasterVolume(out float level) == 0 ? level : 1f;
            set
            {
                int hr = _volume.SetMasterVolume(Math.Clamp(value, 0f, 1f), ref _eventContext);
                if (hr != 0)
                {
                    Logger.Warn("Setting {Program}'s volume failed: 0x{Result:X8}", ProgramName, hr);
                }
            }
        }

        /// <summary>
        /// Wrap a session, or return null for system sounds, ChatMixr itself, or a program that is gone.
        /// The volume interface is the same COM object as the control, so it shares its lifetime.
        /// </summary>
        public static AppSession? TryCreate(IAudioSessionControl2 control)
        {
            // S_OK (0) means it is the system sounds session; S_FALSE (1) means it is not.
            if (control.IsSystemSoundsSession() == 0)
            {
                return null;
            }

            if (control.GetProcessId(out uint processId) != 0 || processId == 0 || processId == OwnProcessId)
            {
                return null;
            }

            if (control.GetSessionInstanceIdentifier(out string sessionId) != 0 || ProgramNameOf(processId) is not string program)
            {
                return null;
            }

            bool playing = control.GetState(out int state) == 0 && state == AudioSessionStateActive;
            return new AppSession(sessionId, program, (ISimpleAudioVolume)control, playing);
        }
    }

    /// <summary>The program name of a running process, or null if it has exited.</summary>
    private static string? ProgramNameOf(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Guid? ReadGuid(IMMDevice device, PROPERTYKEY key) =>
        ReadProperty(device, key, value => value.vt == VT_CLSID ? Marshal.PtrToStructure<Guid>(value.p) : (Guid?)null);

    private static string? ReadString(IMMDevice device, PROPERTYKEY key) =>
        ReadProperty(device, key, value => value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.p) : null);

    /// <summary>Read one property of an endpoint and convert it while the value is still valid.</summary>
    private static T? ReadProperty<T>(IMMDevice device, PROPERTYKEY key, Func<PROPVARIANT, T?> convert)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0)
        {
            return default;
        }

        try
        {
            if (store.GetValue(ref key, out var value) != 0)
            {
                return default;
            }

            try
            {
                return convert(value);
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }
}
