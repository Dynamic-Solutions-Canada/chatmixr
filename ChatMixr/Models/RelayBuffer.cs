namespace ChatMixr.Models;

/// <summary>
/// The audio held between the chat device's capture side and the output it is played on. The two
/// run on different clocks, so one always drifts ahead of the other: this buffer evens out the
/// small differences and keeps the delay bounded. <see cref="Trim"/> drops the excess when the
/// capture side runs ahead: during a quiet moment as soon as there is any, and regardless once it
/// passes a ceiling. When it holds more than <see cref="MaxFrames"/> the oldest audio is dropped;
/// when it runs dry (the output got ahead), the caller plays silence. Whole frames only, so channels
/// never get out of step. Not thread-safe: the relay uses it from its one thread.
/// </summary>
public sealed class RelayBuffer
{
    private readonly byte[] _data;
    private readonly int _frameBytes;
    private int _start;
    private int _count;

    /// <param name="frameBytes">Bytes per frame (every channel's sample for one instant).</param>
    /// <param name="maxFrames">The most frames held before the oldest are dropped: the delay ceiling.</param>
    public RelayBuffer(int frameBytes, int maxFrames)
    {
        _frameBytes = frameBytes;
        MaxFrames = maxFrames;
        _data = new byte[frameBytes * maxFrames];
    }

    /// <summary>The most frames held.</summary>
    public int MaxFrames { get; }

    /// <summary>Frames held now.</summary>
    public int Frames => _count / _frameBytes;

    /// <summary>Frames dropped so far because the output fell behind.</summary>
    public long DroppedFrames { get; private set; }

    /// <summary>Add captured frames; drops the oldest when full.</summary>
    /// <param name="frames">Whole frames of audio; a trailing partial frame is ignored.</param>
    public void Write(ReadOnlySpan<byte> frames)
    {
        int length = frames.Length - (frames.Length % _frameBytes);
        if (length > _data.Length)
        {
            // More than the whole buffer: only the newest part can be kept.
            DroppedFrames += (length - _data.Length) / _frameBytes;
            frames = frames.Slice(length - _data.Length, _data.Length);
            length = _data.Length;
        }

        int overflow = _count + length - _data.Length;
        if (overflow > 0)
        {
            Skip(overflow);
            DroppedFrames += overflow / _frameBytes;
        }

        int end = (_start + _count) % _data.Length;
        int first = Math.Min(length, _data.Length - end);
        frames[..first].CopyTo(_data.AsSpan(end));
        frames[first..length].CopyTo(_data);
        _count += length;
    }

    /// <summary>Take up to <paramref name="destination"/>'s whole frames, oldest first.</summary>
    /// <returns>The number of bytes copied (whole frames); the caller fills the rest with silence.</returns>
    public int Read(Span<byte> destination)
    {
        int length = Math.Min(_count, destination.Length - (destination.Length % _frameBytes));
        int first = Math.Min(length, _data.Length - _start);
        _data.AsSpan(_start, first).CopyTo(destination);
        _data.AsSpan(0, length - first).CopyTo(destination[first..]);
        Skip(length);
        return length;
    }

    /// <summary>
    /// Keep the delay down when the capture side's clock runs ahead of the output's: drop the oldest
    /// frames beyond <paramref name="targetFrames"/> when they are quiet (nobody hears silence being
    /// skipped), or whatever the audio once more than <paramref name="ceilingFrames"/> are held (a
    /// short skip beats a delay that keeps growing).
    /// </summary>
    /// <param name="targetFrames">How many frames to keep held.</param>
    /// <param name="ceilingFrames">Beyond this many, drop even audible frames.</param>
    /// <param name="isQuiet">True when a run of whole frames is silent enough to skip.</param>
    /// <returns>How many frames were dropped.</returns>
    public int Trim(int targetFrames, int ceilingFrames, Func<ReadOnlySpan<byte>, bool> isQuiet)
    {
        int excess = Frames - targetFrames;
        if (excess <= 0)
        {
            return 0;
        }

        if (Frames <= ceilingFrames && !OldestAreQuiet(excess * _frameBytes, isQuiet))
        {
            return 0;
        }

        Skip(excess * _frameBytes);
        DroppedFrames += excess;
        return excess;
    }

    /// <summary>True when the oldest <paramref name="bytes"/> held are quiet; checked in up to two parts across the wrap.</summary>
    private bool OldestAreQuiet(int bytes, Func<ReadOnlySpan<byte>, bool> isQuiet)
    {
        int first = Math.Min(bytes, _data.Length - _start);
        return isQuiet(_data.AsSpan(_start, first)) && (first == bytes || isQuiet(_data.AsSpan(0, bytes - first)));
    }

    /// <summary>Forget everything held (the output changed, so old audio would arrive late).</summary>
    public void Clear()
    {
        _start = 0;
        _count = 0;
    }

    private void Skip(int bytes)
    {
        _start = (_start + bytes) % _data.Length;
        _count -= bytes;
    }
}
