using ChatMixr.Classes;
using ChatMixr.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;
using WinUIAppKit.Pages;
using WinUIAppKit.Windowing;

namespace ChatMixr;

/// <summary>
/// The main window, hosted in the kit's <see cref="SettingsShell"/> (UI-1, UI-2): Devices, Chat apps,
/// Settings, Our other apps and About. Opened from the flyout's Open button or the tray menu. One
/// instance at a time.
/// </summary>
public sealed partial class MixerWindow : Window
{
    // This window's own item tags; Settings, Our other apps and About use the shell's tags.
    private const string DevicesTag = "devices";
    private const string ChatAppsTag = "chat-apps";

    private static MixerWindow? _instance;

    /// <summary>The open window, or null when it is closed. Pages use it to ask questions on it.</summary>
    internal static Window? Instance => _instance;

    // ── Opening ─────────────────────────────────────────────────────────
    /// <summary>Show the window (creating it if needed) on its current page (Devices for a new window).</summary>
    public static void ShowInstance() => Show(null);

    /// <summary>Show the window on the Chat apps page, where in-game voice chat is set up.</summary>
    public static void ShowChatApps() => Show(ChatAppsTag);

    /// <summary>Show the window on the Settings page.</summary>
    public static void ShowSettings() => Show(SettingsShell.SettingsTag);

    /// <summary>
    /// Open the Settings page afresh after Reset settings, so the kit's rows, which read their state
    /// when created, show the defaults. Unlike a language change, a reset changes what the rows hold.
    /// </summary>
    public static void ReloadSettingsPage()
    {
        if (_instance?.Shell.ContentFrame.Content is GeneralPage)
        {
            _instance.Shell.ContentFrame.Navigate(typeof(GeneralPage), null);
        }
    }

    private static void Show(string? tag)
    {
        _instance ??= new MixerWindow();
        if (tag != null)
        {
            _instance.Shell.Select(tag);
        }

        WindowChrome.BringToFront(_instance);
    }

    private MixerWindow()
    {
        InitializeComponent();
        Closed += OnClosed;

        Shell.Caption = "ChatMixr";
        Shell.IconPngPath = App.IconPngPath;
        WindowChrome.SetUpSettingsWindow(this, Shell.TitleBar, App.IconPath, SettingsManager.Current.MainWindow);
        Shell.PageSelected += OnPageSelected;

        BuildMenu();
        Shell.Select(DevicesTag);
        UiText.Changed += OnLanguageChanged;
    }

    // ── Menu ────────────────────────────────────────────────────────────
    /// <summary>The menu is built in code, so a language change rebuilds it and keeps the selection.</summary>
    private void OnLanguageChanged()
    {
        string tag = Shell.SelectedTag ?? DevicesTag;
        BuildMenu();
        Shell.Select(tag);
    }

    private void BuildMenu()
    {
        Shell.MenuItems.Clear();
        Shell.MenuItems.Add(new NavigationViewItem
        {
            Content = UiText.Get("nav.devices"),
            Tag = DevicesTag,
            Icon = new FontIcon { Glyph = "" },
        });
        Shell.MenuItems.Add(new NavigationViewItem
        {
            Content = UiText.Get("nav.chat-apps"),
            Tag = ChatAppsTag,
            Icon = new FontIcon { Glyph = "" },
        });
    }

    // ── Navigation ──────────────────────────────────────────────────────
    private void OnPageSelected(string tag)
    {
        if (tag == SettingsShell.SettingsTag)
        {
            Navigate(typeof(GeneralPage), null);
        }
        else if (tag == SettingsShell.AboutTag)
        {
            Navigate(typeof(AboutPage), AboutContent.Create());
        }
        else if (tag == SettingsShell.OtherAppsTag)
        {
            Navigate(typeof(TechnicallyReal.Promo.PromotedAppsPage), null);
        }
        else if (tag == ChatAppsTag)
        {
            Navigate(typeof(ChatAppsPage), null);
        }
        else
        {
            Navigate(typeof(DevicesPage), null);
        }
    }

    /// <summary>Show a page, unless it is already showing.</summary>
    /// <param name="parameter">The <see cref="AboutInfo"/> for About, otherwise null.</param>
    private void Navigate(Type page, object? parameter)
    {
        if (Shell.ContentFrame.Content?.GetType() == page)
        {
            return;
        }

        Shell.ContentFrame.Navigate(page, parameter);
    }

    // ── Closing ─────────────────────────────────────────────────────────
    private void OnClosed(object sender, WindowEventArgs e)
    {
        _instance = null;
        UiText.Changed -= OnLanguageChanged;

        // Unloaded isn't reliably raised when a window closes; drop the current page's subscriptions here.
        if (Shell.ContentFrame.Content is LivePage page)
        {
            page.Detach();
        }

        if (WindowChrome.CapturePlacement(this) is { } placement)
        {
            SettingsManager.Current.MainWindow = placement;
            SettingsManager.Save();
        }
    }
}
