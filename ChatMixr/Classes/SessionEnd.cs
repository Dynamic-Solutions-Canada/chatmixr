using static WinUIAppKit.Interop.NativeMethods;

namespace ChatMixr.Classes;

/// <summary>
/// Hears Windows ending the session (shut down, restart, sign out) on the resident host window and
/// raises <see cref="Ending"/>, so the app's exit steps run then too. Without it, a shutdown ends the
/// process without them: apps stay turned down until ChatMixr next starts, and calls stay on the
/// virtual cable, silent if ChatMixr doesn't start with Windows. The kit's host doesn't handle this
/// yet; this subclass sits alongside its own until it does.
/// </summary>
internal sealed class SessionEnd : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>This subclass's id on the host window; the kit's own and the shortcuts' use others.</summary>
    private static readonly UIntPtr SubclassId = 0x43_4D_45;

    private const uint WM_ENDSESSION = 0x0016;

    private readonly IntPtr _hwnd;
    private readonly SUBCLASSPROC _wndProc;

    /// <summary>
    /// Raised on the UI thread when the session is really ending. Windows waits for the handler to
    /// return (a few seconds at most) before ending the process, so the work must be quick.
    /// </summary>
    public event Action? Ending;

    /// <param name="hwnd">The resident host window, which lives as long as the app.</param>
    public SessionEnd(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _wndProc = WndProc;   // kept in a field so the delegate isn't collected while Windows holds it
        SetWindowSubclass(_hwnd, _wndProc, SubclassId, 0);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        // WM_ENDSESSION with wParam TRUE: the session ends once every app has answered. FALSE means a
        // shutdown that was cancelled, which needs nothing.
        if (msg == WM_ENDSESSION && wParam != IntPtr.Zero)
        {
            Logger.Info("Windows is ending the session; running the exit steps");
            Ending?.Invoke();
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose() => RemoveWindowSubclass(_hwnd, _wndProc, SubclassId);
}
