using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using WinRT;

namespace ChatMixr.GameBar;

/// <summary>
/// The widget app. Game Bar starts it with an <c>ms-gamebarwidget:</c> protocol activation; the first
/// one creates the widget and its page, and later ones (Game Bar reopening it) must not. It has no
/// window of its own outside Game Bar.
/// </summary>
public sealed partial class App : Application
{
    // Held for the app's lifetime: Game Bar's link to the widget's window lives in this object.
    private XboxGameBarWidget? _widget;

    public App()
    {
        // Recorded, then handled once the widget is open: a crashing widget makes Windows end the whole
        // package, ChatMixr's tray app included. Before that (start-up, opening) it still fails, since
        // there is no working widget to keep.
        UnhandledException += (_, e) =>
        {
            WidgetLog.Error("unhandled", e.Exception);
            e.Handled = _widget != null;
        };
        WidgetLog.Info("started");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            WidgetLog.Error("start-up", ex);
            throw;
        }
    }

    /// <summary>A plain launch isn't Game Bar's way in; recorded so a wrong entry point shows in the log.</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WidgetLog.Info($"launched ({args.Kind}); not a Game Bar activation, ignored");
    }

    /// <summary>Game Bar's activation: create the widget on its first launch activation.</summary>
    protected override void OnActivated(IActivatedEventArgs args)
    {
        WidgetLog.Info($"activated: {args.Kind}, {args.GetType().Name}");
        if (args.Kind != ActivationKind.Protocol
            || args is not IProtocolActivatedEventArgs protocol
            || protocol.Uri.Scheme != "ms-gamebarwidget")
        {
            WidgetLog.Info("not a Game Bar widget activation; ignored");
            return;
        }

        try
        {
            var widgetArgs = GameBarArgs(args);
            if (!widgetArgs.IsLaunchActivation)
            {
                WidgetLog.Info("Game Bar reactivated the widget; nothing to do");
                return;
            }

            var frame = new Frame();
            Window.Current.Content = frame;
            _widget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, frame);
            frame.Navigate(typeof(WidgetPage), _widget);
            Window.Current.Closed += OnWindowClosed;
            Window.Current.Activate();
            WidgetLog.Info("widget opened");
        }
        catch (Exception ex)
        {
            WidgetLog.Error("opening the widget", ex);
            throw;
        }
    }

    /// <summary>
    /// Game Bar's arguments. Under Native AOT the activation can arrive as a plain protocol activation
    /// even though the object behind it is Game Bar's, so when the type test fails the same object is
    /// wrapped as Game Bar's type (which asks it for that interface, and throws if it isn't one).
    /// </summary>
    private static XboxGameBarWidgetActivatedEventArgs GameBarArgs(IActivatedEventArgs args)
    {
        if (args is XboxGameBarWidgetActivatedEventArgs widgetArgs)
        {
            return widgetArgs;
        }

        WidgetLog.Info("activation isn't typed as Game Bar's; wrapping it");
        IntPtr pointer = MarshalInspectable<object>.FromManaged(args);
        try
        {
            return XboxGameBarWidgetActivatedEventArgs.FromAbi(pointer);
        }
        finally
        {
            MarshalInspectable<object>.DisposeAbi(pointer);
        }
    }

    /// <summary>Game Bar closed the widget: let go of it (the page drops its connection).</summary>
    private void OnWindowClosed(object sender, Windows.UI.Core.CoreWindowEventArgs e)
    {
        if (Window.Current.Content is Frame frame)
        {
            frame.Content = null;
        }

        _widget = null;
        Window.Current.Closed -= OnWindowClosed;
    }
}
