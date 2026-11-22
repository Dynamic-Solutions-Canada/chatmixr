using System.IO;
using WinUIAppKit;
using WinUIAppKit.Localization;
using WinUIAppKit.Settings;
using WinUIAppKit.Theming;
using ChatMixr.ViewModels;

namespace ChatMixr.Classes;

/// <summary>
/// The app's settings: %AppData%\ChatMixr\settings.json (redirected into package data when packaged),
/// loaded and saved atomically by the kit's versioned <see cref="JsonSettingsStore{T}"/> (CONF-3). It
/// stamps <c>SchemaVersion</c>, runs <see cref="SettingsMigrations"/> on older files after backing them
/// up, and keeps an unreadable file as <c>.bad</c>. Edits apply and save immediately.
/// </summary>
public static class SettingsManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Folder name under %AppData% for settings and logs; also named to the shared NLog config through AppLogging.UseDataFolder.</summary>
    public const string DataFolderName = "ChatMixr";

    private static readonly JsonSettingsStore<UserSettings> Store =
        new(Path.Combine(AppEnvironment.DataDirectory(DataFolderName), "settings.json"), SettingsMigrations.Steps);

    /// <summary>The loaded settings. Valid after <see cref="Restore"/>; replaced by <see cref="Reset"/>.</summary>
    public static UserSettings Current { get; private set; } = new();

    /// <summary>
    /// True when no settings file existed at launch: the app registers Start with Windows and opens
    /// its window. An unreadable file is not a first run, so those actions don't repeat.
    /// </summary>
    public static bool IsFirstRun { get; private set; }

    /// <summary>
    /// The load result when the file was unreadable and the user has not been told yet (CONF-2);
    /// null otherwise. Taken once by the Settings page through <see cref="TakeResetNotice"/>.
    /// </summary>
    private static SettingsLoadResult<UserSettings>? _pendingResetNotice;

    /// <summary>True on the launch that found the settings file unreadable, until the Settings page shows the notice.</summary>
    public static bool NeedsResetNotice => _pendingResetNotice != null;

    /// <summary>Hand the pending reset notice to the page that shows it, once; null when there is none.</summary>
    public static SettingsLoadResult<UserSettings>? TakeResetNotice()
    {
        var notice = _pendingResetNotice;
        _pendingResetNotice = null;
        return notice;
    }

    /// <summary>Real folder of settings and logs, for Explorer.</summary>
    public static string PhysicalDataDirectory => AppEnvironment.PhysicalDataDirectory(DataFolderName);

    /// <summary>
    /// Load settings (creating them on first run, upgrading an older file) and apply the saved theme.
    /// Never throws. Called after the single instance is claimed, so a second launch that only signals
    /// the first never touches the file.
    /// </summary>
    public static void Restore()
    {
        var result = Store.Load();
        Current = result.Settings;
        IsFirstRun = result.Outcome == SettingsLoadOutcome.FirstRun;
        _pendingResetNotice = result.NeedsResetNotice ? result : null;
        Logger.Info("Settings: {Outcome} (file schema {FileVersion}, backup {Backup})",
            result.Outcome, result.FileSchemaVersion?.ToString() ?? "none", result.BackupPath ?? "none");
        AppTheme.Set(Current.AppTheme);
    }

    /// <summary>
    /// "Reset settings" (CONF-4): the store keeps the current file as
    /// <c>settings.json.&lt;UTC time&gt;.reset</c> and writes defaults, keeping only the records of what
    /// ChatMixr changed in Windows (the cable's original names, its hidden entrance) and the calls
    /// device; the default theme and language apply at once. Start with Windows is Windows' state and is left as it is. Callers refresh their
    /// controls; <see cref="WasReset"/> lets the engine and the popup take the defaults.
    /// </summary>
    public static void Reset()
    {
        // Records of changes ChatMixr made to Windows itself, and the calls device Windows has, are
        // kept: without them the renamed cable and its hidden entrance could never be put back.
        var keep = Current;
        Current = Store.Reset().Settings;
        Current.CableOriginalNames = keep.CableOriginalNames;
        Current.HiddenCableEndpoints = keep.HiddenCableEndpoints;
        Current.CallsDevice = keep.CallsDevice;
        Store.Save(Current);
        AppTheme.Set(Current.AppTheme);
        UiText.SetOverride(Current.Language);
        WasReset?.Invoke();
    }

    /// <summary>
    /// Raised on the UI thread after <see cref="Reset"/>, so the parts that hold settings outside the
    /// pages (the mix engine's chat apps and slider, the popup's slider) take the defaults.
    /// </summary>
    public static event Action? WasReset;

    /// <summary>Save the current settings; failures are logged by the store, never thrown.</summary>
    public static void Save()
    {
        _saveSoonTimer?.Stop();
        Store.Save(Current);
    }

    /// <summary>
    /// Save about a second after the last call, for settings that change many times a second (a
    /// slider being dragged). Call on the UI thread. Exit saves anything still pending.
    /// </summary>
    public static void SaveSoon()
    {
        if (_saveSoonTimer == null)
        {
            _saveSoonTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _saveSoonTimer.Interval = TimeSpan.FromSeconds(1);
            _saveSoonTimer.IsRepeating = false;
            _saveSoonTimer.Tick += (_, _) => Save();
        }

        _saveSoonTimer.Stop();
        _saveSoonTimer.Start();
    }

    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveSoonTimer;
}
