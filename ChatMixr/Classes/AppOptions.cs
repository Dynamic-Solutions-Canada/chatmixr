namespace ChatMixr.Classes;

/// <summary>
/// The options both the tray popup and the Settings page switch (headset auto-switching and the mix
/// shortcuts), in one place: each saves the setting, applies it, and raises <see cref="Changed"/> so
/// the other place shows the same state.
/// </summary>
internal static class AppOptions
{
    /// <summary>Raised on the UI thread after either option changes.</summary>
    public static event Action? Changed;

    /// <summary>Switch devices when a headset turns on or off.</summary>
    public static void SetAutoSwitch(bool on)
    {
        if (SettingsManager.Current.AutoSwitchHeadsets == on)
        {
            return;
        }

        SettingsManager.Current.AutoSwitchHeadsets = on;
        SettingsManager.Save();
        MainWindow.Engine.SetAutoSwitch(on);
        Changed?.Invoke();
    }

    /// <summary>
    /// Turn the mix shortcuts on or off. Returns false when another app already holds one of them.
    /// </summary>
    public static bool SetShortcuts(bool on)
    {
        SettingsManager.Current.MixShortcuts = on;
        SettingsManager.Save();
        bool ok = MainWindow.ApplyShortcuts();
        Changed?.Invoke();
        return ok;
    }
}
