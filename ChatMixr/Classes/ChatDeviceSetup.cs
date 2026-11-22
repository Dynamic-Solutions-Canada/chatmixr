using ChatMixr.Models;
using WinUIAppKit.Localization;
using WinUIAppKit.Windowing;

namespace ChatMixr.Classes;

/// <summary>
/// The guided VB-Cable setup on the Chat apps page. VB-Cable's licence doesn't allow building its
/// installer into another app's installation, so ChatMixr doesn't download or run it: it opens VB-Audio's
/// page, says what to run, offers the restart the driver needs, and when a cable appears turns on
/// in-game voice chat (<see cref="OnSnapshotChanged"/>, also after the restart).
/// </summary>
internal static class ChatDeviceSetup
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>VB-Audio's own VB-Cable page, where the download is.</summary>
    public static readonly Uri CablePage = new("https://vb-audio.com/Cable/");

    /// <summary>Step 1: open VB-Audio's page, and remember that setup started.</summary>
    public static async Task OpenDownloadPageAsync()
    {
        SettingsManager.Current.CableSetupStarted = true;
        SettingsManager.Save();
        await global::Windows.System.Launcher.LaunchUriAsync(CablePage);
    }

    /// <summary>
    /// Step 3: restart Windows, after asking on the main window. The driver only works after one.
    /// </summary>
    public static async Task RestartAsync()
    {
        if (MixerWindow.Instance is not { } window)
        {
            return;
        }

        bool restart = await ConfirmDialog.ConfirmAsync(window,
            UiText.Get("cable-setup.restart-title"),
            UiText.Get("cable-setup.restart-body"),
            UiText.Get("cable-setup.restart-confirm"));
        if (!restart)
        {
            return;
        }

        Logger.Info("Restarting Windows to finish installing VB-Cable");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /t 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    /// <summary>
    /// After setup started: as soon as a cable appears, turn on in-game voice chat, so the setup ends
    /// with it working. Called on the UI thread on every engine change.
    /// </summary>
    public static void OnSnapshotChanged(MixSnapshot snapshot)
    {
        var settings = SettingsManager.Current;
        if (!settings.CableSetupStarted || snapshot.CableName == null)
        {
            return;
        }

        Logger.Info("{Cable} found after setup: in-game voice chat on", snapshot.CableName);
        settings.CableSetupStarted = false;
        SettingsManager.Save();
        DeviceActions.SetInGameVoiceChat(true);
    }
}
