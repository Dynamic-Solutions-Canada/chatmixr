using ChatMixr.Models;
using Windows.UI.Xaml.Data;

namespace ChatMixr.GameBar;

/// <summary>
/// Turns the widget slider's raw value into the bubble shown over its thumb while it is dragged: the
/// two levels in words, the same text as ChatMixr's popup (<see cref="SliderMix.LevelsText"/>).
/// </summary>
internal sealed partial class LevelsBubble : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        SliderMix.LevelsText(value is double position ? position : 0, WidgetText.Format);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
