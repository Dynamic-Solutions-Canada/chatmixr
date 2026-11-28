using ChatMixr.Classes;
using ChatMixr.Models;
using Microsoft.UI.Xaml;
using WinUIAppKit.Localization;
using WinUIAppKit.Theming;
using WinUIAppKit.Windowing;

namespace ChatMixr.Windows;

/// <summary>
/// ChatMixr's one passing popup, at the bottom centre of the main display, with two faces. The mix
/// (<see cref="PresentMix"/>), when a ChatMix wheel turns or a mix shortcut is pressed, styled after
/// Windows' own volume popup: a game icon, a bar filled from its centre toward the side the mix leans
/// with a line marking the position, a chat icon. And the device ChatMixr just switched to on its own
/// (<see cref="PresentSwitch"/>): its icon and name. The window itself is the kit's
/// <see cref="OsdController"/>: it never activates and is click-through, holds while changes keep coming,
/// then fades and parks off-screen, cloaked, so the next one appears instantly.
/// </summary>
public sealed partial class OsdWindow : Window
{
    private const double WidthDip = 300, HeightDip = 56;

    /// <summary>How long the mix stays after the last change before fading; a wheel's small steps follow it for as long.</summary>
    private static readonly TimeSpan MixHold = MixPopup.Hold;

    /// <summary>How long a switch stays: a little longer, since it is read rather than glanced at.</summary>
    private static readonly TimeSpan SwitchHold = TimeSpan.FromSeconds(2.5);

    private readonly OsdController _osd;

    public OsdWindow()
    {
        InitializeComponent();
        _osd = new OsdController(this, WindowRoot, HeightDip, WidthDip);
        AppTheme.Track(this);
    }

    /// <summary>Show a device's mix, or update the popup already showing, and restart its hold.</summary>
    public void PresentMix(OutputDevice device)
    {
        var levels = device.Levels ?? new MixLevels(100, 100);
        ShowPosition(SliderMix.PositionOf(levels));
        MixPanel.Visibility = Visibility.Visible;
        DevicePanel.Visibility = Visibility.Collapsed;

        // No visible text, like Windows' volume popup; screen readers hear the device and its levels.
        _osd.Show(MixHold, UiText.Format("osd.name", ("device", device.Name), ("game", levels.Game), ("chat", levels.Chat)));
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
        _osd.Show(SwitchHold, UiText.Format("osd.switched", ("device", device.Name)));
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

    /// <summary>Follow a display change: re-centre while showing, re-park while hidden.</summary>
    public void Reposition() => _osd.Reposition();
}
