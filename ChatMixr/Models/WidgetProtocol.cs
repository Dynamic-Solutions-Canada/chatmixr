using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatMixr.Models;

/// <summary>
/// What the Game Bar widget shows, sent by the tray app whenever it changes. The widget is a thin
/// view: every word it shows while connected comes from here, in the tray app's own language, so the
/// two can't disagree about wording or language.
/// </summary>
/// <param name="Language">The tray app's interface language (for example "fr-FR"), remembered by the widget for its own few words.</param>
/// <param name="Status">One line summing up the mix, the tray tooltip's.</param>
/// <param name="Devices">The devices the popup lists, in the user's order.</param>
/// <param name="VoiceChat">The in-game voice chat switch, or null when no virtual cable is installed.</param>
/// <param name="Labels">The fixed words the widget shows.</param>
public sealed record WidgetState(string Language, string Status, IReadOnlyList<WidgetDevice> Devices, WidgetVoiceChat? VoiceChat, WidgetLabels Labels);

/// <summary>One device as the widget shows it.</summary>
/// <param name="Id">Windows' endpoint id, sent back in commands.</param>
/// <param name="Name">The name to show.</param>
/// <param name="Glyph">Its Segoe Fluent Icons glyph.</param>
/// <param name="InUse">True for the device Windows sends games and media to.</param>
/// <param name="HasWheel">True when a headset's ChatMix wheel sets its mix, so its slider can't be dragged.</param>
/// <param name="Position">Its slider, <see cref="SliderMix.Min"/> to <see cref="SliderMix.Max"/>; 0 for a wheel not yet read.</param>
/// <param name="Caption">The line under its name (headset state, what it plays), possibly empty.</param>
/// <param name="CanSwitch">True when Switch to is offered: not the device in use, and not a headset that is off.</param>
public sealed record WidgetDevice(string Id, string Name, string Glyph, bool InUse, bool HasWheel, int Position, string Caption, bool CanSwitch);

/// <summary>The in-game voice chat switch.</summary>
/// <param name="On">True while voice chat goes through the cable.</param>
/// <param name="Label">The switch's name.</param>
/// <param name="Caption">The line under it, naming the device to pick in games.</param>
public sealed record WidgetVoiceChat(bool On, string Label, string Caption);

/// <summary>The fixed words the widget shows while connected.</summary>
public sealed record WidgetLabels(string SwitchTo, string InUse, string Game, string Chat, string WheelSets);

/// <summary>Something the user did in the widget, sent to the tray app.</summary>
/// <param name="Op">One of <see cref="WidgetCommand.Slider"/>, <see cref="WidgetCommand.Switch"/> or <see cref="WidgetCommand.VoiceChat"/>.</param>
/// <param name="DeviceId">The device, for a slider or switch.</param>
/// <param name="Position">The slider's new position.</param>
/// <param name="On">The voice chat switch's new state.</param>
public sealed record WidgetCommand(string Op, string? DeviceId = null, int Position = 0, bool On = false)
{
    /// <summary>A device's slider moved.</summary>
    public const string Slider = "slider";

    /// <summary>Switch to a device (a plain Switch to, as the popup's button does).</summary>
    public const string Switch = "switch";

    /// <summary>The in-game voice chat switch changed.</summary>
    public const string VoiceChat = "voice-chat";
}

/// <summary>
/// The pipe between the tray app and the widget: one JSON message per line, UTF-8. The tray app
/// serves it; the widget connects. One pipe per Windows session, so two signed-in users don't meet.
/// </summary>
public static class WidgetPipe
{
    /// <summary>
    /// The link the widget's Open ChatMixr falls back to, asking Game Bar to open it; the package
    /// registers its scheme to ChatMixr. Open ChatMixr first starts ChatMixr through the package's
    /// full-trust launcher, because opening a link through Game Bar closes Game Bar.
    /// </summary>
    public const string OpenUri = "chatmixr:open";

    /// <summary>The longest line either side decodes; a longer one is dropped and ends the connection.</summary>
    public const int MaxLineChars = 256 * 1024;

    /// <summary>The pipe's name (without <c>\\.\pipe\</c>) for a Windows session.</summary>
    public static string Name(int sessionId) => $"ChatMixr.GameBar.{sessionId}";

    /// <summary>One message as a line, without the newline.</summary>
    public static string Encode(WidgetState state) => JsonSerializer.Serialize(state, WidgetJson.Default.WidgetState);

    /// <summary>One message as a line, without the newline.</summary>
    public static string Encode(WidgetCommand command) => JsonSerializer.Serialize(command, WidgetJson.Default.WidgetCommand);

    /// <summary>A state line, or null when it isn't one (the connection is then dropped).</summary>
    public static WidgetState? DecodeState(string line) => Decode(line, WidgetJson.Default.WidgetState);

    /// <summary>A command line, or null when it isn't one or names nothing it can act on.</summary>
    public static WidgetCommand? DecodeCommand(string line)
    {
        var command = Decode(line, WidgetJson.Default.WidgetCommand);
        return command?.Op switch
        {
            WidgetCommand.Slider or WidgetCommand.Switch when !string.IsNullOrEmpty(command.DeviceId) => command,
            WidgetCommand.VoiceChat => command,
            _ => null,
        };
    }

    /// <summary>Parse one line, treating malformed input as nothing rather than an error.</summary>
    private static T? Decode<T>(string line, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        if (line.Length > MaxLineChars)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(line, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Source-generated JSON for the widget messages, so the Native AOT widget needs no reflection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WidgetState))]
[JsonSerializable(typeof(WidgetCommand))]
internal sealed partial class WidgetJson : JsonSerializerContext;
