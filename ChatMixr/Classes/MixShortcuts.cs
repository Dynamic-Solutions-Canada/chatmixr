using ChatMixr.Models;
using static ChatMixr.Classes.NativeMethods;
using static WinUIAppKit.Interop.NativeMethods;

namespace ChatMixr.Classes;

/// <summary>
/// The optional global keyboard shortcuts for the mix: one moves the device in use toward chat, the
/// other toward game (Ctrl+Shift+Plus and Minus unless the user records others). Off by default:
/// while registered, Windows gives these combinations to ChatMixr instead of the app in front.
/// Registered on the resident host window and heard through a subclass of it, alongside the kit's own.
/// </summary>
internal sealed class MixShortcuts : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>This subclass's id on the host window; the kit's own subclasses use others.</summary>
    private static readonly UIntPtr SubclassId = 0x43_4D_58;

    /// <summary>The hotkey ids: one per direction.</summary>
    private const int TowardChatId = 0x4D01, TowardGameId = 0x4D02;

    private readonly IntPtr _hwnd;
    private readonly SUBCLASSPROC _wndProc;
    private bool _registered;

    /// <summary>Raised on the UI thread with +1 (toward chat) or -1 (toward game) for each press.</summary>
    public event Action<int>? Pressed;

    /// <param name="hwnd">The resident host window, which lives as long as the app.</param>
    public MixShortcuts(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _wndProc = WndProc;   // kept in a field so the delegate isn't collected while Windows holds it
        SetWindowSubclass(_hwnd, _wndProc, SubclassId, 0);
    }

    /// <summary>
    /// Register the two shortcuts (replacing any registered before), or none when <paramref name="on"/>
    /// is false. Returns false when another app already holds one of them; the other stays registered.
    /// </summary>
    public bool Apply(bool on, Shortcut towardChat, Shortcut towardGame)
    {
        Unregister();
        if (!on)
        {
            return true;
        }

        bool chat = Register(TowardChatId, towardChat);
        bool game = Register(TowardGameId, towardGame);
        _registered = true;
        return chat && game;
    }

    /// <summary>Release both shortcuts, for example while the user records a new one.</summary>
    public void Suspend() => Unregister();

    private bool Register(int id, Shortcut shortcut)
    {
        if (RegisterHotKey(_hwnd, id, shortcut.Modifiers | MOD_NOREPEAT, shortcut.Key))
        {
            Logger.Info("Shortcut {Shortcut} registered", shortcut.Describe(KeyName));
            return true;
        }

        Logger.Warn("Shortcut {Shortcut} is taken by another app", shortcut.Describe(KeyName));
        return false;
    }

    private void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(_hwnd, TowardChatId);
        UnregisterHotKey(_hwnd, TowardGameId);
        _registered = false;
    }

    /// <summary>The keyboard's own name for a key, in the current layout ("=", "F6", "Num +").</summary>
    public static string KeyName(uint key)
    {
        uint scan = MapVirtualKey(key, MAPVK_VK_TO_VSC);
        bool extended = key is >= 0x21 and <= 0x2E or 0x6F or >= 0x5B and <= 0x5D;   // navigation keys, numpad /, Windows keys
        int lParam = (int)(scan << 16) | (extended ? 1 << 24 : 0);
        var name = new System.Text.StringBuilder(64);
        return GetKeyNameText(lParam, name, name.Capacity) > 0 ? name.ToString() : $"0x{key:X2}";
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() is TowardChatId or TowardGameId)
        {
            Logger.Info("Shortcut pressed: {Direction}", wParam.ToInt32() == TowardChatId ? "toward chat" : "toward game");
            Pressed?.Invoke(wParam.ToInt32() == TowardChatId ? +1 : -1);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Unregister();
        RemoveWindowSubclass(_hwnd, _wndProc, SubclassId);
    }
}
