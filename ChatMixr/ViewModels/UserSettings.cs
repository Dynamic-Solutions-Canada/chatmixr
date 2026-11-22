using ChatMixr.Models;
using WinUIAppKit.Theming;
using WinUIAppKit.Windowing;

namespace ChatMixr.ViewModels;

/// <summary>
/// User settings, saved as settings.json by <see cref="Classes.SettingsManager"/>. The kit's store owns
/// the file's <c>SchemaVersion</c>; never declare one here. A change to the shape needs a step in
/// <see cref="Classes.SettingsMigrations"/>.
/// </summary>
public sealed class UserSettings
{
    public AppThemeChoice AppTheme { get; set; }

    /// <summary>Interface language identity (for example "fr-CA"), or null to follow Windows.</summary>
    public string? Language { get; set; }

    /// <summary>Start at sign-in. Registered once on first run; afterwards the startup task/Run key is the truth.</summary>
    public bool Startup { get; set; } = true;

    /// <summary>
    /// Switch to a wireless headset when it turns on, and to the first ready device in the user's order
    /// when it turns off. On by default; off when another app (Hub Display Switch, say) manages the
    /// default device.
    /// </summary>
    public bool AutoSwitchHeadsets { get; set; } = true;

    /// <summary>
    /// The mix shortcuts (<see cref="ShortcutTowardChat"/> and <see cref="ShortcutTowardGame"/>) move the
    /// device in use toward chat / game. Off by default, since while on, those combinations stop
    /// reaching other apps.
    /// </summary>
    public bool MixShortcuts { get; set; }

    /// <summary>The shortcut that moves the device in use toward chat.</summary>
    public Shortcut ShortcutTowardChat { get; set; } = Shortcut.DefaultTowardChat;

    /// <summary>The shortcut that moves the device in use toward game.</summary>
    public Shortcut ShortcutTowardGame { get; set; } = Shortcut.DefaultTowardGame;

    /// <summary>
    /// Each device's slider position, -100 (toward game) to 100 (toward chat), by Windows' endpoint
    /// id. A device not listed is in the middle. Kept across restarts, since unlike a wheel a slider
    /// has no position of its own. Wheel devices are never listed.
    /// </summary>
    public Dictionary<string, int> SliderPositions { get; set; } = new();

    /// <summary>
    /// The user's device order, by endpoint id. Devices not connected now keep their place; devices
    /// not listed go after the listed ones, in Windows' order.
    /// </summary>
    public List<string> DeviceOrder { get; set; } = [];

    /// <summary>
    /// In-game voice chat through the virtual cable: while on, Windows' communications device is the
    /// cable and ChatMixr plays it on whichever device is in use, at its chat level.
    /// </summary>
    public bool InGameVoiceChat { get; set; }

    /// <summary>
    /// True to give the virtual cable's two ends ChatMixr's clearer names ("ChatMixr Voice Chat" and
    /// "ChatMixr internal, do not choose") and hide its extra entrance. Off by default, since it changes
    /// Windows for every app; off puts the original names back and shows the entrance again.
    /// </summary>
    public bool RenameCable { get; set; }

    /// <summary>
    /// The names the cable's endpoints had before ChatMixr renamed them, by endpoint id, for putting
    /// back. A record of a change made to Windows: Reset settings keeps it.
    /// </summary>
    public Dictionary<string, string> CableOriginalNames { get; set; } = new();

    /// <summary>
    /// The cable's extra entrances ChatMixr hid (VB-Cable's "CABLE In 16ch"), by endpoint id, to show
    /// again. A record of a change made to Windows: Reset settings keeps it.
    /// </summary>
    public List<string> HiddenCableEndpoints { get; set; } = [];

    /// <summary>
    /// The playback device calls should go to, by endpoint id: Windows' communications default as the
    /// user last chose it. While in-game voice chat holds that default for the cable, this is where calls
    /// go back to (when it is turned off, and at exit). Null until ChatMixr first sees one. Follows
    /// Windows, so Reset settings keeps it.
    /// </summary>
    public string? CallsDevice { get; set; }

    /// <summary>
    /// True after the user started the VB-Cable steps and before a cable was found, so ChatMixr turns
    /// on Separate chat for the device in use when the cable appears, even after the restart the
    /// install needs.
    /// </summary>
    public bool CableSetupStarted { get; set; }

    /// <summary>Devices hidden from the lists, by endpoint id. They are still mixed at their slider's position.</summary>
    public List<string> HiddenDevices { get; set; } = [];

    /// <summary>
    /// Program names (without ".exe") on the chat side of the mix. A saved list replaces the default
    /// one entirely, so a default the user removed stays removed.
    /// </summary>
    public List<string> ChatApps { get; set; } = [.. Models.ChatApps.Defaults];

    /// <summary>
    /// Programs ChatMixr has heard on any device, most recent first, for the Chat apps page's
    /// suggestions. Program names only; never shown anywhere else or sent anywhere.
    /// </summary>
    public List<string> RecentPrograms { get; set; } = [];

    /// <summary>Last normal position and size of the main window.</summary>
    public WindowPlacement? MainWindow { get; set; }
}
