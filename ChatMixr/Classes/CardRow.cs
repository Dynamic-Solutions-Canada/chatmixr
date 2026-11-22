using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChatMixr.Classes;

/// <summary>
/// The app's one list row: a rounded card with an icon, a title and a caption, actions on the right,
/// and optional content under the title (a device's slider). Every list in the app is made of these
/// (devices, chat apps, suggestions, hidden devices), so they all look and space the same. A Grid
/// holding the card, since Border can't be derived from.
/// </summary>
internal partial class CardRow : Grid
{
    private readonly Border _card = new();
    private readonly StackPanel _layout = new() { Spacing = 2 };

    /// <summary>The vertical bar at the left edge; collapsed unless the row uses <see cref="ShowsIndicator"/>.</summary>
    private readonly Border _indicator = new() { Visibility = Visibility.Collapsed };

    /// <summary>The kind icon on the left.</summary>
    protected FontIcon Icon { get; } = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The row's name.</summary>
    protected TextBlock Title { get; } = new() { TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>A small badge right after the name; empty unless the row sets one.</summary>
    protected ContentPresenter Badge { get; } = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A quieter line right under the name (a device's maker name); collapsed while empty.</summary>
    protected TextBlock Subtitle { get; } = new() { TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>The secondary line under the name; collapsed while empty.</summary>
    protected TextBlock Caption { get; } = new() { TextWrapping = TextWrapping.Wrap };

    /// <summary>Buttons on the right, in order.</summary>
    protected StackPanel Actions { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

    /// <param name="glyph">The icon (Segoe Fluent Icons).</param>
    /// <param name="title">The name; can be changed later through <see cref="SetText"/>.</param>
    /// <param name="caption">The secondary line, or empty for none.</param>
    public CardRow(string glyph = "", string title = "", string caption = "")
    {
        // Colours come from styles (Styles/RowStyles.xaml) so they follow the window's theme.
        var resources = Application.Current.Resources;
        Margin = new Thickness(0, 0, 0, 6);
        SetAccent(false);

        Title.Style = (Style)resources["BodyStrongTextBlockStyle"];
        Caption.Style = (Style)resources["SecondaryCaptionStyle"];
        Subtitle.Style = (Style)resources["TertiaryCaptionStyle"];
        Subtitle.Visibility = Visibility.Collapsed;
        Icon.Glyph = glyph;

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // The title, with an optional badge right after it (a headset's battery or power state).
        // Left-aligned with a star column: the badge sits right after a short name, and a long name
        // wraps instead of pushing the badge out of the card.
        var titleLine = new Grid { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        titleLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(Badge, 1);
        titleLine.Children.Add(Title);
        titleLine.Children.Add(Badge);

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(titleLine);
        text.Children.Add(Subtitle);
        text.Children.Add(Caption);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(Actions, 2);
        header.Children.Add(Icon);
        header.Children.Add(text);
        header.Children.Add(Actions);

        _layout.Children.Add(header);

        // The indicator bar, then everything else.
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_layout, 1);
        body.Children.Add(_indicator);
        body.Children.Add(_layout);
        _card.Style = (Style)resources["CardRowStyle"];
        _card.Child = body;
        Children.Add(_card);
        SetText(title, caption);
    }

    /// <summary>Change the title and caption; an empty caption collapses its line.</summary>
    public void SetText(string title, string caption)
    {
        Title.Text = title;
        Caption.Text = caption;
        Caption.Visibility = caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Add an action on the right.</summary>
    public CardRow WithAction(UIElement action)
    {
        Actions.Children.Add(action);
        return this;
    }

    /// <summary>Add content under the header, the full width of the card.</summary>
    protected void AddBody(FrameworkElement body)
    {
        body.Margin = new Thickness(0, 2, 0, 0);
        _layout.Children.Add(body);
    }

    /// <summary>Set the quieter line under the name; empty hides it.</summary>
    protected void SetSubtitle(string text)
    {
        Subtitle.Text = text;
        Subtitle.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// True for rows that can be the current one (devices): a vertical bar at the card's left edge,
    /// neutral until <see cref="SetAccent"/> marks the row current.
    /// </summary>
    protected bool ShowsIndicator
    {
        set => _indicator.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Mark the row as the current one: the indicator bar and the icon in the accent colour.</summary>
    protected void SetAccent(bool accent)
    {
        var resources = Application.Current.Resources;
        _indicator.Style = (Style)resources[accent ? "RowIndicatorCurrentStyle" : "RowIndicatorStyle"];
        Icon.Style = (Style)resources[accent ? "RowIconCurrentStyle" : "RowIconStyle"];
    }

    /// <summary>
    /// A compact text button for a row's main action ("Use", "Add", "Show"), named for screen readers
    /// with what it acts on.
    /// </summary>
    public static Button TextButton(string label, string accessibleName, string? tooltip, Action onClick)
    {
        var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, accessibleName);
        if (tooltip != null)
        {
            ToolTipService.SetToolTip(button, tooltip);
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>
    /// A button with an icon and a label (the family's <c>IconLabelStyle</c>), for an action that
    /// must be easy to find. <see cref="SetIconLabel"/> changes both later (Test, then Stop).
    /// </summary>
    public static Button IconTextButton(string glyph, string label, Action onClick)
    {
        var resources = Application.Current.Resources;
        var panel = new StackPanel { Style = (Style)resources["IconLabelStyle"] };
        panel.Children.Add(new FontIcon { Glyph = glyph, Style = (Style)resources["ButtonGlyphStyle"] });
        panel.Children.Add(new TextBlock { Text = label });
        var button = new Button { Content = panel, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Change an <see cref="IconTextButton"/>'s icon and label.</summary>
    public static void SetIconLabel(Button button, string glyph, string label)
    {
        var panel = (StackPanel)button.Content;
        ((FontIcon)panel.Children[0]).Glyph = glyph;
        ((TextBlock)panel.Children[1]).Text = label;
    }

    /// <summary>
    /// A borderless icon button for a row's secondary actions (Move up, Hide, Remove), with a tooltip
    /// and an accessible name, since it has no text.
    /// </summary>
    public static Button IconButton(string glyph, string accessibleName, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Relabel(button, accessibleName, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Set a button's accessible name and tooltip again, after a language change.</summary>
    public static void Relabel(Button button, string accessibleName, string tooltip)
    {
        AutomationProperties.SetName(button, accessibleName);
        ToolTipService.SetToolTip(button, tooltip);
    }
}
