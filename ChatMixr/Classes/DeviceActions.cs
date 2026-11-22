using ChatMixr.Models;

namespace ChatMixr.Classes;

/// <summary>
/// What the device list's controls do, in one place for the popup and the main window: switch
/// devices, move a device's slider, in-game voice chat, test, reorder, hide and show devices. Each saves the setting it
/// changes and tells the mix engine (or, for hiding, every list through <see cref="HiddenChanged"/>).
/// </summary>
internal static class DeviceActions
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// "Switch to", or one of its menu's entries: move games and media, calls or the microphone to the
    /// device, as <see cref="DefaultRoles"/> describes for <paramref name="scope"/>.
    /// </summary>
    public static void SwitchTo(string deviceId, SwitchScope scope) => MainWindow.Engine.SwitchTo(deviceId, scope);

    /// <summary>A device's slider moved. Saved shortly after the last movement, applied at once.</summary>
    public static void SetSlider(string deviceId, int position)
    {
        if (position == 0)
        {
            SettingsManager.Current.SliderPositions.Remove(deviceId);
        }
        else
        {
            SettingsManager.Current.SliderPositions[deviceId] = position;
        }

        SettingsManager.SaveSoon();
        MainWindow.Engine.SetSliderPosition(deviceId, position);
    }

    /// <summary>
    /// A mix shortcut was pressed: move the device in use one step toward chat (+1) or game (-1), and
    /// show the mix popup with the new position. A wheel device's wheel sets its mix, so there the press
    /// only shows where the wheel is.
    /// </summary>
    public static void StepInUse(int direction)
    {
        var inUse = MainWindow.Engine.Snapshot.Devices.FirstOrDefault(d => d.IsDefault);
        if (inUse == null)
        {
            Logger.Info("Shortcut ignored: no device in use");
            return;
        }

        if (inUse.HasWheel)
        {
            Logger.Info("Shortcut: {Device} has a ChatMix wheel, which sets its mix; showing it", inUse.Name);
            MainWindow.ShowMix(inUse);
            return;
        }

        int current = SettingsManager.Current.SliderPositions.GetValueOrDefault(inUse.Id);
        int next = SliderMix.Step(current, direction);
        Logger.Info("Shortcut: {Device} slider {From} to {To}", inUse.Name, current, next);
        if (next != current)
        {
            SetSlider(inUse.Id, next);
        }

        // Shown from the levels just set, not from the engine's next snapshot, which arrives a moment
        // later (and not at all when nothing changed).
        MainWindow.ShowMix(inUse with { Levels = SliderMix.LevelsAt(next) });
    }

    /// <summary>The user dragged the devices into a new order.</summary>
    /// <param name="presentOrder">Every listed device's id, in the new order.</param>
    public static void Reorder(IReadOnlyList<string> presentOrder)
    {
        var saved = DeviceOrder.Save(presentOrder, SettingsManager.Current.DeviceOrder);
        SettingsManager.Current.DeviceOrder = saved;
        SettingsManager.Save();
        MainWindow.Engine.SetDeviceOrder(saved);
    }

    /// <summary>Move one device up (-1) or down (+1) among the visible devices.</summary>
    public static void Move(string deviceId, int step)
    {
        var visible = DeviceList.Visible(MainWindow.Engine.Snapshot).Select(d => d.Id).ToList();
        Reorder(DeviceOrder.Move(visible, deviceId, step));
    }

    /// <summary>Turn in-game voice chat (through the virtual cable) on or off, for every device.</summary>
    public static void SetInGameVoiceChat(bool on)
    {
        if (SettingsManager.Current.InGameVoiceChat == on)
        {
            return;
        }

        SettingsManager.Current.InGameVoiceChat = on;
        SettingsManager.Save();
        MainWindow.Engine.SetSeparateChat(on);
        InGameVoiceChatChanged?.Invoke();
    }

    /// <summary>Raised on the UI thread when in-game voice chat is turned on or off, so every switch shows it.</summary>
    public static event Action? InGameVoiceChatChanged;

    /// <summary>Test: start the test sounds on a device, or stop them if that device is already testing.</summary>
    public static void ToggleTest(string deviceId)
    {
        if (string.Equals(MainWindow.Engine.Snapshot.TestingDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            MainWindow.Engine.StopTest();
        }
        else
        {
            MainWindow.Engine.StartTest(deviceId);
        }
    }

    /// <summary>True when the user hid the device from the lists.</summary>
    public static bool IsHidden(string deviceId) =>
        SettingsManager.Current.HiddenDevices.Contains(deviceId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Hide a device from the lists. It is still mixed at its slider's position.</summary>
    public static void Hide(string deviceId)
    {
        if (!IsHidden(deviceId))
        {
            SettingsManager.Current.HiddenDevices.Add(deviceId);
            SettingsManager.Save();
            HiddenChanged?.Invoke();
        }
    }

    /// <summary>Bring a hidden device back to the lists.</summary>
    public static void Show(string deviceId)
    {
        SettingsManager.Current.HiddenDevices.RemoveAll(id => string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase));
        SettingsManager.Save();
        HiddenChanged?.Invoke();
    }

    /// <summary>Reset settings showed every hidden device again: redraw the lists.</summary>
    public static void SettingsWereReset() => HiddenChanged?.Invoke();

    /// <summary>Raised on the UI thread when a device is hidden or shown, so every list redraws.</summary>
    public static event Action? HiddenChanged;
}
