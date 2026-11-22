using System.Runtime.InteropServices;
using ChatMixr.Models;
using static ChatMixr.Services.CoreAudio;

namespace ChatMixr.Services;

/// <summary>
/// Plays a virtual chat device into the output the user listens to. A game sends its voice chat to
/// the cable's playback side ("CABLE Input"); this records the recording side ("CABLE Output") and
/// plays it on the current output, at the chat level the engine gives it. Two shared-mode WASAPI
/// streams on one thread of its own: the recording stream wakes the thread every packet, the audio
/// goes through a <see cref="RelayBuffer"/>, and Windows converts the format for the output. The level
/// is the output stream's own volume, so no samples are touched.
/// <para>
/// Delay: the output is kept about <see cref="TargetPaddingMs"/> ahead rather than filled, and the two
/// devices' clocks drift apart, so the buffer's excess is dropped during quiet moments (or past a
/// ceiling) instead of growing into a steady lag. When the output runs ahead and the buffer is empty,
/// a little silence keeps it from running dry. The thread runs at Windows' "Pro Audio" priority
/// (MMCSS), so a busy game doesn't starve it.
/// </para>
/// </summary>
internal sealed class ChatRelay : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>How long the recording stream buffers: short, since every millisecond is delay.</summary>
    private const int CaptureBufferMs = 20;

    /// <summary>How long the output stream's buffer is; it is never filled, see <see cref="TargetPaddingMs"/>.</summary>
    private const int RenderBufferMs = 60;

    /// <summary>How far ahead the output is kept: enough to ride out a late wake-up, little enough to keep delay low.</summary>
    private const int TargetPaddingMs = 30;

    /// <summary>Below this much queued output with nothing to send, silence is added so it never runs dry.</summary>
    private const int MinPaddingMs = 10;

    /// <summary>Audio held beyond what the output takes is trimmed back to this during quiet moments.</summary>
    private const int TargetHeldMs = 10;

    /// <summary>Held beyond this, the excess is dropped even mid-speech: the delay ceiling past the output's own.</summary>
    private const int CeilingHeldMs = 60;

    /// <summary>The most audio the buffer can hold at all.</summary>
    private const int MaxDelayMs = 120;

    /// <summary>How long to wait before trying a failed stream again.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>What to relay: set by the engine, read by the relay thread.</summary>
    private sealed record Route(string? ListenId, string? TargetId, float Gain);

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile Route _route = new(null, null, 1f);
    private volatile bool _disposed;

    public ChatRelay()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "ChatMixr chat relay" };
        _thread.Start();
    }

    /// <summary>True while a route to listen to is set (the engine's latest <see cref="Configure"/>).</summary>
    public bool IsListening => _route.ListenId != null;

    /// <summary>What the relay is doing now. Written on the relay thread, read on the audio thread.</summary>
    public RelayState State
    {
        get => _state;
        private set => _state = value;
    }

    private volatile RelayState _state;

    /// <summary>
    /// Set what to relay. Called every engine pass; cheap when nothing changed.
    /// </summary>
    /// <param name="listenId">The chat device's recording side, or null for off.</param>
    /// <param name="targetId">The output to play it on, or null for off.</param>
    /// <param name="gain">The chat level as a volume factor, 0 to 1.</param>
    public void Configure(string? listenId, string? targetId, float gain)
    {
        var route = new Route(listenId, targetId, gain);
        if (route == _route)
        {
            return;
        }

        _route = route;
        _wake.Set();
    }

    // ── Relay thread ────────────────────────────────────────────────────
    private void Run()
    {
        Streams? streams = null;

        // Ask Windows to schedule this thread like other real-time audio, so a game's load doesn't
        // make it miss its deadlines (audible as crackles). Works without; logged if refused.
        uint taskIndex = 0;
        IntPtr mmcss = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
        if (mmcss == IntPtr.Zero)
        {
            Logger.Info("Chat relay runs at normal priority (MMCSS refused: {Error})", Marshal.GetLastWin32Error());
        }

        try
        {
            while (!_disposed)
            {
                var route = _route;
                if (route.ListenId == null || route.TargetId == null || route.ListenId == route.TargetId)
                {
                    streams?.Dispose();
                    streams = null;
                    State = RelayState.Off;
                    _wake.WaitOne(TimeSpan.FromSeconds(1));
                    continue;
                }

                if (streams == null || streams.ListenId != route.ListenId || streams.TargetId != route.TargetId)
                {
                    streams?.Dispose();
                    streams = Streams.Open(route.ListenId, route.TargetId, _wake);
                    if (streams == null)
                    {
                        State = RelayState.Failed;
                        _wake.WaitOne(RetryDelay);
                        continue;
                    }

                    Logger.Info("Chat relay running");
                }

                State = RelayState.Running;
                streams.SetGain(route.Gain);
                if (!streams.Pump())
                {
                    Logger.Info("Chat relay lost a stream; retrying");
                    streams.Dispose();
                    streams = null;
                    State = RelayState.Failed;
                    _wake.WaitOne(RetryDelay);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Chat relay stopped");
            State = RelayState.Failed;
        }
        finally
        {
            streams?.Dispose();
            if (mmcss != IntPtr.Zero)
            {
                AvRevertMmThreadCharacteristics(mmcss);
            }
        }
    }

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

    /// <summary>Stop relaying and end the thread.</summary>
    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>The two open streams and the buffer between them.</summary>
    private sealed class Streams : IDisposable
    {
        private readonly IAudioClient _capture;
        private readonly IAudioCaptureClient _captureClient;
        private readonly IAudioClient _render;
        private readonly IAudioRenderClient _renderClient;
        private readonly IAudioStreamVolume _volume;
        private readonly AutoResetEvent _packetReady;
        private readonly RelayBuffer _buffer;
        private readonly int _frameBytes;
        private readonly uint _renderFrames;
        private readonly uint _channels;
        private readonly int _framesPerMs;
        private readonly RelaySilence _silence;
        private byte[] _scratch = [];
        private float _gain = -1;

        public string ListenId { get; }

        public string TargetId { get; }

        private Streams(string listenId, string targetId, IAudioClient capture, IAudioCaptureClient captureClient, IAudioClient render,
            IAudioRenderClient renderClient, IAudioStreamVolume volume, AutoResetEvent packetReady, int frameBytes, int sampleRate, uint renderFrames, RelaySilence silence)
        {
            _framesPerMs = Math.Max(1, sampleRate / 1000);
            _silence = silence;
            ListenId = listenId;
            TargetId = targetId;
            _capture = capture;
            _captureClient = captureClient;
            _render = render;
            _renderClient = renderClient;
            _volume = volume;
            _packetReady = packetReady;
            _frameBytes = frameBytes;
            _renderFrames = renderFrames;
            _buffer = new RelayBuffer(frameBytes, Math.Max(1, sampleRate * MaxDelayMs / 1000));
            _volume.GetChannelCount(out _channels);
        }

        /// <summary>
        /// Open the recording stream in the device's own format and the output stream in that same
        /// format (Windows converts it), then start both. Null, with everything released, on failure.
        /// </summary>
        public static Streams? Open(string listenId, string targetId, AutoResetEvent packetReady)
        {
            IAudioClient? capture = null;
            IAudioClient? render = null;
            IntPtr format = IntPtr.Zero;
            try
            {
                capture = AudioSessions.OpenClient(listenId);
                render = AudioSessions.OpenClient(targetId);
                if (capture == null || render == null || capture.GetMixFormat(out format) != 0)
                {
                    throw new InvalidOperationException("a chat relay device is gone");
                }

                var header = Marshal.PtrToStructure<WAVEFORMATEX>(format);
                Check(capture.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                    CaptureBufferMs * ReferenceTimePerMillisecond, 0, format, IntPtr.Zero), "open the chat device");
                Check(capture.SetEventHandle(packetReady.SafeWaitHandle.DangerousGetHandle()), "listen to the chat device");
                Check(render.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                    RenderBufferMs * ReferenceTimePerMillisecond, 0, format, IntPtr.Zero), "open the output");
                Check(render.GetBufferSize(out uint renderFrames), "size the output");

                var captureClient = (IAudioCaptureClient)Service(capture, IID_IAudioCaptureClient);
                var renderClient = (IAudioRenderClient)Service(render, IID_IAudioRenderClient);
                var volume = (IAudioStreamVolume)Service(render, IID_IAudioStreamVolume);

                var streams = new Streams(listenId, targetId, capture, captureClient, render, renderClient, volume,
                    packetReady, header.nBlockAlign, (int)header.nSamplesPerSec, renderFrames, SilenceFor(format, header));
                Check(render.Start(), "start the output");
                Check(capture.Start(), "start listening");
                return streams;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or InvalidCastException)
            {
                Logger.Warn("Chat relay could not start: {Message}", ex.Message);
                Release(capture);
                Release(render);
                return null;
            }
            finally
            {
                if (format != IntPtr.Zero)
                {
                    CoTaskMemFree(format);
                }
            }
        }

        /// <summary>The silence test for the stream's format: float, or integer PCM of its sample size.</summary>
        private static RelaySilence SilenceFor(IntPtr format, WAVEFORMATEX header)
        {
            const ushort Float = 3, Extensible = 0xFFFE;
            bool isFloat = header.wFormatTag == Float;
            if (header.wFormatTag == Extensible && header.cbSize >= 22)
            {
                // WAVEFORMATEXTENSIBLE: the sub-format GUID follows the 18-byte header, the valid-bits
                // word and the channel mask.
                var subFormat = Marshal.PtrToStructure<Guid>(format + 24);
                isFloat = subFormat == FloatSubFormat;
            }

            return new RelaySilence(header.wBitsPerSample, isFloat);
        }

        /// <summary>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT.</summary>
        private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

        /// <summary>Set the output stream's volume (every channel) to the chat level.</summary>
        public void SetGain(float gain)
        {
            if (Math.Abs(gain - _gain) < 0.001f || _channels == 0)
            {
                return;
            }

            _gain = gain;
            var levels = Enumerable.Repeat(Math.Clamp(gain, 0f, 1f), (int)_channels).ToArray();
            _volume.SetAllVolumes(_channels, levels);
        }

        /// <summary>
        /// Wait for the next packet (or a short timeout), move every recorded packet into the buffer,
        /// trim any drift, then top the output up to <see cref="TargetPaddingMs"/>. False when a stream
        /// was lost.
        /// </summary>
        public bool Pump()
        {
            _packetReady.WaitOne(200);

            // Recording side: every packet waiting, into the buffer.
            while (true)
            {
                if (_captureClient.GetNextPacketSize(out uint packet) != 0)
                {
                    return false;
                }

                if (packet == 0)
                {
                    break;
                }

                if (_captureClient.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) != 0)
                {
                    return false;
                }

                int bytes = (int)frames * _frameBytes;
                EnsureScratch(bytes);
                if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                {
                    Array.Clear(_scratch, 0, bytes);
                }
                else
                {
                    Marshal.Copy(data, _scratch, 0, bytes);
                }

                _buffer.Write(_scratch.AsSpan(0, bytes));
                _captureClient.ReleaseBuffer(frames);
            }

            // Clock drift: drop what has piled up beyond the target, quietly if possible.
            _buffer.Trim(TargetHeldMs * _framesPerMs, CeilingHeldMs * _framesPerMs, _silence.IsQuiet);

            // Output side: top it up to the target, not to the full buffer, so the delay stays low.
            if (_render.GetCurrentPadding(out uint padding) != 0)
            {
                return false;
            }

            int wanted = TargetPaddingMs * _framesPerMs - (int)padding;
            int frameCount = Math.Min(Math.Min(wanted, (int)(_renderFrames - padding)), _buffer.Frames);
            bool runningDry = _buffer.Frames == 0 && padding < MinPaddingMs * _framesPerMs;
            if (runningDry)
            {
                // The output's clock is ahead and there is nothing to send: a little silence keeps it
                // from running dry (which Windows plays as a click).
                frameCount = Math.Max(0, wanted);
            }

            if (frameCount <= 0)
            {
                return true;
            }

            if (_renderClient.GetBuffer((uint)frameCount, out IntPtr target) != 0)
            {
                return false;
            }

            if (runningDry)
            {
                return _renderClient.ReleaseBuffer((uint)frameCount, AUDCLNT_BUFFERFLAGS_SILENT) == 0;
            }

            int length = frameCount * _frameBytes;
            EnsureScratch(length);
            int read = _buffer.Read(_scratch.AsSpan(0, length));
            Marshal.Copy(_scratch, 0, target, read);
            return _renderClient.ReleaseBuffer((uint)(read / _frameBytes), 0) == 0;
        }

        public void Dispose()
        {
            _capture.Stop();
            _render.Stop();
            Release(_captureClient);
            Release(_renderClient);
            Release(_volume);
            Release(_capture);
            Release(_render);
        }

        private void EnsureScratch(int bytes)
        {
            if (_scratch.Length < bytes)
            {
                _scratch = new byte[bytes];
            }
        }

        private static object Service(IAudioClient client, Guid iid)
        {
            Check(client.GetService(ref iid, out object service), "reach a stream");
            return service;
        }

        private static void Check(int hr, string step)
        {
            if (hr != 0)
            {
                throw new COMException($"Could not {step}", hr);
            }
        }

        private static void Release(object? com)
        {
            if (com != null && Marshal.IsComObject(com))
            {
                Marshal.ReleaseComObject(com);
            }
        }
    }
}
