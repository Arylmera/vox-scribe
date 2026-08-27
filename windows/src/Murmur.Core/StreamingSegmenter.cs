using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Cuts a live recording into utterance-sized pieces <b>while the user is still speaking</b>,
/// so transcription overlaps with speech instead of following it.
/// </summary>
/// <remarks>
/// <para>
/// This is the difference between latency that grows with the length of the dictation and
/// latency that does not. <see cref="AudioSegmenter"/> splits a finished recording because
/// the encoder cannot swallow it whole; this splits an unfinished one because waiting for
/// the end is the wait the user actually feels. On release only the tail is left to
/// transcribe — everything before the last pause is already done.
/// </para>
/// <para>
/// <b>Cuts land in pauses, and only in pauses.</b> A cut through a word costs a word, so the
/// hard length cap is a backstop for someone who never breathes, not the normal path.
/// </para>
/// <para>
/// <b>The silence threshold calibrates itself</b> to the loudest moment heard so far in this
/// utterance, with an absolute floor underneath. A fixed threshold is wrong on every
/// microphone that is not the one it was tuned on — a headset boom and a laptop array are
/// two orders of magnitude apart, and the same number cannot be a pause on both.
/// </para>
/// </remarks>
public sealed class StreamingSegmenter
{
    /// <summary>Quiet time that closes a segment.</summary>
    public const double DefaultPauseSeconds = 0.6;

    /// <summary>
    /// Shortest segment worth sending on its own. Below this the per-call overhead dominates
    /// and the recogniser has too little context to punctuate sensibly.
    /// </summary>
    public const double DefaultMinSegmentSeconds = 1.5;

    /// <summary>
    /// Absolute quiet floor, below which audio is a pause no matter how quiet the speaker is.
    /// Sits above a typical room noise floor and well below any speech.
    /// </summary>
    public const float SilenceFloor = 0.01f;

    /// <summary>Fraction of the loudest level heard that still counts as a pause.</summary>
    private const float SilenceFraction = 0.15f;

    private readonly int _pauseSamples;
    private readonly int _minSegmentSamples;
    private readonly int _maxSegmentSamples;

    private readonly List<float> _buffer = [];
    private int _quietSamples;
    private float _peak;

    /// <summary>Builds a segmenter with tunable timings.</summary>
    /// <param name="pauseSeconds">Quiet time that closes a segment.</param>
    /// <param name="minSegmentSeconds">Shortest segment a pause may close.</param>
    /// <param name="maxSegmentSeconds">
    /// Hard cap; defaults to <see cref="AudioSegmenter.MaxSegmentSeconds"/>, which is the
    /// point where a single inference stops fitting comfortably in memory.
    /// </param>
    public StreamingSegmenter(
        double pauseSeconds = DefaultPauseSeconds,
        double minSegmentSeconds = DefaultMinSegmentSeconds,
        double maxSegmentSeconds = AudioSegmenter.MaxSegmentSeconds)
    {
        _pauseSamples = (int)(pauseSeconds * AudioChunk.SampleRate);
        _minSegmentSamples = (int)(minSegmentSeconds * AudioChunk.SampleRate);
        _maxSegmentSamples = (int)(maxSegmentSeconds * AudioChunk.SampleRate);
    }

    /// <summary>Samples held back, waiting for a pause.</summary>
    public int Pending => _buffer.Count;

    /// <summary>
    /// Takes one chunk of live audio.
    /// </summary>
    /// <returns>
    /// A finished segment, ready to transcribe now, or null when the utterance is still
    /// running — the common case, chunk after chunk.
    /// </returns>
    public ReadOnlyMemory<float>? Accept(AudioChunk chunk)
    {
        var span = chunk.Samples.Span;
        if (span.Length == 0) return null;

        _buffer.AddRange(span);

        var level = chunk.Rms();
        if (level > _peak) _peak = level;

        // Trailing quiet is counted, not reset per chunk: a pause is longer than one buffer.
        if (level < Math.Max(SilenceFloor, _peak * SilenceFraction)) _quietSamples += span.Length;
        else _quietSamples = 0;

        var pause = _quietSamples >= _pauseSamples && _buffer.Count >= _minSegmentSamples;
        return pause || _buffer.Count >= _maxSegmentSamples ? Take() : null;
    }

    /// <summary>
    /// Hands back whatever is still buffered and resets. Called once, on key release.
    /// </summary>
    /// <returns>The tail; empty when the last pause happened to close on the last chunk.</returns>
    public ReadOnlyMemory<float> Flush() => _buffer.Count == 0 ? default : Take();

    private ReadOnlyMemory<float> Take()
    {
        var segment = _buffer.ToArray();
        _buffer.Clear();
        _quietSamples = 0;

        // The peak is deliberately *not* reset: it is the calibration for this utterance, and
        // re-learning it from the first chunk of every segment would make a quiet sentence
        // after a loud one read as silence throughout.
        return segment;
    }
}
