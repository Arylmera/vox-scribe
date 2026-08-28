using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// The two shortcuts: the first types what was heard, the second types what was tidied.
/// </summary>
public sealed class CleanupShortcutTests
{
    private static DictationEngine Build(
        FakeHotkeySource plain,
        FakeHotkeySource cleanup,
        RecordingTextInjector injector,
        Func<string, CancellationToken, Task<string>> clean)
    {
        var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4),
            plain,
            new FakeTranscriber("le build passe"),
            injector,
            () => Array.Empty<DictionaryEntry>(),
            new FakeClock(),
            cleanup);

        engine.Cleanup = clean;
        return engine;
    }

    private static async Task DictateAsync(FakeHotkeySource hotkey, DictationEngine engine)
    {
        hotkey.Press();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        for (var i = 0; i < 20000 && engine.Level == 0; i++) await Task.Yield();

        hotkey.Release();
        for (var i = 0; i < 20000 && engine.State != DictationState.Idle; i++) await Task.Yield();
    }

    [Fact]
    public async Task The_second_shortcut_runs_the_cleanup_pass()
    {
        var plain = new FakeHotkeySource();
        var cleanup = new FakeHotkeySource();
        var injector = new RecordingTextInjector();

        await using var engine = Build(plain, cleanup, injector,
            (text, _) => Task.FromResult($"[{text}]"));

        await DictateAsync(cleanup, engine);

        injector.Injected.ShouldBe(["[le build passe]"]);
    }

    [Fact]
    public async Task The_first_shortcut_types_the_raw_transcript()
    {
        var plain = new FakeHotkeySource();
        var cleanup = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var called = false;

        await using var engine = Build(plain, cleanup, injector,
            (text, _) => { called = true; return Task.FromResult($"[{text}]"); });

        await DictateAsync(plain, engine);

        injector.Injected.ShouldBe(["le build passe"]);
        called.ShouldBeFalse();
    }

    /// <summary>
    /// The flag is set on press. Using the plain key after the cleanup key must not inherit
    /// the previous utterance's choice.
    /// </summary>
    [Fact]
    public async Task The_choice_does_not_leak_into_the_next_utterance()
    {
        var plain = new FakeHotkeySource();
        var cleanup = new FakeHotkeySource();
        var injector = new RecordingTextInjector();

        var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4),
            plain,
            new FakeTranscriber("le build passe", "le build passe"),
            injector,
            () => Array.Empty<DictionaryEntry>(),
            new FakeClock(),
            cleanup);
        engine.Cleanup = (text, _) => Task.FromResult($"[{text}]");

        await using (engine)
        {
            await DictateAsync(cleanup, engine);
            await DictateAsync(plain, engine);
        }

        injector.Injected.ShouldBe(["[le build passe]", "le build passe"]);
    }

    /// <summary>A second shortcut that is never armed is a shortcut that silently does nothing.</summary>
    [Fact]
    public async Task Both_hooks_are_armed_by_Start()
    {
        var plain = new FakeHotkeySource();
        var cleanup = new FakeHotkeySource();

        await using var engine = Build(plain, cleanup, new RecordingTextInjector(),
            (text, _) => Task.FromResult(text));

        engine.Start().ShouldBeTrue();

        plain.IsRunning.ShouldBeTrue();
        cleanup.IsRunning.ShouldBeTrue();
    }
}
