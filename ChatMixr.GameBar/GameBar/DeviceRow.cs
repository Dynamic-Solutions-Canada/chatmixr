using ChatMixr.Models;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace ChatMixr.GameBar;

/// <summary>
/// One device in the widget: its icon, name and caption, Switch to (or "In use"), and its
/// Game-to-Chat slider, read-only on a headset whose wheel sets it. Updated in place, so a drag in
/// progress isn't interrupted by the state it causes.
/// </summary>
internal sealed class DeviceRow
{
    /// <summary>
    /// After the user moves the slider, ChatMixr's states keep repeating the position until it has
    /// applied the move; for this long the slider keeps the user's position instead.
    /// </summary>
    private static readonly TimeSpan LocalHold = TimeSpan.FromSeconds(1);

    private readonly FontIcon _icon = new() { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _caption = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _switch = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _inUse = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _game = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _chat = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _slider = new()
    {
        Minimum = SliderMix.Min,
        Maximum = SliderMix.Max,
        StepFrequency = 1,
        SmallChange = SliderMix.KeyStep,
        LargeChange = SliderMix.KeyStep,
        TickFrequency = SliderMix.Max,
        TickPlacement = TickPlacement.Outside,
        Margin = new Thickness(8, 0, 8, 0),
    };

    private readonly Action<WidgetCommand> _send;
    private bool _updating;
    private DateTime _lastLocalMove;

    /// <summary>The device's endpoint id.</summary>
    public string DeviceId { get; private set; } = "";

    /// <summary>The row's content, to add to the list.</summary>
    public UIElement View { get; }

    /// <param name="send">Sends a command to ChatMixr.</param>
    /// <param name="secondaryText">The page's style for secondary text (the caption and In use).</param>
    public DeviceRow(Action<WidgetCommand> send, Style secondaryText)
    {
        _send = send;
        _caption.Style = secondaryText;
        _inUse.Style = secondaryText;

        var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_name);
        text.Children.Add(_caption);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_switch, 2);
        Grid.SetColumn(_inUse, 2);
        header.Children.Add(_icon);
        header.Children.Add(text);
        header.Children.Add(_switch);
        header.Children.Add(_inUse);

        var mix = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        mix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_slider, 1);
        Grid.SetColumn(_chat, 2);
        mix.Children.Add(_game);
        mix.Children.Add(_slider);
        mix.Children.Add(_chat);

        var row = new StackPanel();
        row.Children.Add(header);
        row.Children.Add(mix);
        View = row;

        _switch.Click += (_, _) => _send(new WidgetCommand(WidgetCommand.Switch, DeviceId));
        _slider.ValueChanged += OnSliderMoved;
    }

    /// <summary>Show a device's latest state.</summary>
    public void Update(WidgetDevice device, WidgetLabels labels)
    {
        DeviceId = device.Id;
        _icon.Glyph = device.Glyph;
        _name.Text = device.Name;
        _caption.Text = device.Caption;
        _caption.Visibility = device.Caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _switch.Content = labels.SwitchTo;
        _switch.IsEnabled = device.CanSwitch;
        _switch.Visibility = device.InUse ? Visibility.Collapsed : Visibility.Visible;
        _inUse.Text = labels.InUse;
        _inUse.Visibility = device.InUse ? Visibility.Visible : Visibility.Collapsed;

        _game.Text = labels.Game;
        _chat.Text = labels.Chat;
        _slider.IsEnabled = !device.HasWheel;
        ToolTipService.SetToolTip(_slider, device.HasWheel ? labels.WheelSets : null);
        Windows.UI.Xaml.Automation.AutomationProperties.SetName(_slider, $"{device.Name}: {labels.Game} / {labels.Chat}");

        // Keep the user's own position while ChatMixr catches up with it.
        if (DateTime.UtcNow - _lastLocalMove > LocalHold)
        {
            _updating = true;
            _slider.Value = device.Position;
            _updating = false;
        }
    }

    /// <summary>The user moved the slider: snap to the middle near it, as the popup does, and tell ChatMixr.</summary>
    private void OnSliderMoved(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        int position = SliderMix.Snap((int)Math.Round(e.NewValue));
        _lastLocalMove = DateTime.UtcNow;
        if (position != (int)Math.Round(e.NewValue))
        {
            _updating = true;
            _slider.Value = position;
            _updating = false;
        }

        _send(new WidgetCommand(WidgetCommand.Slider, DeviceId, position));
    }
}
