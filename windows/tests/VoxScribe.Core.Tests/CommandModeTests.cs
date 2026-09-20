using VoxScribe.Core;
using VoxScribe.Testing;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The command shortcut dictates at a Claude Code session: the text goes to the window
/// named by title and is submitted with Return. The undo shortcut takes the last dictation
/// back without opening the app.
/// </summary>
public sealed class CommandModeTests
{
    private static (DictationEngine Engine, FakeHotkeySource Raw, FakeHotkeySource Command, FakeHotkeySource Undo,
        RecordingTextInjector Injector, FakeFocusAnchor Anchor) Build(string transcript = "le build passe")
    {
        var raw = new FakeHotkeySource();
        var command = new FakeHotkeySource();
        var undo = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), raw, new FakeTranscriber(transcript), injector,
            () => [], new FakeClock(),
            focusAnchor: anchor, undoHotkey: undo, commandHotkey: command)
        {
            AnchorFocus = true,
        };

        return (engine, raw, command, undo, injector, anchor);
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
    public async Task A_command_is_typed_into_the_named_window_and_submitted()
    {
        var (engine, _, command, _, injector, anchor) = Build();
        await using var _ = engine;
        anchor.WindowTitles.Add("✳ Claude Code — vox-scribe");
        engine.Cleanup = (text, _) => Task.FromResult($"[{text}]");

        await DictateAsync(command, engine);

        anchor.LastFind.ShouldBe("Claude");
        anchor.Targets.ShouldHaveSingleItem().InjectedWhenRestored.ShouldBe(0, "the window comes forward before typing");
        injector.Injected.ShouldBe(["[le build passe]"]);
        injector.Enters.ShouldBe(1);
        engine.Journal.InjectedText.ShouldBe("[le build passe]");
    }

    [Fact]
    public async Task Without_a_cleaner_the_command_goes_raw()
    {
        var (engine, _, command, _, injector, anchor) = Build();
        await using var _ = engine;
        anchor.WindowTitles.Add("Claude");

        await DictateAsync(command, engine);

        injector.Injected.ShouldBe(["le build passe"]);
        injector.Enters.ShouldBe(1);
    }

    /// <summary>A prompt meant for Claude must never land in whatever field happens to have focus.</summary>
    [Fact]
    public async Task No_matching_window_means_nothing_is_typed_anywhere()
    {
        var (engine, _, command, _, injector, anchor) = Build();
        await using var _ = engine;
        anchor.WindowTitles.Add("Excel");
        DictationResult? completed = null;
        engine.Completed += (_, r) => completed = r;

        await DictateAsync(command, engine);

        injector.Injected.ShouldBeEmpty();
        injector.Enters.ShouldBe(0);
        engine.Notice.ShouldContain("Claude");
        completed.ShouldNotBeNull("the dictation still reaches the history");
    }

    [Fact]
    public async Task The_command_window_title_is_a_setting()
    {
        var (engine, _, command, _, injector, anchor) = Build();
        await using var _ = engine;
        engine.CommandWindowTitle = "Windows Terminal";
        anchor.WindowTitles.Add("guill — Windows Terminal");

        await DictateAsync(command, engine);

        anchor.LastFind.ShouldBe("Windows Terminal");
        injector.Injected.ShouldHaveSingleItem();
    }

    /// <summary>Command mode never anchors the field at press — the target is found at release.</summary>
    [Fact]
    public async Task A_command_does_not_capture_the_focused_field()
    {
        var (engine, _, command, _, _, anchor) = Build();
        await using var _ = engine;
        anchor.WindowTitles.Add("Claude");

        await DictateAsync(command, engine);

        anchor.Captures.ShouldBe(0);
    }

    [Fact]
    public async Task The_badge_knows_a_command_from_a_dictation()
    {
        var (engine, raw, command, _, _, anchor) = Build();
        await using var _ = engine;
        anchor.WindowTitles.Add("Claude");

        command.Press();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        engine.CommandThisUtterance.ShouldBeTrue();
        command.Release();
        for (var i = 0; i < 20000 && engine.State != DictationState.Idle; i++) await Task.Yield();

        raw.Press();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        engine.CommandThisUtterance.ShouldBeFalse();
        raw.Release();
        for (var i = 0; i < 20000 && engine.State != DictationState.Idle; i++) await Task.Yield();
    }

    [Fact]
    public async Task The_undo_shortcut_takes_the_last_dictation_back()
    {
        var (engine, raw, _, undo, injector, _) = Build("hello");
        await using var _ = engine;

        await DictateAsync(raw, engine);
        injector.Injected.ShouldBe(["hello"]);

        undo.Press();
        for (var i = 0; i < 20000 && injector.Backspaces < 5; i++) await Task.Yield();

        injector.Backspaces.ShouldBe(5);
        engine.Journal.InjectedText.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_arms_every_shortcut()
    {
        var (engine, raw, command, undo, _, _) = Build();
        await using var _ = engine;

        engine.Start().ShouldBeTrue();

        raw.IsRunning.ShouldBeTrue();
        command.IsRunning.ShouldBeTrue();
        undo.IsRunning.ShouldBeTrue();
    }
}
