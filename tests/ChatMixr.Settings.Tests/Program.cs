// Portable harness for the settings file (CONF-3, CONF-4): the kit's real JsonSettingsStore with the
// app's real schema steps (SettingsMigrations) and settings class, run against real files in a
// temporary folder. Each scenario writes the file a real install could have, loads it the way
// SettingsManager does, and checks what the user would get back. Exits non-zero on the first failure.
using System.Text.Json.Nodes;
using ChatMixr.Classes;
using ChatMixr.Models;
using ChatMixr.ViewModels;
using WinUIAppKit.Settings;

var scenarios = new List<(string Name, Action<string> Body)>
{
    ("with no file, it is a first run with the default chat apps, and the file carries a schema version", FirstRun),
    ("a saved chat app list replaces the defaults, so a removed default stays removed", SavedListReplacesDefaults),
    ("a saved false for a setting that defaults to true stays false", FalseStaysFalse),
    ("a file that isn't JSON is kept as .bad, defaults are written, and it is not a first run", NotJson),
    ("a current file loads without a backup", CurrentVersion),
    ("a file from a newer version is backed up and loaded as far as this version understands", NewerVersion),
    ("Reset keeps the old file and brings the default chat apps back", ResetKeepsOldFile),
    ("slider positions, the device order and a recorded shortcut round-trip", DeviceSettingsRoundTrip),
    ("a version 0 file where ChatMixr renamed the cable keeps its names on, with a backup", RenamedCableStaysOn),
    ("a version 0 file where nothing was renamed turns the cable names off", UnrenamedCableTurnsOff),
};

string root = Path.Combine(Path.GetTempPath(), "chatmixr-settings-tests-" + Guid.NewGuid().ToString("N"));
try
{
    for (int i = 0; i < scenarios.Count; i++)
    {
        var (name, body) = scenarios[i];
        string folder = Path.Combine(root, i.ToString());
        Directory.CreateDirectory(folder);
        try
        {
            body(Path.Combine(folder, "settings.json"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + name);
            Console.WriteLine("  " + ex.Message);
            return 1;
        }

        Console.WriteLine("PASS: " + name);
    }
}
finally
{
    Directory.Delete(root, recursive: true);
}

Console.WriteLine($"All {scenarios.Count} scenarios passed.");
return 0;

// ── Scenarios ───────────────────────────────────────────────────────────

void RenamedCableStaysOn(string path)
{
    File.WriteAllText(path, """{ "RenameCable": true, "CableOriginalNames": { "{0.0.0.00000000}.{cable}": "CABLE Input" }, "HiddenCableEndpoints": [] }""");
    var result = Store(path).Load();

    Check(result.Settings.RenameCable, "the names stay on, matching what Windows shows");
    Check(result.Settings.CableOriginalNames["{0.0.0.00000000}.{cable}"] == "CABLE Input", "the original name is kept for putting back");
    Check(result.BackupPath != null, "the version 0 file was backed up before upgrading");
    Check(FileVersion(path) == SettingsMigrations.Steps.Count, "the file now carries the current schema version");
}

void UnrenamedCableTurnsOff(string path)
{
    File.WriteAllText(path, """{ "RenameCable": true, "CableOriginalNames": {}, "HiddenCableEndpoints": [] }""");
    var result = Store(path).Load();

    Check(!result.Settings.RenameCable, "version 0 saved true without anyone choosing it, so it becomes off");
}

void FirstRun(string path)
{
    var result = Store(path).Load();

    Check(result.Outcome == SettingsLoadOutcome.FirstRun, "the outcome is FirstRun");
    Check(File.Exists(path), "the file was written");
    Check(FileVersion(path) == SettingsMigrations.Steps.Count, "the file carries the current schema version");
    Check(result.Settings.Startup, "Start with Windows defaults to on");
    Check(result.Settings.ChatApps.SequenceEqual(ChatApps.Defaults), "the chat app list is the default one");
    Check(result.Settings.SliderPositions.Count == 0, "every slider starts in the middle");
    Check(result.Settings.DeviceOrder.Count == 0, "devices start in Windows' order");
    Check(result.Settings.HiddenDevices.Count == 0, "no device starts hidden");
    Check(!result.Settings.InGameVoiceChat, "in-game voice chat starts off");
    Check(!result.Settings.MixShortcuts, "the mix shortcuts start off");
    Check(result.Settings.ShortcutTowardChat == Shortcut.DefaultTowardChat && result.Settings.ShortcutTowardGame == Shortcut.DefaultTowardGame, "the shortcuts start as Ctrl+Shift+Plus / Minus");
    Check(result.Settings.AutoSwitchHeadsets, "switching to a headset when it turns on starts on");
    Check(!result.Settings.RenameCable && result.Settings.CableOriginalNames.Count == 0, "ChatMixr's cable names start off, with nothing renamed");
    Check(result.Settings.CallsDevice == null, "no calls device is known yet");
}

void SavedListReplacesDefaults(string path)
{
    File.WriteAllText(path, """{ "SchemaVersion": 0, "ChatApps": ["Discord", "MyGameChat"] }""");
    var store = Store(path);
    var result = store.Load();

    Check(result.Settings.ChatApps.SequenceEqual(["Discord", "MyGameChat"]), "the saved list is loaded as it is, without the other defaults");

    store.Save(result.Settings);
    Check(store.Load().Settings.ChatApps.Count == 2, "saving and loading again does not add the defaults back");
}

void FalseStaysFalse(string path)
{
    File.WriteAllText(path, """{ "SchemaVersion": 0, "Startup": false }""");
    var store = Store(path);
    store.Load();

    var reloaded = store.Load();
    Check(reloaded.Outcome == SettingsLoadOutcome.Loaded, "the second load finds a current file");
    Check(!reloaded.Settings.Startup, "false is still false after saving and loading again");
}

void NotJson(string path)
{
    File.WriteAllText(path, "not json {");
    var result = Store(path).Load();

    Check(result.Outcome == SettingsLoadOutcome.Unreadable, "the outcome is Unreadable, not FirstRun");
    Check(File.ReadAllText(path + ".bad") == "not json {", "the unreadable file is kept as .bad");
    Check(FileVersion(path) == SettingsMigrations.Steps.Count, "defaults were written with the current schema version");
}

void CurrentVersion(string path)
{
    var store = Store(path);
    store.Load();
    var result = store.Load();

    Check(result.Outcome == SettingsLoadOutcome.Loaded, "the outcome is Loaded");
    Check(result.BackupPath == null && Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "no backup was made");
}

void NewerVersion(string path)
{
    string newer = """{ "SchemaVersion": 99, "AppTheme": 1, "SomethingNew": true }""";
    File.WriteAllText(path, newer);
    var result = Store(path).Load();

    Check(result.Outcome == SettingsLoadOutcome.NewerVersion, "the outcome is NewerVersion");
    Check(File.ReadAllText(path + ".v99.bak") == newer, "the newer file is kept as .v99.bak");
    Check((int)result.Settings.AppTheme == 1, "what this version understands is loaded");
}

void ResetKeepsOldFile(string path)
{
    var store = Store(path);
    File.WriteAllText(path, """{ "SchemaVersion": 0, "AppTheme": 2, "Language": "de-DE", "ChatApps": ["OnlyThis"] }""");
    store.Load();

    var result = store.Reset();
    var resetFiles = Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.json.*.reset");

    Check(result.Outcome == SettingsLoadOutcome.Reset, "the outcome is Reset");
    Check(resetFiles.Length == 1 && File.ReadAllText(resetFiles[0]).Contains("OnlyThis"), "the previous file is kept as .reset");
    Check((int)result.Settings.AppTheme == 0 && result.Settings.Language == null, "theme and language are back to their defaults");
    Check(result.Settings.ChatApps.SequenceEqual(ChatApps.Defaults), "the default chat apps are back");
    Check(FileVersion(path) == SettingsMigrations.Steps.Count, "the new file carries the current schema version");
}

void DeviceSettingsRoundTrip(string path)
{
    var store = Store(path);
    var settings = store.Load().Settings;
    settings.SliderPositions["{0.0.0.00000000}.{soundbar}"] = -40;
    settings.DeviceOrder = ["{0.0.0.00000000}.{soundbar}", "{0.0.0.00000000}.{headphones}"];
    settings.ShortcutTowardChat = new Shortcut(Shortcut.Alt, 0x75);
    store.Save(settings);

    var reloaded = store.Load().Settings;
    Check(reloaded.SliderPositions["{0.0.0.00000000}.{soundbar}"] == -40, "the slider position is kept");
    Check(reloaded.DeviceOrder.SequenceEqual(settings.DeviceOrder), "the order is kept");
    Check(reloaded.ShortcutTowardChat == new Shortcut(Shortcut.Alt, 0x75), "a recorded shortcut is kept");
}

// ── Helpers ─────────────────────────────────────────────────────────────

// The store as SettingsManager creates it, at a test path.
static JsonSettingsStore<UserSettings> Store(string path) => new(path, SettingsMigrations.Steps);

static int? FileVersion(string path) =>
    JsonNode.Parse(File.ReadAllText(path))?[JsonSettingsStore<UserSettings>.SchemaVersionProperty]?.GetValue<int>();

static void Check(bool condition, string what)
{
    if (!condition)
    {
        throw new Exception(what);
    }
}
