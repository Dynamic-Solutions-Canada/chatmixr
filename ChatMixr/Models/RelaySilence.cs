namespace ChatMixr.Models;

/// <summary>
/// Whether a stretch of audio is quiet enough that skipping it can't be heard, for the chat relay's
/// drift correction (<see cref="RelayBuffer.Trim"/>). Knows the formats Windows' shared mode uses: 32-bit
/// float (nearly always) and 16-, 24- and 32-bit integer PCM. Any other format is never called quiet,
/// so the relay then only drops audio past its ceiling.
/// </summary>
public sealed class RelaySilence
{
    /// <summary>The loudest sample still counted as quiet: about -60 dB below full scale.</summary>
    public const double Threshold = 0.001;

    private readonly int _bytesPerSample;
    private readonly bool _isFloat;

    /// <param name="bitsPerSample">The stream's container size per sample (16, 24 or 32).</param>
    /// <param name="isFloat">True for IEEE float samples.</param>
    public RelaySilence(int bitsPerSample, bool isFloat)
    {
        _bytesPerSample = bitsPerSample / 8;
        _isFloat = isFloat;
    }

    /// <summary>True when every sample in <paramref name="audio"/> is below <see cref="Threshold"/>.</summary>
    public bool IsQuiet(ReadOnlySpan<byte> audio)
    {
        if (_isFloat && _bytesPerSample == 4)
        {
            for (int i = 0; i + 4 <= audio.Length; i += 4)
            {
                if (Math.Abs(BitConverter.ToSingle(audio.Slice(i, 4))) >= Threshold)
                {
                    return false;
                }
            }

            return true;
        }

        if (_isFloat || _bytesPerSample is not (2 or 3 or 4))
        {
            return false;
        }

        // Integer PCM, little-endian: compare each sample's magnitude to the threshold at its size.
        double fullScale = Math.Pow(2, _bytesPerSample * 8 - 1);
        long limit = (long)(Threshold * fullScale);
        for (int i = 0; i + _bytesPerSample <= audio.Length; i += _bytesPerSample)
        {
            long sample = _bytesPerSample switch
            {
                2 => BitConverter.ToInt16(audio.Slice(i, 2)),
                3 => (audio[i] | (audio[i + 1] << 8) | ((sbyte)audio[i + 2] << 16)),
                _ => BitConverter.ToInt32(audio.Slice(i, 4)),
            };
            if (Math.Abs(sample) >= limit)
            {
                return false;
            }
        }

        return true;
    }
}
