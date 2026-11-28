using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;
using WinUIAppKit.Settings;
using ChatMixr.Classes;
using ChatMixr.Models;
using Microsoft.UI.Xaml.Input;

namespace ChatMixr.Pages;

/// <summary>
/// App-wide settings (the NavigationView's built-in Settings item). The startup, language and theme rows
/// are the kit's shared Settings rows (UI-4); this page saves what they report. Edits apply and save
/// immediately.
/// </summary>
public sealed partial class GeneralPage : Page
{
    public GeneralPage()
    {
        InitializeComponent();

        // Switch labels and help (catalog keys), set here so the catalog check sees them.
        AutoSwitchSwitch.LabelKey = "settings.auto-switch";
        AutoSwitchDevicesSwitch.LabelKey = "settings.auto-switch-devices";
        ShortcutsSwitch.LabelKey = "settings.shortcuts";

        StartupRow.Registration = MainWindow.Startup;
        StartupRow.StatusChanged += isOn =>
        {
            SettingsManager.Current.Startup = isOn;
            SettingsManager.Save();
        };
        LanguageRow.ChoiceChanged += identity =>
        {
            SettingsManager.Current.Language = identity;
            SettingsManager.Save();
        };
        ThemeRow.ChoiceChanged += choice =>
        {
            SettingsManager.Current.AppTheme = choice;
            SettingsManager.Save();
        };

        // The kit's Reset row asks on the main window, then runs the reset below (CONF-4). The window
        // is read once the page is shown, since the page never outlives the window that hosts it.
        Loaded += (_, _) => ResetRow.Window = MixerWindow.Instance;
        ResetRow.ResetAsync = ResetSettingsAsync;
        ResetRow.KeptNote = () => UiText.Get("settings.reset-kept");
        ResetRow.ResetCompleted += MixerWindow.ReloadSettingsPage;

        _settingShortcuts = true;
        AutoSwitchSwitch.IsOn = SettingsManager.Current.AutoSwitchHeadsets;
        AutoSwitchDevicesSwitch.IsOn = SettingsManager.Current.AutoSwitchDevices;
        ShortcutsSwitch.IsOn = SettingsManager.Current.MixShortcuts;
        _settingShortcuts = false;
        ShowShortcuts(null);

        // The popup switches the same options: show its changes here while this page is open.
        AppOptions.Changed += SyncFromOptions;
        Unloaded += (_, _) => AppOptions.Changed -= SyncFromOptions;

        // Shown once, on the launch that found the settings file unreadable (CONF-2, UI-15).
        if (SettingsManager.TakeResetNotice() is { } notice)
        {
            NoticeHost.Content = SettingsResetNotice.Create(notice.BackupPath);
        }
    }

    // ── Headset auto-switch ─────────────────────────────────────────────
    private void AutoSwitchSwitch_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_settingShortcuts)
        {
            return;
        }

        AppOptions.SetAutoSwitch(AutoSwitchSwitch.IsOn);
    }

    // ── Device auto-switch ──────────────────────────────────────────────
    private void AutoSwitchDevicesSwitch_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_settingShortcuts)
        {
            return;
        }

        AppOptions.SetAutoSwitchDevices(AutoSwitchDevicesSwitch.IsOn);
    }

    // ── Mix shortcuts ─────────────────────────────────────────────────
    /// <summary>True while the switches are set in code, so that isn't taken as the user's choice.</summary>
    private bool _settingShortcuts;

    /// <summary>The shortcut button waiting for a key combination, or null.</summary>
    private Button? _recording;

    private void ShortcutsSwitch_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_settingShortcuts)
        {
            return;
        }

        bool ok = AppOptions.SetShortcuts(ShortcutsSwitch.IsOn);
        ShowShortcuts(ShortcutsSwitch.IsOn && !ok ? "settings.shortcuts-taken" : null);
    }

    /// <summary>Show the options as they are now (after the popup changed one).</summary>
    private void SyncFromOptions()
    {
        _settingShortcuts = true;
        AutoSwitchSwitch.IsOn = SettingsManager.Current.AutoSwitchHeadsets;
        AutoSwitchDevicesSwitch.IsOn = SettingsManager.Current.AutoSwitchDevices;
        ShortcutsSwitch.IsOn = SettingsManager.Current.MixShortcuts;
        _settingShortcuts = false;
        ShowShortcuts(null);
    }

    /// <summary>Register the shortcuts as set, then show them and what happened.</summary>
    private void ApplyAndShow()
    {
        bool ok = MainWindow.ApplyShortcuts();
        ShowShortcuts(ShortcutsSwitch.IsOn && !ok ? "settings.shortcuts-taken" : null);
    }

    /// <summary>
    /// Show both shortcuts on their buttons, and the note: <paramref name="problemKey"/> when something
    /// went wrong, otherwise what the shortcuts do.
    /// </summary>
    private void ShowShortcuts(string? problemKey)
    {
        var settings = SettingsManager.Current;
        ChatShortcutButton.Content = settings.ShortcutTowardChat.Describe(MixShortcuts.KeyName);
        GameShortcutButton.Content = settings.ShortcutTowardGame.Describe(MixShortcuts.KeyName);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ChatShortcutButton, UiText.Format("settings.shortcut-chat-name", ("shortcut", ChatShortcutButton.Content)));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(GameShortcutButton, UiText.Format("settings.shortcut-game-name", ("shortcut", GameShortcutButton.Content)));
        ShortcutsNote.Text = UiText.Get(problemKey ?? (ShortcutsSwitch.IsOn ? "settings.shortcuts-on" : "settings.shortcuts-off"));
    }

    /// <summary>Start recording: the button asks for keys, and the shortcuts are released so their keys reach it.</summary>
    private void ShortcutButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _recording = (Button)sender;
        MainWindow.SuspendShortcuts();
        _recording.Content = UiText.Get("settings.shortcut-recording");
        ShortcutsNote.Text = UiText.Get("settings.shortcut-recording-note");
    }

    /// <summary>
    /// While recording: Esc cancels; a modifier alone waits for more; any other key with the modifiers
    /// held becomes the shortcut, if it passes the rules (<see cref="Shortcut.Problem"/>).
    /// </summary>
    private void ShortcutButton_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ReferenceEquals(_recording, sender))
        {
            return;
        }

        e.Handled = true;
        uint key = (uint)e.Key;
        if (e.Key == global::Windows.System.VirtualKey.Escape)
        {
            StopRecording(null);
            return;
        }

        if (Shortcut.IsModifierKey(key))
        {
            return;
        }

        var recorded = new Shortcut(HeldModifiers(), key);
        bool chat = ReferenceEquals(sender, ChatShortcutButton);
        var settings = SettingsManager.Current;
        if (Shortcut.Problem(recorded, chat ? settings.ShortcutTowardGame : settings.ShortcutTowardChat) is ShortcutProblem problem)
        {
            ShortcutsNote.Text = UiText.Get(problem switch
            {
                ShortcutProblem.NoModifier => "settings.shortcut-no-modifier",
                ShortcutProblem.NoKey => "settings.shortcut-no-key",
                _ => "settings.shortcut-same-as-other",
            });
            return;
        }

        if (chat)
        {
            settings.ShortcutTowardChat = recorded;
        }
        else
        {
            settings.ShortcutTowardGame = recorded;
        }

        SettingsManager.Save();
        StopRecording(null);
    }

    /// <summary>Clicking away cancels recording.</summary>
    private void ShortcutButton_LostFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ReferenceEquals(_recording, sender))
        {
            StopRecording(null);
        }
    }

    /// <summary>Restore one shortcut's default.</summary>
    private void ShortcutReset_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var settings = SettingsManager.Current;
        if (ReferenceEquals(sender, ChatShortcutReset))
        {
            settings.ShortcutTowardChat = Shortcut.Problem(Shortcut.DefaultTowardChat, settings.ShortcutTowardGame) == null ? Shortcut.DefaultTowardChat : settings.ShortcutTowardChat;
        }
        else
        {
            settings.ShortcutTowardGame = Shortcut.Problem(Shortcut.DefaultTowardGame, settings.ShortcutTowardChat) == null ? Shortcut.DefaultTowardGame : settings.ShortcutTowardGame;
        }

        SettingsManager.Save();
        StopRecording(null);
    }

    /// <summary>End recording and register the shortcuts again.</summary>
    private void StopRecording(string? problemKey)
    {
        _recording = null;
        ApplyAndShow();
        if (problemKey != null)
        {
            ShortcutsNote.Text = UiText.Get(problemKey);
        }
    }

    /// <summary>The modifier keys held right now, as RegisterHotKey flags.</summary>
    private static uint HeldModifiers()
    {
        static bool Down(global::Windows.System.VirtualKey key) =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);

        uint modifiers = 0;
        if (Down(global::Windows.System.VirtualKey.Control))
        {
            modifiers |= Shortcut.Control;
        }

        if (Down(global::Windows.System.VirtualKey.Menu))
        {
            modifiers |= Shortcut.Alt;
        }

        if (Down(global::Windows.System.VirtualKey.Shift))
        {
            modifiers |= Shortcut.Shift;
        }

        if (Down(global::Windows.System.VirtualKey.LeftWindows) || Down(global::Windows.System.VirtualKey.RightWindows))
        {
            modifiers |= Shortcut.Win;
        }

        return modifiers;
    }

    // ── Reset ───────────────────────────────────────────────────────────
    /// <summary>
    /// The app's part of "Reset settings", run by the kit's row once the user confirms: reset the store
    /// (which keeps the old file, applies the default theme and language, and tells the engine and the
    /// popup through <see cref="SettingsManager.WasReset"/>). The row then raises ResetCompleted, which opens this page
    /// again so every row shows the defaults. Start with Windows is Windows' state and is left alone.
    /// </summary>
    private static Task ResetSettingsAsync()
    {
        SettingsManager.Reset();
        return Task.CompletedTask;
    }
}
