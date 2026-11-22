using System.IO;
using System.Text.RegularExpressions;
using ChatMixr.Models;
using Microsoft.Win32.SafeHandles;

namespace ChatMixr.Services;

/// <summary>
/// The receivers ChatMixr knows how to read: SteelSeries (USB vendor 1038) Arctis Nova 7 family
/// receivers. Only the Nova 7X (22A5) and Nova 7X Gen 2 (229E) have been tested; the others are the
/// product ids the Linux kernel's driver lists for the same wheel report.
/// </summary>
internal static partial class SupportedReceivers
{
    /// <summary>SteelSeries' USB vendor id, as it appears in device paths.</summary>
    public const string VendorFilter = "vid_1038";

    /// <summary>The vendor-defined usage page the wheel, power and battery messages arrive on.</summary>
    public const ushort ChatMixUsagePage = 0xFF00;

    /// <summary>The vendor-defined usage page that answers the status request.</summary>
    public const ushort StatusUsagePage = 0xFFC0;

    private static readonly HashSet<int> ProductIds =
    [
        0x2202, 0x2206, 0x223A, 0x227A, 0x22A4, 0x22AB,   // Nova 7 family, discrete battery levels
        0x22A1, 0x227E, 0x2258, 0x229E, 0x22A9, 0x22A5,   // Nova 7 family, battery percentage
    ];

    /// <summary>The product id in a device path, or null when the path has none.</summary>
    public static int? ProductIdOf(string path)
    {
        var match = ProductIdPattern().Match(path);
        return match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : null;
    }

    /// <summary>True when the path belongs to a supported receiver.</summary>
    public static bool IsSupported(string path) =>
        ProductIdOf(path) is int pid && ProductIds.Contains(pid);

    [GeneratedRegex("pid_([0-9a-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex ProductIdPattern();
}

/// <summary>
/// One of a connected receiver's two vendor interfaces: the one the wheel, power and battery messages
/// arrive on, or the one that answers the status request (asked when it opens and again when its
/// headset turns on). A background thread reads its reports and raises <see cref="Reported"/> for each
/// one ChatMixr understands. Reading ends when the receiver is unplugged or <see cref="Dispose"/> is
/// called, and <see cref="Ended"/> is raised once either way.
/// </summary>
internal sealed class Receiver : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly SafeFileHandle _handle;
    private readonly int _reportLength;

    /// <summary>
    /// The status request's own stream (on a second handle), on the status interface only. Kept open:
    /// disposing a FileStream closes its handle.
    /// </summary>
    private readonly FileStream? _requests;
    private readonly int _requestLength;
    private volatile bool _disposed;

    /// <summary>The interface path; unique per receiver while it stays plugged into the same port.</summary>
    public string Path { get; }

    /// <summary>The physical receiver, shared with its headset's audio endpoints.</summary>
    public Guid ContainerId { get; }

    /// <summary>Raised on the reader thread for every message ChatMixr understands.</summary>
    public event Action<Receiver, ReceiverReport>? Reported;

    /// <summary>Raised on the reader thread when reading stops for good.</summary>
    public event Action<Receiver>? Ended;

    private Receiver(string path, Guid containerId, SafeFileHandle handle, int reportLength, SafeFileHandle? requests, int requestLength)
    {
        Path = path;
        ContainerId = containerId;
        _handle = handle;
        _reportLength = reportLength;
        _requests = requests == null ? null : new FileStream(requests, FileAccess.Write, requestLength, isAsync: false);
        _requestLength = requestLength;
    }

    /// <summary>
    /// Every supported receiver's ChatMix interface that is present now, opened but not yet reading.
    /// </summary>
    /// <param name="skipPaths">Interfaces already open; left alone.</param>
    /// <param name="rejectedPaths">
    /// Interfaces already found not to be the ChatMix one (a receiver's other HID interfaces). They are
    /// skipped, and newly rejected ones are added, so a rescan doesn't reopen them every time.
    /// </param>
    public static List<Receiver> OpenNew(IReadOnlySet<string> skipPaths, ISet<string> rejectedPaths)
    {
        var opened = new List<Receiver>();
        foreach (var hid in HidInterop.ListInterfaces(SupportedReceivers.VendorFilter))
        {
            if (skipPaths.Contains(hid.Path) || rejectedPaths.Contains(hid.Path))
            {
                continue;
            }

            if (!SupportedReceivers.IsSupported(hid.Path) || hid.ContainerId is not Guid container)
            {
                rejectedPaths.Add(hid.Path);
                continue;
            }

            switch (TryOpen(hid.Path, container))
            {
                case OpenResult.Opened found:
                    opened.Add(found.Receiver);
                    break;
                case OpenResult.NotChatMix:
                    rejectedPaths.Add(hid.Path);
                    break;
            }
        }

        return opened;
    }

    /// <summary>What opening one interface found.</summary>
    private abstract record OpenResult
    {
        /// <summary>The ChatMix interface, open and ready to read.</summary>
        public sealed record Opened(Receiver Receiver) : OpenResult;

        /// <summary>Another of the receiver's interfaces; it never will be one ChatMixr reads.</summary>
        public sealed record NotChatMix : OpenResult;

        /// <summary>Could not be opened now (it may be going away); try again on the next scan.</summary>
        public sealed record Failed : OpenResult;
    }

    /// <summary>Open the interface if it is one of the two vendor-defined ones ChatMixr reads.</summary>
    private static OpenResult TryOpen(string path, Guid container)
    {
        var handle = HidInterop.OpenForReading(path);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return new OpenResult.Failed();
        }

        var caps = HidInterop.CapsOf(handle);
        if (caps == null)
        {
            handle.Dispose();
            return new OpenResult.Failed();
        }

        bool events = caps.UsagePage == SupportedReceivers.ChatMixUsagePage;
        bool status = caps.UsagePage == SupportedReceivers.StatusUsagePage && caps.OutputReportLength >= 2;
        if ((!events && !status) || caps.InputReportLength < 4)
        {
            handle.Dispose();
            return new OpenResult.NotChatMix();
        }

        SafeFileHandle? requests = null;
        if (status)
        {
            requests = HidInterop.OpenForWriting(path);
            if (requests.IsInvalid)
            {
                requests.Dispose();
                handle.Dispose();
                return new OpenResult.Failed();
            }
        }

        Logger.Info("Receiver interface found: {Path}", path);
        return new OpenResult.Opened(new Receiver(path, container, handle, caps.InputReportLength, requests, caps.OutputReportLength));
    }

    /// <summary>Start the reader thread, then ask the status interface for the headset's state.</summary>
    public void Start()
    {
        var thread = new Thread(ReadLoop) { IsBackground = true, Name = "ChatMix receiver" };
        thread.Start();
        RequestStatus();
    }

    /// <summary>
    /// Ask for power, battery and the wheel's position (status interface only); the answer arrives as a
    /// <see cref="ReceiverReport.Status"/>. Only reads state; failures are logged and ignored.
    /// </summary>
    public void RequestStatus()
    {
        if (_requests == null || _disposed)
        {
            return;
        }

        try
        {
            var request = ReceiverReport.StatusRequest(_requestLength);
            _requests.Write(request, 0, request.Length);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Logger.Info("Status request to {Path} failed: {Message}", Path, ex.Message);
        }
    }

    private void ReadLoop()
    {
        try
        {
            using var stream = new FileStream(_handle, FileAccess.Read, _reportLength, isAsync: false);
            byte[] buffer = new byte[_reportLength];
            while (!_disposed)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                if (ReceiverReport.Parse(buffer.AsSpan(0, read)) is { } report)
                {
                    Reported?.Invoke(this, report);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Unplugged, or closed by Dispose: both end reading normally.
            if (!_disposed)
            {
                Logger.Info("Receiver gone: {Path} ({Message})", Path, ex.Message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reading receiver {Path} failed", Path);
        }

        Ended?.Invoke(this);
    }

    /// <summary>Stop reading and close the interface. The reader thread raises <see cref="Ended"/>.</summary>
    public void Dispose()
    {
        _disposed = true;
        _requests?.Dispose();

        // Closing a handle doesn't end a read already waiting on it; cancelling it does, so the reader
        // thread ends now instead of at the next report (or never, while the headset is quiet).
        HidInterop.CancelPendingReads(_handle);
        _handle.Dispose();
    }
}
