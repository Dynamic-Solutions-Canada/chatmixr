using ChatMixr.Models;
using Microsoft.UI.Xaml;
using WinUIAppKit.Localization;

namespace ChatMixr.Classes;

/// <summary>
/// The badge after a wireless headset's name: a battery icon that fills with the level, coloured
/// green, amber (40% and below) or red (below 15%), with the percentage; or a grey power icon and
/// "Off". Theme colours only, so it reads in light, dark and high contrast.
/// </summary>
internal static class HeadsetBadge
{
    /// <summary>At or below this the battery shows amber.</summary>
    public const int LowBattery = 40;

    /// <summary>Below this the battery shows red.</summary>
    public const int CriticalBattery = 15;

    /// <summary>Segoe Fluent Icons: Battery0 to Battery9 run from U+E850, Battery10 is U+E83F; PowerButton is U+E7E8.</summary>
    private const int Battery0 = 0xE850, Battery10 = 0xE83F, PowerButton = 0xE7E8;

    /// <summary>The badge for a headset's state, or null when there is nothing to show yet.</summary>
    public static UIElement? Create(HeadsetState? state)
    {
        if (state is { On: false })
        {
            return Pill.Create(char.ConvertFromUtf32(PowerButton), "BadgeIconStyle", UiText.Get("device.headset-off"));
        }

        if (state is { On: true, Battery: int battery })
        {
            string style = battery < CriticalBattery ? "BadgeIconCriticalStyle"
                : battery <= LowBattery ? "BadgeIconLowStyle"
                : "BadgeIconGoodStyle";
            return Pill.Create(BatteryGlyph(battery), style, UiText.Format("device.battery", ("battery", battery)));
        }

        return null;
    }

    /// <summary>The battery icon nearest the level, in tenths.</summary>
    public static string BatteryGlyph(int percent)
    {
        int tenths = (int)Math.Round(Math.Clamp(percent, 0, 100) / 10.0);
        return char.ConvertFromUtf32(tenths == 10 ? Battery10 : Battery0 + tenths);
    }
}
