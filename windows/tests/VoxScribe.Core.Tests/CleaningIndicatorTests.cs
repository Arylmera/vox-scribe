using VoxScribe.Core;
using VoxScribe.Dictionary;
using VoxScribe.Testing;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// What the pill's badge reads. It reports the outcome, not the key that was pressed — a
/// badge that says CLEAN while nothing is cleaned is worse than no badge.
/// </summary>
public sealed class CleaningIndicatorTests
{
    private static DictationEngine Build(FakeHotkeySource plain, FakeHotkeySource cleanup) =>
        new(FakeAudioCapture.Silence(0.1),
            plain,
            new FakeTranscriber(),
            new RecordingTextInjector(),
            () => Array.Empty<DictionaryEntry>(),
            new FakeClock(),
            cleanup);

    [Fact]
    public async Task Idle_is_never_cleaning()
    {
        var plain = new FakeHotkeySource();
        await using var engine = Build(plain, new FakeHotkeySource());

        engine.CleaningThisUtterance.ShouldBeFalse();
    }

    [Fact]
    public async Task The_cleanup_shortcut_lights_the_badge()
    {
        var cleanup = new FakeHotkeySource();
        await using var engine = Build(new FakeHotkeySource(), cleanup);
        engine.Cleanup = (text, _) => Task.FromResult(text);

        cleanup.Press();

        engine.CleaningThisUtterance.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_an_endpoint_the_badge_stays_dark()
    {
        var cleanup = new FakeHotkeySource();
        await using var engine = Build(new FakeHotkeySource(), cleanup);

        cleanup.Press();

        engine.CleaningThisUtterance.ShouldBeFalse();
    }

    /// <summary>
    /// The cleanup shortcut overrides incremental typing rather than being disabled by it.
    /// </summary>
    [Fact]
    public async Task Incremental_injection_does_not_dim_the_badge()
    {
        var cleanup = new FakeHotkeySource();
        await using var engine = Build(new FakeHotkeySource(), cleanup);
        engine.Cleanup = (text, _) => Task.FromResult(text);
        engine.IncrementalInjection = true;

        cleanup.Press();

        engine.CleaningThisUtterance.ShouldBeTrue();
    }
}
