using ChatMixr.Models;
using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace ChatMixr.GameBar;

/// <summary>
/// The widget's one page: the devices ChatMixr's popup lists, each with Switch to and its slider, and
/// the in-game voice chat switch; or, while ChatMixr isn't running, a button that opens it.
/// </summary>
public sealed partial class WidgetPage : Page
{
    /// <summary>How long "Starting ChatMixr" waits for it to connect before offering Open ChatMixr again.</summary>
    private static readonly TimeSpan StartingTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Room left on the right for the scrollbar while the list scrolls.</summary>
    private const double ScrollBarRoom = 16;

    private readonly List<DeviceRow> _rows = [];
    private WidgetClient? _client;
    private XboxGameBarWidget? _widget;
    private WidgetState? _state;
    private bool _connected;
    private bool _opening;
    private bool _updatingVoiceChat;

    public WidgetPage()
    {
        InitializeComponent();
        ShowConnection();
        DeviceScroller.RegisterPropertyChangedCallback(ScrollViewer.ScrollableHeightProperty, (_, _) => FitScrollBar());

        // Game Bar closed the widget (its content is cleared): drop the connection.
        Unloaded += (_, _) =>
        {
            _client?.Dispose();
            _client = null;
        };
    }

    /// <summary>Game Bar's widget arrives as the navigation parameter: follow its theme, and connect.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _widget = e.Parameter as XboxGameBarWidget;
        if (_widget != null)
        {
            _widget.RequestedThemeChanged += (_, _) => _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, ApplyTheme);
            ApplyTheme();
        }

        _client = new WidgetClient(DispatcherQueue.GetForCurrentThread());
        _client.StateReceived += Show;
        _client.ConnectionChanged += connected =>
        {
            _connected = connected;
            _opening &= !connected;
            ShowConnection();
        };
        _client.Start();
    }

    /// <summary>Match Game Bar's light or dark theme.</summary>
    private void ApplyTheme()
    {
        // Runs from a dispatcher callback, where an exception would end the whole package.
        try
        {
            if (_widget != null)
            {
                RequestedTheme = _widget.RequestedTheme;
            }
        }
        catch (Exception ex)
        {
            WidgetLog.Error("following Game Bar's theme", ex);
        }
    }

    /// <summary>Show ChatMixr's latest state.</summary>
    private void Show(WidgetState state)
    {
        _state = state;

        // Always, not only when the language changes: the first state is what turns "isn't running"
        // into the mix (the connection alone arrives before any state).
        WidgetText.Follow(state.Language);
        ShowConnection();

        StatusText.Text = state.Status;
        ShowDevices(state);
        ShowVoiceChat(state.VoiceChat);
    }

    /// <summary>
    /// Update the rows in place when the same devices are listed in the same order, so a drag isn't
    /// interrupted; otherwise rebuild them.
    /// </summary>
    private void ShowDevices(WidgetState state)
    {
        bool sameDevices = _rows.Count == state.Devices.Count
            && _rows.Zip(state.Devices).All(pair => pair.First.DeviceId == pair.Second.Id);
        if (!sameDevices)
        {
            _rows.Clear();
            DeviceRows.Children.Clear();
            foreach (var _ in state.Devices)
            {
                // StatusText already carries the secondary style; a cast from the page's resource
                // dictionary fails in the Native AOT build (InvalidCastException).
                var row = new DeviceRow(Send, StatusText.Style);
                _rows.Add(row);
                DeviceRows.Children.Add(row.View);
            }
        }

        for (int i = 0; i < state.Devices.Count; i++)
        {
            _rows[i].Update(state.Devices[i], state.Labels);
        }
    }

    /// <summary>
    /// Keep the scrollbar, which is drawn over the content, off the Switch to buttons: room on the
    /// right only while the list is long enough to scroll, so a short list uses the full width.
    /// </summary>
    private void FitScrollBar()
    {
        double room = DeviceScroller.ScrollableHeight > 0 ? ScrollBarRoom : 0;
        DeviceRows.Padding = new Thickness(0, 0, room, 0);
    }

    /// <summary>The in-game voice chat switch, shown only when a virtual cable is installed.</summary>
    private void ShowVoiceChat(WidgetVoiceChat? voiceChat)
    {
        VoiceChatPanel.Visibility = voiceChat != null ? Visibility.Visible : Visibility.Collapsed;
        if (voiceChat == null)
        {
            return;
        }

        _updatingVoiceChat = true;
        VoiceChatSwitch.Header = voiceChat.Label;
        VoiceChatSwitch.IsOn = voiceChat.On;
        _updatingVoiceChat = false;
        VoiceChatCaption.Text = voiceChat.Caption;
        VoiceChatCaption.Visibility = voiceChat.Caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Connected: the mix. Not connected: say ChatMixr isn't running (or is starting) and offer to open it.</summary>
    private void ShowConnection()
    {
        bool showMix = _connected && _state != null;
        DisconnectedPanel.Visibility = showMix ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Visibility = showMix ? Visibility.Visible : Visibility.Collapsed;
        DeviceRows.Visibility = showMix ? Visibility.Visible : Visibility.Collapsed;
        if (!showMix)
        {
            VoiceChatPanel.Visibility = Visibility.Collapsed;
        }

        DisconnectedText.Text = WidgetText.Get(_opening ? "widget.connecting" : "widget.not-running");
        OpenButton.Content = WidgetText.Get("menu.open");
        OpenButton.Visibility = _opening ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Send(WidgetCommand command) => _ = _client?.SendAsync(command);

    /// <summary>
    /// Start ChatMixr in the tray. First as the package's own desktop program (the manifest's "Quiet"
    /// parameter group), which leaves Game Bar open; failing that, through Game Bar's link launch,
    /// which works but closes Game Bar. Windows refuses the widget's own app launch (access denied),
    /// because a widget never counts as the app in front.
    /// </summary>
    /// <returns>True when Windows took the request; ChatMixr may still be starting.</returns>
    private async Task<bool> StartChatMixrAsync()
    {
        try
        {
            await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync("Quiet");
            WidgetLog.Info("open ChatMixr: started it");
            return true;
        }
        catch (Exception ex)
        {
            WidgetLog.Error("starting ChatMixr directly; trying Game Bar's link", ex);
        }

        if (_widget == null)
        {
            WidgetLog.Info("open ChatMixr: not running in Game Bar");
            return false;
        }

        bool launched = await _widget.LaunchUriAsync(new Uri(WidgetPipe.OpenUri));
        WidgetLog.Info(launched ? "open ChatMixr: Game Bar opened its link" : "open ChatMixr: Game Bar didn't open its link");
        return launched;
    }

    private void OnVoiceChatToggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingVoiceChat)
        {
            Send(new WidgetCommand(WidgetCommand.VoiceChat, On: VoiceChatSwitch.IsOn));
        }
    }

    /// <summary>
    /// Open ChatMixr (the package's other app) in the tray; the widget connects once it is up.
    /// </summary>
    private async void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        _opening = true;
        ShowConnection();

        // An async void handler's exception would end the whole package, ChatMixr included.
        bool launched = false;
        try
        {
            launched = await StartChatMixrAsync();
        }
        catch (Exception ex)
        {
            WidgetLog.Error("opening ChatMixr", ex);
        }

        if (launched)
        {
            // Windows can accept the link and still not start ChatMixr (a copy still exiting holds its
            // single instance, for one), so "Starting" gives way to the button again.
            await Task.Delay(StartingTimeout);
            if (!_opening || _connected)
            {
                return;
            }

            WidgetLog.Info("open ChatMixr: it didn't connect in time; offering the button again");
        }

        _opening = false;
        ShowConnection();
    }
}
