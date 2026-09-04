using VoxScribe.Abstractions;
using VoxScribe.Core;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// Pins the segmenter's cutting rules: cuts land in pauses, short segments wait, a
/// breathless speaker hits the hard cap, and silence is never mistaken for speech.
/// </summary>
public sealed class StreamingSegmenterTests
{
    /// <summary>A chunk of constant amplitude, <paramref name="seconds"/> long.</summary>
    private static AudioChunk Chunk(float amplitude, double seconds = 0.1)
    {
        var samples = new float[(int)(seconds * AudioChunk.SampleRate)];
        Array.Fill(samples, amplitude);
        return new AudioChunk(samples);
    }

    /// <summary>Feeds chunks until one closes a segment, or null if none does.</summary>
    private static ReadOnlyMemory<float>? Feed(
        StreamingSegmenter segmenter, float amplitude, double seconds)
    {
        for (var fed = 0; fed < (int)Math.Round(seconds / 0.1); fed++)
        {
            if (segmenter.Accept(Chunk(amplitude)) is { } closed) return closed;
        }

        return null;
    }

    [Fact]
    public void Continuous_speech_is_never_cut_before_the_hard_cap()
    {
        var segmenter = new StreamingSegmenter();

        Feed(segmenter, 0.5f, seconds: 10).ShouldBeNull();
        segmenter.Flush().Length.ShouldBe(10 * AudioChunk.SampleRate);
    }

    [Fact]
    public void A_pause_closes_the_segment_behind_it()
    {
        var segmenter = new StreamingSegmenter();

        Feed(segmenter, 0.5f, seconds: 2).ShouldBeNull();
        var segment = Feed(segmenter, 0.0f, seconds: 1);

        segment.ShouldNotBeNull();
        // The cut carries the speech and the pause that closed it — nothing is left behind.
        segmenter.Pending.ShouldBe(0);
    }

    [Fact]
    public void A_pause_before_the_minimum_length_does_not_cut()
    {
        var segmenter = new StreamingSegmenter();

        // Half a second of speech is below the 1.5 s minimum: the pause must wait.
        Feed(segmenter, 0.5f, seconds: 0.5).ShouldBeNull();
        Feed(segmenter, 0.0f, seconds: 0.7).ShouldBeNull();
        segmenter.Pending.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_hard_cap_cuts_a_speaker_who_never_breathes()
    {
        var segmenter = new StreamingSegmenter(maxSegmentSeconds: 3);

        var segment = Feed(segmenter, 0.5f, seconds: 4);

        segment.ShouldNotBeNull();
        segment.Value.Length.ShouldBeGreaterThanOrEqualTo(3 * AudioChunk.SampleRate);
    }

    [Fact]
    public void The_silence_threshold_calibrates_to_the_speaker()
    {
        var segmenter = new StreamingSegmenter();

        // Loud speech, then audio well above the absolute floor but far below the speaker's
        // level: for this utterance, that quiet is a pause.
        Feed(segmenter, 0.8f, seconds: 2).ShouldBeNull();
        Feed(segmenter, 0.02f, seconds: 1).ShouldNotBeNull();
    }

    [Fact]
    public void Flush_is_empty_when_the_last_pause_closed_on_the_last_chunk()
    {
        var segmenter = new StreamingSegmenter();

        Feed(segmenter, 0.5f, seconds: 2);
        Feed(segmenter, 0.0f, seconds: 1);

        segmenter.Flush().Length.ShouldBe(0);
    }

    [Fact]
    public void HasSpeech_rejects_silence_and_hears_one_word_in_a_long_quiet()
    {
        StreamingSegmenter.HasSpeech(new float[AudioChunk.SampleRate * 2]).ShouldBeFalse();
        StreamingSegmenter.HasSpeech([]).ShouldBeFalse();

        // Ten seconds of quiet with one 20 ms word in the middle: the overall RMS is nothing,
        // and it is still speech.
        var quiet = new float[AudioChunk.SampleRate * 10];
        Array.Fill(quiet, 0.5f, quiet.Length / 2, AudioChunk.SampleRate / 50);
        StreamingSegmenter.HasSpeech(quiet).ShouldBeTrue();
    }
}
