using VoxScribe.Abstractions;
using VoxScribe.Core;
using VoxScribe.Dictionary;
using VoxScribe.Testing;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// Exercises the entire dictation path with fakes.
/// </summary>
/// <remarks>
/// This is the substitute for a Windows machine. Everything here runs on any platform, so a
/// regression in the state machine, the chunking, or the correction pass is caught on the
/// developer's own machine rather than discovered by a user on Windows.
/// </remarks>
public sealed class DictationEngineTests
{
    private static DictationEngine Build(
        IAudioCapture capture,
        FakeHotkeySource hotkey,
        ITranscriber transcriber,
        RecordingTextInjector injector,
        params DictionaryEntry[] dictionary) =>
        new(capture, hotkey, transcriber, injector, () => dictionary, new FakeClock());

    private static DictationEngine BuildAnchored(
        FakeHotkeySource hotkey,
        ITranscriber transcriber,
        RecordingTextInjector injector,
        FakeFocusAnchor anchor,
        bool anchorFocus = true,
        bool incremental = false) =>
        new(FakeAudioCapture.Tone(1.0), hotkey, transcriber, injector, () => [], new FakeClock(),
            focusAnchor: anchor)
        {
            AnchorFocus = anchorFocus,
            IncrementalInjection = incremental,
        };

    /// <summary>Presses, waits for capture to drain, then releases.</summary>
    private static async Task DictateAsync(FakeHotkeySource hotkey, DictationEngine engine)
    {
        hotkey.Press();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        for (var i = 0; i < 20000 && engine.Level == 0; i++) await Task.Yield();

        hotkey.Release();
        for (var i = 0; i < 20000 && engine.State != DictationState.Idle; i++) await Task.Yield();
    }

    [Fact]
    public async Task Speech_is_transcribed_corrected_and_injected()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("I use cloud code every day");
        var injector = new RecordingTextInjector();

        await using var engine = Build(
            FakeAudioCapture.Tone(1.0), hotkey, transcriber, injector,
            DictionaryEntry.Correction("cloud code", "Claude Code"));

        DictationResult? completed = null;
        engine.Completed += (_, r) => completed = r;

        await DictateAsync(hotkey, engine);

        injector.Injected.ShouldHaveSingleItem();
        injector.Injected[0].ShouldBe("I use Claude Code every day");

        completed.ShouldNotBeNull();
        completed.Corrections.ShouldHaveSingleItem();
        completed.Corrections[0].To.ShouldBe("Claude Code");
    }

    [Fact]
    public async Task Silence_injects_nothing()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();

        // An engine that heard nothing returns empty — and empty must never be typed.
        await using var engine = Build(
            FakeAudioCapture.Silence(0.5), hotkey, new FakeTranscriber(""), injector);

        hotkey.Press();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        hotkey.Release();
        for (var i = 0; i < 20000 && engine.State != DictationState.Idle; i++) await Task.Yield();

        injector.Injected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Silence_is_never_sent_to_the_recogniser()
    {
        var hotkey = new FakeHotkeySource();
        // Would happily hand back a hallucination if it were ever asked.
        var transcriber = new FakeTranscriber("Thank you.");
        var injector = new RecordingTextInjector();

        await using var engine = Build(
            FakeAudioCapture.Silence(2.0), hotkey, transcriber, injector);

        hotkey.Press();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        hotkey.Release();
        for (var i = 0; i < 20000 && engine.State != DictationState.Idle; i++) await Task.Yield();

        transcriber.SegmentLengths.ShouldBeEmpty();
        injector.Injected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Release_without_press_is_ignored()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(
            FakeAudioCapture.Tone(0.2), hotkey, new FakeTranscriber("hello"), injector);

        hotkey.Release();
        for (var i = 0; i < 500; i++) await Task.Yield();

        engine.State.ShouldBe(DictationState.Idle);
        injector.Injected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dictionary_terms_are_offered_to_the_engine_as_bias()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("anything");

        await using var engine = Build(
            FakeAudioCapture.Tone(0.4), hotkey, transcriber, new RecordingTextInjector(),
            DictionaryEntry.Term("Anthropic"),
            DictionaryEntry.Correction("cloud code", "Claude Code"));

        await DictateAsync(hotkey, engine);

        // Both the plain term and the *write* side of the correction get biased — the whole
        // point is to nudge the recogniser toward the correct spelling.
        transcriber.LastBias.ShouldContain("Anthropic");
        transcriber.LastBias.ShouldContain("Claude Code");
    }

    [Fact]
    public async Task State_returns_to_idle_after_a_dictation()
    {
        var hotkey = new FakeHotkeySource();
        await using var engine = Build(
            FakeAudioCapture.Tone(0.3), hotkey, new FakeTranscriber("done"), new RecordingTextInjector());

        engine.State.ShouldBe(DictationState.Idle);
        await DictateAsync(hotkey, engine);
        engine.State.ShouldBe(DictationState.Idle);
        engine.Level.ShouldBe(0);
    }

    /// <summary>Presses, lets every chunk be captured, then releases.</summary>
    /// <remarks>
    /// Distinct from <see cref="DictateAsync"/>, which releases as soon as any audio has been
    /// heard. Anything that asserts on segmentation needs the whole recording to have gone in
    /// first, or it is asserting on a truncated one.
    /// </remarks>
    private static async Task DictateFullyAsync(
        FakeHotkeySource hotkey, FakeAudioCapture capture, DictationEngine engine)
    {
        hotkey.Press();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        for (var i = 0; i < 200000 && !capture.IsCapturing; i++) await Task.Yield();
        for (var i = 0; i < 200000 && capture.IsCapturing; i++) await Task.Yield();

        hotkey.Release();
        for (var i = 0; i < 200000 && engine.State != DictationState.Idle; i++) await Task.Yield();
    }

    [Fact]
    public async Task Phrases_are_transcribed_at_the_pauses_not_all_at_the_end()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Phrases(3);
        var transcriber = new FakeTranscriber("one", "two", "three");
        var injector = new RecordingTextInjector();

        await using var engine = Build(capture, hotkey, transcriber, injector);
        await DictateFullyAsync(hotkey, capture, engine);

        // Three phrases, two pauses between them: the first two segments were transcribed
        // while the user was still speaking, only the third was left at key release.
        transcriber.SegmentLengths.Count.ShouldBe(3);
        injector.Injected.ShouldHaveSingleItem();
        injector.Injected[0].ShouldBe("one two three");
    }

    [Fact]
    public async Task A_pauseless_phrase_is_still_a_single_segment()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(6);
        var transcriber = new FakeTranscriber("unbroken");

        await using var engine = Build(capture, hotkey, transcriber, new RecordingTextInjector());
        await DictateFullyAsync(hotkey, capture, engine);

        transcriber.SegmentLengths.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Incremental_injection_types_each_phrase_as_it_lands()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Phrases(3);
        var injector = new RecordingTextInjector();

        await using var engine = Build(
            capture, hotkey, new FakeTranscriber("one", "two", "three"), injector);
        engine.IncrementalInjection = true;

        await DictateFullyAsync(hotkey, capture, engine);

        // Typed phrase by phrase, and exactly once in total — the end-of-utterance injection
        // must not repeat what was already typed.
        injector.Injected.ShouldBe(["one", " two", " three"]);
        string.Concat(injector.Injected).ShouldBe("one two three");
    }

    [Fact]
    public async Task The_live_preview_carries_the_text_so_far()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Phrases(2);
        var seen = new List<string>();

        await using var engine = Build(
            capture, hotkey, new FakeTranscriber("hello", "world"), new RecordingTextInjector());
        engine.Changed += (s, _) =>
        {
            var partial = ((DictationEngine)s!).PartialText;
            if (partial.Length > 0 && (seen.Count == 0 || seen[^1] != partial)) seen.Add(partial);
        };

        await DictateFullyAsync(hotkey, capture, engine);

        seen.ShouldBe(["hello", "hello world"]);
    }

    [Fact]
    public async Task A_failing_segment_loses_only_itself()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Phrases(3);
        var injector = new RecordingTextInjector();

        // Throws on the second of three segments.
        var transcriber = new ThrowingOnceTranscriber(2, "one", "two", "three");

        await using var engine = Build(capture, hotkey, transcriber, injector);
        await DictateFullyAsync(hotkey, capture, engine);

        injector.Injected.ShouldHaveSingleItem();
        injector.Injected[0].ShouldBe("one three");
    }


    /// <summary>Starts and stops from the in-app button rather than the shortcut.</summary>
    private static async Task DictateFromButtonAsync(DictationEngine engine)
    {
        engine.TogglePushToTalk();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        for (var i = 0; i < 20000 && engine.Level == 0; i++) await Task.Yield();

        engine.TogglePushToTalk();
        for (var i = 0; i < 20000 && engine.State != DictationState.Idle; i++) await Task.Yield();
    }

    [Fact]
    public async Task The_record_button_does_not_anchor_voxscribes_own_window()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        await using var engine = BuildAnchored(hotkey, new FakeTranscriber("hello there"), injector, anchor);

        await DictateFromButtonAsync(engine);

        // Clicking the button focuses VoxScribe, so there is no field worth returning to.
        anchor.Captures.ShouldBe(0);
        injector.Injected.ShouldBe(["hello there"]);
    }

    [Fact]
    public async Task Anchor_is_captured_at_press_and_restored_before_typing()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        await using var engine = BuildAnchored(hotkey, new FakeTranscriber("hello there"), injector, anchor);

        await DictateAsync(hotkey, engine);

        anchor.Captures.ShouldBe(1);
        anchor.Targets.ShouldHaveSingleItem();
        anchor.Targets[0].Restores.ShouldBe(1);
        anchor.Targets[0].InjectedWhenRestored.ShouldBe(0);
        injector.Injected.ShouldBe(["hello there"]);
    }

    [Fact]
    public async Task Anchoring_holds_phrases_until_release_even_when_incremental_is_on()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        await using var engine = BuildAnchored(
            hotkey, new FakeTranscriber("hello there"), injector, anchor, incremental: true);

        await DictateAsync(hotkey, engine);

        // One string, typed after restore — not one per phrase while the user was elsewhere.
        injector.Injected.ShouldBe(["hello there"]);
        anchor.Targets[0].InjectedWhenRestored.ShouldBe(0);
    }

    [Fact]
    public async Task Anchor_off_neither_captures_nor_restores()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        await using var engine = BuildAnchored(
            hotkey, new FakeTranscriber("hello there"), injector, anchor, anchorFocus: false);

        await DictateAsync(hotkey, engine);

        anchor.Captures.ShouldBe(0);
        injector.Injected.ShouldBe(["hello there"]);
    }

    [Fact]
    public async Task Failed_capture_still_types_into_current_focus()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector) { CaptureReturnsNull = true };

        await using var engine = BuildAnchored(hotkey, new FakeTranscriber("hello there"), injector, anchor);

        await DictateAsync(hotkey, engine);

        anchor.Captures.ShouldBe(1);
        anchor.Targets.ShouldBeEmpty();
        injector.Injected.ShouldBe(["hello there"]);
    }

    [Fact]
    public async Task Empty_utterance_does_not_steal_foreground()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        await using var engine = BuildAnchored(hotkey, new FakeTranscriber(""), injector, anchor);

        await DictateAsync(hotkey, engine);

        anchor.Captures.ShouldBe(1);
        anchor.Targets.ShouldHaveSingleItem();
        anchor.Targets[0].Restores.ShouldBe(0);
        injector.Injected.ShouldBeEmpty();
    }

    /// <summary>A transcriber that fails on one nominated call and works on the rest.</summary>
    private sealed class ThrowingOnceTranscriber(int failOnCall, params string[] responses)
        : ITranscriber
    {
        private readonly Queue<string> _responses = new(responses);
        private int _calls;

        public bool IsReady => true;

        public ValueTask<bool> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<string> TranscribeAsync(
            ReadOnlyMemory<float> samples,
            IReadOnlyList<string> biasPhrases,
            CancellationToken cancellationToken)
        {
            var text = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            if (++_calls == failOnCall) throw new InvalidOperationException("engine fell over");
            return ValueTask.FromResult(text);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// The boundary is enforced by the compiler via CA1416, but this fails louder and names
    /// the reason: anything reachable from Core must run in CI on any platform.
    /// </summary>
    [Fact]
    public void Core_does_not_depend_on_any_platform_project()
    {
        var result = Types.InAssembly(typeof(DictationEngine).Assembly)
            .That().ResideInNamespace("VoxScribe.Core")
            .ShouldNot().HaveDependencyOn("VoxScribe.Platform")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "VoxScribe.Core must stay platform-neutral: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}

/// <summary>Chunking behaviour around the encoder's hard limit.</summary>
public sealed class AudioSegmenterTests
{
    private static ReadOnlyMemory<float> Seconds(int n) =>
        new float[n * AudioChunk.SampleRate];

    [Fact]
    public void Short_audio_is_one_segment_and_is_not_copied()
    {
        var audio = Seconds(5);
        var pieces = AudioSegmenter.Split(audio);

        pieces.ShouldHaveSingleItem();
        pieces[0].Length.ShouldBe(audio.Length);
    }

    [Fact]
    public void Audio_at_the_limit_is_still_one_segment()
    {
        AudioSegmenter.Split(Seconds(AudioSegmenter.MaxSegmentSeconds)).ShouldHaveSingleItem();
    }

    [Fact]
    public void Long_audio_is_split_and_every_piece_is_under_the_limit()
    {
        var pieces = AudioSegmenter.Split(Seconds(200));

        pieces.Count.ShouldBeGreaterThan(1);
        foreach (var piece in pieces)
        {
            piece.Length.ShouldBeLessThanOrEqualTo(
                AudioSegmenter.MaxSegmentSeconds * AudioChunk.SampleRate);
            piece.Length.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void Splitting_loses_no_samples()
    {
        var pieces = AudioSegmenter.Split(Seconds(200));
        pieces.Sum(p => p.Length).ShouldBe(200 * AudioChunk.SampleRate);
    }

    /// <summary>
    /// 410 seconds is past the point where the encoder's position table overflows and
    /// inference throws rather than degrading. Nothing may reach it.
    /// </summary>
    [Fact]
    public void Nothing_ever_reaches_the_encoder_ceiling()
    {
        var pieces = AudioSegmenter.Split(Seconds(AudioSegmenter.EncoderCeilingSeconds + 100));

        foreach (var piece in pieces)
        {
            var seconds = (double)piece.Length / AudioChunk.SampleRate;
            seconds.ShouldBeLessThan(AudioSegmenter.EncoderCeilingSeconds);
        }
    }

    [Fact]
    public void A_cut_lands_in_the_quiet_part_rather_than_mid_word()
    {
        // Loud throughout, with one silent second placed inside the search window that
        // precedes the ideal cut point. The cut should be drawn to it.
        var total = AudioSegmenter.MaxSegmentSeconds * 2 * AudioChunk.SampleRate;
        var samples = new float[total];
        Array.Fill(samples, 0.5f);

        var quietStart = (AudioSegmenter.MaxSegmentSeconds - 3) * AudioChunk.SampleRate;
        Array.Clear(samples, quietStart, AudioChunk.SampleRate);

        var pieces = AudioSegmenter.Split(samples);
        var firstCut = pieces[0].Length;

        firstCut.ShouldBeGreaterThanOrEqualTo(quietStart);
        firstCut.ShouldBeLessThanOrEqualTo(quietStart + AudioChunk.SampleRate);
    }
}
