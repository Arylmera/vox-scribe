using Murmur.Abstractions;
using Murmur.Dictionary;

namespace Murmur.Core;

/// <summary>What the engine is doing right now.</summary>
public enum DictationState
{
    /// <summary>Waiting for the hotkey.</summary>
    Idle,

    /// <summary>The key is held; audio is being captured.</summary>
    Recording,

    /// <summary>The key is released; the utterance is being transcribed.</summary>
    Transcribing,
}

/// <summary>One completed dictation.</summary>
public sealed record DictationResult(
    DateTimeOffset At,
    TimeSpan AudioDuration,
    TimeSpan ProcessingTime,
    string Text,
    IReadOnlyList<AppliedCorrection> Corrections);

/// <summary>
/// The whole dictation flow: hotkey down, capture, transcribe as you speak, correct, inject.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately platform-neutral. It targets plain <c>net10.0</c>, so <c>CA1416</c> turns any
/// accidental Windows API call in here into a build error. Everything platform-specific
/// arrives through the four interfaces it is constructed with.
/// </para>
/// <para>
/// That is what makes the interesting behaviour testable without Windows: hand it fakes and
/// the entire path — including segmentation, the correction pass and the "nothing was said"
/// case — runs on any machine, in milliseconds.
/// </para>
/// <para>
/// <b>Transcription overlaps with speech.</b> <see cref="StreamingSegmenter"/> closes a
/// segment at every pause and it goes to the engine immediately, while the user keeps
/// talking. What is left at key release is one tail, so the wait after the key comes up is
/// roughly constant instead of growing with the length of the dictation.
/// </para>
/// </remarks>
public sealed class DictationEngine : IAsyncDisposable
{
    private readonly IAudioCapture _capture;
    private readonly IHotkeySource _hotkey;
    private readonly ITranscriber _transcriber;
    private readonly ITextInjector _injector;
    private readonly IClock _clock;
    private readonly Func<IReadOnlyList<DictionaryEntry>> _dictionary;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Guards the segmenter and the segment chain. Held for microseconds at buffer rate; the
    /// alternative — taking <see cref="_gate"/> per chunk — would put the capture loop behind
    /// the state machine.
    /// </summary>
    private readonly Lock _segments = new();

    private CancellationTokenSource? _recording;
    private StreamingSegmenter? _segmenter;
    private List<Task<Segment>> _queued = [];
    private Task _chain = Task.CompletedTask;
    private DictionaryCorrector? _corrector;
    private IReadOnlyList<string> _bias = [];
    private int _capturedSamples;

    /// <summary>Current state.</summary>
    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>Most recent input level, 0…1. Drives the meter.</summary>
    public float Level { get; private set; }

    /// <summary>
    /// Everything transcribed so far in the utterance in progress, corrected. Empty when
    /// idle. Drives the HUD's live preview.
    /// </summary>
    public string PartialText { get; private set; } = string.Empty;

    /// <summary>Raised when a dictation completes and produced text.</summary>
    public event EventHandler<DictationResult>? Completed;

    /// <summary>
    /// Raised whenever <see cref="State"/>, <see cref="Level"/> or <see cref="PartialText"/>
    /// changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Wires the engine to its platform implementations.</summary>
    /// <param name="capture">Microphone source.</param>
    /// <param name="hotkey">Push-to-talk source.</param>
    /// <param name="transcriber">Speech engine.</param>
    /// <param name="injector">Where finished text goes.</param>
    /// <param name="dictionary">
    /// Read fresh on every utterance rather than captured once, so edits take effect without
    /// a restart.
    /// </param>
    /// <param name="clock">Time source; defaults to the system clock.</param>
    public DictationEngine(
        IAudioCapture capture,
        IHotkeySource hotkey,
        ITranscriber transcriber,
        ITextInjector injector,
        Func<IReadOnlyList<DictionaryEntry>> dictionary,
        IClock? clock = null)
    {
        _capture = capture;
        _hotkey = hotkey;
        _transcriber = transcriber;
        _injector = injector;
        _dictionary = dictionary;
        _clock = clock ?? SystemClock.Instance;

        _hotkey.Pressed += OnPressed;
        _hotkey.Released += OnReleased;
    }

    /// <summary>Arms the hotkey.</summary>
    /// <returns>False if the hook could not be installed.</returns>
    public bool Start() => _hotkey.Start();

    /// <summary>
    /// Starts or stops recording from a button rather than the hotkey.
    /// </summary>
    /// <remarks>
    /// Routed through the same state machine as the hotkey, deliberately. Two independent
    /// paths into recording would eventually disagree about whether it is running.
    /// </remarks>
    public void TogglePushToTalk()
    {
        if (State == DictationState.Idle) _ = BeginAsync();
        else if (State == DictationState.Recording) _ = EndAsync();
    }

    /// <summary>
    /// When true the shortcut toggles: one press starts, the next stops, releases are
    /// ignored. When false (the default) it is hold-to-talk. Safe to flip live.
    /// </summary>
    public bool ToggleMode { get; set; }

    /// <summary>
    /// When true each segment is typed the moment it is transcribed, so text appears while
    /// the user is still speaking. When false (the default) the whole utterance is typed once
    /// at the end.
    /// </summary>
    /// <remarks>
    /// Off by default because incremental typing follows the caret: move it mid-sentence and
    /// the rest of the dictation lands at the new spot. The live preview in the HUD shows the
    /// same text either way, so the wait disappears in both modes — this only chooses where
    /// the text goes while it is being spoken.
    /// </remarks>
    public bool IncrementalInjection { get; set; }

    /// <summary>
    /// Optional repair pass applied to the finished utterance before it is reported and
    /// typed. Null leaves the transcript exactly as the engine produced it.
    /// </summary>
    /// <remarks>
    /// A delegate rather than an interface: there is one implementation
    /// (<see cref="TextCleaner"/>), and the dictionary is already passed in as a
    /// <see cref="Func{TResult}"/> for the same reason. It must never throw — the whole point
    /// of the pass is that a dead gateway costs nothing.
    /// </remarks>
    public Func<string, CancellationToken, Task<string>>? Cleanup { get; set; }

    private void OnPressed(object? sender, EventArgs e)
    {
        if (ToggleMode) TogglePushToTalk();
        else _ = BeginAsync();
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        // In toggle mode the release of the starting press must not stop the recording.
        if (!ToggleMode) _ = EndAsync();
    }

    private async Task BeginAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != DictationState.Idle) return;

            var entries = _dictionary();
            _corrector = new DictionaryCorrector(entries);
            _bias = DictionaryCorrector.BiasPhrases(entries);

            lock (_segments)
            {
                _segmenter = new StreamingSegmenter();
                _queued = [];
                _chain = Task.CompletedTask;
            }

            _capturedSamples = 0;
            PartialText = string.Empty;
            _recording = new CancellationTokenSource();
            SetState(DictationState.Recording);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await foreach (var chunk in _capture.CaptureAsync(_recording!.Token).ConfigureAwait(false))
            {
                // Stop consuming the moment recording ends. Cancellation is cooperative, so
                // chunks already queued still arrive after EndAsync has moved on — and
                // without this guard one of them sets Level back to a reading that has
                // already been zeroed.
                if (State != DictationState.Recording) break;

                _capturedSamples += chunk.Samples.Length;

                // Copied by the segmenter, not referenced: capture implementations are
                // entitled to reuse their buffer the moment this returns.
                lock (_segments)
                {
                    if (_segmenter?.Accept(chunk) is { Length: > 0 } closed) Queue(closed);
                }

                Level = chunk.Rms();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal: the key was released.
        }
        finally
        {
            // Authoritative: this runs only once the capture loop has genuinely finished, so
            // nothing can raise the level afterwards and leave the meter stuck.
            Level = 0;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task EndAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != DictationState.Recording) return;

            await _recording!.CancelAsync().ConfigureAwait(false);
            Level = 0;
            SetState(DictationState.Transcribing);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await ProcessAsync().ConfigureAwait(false);
        }
        finally
        {
            _recording?.Dispose();
            _recording = null;
            SetState(DictationState.Idle);
        }
    }

    /// <summary>Flushes the tail, waits for every segment, then reports and injects.</summary>
    private async Task ProcessAsync()
    {
        // Measured from key release, because that is the wait the user actually feels — and
        // with segments already in flight it is now roughly the length of the tail, not of
        // the whole recording.
        var releasedAt = _clock.Now;

        Task<Segment>[] pending;
        lock (_segments)
        {
            if (_segmenter?.Flush() is { Length: > 0 } tail) Queue(tail);
            _segmenter = null;
            pending = [.. _queued];
            _queued = [];
        }

        // Never faults: every segment task swallows its own failure.
        var segments = await Task.WhenAll(pending).ConfigureAwait(false);

        var spoken = segments.Where(s => s.Text.Length > 0).ToArray();
        if (spoken.Length == 0) return;

        var text = string.Join(' ', spoken.Select(s => s.Text));

        // Before Completed, so the history keeps what was actually typed. Skipped in
        // incremental mode, where the phrases are already in the target window and there is
        // nothing left to improve.
        if (Cleanup is { } cleanup && !IncrementalInjection)
            text = await cleanup(text, CancellationToken.None).ConfigureAwait(false);

        var result = new DictationResult(
            At: releasedAt,
            AudioDuration: TimeSpan.FromSeconds((double)_capturedSamples / AudioChunk.SampleRate),
            ProcessingTime: _clock.Now - releasedAt,
            Text: text,
            Corrections: [.. spoken.SelectMany(s => s.Corrections)]);

        Completed?.Invoke(this, result);

        // In incremental mode every segment was typed as it landed, so there is nothing left
        // to type — injecting here would double the whole utterance.
        if (!IncrementalInjection)
            await _injector.InjectAsync(text, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Adds a closed segment to the ordered transcription chain.</summary>
    /// <remarks>Callers must hold <see cref="_segments"/>.</remarks>
    private void Queue(ReadOnlyMemory<float> piece)
    {
        var task = TranscribeSegmentAsync(_chain, piece);
        _chain = task;
        _queued.Add(task);
    }

    private async Task<Segment> TranscribeSegmentAsync(Task previous, ReadOnlyMemory<float> piece)
    {
        // Strictly one at a time and strictly in order: the segments have to be joined in the
        // order they were spoken, and the local engine already uses every core it wants — two
        // inferences at once make both of them slower.
        // This never faults — see the catch below — so awaiting it cannot cascade a failure
        // down the chain.
        await previous.ConfigureAwait(false);

        try
        {
            var raw = await _transcriber
                .TranscribeAsync(piece, _bias, CancellationToken.None)
                .ConfigureAwait(false);

            var trimmed = raw?.Trim() ?? string.Empty;
            if (trimmed.Length == 0) return Segment.Empty;

            // The dictionary runs on every segment and unconditionally. Biasing only raises
            // the odds of the right word; this is the pass that guarantees it.
            // ponytail: a correction phrase straddling a segment boundary is missed —
            // boundaries sit in pauses, so that is a phrase said with a pause through it.
            var (corrected, applied) = _corrector!.Apply(trimmed);

            var separator = PartialText.Length == 0 ? string.Empty : " ";
            PartialText += separator + corrected;
            Changed?.Invoke(this, EventArgs.Empty);

            if (IncrementalInjection)
            {
                await _injector
                    .InjectAsync(separator + corrected, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            return new Segment(corrected, applied);
        }
        catch (Exception)
        {
            // ponytail: one bad segment loses one segment, not the sentence around it — and
            // the chain stays intact for the ones behind it. There is no logger down here in
            // Core; if this needs diagnosing, add one to the transcriber, which knows why.
            return Segment.Empty;
        }
    }

    private void SetState(DictationState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _hotkey.Pressed -= OnPressed;
        _hotkey.Released -= OnReleased;
        _hotkey.Dispose();

        if (_recording is not null)
        {
            await _recording.CancelAsync().ConfigureAwait(false);
            _recording.Dispose();
        }

        await _capture.DisposeAsync().ConfigureAwait(false);
        await _transcriber.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    /// <summary>One transcribed segment and the corrections it took.</summary>
    private readonly record struct Segment(string Text, IReadOnlyList<AppliedCorrection> Corrections)
    {
        public static Segment Empty { get; } = new(string.Empty, []);
    }
}
