using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatMixr.Classes;

/// <summary>
/// The app's one secondary-text message (UI-13): an empty list's "what is missing and what to do",
/// and the notes under a card. Secondary caption text, wrapping, built the same way everywhere.
/// </summary>
internal static class EmptyState
{
    /// <summary>Create the message.</summary>
    /// <param name="text">Translated text.</param>
    /// <returns>A wrapping text block in the theme's secondary text colour.</returns>
    public static TextBlock Create(string text)
    {
        var resources = Application.Current.Resources;
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)resources["SecondaryCaptionStyle"],
        };
    }
}
