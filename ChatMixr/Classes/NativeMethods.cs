using System.Runtime.InteropServices;

namespace ChatMixr.Classes;

/// <summary>
/// Win32 declarations ChatMixr needs that the app kit doesn't have (global keyboard shortcuts, the
/// keyboard's names for keys, and the fading mix popup).
/// Everything else comes from <c>WinUIAppKit.Interop.NativeMethods</c>; files import both with
/// <c>using static</c>, so never add a name the kit already declares. Audio and HID interop lives
/// with its service in <c>Services/</c>.
/// </summary>
internal static class NativeMethods
{
    public const uint WM_HOTKEY = 0x0312;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint MAPVK_VK_TO_VSC = 0;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint LWA_ALPHA = 0x2;

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetKeyNameText(int lParam, System.Text.StringBuilder name, int size);
}
