namespace ChatMixr.Models;

/// <summary>
/// One message from an Arctis Nova 7 family receiver, as Windows delivers it (report ID 0 first, then
/// an opcode). Measured on the Nova 7X and Nova 7X Gen 2 receivers on 2026-11-21:
/// <list type="bullet">
/// <item>0x45 game chat: the ChatMix wheel moved (levels 0 to 100), unprompted.</item>
/// <item>0xB9 power: the headset turned on (03) or off (02), unprompted.</item>
/// <item>0xB7 battery: the battery percentage, unprompted.</item>
/// <item>0xB0 power battery link game chat: the answer to a status request
/// (<see cref="StatusRequest"/>) on the receiver's other vendor interface.</item>
/// </list>
/// Anything else is ignored.
/// </summary>
public abstract record ReceiverReport
{
    /// <summary>The ChatMix wheel's position.</summary>
    public sealed record Wheel(MixLevels Levels) : ReceiverReport;

    /// <summary>The headset turned on or off.</summary>
    public sealed record Power(bool On) : ReceiverReport;

    /// <summary>The headset's battery level, 0 to 100.</summary>
    public sealed record Battery(int Percent) : ReceiverReport;

    /// <summary>The answer to a status request: power, battery and the wheel's position, all at once.</summary>
    public sealed record Status(bool On, int BatteryPercent, MixLevels Levels) : ReceiverReport;

    private const byte PowerOn = 0x03, PowerOff = 0x02;

    /// <summary>
    /// The status request: report ID 0 and opcode 0xB0, padded to the interface's output report
    /// length. It only asks; it changes nothing on the headset.
    /// </summary>
    public static byte[] StatusRequest(int outputReportLength)
    {
        var request = new byte[Math.Max(2, outputReportLength)];
        request[1] = 0xB0;
        return request;
    }

    /// <summary>Parse one input report; null when it isn't one of the messages above.</summary>
    public static ReceiverReport? Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < 3 || report[0] != 0)
        {
            return null;
        }

        switch (report[1])
        {
            case 0x45 when report.Length >= 4:
                return new Wheel(ToLevels(report[2], report[3]));
            case 0xB9 when report[2] is PowerOn or PowerOff:
                return new Power(report[2] == PowerOn);
            case 0xB7:
                return new Battery(Math.Min((int)report[2], 100));
            case 0xB0 when report.Length >= 7 && report[2] is PowerOn or PowerOff:
                return new Status(report[2] == PowerOn, Math.Min((int)report[3], 100), ToLevels(report[5], report[6]));
            default:
                return null;
        }
    }

    /// <summary>Levels capped at 100, so a corrupt report can't raise an app above its own volume.</summary>
    private static MixLevels ToLevels(byte game, byte chat) => new(Math.Min((int)game, 100), Math.Min((int)chat, 100));
}
