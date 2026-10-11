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

    [Fact]
    public async Task Silence_after_speech_ends_a_toggle_dictation()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(SpeechThenQuiet(1, 4), hotkey, injector, toggle: true, autoStop: 3);

        await PressOnceAndWaitAsync(hotkey, engine);

        // Under a fully parallel test run, the 50000-iteration Task.Yield() budget above can
        // run out around the time the capture loop reaches the quiet threshold (~200 chunk
        // round-trips in), not because EndAsync is stuck — once it fires it runs straight
        // through to Idle in about 10-20ms measured, not tens of iterations. A short, bounded
        // real-time wait (observed ~200ms needed; this leaves headroom) covers that gap. Only
        // this test needs it: it is the only one that expects the state to leave Recording on
        // its own.
        for (var i = 0; i < 200 && engine.State != DictationState.Idle; i++) await Task.Delay(10);

        engine.State.ShouldBe(DictationState.Idle);
        injector.Injected.ShouldBe(["bonjour"]);
    }

    [Fact]
    public async Task Silence_before_any_speech_keeps_recording()
    {
        var hotkey = new FakeHotkeySource();
        await using var engine = Build(FakeAudioCapture.Silence(5), hotkey, new RecordingTextInjector(), toggle: true, autoStop: 3);

        await PressOnceAndWaitAsync(hotkey, engine);

        engine.State.ShouldBe(DictationState.Recording);
        await engine.CancelAsync();
    }

    [Fact]
    public async Task Off_means_off()
    {
        var hotkey = new FakeHotkeySource();
        await using var engine = Build(SpeechThenQuiet(1, 4), hotkey, new RecordingTextInjector(), toggle: true, autoStop: 0);

        await PressOnceAndWaitAsync(hotkey, engine);

        engine.State.ShouldBe(DictationState.Recording);
        await engine.CancelAsync();
    }

    [Fact]
    public async Task Push_to_talk_ignores_auto_stop()
    {
        var hotkey = new FakeHotkeySource();
        await using var engine = Build(SpeechThenQuiet(1, 4), hotkey, new RecordingTextInjector(), toggle: false, autoStop: 3);

        await PressOnceAndWaitAsync(hotkey, engine);

        engine.State.ShouldBe(DictationState.Recording);
        hotkey.Release();
    }
}
