using ChatMixr.Classes;
using WinUIAppKit.Localization;

namespace ChatMixr.Pages;

/// <summary>
/// Every playback device, the same list as the tray popup plus Move up, Move down and Hide buttons,
/// the hidden devices with Show, a warning while SteelSeries Sonar runs, and a short explanation of
/// how mixing works.
/// </summary>
public sealed partial class DevicesPage : LivePage
{
    public DevicesPage()
    {
        InitializeComponent();
        DeviceList.Attach(Devices);
    }

    protected override void OnAttach()
    {
        MainWindow.Engine.Changed += Refresh;
        UiText.Changed += Refresh;
        DeviceActions.HiddenChanged += Refresh;
        Refresh();
    }

    protected override void OnDetach()
    {
        MainWindow.Engine.Changed -= Refresh;
        UiText.Changed -= Refresh;
        DeviceActions.HiddenChanged -= Refresh;
    }

    private void Refresh()
    {
        var snapshot = MainWindow.Engine.Snapshot;
        DeviceList.Fill(Devices, snapshot, withPageButtons: true);
        FillHidden(snapshot);

        // Nothing to list: say why in the list's place.
        string? empty = snapshot.Devices.Count == 0
            ? MixVisuals.StatusLine(snapshot)
            : DeviceList.Visible(snapshot).Count == 0 ? UiText.Get("devices.all-hidden") : null;
        EmptyHost.Content = empty != null ? EmptyState.Create(empty) : null;

        SonarHost.Content = snapshot.SonarRunning && snapshot.Devices.Any(d => d.HasWheel)
            ? EmptyState.Create(UiText.Get("sonar.warning"))
            : null;
    }

    /// <summary>One row per hidden device that is connected now, with its mix and Show.</summary>
    private void FillHidden(Models.MixSnapshot snapshot)
    {
        HiddenPanel.Children.Clear();
        var hidden = snapshot.Devices.Where(d => DeviceActions.IsHidden(d.Id)).ToList();
        foreach (var device in hidden)
        {
            var show = CardRow.TextButton(
                UiText.Get("device.show"),
                UiText.Format("device.show-name", ("device", device.Name)),
                null,
                () => DeviceActions.Show(device.Id));

            // A hidden device keeps its mix with no slider on screen, so its caption says where it is.
            var levels = device.Levels ?? new Models.MixLevels(100, 100);
            string caption = levels is { Game: 100, Chat: 100 }
                ? UiText.Get("devices.hidden-centred")
                : UiText.Format("slider.levels", ("game", levels.Game), ("chat", levels.Chat));
            HiddenPanel.Children.Add(new CardRow(MixVisuals.Glyph(device.Kind), device.Name, caption).WithAction(show));
        }

        HiddenSection.Visibility = hidden.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
