using Shouldly;
using VoxScribe.Abstractions;
using VoxScribe.Core;
using VoxScribe.Testing;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>Toggle mode ends itself once the user has spoken and then gone quiet.</summary>
public sealed class AutoStopTests
{
    /// <summary><paramref name="speech"/> s of tone, then <paramref name="quiet"/> s of silence.</summary>
    private static FakeAudioCapture SpeechThenQuiet(double speech, double quiet)
    {
        var tone = (int)(speech * AudioChunk.SampleRate);
        var samples = new float[tone + (int)(quiet * AudioChunk.SampleRate)];
        for (var i = 0; i < tone; i++) samples[i] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / AudioChunk.SampleRate);
        return new FakeAudioCapture(samples);
    }

    private static DictationEngine Build(IAudioCapture capture, FakeHotkeySource hotkey, RecordingTextInjector injector, bool toggle, double autoStop) =>
        new(capture, hotkey, new FakeTranscriber("bonjour"), injector, () => [], new FakeClock())
        {
            ToggleMode = toggle,
            AutoStopSilenceSeconds = autoStop,
        };

    /// <summary>Presses once and waits for the capture to run dry; never presses again.</summary>
    private static async Task PressOnceAndWaitAsync(FakeHotkeySource hotkey, DictationEngine engine)
    {
        hotkey.Press();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        for (var i = 0; i < 50000 && engine.State != DictationState.Idle; i++) await Task.Yield();
    }

    /// <summary>
    /// Presses once and waits (bounded) for the fake capture to consume every queued chunk —
    /// so a negative test asserts against the engine's state only once auto-stop has had every
    /// chance to fire, not after an arbitrary yield budget that may or may not have let it.
    /// </summary>
    private static async Task PressOnceAndDrainCaptureAsync(FakeHotkeySource hotkey, FakeAudioCapture capture)
    {
        hotkey.Press();
        for (var i = 0; i < 2000 && !capture.IsCapturing; i++) await Task.Yield();
        for (var i = 0; i < 200000 && capture.IsCapturing; i++) await Task.Yield();
    }

    [Fact]
    public async Task Silence_after_speech_ends_a_toggle_dictation()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(SpeechThenQuiet(1, 4), hotkey, injector, toggle: true, autoStop: 3);

        await PressOnceAndWaitAsync(hotkey, engine);

        // EndAsync runs a little past the capture loop's last chunk (closing the segmenter,
        // transcribing, injecting), so give it a short bounded real-time budget on top.
        for (var i = 0; i < 200 && engine.State != DictationState.Idle; i++) await Task.Delay(10);

        engine.State.ShouldBe(DictationState.Idle);
        injector.Injected.ShouldBe(["bonjour"]);
    }

    [Fact]
    public async Task Silence_before_any_speech_keeps_recording()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Silence(5);
        await using var engine = Build(capture, hotkey, new RecordingTextInjector(), toggle: true, autoStop: 3);

        await PressOnceAndDrainCaptureAsync(hotkey, capture);

        engine.State.ShouldBe(DictationState.Recording);
        await engine.CancelAsync();
    }

    [Fact]
    public async Task Off_means_off()
    {
        var hotkey = new FakeHotkeySource();
        var capture = SpeechThenQuiet(1, 4);
        await using var engine = Build(capture, hotkey, new RecordingTextInjector(), toggle: true, autoStop: 0);

        await PressOnceAndDrainCaptureAsync(hotkey, capture);

        engine.State.ShouldBe(DictationState.Recording);
        await engine.CancelAsync();
    }

    [Fact]
    public async Task Push_to_talk_ignores_auto_stop()
    {
        var hotkey = new FakeHotkeySource();
        var capture = SpeechThenQuiet(1, 4);
        await using var engine = Build(capture, hotkey, new RecordingTextInjector(), toggle: false, autoStop: 3);

        await PressOnceAndDrainCaptureAsync(hotkey, capture);

        engine.State.ShouldBe(DictationState.Recording);
        hotkey.Release();
    }
}
