using System.IO.Pipes;
using System.Text;
using ChatMixr.Models;
using Windows.System;

namespace ChatMixr.GameBar;

/// <summary>
/// The widget's end of the pipe to the running ChatMixr (<see cref="WidgetPipe"/>): connects, keeps
/// trying while ChatMixr isn't running, raises each state it sends, and sends the user's commands.
/// Events are raised on the thread whose <see cref="DispatcherQueue"/> is passed in (the page's).
/// </summary>
internal sealed class WidgetClient : IDisposable
{
    /// <summary>How long one connection attempt waits for the pipe.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The pause between attempts while ChatMixr isn't running.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly DispatcherQueue _ui;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writing = new(1, 1);
    private NamedPipeClientStream? _pipe;

    /// <summary>Raised with each state ChatMixr sends.</summary>
    public event Action<WidgetState>? StateReceived;

    /// <summary>Raised when the connection opens (true) or is lost (false).</summary>
    public event Action<bool>? ConnectionChanged;

    /// <param name="ui">The queue events are raised on.</param>
    public WidgetClient(DispatcherQueue ui)
    {
        _ui = ui;
    }

    /// <summary>Start connecting, and keep reconnecting until disposed.</summary>
    public void Start() => _ = Task.Run(() => RunAsync(_stop.Token));

    /// <summary>Send a command; dropped when not connected (the controls show that state).</summary>
    public async Task SendAsync(WidgetCommand command)
    {
        var pipe = _pipe;
        if (pipe is not { IsConnected: true })
        {
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(WidgetPipe.Encode(command) + "\n");
        await _writing.WaitAsync();
        try
        {
            await pipe.WriteAsync(bytes);
            await pipe.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The read loop sees the broken pipe and reconnects.
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>Stop and close the connection.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _pipe?.Dispose();
    }

    /// <summary>Connect, read until the connection ends, wait, and go again, until stopped.</summary>
    private async Task RunAsync(CancellationToken stop)
    {
        string name = WidgetPipe.Name(System.Diagnostics.Process.GetCurrentProcess().SessionId);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, stop);
                _pipe = pipe;
                Raise(() => ConnectionChanged?.Invoke(true));
                await ReadAsync(pipe, stop);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // ChatMixr isn't running, exited, or the widget is closing.
            }

            if (_pipe != null)
            {
                _pipe = null;
                Raise(() => ConnectionChanged?.Invoke(false));
            }

            try
            {
                await Task.Delay(RetryDelay, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Raise each state line until the pipe closes; a line that isn't a state ends the connection.</summary>
    private async Task ReadAsync(NamedPipeClientStream pipe, CancellationToken stop)
    {
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        while (await reader.ReadLineAsync(stop) is { } line)
        {
            if (WidgetPipe.DecodeState(line) is not { } state)
            {
                return;
            }

            Raise(() => StateReceived?.Invoke(state));
        }
    }

    private void Raise(Action action)
    {
        _ui.TryEnqueue(() =>
        {
            // An exception here would reach Windows' dispatcher, which ends the whole package, ChatMixr
            // included; it never reaches Application.UnhandledException. Log it and keep the widget up.
            try
            {
                action();
            }
            catch (Exception ex)
            {
                WidgetLog.Error("showing ChatMixr's state", ex);
            }
        });
    }
}
