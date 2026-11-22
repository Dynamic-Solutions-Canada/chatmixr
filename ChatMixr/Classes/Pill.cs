using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ChatMixr.Classes;

/// <summary>
/// The small rounded label after a row's name: an icon, then short secondary text, on a subtle fill.
/// Used for a headset's battery or power state (<see cref="HeadsetBadge"/>) and for the roles a device
/// holds besides the one in use (calls, microphone). Colours come from Styles/RowStyles.xaml, so they
/// follow the window's theme.
/// </summary>
internal static class Pill
{
    /// <summary>Create a pill.</summary>
    /// <param name="glyph">The icon (Segoe Fluent Icons).</param>
    /// <param name="iconStyle">The icon's style key in Styles/RowStyles.xaml, which sets its colour.</param>
    /// <param name="text">Translated text, also its accessible name.</param>
    public static UIElement Create(string glyph, string iconStyle, string text)
    {
        var resources = Application.Current.Resources;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(new FontIcon { Glyph = glyph, Style = (Style)resources[iconStyle], VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock
        {
            Text = text,
            Style = (Style)resources["SecondaryCaptionStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        var pill = new Border
        {
            Child = row,
            Style = (Style)resources["BadgePillStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(pill, text);
        return pill;
    }
}
