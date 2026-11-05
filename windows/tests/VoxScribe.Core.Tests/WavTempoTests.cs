using System.Buffers.Binary;
using System.Text;
using VoxScribe.Core;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>Faster speech keeps its pitch: a shorter clip, the same frequency.</summary>
public sealed class WavTempoTests
{
    private const int Rate = 24000;

    private static byte[] Tone(double hertz, double seconds)
    {
        var n = (int)(Rate * seconds);
        var wav = new byte[44 + (n * 2)];
        var s = wav.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(s);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], wav.Length - 8);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(s[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(s[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(s[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], Rate);
        BinaryPrimitives.WriteInt32LittleEndian(s[28..], Rate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(s[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(s[34..], 16);
        Encoding.ASCII.GetBytes("data").CopyTo(s[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[40..], n * 2);
        for (var i = 0; i < n; i++)
            BinaryPrimitives.WriteInt16LittleEndian(s[(44 + (i * 2))..], (short)(12000 * Math.Sin(2 * Math.PI * hertz * i / Rate)));
        return wav;
    }

    private static double Seconds(byte[] wav) => (wav.Length - 44) / 2.0 / Rate;

    /// <summary>Upward zero crossings per second, away from the edges.</summary>
    private static double Pitch(byte[] wav)
    {
        var n = (wav.Length - 44) / 2;
        short At(int i) => BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + (i * 2)));
        int from = n / 10, to = n * 9 / 10, crossings = 0;
        for (var i = from; i < to; i++)
            if (At(i) < 0 && At(i + 1) >= 0) crossings++;
        return crossings / ((to - from) / (double)Rate);
    }

    [Fact]
    public void Faster_is_shorter_at_the_same_pitch()
    {
        var clip = Tone(220, 2);

        var fast = WavTempo.Apply(clip, 1.1);

        Seconds(fast).ShouldBe(2 / 1.1, 0.01);
        Pitch(fast).ShouldBe(220, 3);
    }

    [Fact]
    public void Normal_speed_returns_the_clip_untouched()
    {
        var clip = Tone(220, 0.5);

        WavTempo.Apply(clip, 1).ShouldBeSameAs(clip);
    }

    [Fact]
    public void What_is_not_a_plain_wav_is_left_alone()
    {
        var junk = new byte[100];

        WavTempo.Apply(junk, 1.1).ShouldBeSameAs(junk);
    }

    [Fact]
    public void A_clip_too_short_to_stretch_still_plays()
    {
        Seconds(WavTempo.Apply(Tone(220, 0.02), 1.1)).ShouldBe(0.02, 0.001);
    }
}
