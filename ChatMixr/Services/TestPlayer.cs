using System.Runtime.InteropServices;
using ChatMixr.Models;
using static ChatMixr.Services.CoreAudio;

namespace ChatMixr.Services;

/// <summary>
/// The device test: plays the game sound and the chat sound (<see cref="TestSignal"/>) continuously,
/// at the same time, until stopped, so the user can move a slider or wheel and hear the balance
/// change. Each sound is its own output stream with its own volume, which the engine keeps at that
/// side's level. With Separate chat on, the chat sound goes into the cable and comes back through the
/// chat relay, so hearing it proves the whole path works. One thread of its own.
/// </summary>
internal sealed class TestPlayer : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int BufferMs = 100;

    /// <summary>What to play: set by the engine, read by the player thread.</summary>
    private sealed record Plan(string? GameTarget, string? ChatTarget, float GameGain, float ChatGain);

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile Plan _plan = new(null, null, 1f, 1f);
    private volatile bool _disposed;

    public TestPlayer()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "ChatMixr test sounds" };
        _thread.Start();
    }

    /// <summary>Set what plays, and how loud. Null targets stop that sound; cheap when nothing changed.</summary>
    /// <param name="gameTarget">The output the game sound plays on.</param>
    /// <param name="chatTarget">The output the chat sound plays on (the cable with Separate chat on).</param>
    /// <param name="gameGain">The game sound's volume, 0 to 1.</param>
    /// <param name="chatGain">The chat sound's volume, 0 to 1.</param>
    public void Configure(string? gameTarget, string? chatTarget, float gameGain, float chatGain)
    {
        var plan = new Plan(gameTarget, chatTarget, gameGain, chatGain);
        if (plan == _plan)
        {
            return;
        }

        _plan = plan;
        _wake.Set();
    }

    private void Run()
    {
        ToneStream? game = null;
        ToneStream? chat = null;
        try
        {
            while (!_disposed)
            {
                try
                {
                    Step(ref game, ref chat);
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException)
                {
                    // One device failing (unplugged mid-test, say) drops its streams; the next step
                    // reopens whatever the plan still names, so a test can be started again.
                    Logger.Warn("Test sounds hit a problem; reopening: {Message}", ex.Message);
                    game?.Dispose();
                    chat?.Dispose();
                    game = null;
                    chat = null;
                    _wake.WaitOne(TimeSpan.FromSeconds(1));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Test sounds stopped");
        }
        finally
        {
            game?.Dispose();
            chat?.Dispose();
        }
    }

    /// <summary>One turn of the test thread: open or close streams to match the plan, then top them up.</summary>
    private void Step(ref ToneStream? game, ref ToneStream? chat)
    {
        var plan = _plan;
        game = Keep(game, plan.GameTarget, TestChannel.Game);
        chat = Keep(chat, plan.ChatTarget, TestChannel.Chat);
        if (game == null && chat == null)
        {
            _wake.WaitOne(TimeSpan.FromSeconds(1));
            return;
        }

        game?.SetGain(plan.GameGain);
        chat?.SetGain(plan.ChatGain);
        if (game != null && !game.Fill())
        {
            game.Dispose();
            game = null;
        }

        if (chat != null && !chat.Fill())
        {
            chat.Dispose();
            chat = null;
        }

        _wake.WaitOne(20);
    }

    /// <summary>The stream for a target: the open one if it still matches, a new one, or none.</summary>
    private static ToneStream? Keep(ToneStream? current, string? target, TestChannel channel)
    {
        if (current != null && current.Target == target)
        {
            return current;
        }

        current?.Dispose();
        return target == null ? null : ToneStream.Open(target, channel);
    }

    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>One test sound playing on one output.</summary>
    private sealed class ToneStream : IDisposable
    {
        private readonly IAudioClient _client;
        private readonly IAudioRenderClient _render;
        private readonly IAudioStreamVolume _volume;
        private readonly TestSignal _signal;
        private readonly uint _bufferFrames;
        private float[] _samples = [];
        private float _gain = -1;

        public string Target { get; }

        private ToneStream(string target, IAudioClient client, IAudioRenderClient render, IAudioStreamVolume volume, TestSignal signal, uint bufferFrames)
        {
            Target = target;
            _client = client;
            _render = render;
            _volume = volume;
            _signal = signal;
            _bufferFrames = bufferFrames;
        }

        /// <summary>Open a 48 kHz stereo float stream on the output (Windows converts it); null on failure.</summary>
        public static ToneStream? Open(string target, TestChannel channel)
        {
            var client = AudioSessions.OpenClient(target);
            if (client == null)
            {
                return null;
            }

            var format = new WAVEFORMATEX
            {
                wFormatTag = 3, // WAVE_FORMAT_IEEE_FLOAT
                nChannels = Channels,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = 32,
                nBlockAlign = Channels * 4,
                nAvgBytesPerSec = SampleRate * Channels * 4,
                cbSize = 0,
            };
            IntPtr formatPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEFORMATEX>());
            try
            {
                Marshal.StructureToPtr(format, formatPointer, false);
                int hr = client.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                    BufferMs * ReferenceTimePerMillisecond, 0, formatPointer, IntPtr.Zero);
                if (hr != 0 || client.GetBufferSize(out uint frames) != 0)
                {
                    throw new COMException("Could not open the test sound", hr);
                }

                var renderIid = IID_IAudioRenderClient;
                var volumeIid = IID_IAudioStreamVolume;
                Check(client.GetService(ref renderIid, out object render), "reach the test stream");
                Check(client.GetService(ref volumeIid, out object volume), "reach the test stream's volume");
                var stream = new ToneStream(target, client, (IAudioRenderClient)render, (IAudioStreamVolume)volume, new TestSignal(channel, SampleRate), frames);
                Check(client.Start(), "start the test sound");
                return stream;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                Logger.Warn("Test sound could not start on {Target}: {Message}", target, ex.Message);
                Marshal.ReleaseComObject(client);
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(formatPointer);
            }
        }

        private static void Check(int hr, string step)
        {
            if (hr != 0)
            {
                throw new COMException($"Could not {step}", hr);
            }
        }

        /// <summary>Set the stream's volume on both channels.</summary>
        public void SetGain(float gain)
        {
            if (Math.Abs(gain - _gain) < 0.001f)
            {
                return;
            }

            _gain = gain;
            _volume.SetAllVolumes(Channels, [Math.Clamp(gain, 0f, 1f), Math.Clamp(gain, 0f, 1f)]);
        }

        /// <summary>Top the output up with the next stretch of the sound. False when the output is gone.</summary>
        public bool Fill()
        {
            if (_client.GetCurrentPadding(out uint padding) != 0)
            {
                return false;
            }

            uint frames = _bufferFrames - padding;
            if (frames == 0)
            {
                return true;
            }

            int count = (int)frames * Channels;
            if (_samples.Length < count)
            {
                _samples = new float[count];
            }

            _signal.Fill(_samples.AsSpan(0, count), Channels);
            if (_render.GetBuffer(frames, out IntPtr data) != 0)
            {
                return false;
            }

            Marshal.Copy(_samples, 0, data, count);
            return _render.ReleaseBuffer(frames, 0) == 0;
        }

        public void Dispose()
        {
            _client.Stop();
            Marshal.ReleaseComObject(_render);
            Marshal.ReleaseComObject(_volume);
            Marshal.ReleaseComObject(_client);
        }
    }
}
