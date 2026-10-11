using System.Globalization;
using System.Text;
using VoxScribe.Abstractions;
using VoxScribe.Dictionary;

namespace VoxScribe.Core;

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
/// <param name="At">When the key was released.</param>
/// <param name="AudioDuration">How long the key was held.</param>
/// <param name="ProcessingTime">Release to finished text — the wait actually felt.</param>
/// <param name="Text">The final text, after corrections and any cleanup.</param>
/// <param name="Corrections">Dictionary rules that fired.</param>
/// <param name="RawText">
/// The dictionary-corrected text before the cleanup model rewrote it, or null when no
/// cleanup ran or it changed nothing. Feeds the dictionary suggestions.
/// </param>
/// <param name="CleanupTime">How long the cleanup call took; null when none ran.</param>
/// <param name="CleanupFailed">
/// True when cleanup fell back to the raw text; null when none ran.
/// </param>
/// <param name="Edited">
/// True when this is an Edit-chord rewrite rather than a dictation. <paramref name="RawText"/>
/// is then the spoken instruction, not a cleanup candidate — the dictionary miner must skip it.
/// </param>
public sealed record DictationResult(
    DateTimeOffset At,
    TimeSpan AudioDuration,
    TimeSpan ProcessingTime,
    string Text,
    IReadOnlyList<AppliedCorrection> Corrections,
    string? RawText = null,
    TimeSpan? CleanupTime = null,
    bool? CleanupFailed = null,
    bool Edited = false);

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
    private readonly IHotkeySource? _cleanupHotkey;
    private readonly IHotkeySource? _cancelHotkey;
    private readonly IHotkeySource? _undoHotkey;
    private readonly IHotkeySource? _commandHotkey;
    private readonly IHotkeySource? _editHotkey;
    private readonly IFocusAnchor? _focusAnchor;

    /// <summary>Set by <see cref="CancelAsync"/>; segments still in flight then stay untyped.</summary>
    private volatile bool _cancelled;

    /// <summary>Set once auto-stop has fired this utterance, so it ends the dictation once.</summary>
    private volatile bool _autoStopped;

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

    /// <summary>The capture started at press, awaited at release. Null when not anchoring.</summary>
    private Task<IFocusTarget?>? _anchorCapture;

    /// <summary>Fixed at press, like the cleanup flag, so a mid-utterance toggle cannot double-type.</summary>
    private bool _anchoredThisUtterance;

    /// <summary>
    /// Whether the press that started the utterance left a field worth coming back to. False
    /// for the in-app button, which the user reaches by clicking VoxScribe's own window.
    /// </summary>
    private bool _anchorRequested;

    /// <summary>Current state.</summary>
    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>Most recent input level, 0…1. Drives the meter.</summary>
    public float Level { get; private set; }

    /// <summary>
    /// Everything transcribed so far in the utterance in progress, corrected. Empty when
    /// idle. Drives the HUD's live preview.
    /// </summary>
    public string PartialText { get; private set; } = string.Empty;

    /// <summary>
    /// Exact record of what this engine sent to the injector for the current/last dictation.
    /// </summary>
    public InjectionJournal Journal { get; } = new();

    /// <summary>
    /// The most recent failure worth telling the user about, or empty. Cleared when the next
    /// utterance starts.
    /// </summary>
    /// <remarks>
    /// The engine never sets this itself — a transcriber that fails returns an empty string,
    /// by contract. The composition wires the network layers' failure callbacks here, because
    /// the HUD watches the engine and nothing else.
    /// </remarks>
    public string Notice { get; private set; } = string.Empty;

    /// <summary>Publishes <paramref name="notice"/> to whoever is watching the engine.</summary>
    public void ReportNotice(string notice)
    {
        Notice = notice;
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
    /// <param name="cleanupHotkey">
    /// Optional second shortcut. It records exactly like the first, but its utterance goes
    /// through <see cref="Cleanup"/> before it is typed — one key for raw and fast, one for
    /// tidied. Null leaves a single shortcut, and then nothing is ever cleaned.
    /// </param>
    /// <param name="focusAnchor">
    /// Optional. Remembers the focused field at press so the text can be typed there at
    /// release even if the user has moved on. Null means text goes wherever focus is.
    /// </param>
    /// <param name="cancelHotkey">
    /// Optional. Its press while recording throws the utterance away — see
    /// <see cref="CancelAsync"/>. Ignored in every other state, so the key keeps its
    /// ordinary meaning outside a dictation.
    /// </param>
    /// <param name="undoHotkey">
    /// Optional. Its press while idle runs <see cref="UndoLastDictationAsync"/>.
    /// </param>
    /// <param name="commandHotkey">
    /// Optional third shortcut: records like the others, but the finished text goes to the
    /// window named by <see cref="CommandWindowTitle"/> and is submitted with Return. Tidied
    /// first when <see cref="Cleanup"/> is set.
    /// </param>
    /// <param name="editHotkey">
    /// Optional fourth shortcut: records like the others, but the finished text is an
    /// instruction applied to the current selection — see <see cref="Edit"/> — rather than
    /// text typed anywhere.
    /// </param>
    public DictationEngine(
        IAudioCapture capture,
        IHotkeySource hotkey,
        ITranscriber transcriber,
        ITextInjector injector,
        Func<IReadOnlyList<DictionaryEntry>> dictionary,
        IClock? clock = null,
        IHotkeySource? cleanupHotkey = null,
        IFocusAnchor? focusAnchor = null,
        IHotkeySource? cancelHotkey = null,
        IHotkeySource? undoHotkey = null,
        IHotkeySource? commandHotkey = null,
        IHotkeySource? editHotkey = null)
    {
        _capture = capture;
        _hotkey = hotkey;
        _transcriber = transcriber;
        _injector = injector;
        _dictionary = dictionary;
        _clock = clock ?? SystemClock.Instance;
        _focusAnchor = focusAnchor;

        _hotkey.Pressed += OnPlainPressed;
        _hotkey.Released += OnReleased;

        if (cleanupHotkey is not null)
        {
            _cleanupHotkey = cleanupHotkey;
            cleanupHotkey.Pressed += OnCleanupPressed;
            cleanupHotkey.Released += OnReleased;
        }

        if (cancelHotkey is not null)
        {
            _cancelHotkey = cancelHotkey;
            cancelHotkey.Pressed += OnCancelPressed;
        }

        if (undoHotkey is not null)
        {
            _undoHotkey = undoHotkey;
            undoHotkey.Pressed += OnUndoPressed;
        }

        if (commandHotkey is not null)
        {
            _commandHotkey = commandHotkey;
            commandHotkey.Pressed += OnCommandPressed;
            commandHotkey.Released += OnReleased;
        }

        if (editHotkey is not null)
        {
            _editHotkey = editHotkey;
            editHotkey.Pressed += OnEditPressed;
            editHotkey.Released += OnReleased;
        }
    }

    /// <summary>
    /// Title fragment of the window command-mode dictations are sent to. "Claude" by default:
    /// it matches both the desktop app and a terminal tab running Claude Code.
    /// </summary>
    public string CommandWindowTitle { get; set; } = "Claude";

    /// <summary>Whether the utterance in progress is a command-mode one. Drives the badge.</summary>
    public bool CommandThisUtterance { get; private set; }

    /// <summary>
    /// The model load kicked off by <see cref="Start"/>; every segment waits on it before
    /// transcribing. Completed-false until then, so an engine that was never started still
    /// transcribes with whatever the transcriber can do unloaded.
    /// </summary>
    private Task _loading = Task.CompletedTask;

    /// <summary>Arms the hotkey and starts loading the speech model.</summary>
    /// <returns>False if the hook could not be installed.</returns>
    public bool Start()
    {
        // In the background, because the local model takes seconds to load and this runs on
        // the UI thread. Nothing called LoadAsync at all before this line existed, so the
        // local engine sat unloaded and answered every utterance with an empty string.
        if (!_transcriber.IsReady)
        {
            _loading = Task.Run(async () =>
            {
                try
                {
                    if (!await _transcriber.LoadAsync(CancellationToken.None).ConfigureAwait(false))
                        ReportNotice("Speech model not loaded — check Settings → SPEECH");
                }
                catch (Exception e)
                {
                    ReportNotice($"Speech model failed to load — {e.Message}");
                }
            });
        }

        // The other shortcuts are conveniences: if their hooks fail to install, dictation
        // must still work from the first.
        _cleanupHotkey?.Start();
        _cancelHotkey?.Start();
        _undoHotkey?.Start();
        _commandHotkey?.Start();
        _editHotkey?.Start();
        return _hotkey.Start();
    }

    /// <summary>
    /// Starts or stops recording from a button rather than the hotkey.
    /// </summary>
    /// <remarks>
    /// Routed through the same state machine as the hotkey, deliberately. Two independent
    /// paths into recording would eventually disagree about whether it is running.
    /// </remarks>
    public void TogglePushToTalk()
    {
        // Clicking the button focuses VoxScribe, so the field that had focus at press is our
        // own window: restoring it at release would type the utterance back into the app.
        if (State == DictationState.Idle)
        {
            _anchorRequested = false;
            // Otherwise the button would inherit the Edit chord's mode from whatever utterance
            // ran last, and start copying a selection instead of typing what is said.
            EditThisUtterance = false;
        }
        Toggle();
    }

    /// <summary>
    /// Starts a command dictation from the Claude Code band's microphone button. Ignored unless
    /// idle — the engine already refuses a second start.
    /// </summary>
    /// <remarks>Never anchors: the band's own session is the target, found at release.</remarks>
    public void BeginCommand()
    {
        if (State != DictationState.Idle) return;
        _cleanThisUtterance = true;
        CommandThisUtterance = true;
        EditThisUtterance = false;
        _anchorRequested = false;
        _ = BeginAsync();
    }

    /// <summary>
    /// Ends the dictation in progress as a key release would, in either mode: toggle mode ignores
    /// releases, and the band's Envoyer button must still stop. No-op unless recording.
    /// </summary>
    public void EndUtterance() => _ = EndAsync();

    /// <summary>
    /// Hands a finished command to the armed Claude Code session before the window-title path is
    /// tried. Null — no plugin bridge — leaves command mode exactly as it was.
    /// </summary>
    public Func<string, CancellationToken, Task<ClaudeDelivery>>? DeliverToClaude { get; set; }

    /// <summary>
    /// Rewrites a selection: (selection, instruction) → the edit. Null when no endpoint is
    /// configured, which makes the Edit chord explain itself instead of recording. May throw;
    /// the engine catches whatever it throws and reports it as a notice rather than letting it
    /// escape the fire-and-forget end path.
    /// </summary>
    public Func<string, string, CancellationToken, Task<EditOutcome>>? Edit { get; set; }

    /// <summary>True while the current or last utterance came from the Edit chord.</summary>
    public bool EditThisUtterance { get; private set; }

    /// <summary>Longest selection an edit will send; past it the edit is refused.</summary>
    public const int MaxEditSelection = 8000;

    // Backspace pacing mirrors the Windows injector's typing cadence (bursts with a
    // small settle gap) so slow target apps don't drop keystrokes.
    private const int BackspaceBurst = 40;
    private const int BackspaceGapMilliseconds = 4;

    /// <summary>
    /// Deletes the last dictation's injected text by sending one backspace per
    /// text element (grapheme — surrogate pairs are one backspace, not two). If that
    /// dictation was an edit applied to a selection, its original text is re-typed.
    /// Only runs while idle; false when there is nothing to undo or a keystroke failed.
    /// </summary>
    public async Task<bool> UndoLastDictationAsync(CancellationToken cancellationToken)
    {
        if (State != DictationState.Idle) return false;

        var text = Journal.InjectedText;
        if (text.Length == 0) return false;
        var replaced = Journal.Replaced;

        if (!await BackspaceAsync(text, cancellationToken).ConfigureAwait(false)) return false;
        if (replaced is null) return true;

        // An edit replaced a selection: put it back, then forget, so a second undo does nothing.
        // ponytail: one backspace per grapheme — undoing an 8 000-character edit takes about a
        // second; selecting the result would need a platform selection API.
        var restored = await _injector.InjectAsync(replaced, cancellationToken).ConfigureAwait(false);
        Journal.BeginDictation();
        return restored;
    }

    /// <summary>Takes <paramref name="text"/> back out of the target, one backspace per grapheme.</summary>
    private async Task<bool> BackspaceAsync(string text, CancellationToken cancellationToken)
    {
        var keystrokes = new StringInfo(text).LengthInTextElements;
        for (var i = 0; i < keystrokes; i++)
        {
            if (!await _injector.BackspaceAsync(cancellationToken).ConfigureAwait(false))
                return false;

            if ((i + 1) % BackspaceBurst == 0)
                await Task.Delay(BackspaceGapMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        Journal.Retract(text.Length);
        return true;
    }

    /// <summary>
    /// Throws the utterance in progress away: stops the microphone, drops every segment
    /// still in flight, and takes back whatever incremental mode already typed. Nothing is
    /// reported to <see cref="Completed"/>. No-op unless recording.
    /// </summary>
    public async Task CancelAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != DictationState.Recording) return;

            _cancelled = true;
            await _recording!.CancelAsync().ConfigureAwait(false);
            Level = 0;
            // Transcribing, not Idle yet: a release arriving now must not start a second
            // end, and the pill keeps showing "working" until the typed text is gone.
            SetState(DictationState.Transcribing);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            Task<Segment>[] pending;
            lock (_segments)
            {
                _segmenter = null;
                pending = [.. _queued];
                _queued = [];
            }

            // Segments already past the transcriber may have typed before the flag was
            // seen; wait for the chain so the journal is final before it is replayed.
            await Task.WhenAll(pending).ConfigureAwait(false);

            var typed = Journal.InjectedText;
            if (typed.Length > 0)
                await BackspaceAsync(typed, CancellationToken.None).ConfigureAwait(false);

            PartialText = string.Empty;
            ReportNotice("Cancelled");
        }
        finally
        {
            _recording?.Dispose();
            _recording = null;
            SetState(DictationState.Idle);
        }
    }

    private void Toggle()
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
    /// Toggle mode only: seconds of quiet, after speech was heard, that end the dictation as a
    /// second press would. 0 turns it off. Safe to change live.
    /// </summary>
    public double AutoStopSilenceSeconds { get; set; }

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
    /// <para>
    /// Applies to raw dictation only. The cleanup shortcut always types once, at the end —
    /// see <see cref="InjectIncrementally"/>.
    /// </para>
    /// </remarks>
    public bool IncrementalInjection { get; set; }

    /// <summary>
    /// When true, spoken punctuation — "virgule", "point", "new line" — becomes the mark
    /// itself, after dictionary correction and before anything is typed or previewed.
    /// Off by default; see <see cref="VoiceCommandProcessor"/>.
    /// </summary>
    public bool SpokenPunctuation { get; set; }

    /// <summary>
    /// Whether to type into the field that had focus at press. Overrides
    /// <see cref="IncrementalInjection"/> for the utterance: phrases are held and typed
    /// together at release, because typing them as they land would send them wherever the
    /// user is clicking at that moment.
    /// </summary>
    public bool AnchorFocus { get; set; }

    /// <summary>
    /// Whether the utterance in progress is being typed phrase by phrase.
    /// </summary>
    /// <remarks>
    /// The cleanup shortcut overrides the setting rather than being disabled by it. Asking
    /// for a tidied dictation is a per-utterance instruction; incremental typing is a
    /// standing preference, and the two cannot both be honoured — text already in the target
    /// window cannot be repaired. The specific request wins, and the setting keeps its full
    /// effect on every raw dictation.
    /// </remarks>
    private bool InjectIncrementally =>
        IncrementalInjection && !_cleanThisUtterance && !EditThisUtterance && !_anchoredThisUtterance;

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
    public Func<string, CancellationToken, Task<CleanupOutcome>>? Cleanup { get; set; }

    /// <summary>
    /// True when the utterance being recorded was started by the cleanup shortcut, and must
    /// therefore go through <see cref="Cleanup"/> before it is typed.
    /// </summary>
    /// <remarks>
    /// Set on press rather than read on release: the choice belongs to the key the user
    /// actually held, and rebinding or reconfiguring mid-utterance must not change it.
    /// </remarks>
    private bool _cleanThisUtterance;

    /// <summary>
    /// Whether the utterance in progress will actually be tidied before it is typed.
    /// </summary>
    /// <remarks>
    /// Reports the outcome, not the intention: pressing the cleanup shortcut with no endpoint
    /// configured, or while typing each phrase as it is spoken, changes nothing about the text
    /// — and a badge that promised otherwise would be lying at the one moment the user is
    /// looking at it.
    /// </remarks>
    public bool CleaningThisUtterance =>
        _cleanThisUtterance && Cleanup is not null;

    private void OnPlainPressed(object? sender, EventArgs e) => Pressed(cleanup: false, command: false, edit: false, sender, e);

    private void OnCleanupPressed(object? sender, EventArgs e) => Pressed(cleanup: true, command: false, edit: false, sender, e);

    // A command is tidied when a cleaner exists — what reaches Claude should read well — and
    // it never anchors: the target is found by title at release, not remembered from press.
    private void OnCommandPressed(object? sender, EventArgs e) => Pressed(cleanup: true, command: true, edit: false, sender, e);

    private void OnUndoPressed(object? sender, EventArgs e) => _ = UndoLastDictationAsync(CancellationToken.None);

    // An edit is never cleaned (the instruction is not the text) and anchors like a dictation:
    // the selection lives in the window that had focus at press. Whether an endpoint is
    // configured is checked at release, in EditSelectionAsync — not here, so the chord
    // records like any other and the pill is visible when the explanation lands.
    private void OnEditPressed(object? sender, EventArgs e) =>
        Pressed(cleanup: false, command: false, edit: true, sender, e);

    /// <summary>
    /// Chooses the mode, then runs the shared press path.
    /// </summary>
    /// <remarks>
    /// Only an idle engine takes a new mode. In toggle mode the second press is a stop, not a
    /// re-choice, and with two chords that share a key both hooks can fire for one gesture —
    /// letting either flip the mode mid-utterance would mean phrases already typed
    /// incrementally and then typed again, whole, at the end.
    /// </remarks>
    private void Pressed(bool cleanup, bool command, bool edit, object? sender, EventArgs e)
    {
        if (State == DictationState.Idle)
        {
            _cleanThisUtterance = cleanup;
            CommandThisUtterance = command;
            EditThisUtterance = edit;
            _anchorRequested = !command;
        }

        OnPressed(sender, e);
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        if (ToggleMode) Toggle();
        else _ = BeginAsync();
    }

    private void OnCancelPressed(object? sender, EventArgs e) => _ = CancelAsync();

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

            // Started, not awaited: capture must never delay the first audio chunk. It is
            // collected at release, just before typing.
            _anchoredThisUtterance = AnchorFocus && _anchorRequested && _focusAnchor is not null;
            _anchorCapture = _anchoredThisUtterance
                ? _focusAnchor!.CaptureAsync(CancellationToken.None).AsTask()
                : null;

            _capturedSamples = 0;
            _zeroSamples = 0;
            _cancelled = false;
            _autoStopped = false;
            PartialText = string.Empty;
            Notice = string.Empty;
            Journal.BeginDictation();
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
                DetectBlockedMicrophone(chunk);

                // Copied by the segmenter, not referenced: capture implementations are
                // entitled to reuse their buffer the moment this returns.
                bool quietLongEnough;
                lock (_segments)
                {
                    if (_segmenter?.Accept(chunk) is { Length: > 0 } closed) Queue(closed);

                    quietLongEnough = ToggleMode
                        && AutoStopSilenceSeconds > 0
                        && _segmenter is { HeardSpeech: true } s
                        && s.TrailingQuietSeconds >= AutoStopSilenceSeconds;
                }

                // As if the toggle key were pressed again; the State guard above then ends the loop.
                if (quietLongEnough && !_autoStopped)
                {
                    _autoStopped = true;
                    _ = EndAsync();
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

        var joined = new StringBuilder();
        foreach (var segment in spoken) joined.Append(Separator(joined.ToString())).Append(segment.Text);
        var text = joined.ToString();

        if (EditThisUtterance)
        {
            await EditSelectionAsync(text, releasedAt).ConfigureAwait(false);
            return;
        }

        // Before Completed, so the history keeps what was actually typed. Skipped in
        // incremental mode, where the phrases are already in the target window and there is
        // nothing left to improve.
        string? rawText = null;
        TimeSpan? cleanupTime = null;
        bool? cleanupFailed = null;
        if (_cleanThisUtterance && Cleanup is { } cleanup)
        {
            var beforeCleanup = text;
            var startedAt = _clock.Now;
            var outcome = await cleanup(text, CancellationToken.None).ConfigureAwait(false);
            cleanupTime = _clock.Now - startedAt;
            cleanupFailed = !outcome.Succeeded;
            text = outcome.Text;
            // Only a rewrite is worth keeping: identical raw text is noise for the miner.
            if (!string.Equals(text, beforeCleanup, StringComparison.Ordinal)) rawText = beforeCleanup;
        }

        var result = new DictationResult(
            At: releasedAt,
            AudioDuration: TimeSpan.FromSeconds((double)_capturedSamples / AudioChunk.SampleRate),
            ProcessingTime: _clock.Now - releasedAt,
            Text: text,
            Corrections: [.. spoken.SelectMany(s => s.Corrections)],
            RawText: rawText,
            CleanupTime: cleanupTime,
            CleanupFailed: cleanupFailed);

        Completed?.Invoke(this, result);

        // In incremental mode every segment was typed as it landed, so there is nothing left
        // to type — injecting here would double the whole utterance.
        if (InjectIncrementally) return;

        if (CommandThisUtterance)
        {
            await SendCommandAsync(text).ConfigureAwait(false);
            return;
        }

        await RestoreAnchorAsync().ConfigureAwait(false);

        if (await _injector.InjectAsync(text, CancellationToken.None).ConfigureAwait(false))
            Journal.Record(text);
    }

    /// <summary>
    /// Copies the selection, applies <paramref name="instruction"/> to it and types the result
    /// over it. Every refusal is a notice and types nothing: there is no "raw" fallback for an
    /// edit, and the selection must be left as it was.
    /// </summary>
    private async Task EditSelectionAsync(string instruction, DateTimeOffset releasedAt)
    {
        if (Edit is not { } edit)
        {
            ReportNotice("Set a cleanup endpoint to edit by voice");
            return;
        }

        await RestoreAnchorAsync().ConfigureAwait(false);

        var selection = await _injector.CopySelectionAsync(CancellationToken.None).ConfigureAwait(false);
        if (string.IsNullOrEmpty(selection))
        {
            ReportNotice("Nothing selected");
            return;
        }

        if (selection.Length > MaxEditSelection)
        {
            ReportNotice("Selection too long");
            return;
        }

        var startedAt = _clock.Now;
        EditOutcome outcome;
#pragma warning disable CA1031 // the edit endpoint must never take the fire-and-forget end path down
        try
        {
            outcome = await edit(selection, instruction, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            ReportNotice($"Edit failed — {e.Message}");
            return;
        }
#pragma warning restore CA1031
        var took = _clock.Now - startedAt;

        if (outcome.Text is not { } edited)
        {
            ReportNotice($"Edit failed — {outcome.Failure ?? "no answer"}");
            return;
        }

        Completed?.Invoke(this, new DictationResult(
            At: releasedAt,
            AudioDuration: TimeSpan.FromSeconds((double)_capturedSamples / AudioChunk.SampleRate),
            ProcessingTime: _clock.Now - releasedAt,
            Text: edited,
            // Empty, not the instruction's own corrections: the dictionary runs on what was
            // spoken, and the edit's text is the model's rewrite, not a transcription of it.
            Corrections: [],
            RawText: instruction,
            CleanupTime: took,
            CleanupFailed: false,
            Edited: true));

        // The model call can take up to 30 seconds; the user may have switched windows while
        // it ran, so the field anchored at press is restored again, right before the paste.
        await RestoreAnchorAsync().ConfigureAwait(false);

        if (await _injector.InjectAsync(edited, CancellationToken.None).ConfigureAwait(false))
            Journal.Record(edited, replaced: selection);
    }

    /// <summary>
    /// Brings the field anchored at press back to the foreground. A failed restore is
    /// deliberate silence: the fallback is to type where focus is now, which is what the app
    /// always did.
    /// </summary>
    private async Task RestoreAnchorAsync()
    {
        if (_anchorCapture is { } capture && await capture.ConfigureAwait(false) is { } target)
            await target.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Types <paramref name="text"/> into the command target and submits it. If the window is
    /// not there, or will not come forward, nothing is typed anywhere: a prompt meant for
    /// Claude landing in a spreadsheet is worse than a dictation lost to a notice.
    /// </summary>
    private async Task SendCommandAsync(string text)
    {
        if (DeliverToClaude is { } deliver)
        {
            ClaudeDelivery outcome;
#pragma warning disable CA1031 // any failure of the bridge must stay inside Vox-Scribe
            try
            {
                outcome = await deliver(text, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The outbox may already be written: typing as well could send it twice.
                outcome = ClaudeDelivery.NotAcknowledged;
            }
#pragma warning restore CA1031

            // Not journalled when delivered: undo backspaces into the focused window, and this
            // text was submitted in Claude Code, never typed there.
            if (outcome == ClaudeDelivery.Delivered) return;
            if (outcome == ClaudeDelivery.NotAcknowledged)
            {
                ReportNotice("Claude Code did not take the command — command not sent");
                return;
            }
        }

        var target = _focusAnchor is null
            ? null
            : await _focusAnchor.FindAsync(CommandWindowTitle, CancellationToken.None).ConfigureAwait(false);

        // Two notices, not one: which of the two failed is the whole diagnosis.
        if (target is null)
        {
            ReportNotice($"No window titled “{CommandWindowTitle}” — command not sent");
            return;
        }

        if (!await target.RestoreAsync(CancellationToken.None).ConfigureAwait(false))
        {
            ReportNotice($"Window “{CommandWindowTitle}” would not come forward — command not sent");
            return;
        }

        if (!await _injector.InjectAsync(text, CancellationToken.None).ConfigureAwait(false)) return;

        Journal.Record(text);
        await _injector.EnterAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// What goes between the text so far and the next segment: nothing at the start or after
    /// a spoken newline, otherwise one space.
    /// </summary>
    private static string Separator(ReadOnlySpan<char> soFar) =>
        soFar.Length == 0 || soFar[^1] == '\n' ? string.Empty : " ";

    /// <summary>Adds a closed segment to the ordered transcription chain.</summary>
    /// <remarks>Callers must hold <see cref="_segments"/>.</remarks>
    private void Queue(ReadOnlyMemory<float> piece)
    {
        // Silence is never sent: a recogniser given nothing invents something, and that is
        // where the stray "thank you" at the end of an utterance comes from.
        if (!StreamingSegmenter.HasSpeech(piece.Span)) return;

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
            // Never faults: the load task reports its own failure through the notice.
            await _loading.ConfigureAwait(false);

            var raw = await _transcriber
                .TranscribeAsync(piece, _bias, CancellationToken.None)
                .ConfigureAwait(false);

            var trimmed = raw?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || _cancelled) return Segment.Empty;

            // The dictionary runs on every segment and unconditionally. Biasing only raises
            // the odds of the right word; this is the pass that guarantees it.
            // ponytail: a correction phrase straddling a segment boundary is missed —
            // boundaries sit in pauses, so that is a phrase said with a pause through it.
            var (corrected, applied) = _corrector!.Apply(trimmed);
            if (SpokenPunctuation) corrected = VoiceCommandProcessor.Apply(corrected);

            var separator = Separator(PartialText);
            PartialText += separator + corrected;
            Changed?.Invoke(this, EventArgs.Empty);

            if (InjectIncrementally)
            {
                // Journalled only when delivered: undo replays the journal as backspaces,
                // and a string that never landed must not cost the user its length.
                if (await _injector
                    .InjectAsync(separator + corrected, CancellationToken.None)
                    .ConfigureAwait(false))
                {
                    Journal.Record(separator + corrected);
                }
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

    /// <summary>Exact-zero samples seen in a row this utterance; -1 once the notice went out.</summary>
    private int _zeroSamples;

    /// <summary>How much digital silence means the OS, not the room, is the source.</summary>
    private const double BlockedMicrophoneSeconds = 1.5;

    /// <summary>
    /// When Windows' microphone privacy switch is off, WASAPI does not fail — it delivers
    /// exact zeros. A live microphone always has a noise floor, so a run of precisely zero
    /// is the only tell there is, and this is the one place every chunk passes through.
    /// </summary>
    private void DetectBlockedMicrophone(AudioChunk chunk)
    {
        if (_zeroSamples < 0) return;

        _zeroSamples = chunk.Samples.Span.IndexOfAnyExcept(0f) < 0
            ? _zeroSamples + chunk.Samples.Length
            : 0;

        if (_zeroSamples < BlockedMicrophoneSeconds * AudioChunk.SampleRate) return;

        _zeroSamples = -1;
        ReportNotice("Microphone delivers silence — check Windows microphone privacy settings");
    }

    private void SetState(DictationState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _hotkey.Pressed -= OnPlainPressed;
        _hotkey.Released -= OnReleased;
        _hotkey.Dispose();

        if (_cleanupHotkey is not null)
        {
            _cleanupHotkey.Pressed -= OnCleanupPressed;
            _cleanupHotkey.Released -= OnReleased;
            _cleanupHotkey.Dispose();
        }

        if (_cancelHotkey is not null)
        {
            _cancelHotkey.Pressed -= OnCancelPressed;
            _cancelHotkey.Dispose();
        }

        if (_undoHotkey is not null)
        {
            _undoHotkey.Pressed -= OnUndoPressed;
            _undoHotkey.Dispose();
        }

        if (_commandHotkey is not null)
        {
            _commandHotkey.Pressed -= OnCommandPressed;
            _commandHotkey.Released -= OnReleased;
            _commandHotkey.Dispose();
        }

        if (_editHotkey is not null)
        {
            _editHotkey.Pressed -= OnEditPressed;
            _editHotkey.Released -= OnReleased;
            _editHotkey.Dispose();
        }

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
