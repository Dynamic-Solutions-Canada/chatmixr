using Windows.Storage;

namespace ChatMixr.GameBar;

/// <summary>
/// The widget's log: <c>widget.log</c> in the package's local folder, kept short. The widget has no
/// logging library (it is a small Native AOT app), and Game Bar shows only "Something isn't playing
/// nice" or "This is taking longer than expected" when it fails, so this is the only record of why: its
/// errors, and each step of opening. Never throws.
/// </summary>
internal static class WidgetLog
{
    /// <summary>Above this size the log is started afresh, so it can't grow without limit.</summary>
    private const long MaxBytes = 256 * 1024;

    private static readonly Lock Gate = new();

    /// <summary>Record a step, such as how Game Bar activated the widget.</summary>
    public static void Info(string message) => Write("INFO", message);

    /// <summary>Record an error with what the widget was doing.</summary>
    /// <param name="doing">What failed, in a few words (for example "start-up").</param>
    /// <param name="error">The exception, written with its stack and inner exceptions.</param>
    public static void Error(string doing, Exception error) => Write("ERROR", $"{doing}: {error}");

    /// <summary>Append one line, starting afresh past <see cref="MaxBytes"/>.</summary>
    private static void Write(string level, string message)
    {
        try
        {
            string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "widget.log");
            string entry = $"{DateTimeOffset.UtcNow:O} {level} {message}{Environment.NewLine}";
            lock (Gate)
            {
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, entry);
            }
        }
        catch
        {
            // Logging must never be the reason the widget fails.
        }
    }
}
