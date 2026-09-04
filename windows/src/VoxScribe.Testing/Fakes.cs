using System.Runtime.CompilerServices;
using VoxScribe.Abstractions;

namespace VoxScribe.Testing;

/// <summary>
/// Test doubles for the four platform interfaces.
/// </summary>
/// <remarks>
/// These are what make the Windows app's behaviour verifiable from a machine that is not
/// Windows. Everything interesting — the state machine, chunking, the correction pass, the
/// silence case — runs against these in milliseconds, on any platform, in CI.
/// </remarks>
public sealed class FakeAudioCapture : IAudioCapture
{
    private readonly float[] _samples;
    private readonly int _chunkSize;

    /// <summary>Replays a fixed buffer as a series of chunks.</summary>
    /// <param name="samples">16 kHz mono float.</param>
    /// <param name="chunkSize">Samples per chunk; defaults to 20 ms.</param>
    public FakeAudioCapture(float[] samples, int chunkSize = AudioChunk.SampleRate / 50)
    {
        _samples = samples;
        _chunkSize = Math.Max(1, chunkSize);
    }

    /// <summary>Generates <paramref name="seconds"/> of a steady tone at <paramref name="amplitude"/>.</summary>
    public static FakeAudioCapture Tone(double seconds, float amplitude = 0.5f)
    {
        var count = (int)(seconds * AudioChunk.SampleRate);
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = amplitude * MathF.Sin(2 * MathF.PI * 440 * i / AudioChunk.SampleRate);
        }
        return new FakeAudioCapture(samples);
    }

    /// <summary>
    /// Generates speech separated by pauses: <paramref name="phrases"/> bursts of tone of
    /// <paramref name="phraseSeconds"/> each, with <paramref name="pauseSeconds"/> of silence
    /// between them.
    /// </summary>
    /// <remarks>
    /// This is what exercises <c>StreamingSegmenter</c>: a single tone never has a pause to
    /// cut at, so it can only ever produce one segment.
    /// </remarks>
    public static FakeAudioCapture Phrases(
        int phrases,
        double phraseSeconds = 2.0,
        double pauseSeconds = 1.0,
        float amplitude = 0.5f)
    {
        var phraseCount = (int)(phraseSeconds * AudioChunk.SampleRate);
        var pauseCount = (int)(pauseSeconds * AudioChunk.SampleRate);

        var samples = new List<float>();
        for (var phrase = 0; phrase < phrases; phrase++)
        {
            if (phrase > 0) samples.AddRange(new float[pauseCount]);
            for (var i = 0; i < phraseCount; i++)
            {
                samples.Add(amplitude * MathF.Sin(2 * MathF.PI * 440 * i / AudioChunk.SampleRate));
            }
        }

        return new FakeAudioCapture([.. samples]);
    }

    /// <summary>Generates <paramref name="seconds"/> of digital silence.</summary>
    public static FakeAudioCapture Silence(double seconds) =>
        new(new float[(int)(seconds * AudioChunk.SampleRate)]);

    /// <inheritdoc />
    public bool IsCapturing { get; private set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IsCapturing = true;
        try
        {
            for (var offset = 0; offset < _samples.Length; offset += _chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(_chunkSize, _samples.Length - offset);

                // A fresh array per chunk, deliberately: a fake that reuses one buffer would
                // hide the very aliasing bug real capture implementations are prone to.
                var chunk = new float[length];
                Array.Copy(_samples, offset, chunk, 0, length);
                yield return new AudioChunk(chunk);

                await Task.Yield();
            }
        }
        finally
        {
            IsCapturing = false;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>A hotkey you press from a test.</summary>
public sealed class FakeHotkeySource : IHotkeySource
{
    /// <inheritdoc />
    public event EventHandler? Pressed;

    /// <inheritdoc />
    public event EventHandler? Released;

    /// <summary>Whether <see cref="Start"/> has been called.</summary>
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public bool Start() { IsRunning = true; return true; }

    /// <inheritdoc />
    public void StopListening() => IsRunning = false;

    /// <summary>Raises <see cref="Pressed"/>.</summary>
    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="Released"/>.</summary>
    public void Release() => Released?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose() => StopListening();
}

/// <summary>Returns canned transcripts and records what it was asked to transcribe.</summary>
public sealed class FakeTranscriber : ITranscriber
{
    private readonly Queue<string> _responses;

    /// <summary>Each call returns the next response; the last repeats once exhausted.</summary>
    public FakeTranscriber(params string[] responses) => _responses = new Queue<string>(responses);

    /// <summary>How many segments were submitted. Reveals chunking behaviour.</summary>
    public List<int> SegmentLengths { get; } = [];

    /// <summary>The bias list handed over on the most recent call.</summary>
    public IReadOnlyList<string> LastBias { get; private set; } = [];

    /// <inheritdoc />
    public bool IsReady { get; private set; }

    /// <inheritdoc />
    public ValueTask<bool> LoadAsync(CancellationToken cancellationToken)
    {
        IsReady = true;
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc />
    public ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        IReadOnlyList<string> biasPhrases,
        CancellationToken cancellationToken)
    {
        SegmentLengths.Add(samples.Length);
        LastBias = biasPhrases;
        var text = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        return ValueTask.FromResult(text);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Records what would have been typed.
/// </summary>
/// <remarks>
/// Asserting on intent rather than on an OS effect is deliberate: whether a keystroke really
/// lands in another application's text field is the one thing CI cannot observe, because
/// runners cannot take the foreground.
/// </remarks>
public sealed class RecordingTextInjector : ITextInjector
{
    /// <summary>Everything injected, in order.</summary>
    public List<string> Injected { get; } = [];

    /// <inheritdoc />
    public ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
    {
        Injected.Add(text);
        return ValueTask.FromResult(true);
    }
}

/// <summary>A focus anchor that records when it was captured and restored.</summary>
public sealed class FakeFocusAnchor : IFocusAnchor
{
    private readonly RecordingTextInjector _injector;

    /// <summary>Builds an anchor that watches <paramref name="injector"/> to prove ordering.</summary>
    public FakeFocusAnchor(RecordingTextInjector injector) => _injector = injector;

    /// <summary>When true, <see cref="CaptureAsync"/> returns null — a failed capture.</summary>
    public bool CaptureReturnsNull { get; set; }

    /// <summary>How many captures were requested.</summary>
    public int Captures { get; private set; }

    /// <summary>Every target handed out, in order.</summary>
    public List<FakeFocusTarget> Targets { get; } = [];

    /// <inheritdoc />
    public ValueTask<IFocusTarget?> CaptureAsync(CancellationToken cancellationToken)
    {
        Captures++;
        if (CaptureReturnsNull) return ValueTask.FromResult<IFocusTarget?>(null);

        var target = new FakeFocusTarget(_injector);
        Targets.Add(target);
        return ValueTask.FromResult<IFocusTarget?>(target);
    }
}

/// <summary>A target that notes how much had already been typed when it was restored.</summary>
public sealed class FakeFocusTarget : IFocusTarget
{
    private readonly RecordingTextInjector _injector;

    /// <summary>Builds a target watching <paramref name="injector"/>.</summary>
    public FakeFocusTarget(RecordingTextInjector injector) => _injector = injector;

    /// <summary>How many times <see cref="RestoreAsync"/> ran.</summary>
    public int Restores { get; private set; }

    /// <summary>
    /// Number of strings the injector had received when the first restore ran, or -1 if
    /// never restored. Zero proves restore happened before typing.
    /// </summary>
    public int InjectedWhenRestored { get; private set; } = -1;

    /// <inheritdoc />
    public ValueTask<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        if (Restores++ == 0) InjectedWhenRestored = _injector.Injected.Count;
        return ValueTask.FromResult(true);
    }
}

/// <summary>A clock you advance by hand.</summary>
public sealed class FakeClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Moves time forward.</summary>
    public void Advance(TimeSpan by) => Now += by;
}
