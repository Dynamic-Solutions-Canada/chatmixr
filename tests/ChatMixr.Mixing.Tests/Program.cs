// Portable harness for the mixing rules: the wheel report as the receivers send it, the volume
// curve, which apps count as chat, and how each app's volume is worked out from the wheel without
// losing the volume the user chose for it. Exits non-zero on the first failure.
using ChatMixr.Models;

var scenarios = new List<(string Name, Action Body)>
{
    ("a ChatMix report from the Nova 7X receiver gives its game and chat levels", ParsesCapturedReports),
    ("other reports on the same interface are ignored", IgnoresOtherReports),
    ("power, battery and status messages captured from both receivers parse", ReceiverMessages),
    ("levels above 100 are capped, so a bad report can't raise an app above its own volume", CapsLevels),
    ("the curve is 0 at 0, 1 at 100, and quieter than linear in between", CurveShape),
    ("the slider is 100 and 100 in the middle and lowers the other side toward each end", SliderLevels),
    ("a wheel's levels show at the matching slider position", SliderShowsWheel),
    ("near the middle the slider snaps to it, like a detent", SliderDetent),
    ("a key press always leaves the middle, and stops there on the way through", SliderKeys),
    ("a plain switch brings calls and the headset's mic along only when they were with the old device", SwitchFollows),
    ("a split setup stays split, and a desk microphone stays put", SwitchKeepsSplit),
    ("the switch menu moves one default at a time, and nothing already there", SwitchSingleRoles),
    ("calls follow an output change made outside ChatMixr only when they were with it", CallsFollowOutsideChange),
    ("a device's calls and microphone roles count as a change to redraw", SnapshotSeesRoles),
    ("a recorded shortcut needs a modifier and a real key, and must differ from the other", ShortcutRules),
    ("shortcuts read in Windows' order", ShortcutNames),
    ("a shortcut press moves a step, stopping at the middle and the ends", ShortcutSteps),
    ("devices follow the saved order, with new ones after it in Windows' order", OrderApplies),
    ("reordering keeps the place of devices that aren't connected", OrderSaveKeepsAbsent),
    ("auto-switching picks the first ready device in the user's order", PreferredDevice),
    ("moving a device up or down swaps it with its neighbour, and stops at the ends", OrderMove),
    ("a Squirrel shortcut's program is the one it starts, not Update.exe", ShortcutProgram),
    ("the search ranks name starts first, leaves out chat apps, and matches program names", SearchRanking),
    ("removed presets are offered back, with their names", MissingPresets),
    ("newly heard programs go to the front of the recent list, which is capped", RecentPrograms),
    ("the relay buffer keeps whole frames in order, across the wrap", RelayBufferOrder),
    ("the relay buffer drops the oldest audio when the output falls behind", RelayBufferOverflow),
    ("drift correction skips quiet excess at once, and loud excess only past the ceiling", RelayBufferTrim),
    ("silence is told apart from quiet speech in float and 16-bit audio", RelaySilenceLevels),
    ("VB-Cable's input pairs with its output; a headset never pairs with its microphone", CablePairing),
    ("the two test sounds play continuously, stay within range, and differ", TestSounds),
    ("after ChatMixr renames the cable, its two ends still pair, and it is preferred", RenamedCablePairing),
    ("the suggested microphone is the headset's own, else the first real one", MicrophoneSuggestion),
    ("chat apps match by program name, with or without .exe and in any case", ChatAppMatching),
    ("an app's first volume is its baseline, and the wheel scales it", FirstSightScales),
    ("moving the wheel back to the middle restores the app's own volume", WheelBackRestores),
    ("a volume the user changes in Windows' mixer becomes the new baseline", UserChangeRebases),
    ("a user change while the wheel has the app at zero is left alone", UserChangeAtZeroLeftAlone),
    ("a baseline saved by a run that crashed wins over the scaled volume found at start", SavedBaselineWins),
    ("a new session of a known program starts from its existing baseline", SecondSessionSharesBaseline),
    ("after restoring, only restored baselines are forgotten; a closed app's baseline is kept", ForgetRestored),
};

foreach (var (name, body) in scenarios)
{
    try
    {
        body();
    }
    catch (Exception ex)
    {
        Console.WriteLine("FAIL: " + name);
        Console.WriteLine("  " + ex.Message);
        return 1;
    }

    Console.WriteLine("PASS: " + name);
}

Console.WriteLine($"All {scenarios.Count} scenarios passed.");
return 0;

// ── Wheel reports ───────────────────────────────────────────────────────

void ParsesCapturedReports()
{
    // Captured from a Nova 7X receiver (PID 22A5) on 2026-11-21: wheel turned toward chat, then game.
    Check(Wheel(Bytes("00-45-37-64-00-00-00-00")) == new MixLevels(55, 100), "game 0x37 = 55, chat 100");
    Check(Wheel(Bytes("00-45-64-2D-00-00-00-00")) == new MixLevels(100, 45), "game 100, chat 0x2D = 45");
    Check(Wheel(Bytes("00-45-64-64-00-00-00-00")) == new MixLevels(100, 100), "the middle is 100 and 100");
}

void IgnoresOtherReports()
{
    // 0xB7 arrived on the same interface during the capture (a status report).
    Check(Wheel(Bytes("00-B7-43-00-00-00-00-00")) == null, "a 0xB7 report is not a wheel report");
    Check(Wheel(Bytes("01-45-64-64")) == null, "a different report ID is not a wheel report");
    Check(Wheel(Bytes("00-45-64")) == null, "a short report is ignored");
}

void ReceiverMessages()
{
    // Captured on 2026-11-21: the Gen 2 turned on then off, battery reports, and status answers.
    Check(ReceiverReport.Parse(Bytes("00-B9-03-00")) == new ReceiverReport.Power(true), "B9 03 is on");
    Check(ReceiverReport.Parse(Bytes("00-B9-02-00")) == new ReceiverReport.Power(false), "B9 02 is off");
    Check(ReceiverReport.Parse(Bytes("00-B7-43-00")) == new ReceiverReport.Battery(67), "B7 0x43 is 67%");
    Check(ReceiverReport.Parse(Bytes("00-B0-03-4F-03-64-64-00-00-00")) == new ReceiverReport.Status(true, 79, new MixLevels(100, 100)), "the Gen 2's status while on");
    Check(ReceiverReport.Parse(Bytes("00-B0-02-40-00-64-64-00-00-01")) == new ReceiverReport.Status(false, 64, new MixLevels(100, 100)), "the 7X's status while off");
    Check(ReceiverReport.Parse(Bytes("00-45-37-64")) == new ReceiverReport.Wheel(new MixLevels(55, 100)), "the wheel still parses");
    Check(ReceiverReport.Parse(Bytes("00-B9-07-00")) == null && ReceiverReport.Parse(Bytes("00-C1-01")) == null, "unknown values and opcodes are ignored");
    Check(ReceiverReport.StatusRequest(65) is { Length: 65 } r && r[0] == 0 && r[1] == 0xB0, "the status request is report 0, opcode B0, padded");
}

void CapsLevels()
{
    Check(Wheel(Bytes("00-45-FF-C8")) == new MixLevels(100, 100), "255 and 200 become 100");
}

// ── Curve and chat apps ─────────────────────────────────────────────────

void CurveShape()
{
    Check(MixCurve.Factor(0) == 0f && MixCurve.Factor(100) == 1f, "the ends are 0 and 1");
    Check(MixCurve.Factor(50) < 0.5f && MixCurve.Factor(50) > 0f, "the middle of the travel is quieter than half");
    Check(MixCurve.Factor(-5) == 0f && MixCurve.Factor(140) == 1f, "out-of-range levels are clamped");
    Check(new MixLevels(100, 50).FactorFor(isChat: false) == 1f, "the game side uses the game level");
}

void SliderLevels()
{
    Check(SliderMix.LevelsAt(0) == new MixLevels(100, 100), "the middle leaves both sides alone");
    Check(SliderMix.LevelsAt(100) == new MixLevels(0, 100), "all the way toward chat silences the game side");
    Check(SliderMix.LevelsAt(-40) == new MixLevels(100, 60), "toward game lowers chat only");
    Check(SliderMix.LevelsAt(500) == new MixLevels(0, 100), "out-of-range positions are clamped");
}

void SliderDetent()
{
    Check(SliderMix.Snap(3) == 0 && SliderMix.Snap(-5) == 0, "within the detent snaps to the middle");
    Check(SliderMix.Snap(6) == 6 && SliderMix.Snap(-40) == -40, "outside it stays put");
    Check(SliderMix.Snap(250) == SliderMix.Max, "out of range is clamped");
}

void SliderKeys()
{
    Check(SliderMix.KeyMove(0, 10) == 10 && SliderMix.KeyMove(0, -10) == -10, "a 10-step press leaves the middle (the detent would snap it back)");
    Check(SliderMix.KeyMove(-5, 5) == 0 && SliderMix.KeyMove(30, -20) == 0, "passing through the middle stops there");
    Check(SliderMix.KeyMove(90, 140) == SliderMix.Max, "out of range is clamped");
}

// Ids for the switch scenarios: a headset with its own mic, speakers with none, a desk microphone.
const string HeadsetA = "headset-a", HeadsetB = "headset-b", Speakers = "speakers";
const string MicA = "mic-a", MicB = "mic-b", DeskMic = "desk-mic";
string? MicOf(string device) => device switch { HeadsetA => MicA, HeadsetB => MicB, _ => null };

void SwitchFollows()
{
    var together = new Defaults(HeadsetA, HeadsetA, MicA);
    var plan = DefaultRoles.Plan(SwitchScope.Follow, HeadsetB, MicB, together, MicOf);
    Check(plan == new SwitchPlan(HeadsetB, HeadsetB, MicB), "a headset used for everything hands everything to the next one");

    var fresh = new Defaults(Speakers, null, null);
    plan = DefaultRoles.Plan(SwitchScope.Follow, HeadsetB, MicB, fresh, MicOf);
    Check(plan == new SwitchPlan(HeadsetB, HeadsetB, MicB), "with no calls device or microphone yet, both come along");

    plan = DefaultRoles.Plan(SwitchScope.Follow, Speakers, null, together, MicOf);
    Check(plan == new SwitchPlan(Speakers, Speakers, null), "speakers take calls too, but have no mic to take, so the headset's stays");
}

void SwitchKeepsSplit()
{
    var split = new Defaults(Speakers, HeadsetA, MicA);
    var plan = DefaultRoles.Plan(SwitchScope.Follow, HeadsetB, MicB, split, MicOf);
    Check(plan == new SwitchPlan(HeadsetB, null, null), "speakers for games, headset for calls: only games move");

    var desk = new Defaults(HeadsetA, HeadsetA, DeskMic);
    plan = DefaultRoles.Plan(SwitchScope.Follow, HeadsetB, MicB, desk, MicOf);
    Check(plan == new SwitchPlan(HeadsetB, HeadsetB, null), "a desk microphone isn't the old headset's, so it stays");
}

void SwitchSingleRoles()
{
    var together = new Defaults(HeadsetA, HeadsetA, MicA);
    Check(DefaultRoles.Plan(SwitchScope.Output, HeadsetB, MicB, together, MicOf) == new SwitchPlan(HeadsetB, null, null), "games and media only");
    Check(DefaultRoles.Plan(SwitchScope.Calls, HeadsetB, MicB, together, MicOf) == new SwitchPlan(null, HeadsetB, null), "calls only");
    Check(DefaultRoles.Plan(SwitchScope.Microphone, HeadsetB, MicB, together, MicOf) == new SwitchPlan(null, null, MicB), "microphone only");
    Check(DefaultRoles.Plan(SwitchScope.Microphone, Speakers, null, together, MicOf).IsEmpty, "speakers have no microphone to move");
    Check(DefaultRoles.Plan(SwitchScope.All, HeadsetA, MicA, new Defaults(HeadsetA, Speakers, DeskMic), MicOf) == new SwitchPlan(null, HeadsetA, MicA),
        "everything on the device in use moves only what isn't there yet");
}

void CallsFollowOutsideChange()
{
    Check(DefaultRoles.CallsAfterOutputChange(HeadsetA, HeadsetA, HeadsetB) == HeadsetB, "calls with the old output move to the new one");
    Check(DefaultRoles.CallsAfterOutputChange(HeadsetA, Speakers, HeadsetB) == HeadsetA, "calls kept apart stay put");
    Check(DefaultRoles.CallsAfterOutputChange(null, Speakers, HeadsetB) == HeadsetB, "no calls device yet: they go with the output");
    Check(DefaultRoles.CallsAfterOutputChange(HeadsetA, HeadsetA, HeadsetA) == HeadsetA, "no change, nothing moves");
    Check(DefaultRoles.CallsAfterOutputChange(HeadsetA, HeadsetA, null) == HeadsetA, "no output at all leaves calls alone");
}

void SnapshotSeesRoles()
{
    var device = new OutputDevice("id", "Headphones (Arctis)", OutputKind.Headphones, false, false, null, [], null, "Headphones", "Arctis");
    Check(device.SameAs(device with { Playing = new List<string>() }), "an equal copy is the same");
    Check(!device.SameAs(device with { IsCalls = true }), "gaining calls redraws");
    Check(!device.SameAs(device with { HasMicrophone = true }), "a microphone appearing redraws");
    Check(!device.SameAs(device with { IsMicrophone = true }), "its mic becoming the default redraws");
}

void ShortcutRules()
{
    var chat = Shortcut.DefaultTowardChat;
    Check(Shortcut.Problem(new Shortcut(0, 0x41), null) == ShortcutProblem.NoModifier, "a plain A is refused");
    Check(Shortcut.Problem(new Shortcut(Shortcut.Control, 0x11), null) == ShortcutProblem.NoKey, "Ctrl alone is refused");
    Check(Shortcut.Problem(chat, chat) == ShortcutProblem.SameAsOther, "the same as the other shortcut is refused");
    Check(Shortcut.Problem(new Shortcut(Shortcut.Alt, 0x75), chat) == null, "Alt+F6 is fine");
}

void ShortcutNames()
{
    var all = new Shortcut(Shortcut.Shift | Shortcut.Alt | Shortcut.Control | Shortcut.Win, 0x41);
    Check(all.Describe(_ => "A") == "Win+Ctrl+Alt+Shift+A", "modifiers in Windows' order");
    Check(Shortcut.DefaultTowardChat.Describe(_ => "=") == "Ctrl+Shift+=", "the default reads as the keyboard names its key");
}

void ShortcutSteps()
{
    Check(SliderMix.Step(0, +1) == 10 && SliderMix.Step(0, -1) == -10, "one step each way from the middle");
    Check(SliderMix.Step(-5, +1) == 0 && SliderMix.Step(4, -1) == 0, "passing through the middle stops at it");
    Check(SliderMix.Step(15, -1) == 0 && SliderMix.Step(16, -1) == 6, "a step ending within the detent lands on the middle");
    Check(SliderMix.Step(95, +1) == SliderMix.Max && SliderMix.Step(-100, -1) == SliderMix.Min, "the ends stop it");
}

void SliderShowsWheel()
{
    Check(SliderMix.PositionOf(new MixLevels(100, 100)) == 0, "the middle is 0");
    Check(SliderMix.PositionOf(new MixLevels(66, 100)) == 34, "game 66 is 34 toward chat");
    Check(SliderMix.PositionOf(new MixLevels(100, 45)) == -55, "chat 45 is 55 toward game");
    foreach (int p in new[] { -100, -37, 0, 12, 100 })
    {
        Check(SliderMix.PositionOf(SliderMix.LevelsAt(p)) == p, $"position {p} survives the round trip");
    }
}

void OrderApplies()
{
    var order = DeviceOrder.Apply(["a", "b", "c", "d"], ["c", "gone", "a"]);
    Check(order.SequenceEqual(["c", "a", "b", "d"]), "saved first, then new ones in Windows' order; absent ones skipped");
}

void OrderSaveKeepsAbsent()
{
    // Saved: tv (not connected now), soundbar, headset. The user drags headset above soundbar.
    var saved = DeviceOrder.Save(["headset", "soundbar"], ["tv", "soundbar", "headset"]);
    Check(saved.SequenceEqual(["tv", "headset", "soundbar"]), "the TV keeps its first place for when it returns");

    var fresh = DeviceOrder.Save(["b", "a", "new"], ["a", "b"]);
    Check(fresh.SequenceEqual(["b", "a", "new"]), "a device seen for the first time is added at the end");
}

void PreferredDevice()
{
    string[] order = ["gen2", "7x", "soundbar", "tv"];
    var on = new HashSet<string> { "soundbar", "tv" };
    Check(DeviceOrder.Preferred(order, on.Contains) == "soundbar", "both headsets off: the soundbar");
    on.Add("7x");
    Check(DeviceOrder.Preferred(order, on.Contains) == "7x", "the 7X turns on and is higher: the 7X");
    on.Add("gen2");
    Check(DeviceOrder.Preferred(order, on.Contains) == "gen2", "the Gen 2 is first: the Gen 2");
    Check(DeviceOrder.Preferred(order, _ => false) == null, "nothing ready: no switch");
}

void OrderMove()
{
    string[] order = ["a", "b", "c"];
    Check(DeviceOrder.Move(order, "b", -1).SequenceEqual(["b", "a", "c"]), "up swaps with the one above");
    Check(DeviceOrder.Move(order, "b", 1).SequenceEqual(["a", "c", "b"]), "down swaps with the one below");
    Check(DeviceOrder.Move(order, "a", -1).SequenceEqual(order), "the first can't go up");
}

void ShortcutProgram()
{
    Check(AppSearch.ProgramOf(@"C:\Users\me\AppData\Local\Discord\Update.exe", "--processStart Discord.exe") == "Discord", "Discord's shortcut starts Discord");
    Check(AppSearch.ProgramOf(@"C:\x\Update.exe", "--processStart \"Slack App.exe\" --flag") == "Slack App", "a quoted name with a space");
    Check(AppSearch.ProgramOf(@"C:\Program Files\Mumble\client\mumble.exe", null) == "mumble", "a plain program");
    Check(AppSearch.ProgramOf(@"C:\docs\readme.txt", null) == null, "a document is not a program");
    Check(AppSearch.IsNotAnApp("Uninstall Discord", "Update") && AppSearch.IsNotAnApp("Zoom", "unins000"), "uninstallers are left out");
}

void SearchRanking()
{
    InstalledApp[] apps =
    [
        new("Microsoft Teams", "ms-teams"),
        new("TeamSpeak 3 Client", "ts3client_win64"),
        new("Steam", "steam"),
        new("Discord", "Discord"),
        new("Teamviewer", "TeamViewer"),
    ];

    var team = AppSearch.Find(apps, "team", []);
    Check(team.Select(a => a.Name).SequenceEqual(["TeamSpeak 3 Client", "Teamviewer", "Microsoft Teams", "Steam"]), "name starts, then word starts, then anywhere");
    Check(AppSearch.Find(apps, "ts3", []).Single().Program == "ts3client_win64", "program names match too");
    Check(AppSearch.Find(apps, "disc", ["Discord"]).Count == 0, "apps already on the list are left out");
    Check(AppSearch.Find(apps, "  ", []).Count == 0, "nothing typed, nothing found");
}

void MissingPresets()
{
    var missing = ChatApps.MissingPresets(ChatApps.Defaults.Where(p => p != "Discord" && p != "Zoom"));
    Check(missing.Select(p => p.Name).SequenceEqual(["Discord", "Zoom"]), "the two removed presets, in preset order");
    Check(ChatApps.PresetName("discord.exe") == "Discord" && ChatApps.PresetName("notepad") == null, "preset names by program");
}

void RecentPrograms()
{
    var recent = ChatApps.Remember(["chrome", "steam"], ["Discord.exe", "chrome"]);
    Check(recent!.SequenceEqual(["Discord", "chrome", "steam"]), "Discord is new and goes first; chrome keeps its place");
    Check(ChatApps.Remember(["chrome"], ["Chrome"]) == null, "nothing new, nothing to save");

    var many = Enumerable.Range(0, 40).Select(i => "app" + i).ToList();
    Check(ChatApps.Remember([], many)!.Count == ChatApps.RecentLimit, "the list is capped");
}

void RelayBufferOrder()
{
    var buffer = new RelayBuffer(frameBytes: 2, maxFrames: 4);
    buffer.Write([1, 1, 2, 2, 3]);   // a trailing half frame is ignored
    var out1 = new byte[2];
    Check(buffer.Read(out1) == 2 && out1.SequenceEqual(new byte[] { 1, 1 }), "oldest frame first");
    buffer.Write([3, 3, 4, 4, 5, 5]);   // wraps around the end
    var out2 = new byte[8];
    int read = buffer.Read(out2);
    Check(read == 8 && out2.SequenceEqual(new byte[] { 2, 2, 3, 3, 4, 4, 5, 5 }), "frames come out in order across the wrap");
    Check(buffer.Frames == 0 && buffer.Read(new byte[4]) == 0, "empty reads nothing; the caller plays silence");
}

void RelayBufferOverflow()
{
    var buffer = new RelayBuffer(frameBytes: 1, maxFrames: 3);
    buffer.Write([1, 2, 3]);
    buffer.Write([4, 5]);
    var all = new byte[3];
    buffer.Read(all);
    Check(all.SequenceEqual(new byte[] { 3, 4, 5 }) && buffer.DroppedFrames == 2, "the two oldest frames were dropped");
    buffer.Write([6, 7, 8, 9, 10]);
    buffer.Read(all);
    Check(all.SequenceEqual(new byte[] { 8, 9, 10 }), "a write bigger than the buffer keeps its newest part");
}

void RelayBufferTrim()
{
    var silence = new RelaySilence(16, isFloat: false);
    var buffer = new RelayBuffer(frameBytes: 2, maxFrames: 100);
    buffer.Write(new byte[2 * 30]);   // 30 silent frames
    Check(buffer.Trim(10, 50, silence.IsQuiet) == 20 && buffer.Frames == 10, "quiet excess beyond the target is skipped at once");

    var loud = new byte[2 * 30];
    for (int i = 0; i < loud.Length; i += 2)
    {
        BitConverter.GetBytes((short)8000).CopyTo(loud, i);
    }

    buffer.Clear();
    buffer.Write(loud);
    Check(buffer.Trim(10, 50, silence.IsQuiet) == 0 && buffer.Frames == 30, "loud excess under the ceiling is kept (no audible skip)");
    buffer.Write(loud);
    Check(buffer.Trim(10, 50, silence.IsQuiet) == 50 && buffer.Frames == 10, "past the ceiling it is dropped back to the target anyway");
}

void RelaySilenceLevels()
{
    var asFloat = new RelaySilence(32, isFloat: true);
    var hush = new byte[8];
    BitConverter.GetBytes(0.0005f).CopyTo(hush, 0);
    BitConverter.GetBytes(-0.0002f).CopyTo(hush, 4);
    Check(asFloat.IsQuiet(hush), "samples under -60 dB are silence");
    BitConverter.GetBytes(0.02f).CopyTo(hush, 4);
    Check(!asFloat.IsQuiet(hush), "quiet speech (-34 dB) is not");

    var pcm16 = new RelaySilence(16, isFloat: false);
    Check(pcm16.IsQuiet(BitConverter.GetBytes((short)-20)) && !pcm16.IsQuiet(BitConverter.GetBytes((short)-400)), "16-bit works the same way");
    Check(!new RelaySilence(8, isFloat: false).IsQuiet(new byte[4]), "an unknown format is never called quiet");
}

void CablePairing()
{
    var soft = ChatDevicePairing.SoftwareContainer;
    var cableIn = new AudioEndpoint("in", "CABLE Input", "VB-Audio Virtual Cable", soft);
    var cableOut = new AudioEndpoint("out", "CABLE Output", "VB-Audio Virtual Cable", soft);
    var sonarChat = new AudioEndpoint("sc", "SteelSeries Sonar - Chat", "SteelSeries Sonar Virtual Audio Device", soft);
    var sonarMic1 = new AudioEndpoint("m1", "SteelSeries Sonar - Microphone", "SteelSeries Sonar Virtual Audio Device", soft);
    var sonarMic2 = new AudioEndpoint("m2", "SteelSeries Sonar - Microphone", "SteelSeries Sonar Virtual Audio Device", soft);
    var arctis = Guid.NewGuid();
    var headphones = new AudioEndpoint("hp", "Headphones", "Arctis Nova 7X", arctis);
    var mic = new AudioEndpoint("mic", "Microphone", "Arctis Nova 7X", arctis);
    AudioEndpoint[] recording = [cableOut, sonarMic1, sonarMic2, mic];

    Check(ChatDevicePairing.ListenSideOf(cableIn, recording) == cableOut, "CABLE Input pairs with CABLE Output");
    Check(ChatDevicePairing.ListenSideOf(sonarChat, recording) == null, "Sonar's chat device beside two microphones is ambiguous");
    Check(ChatDevicePairing.ListenSideOf(headphones, recording) == null, "a real headset is never a chat device");
    Check(!ChatDevicePairing.IsVirtual(headphones) && ChatDevicePairing.IsVirtual(cableIn), "only software devices are offered");
}

void TestSounds()
{
    const int rate = 48000;
    var game = new float[rate * 3 * 2];
    var chat = new float[rate * 3 * 2];
    new TestSignal(TestChannel.Game, rate).Fill(game, 2);
    new TestSignal(TestChannel.Chat, rate).Fill(chat, 2);

    Check(game.All(v => Math.Abs(v) <= TestSignal.Level + 0.001f) && chat.All(v => Math.Abs(v) <= TestSignal.Level + 0.001f), "both stay within the test level");
    Check(Enumerable.Range(0, 6).All(s => game.Skip(s * rate).Take(rate).Any(v => Math.Abs(v) > 0.05f)), "the game hum never goes quiet");
    Check(Enumerable.Range(0, 2).All(c => chat.Skip(c * 3 * rate).Take(rate / 2).Any(v => Math.Abs(v) > 0.05f)), "the chat chime repeats every cycle");
    Check(game[0] == game[1] && chat[200] == chat[201], "every channel of a frame gets the same sample");

    // Two fills in a row continue the sound instead of restarting it.
    var signal = new TestSignal(TestChannel.Game, rate);
    var first = new float[100 * 2];
    var second = new float[100 * 2];
    signal.Fill(first, 2);
    signal.Fill(second, 2);
    Check(second[0] == game[200], "the second fill picks up where the first stopped");
}

void RenamedCablePairing()
{
    var soft = ChatDevicePairing.SoftwareContainer;
    var input = new AudioEndpoint("in", ChatDevicePairing.PlaybackName, "VB-Audio Virtual Cable", soft);
    var sixteen = new AudioEndpoint("16", "CABLE In 16ch", "VB-Audio Virtual Cable", soft);
    var output = new AudioEndpoint("out", ChatDevicePairing.RecordingName, "VB-Audio Virtual Cable", soft);
    var other = new AudioEndpoint("x", "Some Other Output", "VB-Audio Virtual Cable", soft);

    Check(ChatDevicePairing.ListenSideOf(input, [output, other]) == output, "the renamed recording side is found by its ChatMixr name");
    Check(ChatDevicePairing.Preference(input) > ChatDevicePairing.Preference(sixteen), "the renamed cable wins over the 16-channel twin");
}

void MicrophoneSuggestion()
{
    var soft = ChatDevicePairing.SoftwareContainer;
    var headset = Guid.NewGuid();
    var cable = new AudioEndpoint("c", ChatDevicePairing.RecordingName, "VB-Audio Virtual Cable", soft);
    var webcam = new AudioEndpoint("w", "Microphone", "Webcam", Guid.NewGuid());
    var headsetMic = new AudioEndpoint("h", "Microphone", "Arctis Nova 7X", headset);

    Check(ChatDevicePairing.SuggestedMicrophone([cable, webcam, headsetMic], headset) == headsetMic, "the headset in use's own microphone");
    Check(ChatDevicePairing.SuggestedMicrophone([cable, webcam], Guid.NewGuid()) == webcam, "else the first real microphone");
    Check(ChatDevicePairing.SuggestedMicrophone([cable], null) == null, "never the cable itself");
}

void ChatAppMatching()
{
    string[] list = ["Discord", "ms-teams.exe"];
    Check(ChatApps.IsChat("discord", list), "case does not matter");
    Check(ChatApps.IsChat("Discord.EXE", list), ".exe is ignored on the program");
    Check(ChatApps.IsChat("ms-teams", list), ".exe is ignored on the list entry");
    Check(!ChatApps.IsChat("DiscordSetup", list), "a longer name is a different program");
    Check(ChatApps.Normalize("  Zoom.exe ") == "Zoom", "names are trimmed");
}

// ── Volume bookkeeping ──────────────────────────────────────────────────

void FirstSightScales()
{
    var tracker = new SessionVolumeTracker();
    float? set = tracker.Decide("s1", Key("game"), current: 0.8f, factor: 0.5f);

    Check(Near(set, 0.4f), "0.8 at half becomes 0.4");
    Check(Near(tracker.Baselines[Key("game")], 0.8f), "0.8 is remembered as the baseline");
    Check(tracker.BaselinesChanged, "the new baseline is marked for saving");
}

void WheelBackRestores()
{
    var tracker = new SessionVolumeTracker();
    tracker.Decide("s1", Key("game"), 0.8f, 0.25f);   // sets 0.2
    float? set = tracker.Decide("s1", Key("game"), 0.2f, 1f);

    Check(Near(set, 0.8f), "back at full the app returns to 0.8");
    Check(tracker.Decide("s1", Key("game"), 0.8f, 1f) == null, "nothing to do once it is right");
}

void UserChangeRebases()
{
    var tracker = new SessionVolumeTracker();
    tracker.Decide("s1", Key("game"), 1f, 0.5f);      // sets 0.5
    float? set = tracker.Decide("s1", Key("game"), 0.3f, 0.5f);   // the user dragged it to 0.3

    Check(set == null, "the user's level is left as it is");
    Check(Near(tracker.Baselines[Key("game")], 0.6f), "the baseline becomes 0.3 / 0.5 = 0.6");
    Check(Near(tracker.Decide("s1", Key("game"), 0.3f, 1f), 0.6f), "turning the wheel back gives 0.6, not 1");
}

void UserChangeAtZeroLeftAlone()
{
    var tracker = new SessionVolumeTracker();
    tracker.Decide("s1", Key("chat"), 0.9f, 0f);       // sets 0
    float? set = tracker.Decide("s1", Key("chat"), 0.4f, 0f);   // the user raised it anyway

    Check(set == null, "ChatMixr doesn't fight the user back to zero");
    Check(Near(tracker.Baselines[Key("chat")], 0.9f), "the baseline is kept, since 0.4 / 0 means nothing");
}

void SavedBaselineWins()
{
    var saved = new Dictionary<string, float> { [Key("game")] = 0.9f };
    var tracker = new SessionVolumeTracker(saved);

    // The crash left the app at 0.3; that must not become its baseline.
    float? set = tracker.Decide("s1", Key("game"), current: 0.3f, factor: 1f);
    Check(Near(set, 0.9f), "the app goes back to the saved 0.9");
}

void SecondSessionSharesBaseline()
{
    var tracker = new SessionVolumeTracker();
    tracker.Decide("s1", Key("game"), 0.6f, 0.5f);
    float? set = tracker.Decide("s2", Key("game"), current: 0.3f, factor: 0.5f);

    Check(set == null, "a second session already at baseline x factor is left alone");
    Check(Near(tracker.Baselines[Key("game")], 0.6f), "the shared baseline is still 0.6");
}

void ForgetRestored()
{
    var tracker = new SessionVolumeTracker();
    tracker.Decide("s1", Key("game"), 0.7f, 0.5f);
    tracker.Decide("s2", Key("closed"), 0.9f, 0.5f);   // an app that exits before ChatMixr does

    // On exit only the game app is still playing, so only it is put back.
    Check(tracker.RestoreTarget(Key("game")) is float target && Near(target, 0.7f), "the restore target is the baseline");
    tracker.BaselinesChanged = false;
    tracker.ForgetRestored();
    tracker.ForgetSessions();

    Check(!tracker.Baselines.ContainsKey(Key("game")) && tracker.BaselinesChanged, "the restored baseline is forgotten and marked for saving");
    Check(Near(tracker.Baselines[Key("closed")], 0.9f), "the closed app's baseline is kept, so the next start can undo its reduced volume");

    float? set = tracker.Decide("s1", Key("game"), 0.5f, 1f);
    Check(set == null && Near(tracker.Baselines[Key("game")], 0.5f), "the next run reads the restored app's level afresh");
}

// ── Helpers ─────────────────────────────────────────────────────────────

static MixLevels? Wheel(byte[] report) => ReceiverReport.Parse(report) is ReceiverReport.Wheel wheel ? wheel.Levels : null;

static string Key(string program) => SessionVolumeTracker.BaselineKey("{0.0.0.00000000}.{headphones}", program);

static byte[] Bytes(string hex) => hex.Split('-').Select(b => Convert.ToByte(b, 16)).ToArray();

static bool Near(float? value, float expected) => value is float v && Math.Abs(v - expected) < 0.001f;

static void Check(bool condition, string what)
{
    if (!condition)
    {
        throw new Exception(what);
    }
}
