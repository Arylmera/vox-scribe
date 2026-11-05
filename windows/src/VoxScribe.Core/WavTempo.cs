using System.Buffers.Binary;
using System.Text;

namespace VoxScribe.Core;

/// <summary>
/// Speeds a mono 16-bit WAV clip up without raising its pitch (WSOLA: overlapped frames, each
/// picked where it best continues the last, so the waveform stays in phase).
/// </summary>
public static class WavTempo
{
    private const double FrameSeconds = 0.04;
    private const double ToleranceSeconds = 0.01;

    /// <summary>The clip at <paramref name="rate"/>; the clip itself at 1, or when it is not mono 16-bit PCM.</summary>
    public static byte[] Apply(byte[] wav, double rate)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (Math.Abs(rate - 1) < 0.001 || rate <= 0 || Parse(wav) is not var (sampleRate, start, length)) return wav;

        var input = new float[length / 2];
        for (var i = 0; i < input.Length; i++)
            input[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(start + (i * 2)));

        return Build(Stretch(input, sampleRate, rate), sampleRate);
    }

    private static float[] Stretch(float[] x, int sampleRate, double rate)
    {
        var frame = (int)(sampleRate * FrameSeconds);
        var hop = frame / 2;
        var tolerance = (int)(sampleRate * ToleranceSeconds);
        if (x.Length < frame * 2) return x;

        var window = new float[frame];
        for (var i = 0; i < frame; i++) window[i] = (float)(0.5 - (0.5 * Math.Cos(2 * Math.PI * i / frame)));

        var length = (int)(x.Length / rate);
        var y = new float[length + frame];
        var weight = new float[length + frame];
        var previous = 0;

        for (var k = 0; k * hop < length; k++)
        {
            var target = (int)(k * hop * rate);
            var position = k == 0 ? 0 : BestMatch(x, previous + hop, target, frame, tolerance);
            if (position + frame > x.Length) break;

            for (var i = 0; i < frame; i++)
            {
                y[(k * hop) + i] += x[position + i] * window[i];
                weight[(k * hop) + i] += window[i];
            }

            previous = position;
        }

        var result = new float[length];
        for (var i = 0; i < length; i++) result[i] = weight[i] > 0.001f ? y[i] / weight[i] : 0;
        return result;
    }

    /// <summary>Where near <paramref name="target"/> the signal best matches what would naturally come next.</summary>
    private static int BestMatch(float[] x, int natural, int target, int frame, int tolerance)
    {
        if (natural + frame > x.Length) return Math.Min(target, Math.Max(0, x.Length - frame));

        var best = target;
        var bestScore = double.NegativeInfinity;
        for (var position = Math.Max(0, target - tolerance); position <= target + tolerance && position + frame <= x.Length; position++)
        {
            double score = 0;
            for (var i = 0; i < frame; i += 2) score += x[natural + i] * x[position + i];
            if (score > bestScore) (bestScore, best) = (score, position);
        }

        return best;
    }

    /// <summary>Sample rate and the data chunk of a mono 16-bit PCM WAV, or null for anything else.</summary>
    private static (int SampleRate, int Start, int Length)? Parse(byte[] wav)
    {
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") return null;

        var sampleRate = 0;
        for (var at = 12; at + 8 <= wav.Length;)
        {
            var id = Encoding.ASCII.GetString(wav, at, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(at + 4));
            if (size < 0) return null;

            if (id == "fmt " && at + 24 <= wav.Length)
            {
                var format = wav.AsSpan(at + 8);
                if (BinaryPrimitives.ReadInt16LittleEndian(format) != 1
                    || BinaryPrimitives.ReadInt16LittleEndian(format[2..]) != 1
                    || BinaryPrimitives.ReadInt16LittleEndian(format[14..]) != 16)
                {
                    return null;
                }

                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(format[4..]);
            }
            else if (id == "data")
            {
                return sampleRate > 0 ? (sampleRate, at + 8, Math.Min(size, wav.Length - at - 8)) : null;
            }

            at += 8 + size + (size & 1);
        }

        return null;
    }

    private static byte[] Build(float[] samples, int sampleRate)
    {
        var wav = new byte[44 + (samples.Length * 2)];
        var span = wav.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], wav.Length - 8);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        Encoding.ASCII.GetBytes("data").CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], samples.Length * 2);

        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + (i * 2))..], (short)Math.Clamp(samples[i], short.MinValue, short.MaxValue));

        return wav;
    }
}
