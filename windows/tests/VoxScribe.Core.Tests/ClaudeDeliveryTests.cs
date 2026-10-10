using VoxScribe.Core;
using VoxScribe.Testing;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// A command dictation goes to the armed Claude Code session first; the window-title path is
/// taken only when no session is armed.
/// </summary>
public sealed class ClaudeDeliveryTests
{
    private static (DictationEngine Engine, FakeHotkeySource Command, FakeHotkeySource Plain,
        RecordingTextInjector Injector, FakeFocusAnchor Anchor, List<string> Delivered) Build(ClaudeDelivery outcome)
    {
        var command = new FakeHotkeySource();
        var plain = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);
        anchor.WindowTitles.Add("Claude");
        var delivered = new List<string>();

        var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), plain, new FakeTranscriber("lance les tests"), injector,
            () => [], new FakeClock(), focusAnchor: anchor, commandHotkey: command)
        {
            DeliverToClaude = (text, _) =>
            {
                delivered.Add(text);
                return Task.FromResult(outcome);
            },
        };

        return (engine, command, plain, injector, anchor, delivered);
    }

    private static async Task WaitAsync(DictationEngine engine, DictationState state)
    {
        for (var i = 0; i < 20000 && engine.State != state; i++) await Task.Yield();
    }

    private static async Task DictateAsync(FakeHotkeySource hotkey, DictationEngine engine)
    {
        hotkey.Press();
        await WaitAsync(engine, DictationState.Recording);
        for (var i = 0; i < 20000 && engine.Level == 0; i++) await Task.Yield();
        hotkey.Release();
        await WaitAsync(engine, DictationState.Idle);
    }

    [Fact]
    public async Task Delivered_types_nothing_and_finds_no_window()
    {
        var (engine, command, _, injector, anchor, delivered) = Build(ClaudeDelivery.Delivered);
        await using var _ = engine;

        await DictateAsync(command, engine);

        delivered.ShouldBe(["lance les tests"]);
        injector.Injected.ShouldBeEmpty();
        injector.Enters.ShouldBe(0);
        anchor.LastFind.ShouldBeNull();
        engine.Journal.InjectedText.ShouldBeEmpty("undo must not backspace text that was never typed");
    }

    /// <summary>The armed session may still submit late: typing it too would send it twice.</summary>
    [Fact]
    public async Task Not_acknowledged_types_nothing_and_says_so()
    {
        var (engine, command, _, injector, anchor, _) = Build(ClaudeDelivery.NotAcknowledged);
        await using var _ = engine;

        await DictateAsync(command, engine);

        injector.Injected.ShouldBeEmpty();
        anchor.LastFind.ShouldBeNull();
        engine.Notice.ShouldContain("not sent");
    }

    [Fact]
    public async Task No_target_takes_the_window_title_path()
    {
        var (engine, command, _, injector, anchor, _) = Build(ClaudeDelivery.NoTarget);
        await using var _ = engine;

        await DictateAsync(command, engine);

        anchor.LastFind.ShouldBe("Claude");
        injector.Injected.ShouldBe(["lance les tests"]);
        injector.Enters.ShouldBe(1);
    }

    [Fact]
    public async Task A_throwing_delivery_types_nothing()
    {
        var (engine, command, _, injector, _, _) = Build(ClaudeDelivery.Delivered);
        await using var _ = engine;
        engine.DeliverToClaude = (_, _) => throw new InvalidOperationException("boom");

        await DictateAsync(command, engine);

        injector.Injected.ShouldBeEmpty();
        engine.Notice.ShouldContain("not sent");
    }

    /// <summary>Stop must stop even in toggle mode, where a key release is ignored.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_band_button_records_a_command(bool toggleMode)
    {
        var (engine, _, _, _, _, delivered) = Build(ClaudeDelivery.Delivered);
        await using var _ = engine;
        engine.ToggleMode = toggleMode;

        engine.BeginCommand();
        await WaitAsync(engine, DictationState.Recording);
        engine.CommandThisUtterance.ShouldBeTrue();
        for (var i = 0; i < 20000 && engine.Level == 0; i++) await Task.Yield();

        engine.EndUtterance();
        await WaitAsync(engine, DictationState.Idle);

        delivered.ShouldBe(["lance les tests"]);
    }

    [Fact]
    public async Task The_band_button_is_ignored_while_a_dictation_runs()
    {
        var (engine, _, plain, injector, _, delivered) = Build(ClaudeDelivery.Delivered);
        await using var _ = engine;

        plain.Press();
        await WaitAsync(engine, DictationState.Recording);
        engine.BeginCommand();

        engine.CommandThisUtterance.ShouldBeFalse();
        plain.Release();
        await WaitAsync(engine, DictationState.Idle);

        injector.Injected.ShouldBe(["lance les tests"]);
        delivered.ShouldBeEmpty();
    }
}
