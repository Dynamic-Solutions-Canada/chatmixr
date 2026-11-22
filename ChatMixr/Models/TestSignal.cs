namespace ChatMixr.Models;

/// <summary>Which side of the mix a test sound stands for.</summary>
public enum TestChannel
{
    /// <summary>A steady low hum, like game sound that never stops.</summary>
    Game,

    /// <summary>A repeating two-note chime, like someone talking now and then.</summary>
    Chat,
}

/// <summary>
/// The sounds the device test plays continuously, one per side of the mix, made so they are easy to
/// tell apart when both play at once: the game side is a low, slowly pulsing hum (110 and 165 Hz),
/// the chat side a bright two-note chime (880 then 1175 Hz) every 1.5 seconds. Both are quiet enough
/// to leave headroom, and loop without clicks. Generated, so nothing is shipped or downloaded.
/// </summary>
public sealed class TestSignal
{
    /// <summary>How loud each sound is at full volume (0 to 1); leaves headroom when both play.</summary>
    public const float Level = 0.25f;

    private readonly TestChannel _channel;
    private readonly int _sampleRate;
    private long _sample;

    /// <param name="channel">Which sound to make.</param>
    /// <param name="sampleRate">Samples per second of the stream it fills.</param>
    public TestSignal(TestChannel channel, int sampleRate)
    {
        _channel = channel;
        _sampleRate = sampleRate;
    }

    /// <summary>
    /// Fill interleaved 32-bit float frames with the next stretch of the sound, the same sample in
    /// every channel. Continues where the previous call stopped.
    /// </summary>
    /// <param name="frames">The buffer to fill; its length is a whole number of frames.</param>
    /// <param name="channels">Samples per frame.</param>
    public void Fill(Span<float> frames, int channels)
    {
        for (int i = 0; i + channels <= frames.Length; i += channels)
        {
            float value = Next();
            frames.Slice(i, channels).Fill(value);
        }
    }

    /// <summary>The next sample of the sound.</summary>
    private float Next()
    {
        double t = (double)_sample++ / _sampleRate;
        return (float)(_channel == TestChannel.Game ? Hum(t) : Chime(t)) * Level;
    }

    /// <summary>Two low notes a fifth apart, swelling gently twice a second.</summary>
    private static double Hum(double t)
    {
        double tone = 0.6 * Math.Sin(2 * Math.PI * 110 * t) + 0.4 * Math.Sin(2 * Math.PI * 165 * t);
        double swell = 0.75 + 0.25 * Math.Sin(2 * Math.PI * 0.5 * t);
        return tone * swell;
    }

    /// <summary>Two short bell notes at the start of every 1.5-second cycle, then quiet.</summary>
    private static double Chime(double t)
    {
        const double Cycle = 1.5;
        double inCycle = t % Cycle;
        // The first note is still ringing (about a fifth of its peak) when the second starts, so both
        // are kept at 0.8 to stay below full scale where they overlap.
        return 0.8 * (Note(inCycle, 0.0, 880) + Note(inCycle, 0.22, 1175));
    }

    /// <summary>A bell-like note starting at <paramref name="start"/>: a quick attack and a fading tail.</summary>
    private static double Note(double t, double start, double frequency)
    {
        double since = t - start;
        if (since < 0 || since > 0.9)
        {
            return 0;
        }

        double attack = Math.Min(1, since / 0.005);
        double decay = Math.Exp(-since * 7);
        return Math.Sin(2 * Math.PI * frequency * since) * attack * decay;
    }
}
