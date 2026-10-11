# Silence auto-stop and cleanup timing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In toggle mode, end a dictation after N seconds of silence once speech was heard; record and show whether cleanup succeeded and how long it took (pill, history, Home).

**Architecture:** All behaviour lives in the platform-neutral `VoxScribe.Core` (segmenter, engine, cleaner, stats) and is tested with the fakes in `VoxScribe.Testing`. The App layer only wires settings and renders the new fields.

**Tech Stack:** C# / .NET 10, Avalonia 11.3.20, xUnit 2.9.3 + Shouldly, Avalonia.Headless.XUnit.

**Spec:** `docs/superpowers/specs/2026-10-11-silence-autostop-and-cleanup-timing-design.md`

## Global Constraints

- Work from `windows/`. Test command: `dotnet test VoxScribe.CrossPlatform.slnf` (add `--filter "FullyQualifiedName~<Class>"` to narrow).
- Final gate (must pass before the last commit): `dotnet format VoxScribe.sln --verify-no-changes` and `dotnet build VoxScribe.sln -c Release --no-incremental -warnaserror`.
- Views contain **no literal colours/sizes** — use `Tokens.*` (`windows/src/VoxScribe.App/Design/DesignTokens.cs`). UI strings are English.
- `VoxScribe.Core` must stay free of Windows APIs (CA1416 is an error).
- Red (`#E5484D`) is the recording dot only. Failure text uses `Tokens.Brushes.Caution`.
- Match surrounding comment density and XML-doc style (`/// <summary>` on public members).
- Commit after each task with a conventional message; no attribution lines. Push after each commit (`git push`).

---

### Task 1: Segmenter exposes speech-heard and trailing quiet

**Files:**
- Modify: `windows/src/VoxScribe.Core/StreamingSegmenter.cs`
- Test: `windows/tests/VoxScribe.Core.Tests/StreamingSegmenterTests.cs`

**Interfaces:**
- Produces: `bool StreamingSegmenter.HeardSpeech { get; }`, `double StreamingSegmenter.TrailingQuietSeconds { get; }`

- [ ] **Step 1: Write the failing tests** (append inside `StreamingSegmenterTests`, reusing its `Chunk`/`Feed` helpers)

```csharp
    [Fact]
    public void Room_noise_below_the_floor_is_not_speech()
    {
        var segmenter = new StreamingSegmenter();

        Feed(segmenter, StreamingSegmenter.SilenceFloor / 2, seconds: 3);

        segmenter.HeardSpeech.ShouldBeFalse();
    }

    [Fact]
    public void Speech_is_remembered_once_heard()
    {
        var segmenter = new StreamingSegmenter();

        Feed(segmenter, 0.5f, seconds: 1);
        Feed(segmenter, 0f, seconds: 2);

        segmenter.HeardSpeech.ShouldBeTrue();
    }

    /// <summary>
    /// The pause that closes a segment must not restart the count: auto-stop needs the whole
    /// quiet stretch, and a cut at 0.6 s would otherwise reset it forever.
    /// </summary>
    [Fact]
    public void Trailing_quiet_keeps_counting_across_a_segment_cut()
    {
        var segmenter = new StreamingSegmenter();

        Feed(segmenter, 0.5f, seconds: 2);
        for (var i = 0; i < 25; i++) segmenter.Accept(Chunk(0f));   // 2.5 s, a cut happens inside

        segmenter.TrailingQuietSeconds.ShouldBe(2.5, tolerance: 0.01);
    }

    [Fact]
    public void Speech_resets_the_trailing_quiet()
    {
        var segmenter = new StreamingSegmenter();

        Feed(segmenter, 0.5f, seconds: 1);
        Feed(segmenter, 0f, seconds: 1);
        segmenter.Accept(Chunk(0.5f));

        segmenter.TrailingQuietSeconds.ShouldBe(0);
    }
```

- [ ] **Step 2: Run, expect compile failure** — `dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~StreamingSegmenterTests"` → error CS1061 `HeardSpeech` not found.

- [ ] **Step 3: Implement** in `StreamingSegmenter.cs`. Add fields next to `_quietSamples`, properties next to `Pending`, and update `Accept`:

```csharp
    /// <summary>Quiet samples at the end of the recording; unlike the pause counter, never reset by a cut.</summary>
    private int _trailingQuietSamples;

    /// <summary>True once any chunk reached <see cref="SilenceFloor"/> this utterance.</summary>
    /// <remarks>
    /// The absolute floor only: before the first word the first chunk <i>is</i> the peak, so
    /// the peak-relative threshold cannot tell noise from speech yet.
    /// </remarks>
    public bool HeardSpeech { get; private set; }

    /// <summary>Continuous quiet at the end of the recording, across segment cuts.</summary>
    public double TrailingQuietSeconds => (double)_trailingQuietSamples / AudioChunk.SampleRate;
```

In `Accept`, replace the two quiet-counting lines with:

```csharp
        if (level >= SilenceFloor) HeardSpeech = true;

        // Trailing quiet is counted, not reset per chunk: a pause is longer than one buffer.
        if (level < Math.Max(SilenceFloor, _peak * SilenceFraction))
        {
            _quietSamples += span.Length;
            _trailingQuietSamples += span.Length;
        }
        else
        {
            _quietSamples = 0;
            _trailingQuietSamples = 0;
        }
```

`Take()` keeps resetting only `_quietSamples`.

- [ ] **Step 4: Run the class's tests, expect all PASS.**
- [ ] **Step 5: Commit** — `feat: segmenter tracks heard speech and trailing quiet`

---

### Task 2: Engine auto-stops a toggle dictation on silence

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs` (property near `ToggleMode` ~line 422; capture loop in `BeginAsync` ~line 590; reset in `BeginAsync` setup ~line 572)
- Test: create `windows/tests/VoxScribe.Core.Tests/AutoStopTests.cs`

**Interfaces:**
- Consumes: `StreamingSegmenter.HeardSpeech`, `StreamingSegmenter.TrailingQuietSeconds` (Task 1)
- Produces: `double DictationEngine.AutoStopSilenceSeconds { get; set; }` (0 = off)

- [ ] **Step 1: Write the failing tests** — `AutoStopTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run, expect compile failure** (`AutoStopSilenceSeconds` missing).

- [ ] **Step 3: Implement** in `DictationEngine.cs`:

Property, after `ToggleMode`:

```csharp
    /// <summary>
    /// Toggle mode only: seconds of quiet, after speech was heard, that end the dictation as a
    /// second press would. 0 turns it off. Safe to change live.
    /// </summary>
    public double AutoStopSilenceSeconds { get; set; }
```

Field next to `_cancelled`:

```csharp
    /// <summary>Set once auto-stop has fired this utterance, so it ends the dictation once.</summary>
    private bool _autoStopped;
```

In `BeginAsync`, beside `_cancelled = false;` add `_autoStopped = false;`.

In the capture loop, replace the `lock (_segments) { ... }` block with:

```csharp
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
```

- [ ] **Step 4: Run `--filter "FullyQualifiedName~AutoStopTests"`, then the whole Core suite; expect PASS.**
- [ ] **Step 5: Commit** — `feat: toggle dictation stops itself after a silence`

---

### Task 3: Auto-stop setting and its control

**Files:**
- Modify: `windows/src/VoxScribe.Core/AppSettings.cs` (after `PushToTalkToggle`, ~line 38)
- Modify: `windows/src/VoxScribe.App/Composition.cs` (~lines 186 and 213)
- Modify: `windows/src/VoxScribe.App/Views/Settings/ShortcutsSection.cs` (after the toggle, ~line 59)
- Test: `windows/tests/VoxScribe.Core.Tests/SettingsMigrationTests.cs` (append)

**Interfaces:**
- Consumes: `DictationEngine.AutoStopSilenceSeconds` (Task 2)
- Produces: `int SettingsData.AutoStopSilenceSeconds { get; init; } = 3`

The control is a **ComboBox** ("Off", "1 s" … "10 s"), following `SpeechSection`'s device picker — the app has no themed slider, and one picker covers both on/off and the duration.

- [ ] **Step 1: Failing test** — append to `SettingsMigrationTests` (check how that file builds `SettingsData` from JSON and mirror it; if it has no JSON helper, use `System.Text.Json` with the same `JsonSerializerContext` the store uses):

```csharp
    [Fact]
    public void Auto_stop_defaults_to_three_seconds() =>
        new SettingsData().AutoStopSilenceSeconds.ShouldBe(3);
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement.**

`AppSettings.cs`, after `PushToTalkToggle`:

```csharp
    /// <summary>
    /// Toggle mode only: seconds of silence, once speech was heard, that end a dictation.
    /// 0 turns it off. Default 3.
    /// </summary>
    public int AutoStopSilenceSeconds { get; init; } = 3;
```

`Composition.cs`: after `engine.ToggleMode = settings.Data.PushToTalkToggle;` add
`engine.AutoStopSilenceSeconds = settings.Data.AutoStopSilenceSeconds;` and after
`live.ToggleMode = settings.Data.PushToTalkToggle;` add
`live.AutoStopSilenceSeconds = settings.Data.AutoStopSilenceSeconds;`.

`ShortcutsSection.cs`: after the `Panels.Toggle("Toggle mode …")` entry add `AutoStop(settings, save),` and a helper in the class:

```csharp
    /// <summary>Off, or 1–10 s of silence that end a toggle dictation. Disabled outside toggle mode.</summary>
    private static Control AutoStop(SettingsStore settings, Action<SettingsData> save)
    {
        var choices = Enumerable.Range(0, 11)
            .Select(s => new ComboBoxItem { Content = s == 0 ? "Off" : $"{s} s", Tag = s })
            .ToList();
        var picker = new ComboBox
        {
            ItemsSource = choices,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            IsEnabled = settings.Data.PushToTalkToggle,
            SelectedItem = choices.FirstOrDefault(c => (int)c.Tag! == settings.Data.AutoStopSilenceSeconds) ?? choices[3],
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: int s } && s != settings.Data.AutoStopSilenceSeconds)
                save(settings.Data with { AutoStopSilenceSeconds = s });
        };

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Labelled("STOP AFTER SILENCE", picker),
                Panels.Note("Toggle mode only: once you have spoken, this much silence ends the "
                    + "dictation as a second press would."),
            },
        };
    }
```

Use the exact parameter types `ShortcutsSection`'s existing method already receives for `settings` and `save` (read its signature; rename the types above to match). The section is rebuilt on settings change, so `IsEnabled` follows the toggle.

- [ ] **Step 4: Run the whole suite (`dotnet test VoxScribe.CrossPlatform.slnf`), expect PASS.**
- [ ] **Step 5: Commit** — `feat: setting for the toggle-mode silence auto-stop`

---

### Task 4: TextCleaner reports success or fall-back

**Files:**
- Modify: `windows/src/VoxScribe.Core/TextCleaner.cs`
- Test: `windows/tests/VoxScribe.Core.Tests/TextCleanerTests.cs` (append)

**Interfaces:**
- Produces: `public sealed record CleanupOutcome(string Text, bool Succeeded);` (in `TextCleaner.cs`, namespace `VoxScribe.Core`), `Task<CleanupOutcome> TextCleaner.TryCleanAsync(string text, CancellationToken)`, `static string? TextCleaner.Vet(string original, string? candidate)` (null = rejected). `CleanAsync` and `Accept` keep their signatures and behaviour.

- [ ] **Step 1: Failing tests:**

```csharp
    [Fact]
    public void A_rejected_candidate_vets_as_null() =>
        TextCleaner.Vet("le build passe", "```le build passe```").ShouldBeNull();

    [Fact]
    public void An_identical_answer_vets_as_itself() =>
        TextCleaner.Vet("already clean", "already clean").ShouldBe("already clean");

    [Fact]
    public async Task An_unreachable_gateway_is_a_failure_with_the_original_text()
    {
        // Port 9 (discard) is closed on any dev machine and CI runner: connection refused.
        var cleaner = new TextCleaner("http://127.0.0.1:9/v1", "local-light", null);

        var outcome = await cleaner.TryCleanAsync("le build passe", CancellationToken.None);

        outcome.ShouldBe(new CleanupOutcome("le build passe", Succeeded: false));
    }
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement.**
  - Add above the class:

```csharp
/// <summary>What a cleanup pass produced, and whether the model's text was actually used.</summary>
/// <param name="Text">The text to type: the repair, or the original on any failure.</param>
/// <param name="Succeeded">False when it fell back to the original (timeout, error, rejected answer).</param>
public sealed record CleanupOutcome(string Text, bool Succeeded);
```

  - Rename the body of `CleanAsync` into `TryCleanAsync` returning `CleanupOutcome`: the whitespace early return becomes `return new CleanupOutcome(text, Succeeded: true);`; the non-success-status return and the `catch` return become `new CleanupOutcome(text, Succeeded: false)`; the final line becomes:

```csharp
            return Vet(text, Content(answer)) is { } repaired
                ? new CleanupOutcome(repaired, Succeeded: true)
                : new CleanupOutcome(text, Succeeded: false);
```

  - `CleanAsync` becomes:

```csharp
    /// <summary>Repairs <paramref name="text"/>, or returns it unchanged on any failure.</summary>
    public async Task<string> CleanAsync(string text, CancellationToken cancellationToken) =>
        (await TryCleanAsync(text, cancellationToken).ConfigureAwait(false)).Text;
```

  - Turn `Accept` into `Vet` returning `string?` (every `return original;` becomes `return null;`, `return trimmed;` stays), keep its XML doc, and re-add:

```csharp
    /// <summary>
    /// <see cref="Vet"/>, falling back to <paramref name="original"/> when the candidate is refused.
    /// </summary>
    public static string Accept(string original, string? candidate) => Vet(original, candidate) ?? original;
```

- [ ] **Step 4: Run `--filter "FullyQualifiedName~TextCleanerTests"`; all old `Accept` tests and the new ones PASS.**
- [ ] **Step 5: Commit** — `feat: cleanup reports whether the model's text was used`

---

### Task 5: Engine times cleanup; result and history carry it

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs` (`DictationResult` ~line 31, `Cleanup` property ~line 479, `ProcessAsync` ~lines 683–697)
- Modify: `windows/src/VoxScribe.Core/TranscriptStore.cs` (`TranscriptRecord`)
- Modify: `windows/src/VoxScribe.App/Composition.cs` (`BuildCleanup` ~line 197, record creation ~line 274)
- Modify (mechanical, delegate type change): `windows/tests/VoxScribe.Core.Tests/CleaningIndicatorTests.cs:38,64`, `CleanupShortcutTests.cs:19,30,95` and every lambda passed to `Build(..., clean)` in that file, `CommandModeTests.cs:51`, `DictationEngineTests.cs:217`
- Test: `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs` (append)

**Interfaces:**
- Consumes: `CleanupOutcome`, `TextCleaner.TryCleanAsync` (Task 4)
- Produces: `DictationEngine.Cleanup` is `Func<string, CancellationToken, Task<CleanupOutcome>>?`; `DictationResult` gains trailing optional params `TimeSpan? CleanupTime = null, bool? CleanupFailed = null`; `TranscriptRecord` gains `double? CleanupSeconds { get; init; }` and `bool? CleanupFailed { get; init; }`.

- [ ] **Step 1: Failing tests** — append to `DictationEngineTests` (uses that file's `DictateAsync`):

```csharp
    [Fact]
    public async Task Cleanup_time_and_success_are_reported()
    {
        var hotkey = new FakeHotkeySource();
        var cleanupHotkey = new FakeHotkeySource();
        var clock = new FakeClock();
        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(1.0), hotkey, new FakeTranscriber("helo"), new RecordingTextInjector(),
            () => [], clock, cleanupHotkey);
        engine.Cleanup = (text, _) =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(700));
            return Task.FromResult(new CleanupOutcome("hello", Succeeded: true));
        };
        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        await DictateAsync(cleanupHotkey, engine);

        result.ShouldNotBeNull();
        result.CleanupTime.ShouldBe(TimeSpan.FromMilliseconds(700));
        result.CleanupFailed.ShouldBe(false);
    }

    [Fact]
    public async Task A_failed_cleanup_types_the_raw_text_and_says_so()
    {
        var hotkey = new FakeHotkeySource();
        var cleanupHotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(1.0), hotkey, new FakeTranscriber("helo"), injector,
            () => [], new FakeClock(), cleanupHotkey);
        engine.Cleanup = (text, _) => Task.FromResult(new CleanupOutcome(text, Succeeded: false));
        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        await DictateAsync(cleanupHotkey, engine);

        injector.Injected.ShouldBe(["helo"]);
        result!.CleanupFailed.ShouldBe(true);
    }

    [Fact]
    public async Task A_raw_dictation_reports_no_cleanup()
    {
        var hotkey = new FakeHotkeySource();
        await using var engine = Build(FakeAudioCapture.Tone(1.0), hotkey, new FakeTranscriber("hello"), new RecordingTextInjector());
        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        await DictateAsync(hotkey, engine);

        result!.CleanupTime.ShouldBeNull();
        result.CleanupFailed.ShouldBeNull();
    }
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement.**
  - `DictationResult`: add `TimeSpan? CleanupTime = null, bool? CleanupFailed = null` after `RawText`, and document them in the record's `<param>` list ("How long the cleanup call took; null when none ran." / "True when cleanup fell back to the raw text; null when none ran.").
  - `Cleanup` property type → `Func<string, CancellationToken, Task<CleanupOutcome>>?`.
  - `ProcessAsync` cleanup block:

```csharp
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
```

    and pass `CleanupTime: cleanupTime, CleanupFailed: cleanupFailed` to `new DictationResult(...)`.
  - `TranscriptRecord`, after `RawText`:

```csharp
    /// <summary>How long cleanup took; null when no cleanup ran (and on lines written before it was recorded).</summary>
    public double? CleanupSeconds { get; init; }

    /// <summary>True when cleanup fell back to the raw text; null when no cleanup ran.</summary>
    public bool? CleanupFailed { get; init; }
```

  - `Composition.cs`: `BuildCleanup` return type → `Func<string, CancellationToken, Task<CleanupOutcome>>?` and `.CleanAsync` → `.TryCleanAsync`. In the `TranscriptRecord` initializer add `CleanupSeconds = result.CleanupTime?.TotalSeconds, CleanupFailed = result.CleanupFailed,`.
  - Test sites: every `Task.FromResult(x)` given to `Cleanup` becomes `Task.FromResult(new CleanupOutcome(x, Succeeded: true))`; `CleanupShortcutTests.Build`'s parameter type becomes `Func<string, CancellationToken, Task<CleanupOutcome>>`.

- [ ] **Step 4: Run the whole suite, expect PASS.** Also check that `TranscriptJsonContext` (source-generated) needs no change — nullable `double?`/`bool?` on the record are picked up automatically; `StorageTests` must still pass.
- [ ] **Step 5: Commit** — `feat: dictations record cleanup time and outcome`

---

### Task 6: Home shows the week's cleanup timing

**Files:**
- Modify: `windows/src/VoxScribe.Core/HomeStats.cs`
- Modify: `windows/src/VoxScribe.App/Views/HomePage.cs` (`Refresh`, ~line 72)
- Test: `windows/tests/VoxScribe.Core.Tests/HomeStatsTests.cs` (append)

**Interfaces:**
- Consumes: `TranscriptRecord.CleanupSeconds`, `TranscriptRecord.CleanupFailed` (Task 5)
- Produces: `HomeStatsResult` gains trailing `TimeSpan? MedianCleanup = null, int CleanupFailures = 0`.

- [ ] **Step 1: Failing tests:**

```csharp
    [Fact]
    public void Cleanup_median_and_failures_cover_the_week()
    {
        TranscriptRecord Cleaned(double hoursAgo, double seconds, bool failed) =>
            Take(Now.AddHours(-hoursAgo), 3) with { CleanupSeconds = seconds, CleanupFailed = failed };

        var stats = HomeStats.Compute(
        [
            Cleaned(1, 0.5, false), Cleaned(2, 0.9, false), Cleaned(3, 5.0, true),
            Cleaned(24 * 8, 0.1, true),   // outside the week
            Take(Now.AddHours(-1), 3),    // raw, no cleanup
        ], Now);

        stats.MedianCleanup.ShouldBe(TimeSpan.FromSeconds(0.9));
        stats.CleanupFailures.ShouldBe(1);
    }

    [Fact]
    public void No_cleanup_this_week_has_no_median() =>
        HomeStats.Compute([Take(Now.AddHours(-1), 3)], Now).MedianCleanup.ShouldBeNull();
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement.** `HomeStatsResult` doc: add `<param name="MedianCleanup">Median cleanup time this week; null when none ran.</param>` and `<param name="CleanupFailures">Cleanups this week that fell back to the raw text.</param>`; signature `… int StreakDays, TimeSpan? MedianCleanup = null, int CleanupFailures = 0)`. In `Compute`, inside the in-week branch (after the `continue` guard) collect:

```csharp
            if (record.CleanupSeconds is { } cleanupSeconds)
            {
                cleanups.Add(cleanupSeconds);
                if (record.CleanupFailed == true) failures++;
            }
```

with `var cleanups = new List<double>(); var failures = 0;` declared at the top, and return:

```csharp
        return new HomeStatsResult(words, wpm, saved, Streak(days, now.Date), Median(cleanups), failures);
```

plus:

```csharp
    /// <summary>Upper median, so it is always a time that actually happened.</summary>
    private static TimeSpan? Median(List<double> seconds)
    {
        if (seconds.Count == 0) return null;
        seconds.Sort();
        return TimeSpan.FromSeconds(seconds[seconds.Count / 2]);
    }
```

  `HomePage.Refresh`: the top `StackPanel`'s children become `{ BuildHeader(data), BuildStats(stats) }` plus, when `stats.MedianCleanup is { } median`, a line:

```csharp
    /// <summary>"Cleanup · median 0.7s · 1 failed this week" — only once a cleanup ran.</summary>
    private static TextBlock BuildCleanupLine(TimeSpan median, int failures) => new()
    {
        Text = Shell.Caps(string.Create(CultureInfo.InvariantCulture,
            $"Cleanup · median {median.TotalSeconds:0.0}s · {failures} failed this week")),
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Caption,
        Foreground = Tokens.Brushes.InkSecondary,
    };
```

- [ ] **Step 4: Run the whole suite (HomePageTests included), expect PASS.**
- [ ] **Step 5: Commit** — `feat: Home shows the week's cleanup median and failures`

---

### Task 7: History rows show cleanup timing

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/TranscriptionsView.cs` (`MetaBlock`, ~line 158)
- Test: `windows/tests/VoxScribe.App.Tests/UiTests.cs` (append; read how it builds a `TranscriptionsView` over a temp `TranscriptStore` and mirror that)

**Interfaces:**
- Consumes: `TranscriptRecord.CleanupSeconds`, `TranscriptRecord.CleanupFailed` (Task 5)

`MetaBlock` returns a `TextBlock`; switch it to `Inlines` so the failure part can take `Caution`:

```csharp
    /// <summary>Time, felt latency and — when cleanup ran — its time or its failure, on one mono line.</summary>
    private static TextBlock MetaBlock(TranscriptRecord record, double emphasis)
    {
        var block = new TextBlock
        {
            FontFamily = Tokens.Fonts.Mono,
            FontSize = Tokens.Fonts.Caption,
            Foreground = Tokens.Brushes.InkOnDeckAt(emphasis),
            VerticalAlignment = VerticalAlignment.Center,
            Inlines = new InlineCollection
            {
                new Run(record.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)
                    + " · " + record.ProcessingSeconds.ToString("0.0", CultureInfo.CurrentCulture) + "s"),
            },
        };

        if (record.CleanupFailed == true)
            block.Inlines.Add(new Run(" · cleanup failed") { Foreground = Tokens.Brushes.Caution });
        else if (record.CleanupSeconds is { } seconds)
            block.Inlines.Add(new Run(" · cleanup " + seconds.ToString("0.0", CultureInfo.CurrentCulture) + "s"));

        return block;
    }
```

Contrast: rows have no background, so the ground is the page (`Chassis`/`Panel`); `ContrastTests` already checks `caution/ground` and `caution/surface`. Confirm in `Design/Themes.cs` that the history page ground is one of those; if it is `Deck`, add `Check("caution/deck", c.Caution, <deck colour>, Text);` beside the other caution checks.

- [ ] **Step 1: Failing test** — a headless test that adds one record with `CleanupSeconds = 0.7` and one with `CleanupFailed = true, CleanupSeconds = 5`, builds the view, and asserts the concatenated `TextBlock` inline text contains `"cleanup 0.7s"` (invariant culture: set `CultureInfo.CurrentCulture = CultureInfo.InvariantCulture` in the test, restore in `finally`) and `"cleanup failed"`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement as above.**
- [ ] **Step 4: Run the whole suite, expect PASS.**
- [ ] **Step 5: Commit** — `feat: history rows show cleanup time or failure`

---

### Task 8: The pill shows the cleanup split

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/Pill/PillFace.cs` (`PillState` ~line 34, helpers ~line 152)
- Modify: `windows/src/VoxScribe.App/Views/HudWindow.cs` (`_lastLatency` ~line 40, `OnEngineCompleted` ~line 83, Latency `PillState` ~line 118)
- Modify: `windows/src/VoxScribe.App/Views/Pill/PaperPill.cs:108`, `FluentPill.cs:90`, `OrbPill.cs:101`, `TidePill.cs:97`, `MonoPill.cs:116`
- Test: `windows/tests/VoxScribe.App.Tests/PillTests.cs` (append)

**Interfaces:**
- Consumes: `DictationResult.CleanupTime`, `DictationResult.CleanupFailed` (Task 5)
- Produces: `PillState` gains trailing `TimeSpan? Cleanup = null, bool CleanupFailed = false` (existing call sites unchanged).

- [ ] **Step 1: Failing tests:**

```csharp
    [AvaloniaFact]
    public void The_latency_shows_the_cleanup_split() => EachTheme((theme, face) =>
    {
        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "CLEAN", string.Empty,
            Cleanup: TimeSpan.FromSeconds(0.7)));
        var text = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        text.ShouldContain("1.2s", customMessage: theme.Id);
        text.ShouldContain("0.7s", customMessage: theme.Id);

        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "CLEAN", string.Empty,
            Cleanup: TimeSpan.FromSeconds(5), CleanupFailed: true));
        var failed = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        failed.ShouldContain("failed", customMessage: theme.Id);
    });

    [AvaloniaFact]
    public void The_cleanup_split_fits_the_pill() => EachTheme((theme, face) =>
    {
        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(12.3), "CLEAN", string.Empty,
            Cleanup: TimeSpan.FromSeconds(10.4)));
        face.Measure(Avalonia.Size.Infinity);

        face.DesiredSize.Width.ShouldBeLessThanOrEqualTo(Tokens.Size.PillWindowWidth, theme.Id);
    });
```

- [ ] **Step 2: Run `--filter "FullyQualifiedName~PillTests"`, expect compile failure.**

- [ ] **Step 3: Implement.**
  - `PillState`: `internal readonly record struct PillState(PillPhase Phase, double Level, TimeSpan Elapsed, string Mode, string Text, TimeSpan? Cleanup = null, bool CleanupFailed = false);` and add the two `<param>` docs ("Cleanup time, in <see cref="PillPhase.Latency"/> after a cleaned dictation; null otherwise." / "True when that cleanup fell back to the raw text.").
  - `PillFace` helpers, after `Timer`:

```csharp
    /// <summary>" 0.7s" or " failed" after the mode word, in the latency phase of a cleaned dictation; empty otherwise.</summary>
    protected static string CleanupNote(PillState s) =>
        s.Phase != PillPhase.Latency || s.Cleanup is not { } c ? string.Empty
        : s.CleanupFailed ? " failed" : " " + Seconds(c);

    /// <summary>" · clean 0.7s" for faces whose timer has no mode word beside it.</summary>
    protected static string CleanupTail(PillState s) =>
        CleanupNote(s) is { Length: > 0 } note ? " · clean" + note : string.Empty;
```

  - Faces with a mode word append `CleanupNote(state)` after it:
    - `PaperPill.cs:108`: `_meta.Text = $"{Timer(state)} · {(state.Mode switch { … })}{CleanupNote(state)}";` → `1.2s · clean 0.7s`
    - `FluentPill.cs:90`: same, after the `switch` → `1.2s · Cleaned 0.7s`
    - `OrbPill.cs:101`: `$"{Timer(state)} · {state.Mode}{CleanupNote(state)}"`
  - Faces with a bare timer append `CleanupTail(state)`:
    - `TidePill.cs:97`: `_timer.Text = Timer(state) + CleanupTail(state);`
    - `MonoPill.cs:116`: the Latency branch becomes `Seconds(state.Elapsed) + CleanupTail(state)`.
  - `HudWindow`: add `private TimeSpan? _lastCleanup; private bool _lastCleanupFailed;`; `OnEngineCompleted` becomes a block that also sets `_lastCleanup = result.CleanupTime; _lastCleanupFailed = result.CleanupFailed == true;`; the Latency update passes `Cleanup: _lastCleanup, CleanupFailed: _lastCleanupFailed`.
  - If `The_cleanup_split_fits_the_pill` fails for a face, that face uses the short form: replace `" · clean" + note` / the mode-word suffix with `" · " + Seconds(c)` (failure: `" · raw"`) for that face only, via its own call — do not widen the face.

- [ ] **Step 4: Run the whole suite, expect PASS.**
- [ ] **Step 5: Final gate** — from `windows/`: `dotnet format VoxScribe.sln --verify-no-changes` and `dotnet build VoxScribe.sln -c Release --no-incremental -warnaserror`; both clean.
- [ ] **Step 6: Commit** — `feat: pill shows the cleanup time beside the felt latency`

---

## By hand (owner, after the last task)

- Toggle mode on, auto-stop 3 s: speak, stop talking — the dictation types itself after ~3 s. With nothing said, it keeps recording.
- A cleaned dictation: every theme's pill shows `· clean 0.7s` (or `failed`) without clipping; History and Home show the timing.
