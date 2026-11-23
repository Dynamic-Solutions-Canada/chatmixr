using ChatMixr.Models;
using ChatMixr.Services;
using WinUIAppKit;
using WinUIAppKit.Localization;

namespace ChatMixr.Classes;

/// <summary>
/// Connects the Game Bar widget to the rest of the app: tells it what the popup would show, in the
/// same words, and carries out what the user does in it through <see cref="DeviceActions"/>, as the
/// popup's own controls do. Commands are checked against the current snapshot first, since they
/// arrive from another process.
/// </summary>
internal sealed class WidgetLink : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly MixEngine _engine;
    private readonly WidgetServer _server;

    /// <summary>Start serving the widget and keep it up to date.</summary>
    /// <param name="engine">The mixer whose snapshot the widget shows.</param>
    /// <param name="server">The pipe, not yet started.</param>
    public WidgetLink(MixEngine engine, WidgetServer server)
    {
        _engine = engine;
        _server = server;

        _server.CommandReceived += Carry;
        _engine.Changed += Publish;
        UiText.Changed += Publish;
        DeviceActions.HiddenChanged += Publish;
        DeviceActions.InGameVoiceChatChanged += Publish;

        Publish();
        _server.Start(AppEnvironment.IsPackaged ? global::Windows.ApplicationModel.Package.Current.Id.FamilyName : null);
    }

    /// <summary>Stop serving. Part of the exit steps.</summary>
    public void Dispose()
    {
        _engine.Changed -= Publish;
        UiText.Changed -= Publish;
        DeviceActions.HiddenChanged -= Publish;
        DeviceActions.InGameVoiceChatChanged -= Publish;
        _server.Dispose();
    }

    /// <summary>Send the widget the current state (the server skips it when nothing changed).</summary>
    private void Publish() => _server.Publish(StateOf(_engine.Snapshot));

    /// <summary>The widget's view of a snapshot: the popup's devices, words and in-game voice chat switch.</summary>
    private static WidgetState StateOf(MixSnapshot snapshot)
    {
        var devices = DeviceList.Visible(snapshot)
            .Select(device => new WidgetDevice(
                device.Id,
                device.EndpointName,
                MixVisuals.Glyph(device.Kind),
                device.IsDefault,
                device.HasWheel,
                PositionOf(device),
                CaptionOf(device),
                CanSwitch(device)))
            .ToList();

        WidgetVoiceChat? voiceChat = null;
        if (snapshot.CableName is { } cable)
        {
            bool on = SettingsManager.Current.InGameVoiceChat;
            voiceChat = new WidgetVoiceChat(on, UiText.Get("in-game-chat.label"), on ? MixVisuals.VirtualChatInstruction(cable) : "");
        }

        var labels = new WidgetLabels(
            UiText.Get("device.switch"),
            UiText.Get("device.in-use"),
            UiText.Get("mix.game"),
            UiText.Get("mix.chat"),
            UiText.Get("slider.wheel-tip"));

        return new WidgetState(UiText.Locale, MixVisuals.StatusLine(snapshot), devices, voiceChat, labels);
    }

    /// <summary>Switch to is offered as the popup offers it: not for the device in use or a headset that's off.</summary>
    private static bool CanSwitch(OutputDevice device) => !device.IsDefault && device.Headset is not { On: false };

    /// <summary>Where a device's slider sits: its saved position, or where its wheel is.</summary>
    private static int PositionOf(OutputDevice device)
    {
        if (device.HasWheel)
        {
            return device.Levels is { } levels ? SliderMix.PositionOf(levels) : 0;
        }

        return SettingsManager.Current.SliderPositions.GetValueOrDefault(device.Id);
    }

    /// <summary>The line under a device's name: the headset's state when it has one, else what it plays.</summary>
    private static string CaptionOf(OutputDevice device)
    {
        string headset = device.Headset switch
        {
            { On: false } => UiText.Get("device.headset-off"),
            { On: true, Battery: int battery } => UiText.Format("device.battery", ("battery", battery)),
            _ => "",
        };

        string activity = MixVisuals.Activity(device);
        if (headset.Length > 0 && activity.Length > 0)
        {
            return $"{headset} · {activity}";
        }

        return headset.Length > 0 ? headset : activity;
    }

    /// <summary>
    /// Carry out a command from the widget, on the UI thread. Only devices the widget was shown are
    /// acted on, a wheel device's slider is the wheel's, and positions get the popup's centre detent.
    /// </summary>
    private void Carry(WidgetCommand command)
    {
        if (command.Op == WidgetCommand.VoiceChat)
        {
            if (_engine.Snapshot.CableName != null)
            {
                DeviceActions.SetInGameVoiceChat(command.On);
            }

            return;
        }

        var device = DeviceList.Visible(_engine.Snapshot)
            .FirstOrDefault(d => string.Equals(d.Id, command.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (device == null)
        {
            Logger.Info("Game Bar widget asked about a device that isn't listed; ignored");
            return;
        }

        switch (command.Op)
        {
            case WidgetCommand.Slider when !device.HasWheel:
                DeviceActions.SetSlider(device.Id, SliderMix.Snap(command.Position));
                break;

            case WidgetCommand.Switch when CanSwitch(device):
                DeviceActions.SwitchTo(device.Id, SwitchScope.Follow);
                break;
        }
    }
}
