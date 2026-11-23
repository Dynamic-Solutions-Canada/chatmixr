using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using ChatMixr.Models;
using Microsoft.UI.Dispatching;
using Microsoft.Win32.SafeHandles;

namespace ChatMixr.Services;

/// <summary>
/// Serves the Game Bar widget (<c>ChatMixr.GameBar</c>), which runs sandboxed in ChatMixr's own
/// package and can't touch audio: a named pipe that sends it the latest <see cref="WidgetState"/>
/// and takes its <see cref="WidgetCommand"/>s.
/// <para>
/// Who can connect: the pipe's access list allows only the signed-in user, and among sandboxed apps
/// only this package (its AppContainer SID), and it carries a low integrity label so the widget, which
/// runs at low integrity, may write to it. Any of the user's own unsandboxed programs could connect
/// too; they could already change the user's audio themselves.
/// </para>
/// Pipe work runs on the thread pool; commands are raised on the UI thread.
/// </summary>
internal sealed class WidgetServer : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>How many widgets may be connected at once (Game Bar shows one; a reopen can briefly overlap).</summary>
    private const int MaxClients = 4;

    private readonly DispatcherQueue _ui;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Client> _clients = [];
    private readonly Lock _gate = new();
    private string? _latest;

    /// <summary>Raised on the UI thread for each command a widget sends.</summary>
    public event Action<WidgetCommand>? CommandReceived;

    /// <summary>Create the server; it serves nothing until <see cref="Start"/>.</summary>
    /// <param name="ui">The UI thread's queue, where commands are raised.</param>
    public WidgetServer(DispatcherQueue ui)
    {
        _ui = ui;
    }

    /// <summary>
    /// Start listening. Only a packaged ChatMixr has a widget (it ships in the package), so an
    /// unpackaged run skips it.
    /// </summary>
    /// <param name="packageFamilyName">This package's family name, or null when unpackaged.</param>
    public void Start(string? packageFamilyName)
    {
        if (packageFamilyName == null)
        {
            Logger.Info("Game Bar widget: not packaged, so no widget to serve");
            return;
        }

        // The widget is an extra: nothing here may stop the tray app from starting, so any failure
        // only leaves the widget saying ChatMixr isn't running.
        try
        {
            string security = Native.SecurityFor(packageFamilyName);
            string name = WidgetPipe.Name(System.Diagnostics.Process.GetCurrentProcess().SessionId);
            Logger.Info("Game Bar widget: serving {Pipe}", name);
            _ = Task.Run(() => AcceptLoopAsync(name, security, _stop.Token));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Game Bar widget: could not start serving; the widget won't connect");
        }
    }

    /// <summary>
    /// Send a new state to every connected widget, and to widgets that connect later. Called on the
    /// UI thread; does nothing when the state is the same as the last one sent.
    /// </summary>
    public void Publish(WidgetState state)
    {
        string line = WidgetPipe.Encode(state);
        Client[] clients;
        lock (_gate)
        {
            if (line == _latest)
            {
                return;
            }

            _latest = line;
            clients = [.. _clients];
        }

        foreach (var client in clients)
        {
            client.Send(line);
        }
    }

    /// <summary>Stop listening and drop every widget. Part of the exit steps.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        Client[] clients;
        lock (_gate)
        {
            clients = [.. _clients];
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    /// <summary>Wait for widgets, one pipe instance at a time, until stopped.</summary>
    private async Task AcceptLoopAsync(string name, string security, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            // With no widget connected this is the only instance, so it is created as the first one:
            // that fails if another process already holds the name, instead of sharing it.
            bool first;
            lock (_gate)
            {
                first = _clients.Count == 0;
            }

            NamedPipeServerStream pipe;
            try
            {
                pipe = Native.CreatePipe(name, security, MaxClients, first);
            }
            catch (Win32Exception ex)
            {
                // All instances busy, or the name is held by something else: try again shortly.
                Logger.Warn(ex, "Game Bar widget: could not open the pipe; retrying");
                await DelayAsync(TimeSpan.FromSeconds(5), stop);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stop);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                await pipe.DisposeAsync();
                continue;
            }

            Logger.Info("Game Bar widget connected");
            // Joined and given the latest state under the lock, so a Publish can't slip a newer state
            // in between and then be overwritten by this older one.
            var client = new Client(pipe);
            lock (_gate)
            {
                _clients.Add(client);
                if (_latest != null)
                {
                    client.Send(_latest);
                }
            }

            _ = Task.Run(() => ReadLoopAsync(client, stop));
        }
    }

    /// <summary>Read one widget's commands until it goes away, raising each on the UI thread.</summary>
    private async Task ReadLoopAsync(Client client, CancellationToken stop)
    {
        try
        {
            using var reader = new StreamReader(client.Pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            while (!stop.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(stop);
                if (line == null)
                {
                    break;
                }

                if (WidgetPipe.DecodeCommand(line) is not { } command)
                {
                    Logger.Warn("Game Bar widget sent something that isn't a command; dropping it");
                    break;
                }

                _ui.TryEnqueue(() => CommandReceived?.Invoke(command));
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The widget closed or the app is exiting.
        }

        lock (_gate)
        {
            _clients.Remove(client);
        }

        client.Dispose();
        Logger.Info("Game Bar widget disconnected");
    }

    /// <summary>Wait, ending early (without throwing) when stopped.</summary>
    private static async Task DelayAsync(TimeSpan delay, CancellationToken stop)
    {
        try
        {
            await Task.Delay(delay, stop);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// One connected widget. Writes go through one queue so lines never interleave, and a slow
    /// widget only ever has the newest state waiting.
    /// </summary>
    private sealed class Client(NamedPipeServerStream pipe) : IDisposable
    {
        private readonly SemaphoreSlim _writing = new(1, 1);
        private string? _pending;

        public NamedPipeServerStream Pipe { get; } = pipe;

        /// <summary>Queue a line; replaces one still waiting.</summary>
        public void Send(string line)
        {
            Interlocked.Exchange(ref _pending, line);
            _ = FlushAsync();
        }

        /// <summary>Write whatever is waiting; a write that fails ends the connection (the reader notices).</summary>
        private async Task FlushAsync()
        {
            if (!await _writing.WaitAsync(0))
            {
                return;
            }

            try
            {
                while (Interlocked.Exchange(ref _pending, null) is { } line)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                    await Pipe.WriteAsync(bytes);
                    await Pipe.FlushAsync();
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Dispose();
                return;
            }
            finally
            {
                _writing.Release();
            }

            // A Send that landed after the loop's last look but before the release found the writer
            // busy and left its line waiting: go round again for it.
            if (Volatile.Read(ref _pending) != null)
            {
                await FlushAsync();
            }
        }

        public void Dispose() => Pipe.Dispose();
    }

    /// <summary>The Win32 calls for a pipe a sandboxed app in this package can open.</summary>
    private static class Native
    {
        private const uint PIPE_ACCESS_DUPLEX = 0x3, FILE_FLAG_OVERLAPPED = 0x40000000, FILE_FLAG_FIRST_PIPE_INSTANCE = 0x80000;
        private const uint PIPE_TYPE_BYTE = 0x0, PIPE_READMODE_BYTE = 0x0, PIPE_REJECT_REMOTE_CLIENTS = 0x8;
        private const uint SDDL_REVISION_1 = 1;

        /// <summary>
        /// The pipe's security descriptor as SDDL: owned by and fully open to the user, read and
        /// write for this package's AppContainer, and a low integrity label with no write-up, so the
        /// low-integrity widget may write to it.
        /// </summary>
        public static string SecurityFor(string packageFamilyName)
        {
            string packageSid = PackageSid(packageFamilyName);
            string userSid = WindowsIdentity.GetCurrent().User!.Value;
            return $"O:{userSid}D:(A;;GA;;;{userSid})(A;;FRFW;;;{packageSid})S:(ML;;NW;;;LW)";
        }

        /// <summary>
        /// This package's AppContainer SID: Windows' answer when it gives one, else the same SID worked
        /// out from the name (<see cref="AppContainerSid"/>), since inside an installed package Windows
        /// was seen to report success with no SID.
        /// </summary>
        private static string PackageSid(string packageFamilyName)
        {
            int hr = DeriveAppContainerSidFromAppContainerName(packageFamilyName, out IntPtr sidPointer);
            if (hr != 0 || sidPointer == IntPtr.Zero)
            {
                Logger.Info("Game Bar widget: Windows gave no package SID (0x{Result:X8}); working it out from the name", hr);
                return AppContainerSid.FromName(packageFamilyName);
            }

            try
            {
                return new SecurityIdentifier(sidPointer).Value;
            }
            finally
            {
                FreeSid(sidPointer);
            }
        }

        /// <summary>
        /// One instance of the pipe, with that security. Only this session's local processes can use
        /// it (remote clients are rejected). <paramref name="first"/> makes it fail when the name already
        /// exists, so no other process can create the pipe ahead of ChatMixr and receive the widget.
        /// </summary>
        public static NamedPipeServerStream CreatePipe(string name, string sddl, int maxInstances, bool first)
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, SDDL_REVISION_1, out IntPtr descriptor, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not build the pipe's security descriptor");
            }

            try
            {
                var attributes = new SECURITY_ATTRIBUTES
                {
                    nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                    lpSecurityDescriptor = descriptor,
                    bInheritHandle = 0,
                };

                SafePipeHandle handle = CreateNamedPipe(@"\\.\pipe\" + name,
                    PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | (first ? FILE_FLAG_FIRST_PIPE_INSTANCE : 0),
                    PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_REJECT_REMOTE_CLIENTS,
                    (uint)maxInstances, 64 * 1024, 64 * 1024, 0, ref attributes);
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateNamedPipe failed");
                }

                return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
            }
            finally
            {
                LocalFree(descriptor);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBufferSize, uint inBufferSize, uint defaultTimeout, ref SECURITY_ATTRIBUTES securityAttributes);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, IntPtr size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
        private static extern int DeriveAppContainerSidFromAppContainerName(string appContainerName, out IntPtr sid);

        [DllImport("advapi32.dll")]
        private static extern IntPtr FreeSid(IntPtr sid);
    }
}
