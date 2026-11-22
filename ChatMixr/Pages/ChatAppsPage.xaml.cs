using ChatMixr.Classes;
using ChatMixr.Models;
using ChatMixr.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIAppKit.Localization;

namespace ChatMixr.Pages;

/// <summary>
/// The chat app list. A search box finds installed apps by name (the way Little Launcher does) and
/// adds the program they run; each chat app is a card with Remove; and a Suggested section offers the
/// presets that were removed and the apps ChatMixr has heard, each with Add, plus "Add all" for the
/// presets.
/// Edits save and apply at once.
/// </summary>
public sealed partial class ChatAppsPage : LivePage
{
    private const string ChatGlyph = "";

    /// <summary>Installed apps, once loaded; empty until then.</summary>
    private IReadOnlyList<InstalledApp> _installed = [];

    /// <summary>The apps behind the search box's current suggestions, in the same order.</summary>
    private List<InstalledApp> _results = [];

    private bool _loading = true;


    public ChatAppsPage()
    {
        InitializeComponent();

        // Switch labels and help (catalog keys), set here so the catalog check sees them.
        InGameChatSwitch.LabelKey = "in-game-chat.label";
        InGameChatSwitch.HelpKey = "in-game-chat.help";
        RenameCableSwitch.LabelKey = "cable-ready.rename";
    }

    protected override void OnAttach()
    {
        MainWindow.Engine.Changed += RefreshSuggested;
        MainWindow.Engine.Changed += RefreshChatDevice;
        DeviceActions.InGameVoiceChatChanged += RefreshChatDevice;
        UiText.Changed += RefreshAll;
        RefreshAll();
        _ = LoadInstalledAppsAsync();
    }

    protected override void OnDetach()
    {
        MainWindow.Engine.Changed -= RefreshSuggested;
        MainWindow.Engine.Changed -= RefreshChatDevice;
        DeviceActions.InGameVoiceChatChanged -= RefreshChatDevice;
        UiText.Changed -= RefreshAll;
    }

    /// <summary>Load the installed apps for the search (once per session), then refresh what uses them.</summary>
    private async Task LoadInstalledAppsAsync()
    {
        _installed = await InstalledApps.GetAsync();
        _loading = false;
        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshList();
        RefreshSuggested();
        RefreshChatDevice();
        SearchNote.Text = UiText.Get(_loading ? "chat-apps.search-loading" : "chat-apps.search-note");
    }

    /// <summary>The name an app is shown with: its preset or installed name, else its program name.</summary>
    private string DisplayName(string program) =>
        ChatApps.PresetName(program)
        ?? _installed.FirstOrDefault(a => string.Equals(a.Program, program, StringComparison.OrdinalIgnoreCase))?.Name
        ?? program;

    // ── The chat list ───────────────────────────────────────────────────
    private void RefreshList()
    {
        AppsPanel.Children.Clear();
        var apps = SettingsManager.Current.ChatApps;
        if (apps.Count == 0)
        {
            AppsPanel.Children.Add(EmptyState.Create(UiText.Get("chat-apps.empty")));
            return;
        }

        foreach (string program in apps.OrderBy(DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            string name = DisplayName(program);
            var remove = CardRow.IconButton(
                "",
                UiText.Format("chat-apps.remove-name", ("app", name)),
                UiText.Get("chat-apps.remove"),
                () => Remove(program));
            AppsPanel.Children.Add(new CardRow(ChatGlyph, name, ProgramCaption(program)).WithAction(remove));
        }
    }

    /// <summary>"Discord.exe": the program the card matches, so it is clear what is being listened for.</summary>
    private static string ProgramCaption(string program) => UiText.Format("chat-apps.program", ("program", program));

    // ── Suggested ───────────────────────────────────────────────────────
    /// <summary>
    /// Removed presets first, then programs ChatMixr has heard (now or before, most recent first)
    /// that aren't chat apps, so a game's or app's chat can be picked without searching for it.
    /// </summary>
    private void RefreshSuggested()
    {
        SuggestedPanel.Children.Clear();
        var apps = SettingsManager.Current.ChatApps;
        var presets = ChatApps.MissingPresets(apps);
        foreach (var (program, name) in presets)
        {
            SuggestedPanel.Children.Add(SuggestionRow(program, name, UiText.Get("chat-apps.from-preset")));
        }

        var playingNow = MainWindow.Engine.Snapshot.PlayingPrograms;
        var heard = SettingsManager.Current.RecentPrograms
            .Where(p => !ChatApps.IsChat(p, apps) && ChatApps.PresetName(p) == null)
            .ToList();
        foreach (string program in heard)
        {
            bool now = playingNow.Contains(program, StringComparer.OrdinalIgnoreCase);
            string why = UiText.Get(now ? "chat-apps.from-playing" : "chat-apps.from-heard");
            SuggestedPanel.Children.Add(SuggestionRow(program, DisplayName(program), why));
        }

        AddAllButton.Visibility = presets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        SuggestedSection.Visibility = SuggestedPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement SuggestionRow(string program, string name, string why)
    {
        var add = CardRow.TextButton(
            UiText.Get("chat-apps.add"),
            UiText.Format("chat-apps.add-name", ("app", name)),
            null,
            () => Add(program));
        string caption = UiText.Format("chat-apps.suggestion-caption", ("why", why), ("program", ProgramCaption(program)));
        return new CardRow(ChatGlyph, name, caption).WithAction(add);
    }

    private void AddAll_Click(object sender, RoutedEventArgs e)
    {
        var apps = SettingsManager.Current.ChatApps;
        apps.AddRange(ChatApps.MissingPresets(apps).Select(p => p.Program));
        SaveAndApply();
    }

    // ── Search ──────────────────────────────────────────────────────────
    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _results = AppSearch.Find(_installed, sender.Text, SettingsManager.Current.ChatApps);
        sender.ItemsSource = _results
            .Select(a => UiText.Format("chat-apps.search-result", ("app", a.Name), ("program", a.Program)))
            .ToList();
    }

    /// <summary>
    /// Enter or a picked suggestion: add the picked app, else the best match, else what was typed as a
    /// program name (for a program that isn't installed the usual way).
    /// </summary>
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string? program = null;
        if (args.ChosenSuggestion is string chosen && sender.ItemsSource is List<string> shown)
        {
            int index = shown.IndexOf(chosen);
            program = index >= 0 && index < _results.Count ? _results[index].Program : null;
        }

        program ??= _results.FirstOrDefault()?.Program;
        program ??= ChatApps.Normalize(args.QueryText);
        if (program.Length > 0)
        {
            Add(program);
        }

        sender.Text = "";
        sender.ItemsSource = null;
        _results = [];
    }

    // ── Changes ─────────────────────────────────────────────────────────
    private void Add(string program)
    {
        var apps = SettingsManager.Current.ChatApps;
        if (!ChatApps.IsChat(program, apps))
        {
            apps.Add(ChatApps.Normalize(program));
            SaveAndApply();
        }
    }

    private void Remove(string program)
    {
        SettingsManager.Current.ChatApps.RemoveAll(a => string.Equals(ChatApps.Normalize(a), ChatApps.Normalize(program), StringComparison.OrdinalIgnoreCase));
        SaveAndApply();
    }

    private void SaveAndApply()
    {
        SettingsManager.Save();
        MainWindow.Engine.SetChatApps(SettingsManager.Current.ChatApps);
        RefreshList();
        RefreshSuggested();
    }

    // ── Game voice chat (Separate chat) ─────────────────────────────────
    /// <summary>
    /// No cable yet: the guided VB-Cable steps. A cable installed: where to turn Separate chat on.
    /// The cable itself is never shown as a choice; ChatMixr manages it.
    /// </summary>
    private void RefreshChatDevice()
    {
        var snapshot = MainWindow.Engine.Snapshot;
        bool cable = snapshot.CableName != null;
        CableSteps.Visibility = cable ? Visibility.Collapsed : Visibility.Visible;
        CableReady.Visibility = cable ? Visibility.Visible : Visibility.Collapsed;
        // While the virtual chat device is on, the one thing to do is pick it in games and chat apps,
        // and the chat app list no longer applies, so it is hidden.
        bool virtualChat = cable && SettingsManager.Current.InGameVoiceChat;
        CableExplicitText.Text = virtualChat ? MixVisuals.VirtualChatInstruction(snapshot.CableName!) : "";
        CableExplicitText.Visibility = virtualChat ? Visibility.Visible : Visibility.Collapsed;
        ChatListSection.Visibility = virtualChat ? Visibility.Collapsed : Visibility.Visible;
        ChatListOffNote.Visibility = virtualChat ? Visibility.Visible : Visibility.Collapsed;

        _settingRename = true;
        InGameChatSwitch.IsOn = SettingsManager.Current.InGameVoiceChat;
        _settingRename = false;

        MicrophoneHost.Content = MicrophoneWarning.Create(snapshot);
        _settingRename = true;
        RenameCableSwitch.IsOn = SettingsManager.Current.RenameCable;
        _settingRename = false;
    }

    /// <summary>True while the switches are set in code, so that isn't taken as the user's choice.</summary>
    private bool _settingRename;

    private void InGameChatSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingRename)
        {
            DeviceActions.SetInGameVoiceChat(InGameChatSwitch.IsOn);
        }
    }

    /// <summary>ChatMixr's names for the cable on, or VB-Cable's own names back.</summary>
    private void RenameCableSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingRename || RenameCableSwitch.IsOn == SettingsManager.Current.RenameCable)
        {
            return;
        }

        SettingsManager.Current.RenameCable = RenameCableSwitch.IsOn;
        SettingsManager.Save();
        MainWindow.Engine.SetRenameCable(RenameCableSwitch.IsOn, SettingsManager.Current.CableOriginalNames, SettingsManager.Current.HiddenCableEndpoints);
        if (!RenameCableSwitch.IsOn)
        {
            SettingsManager.Current.HiddenCableEndpoints.Clear();
            SettingsManager.Save();
        }
    }

    private async void OpenCablePage_Click(object sender, RoutedEventArgs e) => await ChatDeviceSetup.OpenDownloadPageAsync();

    private async void Restart_Click(object sender, RoutedEventArgs e) => await ChatDeviceSetup.RestartAsync();
}
