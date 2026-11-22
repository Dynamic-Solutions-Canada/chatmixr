using ChatMixr.Classes;
using ChatMixr.Models;
using ChatMixr.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using static WinUIAppKit.Interop.NativeMethods;
using WinUIAppKit.Localization;
using WinUIAppKit.Theming;
using WinUIAppKit.Windowing;

namespace ChatMixr.Windows;

/// <summary>
/// Tray flyout: every visible playback device as a card with its Game-to-Chat slider and Switch to
/// button (the shared <see cref="DeviceList"/>, which can be dragged into a new order), warnings
/// (voice chat, the microphone, SteelSeries Sonar), and a footer with the device in use's volume, an Options button (the app's switches, in a panel)
/// and a gear that opens the main window. Window behaviour (placement, acrylic, parking,
/// dismissal) comes from the shared <see cref="FlyoutController"/>; this class only builds content.
/// </summary>
public sealed partial class MixFlyout : Window
{
    private const double WidthDip = 420;

    /// <summary>Space kept free between the flyout and the screen's edges.</summary>
    private const double ScreenMarginDip = 24;

    /// <summary>The list never shrinks below this, even on a very short screen.</summary>
    private const double MinListHeightDip = 160;

    /// <summary>Where the tray icon was clicked, for the screen the flyout opens on.</summary>
    private (int X, int Y) _anchor;

    private readonly MixEngine _engine;
    private readonly FlyoutController _controller;

    public MixFlyout(MixEngine engine)
    {
        InitializeComponent();

        // Switch labels and help (catalog keys), set here so the catalog check sees them.
        InGameChatSwitch.LabelKey = "in-game-chat.label";
        InGameChatSwitch.HelpKey = "in-game-chat.help";
        AutoSwitchSwitch.LabelKey = "settings.auto-switch";
        AutoSwitchSwitch.HelpKey = "settings.auto-switch-note";
        ShortcutsSwitch.LabelKey = "settings.shortcuts";
        ShortcutsSwitch.HelpKey = "flyout.shortcuts-help";
        _engine = engine;
        DeviceList.Attach(Devices);

        // The slider handles pointer events itself, so these listen to handled events too.
        VolumeSlider.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _draggingVolume = true), handledEventsToo: true);
        VolumeSlider.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _draggingVolume = false), handledEventsToo: true);
        VolumeSlider.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _draggingVolume = false), handledEventsToo: true);


        AppTheme.Track(this);
        Refresh();
        _controller = new FlyoutController(this, WindowRoot, WidthDip);

        // The flyout measures itself the moment it opens, before the list has laid out its cards
        // (worst on the first open after a start), so it re-measures once the list settles.
        Devices.SizeChanged += (_, _) =>
        {
            CapHeight();
            _controller.UpdateSize();
        };
        _controller.Opening += () =>
        {
            Refresh();
            CapHeight();
        };

        _engine.Changed += OnChanged;
        UiText.Changed += OnChanged;   // code-built rows and status relabel the same way
        DeviceActions.HiddenChanged += OnChanged;
        DeviceActions.InGameVoiceChatChanged += OnChanged;
        AppOptions.Changed += OnChanged;
    }

    /// <summary>True while the flyout is showing.</summary>
    public bool IsOpen => _controller.IsOpen;

    /// <summary>Tray single click: open, or close if open.</summary>
    public void Toggle(int anchorX, int anchorY)
    {
        _anchor = (anchorX, anchorY);
        _controller.Toggle(anchorX, anchorY);
    }

    /// <summary>Tray double-click: make sure it is open.</summary>
    public void ShowAt(int anchorX, int anchorY)
    {
        _anchor = (anchorX, anchorY);
        _controller.Show(anchorX, anchorY);
    }

    /// <summary>
    /// Keep the flyout within the screen it opens on: the device list gets whatever height the work
    /// area leaves after the header, warnings and footer, and scrolls inside it. The kit's flyout sizes
    /// to its content with no limit, so this runs before every placement (open and resize).
    /// </summary>
    private void CapHeight()
    {
        var area = DisplayArea.GetFromPoint(new PointInt32(_anchor.X, _anchor.Y), DisplayAreaFallback.Nearest);
        var monitor = MonitorFromPoint(new POINT { X = _anchor.X, Y = _anchor.Y }, MONITOR_DEFAULTTONEAREST);
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        double available = area.WorkArea.Height / scale - ScreenMarginDip;

        Devices.MaxHeight = double.PositiveInfinity;
        WindowRoot.Measure(new global::Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        double others = WindowRoot.DesiredSize.Height - Devices.DesiredSize.Height;
        Devices.MaxHeight = Math.Max(MinListHeightDip, available - others);
    }

    /// <summary>The display layout changed: close and re-park off-screen.</summary>
    public void OnDisplayChanged()
    {
        _controller.Dismiss();
        _controller.Park();
    }

    private void OnChanged()
    {
        if (!_controller.IsOpen)
        {
            return;   // refreshed on the next open anyway
        }

        // Anything can change height (a device coming or going, the switch's instruction line, a
        // warning), so the flyout re-fits to the screen after every refresh.
        Refresh();
        CapHeight();
        _controller.UpdateSize();
    }

    // ── Content ─────────────────────────────────────────────────────────
    private void Refresh()
    {
        var snapshot = _engine.Snapshot;
        DeviceList.Fill(Devices, snapshot, withPageButtons: false);

        RefreshVolume(snapshot);

        InGameChatSwitch.Visibility = snapshot.CableName != null ? Visibility.Visible : Visibility.Collapsed;
        SetUpInGameChat.Visibility = snapshot.HasScanned && snapshot.CableName == null ? Visibility.Visible : Visibility.Collapsed;
        if (!SettingsManager.Current.MixShortcuts)
        {
            ShortcutsProblem.Visibility = Visibility.Collapsed;
        }
        _settingSwitch = true;
        InGameChatSwitch.IsOn = SettingsManager.Current.InGameVoiceChat;
        AutoSwitchSwitch.IsOn = SettingsManager.Current.AutoSwitchHeadsets;
        ShortcutsSwitch.IsOn = SettingsManager.Current.MixShortcuts;
        _settingSwitch = false;
        bool virtualChat = snapshot.CableName != null && SettingsManager.Current.InGameVoiceChat;
        InGameChatInstruction.Text = virtualChat ? MixVisuals.VirtualChatInstruction(snapshot.CableName!) : "";
        InGameChatInstruction.Visibility = virtualChat ? Visibility.Visible : Visibility.Collapsed;
        bool failed = virtualChat && snapshot.Relay == RelayState.Failed;
        InGameChatProblem.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;

        MicrophoneHost.Content = MicrophoneWarning.Create(snapshot);
        MicrophoneHost.Visibility = MicrophoneHost.Content != null ? Visibility.Visible : Visibility.Collapsed;

        SonarPanel.Children.Clear();
        bool showSonar = snapshot.SonarRunning && snapshot.Devices.Any(d => d.HasWheel);
        if (showSonar)
        {
            SonarPanel.Children.Add(EmptyState.Create(UiText.Get("sonar.warning")));
        }

        SonarPanel.Visibility = showSonar ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Volume ──────────────────────────────────────────────────────────
    /// <summary>True while the volume slider is set in code, so that isn't taken as the user moving it.</summary>
    private bool _settingVolume;

    /// <summary>True while a pointer is down on the volume slider, so a refresh never fights a drag.</summary>
    private bool _draggingVolume;

    /// <summary>Show the device in use's volume and mute; the row hides when there is no device in use.</summary>
    private void RefreshVolume(MixSnapshot snapshot)
    {
        var inUse = snapshot.Devices.FirstOrDefault(d => d.IsDefault);
        if (snapshot.Volume is not { } volume || inUse == null)
        {
            VolumeRow.Visibility = Visibility.Collapsed;
            return;
        }

        VolumeRow.Visibility = Visibility.Visible;
        if (!_draggingVolume && (int)VolumeSlider.Value != volume.Percent)
        {
            _settingVolume = true;
            VolumeSlider.Value = volume.Percent;
            _settingVolume = false;
        }

        VolumeText.Text = UiText.Format("volume.level", ("level", (int)VolumeSlider.Value));
        VolumeSlider.Opacity = volume.Muted ? 0.5 : 1;
        MuteIcon.Glyph = volume.Muted ? "\uE74F" : "\uE767";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(VolumeSlider, UiText.Format("volume.name", ("device", inUse.Name)));
        string mute = UiText.Get(volume.Muted ? "volume.unmute" : "volume.mute");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MuteButton, mute);
        ToolTipService.SetToolTip(MuteButton, mute);
        _muted = volume.Muted;
    }

    private bool _muted;

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_settingVolume)
        {
            return;
        }

        int level = (int)Math.Round(e.NewValue);
        VolumeText.Text = UiText.Format("volume.level", ("level", level));
        _engine.SetVolume(level);
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e) => _engine.SetMuted(!_muted);

    /// <summary>True while the switch is set in code, so that isn't taken as the user's choice.</summary>
    private bool _settingSwitch;

    private void AutoSwitchSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingSwitch)
        {
            AppOptions.SetAutoSwitch(AutoSwitchSwitch.IsOn);
        }
    }

    private void ShortcutsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingSwitch)
        {
            // Another app holding one of the keys is said here too, not only in Settings.
            bool registered = AppOptions.SetShortcuts(ShortcutsSwitch.IsOn);
            ShortcutsProblem.Visibility = ShortcutsSwitch.IsOn && !registered ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void InGameChatSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingSwitch)
        {
            DeviceActions.SetInGameVoiceChat(InGameChatSwitch.IsOn);
        }
    }

    private void SetUpInGameChat_Click(object sender, RoutedEventArgs e)
    {
        OptionsFlyout.Hide();
        _controller.Dismiss();
        MixerWindow.ShowChatApps();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        _controller.Dismiss();
        MixerWindow.ShowInstance();
    }
}
