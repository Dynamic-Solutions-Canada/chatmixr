using ChatMixr.Classes;
using ChatMixr.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using WinUIAppKit.Localization;
using WinUIAppKit.Theming;
using static ChatMixr.Classes.NativeMethods;
using static WinUIAppKit.Interop.NativeMethods;

namespace ChatMixr.Windows;

/// <summary>
/// ChatMixr's one passing popup, at the bottom centre of the main display, with two faces. The mix
/// (<see cref="PresentMix"/>), when a ChatMix wheel turns or a mix shortcut is pressed, styled after
/// Windows' own volume popup: a game icon, a bar filled from its centre toward the side the mix leans
/// with a line marking the position, a chat icon. And the device ChatMixr just switched to on its own
/// (<see cref="PresentSwitch"/>): its icon and name. Modelled on Hub
/// Display Switch's popup: it never activates and is click-through, holds while changes keep coming,
/// then fades (layered alpha) and parks off-screen, cloaked, so the next one appears instantly.
/// </summary>
public sealed partial class OsdWindow : Window
{
    private const double WidthDip = 300, HeightDip = 56;

    /// <summary>How long the mix stays after the last change before fading.</summary>
    private static readonly TimeSpan MixHold = TimeSpan.FromSeconds(1.5);

    /// <summary>How long a switch stays: a little longer, since it is read rather than glanced at.</summary>
    private static readonly TimeSpan SwitchHold = TimeSpan.FromSeconds(2.5);

    private const int FadeSteps = 12;

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _hold, _fade;
    private int _fadeStep;
    private bool _visible;

    public OsdWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
        appWindow.SetPresenter(presenter);
        appWindow.IsShownInSwitchers = false;

        SystemBackdrop = new DesktopAcrylicBackdrop();
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        AddExStyle(_hwnd, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);

        AppTheme.Track(this);

        _hold = DispatcherQueue.CreateTimer();
        _hold.IsRepeating = false;
        _hold.Tick += (_, _) => StartFade();
        _fade = DispatcherQueue.CreateTimer();
        _fade.Interval = TimeSpan.FromMilliseconds(30);
        _fade.Tick += (_, _) => FadeTick();

        // Warm up cloaked and parked off-screen so the first popup is composed instantly.
        Cloak(_hwnd, true);
        appWindow.Show(false);
        Park();
    }

    /// <summary>Show a device's mix, or update the popup already showing, and restart its hold.</summary>
    public void PresentMix(OutputDevice device)
    {
        var levels = device.Levels ?? new MixLevels(100, 100);
        ShowPosition(SliderMix.PositionOf(levels));
        MixPanel.Visibility = Visibility.Visible;
        DevicePanel.Visibility = Visibility.Collapsed;

        // No visible text, like Windows' volume popup; screen readers hear the device and its levels.
        Show(UiText.Format("osd.name", ("device", device.Name), ("game", levels.Game), ("chat", levels.Chat)), MixHold);
    }

    /// <summary>Show the device ChatMixr just switched to (a headset turning on or off), and restart the hold.</summary>
    public void PresentSwitch(OutputDevice device)
    {
        DeviceIcon.Glyph = MixVisuals.Glyph(device.Kind);
        DeviceName.Text = device.EndpointName;
        DeviceAdapter.Text = device.AdapterName;
        DeviceAdapter.Visibility = device.AdapterName.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MixPanel.Visibility = Visibility.Collapsed;
        DevicePanel.Visibility = Visibility.Visible;
        Show(UiText.Format("osd.switched", ("device", device.Name)), SwitchHold);
    }

    /// <summary>Bring the popup up (or keep it up) with the face just set, and restart its hold.</summary>
    /// <param name="accessibleName">What screen readers hear for it.</param>
    /// <param name="hold">How long it stays before fading.</param>
    private void Show(string accessibleName, TimeSpan hold)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WindowRoot, accessibleName);
        _fade.Stop();
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
        _visible = true;
        Reposition();

        _hold.Stop();
        _hold.Interval = hold;
        _hold.Start();
    }

    /// <summary>
    /// Fill the bar from its centre to the position (-100 all the way to game, 100 all the way to chat),
    /// and move the line to it: star columns over the bar's 200 steps (before the fill, the fill, after
    /// it; and either side of the line).
    /// </summary>
    private void ShowPosition(int position)
    {
        int p = Math.Clamp(position, SliderMix.Min, SliderMix.Max);
        double before = Math.Min(100, 100 + p);
        double fill = Math.Abs(p);
        double after = 200 - before - fill;
        BeforeColumn.Width = new GridLength(before, GridUnitType.Star);
        FillColumn.Width = new GridLength(fill, GridUnitType.Star);
        AfterColumn.Width = new GridLength(after, GridUnitType.Star);

        // The line sits at the fill's far end: 0 to 200 along the bar.
        LineBeforeColumn.Width = new GridLength(100 + p, GridUnitType.Star);
        LineAfterColumn.Width = new GridLength(100 - p, GridUnitType.Star);
    }

    /// <summary>Bottom centre of the main display, above everything, without activating.</summary>
    public void Reposition()
    {
        // Hidden: re-park (a display change makes Windows pull off-screen windows back into view).
        if (!_visible)
        {
            Park();
            return;
        }

        var (bounds, _, scale) = PrimaryMonitorArea();
        int w = (int)Math.Ceiling(WidthDip * scale), h = (int)Math.Ceiling(HeightDip * scale);
        int x = bounds.Left + (bounds.Right - bounds.Left - w) / 2;
        int y = bounds.Bottom - h - (int)(64 * scale);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Cloak(_hwnd, false);
    }

    private void StartFade()
    {
        _fadeStep = 0;
        _fade.Start();
    }

    private void FadeTick()
    {
        _fadeStep++;
        byte alpha = (byte)Math.Max(0, 255 - 255 * _fadeStep / FadeSteps);
        SetLayeredWindowAttributes(_hwnd, 0, alpha, LWA_ALPHA);
        if (_fadeStep < FadeSteps)
        {
            return;
        }

        _fade.Stop();
        _visible = false;
        Park();
        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
    }

    private void Park()
    {
        Cloak(_hwnd, true);
        var (x, y) = ParkingSpot((int)WidthDip, (int)HeightDip);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, (int)WidthDip, (int)HeightDip, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
