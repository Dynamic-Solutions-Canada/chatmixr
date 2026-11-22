using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinUIAppKit.Localization;

namespace ChatMixr.Classes;

/// <summary>
/// ChatMixr's one switch: the switch, then its label beside it, wrapping when long (a ToggleSwitch's
/// own label can't wrap and overflows narrow places like the tray popup), then an optional help icon
/// whose text shows on hover and, when clicked, in a small panel. Clicking the label toggles the
/// switch, and the label is its accessible name. Used for every ChatMixr switch, in XAML with
/// <see cref="LabelKey"/> (and <see cref="HelpKey"/>) set to catalog keys; the kit's Settings rows
/// keep their own layout.
/// </summary>
public sealed partial class LabeledSwitch : Grid
{
    private readonly ToggleSwitch _switch = new()
    {
        OnContent = "",
        OffContent = "",
        MinWidth = 0,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _label = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The help icon; hidden unless <see cref="HelpKey"/> is set.</summary>
    private readonly Button _help = new()
    {
        Padding = new Thickness(6),
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        BorderThickness = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>The help text, shown in the panel the help icon opens.</summary>
    private readonly TextBlock _helpText = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };

    private string? _helpKey;

    public LabeledSwitch()
    {
        ColumnSpacing = 4;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_label, 1);
        Grid.SetColumn(_help, 2);
        Children.Add(_switch);
        Children.Add(_label);
        Children.Add(_help);

        _help.Content = new FontIcon { Glyph = "\uE946", FontSize = 14 };
        _help.Flyout = new Flyout { Content = _helpText };

        // UiText.Changed is static: subscribe only while on screen, or every rebuilt page would keep
        // its switches alive for good.
        Loaded += (_, _) =>
        {
            UiText.Changed -= RelabelHelp;
            UiText.Changed += RelabelHelp;
            RelabelHelp();
        };
        Unloaded += (_, _) => UiText.Changed -= RelabelHelp;

        _switch.Toggled += (_, e) => Toggled?.Invoke(this, e);
        _label.Tapped += OnLabelTapped;
    }

    /// <summary>The catalog key of the label; it relabels itself when the language changes.</summary>
    public string LabelKey
    {
        get => Localize.GetText(_label);
        set
        {
            Localize.SetText(_label, value);
            Localize.SetName(_switch, value);
        }
    }

    /// <summary>
    /// The catalog key of the help text, or null for no help icon. The text shows as the icon's
    /// tooltip and, when the icon is clicked, in a small panel; the icon's accessible name says it's help.
    /// </summary>
    public string? HelpKey
    {
        get => _helpKey;
        set
        {
            _helpKey = value;
            _help.Visibility = value != null ? Visibility.Visible : Visibility.Collapsed;
            RelabelHelp();
        }
    }

    private void RelabelHelp()
    {
        if (_helpKey == null)
        {
            return;
        }

        string text = UiText.Get(_helpKey);
        _helpText.Text = text;
        ToolTipService.SetToolTip(_help, text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_help, UiText.Format("switch.help-name", ("label", _label.Text)));
    }

    /// <summary>On or off. Setting it raises <see cref="Toggled"/> like a click does.</summary>
    public bool IsOn
    {
        get => _switch.IsOn;
        set => _switch.IsOn = value;
    }

    /// <summary>Raised when the switch changes, by a click on it or its label, or by code.</summary>
    public event RoutedEventHandler? Toggled;

    private void OnLabelTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_switch.IsEnabled)
        {
            _switch.IsOn = !_switch.IsOn;
        }
    }
}
