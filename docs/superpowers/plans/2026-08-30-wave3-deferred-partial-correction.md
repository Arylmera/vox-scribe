# Wave 3 — Deferred Partial Correction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox ('- [ ]') syntax for tracking.

**Goal:** When incremental injection is on and the recogniser revises text already typed, reconcile at final-segment safe points: backspace the divergent tail (capped at 80 chars) and retype the corrected tail, behind a settings toggle that defaults off.

**Architecture:** A pure static planner (`TailReconciler`) computes the longest-common-prefix diff between the injection journal's text and the new hypothesis, returning a delete-count + inject-string plan. `DictationEngine` applies the plan at its one final-segment site (`TranscribeSegmentAsync` — this engine has no mid-segment partial callbacks, so segment-final completion **is** the safe point; the tail flushed by `ProcessAsync` routes through the same method). A `DeferredPartialCorrection` bool flows from `SettingsData` through `Composition` to the engine.

**Tech Stack:** .NET 10, VoxScribe.Core (platform-neutral), xUnit v2 + Shouldly, VoxScribe.Testing fakes.

## Global Constraints — copy these verbatim:
- .NET 10, Avalonia UI; build/test with: cd windows && dotnet test VoxScribe.CrossPlatform.slnf
- NAudio pinned 2.3.0; Avalonia.Headless.XUnit pinned 11.3.20; org.k2fsa.sherpa.onnx pinned 1.13.5 (never reference Microsoft.ML.OnnxRuntime)
- VoxScribe.Platform.Windows stays logic-free; all logic in platform-neutral projects behind interfaces (net10.0, CA1416 guards)
- Views must not contain literal values — every colour/size/radius/duration comes from Design/DesignTokens.cs; add tokens rather than inlining
- Red means recording, nothing else is red; amber/green are instrumentation only (UiTests.cs pins this)
- shared/dictionary-test-vectors.json is the spec for correction behaviour: change vectors first, watch red, then make green
- Dictionary regexes stay in the ICU/.NET safe subset, RegexOptions.CultureInvariant, NFC normalization
- Anything touching PushToTalkHook or real injection must be flagged "manual test required"

## Wave-1 dependencies (consumed, NOT redefined here)

Wave 1 must be merged before this wave starts. These are its **final, verified** names — use them exactly:

- `VoxScribe.Abstractions.ITextInjector.BackspaceAsync(CancellationToken cancellationToken)` → `ValueTask<bool>`. Sends **ONE** backspace keystroke; `false` means the keystroke failed. There is no count-taking delete — the count loop lives in Core, because `VoxScribe.Platform.Windows` stays logic-free.
- `VoxScribe.Core.InjectionJournal` exposed as `public InjectionJournal Journal { get; }` on `DictationEngine`, with:
  - `void BeginDictation()` — called at the start of each dictation, so the journal always describes only the last one
  - `void Record(string injected)` — appends
  - `string InjectedText { get; }` — exact text injected this utterance
  - `void Retract(int count)` — "the last N **chars** were taken back" (clamped at zero)
- Wave 1 already calls `Journal.Record(...)` at both injection sites (per-segment inside `TranscribeSegmentAsync`, and end-of-utterance).
- `DictationEngine.UndoLastDictationAsync(CancellationToken)` exists and contains the backspace pacing loop (`BackspaceBurst = 40`, `BackspaceGapMilliseconds = 4`) that Task 2 extracts and shares.

**Chars vs keystrokes — the one trap in this wave.** `Journal.Retract` and `TailReconciler` count UTF-16 **chars**; backspace keystrokes are counted in **graphemes** (`StringInfo.LengthInTextElements`), because one backspace deletes a whole emoji, not half of a surrogate pair. Never pass a char count to the backspace loop. Wave 1's `UndoLastDictationAsync` already makes this distinction; Task 2 preserves it.

**Manual test required (global flag):** this drives real SendInput backspace bursts. Target apps (terminals, IDEs with autocomplete, chat boxes) may react badly to rapid backspaces. CI covers the logic only; Task 4 lists the manual pass.

---

### Task 1: TailReconciler — pure reconcile planner

**Files:**
- Create: `windows/src/VoxScribe.Core/TailReconciler.cs`
- Create: `windows/tests/VoxScribe.Core.Tests/TailReconcilerTests.cs`

**Interfaces:**
- Consumes: nothing (pure string logic).
- Produces:
  - `public static class TailReconciler` with `public const int DefaultMaxRetrace = 80;`
  - `public readonly record struct TailReconciler.ReconcilePlan(int DeleteCount, string Inject)` with `public static readonly ReconcilePlan None`
  - `public static ReconcilePlan Plan(string injected, string hypothesis, int maxRetrace = DefaultMaxRetrace)`

**Steps:**

- [ ] Write the failing tests — `windows/tests/VoxScribe.Core.Tests/TailReconcilerTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// TailReconciler plans deferred correction of already-injected text: longest
/// common prefix, delete the divergent tail, retype the hypothesis tail —
/// abandoning the whole idea when the retrace would exceed the cap.
/// </summary>
public sealed class TailReconcilerTests
{
    [Fact]
    public void Identical_text_plans_nothing() =>
        TailReconciler.Plan("hello world", "hello world")
            .ShouldBe(TailReconciler.ReconcilePlan.None);

    [Fact]
    public void Pure_append_deletes_nothing()
    {
        var plan = TailReconciler.Plan("hello", "hello world");
        plan.DeleteCount.ShouldBe(0);
        plan.Inject.ShouldBe(" world");
    }

    [Fact]
    public void Revised_tail_is_deleted_and_retyped()
    {
        var plan = TailReconciler.Plan("hello wrold", "hello world");
        plan.DeleteCount.ShouldBe(4);   // "rold"
        plan.Inject.ShouldBe("orld");
    }

    [Fact]
    public void Shrunk_hypothesis_deletes_only()
    {
        var plan = TailReconciler.Plan("hello there", "hello");
        plan.DeleteCount.ShouldBe(6);   // " there"
        plan.Inject.ShouldBe(string.Empty);
    }

    [Fact]
    public void Empty_journal_types_whole_hypothesis()
    {
        var plan = TailReconciler.Plan(string.Empty, "hello");
        plan.DeleteCount.ShouldBe(0);
        plan.Inject.ShouldBe("hello");
    }

    [Fact]
    public void Divergence_beyond_cap_is_abandoned()
    {
        var plan = TailReconciler.Plan(
            "a" + new string('x', 100),
            "a" + new string('y', 100));
        plan.ShouldBe(TailReconciler.ReconcilePlan.None,
            "a wild hypothesis must never destroy 100 chars of typed text");
    }

    [Fact]
    public void Cap_is_inclusive_at_exactly_max()
    {
        var plan = TailReconciler.Plan(
            "keep" + new string('x', TailReconciler.DefaultMaxRetrace),
            "keep" + new string('y', TailReconciler.DefaultMaxRetrace));
        plan.DeleteCount.ShouldBe(TailReconciler.DefaultMaxRetrace);
        plan.Inject.ShouldBe(new string('y', TailReconciler.DefaultMaxRetrace));
    }

    [Fact]
    public void Surrogate_pair_is_never_split()
    {
        // U+1F3A4 (D83C DFA4) vs U+1F3B5 (D83C DFB5): naive char LCP is 2
        // (shared 'x' + shared high surrogate D83C) — the plan must retreat to
        // the pair boundary and delete/retype the whole emoji.
        var plan = TailReconciler.Plan("x🎤", "x🎵");
        plan.DeleteCount.ShouldBe(2);
        plan.Inject.ShouldBe("🎵");
    }
}
```

- [ ] Run it — from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~TailReconcilerTests` — expected failure: build error `CS0103: The name 'TailReconciler' does not exist` (class missing).

- [ ] Minimal implementation — `windows/src/VoxScribe.Core/TailReconciler.cs`:

```csharp
using System;

namespace VoxScribe.Core;

/// <summary>
/// Plans deferred correction of text already injected into the focused app.
/// Given what the journal says was typed and the recogniser's revised
/// hypothesis for the same utterance, computes how many characters to
/// backspace and what to retype. Pure — the engine executes the plan.
/// </summary>
public static class TailReconciler
{
    /// <summary>
    /// Max characters we are willing to backspace in one reconcile. Beyond
    /// this the hypothesis has gone wild; keep what is typed and do nothing.
    /// </summary>
    public const int DefaultMaxRetrace = 80;

    /// <summary>Delete <see cref="DeleteCount"/> backspaces, then type <see cref="Inject"/>.</summary>
    public readonly record struct ReconcilePlan(int DeleteCount, string Inject)
    {
        public static readonly ReconcilePlan None = new(0, string.Empty);
    }

    public static ReconcilePlan Plan(string injected, string hypothesis, int maxRetrace = DefaultMaxRetrace)
    {
        ArgumentNullException.ThrowIfNull(injected);
        ArgumentNullException.ThrowIfNull(hypothesis);

        var limit = Math.Min(injected.Length, hypothesis.Length);
        var lcp = 0;
        while (lcp < limit && injected[lcp] == hypothesis[lcp])
        {
            lcp++;
        }

        // Never split a surrogate pair: a prefix ending on a high surrogate
        // would leave half an emoji on screen. Retreat to the pair boundary.
        if (lcp > 0 && char.IsHighSurrogate(injected[lcp - 1]))
        {
            lcp--;
        }

        var delete = injected.Length - lcp;
        if (delete == 0 && hypothesis.Length == injected.Length)
        {
            return ReconcilePlan.None;
        }

        if (delete > maxRetrace)
        {
            return ReconcilePlan.None; // ponytail: give up rather than mass-delete; smarter merge only if this ever bites
        }

        return new ReconcilePlan(delete, hypothesis[lcp..]);
    }
}
```

- [ ] Run again — from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~TailReconcilerTests` — expected: 8 passed.
- [ ] Run the full suite — from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf` — expected: all green.
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/TailReconciler.cs windows/tests/VoxScribe.Core.Tests/TailReconcilerTests.cs
git commit -m "core: TailReconciler plans capped backspace-and-retype corrections"
```

---

### Task 2: DictationEngine reconciles at final-segment safe points

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs` (property + the incremental-injection block inside `TranscribeSegmentAsync`, ~L440)
- Create: `windows/tests/VoxScribe.Core.Tests/DeferredCorrectionTests.cs`

**Interfaces:**
- Consumes: `TailReconciler.Plan` (Task 1); Wave 1's `DictationEngine.Journal` (`InjectedText`/`Record`/`Retract`), `ITextInjector.BackspaceAsync(CancellationToken)`, and Wave 1's backspace pacing constants; existing `PartialText`, `InjectIncrementally`, `_injector`.
- Produces:
  - `public bool DeferredPartialCorrection { get; set; }` on `DictationEngine` (default `false`; only takes effect when `InjectIncrementally` is true, because the reconcile site lives inside that branch)
  - `private async Task<bool> SendBackspacesAsync(int keystrokes, CancellationToken cancellationToken)` on `DictationEngine` — the paced loop extracted from Wave 1's `UndoLastDictationAsync`, now shared by undo and reconcile.

**Safe-point decision (from the engine map):** `TranscribeSegmentAsync` completions are the only place segment text becomes final — the engine exposes no mid-segment partial hypotheses today, and the release-time tail from `ProcessAsync`'s `Flush()` is queued through the same method. Reconciling there satisfies "finals only, not every partial" with zero extra plumbing. The hypothesis at that point is `PartialText` (all corrected segments joined so far); the journal holds everything actually typed this utterance — any divergence between them (e.g. a future streaming wave typing eager partials) is exactly the tail to fix.

**Steps:**

- [ ] Write the failing tests — `windows/tests/VoxScribe.Core.Tests/DeferredCorrectionTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using VoxScribe.Abstractions;
using VoxScribe.Core;
using VoxScribe.Dictionary;
using VoxScribe.Testing;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// Wave 3: with DeferredPartialCorrection on, the engine reconciles the
/// injection journal against the corrected hypothesis at each final segment —
/// backspacing the divergent tail (capped) and retyping the fix. Off (the
/// default) must be byte-identical to Wave 1 behaviour.
/// </summary>
public sealed class DeferredCorrectionTests
{
    /// <summary>
    /// Injector that models the target app's text box: text appends, one
    /// BackspaceAsync deletes one text element (grapheme), exactly like a real
    /// keyboard.
    /// </summary>
    private sealed class ScreenInjector : ITextInjector
    {
        private readonly StringBuilder _screen = new();
        public List<string> Injections { get; } = [];
        public int Backspaces { get; private set; }
        public string Screen => _screen.ToString();

        public ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
        {
            Injections.Add(text);
            _screen.Append(text);
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> BackspaceAsync(CancellationToken cancellationToken)
        {
            Backspaces++;
            if (_screen.Length > 0)
            {
                // One keystroke removes one grapheme, so a surrogate pair goes whole.
                var last = _screen.Length - 1;
                var width = last > 0 && char.IsLowSurrogate(_screen[last]) && char.IsHighSurrogate(_screen[last - 1]) ? 2 : 1;
                _screen.Length -= width;
            }

            return ValueTask.FromResult(true);
        }

        /// <summary>Pretend earlier (partial) injection already typed this.</summary>
        public void Seed(string text) => _screen.Append(text);
    }

    /// <summary>Transcriber that lets the test run code just before a segment goes final.</summary>
    private sealed class CallbackTranscriber(Func<string> respond) : ITranscriber
    {
        public bool IsReady => true;
        public ValueTask<bool> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<string> TranscribeAsync(
            ReadOnlyMemory<float> samples, IReadOnlyList<string> biasPhrases, CancellationToken cancellationToken) =>
            ValueTask.FromResult(respond());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static DictationEngine Build(
        IAudioCapture capture, FakeHotkeySource hotkey, ITranscriber transcriber, ITextInjector injector) =>
        new(capture, hotkey, transcriber, injector, () => Array.Empty<DictionaryEntry>(), new FakeClock());

    private static async Task DictateFullyAsync(FakeHotkeySource hotkey, IAudioCapture capture, DictationEngine engine)
    {
        hotkey.Press();
        for (var i = 0; i < 200000 && !capture.IsCapturing; i++) await Task.Yield();
        for (var i = 0; i < 200000 && capture.IsCapturing; i++) await Task.Yield();
        hotkey.Release();
        for (var i = 0; i < 200000 && engine.State != DictationState.Idle; i++) await Task.Yield();
    }

    [Fact]
    public async Task Toggle_off_never_backspaces()
    {
        var capture = new FakeAudioCapture(FakeAudioCapture.Phrases(2));
        var hotkey = new FakeHotkeySource();
        var injector = new ScreenInjector();
        await using var engine = Build(capture, hotkey, new FakeTranscriber("hello", "world"), injector);
        engine.IncrementalInjection = true;

        await DictateFullyAsync(hotkey, capture, engine);

        injector.Backspaces.ShouldBe(0);
        injector.Screen.ShouldBe("hello world");
    }

    [Fact]
    public async Task Toggle_on_without_revision_matches_plain_incremental_output()
    {
        var capture = new FakeAudioCapture(FakeAudioCapture.Phrases(2));
        var hotkey = new FakeHotkeySource();
        var injector = new ScreenInjector();
        await using var engine = Build(capture, hotkey, new FakeTranscriber("hello", "world"), injector);
        engine.IncrementalInjection = true;
        engine.DeferredPartialCorrection = true;

        await DictateFullyAsync(hotkey, capture, engine);

        injector.Backspaces.ShouldBe(0, "identical journal and hypothesis must be a no-op");
        injector.Screen.ShouldBe("hello world");
    }

    [Fact]
    public async Task Revised_hypothesis_backspaces_and_retypes_the_tail()
    {
        var capture = new FakeAudioCapture(FakeAudioCapture.Tone(2.0));
        var hotkey = new FakeHotkeySource();
        var injector = new ScreenInjector();
        DictationEngine? engineRef = null;
        var transcriber = new CallbackTranscriber(() =>
        {
            // Simulate an earlier eager partial having typed a wrong hypothesis
            // before this segment went final.
            engineRef!.Journal.Record("helo wrold");
            injector.Seed("helo wrold");
            return "hello world";
        });
        await using var engine = Build(capture, hotkey, transcriber, injector);
        engineRef = engine;
        engine.IncrementalInjection = true;
        engine.DeferredPartialCorrection = true;

        await DictateFullyAsync(hotkey, capture, engine);

        // LCP("helo wrold", "hello world") = "hel" (3) → delete 10-3 = 7 chars
        // ("o wrold" = 7 graphemes = 7 keystrokes), retype "lo world"
        injector.Backspaces.ShouldBe(7);
        injector.Injections.ShouldBe(new[] { "lo world" });
        injector.Screen.ShouldBe("hello world");
    }

    [Fact]
    public async Task Wild_hypothesis_beyond_cap_leaves_typed_text_alone()
    {
        var typed = "a" + new string('x', 100);
        var capture = new FakeAudioCapture(FakeAudioCapture.Tone(2.0));
        var hotkey = new FakeHotkeySource();
        var injector = new ScreenInjector();
        DictationEngine? engineRef = null;
        var transcriber = new CallbackTranscriber(() =>
        {
            engineRef!.Journal.Record(typed);
            injector.Seed(typed);
            return "a" + new string('y', 100);
        });
        await using var engine = Build(capture, hotkey, transcriber, injector);
        engineRef = engine;
        engine.IncrementalInjection = true;
        engine.DeferredPartialCorrection = true;

        await DictateFullyAsync(hotkey, capture, engine);

        injector.Backspaces.ShouldBe(0);
        injector.Injections.ShouldBeEmpty();
        injector.Screen.ShouldBe(typed);
    }
}
```

- [ ] Run it — from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~DeferredCorrectionTests` — expected failure: build error `CS1061: 'DictationEngine' does not contain a definition for 'DeferredPartialCorrection'`.

- [ ] Minimal implementation — `windows/src/VoxScribe.Core/DictationEngine.cs`. Add the property next to `IncrementalInjection` (~L182):

```csharp
/// <summary>
/// Wave 3: in incremental mode, reconcile already-typed text against the
/// corrected hypothesis at each final segment — backspace the divergent
/// tail (capped at <see cref="TailReconciler.DefaultMaxRetrace"/> chars)
/// and retype the fix. Default off. No effect outside incremental mode.
/// Manual test required: real apps react to backspace bursts.
/// </summary>
public bool DeferredPartialCorrection { get; set; }
```

  Extract Wave 1's backspace loop out of `UndoLastDictationAsync` so undo and reconcile share one paced implementation (DRY — the pacing constants `BackspaceBurst`/`BackspaceGapMilliseconds` are already there from Wave 1):

```csharp
    /// <summary>
    /// Sends <paramref name="keystrokes"/> backspaces with the injector's typing
    /// cadence (bursts with a small settle gap) so slow target apps don't drop
    /// keystrokes. Counted in text elements: one keystroke deletes one grapheme.
    /// False as soon as a keystroke fails.
    /// </summary>
    private async Task<bool> SendBackspacesAsync(int keystrokes, CancellationToken cancellationToken)
    {
        for (var i = 0; i < keystrokes; i++)
        {
            if (!await _injector.BackspaceAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            if ((i + 1) % BackspaceBurst == 0)
            {
                await Task.Delay(BackspaceGapMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        return true;
    }
```

  Then replace the body of Wave 1's `UndoLastDictationAsync` loop with a call to it — the method becomes:

```csharp
    public async Task<bool> UndoLastDictationAsync(CancellationToken cancellationToken)
    {
        if (State != DictationState.Idle)
        {
            return false;
        }

        var text = Journal.InjectedText;
        if (text.Length == 0)
        {
            return false;
        }

        if (!await SendBackspacesAsync(new StringInfo(text).LengthInTextElements, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        Journal.Retract(text.Length);
        return true;
    }
```

  Now replace the incremental injection block inside `TranscribeSegmentAsync` (~L440; the `else` branch is Wave 1's existing code, kept verbatim):

```csharp
if (InjectIncrementally)
{
    if (DeferredPartialCorrection)
    {
        var plan = TailReconciler.Plan(Journal.InjectedText, PartialText);
        if (plan.DeleteCount > 0)
        {
            // The plan counts chars; the keyboard counts graphemes.
            var doomed = Journal.InjectedText[^plan.DeleteCount..];
            if (!await SendBackspacesAsync(
                    new StringInfo(doomed).LengthInTextElements,
                    CancellationToken.None).ConfigureAwait(false))
            {
                return; // a failed keystroke leaves screen and journal in step; try again next segment
            }

            Journal.Retract(plan.DeleteCount);
        }

        if (plan.Inject.Length > 0)
        {
            await _injector.InjectAsync(plan.Inject, CancellationToken.None).ConfigureAwait(false);
            Journal.Record(plan.Inject);
        }
    }
    else
    {
        await _injector.InjectAsync(separator + corrected, CancellationToken.None).ConfigureAwait(false);
        Journal.Record(separator + corrected);
    }
}
```

  (`separator`/`corrected` are the existing locals at that site; `PartialText` has already been extended with `corrected` a few lines above, so it IS the current full hypothesis. `using System.Globalization;` is already at the top of the file from Wave 1. If Wave 1's `Journal.Record` call sits outside the `if (InjectIncrementally)` block, move only the incremental record inside the `else` as shown and leave the end-of-utterance site untouched.)

  Because `UndoLastDictationAsync` changed shape, Wave 1's three undo tests must still pass unmodified — that is the regression gate for the extraction.

- [ ] Run again — from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~DeferredCorrectionTests` — expected: 4 passed.
- [ ] Run the full suite — from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf` — expected: all green, **including Wave 1's three `Undo_*` tests unmodified** (they gate the `SendBackspacesAsync` extraction) and existing `DictationEngineTests` (default is off).
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/DictationEngine.cs windows/tests/VoxScribe.Core.Tests/DeferredCorrectionTests.cs
git commit -m "core: deferred correction of injected text at final-segment safe points"
```

---

### Task 3: Settings toggle + composition wiring

**Files:**
- Modify: `windows/src/VoxScribe.Core/AppSettings.cs` (add field to `SettingsData`, ~L57 next to `IncrementalInjection`)
- Modify: `windows/src/VoxScribe.App/Composition.cs` (initial wiring ~L132 and the `settings.Changed` handler ~L145–159)
- Create: `windows/tests/VoxScribe.Core.Tests/DeferredCorrectionSettingsTests.cs`

**Interfaces:**
- Consumes: `AppSettings`/`SettingsData` (record `with` semantics, source-generated `SettingsJsonContext`), `DictationEngine.DeferredPartialCorrection` (Task 2).
- Produces: `public bool DeferredPartialCorrection { get; init; }` on `SettingsData`, default `false`.

**Steps:**

- [ ] Write the failing tests — `windows/tests/VoxScribe.Core.Tests/DeferredCorrectionSettingsTests.cs`:

```csharp
using System.IO;
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The Wave 3 toggle ships default-off and must survive the source-generated
/// JSON round trip (trim/single-file safety).
/// </summary>
public sealed class DeferredCorrectionSettingsTests
{
    [Fact]
    public void Default_is_off() =>
        new SettingsData().DeferredPartialCorrection.ShouldBeFalse();

    [Fact]
    public void Round_trips_through_settings_json()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            var settings = new AppSettings(path);
            settings.Update(settings.Data with { DeferredPartialCorrection = true });

            new AppSettings(path).Data.DeferredPartialCorrection.ShouldBeTrue();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
```

- [ ] Run it — from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~DeferredCorrectionSettingsTests` — expected failure: build error `CS1061: 'SettingsData' does not contain a definition for 'DeferredPartialCorrection'`.

- [ ] Minimal implementation — `windows/src/VoxScribe.Core/AppSettings.cs`, in `SettingsData` directly after `IncrementalInjection` (~L57):

```csharp
/// <summary>
/// Wave 3: in incremental mode, backspace-and-retype already-typed text when
/// the recogniser revises it at a segment final. Off by default — real apps
/// may react badly to backspace bursts.
/// </summary>
public bool DeferredPartialCorrection { get; init; }
```

- [ ] Run again — from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~DeferredCorrectionSettingsTests` — expected: 2 passed.
- [ ] Commit the setting:

```
git add windows/src/VoxScribe.Core/AppSettings.cs windows/tests/VoxScribe.Core.Tests/DeferredCorrectionSettingsTests.cs
git commit -m "core: DeferredPartialCorrection setting, default off"
```

- [ ] Wire it in `windows/src/VoxScribe.App/Composition.cs` — two one-line additions mirroring `IncrementalInjection` exactly. After `engine.IncrementalInjection = settings.Data.IncrementalInjection;` (~L132):

```csharp
engine.DeferredPartialCorrection = settings.Data.DeferredPartialCorrection;
```

  And inside the `settings.Changed` handler (~L145–159), next to the existing `engine.IncrementalInjection = ...` re-set:

```csharp
engine.DeferredPartialCorrection = settings.Data.DeferredPartialCorrection;
```

  No UI checkbox in this wave: the toggle is settings.json-only while default-off (deliberate scope cut — add a SettingsView switch when the feature graduates from experimental). Composition wiring has no headless test (engine is null off-Windows in `Composition.Create`); it is covered by build + the Task 4 manual pass.

- [ ] Build + full suite — from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf` — expected: all green.
- [ ] Commit the wiring:

```
git add windows/src/VoxScribe.App/Composition.cs
git commit -m "windows: wire DeferredPartialCorrection through composition"
```

---

### Task 4: Manual verification pass (real injection — manual test required)

**Files:** none (checklist only; findings go in the PR/commit description).

**Interfaces:** n/a.

This wave changes what `SendInputTextInjector` is asked to do (backspace bursts via Wave 1's `BackspaceAsync`, now driven mid-utterance instead of only on an explicit undo). CI proves the plan logic; only a human at a real Windows session can prove target apps tolerate it.

**Steps:**

- [ ] On Windows, publish and run the app; set `"IncrementalInjection": true` and `"DeferredPartialCorrection": true` in `%LOCALAPPDATA%\VoxScribe\settings.json`; restart or trigger a settings save so `Changed` fires.
- [ ] Dictate multi-phrase utterances (with clear pauses) into: Notepad, VS Code, a browser text area, Windows Terminal. Confirm text appears incrementally and no stray deletions occur (with today's engine the journal always matches the hypothesis, so expected behaviour is: identical to plain incremental mode, zero backspaces).
- [ ] Confirm the toggle off (default) is bit-identical to the previous release in the same apps.
- [ ] Confirm hold-key and toggle-mode dictation both still start/stop cleanly (PushToTalkHook path — manual test required by repo rule).
- [ ] Regression on Wave 1's UNDO button (it now runs through the extracted `SendBackspacesAsync`): dictate a sentence containing an emoji, press UNDO, confirm the text disappears completely with no leftover half-character.
- [ ] Record any app that misbehaves under backspace bursts in the PR description; that list gates enabling the toggle by default in a later wave.
