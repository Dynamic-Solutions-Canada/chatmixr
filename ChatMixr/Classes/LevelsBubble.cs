using ChatMixr.Models;
using Microsoft.UI.Xaml.Data;
using WinUIAppKit.Localization;

namespace ChatMixr.Classes;

/// <summary>
/// Turns a mix slider's raw value into the bubble shown over its thumb while it is dragged: the two
/// levels in words (<see cref="SliderMix.LevelsText"/>, shared with the Game Bar widget).
/// </summary>
internal sealed partial class LevelsBubble : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        SliderMix.LevelsText(value is double position ? position : 0, UiText.Format);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
