using ChatMixr.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;

namespace ChatMixr.Classes;

/// <summary>
/// The warning shown, in the tray popup and on the Chat apps page, while the virtual cable's
/// recording side is Windows' default microphone: then apps set to the default microphone would send
/// the cable's audio, or nothing, instead of the user's voice. It names the real microphone and has
/// a button that makes it the default again.
/// </summary>
internal static class MicrophoneWarning
{
    /// <summary>The warning for a snapshot, or null when the default microphone is fine.</summary>
    public static UIElement? Create(MixSnapshot snapshot)
    {
        if (snapshot.MicrophoneFix is not { } fix)
        {
            return null;
        }

        var panel = new StackPanel { Spacing = 6 };
        if (fix.Id == null || fix.Name == null)
        {
            panel.Children.Add(EmptyState.Create(UiText.Get("microphone.warning-no-fix")));
            return panel;
        }

        panel.Children.Add(EmptyState.Create(UiText.Format("microphone.warning", ("microphone", fix.Name))));
        string id = fix.Id;
        panel.Children.Add(CardRow.TextButton(
            UiText.Get("microphone.fix"),
            UiText.Format("microphone.fix-name", ("microphone", fix.Name)),
            null,
            () => MainWindow.Engine.UseMicrophone(id)));
        return panel;
    }
}
