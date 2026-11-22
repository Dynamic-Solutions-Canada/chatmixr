using System.Collections.Concurrent;
using System.Diagnostics;
using ChatMixr.Models;
using Microsoft.UI.Dispatching;

namespace ChatMixr.Services;

/// <summary>
/// The mixer: on every active playback device, sets each app's volume so chat apps follow the
/// device's chat level and everything else its game level. A device that belongs to a connected
/// Arctis receiver takes its levels from that receiver's ChatMix wheel; every other device takes them
/// from its own slider, whose position is saved per device. With Separate chat on for the device in
/// use, voice chat is routed through a virtual cable (VB-Cable) and played back at the chat level
/// (<see cref="UpdateRouting"/>); a device test plays both sides continuously (<see cref="TestPlayer"/>).
/// <para>
/// All mixing runs on one background audio thread, so Core Audio and the volume bookkeeping are never
/// touched from two threads. Receivers' reader threads, the timer and the interface only queue work
/// for it. A pass every second picks up apps that start playing, volumes the user changes in Windows'
/// mixer, and the default device; every third pass also looks for receivers and devices that came or
/// went. The interface reads <see cref="Snapshot"/> and redraws on <see cref="Changed"/>, raised on
/// the UI thread.
/// </para>
/// A wheel device's volumes are not scaled until its receiver says where the wheel is: asked when the
/// receiver connects and when its headset comes on, or reported when the wheel moves. At start, apps a
/// previous run left turned down are put back to their own volumes first.
/// </summary>
public sealed class MixEngine : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>How often the mix is checked and re-applied.</summary>
    private static readonly TimeSpan PassInterval = TimeSpan.FromSeconds(1);

    /// <summary>Receivers, devices and Sonar are looked for on every this many passes.</summary>
    private const int ScanEveryPasses = 3;

    /// <summary>The longest exit waits for apps to be put back to their own volumes.</summary>
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(3);

    private readonly DispatcherQueue _ui;
    private readonly BaselineFile _baselineFile;
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _audioThread;
    private readonly Timer _timer;

    // Owned by the audio thread.
    private readonly SessionVolumeTracker _tracker;
    private readonly Dictionary<string, Receiver> _receivers = new();
    private readonly HashSet<string> _notChatMixPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, MixLevels?> _wheels = new();

    /// <summary>Each connected receiver's headset: on or off (null until known) and its battery.</summary>
    private readonly Dictionary<Guid, (bool? On, int? Battery)> _headsetState = new();

    /// <summary>Devices hidden from the lists; auto-switching never picks them.</summary>
    private HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    private bool _autoSwitch;
    private readonly Dictionary<string, DeviceState> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _sliderPositions;
    private IReadOnlyList<string> _savedOrder;
    private IReadOnlyList<string> _chatApps;
    private SortedSet<string> _playing = new(StringComparer.OrdinalIgnoreCase);
    private readonly DefaultDevices _defaults;

    /// <summary>Every active recording endpoint, from the latest scan, for each device's own microphone.</summary>
    private IReadOnlyList<OutputInfo> _recording = [];
    private readonly ChatRelay _relay = new();
    private readonly TestPlayer _test = new();
    private bool _separateChat;
    private OutputInfo? _cable;
    private OutputInfo? _cableListenSide;
    private string? _testDeviceId;
    private bool _renameCable;

    /// <summary>When something last played into the cable; the relay records it until a little after.</summary>
    private DateTime _cableHeardAt = DateTime.MinValue;

    /// <summary>
    /// How long the relay keeps recording the cable after it goes quiet: long enough that a pause in a
    /// conversation doesn't stop it (restarting costs the first moment of speech), short enough that
    /// Windows' "microphone in use" indicator doesn't stay on long after a call.
    /// </summary>
    private static readonly TimeSpan CableIdleGrace = TimeSpan.FromSeconds(8);

    /// <summary>How often the cable is checked for something starting to play into it, so speech isn't clipped.</summary>
    private static readonly TimeSpan CableWatchInterval = TimeSpan.FromMilliseconds(250);

    private readonly Timer _cableWatch;
    private MicrophoneChoice? _microphoneFix;
    private bool _sonarRunning;
    private bool _hasScanned;
    private int _passCount;

    private MixSnapshot _snapshot = MixSnapshot.Initial;
    private bool _disposed;

    /// <summary>One active playback device on the audio thread.</summary>
    private sealed class DeviceState(OutputInfo info)
    {
        public OutputInfo Info { get; set; } = info;

        /// <summary>Programs playing sound on it in the latest full pass.</summary>
        public SortedSet<string> Playing { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <param name="baselineFile">Where apps' own volumes are kept between runs.</param>
    /// <param name="chatApps">The chat app list from settings.</param>
    /// <param name="sliderPositions">Each device's saved slider position, by endpoint id.</param>
    /// <param name="deviceOrder">The user's saved device order.</param>
    /// <param name="separateChat">True when in-game voice chat goes through the virtual cable.</param>
    /// <param name="renameCable">True to give the cable's two ends ChatMixr's clearer names.</param>
    /// <param name="autoSwitch">True to switch to a headset when it turns on, and back when it turns off.</param>
    /// <param name="callsDevice">The calls device from settings, or null.</param>
    internal MixEngine(BaselineFile baselineFile, IReadOnlyList<string> chatApps, IReadOnlyDictionary<string, int> sliderPositions, IReadOnlyList<string> deviceOrder, bool separateChat, bool renameCable, bool autoSwitch, string? callsDevice)
    {
        _defaults = new DefaultDevices(callsDevice);
        _autoSwitch = autoSwitch;
        _renameCable = renameCable;
        _separateChat = separateChat;
        _ui = DispatcherQueue.GetForCurrentThread();
        _defaults.CallsChanged += id => _ui.TryEnqueue(() => CallsDeviceChanged?.Invoke(id));
        _baselineFile = baselineFile;
        _chatApps = chatApps.ToList();
        _sliderPositions = new Dictionary<string, int>(sliderPositions, StringComparer.OrdinalIgnoreCase);
        _savedOrder = deviceOrder.ToList();
        _tracker = new SessionVolumeTracker(baselineFile.Load());

        // Core Audio works best from a multithreaded apartment; a new .NET thread is MTA by default.
        _audioThread = new Thread(RunAudioThread) { IsBackground = true, Name = "ChatMixr audio" };
        _timer = new Timer(_ => Post(Pass), null, Timeout.Infinite, Timeout.Infinite);
        _cableWatch = new Timer(_ => Post(WatchCable), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The latest state, for the interface. Read on the UI thread.</summary>
    public MixSnapshot Snapshot => _snapshot;

    /// <summary>Raised on the UI thread when <see cref="Snapshot"/> changes in a way the interface shows.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised on the UI thread when ChatMixr renamed a cable endpoint: its id and the name it had, for
    /// "Restore VB-Cable's names" to put back.
    /// </summary>
    public event Action<string, string>? CableRenamed;

    /// <summary>
    /// Raised on the UI thread when ChatMixr hid one of the cable's extra entrances (VB-Cable's "CABLE In
    /// 16ch"), with its id, so turning the names off can show it again.
    /// </summary>
    public event Action<string>? CableEndpointHidden;

    /// <summary>
    /// Raised on the UI thread with the device's id when its ChatMix wheel turns (not when its position
    /// is read at start), for the mix popup. <see cref="Snapshot"/> already has the new levels.
    /// </summary>
    public event Action<string>? WheelTurned;

    /// <summary>
    /// Raised on the UI thread with the device's id after auto-switching made it the default (a headset
    /// turned on or off), for the switch popup. <see cref="Snapshot"/> already shows it in use.
    /// </summary>
    public event Action<string>? AutoSwitched;

    /// <summary>Raised on the UI thread when the calls device changes (in Windows or in ChatMixr), so settings keep it.</summary>
    public event Action<string>? CallsDeviceChanged;

    /// <summary>Start looking for devices and receivers, and mixing.</summary>
    public void Start()
    {
        _audioThread.Start();

        // Apps a previous run left turned down (a crash, or an update that ended it) go back to their
        // own volumes first; the next pass scales them again from there.
        Post(() =>
        {
            Scan(apply: false);
            RestoreAll();
            ApplyAll();
        });
        _timer.Change(PassInterval, PassInterval);
        _cableWatch.Change(CableWatchInterval, CableWatchInterval);
    }

    // ── Changes from the interface (UI thread) ──────────────────────────
    /// <summary>The chat app list changed in settings; applies at once.</summary>
    public void SetChatApps(IReadOnlyList<string> chatApps)
    {
        var copy = chatApps.ToList();
        Post(() =>
        {
            _chatApps = copy;
            ApplyAll();
        });
    }

    /// <summary>
    /// A device's slider moved; applies to that device at once. The position is kept for wheel devices
    /// too, but their wheel sets the mix while their receiver is connected.
    /// </summary>
    public void SetSliderPosition(string deviceId, int position)
    {
        Post(() =>
        {
            _sliderPositions[deviceId] = Math.Clamp(position, SliderMix.Min, SliderMix.Max);
            if (_devices.TryGetValue(deviceId, out var device))
            {
                Apply(device, null, null);
                SaveBaselinesIfChanged();
                Publish();
            }
        });
    }

    /// <summary>Every slider was reset (Reset settings): all non-wheel devices go back to the middle.</summary>
    public void ResetSliders()
    {
        Post(() =>
        {
            _sliderPositions.Clear();
            ApplyAll();
        });
    }

    /// <summary>The user reordered the devices; the snapshot follows.</summary>
    /// <param name="savedOrder">The new saved order (from <see cref="DeviceOrder.Save"/>).</param>
    public void SetDeviceOrder(IReadOnlyList<string> savedOrder)
    {
        var copy = savedOrder.ToList();
        Post(() =>
        {
            _savedOrder = copy;
            Publish();
        });
    }

    /// <summary>
    /// Turn in-game voice chat through the virtual cable on or off; applies to whichever device is in
    /// use, at once.
    /// </summary>
    public void SetSeparateChat(bool on)
    {
        Post(() =>
        {
            _separateChat = on;
            Publish();
        });
    }

    /// <summary>
    /// Turn ChatMixr's names for the cable on or off. Off puts the remembered original names back
    /// (VB-Cable's own when none were remembered).
    /// </summary>
    /// <param name="originalNames">The names the cable's endpoints had before ChatMixr renamed them, by endpoint id.</param>
    /// <param name="hiddenEndpoints">The cable's extra entrances ChatMixr hid, to show again when off.</param>
    public void SetRenameCable(bool on, IReadOnlyDictionary<string, string> originalNames, IReadOnlyList<string> hiddenEndpoints)
    {
        var originals = new Dictionary<string, string>(originalNames, StringComparer.OrdinalIgnoreCase);
        var hidden = hiddenEndpoints.ToList();
        Post(() =>
        {
            _renameCable = on;
            if (!on)
            {
                ShowHiddenEntrances(hidden);
            }

            if (!on && _cable != null)
            {
                AudioPolicy.Rename(_cable.Id, originals.GetValueOrDefault(_cable.Id, ChatDevicePairing.StockPlaybackName));
                if (_cableListenSide != null)
                {
                    AudioPolicy.Rename(_cableListenSide.Id, originals.GetValueOrDefault(_cableListenSide.Id, ChatDevicePairing.StockRecordingName));
                }

                Logger.Info("Put the cable's own names back");
            }

            Scan(apply: true);
        });
    }

    /// <summary>
    /// Show the cable's hidden entrances again: the ones ChatMixr recorded hiding, and any disabled
    /// virtual endpoint on the cable's own adapter, so one whose record was lost still comes back.
    /// Only the cable's adapter: other disabled devices are the user's own choice.
    /// </summary>
    private void ShowHiddenEntrances(IReadOnlyList<string> recorded)
    {
        var ids = new HashSet<string>(recorded, StringComparer.OrdinalIgnoreCase);
        if (_cable != null)
        {
            foreach (var disabled in AudioSessions.ListDisabledOutputs().Where(o => ChatDevicePairing.IsVirtual(o.ForPairing)
                && string.Equals(o.AdapterName, _cable.AdapterName, StringComparison.OrdinalIgnoreCase)))
            {
                ids.Add(disabled.Id);
            }
        }

        foreach (string id in ids)
        {
            if (AudioPolicy.SetVisible(id, true))
            {
                Logger.Info("Showed {Endpoint} again", id);
            }
        }
    }

    /// <summary>Make a real microphone Windows' default for every role (the fix for the cable being the default).</summary>
    public void UseMicrophone(string recordingId)
    {
        Post(() =>
        {
            if (AudioPolicy.SetAllRoles(recordingId))
            {
                Logger.Info("Made {Microphone} the default microphone", recordingId);
            }

            Scan(apply: true);
        });
    }

    /// <summary>Play the test sounds on a device until <see cref="StopTest"/> (or another device's test).</summary>
    public void StartTest(string deviceId)
    {
        Post(() =>
        {
            _testDeviceId = deviceId;
            Logger.Info("Testing {Device}", _devices.TryGetValue(deviceId, out var d) ? d.Info.Name : deviceId);
            Publish();
        });
    }

    /// <summary>Stop the test sounds.</summary>
    public void StopTest()
    {
        Post(() =>
        {
            _testDeviceId = null;
            Publish();
        });
    }

    /// <summary>Switch to a device (<see cref="DefaultRoles"/> says what moves), then show the change at once.</summary>
    public void SwitchTo(string deviceId, SwitchScope scope) => Post(() => Switch(deviceId, scope));

    /// <summary>Set the device in use's own volume, 0 to 100.</summary>
    public void SetVolume(int percent) => Post(() =>
    {
        if (_defaults.Output != null)
        {
            AudioSessions.SetDeviceVolume(_defaults.Output, percent / 100f);
            Publish();
        }
    });

    /// <summary>Mute or unmute the device in use.</summary>
    public void SetMuted(bool muted) => Post(() =>
    {
        if (_defaults.Output != null)
        {
            AudioSessions.SetDeviceMute(_defaults.Output, muted);
            Publish();
        }
    });

    /// <summary>The device in use's volume, read fresh for each snapshot so changes elsewhere show within a pass.</summary>
    private DeviceVolume? ReadVolume() =>
        _defaults.Output != null && AudioSessions.DeviceVolume(_defaults.Output) is { } volume
            ? new DeviceVolume((int)Math.Round(volume.Volume * 100), volume.Muted)
            : null;

    /// <summary>Turn switching to headsets as they turn on and off on or off.</summary>
    public void SetAutoSwitch(bool on) => Post(() => _autoSwitch = on);

    /// <summary>The devices hidden from the lists, which auto-switching skips.</summary>
    public void SetHiddenDevices(IEnumerable<string> hidden)
    {
        var copy = new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);
        Post(() => _hidden = copy);
    }

    /// <summary>Switch to a device (audio thread), and publish.</summary>
    private void Switch(string deviceId, SwitchScope scope)
    {
        Logger.Info("Switching to {Device} ({Scope})", _devices.TryGetValue(deviceId, out var d) ? d.Info.Name : deviceId, scope);
        _defaults.Switch(deviceId, scope, MicrophoneOf, CableHoldsCalls ? _cable!.Id : null);
        Publish();
    }

    /// <summary>The device-to-microphone pairs last logged, so the log only says when they change.</summary>
    private string _loggedMicrophonePairs = "";

    /// <summary>Log which devices count as having their own microphone (what Switch to may move), when that changes.</summary>
    private void LogMicrophonePairs()
    {
        string pairs = string.Join("; ", _devices.Values
            .Select(d => (d.Info.Name, Microphone: MicrophoneOf(d.Info.Id)))
            .Where(p => p.Microphone != null)
            .Select(p => $"{p.Name} -> {_recording.First(r => r.Id == p.Microphone).Name}"));
        if (pairs != _loggedMicrophonePairs)
        {
            _loggedMicrophonePairs = pairs;
            Logger.Info("Devices with their own microphone: {Pairs}", pairs.Length > 0 ? pairs : "none");
        }
    }

    /// <summary>A listed playback device's own microphone, or null.</summary>
    private string? MicrophoneOf(string deviceId) =>
        _devices.TryGetValue(deviceId, out var device) ? DefaultDevices.MicrophoneOf(device.Info, _recording) : null;

    /// <summary>True while in-game voice chat has Windows' communications default on the cable.</summary>
    private bool CableHoldsCalls => _separateChat && _cable != null && _cableListenSide != null;

    // ── Audio thread ────────────────────────────────────────────────────
    private void RunAudioThread()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // One failed pass (a device vanishing mid-call, say) must not stop mixing.
                Logger.Error(ex, "Mix step failed");
            }
        }
    }

    /// <summary>Queue work for the audio thread; ignored once the engine is shutting down.</summary>
    private void Post(Action action)
    {
        if (!_work.IsAddingCompleted)
        {
            try
            {
                _work.Add(action);
            }
            catch (InvalidOperationException)
            {
                // Completed between the check and the add: shutting down.
            }
        }
    }

    /// <summary>The once-a-second pass: rescan now and then, follow the default device, re-apply, publish.</summary>
    private void Pass()
    {
        _passCount++;
        if (_passCount % ScanEveryPasses == 0)
        {
            Scan(apply: true);
        }
        else
        {
            _defaults.Read(_cable?.Id);
            ApplyAll();
        }
    }

    /// <summary>
    /// Between passes, while in-game voice chat is on and the relay isn't listening: start it as soon
    /// as something plays into the cable, rather than up to a second later, which cut off the first
    /// words. Cheap: one device's sessions, and nothing at all while voice chat is off.
    /// </summary>
    private void WatchCable()
    {
        if (!CableHoldsCalls || _relay.IsListening || !AudioSessions.AnyActiveSession(_cable!.Id))
        {
            return;
        }

        _cableHeardAt = DateTime.UtcNow;
        UpdateRouting();
    }

    /// <summary>
    /// Open receivers that appeared, refresh the list of devices, the default device and Sonar's
    /// state. Then re-apply and publish when <paramref name="apply"/> is set.
    /// </summary>
    private void Scan(bool apply)
    {
        var openPaths = _receivers.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var receiver in Receiver.OpenNew(openPaths, _notChatMixPaths))
        {
            _receivers[receiver.Path] = receiver;
            _wheels.TryAdd(receiver.ContainerId, null);
            receiver.Reported += (r, report) => Post(() => OnReport(r.ContainerId, report));
            receiver.Ended += r => Post(() => OnReceiverEnded(r));
            receiver.Start();
        }

        // The cable Separate chat routes through: the first virtual device ChatMixr can listen to,
        // preferring VB-Cable's plain "CABLE Input" over its 16-channel twin.
        var all = AudioSessions.ListOutputs();
        var recording = AudioSessions.ListRecording();
        _cable = all
            .Where(o => AudioSessions.ListenSideOf(o, recording) != null)
            .OrderByDescending(o => ChatDevicePairing.Preference(o.ForPairing))
            .FirstOrDefault();
        _cableListenSide = _cable != null ? AudioSessions.ListenSideOf(_cable, recording) : null;
        _recording = recording;
        if (_renameCable)
        {
            NameCable();
            HideExtraEntrances(all);
        }

        // Virtual devices (the cable, Sonar's) are routes ChatMixr manages, not things anyone listens
        // to: they are never listed or mixed.
        var present = all.Where(o => !ChatDevicePairing.IsVirtual(o.ForPairing)).ToList();
        foreach (string gone in _devices.Keys.Where(id => !present.Any(o => SameId(o.Id, id))).ToList())
        {
            _devices.Remove(gone);
        }

        foreach (var output in present)
        {
            if (_devices.TryGetValue(output.Id, out var known))
            {
                known.Info = output;
            }
            else
            {
                _devices[output.Id] = new DeviceState(output);
            }
        }

        _defaults.Read(_cable?.Id);
        LogMicrophonePairs();
        _microphoneFix = MicrophoneFix(recording);
        _sonarRunning = IsSonarRunning();
        _hasScanned = true;
        if (apply)
        {
            ApplyAll();
        }
    }

    /// <summary>
    /// Give the cable's two ends ChatMixr's names (what games show, and a warning for the side that
    /// appears among microphones), once, remembering the old names through <see cref="CableRenamed"/>.
    /// </summary>
    private void NameCable()
    {
        _cable = Rename(_cable, ChatDevicePairing.PlaybackName);
        _cableListenSide = Rename(_cableListenSide, ChatDevicePairing.RecordingName);
    }

    /// <summary>
    /// Hide the cable's other playback entrances (VB-Cable's "CABLE In 16ch"): they lead to the same
    /// place, and a second, oddly named device in every list only confuses. Shown again when the names
    /// are turned off (<see cref="SetRenameCable"/>).
    /// </summary>
    private void HideExtraEntrances(IReadOnlyList<OutputInfo> playback)
    {
        if (_cable == null)
        {
            return;
        }

        foreach (var extra in playback.Where(o => !SameId(o.Id, _cable.Id)
            && ChatDevicePairing.IsVirtual(o.ForPairing)
            && string.Equals(o.AdapterName, _cable.AdapterName, StringComparison.OrdinalIgnoreCase)))
        {
            if (AudioPolicy.SetVisible(extra.Id, false))
            {
                Logger.Info("Hid {Endpoint}, another entrance to the cable", extra.Name);
                string id = extra.Id;
                _ui.TryEnqueue(() => CableEndpointHidden?.Invoke(id));
            }
        }
    }

    /// <summary>Rename one endpoint if its name differs; returns it with the new name when that worked.</summary>
    private OutputInfo? Rename(OutputInfo? endpoint, string name)
    {
        if (endpoint == null || string.Equals(endpoint.EndpointName, name, StringComparison.Ordinal))
        {
            return endpoint;
        }

        if (!AudioPolicy.Rename(endpoint.Id, name))
        {
            return endpoint;
        }

        Logger.Info("Renamed {Old} to {New}", endpoint.EndpointName, name);
        string old = endpoint.EndpointName;
        _ui.TryEnqueue(() => CableRenamed?.Invoke(endpoint.Id, old));
        string full = endpoint.AdapterName.Length > 0 ? $"{name} ({endpoint.AdapterName})" : name;
        return endpoint with { EndpointName = name, Name = full };
    }

    /// <summary>
    /// When the cable's recording side is Windows' default microphone (for apps or for calls), the real
    /// microphone to switch to; null when all is well.
    /// </summary>
    private MicrophoneChoice? MicrophoneFix(IReadOnlyList<OutputInfo> recording)
    {
        if (_cableListenSide == null)
        {
            return null;
        }

        bool cableIsMicrophone = SameId(AudioSessions.DefaultRecordingId(AudioSessions.RoleConsole), _cableListenSide.Id)
            || SameId(AudioSessions.DefaultRecordingId(AudioSessions.RoleCommunications), _cableListenSide.Id);
        if (!cableIsMicrophone)
        {
            return null;
        }

        Guid? outputContainer = _defaults.Output != null && _devices.TryGetValue(_defaults.Output, out var output) ? output.Info.ContainerId : null;
        var suggestion = ChatDevicePairing.SuggestedMicrophone(recording.Select(r => r.ForPairing), outputContainer);
        string? name = suggestion == null ? null : recording.First(r => r.Id == suggestion.Id).Name;
        return new MicrophoneChoice(suggestion?.Id, name);
    }

    /// <summary>A message from a receiver: the wheel, power, battery, or the answer to a status request.</summary>
    private void OnReport(Guid containerId, ReceiverReport report)
    {
        var state = _headsetState.GetValueOrDefault(containerId);
        switch (report)
        {
            case ReceiverReport.Wheel wheel:
                if (OnLevels(containerId, wheel.Levels)
                    && _devices.Values.FirstOrDefault(d => d.Info.ContainerId == containerId) is { } turned)
                {
                    // Queued after the snapshot OnLevels published, so the interface sees the new levels.
                    string id = turned.Info.Id;
                    _ui.TryEnqueue(() => WheelTurned?.Invoke(id));
                }

                return;
            case ReceiverReport.Battery battery:
                _headsetState[containerId] = (state.On, battery.Percent);
                break;
            case ReceiverReport.Power power:
                _headsetState[containerId] = (power.On, state.Battery);
                if (power.On && state.On != true)
                {
                    // A headset that just came on: ask for its battery and wheel now rather than
                    // waiting for them to change.
                    RequestStatus(containerId);
                }

                OnPower(containerId, state.On, power.On);
                break;
            case ReceiverReport.Status status:
                // The answer when a receiver connects or its headset comes on: its current state, not a
                // change, so it never switches devices and never shows the mix popup. Levels only count
                // while the headset is on: what a receiver reports for a headset that is off isn't a
                // wheel position, and applying it could turn a device's apps down for no reason.
                _headsetState[containerId] = (status.On, status.BatteryPercent);
                if (status.On)
                {
                    OnLevels(containerId, status.Levels);
                }

                break;
        }

        Publish();
    }

    /// <summary>Ask a receiver's status interface for its headset's state (a no-op for the other interface).</summary>
    private void RequestStatus(Guid containerId)
    {
        foreach (var receiver in _receivers.Values.Where(r => r.ContainerId == containerId))
        {
            receiver.RequestStatus();
        }
    }

    /// <summary>
    /// A headset turned on or off. With auto-switch on, switch to the first device in the user's order
    /// that is ready (<see cref="DeviceOrder.Preferred"/>): a wireless headset while it is on, any other
    /// device always; hidden devices never. A first report (nothing known before) is not a change, and
    /// nothing happens between changes, so a device picked with Use stays picked until the next one.
    /// </summary>
    private void OnPower(Guid containerId, bool? wasOn, bool isOn)
    {
        Logger.Info("Headset {Container} turned {State}", containerId, isOn ? "on" : "off");
        if (!_autoSwitch || wasOn == null || wasOn == isOn)
        {
            return;
        }

        var order = DeviceOrder.Apply(_devices.Values.Select(d => d.Info.Id).ToList(), _savedOrder);
        string? target = DeviceOrder.Preferred(order, id => !_hidden.Contains(id) && IsReady(_devices[id]));
        if (target != null && !SameId(target, _defaults.Output))
        {
            Logger.Info("Auto-switching to {Device}: the first ready device in the user's order", _devices[target].Info.Name);
            Switch(target, SwitchScope.Follow);

            // Queued after the snapshot Switch published, so the popup finds the device in use.
            string id = target;
            _ui.TryEnqueue(() => AutoSwitched?.Invoke(id));
        }
    }

    /// <summary>A device auto-switching may pick: a wireless headset only while on, anything else always.</summary>
    private bool IsReady(DeviceState device) =>
        HeadsetOf(device) is not { } headset || headset.On == true;

    /// <summary>A wheel's position (moved, or read at start); true when it changed.</summary>
    private bool OnLevels(Guid containerId, MixLevels levels)
    {
        if (!_wheels.TryGetValue(containerId, out var old) || old == levels)
        {
            return false;
        }

        _wheels[containerId] = levels;
        if (old == null)
        {
            Logger.Info("Wheel reported for {Container}: game {Game}, chat {Chat}", containerId, levels.Game, levels.Chat);
        }

        foreach (var device in _devices.Values.Where(d => d.Info.ContainerId == containerId))
        {
            Apply(device, null, null);
        }

        SaveBaselinesIfChanged();
        Publish();
        return true;
    }

    private void OnReceiverEnded(Receiver receiver)
    {
        _receivers.Remove(receiver.Path);
        if (!_receivers.Values.Any(r => r.ContainerId == receiver.ContainerId))
        {
            _wheels.Remove(receiver.ContainerId);
            _headsetState.Remove(receiver.ContainerId);
        }

        Publish();
    }

    // ── Mixing ──────────────────────────────────────────────────────────
    /// <summary>True when a connected receiver's wheel sets this device's mix.</summary>
    private bool HasWheel(DeviceState device) =>
        device.Info.ContainerId is Guid container && _wheels.ContainsKey(container);

    /// <summary>A wheel device's headset state, or null for any other device.</summary>
    private HeadsetState? HeadsetOf(DeviceState device) =>
        device.Info.ContainerId is Guid container && _wheels.ContainsKey(container)
            ? new HeadsetState(_headsetState.GetValueOrDefault(container).On, _headsetState.GetValueOrDefault(container).Battery)
            : null;

    /// <summary>The levels a device is mixed at: its wheel's (null until it moves), or its slider's.</summary>
    private MixLevels? LevelsOf(DeviceState device)
    {
        if (device.Info.ContainerId is Guid container && _wheels.TryGetValue(container, out var wheel))
        {
            return wheel;
        }

        return SliderMix.LevelsAt(_sliderPositions.GetValueOrDefault(device.Info.Id));
    }

    /// <summary>Apply every device, and publish.</summary>
    private void ApplyAll()
    {
        var live = new HashSet<string>();
        var playing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in _devices.Values)
        {
            device.Playing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Apply(device, live, playing);
        }

        _tracker.Prune(live);
        _playing = playing;
        SaveBaselinesIfChanged();
        Publish();
    }

    /// <summary>
    /// Set each app's volume on a device from its levels. When given, records the sessions seen in
    /// <paramref name="live"/>, every program with a session in <paramref name="playing"/> (for the chat
    /// app suggestions), and the programs actually playing in the device's own list.
    /// </summary>
    private void Apply(DeviceState device, HashSet<string>? live, SortedSet<string>? playing)
    {
        var levels = LevelsOf(device);
        string outputId = device.Info.Id;
        AudioSessions.ForEachSession(outputId, session =>
        {
            string program = ChatApps.Normalize(session.ProgramName);
            live?.Add(session.SessionId);
            playing?.Add(program);
            if (live != null && session.IsPlaying)
            {
                device.Playing.Add(program);
            }

            if (levels is not MixLevels known)
            {
                return;
            }

            // With the virtual chat device on, voice chat arrives through the cable, so everything playing
            // straight on a device is game sound and the chat app list doesn't apply.
            bool isChat = !_separateChat && ChatApps.IsChat(program, _chatApps);
            float factor = known.FactorFor(isChat);
            string key = SessionVolumeTracker.BaselineKey(outputId, program);
            if (_tracker.Decide(session.SessionId, key, session.Volume, factor) is float volume)
            {
                session.Volume = volume;
            }
        });
    }

    private void SaveBaselinesIfChanged()
    {
        if (_tracker.BaselinesChanged)
        {
            _tracker.BaselinesChanged = false;
            _baselineFile.Save(_tracker.Baselines);
        }
    }

    /// <summary>
    /// Put every app on every device back to its own volume, and forget only those baselines; the
    /// rest are kept for the next start (<see cref="SessionVolumeTracker.ForgetRestored"/>).
    /// </summary>
    private void RestoreAll()
    {
        int restored = 0;
        foreach (string outputId in _devices.Keys)
        {
            AudioSessions.ForEachSession(outputId, session =>
            {
                string key = SessionVolumeTracker.BaselineKey(outputId, session.ProgramName);
                if (_tracker.RestoreTarget(key) is float baseline)
                {
                    session.Volume = baseline;
                    restored++;
                }
            });
        }

        _tracker.ForgetRestored();
        _tracker.ForgetSessions();
        SaveBaselinesIfChanged();
        Logger.Info("Put {Count} app volumes back; {Kept} kept for the next start", restored, _tracker.Baselines.Count);
    }

    private static bool IsSonarRunning()
    {
        var processes = Process.GetProcessesByName("SteelSeriesSonar");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    private static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where in-game voice chat is played while it goes through the cable: the calls device the user
    /// chose (so "speakers for games, headset for calls" survives turning it on), else the device in
    /// use. Null when voice chat isn't going through the cable or there is nowhere to play it.
    /// </summary>
    private DeviceState? ChatListener()
    {
        if (!CableHoldsCalls)
        {
            return null;
        }

        // The calls device, unless it can't play now (a headset that is off): then the device in use.
        if (_defaults.Calls != null && _devices.TryGetValue(_defaults.Calls, out var calls) && IsReady(calls))
        {
            return calls;
        }

        return _defaults.Output != null && _devices.TryGetValue(_defaults.Output, out var inUse) ? inUse : null;
    }

    /// <summary>
    /// In-game voice chat, every pass: while on, the cable is Windows' default communications device
    /// (where voice chat set to "Default" goes) and the cable is played on the <see cref="ChatListener"/>
    /// at its chat level. Otherwise the relay is off and, if the cable was left as the communications
    /// default, calls go back to the calls device the user chose (or the device in use) so they aren't
    /// silent.
    /// </summary>
    private void UpdateRouting()
    {
        string? communications = AudioSessions.DefaultOutputId(AudioSessions.RoleCommunications);
        if (ChatListener() is { } listener)
        {
            if (!SameId(communications, _cable!.Id))
            {
                AudioPolicy.SetRole(_cable.Id, AudioSessions.RoleCommunications);
                Logger.Info("In-game voice chat: calls now go to {Cable}, played on {Device}", _cable.Name, listener.Info.Name);
            }

            // Record the cable only while something plays into it (and a few seconds after, so pauses
            // in a conversation don't stop and start it): any recording shows Windows' "microphone in
            // use" indicator, which must not be on all the time.
            if (AudioSessions.AnyActiveSession(_cable.Id))
            {
                _cableHeardAt = DateTime.UtcNow;
            }

            bool listen = DateTime.UtcNow - _cableHeardAt < CableIdleGrace;
            float gain = LevelsOf(listener) is MixLevels levels ? levels.FactorFor(isChat: true) : 1f;
            _relay.Configure(listen ? _cableListenSide!.Id : null, listen ? listener.Info.Id : null, gain);
            return;
        }

        _relay.Configure(null, null, 1f);
        if (_cable != null && SameId(communications, _cable.Id))
        {
            string? back = _defaults.Calls != null && _devices.ContainsKey(_defaults.Calls) ? _defaults.Calls : _defaults.Output;
            if (back != null && AudioPolicy.SetRole(back, AudioSessions.RoleCommunications))
            {
                Logger.Info("In-game voice chat off: calls go to {Device} again", back);
            }
        }
    }

    /// <summary>
    /// The test sounds, every pass: the game sound on the tested device at its game level, and the
    /// chat sound through the cable when that device plays in-game voice chat (the relay brings it back
    /// at the chat level), otherwise on the device at its chat level. Off when no test runs or the
    /// device is gone.
    /// </summary>
    private void UpdateTest()
    {
        if (_testDeviceId == null || !_devices.TryGetValue(_testDeviceId, out var device))
        {
            _test.Configure(null, null, 1f, 1f);
            return;
        }

        var levels = LevelsOf(device) ?? new MixLevels(100, 100);
        float game = levels.FactorFor(isChat: false);
        float chat = levels.FactorFor(isChat: true);
        if (ChatListener() is { } listener && SameId(listener.Info.Id, device.Info.Id))
        {
            _test.Configure(device.Info.Id, _cable!.Id, game, 1f);
        }
        else
        {
            _test.Configure(device.Info.Id, device.Info.Id, game, chat);
        }
    }

    /// <summary>Build a snapshot, in the user's device order, and hand it to the UI thread if it changed.</summary>
    private void Publish()
    {
        UpdateRouting();
        UpdateTest();
        var windowsOrder = _devices.Values.Select(d => d.Info.Id).ToList();
        var devices = DeviceOrder.Apply(windowsOrder, _savedOrder)
            .Select(id => _devices[id])
            .Select(d => new OutputDevice(
                d.Info.Id,
                d.Info.Name,
                d.Info.Kind,
                SameId(d.Info.Id, _defaults.Output),
                HasWheel(d),
                LevelsOf(d),
                d.Playing.ToList(),
                HeadsetOf(d),
                d.Info.EndpointName,
                d.Info.AdapterName,
                SameId(d.Info.Id, _defaults.Calls),
                MicrophoneOf(d.Info.Id) != null,
                MicrophoneOf(d.Info.Id) is { } microphone && SameId(microphone, _defaults.Microphone)))
            .ToList();
        var snapshot = new MixSnapshot(devices, _sonarRunning, _hasScanned, _playing.ToList(), _cable?.Name, _relay.State, _testDeviceId, _microphoneFix, ReadVolume());

        _ui.TryEnqueue(() =>
        {
            if (_disposed || snapshot.SameAs(_snapshot))
            {
                return;
            }

            _snapshot = snapshot;
            Changed?.Invoke();
        });
    }

    // ── Shutdown ────────────────────────────────────────────────────────
    /// <summary>
    /// Exit step: stop the timer, then on the audio thread put apps back to their own volumes and close
    /// the receivers, waiting up to a few seconds for it. Called on the UI thread.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        _cableWatch.Dispose();

        // Not disposed: if the wait times out, the audio thread may still set it afterwards.
        var finished = new ManualResetEventSlim();
        Post(() =>
        {
            try
            {
                RestoreAll();

                // Voice chat must not be left going to a cable nobody plays once ChatMixr is gone.
                _separateChat = false;
                _testDeviceId = null;
                UpdateRouting();
                UpdateTest();
            }
            finally
            {
                foreach (var receiver in _receivers.Values.ToList())
                {
                    receiver.Dispose();
                }

                finished.Set();
            }
        });
        if (!finished.Wait(RestoreTimeout))
        {
            Logger.Warn("Putting app volumes back took too long; the saved volumes are used on the next start");
        }

        _work.CompleteAdding();
        _relay.Dispose();
        _test.Dispose();
    }
}
