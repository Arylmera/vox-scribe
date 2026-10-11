using Shouldly;
using VoxScribe.Core;
using VoxScribe.Testing;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>The Edit chord: an instruction applied to the selection, never typed as text.</summary>
public sealed class EditModeTests
{
    private static (DictationEngine Engine, FakeHotkeySource Edit, RecordingTextInjector Injector) Build(
        string? selection, Func<string, string, CancellationToken, Task<EditOutcome>>? edit)
    {
        var editHotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector { Selection = selection };
        var engine = new DictationEngine(
            FakeAudioCapture.Tone(1.0), new FakeHotkeySource(), new FakeTranscriber("traduis en anglais"),
            injector, () => [], new FakeClock(), editHotkey: editHotkey)
        {
            Edit = edit,
            IncrementalInjection = true,   // must be ignored for Edit
        };
        return (engine, editHotkey, injector);
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
    public async Task The_selection_is_replaced_by_the_edit()
    {
        string? gotSelection = null, gotInstruction = null;
        var (engine, edit, injector) = Build("le build passe", (s, i, _) =>
        {
            gotSelection = s;
            gotInstruction = i;
            return Task.FromResult(new EditOutcome("the build passes", null));
        });
        await using var _ = engine;

        await DictateAsync(edit, engine);

        gotSelection.ShouldBe("le build passe");
        gotInstruction.ShouldBe("traduis en anglais");
        injector.Injected.ShouldBe(["the build passes"]);
        injector.Copies.ShouldBe(1);
        engine.Journal.Replaced.ShouldBe("le build passe");
    }

    [Fact]
    public async Task Nothing_selected_types_nothing()
    {
        var (engine, edit, injector) = Build(null, (_, _, _) => Task.FromResult(new EditOutcome("x", null)));
        await using var _ = engine;

        await DictateAsync(edit, engine);

        injector.Injected.ShouldBeEmpty();
        engine.Notice.ShouldBe("Nothing selected");
    }

    [Fact]
    public async Task Nothing_selected_fires_no_completion_and_journals_nothing()
    {
        var (engine, edit, injector) = Build(null, (_, _, _) => Task.FromResult(new EditOutcome("x", null)));
        await using var _ = engine;
        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        await DictateAsync(edit, engine);

        result.ShouldBeNull();
        engine.Journal.InjectedText.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task The_record_button_never_inherits_an_edit()
    {
        var (engine, edit, injector) = Build("le build passe",
            (_, _, _) => Task.FromResult(new EditOutcome("the build passes", null)));
        await using var _ = engine;

        await DictateAsync(edit, engine);
        injector.Copies.ShouldBe(1);

        engine.TogglePushToTalk();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        for (var i = 0; i < 20000 && engine.Level == 0; i++) await Task.Yield();
        engine.TogglePushToTalk();
        for (var i = 0; i < 20000 && engine.State != DictationState.Idle; i++) await Task.Yield();

        injector.Injected.ShouldBe(["the build passes", "traduis en anglais"]);
        injector.Copies.ShouldBe(1);
    }

    [Fact]
    public async Task A_selection_too_long_types_nothing()
    {
        var (engine, edit, injector) = Build(new string('a', DictationEngine.MaxEditSelection + 1),
            (_, _, _) => Task.FromResult(new EditOutcome("x", null)));
        await using var _ = engine;

        await DictateAsync(edit, engine);

        injector.Injected.ShouldBeEmpty();
        engine.Notice.ShouldBe("Selection too long");
    }

    [Fact]
    public async Task A_failed_edit_types_nothing_and_says_why()
    {
        var (engine, edit, injector) = Build("le build passe",
            (_, _, _) => Task.FromResult(new EditOutcome(null, "gateway answered 503")));
        await using var _ = engine;

        await DictateAsync(edit, engine);

        injector.Injected.ShouldBeEmpty();
        engine.Notice.ShouldBe("Edit failed — gateway answered 503");
    }

    [Fact]
    public async Task Without_an_endpoint_the_chord_only_explains()
    {
        var (engine, edit, injector) = Build("le build passe", edit: null);
        await using var _ = engine;

        edit.Press();
        for (var i = 0; i < 200; i++) await Task.Yield();

        engine.State.ShouldBe(DictationState.Idle);
        engine.Notice.ShouldBe("Set a cleanup endpoint to edit by voice");
        edit.Release();
        injector.Copies.ShouldBe(0);
    }

    [Fact]
    public async Task A_successful_edit_reports_the_instruction_and_the_call_time()
    {
        var (engine, edit, _) = Build("le build passe", (_, _, _) => Task.FromResult(new EditOutcome("the build passes", null)));
        await using var _e = engine;
        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        await DictateAsync(edit, engine);

        result.ShouldNotBeNull();
        result.Text.ShouldBe("the build passes");
        result.RawText.ShouldBe("traduis en anglais");
        result.CleanupFailed.ShouldBe(false);
        result.CleanupTime.ShouldNotBeNull();
    }
}
