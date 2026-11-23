using ChatMixr.Classes;
using ChatMixr.Models;
using ChatMixr.Services;
using ChatMixr.Windows;
using Microsoft.UI.Xaml;
using WinUIAppKit;
using WinUIAppKit.Hosting;
using WinUIAppKit.Localization;
using WinUIAppKit.Logging;
using WinUIAppKit.Startup;
using WinUIAppKit.Theming;
using WinUIAppKit.Tray;
using static WinUIAppKit.Interop.NativeMethods;

namespace ChatMixr;

/// <summary>
/// Hidden, resident host window (the kit's <see cref="HostWindow"/>: 1×1, off-screen, never closes).
/// Owns the tray icon, the <see cref="MixEngine"/> and the tray flyout. Exit happens only from the
/// tray menu.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // App identity. Never reuse a sibling app's values: a shared tray GUID or mutex makes the apps
    // collide with each other. Never change them either: Windows keeps the tray icon's show or hide
    // choice by the GUID, and a second launch finds the running copy by the title and message.
    public const string HostTitle = "ChatMixr Host";
    private const string MutexName = "ChatMixr";
    private const string ShowMessageName = "ChatMixr_Show";
    private static readonly Guid TrayGuid = new("aaa7cb25-cd98-4752-a141-a38f3a23df04");

    /// <summary>"Start with Windows" (manifest task id / unpackaged Run value).</summary>
    public static StartupRegistration Startup { get; } = new("ChatMixrStartup", "ChatMixr");

    /// <summary>The mixer, created with the host and stopped by its exit steps.</summary>
    public static MixEngine Engine { get; private set; } = null!;

    private readonly HostWindow _host;
    private readonly TrayIcon _tray;
    private readonly MixFlyout _flyout;
    private static OsdWindow? _osd;
    private static MixShortcuts? _shortcuts;
    private readonly WidgetLink _widget;
    private readonly global::Windows.UI.ViewManagement.UISettings _uiSettings = new();

    /// <summary>
    /// Claim the single instance. Called by <see cref="App"/> before settings are loaded, so a second
    /// launch never touches the settings file. When another copy is running, asks it to show itself
    /// (unless this is a silent sign-in launch) and returns false; the caller then exits.
    /// </summary>
    public static bool ClaimSingleInstance()
    {
        if (SingleInstance.TryClaim(MutexName))
        {
            return true;
        }

        if (!IsQuietLaunch())
        {
            SingleInstance.SignalRunningInstance(HostTitle, ShowMessageName);
        }

        return false;
    }

    /// <summary>
    /// True when this launch starts in the tray without opening the main window: a sign-in launch, or
    /// the Game Bar widget's Open ChatMixr (a <c>chatmixr:</c> link), since the user is in a game and
    /// the widget fills in as soon as ChatMixr is up.
    /// </summary>
    private static bool IsQuietLaunch()
    {
        if (Startup.IsSilentLaunch())
        {
            return true;
        }

        try
        {
            return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol;
        }
        catch
        {
            return false;
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        _instance = this;

        // The kit's hidden host (TRAY-7): answers a second launch's "show yourself" message.
        _host = new HostWindow(this, HostTitle, ShowMessageName);
        _host.ShowRequested += () => MixerWindow.ShowInstance();
        RegisterApplicationRestart("--silent", 0);

        Engine = new MixEngine(
            new BaselineFile(SettingsManager.DataFolderName),
            SettingsManager.Current.ChatApps,
            SettingsManager.Current.SliderPositions,
            SettingsManager.Current.DeviceOrder,
            SettingsManager.Current.InGameVoiceChat,
            SettingsManager.Current.RenameCable,
            SettingsManager.Current.AutoSwitchHeadsets,
            SettingsManager.Current.CallsDevice);
        _flyout = new MixFlyout(Engine);
        _osd = new OsdWindow();   // its OsdController registers it with HostWindow.CloseOnExit
        Engine.WheelTurned += ShowMix;
        Engine.AutoSwitched += ShowSwitch;
        SettingsManager.WasReset += () =>
        {
            Engine.SetChatApps(SettingsManager.Current.ChatApps);
            Engine.ResetSliders();
            Engine.SetDeviceOrder(SettingsManager.Current.DeviceOrder);
            Engine.SetSeparateChat(SettingsManager.Current.InGameVoiceChat);
            ApplyShortcuts();
            Engine.SetAutoSwitch(SettingsManager.Current.AutoSwitchHeadsets);
            Engine.SetRenameCable(SettingsManager.Current.RenameCable, SettingsManager.Current.CableOriginalNames, SettingsManager.Current.HiddenCableEndpoints);
            DeviceActions.SettingsWereReset();
        };

        _tray = new TrayIcon(_host.Hwnd, TrayGuid, "ChatMixr_TrayCallback");
        _tray.Clicked += a => _flyout.Toggle(a.X, a.Y);
        _tray.DoubleClicked += a => _flyout.ShowAt(a.X, a.Y);   // double-click also just opens the flyout
        _tray.ContextMenuRequested += ShowContextMenu;
        _tray.RenderNeeded += UpdateTrayIcon;
        _tray.DisplayChanged += _flyout.OnDisplayChanged;
        _tray.DisplayChanged += () => _osd?.Reposition();

        Engine.Changed += UpdateTrayIcon;
        Engine.Changed += RememberHeardPrograms;
        Engine.CableRenamed += RememberCableName;
        Engine.CallsDeviceChanged += id =>
        {
            SettingsManager.Current.CallsDevice = id;
            SettingsManager.Save();
        };
        Engine.SetHiddenDevices(SettingsManager.Current.HiddenDevices);
        DeviceActions.HiddenChanged += () => Engine.SetHiddenDevices(SettingsManager.Current.HiddenDevices);
        Engine.CableEndpointHidden += id =>
        {
            if (!SettingsManager.Current.HiddenCableEndpoints.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                SettingsManager.Current.HiddenCableEndpoints.Add(id);
                SettingsManager.Save();
            }
        };
        Engine.Changed += () => ChatDeviceSetup.OnSnapshotChanged(Engine.Snapshot);
        AppTheme.Changed += UpdateTrayIcon;
        UiText.Changed += UpdateTrayIcon;   // tooltip wording
        // The shared "Our other apps" page has its own strings; point it at our language choice.
        TechnicallyReal.Promo.PromoLocalization.LocaleProvider = () => UiText.Locale;
        UiText.Changed += TechnicallyReal.Promo.PromoLocalization.Refresh;
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateTrayIcon);
        UpdateTrayIcon();

        // Exit steps run in this order on Exit, and when Windows shuts down, restarts or signs out (the
        // kit's HostWindow handles both). The engine puts the apps' own volumes back before the tray icon goes.
        _shortcuts = new MixShortcuts(_host.Hwnd);
        _shortcuts.Pressed += DeviceActions.StepInUse;
        ApplyShortcuts();

        // The Game Bar widget, served over a pipe only this package's sandboxed widget can open.
        _widget = new WidgetLink(Engine, new WidgetServer(DispatcherQueue));

        _host.AddExitStep("save settings", SettingsManager.Save);
        _host.AddExitStep("disconnect the Game Bar widget", _widget.Dispose);
        _host.AddExitStep("release the shortcuts", _shortcuts.Dispose);
        _host.AddExitStep("put app volumes back", Engine.Dispose);
        _host.AddExitStep("remove the tray icon", _tray.Dispose);

        if (SettingsManager.IsFirstRun && SettingsManager.Current.Startup)
        {
            _ = Startup.RegisterOnFirstRunAsync();
        }

        Startup.RemoveUnpackagedEntries();

        Logger.Info("Started v{Version}, packaged={Packaged}", AppEnvironment.Version, AppEnvironment.IsPackaged);
        Engine.Start();

        // An unreadable settings file was replaced by defaults: say so on this launch, even a silent
        // one, because the next launch loads the new file normally (CONF-2). Startup is not re-registered.
        if (SettingsManager.NeedsResetNotice)
        {
            MixerWindow.ShowSettings();
        }
        else if (!IsQuietLaunch() || SettingsManager.IsFirstRun)
        {
            MixerWindow.ShowInstance();
        }
    }

    // ── Tray icon ───────────────────────────────────────────────────────
    private void UpdateTrayIcon()
    {
        var snapshot = Engine.Snapshot;
        var png = TrayIconRenderer.Render(TrayIconRenderer.StatusOf(snapshot), TrayIcon.TaskbarIsDark(), TrayIcon.IconPixelSize);

        // Same summary as the flyout footer, so the two can't disagree.
        string tooltip = UiText.Format("tray.tooltip", ("detail", MixVisuals.StatusLine(snapshot)));
        _tray.Update(png, tooltip);
    }

    /// <summary>
    /// Keep the programs heard on any device in settings, so the Chat apps page can suggest them
    /// after a restart too. Saved only when a new program is heard.
    /// </summary>
    private static void RememberHeardPrograms()
    {
        var settings = SettingsManager.Current;
        if (ChatApps.Remember(settings.RecentPrograms, Engine.Snapshot.PlayingPrograms) is { } recent)
        {
            settings.RecentPrograms = recent;
            SettingsManager.SaveSoon();
        }
    }

    /// <summary>
    /// Keep a cable endpoint's name from before ChatMixr renamed it, the first time only, so "Restore
    /// VB-Cable's names" can put back what the user had.
    /// </summary>
    private static void RememberCableName(string endpointId, string originalName)
    {
        if (SettingsManager.Current.CableOriginalNames.TryAdd(endpointId, originalName))
        {
            SettingsManager.Save();
        }
    }

    /// <summary>
    /// Show the mix popup for a device whose wheel just turned. Not while the tray popup is open, which
    /// already shows it.
    /// </summary>
    public static void ShowMix(string deviceId)
    {
        if (Engine.Snapshot.Devices.FirstOrDefault(d => d.Id == deviceId) is { } device)
        {
            ShowMix(device);
        }
    }

    /// <summary>Show the mix popup for a device at the levels it carries (a shortcut press passes the new ones).</summary>
    public static void ShowMix(OutputDevice device)
    {
        if (_instance?._flyout.IsOpen != true)
        {
            _osd?.PresentMix(device);
        }
    }

    /// <summary>
    /// Show which device ChatMixr just switched to on its own (a headset turning on or off), so the
    /// change of sound isn't a surprise. Not while the tray popup is open, which marks it.
    /// </summary>
    private void ShowSwitch(string deviceId)
    {
        if (!_flyout.IsOpen && Engine.Snapshot.Devices.FirstOrDefault(d => d.Id == deviceId) is { } device)
        {
            _osd?.PresentSwitch(device);
        }
    }

    private static MainWindow? _instance;

    /// <summary>
    /// Register the mix shortcuts from settings (or none while they are off). Returns false when
    /// another app already holds one of them, so the Settings page can say so.
    /// </summary>
    public static bool ApplyShortcuts()
    {
        var settings = SettingsManager.Current;
        return _shortcuts?.Apply(settings.MixShortcuts, settings.ShortcutTowardChat, settings.ShortcutTowardGame) ?? false;
    }

    /// <summary>Release the shortcuts while one is being recorded, so its keys reach the recorder.</summary>
    public static void SuspendShortcuts() => _shortcuts?.Suspend();

    private void ShowContextMenu(int x, int y)
    {
        new TrayMenu()
            .Add(UiText.Get("menu.open"), MixerWindow.ShowInstance)
            .Add(UiText.Get("kit.settings.title"), MixerWindow.ShowSettings)
            .Add(UiText.Get("menu.logs"), AppLogging.OpenLogsFolder)
            .AddSeparator()
            .Add(UiText.Get("menu.exit"), ExitApp)
            .Show(_host.Hwnd, x, y);
    }

    /// <summary>Exits through the host: the exit steps run in order, the log is flushed, then the process ends.</summary>
    private void ExitApp() => _host.Exit();
}
