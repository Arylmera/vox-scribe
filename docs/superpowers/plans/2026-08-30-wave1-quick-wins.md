# Wave 1 Quick Wins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox ('- [ ]') syntax for tracking.

**Goal:** Ship undo-last-dictation, French/English spoken punctuation commands, and dictionary suggestions mined from cleanup rewrites — all built on a new injection journal in Core.

**Architecture:** A platform-neutral `InjectionJournal` in VoxScribe.Core records the exact strings DictationEngine sends to `ITextInjector`; undo replays that length as backspaces through a new single-keystroke `BackspaceAsync` primitive (loop stays in Core, Platform.Windows sends one VK_BACK pair). Spoken punctuation is a position-aware `VoiceCommandProcessor` pass in Core (NOT DictionaryCorrector rules — punctuation needs spacing logic, so `shared/dictionary-test-vectors.json` is untouched). Suggestions come from storing raw pre-cleanup text on `DictationResult`/`TranscriptRecord` and mining recurring single-word substitutions in a pure `SuggestionEngine`.

**Tech Stack:** .NET 10, Avalonia UI (headless tests via Avalonia.Headless.XUnit), xUnit v2 + Shouldly, System.Text.RegularExpressions.

## Global Constraints — copy these verbatim:
- .NET 10, Avalonia UI; build/test with: cd windows && dotnet test VoxScribe.CrossPlatform.slnf
- NAudio pinned 2.3.0; Avalonia.Headless.XUnit pinned 11.3.20; org.k2fsa.sherpa.onnx pinned 1.13.5 (never reference Microsoft.ML.OnnxRuntime)
- VoxScribe.Platform.Windows stays logic-free; all logic in platform-neutral projects behind interfaces (net10.0, CA1416 guards)
- Views must not contain literal values — every colour/size/radius/duration comes from Design/DesignTokens.cs; add tokens rather than inlining
- Red means recording, nothing else is red; amber/green are instrumentation only (UiTests.cs pins this)
- shared/dictionary-test-vectors.json is the spec for correction behaviour: change vectors first, watch red, then make green
- Dictionary regexes stay in the ICU/.NET safe subset, RegexOptions.CultureInvariant, NFC normalization
- Anything touching PushToTalkHook or real injection must be flagged "manual test required"

All test-run commands below are, verbatim:

```
cd windows && dotnet test VoxScribe.CrossPlatform.slnf
```

(run from repo root `C:\Users\guill\Documents\git\vox-scribe`; in PowerShell use `cd windows; dotnet test VoxScribe.CrossPlatform.slnf`).

---

### Task 1: InjectionJournal (foundation)

**Files:**
- Create: `windows/src/VoxScribe.Core/InjectionJournal.cs`
- Create: `windows/tests/VoxScribe.Core.Tests/InjectionJournalTests.cs`

**Interfaces:**
- Produces: `public sealed class InjectionJournal` with `void BeginDictation()`, `void Record(string injected)`, `string InjectedText { get; }`, `void Retract(int count)`

**Steps:**

- [ ] Write the failing tests in `windows/tests/VoxScribe.Core.Tests/InjectionJournalTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The injection journal is the record of exactly what DictationEngine sent to the
/// injector for the current/last dictation. Undo counts its characters; a later
/// wave diffs against it, so Retract must model "the last N chars were taken back".
/// </summary>
public sealed class InjectionJournalTests
{
    [Fact]
    public void Records_appended_text_in_order()
    {
        var journal = new InjectionJournal();
        journal.Record("hello");
        journal.Record(" world");
        journal.InjectedText.ShouldBe("hello world");
    }

    [Fact]
    public void Begin_dictation_clears_the_previous_journal()
    {
        var journal = new InjectionJournal();
        journal.Record("old text");
        journal.BeginDictation();
        journal.InjectedText.ShouldBe(string.Empty);
    }

    [Fact]
    public void Retract_removes_the_last_chars_and_clamps_at_zero()
    {
        var journal = new InjectionJournal();
        journal.Record("hello");
        journal.Retract(2);
        journal.InjectedText.ShouldBe("hel");
        journal.Retract(99);
        journal.InjectedText.ShouldBe(string.Empty);
        journal.Retract(-5);
        journal.InjectedText.ShouldBe(string.Empty);
    }

    [Fact]
    public void Empty_journal_reports_empty_text()
    {
        new InjectionJournal().InjectedText.ShouldBe(string.Empty);
    }
}
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect compile failure: `CS0246 The type or namespace name 'InjectionJournal' could not be found`.
- [ ] Create `windows/src/VoxScribe.Core/InjectionJournal.cs`:

```csharp
using System.Text;

namespace VoxScribe.Core;

/// <summary>
/// Records exactly what was sent to <c>ITextInjector</c> for the current/last
/// dictation — the precise string sequence, concatenated. Foundation for
/// undo-last-dictation (backspace count) and for diff-based correction later
/// (hence <see cref="Retract"/>: "the last N chars were taken back").
/// Thread-safe: Record is called from the transcription chain, reads from the UI.
/// </summary>
public sealed class InjectionJournal
{
    private readonly StringBuilder _text = new();
    private readonly Lock _lock = new();

    /// <summary>Starts a new dictation: forget the previous one.</summary>
    public void BeginDictation()
    {
        lock (_lock)
        {
            _text.Clear();
        }
    }

    /// <summary>Appends one injected string, exactly as sent to the injector.</summary>
    public void Record(string injected)
    {
        lock (_lock)
        {
            _text.Append(injected);
        }
    }

    /// <summary>Full injected text of the current/last dictation.</summary>
    public string InjectedText
    {
        get
        {
            lock (_lock)
            {
                return _text.ToString();
            }
        }
    }

    /// <summary>The last <paramref name="count"/> chars were deleted again (undo). Clamps.</summary>
    public void Retract(int count)
    {
        lock (_lock)
        {
            if (count <= 0)
            {
                return;
            }

            _text.Length = Math.Max(0, _text.Length - count);
        }
    }
}
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (4 new tests pass).
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/InjectionJournal.cs windows/tests/VoxScribe.Core.Tests/InjectionJournalTests.cs
git commit -m "core: injection journal records exactly what was typed"
```

---

### Task 2: DictationEngine journals every injection

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs`
- Modify: `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`

**Interfaces:**
- Consumes: `InjectionJournal` (Task 1); existing fakes `FakeAudioCapture`, `FakeHotkeySource`, `FakeTranscriber`, `RecordingTextInjector`, `FakeClock` from `VoxScribe.Testing`
- Produces: `public InjectionJournal Journal { get; }` on `DictationEngine`

**Steps:**

- [ ] Add failing tests to `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs` (inside the existing `DictationEngineTests` class, reusing its `Build` and `DictateAsync` helpers at lines 21–38):

```csharp
    [Fact]
    public async Task Journal_holds_exactly_what_was_injected()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("hello world"), injector);

        await DictateAsync(hotkey, engine);

        engine.Journal.InjectedText.ShouldBe(string.Concat(injector.Injected));
        engine.Journal.InjectedText.ShouldBe("hello world");
    }

    [Fact]
    public async Task Journal_resets_on_each_new_dictation()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("first", "second"), injector);

        await DictateAsync(hotkey, engine);
        await DictateAsync(hotkey, engine);

        engine.Journal.InjectedText.ShouldBe("second", "journal must describe only the LAST dictation");
    }

    [Fact]
    public async Task Journal_matches_injection_in_incremental_mode()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var capture = FakeAudioCapture.Phrases(2);
        await using var engine = Build(capture, hotkey, new FakeTranscriber("first bit", "second bit"), injector);
        engine.IncrementalInjection = true;

        await DictateFullyAsync(hotkey, capture, engine);

        engine.Journal.InjectedText.ShouldBe(string.Concat(injector.Injected));
    }
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect compile failure: `'DictationEngine' does not contain a definition for 'Journal'`.
- [ ] Modify `windows/src/VoxScribe.Core/DictationEngine.cs`:
  1. Add the public property near the other public state (after `PartialText`, ~L85):

```csharp
    /// <summary>Exact record of what this engine sent to the injector for the current/last dictation.</summary>
    public InjectionJournal Journal { get; } = new();
```

  2. In `BeginAsync()` (~L261), inside the gated setup where `_capturedSamples` and `PartialText` are reset, add:

```csharp
            Journal.BeginDictation();
```

  3. At the incremental injection site in `TranscribeSegmentAsync` (~L440–445), record what was actually typed. The existing call injects `separator + corrected`; wrap it so the journal only records successful injections:

```csharp
                if (await _injector.InjectAsync(separator + corrected, CancellationToken.None).ConfigureAwait(false))
                {
                    Journal.Record(separator + corrected);
                }
```

  (If the existing code discards the `bool` result with a bare `await`, replace that statement with the block above — keep the exact argument expression the file already uses for the injected string.)
  4. At the end-of-utterance injection site in `ProcessAsync` (~L395–396), same treatment:

```csharp
        if (!InjectIncrementally)
        {
            if (await _injector.InjectAsync(text, CancellationToken.None).ConfigureAwait(false))
            {
                Journal.Record(text);
            }
        }
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (3 new tests pass, no existing test regresses).
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/DictationEngine.cs windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs
git commit -m "core: engine journals every injected string"
```

---

### Task 3: BackspaceAsync injection primitive

**Files:**
- Modify: `windows/src/VoxScribe.Abstractions/Interfaces.cs`
- Modify: `windows/src/VoxScribe.Testing/Fakes.cs`
- Modify: `windows/src/VoxScribe.Platform.Windows/SendInputTextInjector.cs`

**Interfaces:**
- Produces: `ValueTask<bool> BackspaceAsync(CancellationToken cancellationToken)` on `ITextInjector` — sends ONE backspace keystroke. The count loop lives in Core (Task 4); Platform.Windows stays logic-free.

**Steps:**

- [ ] Grep for all `ITextInjector` implementations before touching the interface: `rg "ITextInjector" windows/src windows/tests` — expected implementors: `SendInputTextInjector` (Platform.Windows) and `RecordingTextInjector` (Testing). If the grep reveals another, extend it the same way as the fake.
- [ ] Add to `ITextInjector` in `windows/src/VoxScribe.Abstractions/Interfaces.cs` (after `InjectAsync`, ~L73):

```csharp
    /// <summary>
    /// Sends a single backspace keystroke to the focused control (deletes one
    /// character/newline backward from the caret). Callers loop for counts —
    /// keeping the platform layer a single logic-free keystroke.
    /// </summary>
    ValueTask<bool> BackspaceAsync(CancellationToken cancellationToken);
```

- [ ] Extend `RecordingTextInjector` in `windows/src/VoxScribe.Testing/Fakes.cs` (~L185–196):

```csharp
    /// <summary>Number of backspace keystrokes requested via <see cref="BackspaceAsync"/>.</summary>
    public int Backspaces { get; private set; }

    public ValueTask<bool> BackspaceAsync(CancellationToken cancellationToken)
    {
        Backspaces++;
        return ValueTask.FromResult(true);
    }
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect green (the cross-platform slnf does not include Platform.Windows; the fake now satisfies the interface). If red, an implementor was missed — fix it before continuing.
- [ ] Implement in `windows/src/VoxScribe.Platform.Windows/SendInputTextInjector.cs`. Read the file first to see `KeyInput(int virtualKey, bool up)`'s exact return type (:249). Add a constant next to `PasteThreshold` (~L27) and the method next to `InjectAsync`:

```csharp
    private const int BackspaceKey = 0x08; // VK_BACK — not in the extended-key set, plain scancode path.

    /// <inheritdoc />
    public ValueTask<bool> BackspaceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        KeyInput(BackspaceKey, up: false);
        KeyInput(BackspaceKey, up: true);
        return ValueTask.FromResult(true);
    }
```

  (If `KeyInput` returns `bool`/`uint`, propagate failure: `return ValueTask.FromResult(KeyInput(BackspaceKey, false) && KeyInput(BackspaceKey, true));` adapted to its actual signature. Events go through the existing `SendInput` path, so they carry `PushToTalkHook.InjectedTag` and the app's own hook ignores them.)
- [ ] Build the Windows layer to prove it compiles: `cd windows && dotnet build src/VoxScribe.Platform.Windows/VoxScribe.Platform.Windows.csproj` — expect success.
- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect green.
- [ ] **Manual test required** (real SendInput against a live focused window — CI cannot exercise this): after Task 4's UI wiring, dictate into Notepad, trigger undo, verify the text disappears character-for-character including newlines.
- [ ] Commit:

```
git add windows/src/VoxScribe.Abstractions/Interfaces.cs windows/src/VoxScribe.Testing/Fakes.cs windows/src/VoxScribe.Platform.Windows/SendInputTextInjector.cs
git commit -m "core: BackspaceAsync primitive on ITextInjector, SendInput VK_BACK on Windows"
```

---

### Task 4: Undo last dictation (engine + UNDO key)

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs`
- Modify: `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`
- Modify: `windows/src/VoxScribe.App/Views/MainWindow.cs`

**Interfaces:**
- Consumes: `InjectionJournal` (Task 1), `ITextInjector.BackspaceAsync` (Task 3), `Panels.DeckButton(string label)` (existing, `Views/Panels.cs` L75)
- Produces: `public async Task<bool> UndoLastDictationAsync(CancellationToken cancellationToken)` on `DictationEngine`

**Steps:**

- [ ] Add failing tests to `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`:

```csharp
    [Fact]
    public async Task Undo_backspaces_exactly_the_injected_text_and_empties_the_journal()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("hello world"), injector);

        await DictateAsync(hotkey, engine);
        var undone = await engine.UndoLastDictationAsync(CancellationToken.None);

        undone.ShouldBeTrue();
        injector.Backspaces.ShouldBe("hello world".Length);
        engine.Journal.InjectedText.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Undo_with_nothing_injected_does_nothing()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("hi"), injector);

        (await engine.UndoLastDictationAsync(CancellationToken.None)).ShouldBeFalse();
        injector.Backspaces.ShouldBe(0);
    }

    [Fact]
    public async Task Undo_twice_only_deletes_once()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("hi"), injector);

        await DictateAsync(hotkey, engine);
        (await engine.UndoLastDictationAsync(CancellationToken.None)).ShouldBeTrue();
        (await engine.UndoLastDictationAsync(CancellationToken.None)).ShouldBeFalse();
        injector.Backspaces.ShouldBe("hi".Length);
    }
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect compile failure: `'DictationEngine' does not contain a definition for 'UndoLastDictationAsync'`.
- [ ] Add to `windows/src/VoxScribe.Core/DictationEngine.cs` (near `TogglePushToTalk`, plus `using System.Globalization;` at the top):

```csharp
    // Backspace pacing mirrors the Windows injector's typing cadence (bursts with a
    // small settle gap) so slow target apps don't drop keystrokes.
    private const int BackspaceBurst = 40;
    private const int BackspaceGapMilliseconds = 4;

    /// <summary>
    /// Deletes the last dictation's injected text by sending one backspace per
    /// text element (grapheme — surrogate pairs are one backspace, not two).
    /// Only runs while idle; false when there is nothing to undo or a keystroke failed.
    /// </summary>
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

        var keystrokes = new StringInfo(text).LengthInTextElements;
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

        Journal.Retract(text.Length);
        return true;
    }
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green.
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/DictationEngine.cs windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs
git commit -m "core: undo last dictation replays the journal as backspaces"
```

- [ ] Wire an UNDO key into `windows/src/VoxScribe.App/Views/MainWindow.cs`. Read `BuildVoiceBand()` (~L241–262: grid `"Auto,*,Auto"` holding record key + lamp, meter, counter). Change the column definitions to `"Auto,*,Auto,Auto"` and append after the counter, following the band's existing spacing pattern (`Tokens.Space.*` margins only, no literals):

```csharp
        var undo = Panels.DeckButton("UNDO");
        undo.Margin = new Thickness(Tokens.Space.Base, 0, 0, 0);
        ToolTip.SetTip(undo, "Delete the last dictation's text");
        undo.Click += async (_, _) =>
        {
            if (_composition.Engine is { } engine)
            {
                await engine.UndoLastDictationAsync(CancellationToken.None);
            }
        };
        Grid.SetColumn(undo, 3);
        grid.Children.Add(undo);
```

  (`Panels.DeckButton` is `internal static` in the same assembly — L75 of `Views/Panels.cs`. Adapt the local variable name `grid` to whatever the method actually calls its Grid. `ponytail:` UNDO lives in the main window's voice band, not a global shortcut — a dedicated undo hotkey means another `IHotkeySource`, settings key, and chord-blocker plumbing; add when someone actually asks to undo without opening the window.)
- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect green (headless UI tests still pass; the button is neutral-styled via DeckButton, so the "red means recording" pin is untouched).
- [ ] **Manual test required** (real injection): run the app, dictate "hello world" into Notepad, click UNDO — the eleven characters disappear. Dictate a multiline utterance (long text goes through clipboard paste), UNDO, verify newlines each cost one backspace. Note in the commit body that clipboard-pasted text with `\r\n` may need editor-specific verification.
- [ ] Commit:

```
git add windows/src/VoxScribe.App/Views/MainWindow.cs
git commit -m "windows: UNDO key in the voice band deletes the last dictation"
```

---

### Task 5: VoiceCommandProcessor (spoken punctuation, pure)

**Files:**
- Create: `windows/src/VoxScribe.Core/VoiceCommandProcessor.cs`
- Create: `windows/tests/VoxScribe.Core.Tests/VoiceCommandProcessorTests.cs`

**Interfaces:**
- Produces: `public static class VoiceCommandProcessor` with `public static string Apply(string text)`

**Design decision (per the dictionary map):** spoken punctuation needs position-aware output — no leading space before the mark, one trailing space mid-text, none at end, newlines clean on both sides. That cannot be expressed as literal `DictionaryCorrector` replacements, so this is a sibling deterministic pass in Core, and `shared/dictionary-test-vectors.json` (the corrector's spec) is deliberately NOT touched. Same safety contract as the corrector: NFC input normalization, letter/digit fences, `RegexOptions.CultureInvariant`, ICU-safe subset, 1 s match timeout.

**Steps:**

- [ ] Write the failing tests in `windows/tests/VoxScribe.Core.Tests/VoiceCommandProcessorTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// Spoken punctuation is position-aware (no space before the mark, one after,
/// clean newlines), which is why it is its own pass and not a dictionary rule.
/// </summary>
public sealed class VoiceCommandProcessorTests
{
    [Fact]
    public void French_comma_attaches_to_the_previous_word()
        => VoiceCommandProcessor.Apply("bonjour virgule le monde").ShouldBe("bonjour, le monde");

    [Fact]
    public void English_comma_works_too()
        => VoiceCommandProcessor.Apply("hello comma world").ShouldBe("hello, world");

    [Fact]
    public void Period_at_end_of_text_has_no_trailing_space()
        => VoiceCommandProcessor.Apply("hello world period").ShouldBe("hello world.");

    [Fact]
    public void French_point_becomes_a_period()
        => VoiceCommandProcessor.Apply("c'est fini point").ShouldBe("c'est fini.");

    [Fact]
    public void New_line_command_becomes_a_bare_newline()
        => VoiceCommandProcessor.Apply("first line à la ligne second line").ShouldBe("first line\nsecond line");

    [Fact]
    public void English_new_line_and_glued_newline_both_work()
    {
        VoiceCommandProcessor.Apply("one new line two").ShouldBe("one\ntwo");
        VoiceCommandProcessor.Apply("one newline two").ShouldBe("one\ntwo");
    }

    [Fact]
    public void Longest_command_wins_over_its_prefix()
        => VoiceCommandProcessor.Apply("vraiment point d'interrogation").ShouldBe("vraiment?");

    [Fact]
    public void Question_and_exclamation_marks_in_both_languages()
    {
        VoiceCommandProcessor.Apply("why question mark").ShouldBe("why?");
        VoiceCommandProcessor.Apply("super point d'exclamation oui").ShouldBe("super! oui");
        VoiceCommandProcessor.Apply("wow exclamation mark").ShouldBe("wow!");
    }

    [Fact]
    public void Colon_and_semicolon()
    {
        VoiceCommandProcessor.Apply("note deux points ceci").ShouldBe("note: ceci");
        VoiceCommandProcessor.Apply("first semicolon second").ShouldBe("first; second");
        VoiceCommandProcessor.Apply("un point virgule deux").ShouldBe("un; deux");
    }

    [Fact]
    public void Commands_inside_words_are_left_alone()
        => VoiceCommandProcessor.Apply("pointless appointment").ShouldBe("pointless appointment");

    [Fact]
    public void Matching_is_case_insensitive()
        => VoiceCommandProcessor.Apply("oui Virgule non").ShouldBe("oui, non");

    [Fact]
    public void Hyphenated_spoken_form_matches()
        => VoiceCommandProcessor.Apply("un point-virgule deux").ShouldBe("un; deux");

    [Fact]
    public void Text_without_commands_is_unchanged()
        => VoiceCommandProcessor.Apply("rien de spécial ici").ShouldBe("rien de spécial ici");

    [Fact]
    public void Empty_text_is_unchanged()
        => VoiceCommandProcessor.Apply(string.Empty).ShouldBe(string.Empty);

    [Fact]
    public void Consecutive_commands_compose()
        => VoiceCommandProcessor.Apply("fini point à la ligne suite").ShouldBe("fini.\nsuite");
}
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect compile failure: `CS0246 'VoiceCommandProcessor' could not be found`.
- [ ] Create `windows/src/VoxScribe.Core/VoiceCommandProcessor.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace VoxScribe.Core;

/// <summary>
/// Deterministic French + English spoken-punctuation pass, applied after
/// dictionary correction (DictationEngine, when SpokenPunctuation is enabled).
/// Same safety contract as DictionaryCorrector: NFC-normalized input,
/// letter/digit fences (stricter than \b), IgnoreCase + CultureInvariant,
/// ICU/.NET-safe pattern subset, 1-second match timeout.
/// Position-aware output: the mark glues to the previous word, gets one
/// trailing space mid-text and none at the end; newlines are clean on both sides.
/// </summary>
public static class VoiceCommandProcessor
{
    private static readonly (string Spoken, string Mark)[] Commands =
    [
        ("point d'interrogation", "?"),
        ("point d'exclamation", "!"),
        ("exclamation mark", "!"),
        ("question mark", "?"),
        ("point virgule", ";"),
        ("à la ligne", "\n"),
        ("a la ligne", "\n"),
        ("deux points", ":"),
        ("full stop", "."),
        ("semicolon", ";"),
        ("new line", "\n"),
        ("newline", "\n"),
        ("virgule", ","),
        ("period", "."),
        ("comma", ","),
        ("colon", ":"),
        ("point", "."),
    ];

    private static readonly Regex SeparatorRun =
        new(@"[\s\-]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Dictionary<string, string> Marks =
        Commands.ToDictionary(c => c.Spoken, c => c.Mark, StringComparer.Ordinal);

    private static readonly Regex Matcher = BuildMatcher();

    private static Regex BuildMatcher()
    {
        // Longest spoken form first so "point d'interrogation" beats "point".
        // Spaces/hyphens inside a command match any run of either, mirroring
        // DictionaryCorrector's glued/hyphenated tolerance.
        var alternatives = Commands
            .OrderByDescending(c => c.Spoken.Length)
            .Select(c => string.Join(@"[\s\-]+", c.Spoken.Split(' ').Select(Regex.Escape)));
        var pattern = @"[ \t]*(?<![\p{L}\p{N}])(?<cmd>" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}])[ \t]*";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    /// <summary>Replaces spoken punctuation commands with their marks. Whole text pass.</summary>
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var normalized = text.Normalize(NormalizationForm.FormC);
        var replaced = Matcher.Replace(normalized, match =>
        {
            var key = SeparatorRun.Replace(match.Groups["cmd"].Value, " ").ToLowerInvariant();
            var mark = Marks[key];
            if (mark == "\n")
            {
                return "\n";
            }

            var atEnd = match.Index + match.Length >= normalized.Length;
            return atEnd ? mark : mark + " ";
        });

        // "point à la ligne": the period's trailing space bumps into the newline
        // emitted by the very next command — collapse it.
        return replaced.Replace(" \n", "\n", StringComparison.Ordinal);
    }
}
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (15 new tests).
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/VoiceCommandProcessor.cs windows/tests/VoxScribe.Core.Tests/VoiceCommandProcessorTests.cs
git commit -m "core: spoken punctuation processor, French and English"
```

---

### Task 6: Engine + settings wiring for spoken punctuation

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs`
- Modify: `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`
- Modify: `windows/src/VoxScribe.Core/AppSettings.cs`
- Modify: `windows/src/VoxScribe.App/Composition.cs`
- Modify: `windows/src/VoxScribe.App/Views/SettingsWindow.cs`

**Interfaces:**
- Consumes: `VoiceCommandProcessor.Apply(string)` (Task 5)
- Produces: `public bool SpokenPunctuation { get; set; }` on `DictationEngine`; `public bool SpokenPunctuation { get; init; }` on `SettingsData` (default false — opt-in, existing users' output must not change)

**Steps:**

- [ ] Add failing tests to `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`:

```csharp
    [Fact]
    public async Task Spoken_punctuation_applies_when_enabled()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("hello virgule world"), injector);
        engine.SpokenPunctuation = true;
        string? text = null;
        engine.Completed += (_, r) => text = r.Text;

        await DictateAsync(hotkey, engine);

        text.ShouldBe("hello, world");
        injector.Injected.ShouldHaveSingleItem().ShouldBe("hello, world");
    }

    [Fact]
    public async Task Spoken_punctuation_is_off_by_default()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("hello virgule world"), injector);
        string? text = null;
        engine.Completed += (_, r) => text = r.Text;

        await DictateAsync(hotkey, engine);

        text.ShouldBe("hello virgule world");
    }

    [Fact]
    public async Task Newline_at_segment_end_suppresses_the_join_space()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var capture = FakeAudioCapture.Phrases(2);
        await using var engine = Build(capture, hotkey, new FakeTranscriber("first à la ligne", "second"), injector);
        engine.SpokenPunctuation = true;
        string? text = null;
        engine.Completed += (_, r) => text = r.Text;

        await DictateFullyAsync(hotkey, capture, engine);

        text.ShouldBe("first\nsecond");
    }
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect compile failure: no `SpokenPunctuation` on `DictationEngine`.
- [ ] Modify `windows/src/VoxScribe.Core/DictationEngine.cs`:
  1. Add the property near `IncrementalInjection` (~L182):

```csharp
    /// <summary>Apply the spoken-punctuation pass ("virgule" → ",", "new line" → newline) after dictionary correction.</summary>
    public bool SpokenPunctuation { get; set; }
```

  2. In `TranscribeSegmentAsync`, immediately after `_corrector!.Apply(trimmed)` (~L434) and before the `PartialText` append:

```csharp
                if (SpokenPunctuation)
                {
                    corrected = VoiceCommandProcessor.Apply(corrected);
                }
```

  (Adapt the local variable name to what the file uses for the corrected segment text; if it's captured in a tuple deconstruction, introduce `var text = result.Text;` style local as needed — the pass runs on the corrected string in both incremental and end modes because this is the single seam both share.)
  3. Segment-join separator: where `PartialText` is appended with a `' '` separator (~L436–437) and where the injected incremental string is built (~L440), compute the separator once so a segment ending in `\n` is not followed by a space:

```csharp
                var separator = PartialText.Length == 0 || PartialText.EndsWith('\n')
                    ? string.Empty
                    : " ";
```

  4. In `ProcessAsync` (~L379), replace the `string.Join(' ', ...)` over the non-empty segment texts with the same rule:

```csharp
        var joined = new StringBuilder();
        foreach (var segmentText in texts)
        {
            if (joined.Length > 0 && joined[^1] != '\n')
            {
                joined.Append(' ');
            }

            joined.Append(segmentText);
        }

        var text = joined.ToString();
```

  (Adapt `texts` to the actual local holding the surviving segment texts; keep everything downstream — cleanup, `DictationResult`, injection — untouched.)
- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green.
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/DictationEngine.cs windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs
git commit -m "core: engine applies spoken punctuation when enabled, newline-aware joins"
```

- [ ] Add the setting to `windows/src/VoxScribe.Core/AppSettings.cs`, in `SettingsData` next to `IncrementalInjection` (~L57):

```csharp
    /// <summary>Spoken punctuation commands ("virgule", "new line", …) become marks. Off by default.</summary>
    public bool SpokenPunctuation { get; init; }
```

- [ ] Wire it in `windows/src/VoxScribe.App/Composition.cs`:
  1. After `engine.IncrementalInjection = settings.Data.IncrementalInjection;` (~L132):

```csharp
            engine.SpokenPunctuation = settings.Data.SpokenPunctuation;
```

  2. Inside the `settings.Changed` handler (~L145–159), next to the existing `IncrementalInjection` re-set:

```csharp
                engine.SpokenPunctuation = settings.Data.SpokenPunctuation;
```

- [ ] Add the toggle to `windows/src/VoxScribe.App/Views/SettingsWindow.cs`. Read the file first: locate the static `Toggle` helper (~L565–618) and the section containing the `IncrementalInjection` toggle, then add a row directly under it using the file's exact helper signature and the immutable-update pattern the map documents (`Save(_settings.Data with { ... })`), e.g.:

```csharp
            Toggle(
                "Spoken punctuation",
                "\u201cvirgule\u201d, \u201cnew line\u201d and friends become punctuation",
                _settings.Data.SpokenPunctuation,
                value => Save(_settings.Data with { SpokenPunctuation = value })),
```

  (Match the real helper's parameter list — drop or reorder the description argument if the helper takes fewer parameters. No literal colours/sizes: the helper already speaks Tokens.)
- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (StorageTests' settings round-trip picks up the new field via source-gen automatically).
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/AppSettings.cs windows/src/VoxScribe.App/Composition.cs windows/src/VoxScribe.App/Views/SettingsWindow.cs
git commit -m "windows: spoken punctuation toggle in settings"
```

---

### Task 7: Raw text alongside cleaned transcripts

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs` (`DictationResult` record + `ProcessAsync`)
- Modify: `windows/src/VoxScribe.Core/TranscriptStore.cs` (`TranscriptRecord`)
- Modify: `windows/src/VoxScribe.App/Composition.cs` (Completed handler)
- Modify: `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`
- Modify: `windows/tests/VoxScribe.Core.Tests/StorageTests.cs`

**Interfaces:**
- Produces: `DictationResult` gains trailing optional positional `string? RawText = null` (the dictionary-corrected text BEFORE the LLM cleanup pass; null when no cleanup ran or cleanup changed nothing); `TranscriptRecord` gains `public string? RawText { get; init; }`

**Steps:**

- [ ] Add failing tests to `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`:

```csharp
    [Fact]
    public async Task Cleaned_dictation_carries_its_raw_text()
    {
        var hotkey = new FakeHotkeySource();
        var cleanupHotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("helo wrld"), injector,
            () => [], new FakeClock(), cleanupHotkey);
        engine.Cleanup = (_, _) => Task.FromResult("hello world");
        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        await DictateAsync(cleanupHotkey, engine);

        result.ShouldNotBeNull();
        result.Text.ShouldBe("hello world");
        result.RawText.ShouldBe("helo wrld");
    }

    [Fact]
    public async Task Raw_dictation_has_no_raw_text()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("hello"), injector);
        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        await DictateAsync(hotkey, engine);

        result.ShouldNotBeNull();
        result.RawText.ShouldBeNull();
    }

    [Fact]
    public async Task Cleanup_that_changes_nothing_stores_no_raw_text()
    {
        var hotkey = new FakeHotkeySource();
        var cleanupHotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(2.0), hotkey, new FakeTranscriber("already clean"), injector,
            () => [], new FakeClock(), cleanupHotkey);
        engine.Cleanup = (text, _) => Task.FromResult(text);
        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        await DictateAsync(cleanupHotkey, engine);

        result.ShouldNotBeNull();
        result.RawText.ShouldBeNull("identical raw text is noise for the suggestion miner");
    }
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect compile failure: `'DictationResult' does not contain a definition for 'RawText'`.
- [ ] Modify `windows/src/VoxScribe.Core/DictationEngine.cs`:
  1. Extend the record (~L20) with a trailing optional positional parameter:

```csharp
public record DictationResult(
    DateTimeOffset At,
    TimeSpan AudioDuration,
    TimeSpan ProcessingTime,
    string Text,
    IReadOnlyList<AppliedCorrection> Corrections,
    string? RawText = null);
```

  2. In `ProcessAsync` (~L381), capture the pre-cleanup text and pass the delta into the result:

```csharp
        string? rawText = null;
        if (_cleanThisUtterance && Cleanup is { } cleanup)
        {
            var beforeCleanup = text;
            text = await cleanup(text, CancellationToken.None).ConfigureAwait(false);
            if (!string.Equals(text, beforeCleanup, StringComparison.Ordinal))
            {
                rawText = beforeCleanup;
            }
        }
```

  and add `rawText` as the `RawText` argument where the `DictationResult` is constructed.
- [ ] Add `RawText` to `TranscriptRecord` in `windows/src/VoxScribe.Core/TranscriptStore.cs` (~L27, after `Corrections`):

```csharp
    /// <summary>Dictionary-corrected text before the LLM cleanup pass; null when no cleanup rewrote it.</summary>
    public string? RawText { get; init; }
```

  (No `TranscriptJsonContext` change needed — the property rides the existing `[JsonSerializable(typeof(TranscriptRecord))]`; old JSONL lines simply deserialize with `RawText = null`.)
- [ ] Add a round-trip test to `windows/tests/VoxScribe.Core.Tests/StorageTests.cs`, following the file's existing temp-path pattern:

```csharp
    [Fact]
    public void Raw_text_round_trips_through_the_store()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "transcripts.jsonl");
        var store = new TranscriptStore(path);
        store.Add(new TranscriptRecord { Text = "hello world", RawText = "helo wrld" });

        var reloaded = new TranscriptStore(path);
        reloaded.Reload();
        reloaded.Search(string.Empty).ShouldHaveSingleItem().RawText.ShouldBe("helo wrld");
    }
```

- [ ] Wire the field in `windows/src/VoxScribe.App/Composition.cs`, inside the `engine.Completed` handler's `TranscriptRecord` initializer (~L161–173):

```csharp
                    RawText = result.RawText,
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green.
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/DictationEngine.cs windows/src/VoxScribe.Core/TranscriptStore.cs windows/src/VoxScribe.App/Composition.cs windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs windows/tests/VoxScribe.Core.Tests/StorageTests.cs
git commit -m "core: keep the raw pre-cleanup text alongside cleaned transcripts"
```

---

### Task 8: SuggestionEngine (pure analysis)

**Files:**
- Create: `windows/src/VoxScribe.Core/SuggestionEngine.cs`
- Create: `windows/tests/VoxScribe.Core.Tests/SuggestionEngineTests.cs`

**Interfaces:**
- Consumes: `(string Raw, string Cleaned)` pairs (from `TranscriptRecord.RawText`/`Text`, Task 7)
- Produces: `public sealed record DictionarySuggestion(string Hear, string Write, int Count)`; `public static class SuggestionEngine` with `public const int Threshold = 3;` and `public static IReadOnlyList<DictionarySuggestion> Analyze(IEnumerable<(string Raw, string Cleaned)> pairs)`

**Steps:**

- [ ] Write the failing tests in `windows/tests/VoxScribe.Core.Tests/SuggestionEngineTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The suggestion engine mines raw-vs-cleaned transcript pairs for recurring
/// single-word substitutions the LLM keeps making — candidates for permanent
/// dictionary entries so the fix stops costing a cleanup round-trip.
/// </summary>
public sealed class SuggestionEngineTests
{
    [Fact]
    public void Recurring_substitution_surfaces_at_three_occurrences()
    {
        var pair = ("deploy with kubernets today", "deploy with kubernetes today");
        SuggestionEngine.Analyze([pair, pair]).ShouldBeEmpty();

        var suggestion = SuggestionEngine.Analyze([pair, pair, pair]).ShouldHaveSingleItem();
        suggestion.Hear.ShouldBe("kubernets");
        suggestion.Write.ShouldBe("kubernetes");
        suggestion.Count.ShouldBe(3);
    }

    [Fact]
    public void Casing_only_differences_are_ignored()
    {
        var pair = ("i met claude yesterday", "I met Claude yesterday");
        SuggestionEngine.Analyze([pair, pair, pair]).ShouldBeEmpty();
    }

    [Fact]
    public void Word_count_divergence_skips_the_pair_without_false_suggestions()
    {
        var pair = ("um so the thing works", "the thing works");
        SuggestionEngine.Analyze([pair, pair, pair]).ShouldBeEmpty();
    }

    [Fact]
    public void Trailing_punctuation_does_not_block_matching()
    {
        var pair = ("send it to jhon.", "send it to John.");
        var suggestion = SuggestionEngine.Analyze([pair, pair, pair]).ShouldHaveSingleItem();
        suggestion.Hear.ShouldBe("jhon");
        suggestion.Write.ShouldBe("John");
    }

    [Fact]
    public void Suggestions_are_ordered_by_count_descending()
    {
        var frequent = ("use kubernets now", "use kubernetes now");
        var rare = ("ping grafna please", "ping grafana please");
        var result = SuggestionEngine.Analyze([frequent, frequent, frequent, frequent, rare, rare, rare]);
        result.Count.ShouldBe(2);
        result[0].Hear.ShouldBe("kubernets");
        result[1].Hear.ShouldBe("grafna");
    }

    [Fact]
    public void No_pairs_means_no_suggestions()
    {
        SuggestionEngine.Analyze([]).ShouldBeEmpty();
    }
}
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect compile failure: `CS0246 'SuggestionEngine' could not be found`.
- [ ] Create `windows/src/VoxScribe.Core/SuggestionEngine.cs`:

```csharp
namespace VoxScribe.Core;

/// <summary>A recurring raw→cleaned word substitution worth adding to the dictionary.</summary>
public sealed record DictionarySuggestion(string Hear, string Write, int Count);

/// <summary>
/// Mines raw-vs-cleaned transcript pairs for word-level substitutions that
/// recur at least <see cref="Threshold"/> times. Pure, no I/O.
/// </summary>
public static class SuggestionEngine
{
    public const int Threshold = 3;

    private static readonly char[] WordSeparators = [' ', '\t', '\n', '\r'];
    private static readonly char[] EdgePunctuation = ['.', ',', ';', ':', '!', '?', '(', ')', '"', '\u00ab', '\u00bb'];

    public static IReadOnlyList<DictionarySuggestion> Analyze(IEnumerable<(string Raw, string Cleaned)> pairs)
    {
        var counts = new Dictionary<(string Hear, string Write), int>();
        foreach (var (raw, cleaned) in pairs)
        {
            foreach (var substitution in Substitutions(raw, cleaned))
            {
                counts[substitution] = counts.GetValueOrDefault(substitution) + 1;
            }
        }

        return counts
            .Where(pair => pair.Value >= Threshold)
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new DictionarySuggestion(pair.Key.Hear, pair.Key.Write, pair.Value))
            .ToList();
    }

    // ponytail: two-pointer resync finds single-word substitutions only; the first
    // insertion/deletion abandons the pair. Upgrade to a word-level LCS diff when
    // multi-word substitutions ("cloud code" → "Claude Code") start mattering here.
    private static IEnumerable<(string Hear, string Write)> Substitutions(string raw, string cleaned)
    {
        var rawWords = Tokenize(raw);
        var cleanedWords = Tokenize(cleaned);
        var i = 0;
        var j = 0;
        while (i < rawWords.Length && j < cleanedWords.Length)
        {
            if (Same(rawWords[i], cleanedWords[j]))
            {
                i++;
                j++;
                continue;
            }

            var lastPair = i + 1 == rawWords.Length && j + 1 == cleanedWords.Length;
            var resyncs = i + 1 < rawWords.Length && j + 1 < cleanedWords.Length
                && Same(rawWords[i + 1], cleanedWords[j + 1]);
            if (lastPair || resyncs)
            {
                yield return (rawWords[i].ToLowerInvariant(), cleanedWords[j]);
                i++;
                j++;
                continue;
            }

            yield break; // structure diverged — rest of this pair is unreliable
        }
    }

    private static string[] Tokenize(string text) =>
        text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(EdgePunctuation))
            .Where(word => word.Length > 0)
            .ToArray();

    // Case-insensitive: cleanup capitalises sentence starts constantly, and the
    // dictionary corrector matches IgnoreCase anyway — casing-only diffs are noise.
    private static bool Same(string x, string y) =>
        string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (6 new tests).
- [ ] Commit:

```
git add windows/src/VoxScribe.Core/SuggestionEngine.cs windows/tests/VoxScribe.Core.Tests/SuggestionEngineTests.cs
git commit -m "core: suggestion engine mines recurring cleanup substitutions"
```

---

### Task 9: Suggestions in DictionaryView

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/DictionaryView.cs`
- Modify: `windows/src/VoxScribe.App/Views/MainWindow.cs` (construction call site, ~L302–327 `ShowSection`)
- Create: `windows/tests/VoxScribe.App.Tests/SuggestionUiTests.cs`

**Interfaces:**
- Consumes: `SuggestionEngine.Analyze` (Task 8), `TranscriptStore.Search(string)` / `.Changed`, `DictionaryFile.Entries` / `.Add(DictionaryEntry)`, `DictionaryEntry.Correction(hear, write)`, `Panels.DeckButton` / `Panels.DeckCard`, `Silkscreen`, `Tokens.*`
- Produces: `DictionaryView` ctor becomes `public DictionaryView(DictionaryFile dictionary, TranscriptStore transcripts)`

**Steps:**

- [ ] Read `windows/src/VoxScribe.App/Views/DictionaryView.cs` end to end to learn its layout (it follows the TranscriptionsView pattern: DockPanel with `Panels.SearchRow` top, footer bottom, ScrollViewer list filling).
- [ ] Write the failing headless UI test in `windows/tests/VoxScribe.App.Tests/SuggestionUiTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Controls;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

/// <summary>
/// The dictionary view surfaces recurring cleanup substitutions as suggestions
/// with accept/dismiss, so good corrections graduate into permanent entries.
/// </summary>
public sealed class SuggestionUiTests
{
    private static (DictionaryFile Dictionary, TranscriptStore Transcripts) Stores()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        return (new DictionaryFile(Path.Combine(dir, "dictionary.txt")),
                new TranscriptStore(Path.Combine(dir, "transcripts.jsonl")));
    }

    [AvaloniaFact]
    public void Recurring_substitution_shows_a_suggestion_row()
    {
        var (dictionary, transcripts) = Stores();
        for (var i = 0; i < 3; i++)
        {
            transcripts.Add(new TranscriptRecord { Text = "deploy kubernetes now", RawText = "deploy kubernets now" });
        }

        var window = new Window { Content = new DictionaryView(dictionary, transcripts) };
        window.Show();

        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text?.Contains("kubernets", StringComparison.Ordinal) == true)
            .ShouldBeTrue("the recurring substitution must be visible as a suggestion");
    }

    [AvaloniaFact]
    public void Suggestions_covered_by_existing_entries_are_hidden()
    {
        var (dictionary, transcripts) = Stores();
        dictionary.Add(VoxScribe.Dictionary.DictionaryEntry.Correction("kubernets", "kubernetes"));
        for (var i = 0; i < 3; i++)
        {
            transcripts.Add(new TranscriptRecord { Text = "deploy kubernetes now", RawText = "deploy kubernets now" });
        }

        var window = new Window { Content = new DictionaryView(dictionary, transcripts) };
        window.Show();

        window.GetVisualDescendants().OfType<Silkscreen>()
            .Any(label => label.Text == "SUGGESTIONS")
            .ShouldBeFalse("an already-covered substitution is not a suggestion");
    }
}
```

  (Adjust the `using VoxScribe.App.Controls;` / `Silkscreen` namespace to where `Equipment.cs` actually declares it, and the `DictionaryFile`/`DictionaryEntry` namespaces to match existing usings in `UiTests.cs`.)
- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect compile failure: `DictionaryView` does not take a `TranscriptStore`.
- [ ] Modify `windows/src/VoxScribe.App/Views/DictionaryView.cs`:
  1. Change the ctor to `public DictionaryView(DictionaryFile dictionary, TranscriptStore transcripts)`, store both, and add fields:

```csharp
    private readonly TranscriptStore _transcripts;
    private readonly HashSet<(string Hear, string Write)> _dismissed = new(); // ponytail: in-memory; persist to a file if reappearing-after-restart annoys
    private readonly ContentControl _suggestionHost = new();
```

  2. In the ctor, after the existing layout build, dock `_suggestionHost` at the top of the entry list area (directly under the search row, above the scrolling list), subscribe, and fill it:

```csharp
        _transcripts.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshSuggestions);
        RefreshSuggestions();
```

  3. Add the refresh + builder (all styling through `Tokens` and `Panels` — zero literals):

```csharp
    private void RefreshSuggestions() => _suggestionHost.Content = BuildSuggestions();

    private Control? BuildSuggestions()
    {
        var pairs = _transcripts.Search(string.Empty)
            .Where(record => record.RawText is not null)
            .Select(record => (record.RawText!, record.Text));
        var suggestions = SuggestionEngine.Analyze(pairs)
            .Where(s => !_dismissed.Contains((s.Hear, s.Write)))
            .Where(s => !_dictionary.Entries.Any(entry =>
                string.Equals(entry.Hear, s.Hear, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (suggestions.Count == 0)
        {
            return null;
        }

        var list = new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Margin = new Thickness(Tokens.Space.Base, Tokens.Space.Base, Tokens.Space.Base, 0),
        };
        list.Children.Add(new Silkscreen { Text = "SUGGESTIONS" });
        foreach (var suggestion in suggestions)
        {
            list.Children.Add(BuildSuggestionRow(suggestion));
        }

        return list;
    }

    private Control BuildSuggestionRow(DictionarySuggestion suggestion)
    {
        var accept = Panels.DeckButton("ADD");
        accept.Click += (_, _) =>
        {
            _dictionary.Add(VoxScribe.Dictionary.DictionaryEntry.Correction(suggestion.Hear, suggestion.Write));
            RefreshSuggestions();
        };

        var dismiss = Panels.DeckButton("DISMISS");
        dismiss.Click += (_, _) =>
        {
            _dismissed.Add((suggestion.Hear, suggestion.Write));
            RefreshSuggestions();
        };

        var row = new DockPanel();
        DockPanel.SetDock(dismiss, Dock.Right);
        DockPanel.SetDock(accept, Dock.Right);
        row.Children.Add(dismiss);
        row.Children.Add(accept);
        row.Children.Add(new TextBlock
        {
            Text = $"{suggestion.Hear} \u2192 {suggestion.Write}  \u00d7{suggestion.Count}",
            FontFamily = Tokens.Fonts.Mono,
            FontSize = Tokens.Fonts.Body,
            Foreground = Tokens.Brushes.Ink,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Tokens.Space.Base, 0),
        });
        return Panels.DeckCard(row);
    }
```

  (Amber/accent stay untouched: buttons are neutral DeckButtons, so the "red means recording" and amber-instrumentation pins hold. Adapt member name `_dictionary` and existing entry-refresh calls — accepting a suggestion also fires `DictionaryFile.Changed`, which the view's existing entry list already listens to.)
  4. Refresh existing usings (`VoxScribe.Core` for `SuggestionEngine`/`TranscriptStore`, `VoxScribe.App.Controls` for `Silkscreen` if not present).
- [ ] Update the call site in `windows/src/VoxScribe.App/Views/MainWindow.cs` `ShowSection` (~L310):

```csharp
        _dictionaryView ??= new DictionaryView(_composition.Dictionary, _composition.Transcripts);
```

- [ ] Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (2 new UI tests pass headless).
- [ ] Commit:

```
git add windows/src/VoxScribe.App/Views/DictionaryView.cs windows/src/VoxScribe.App/Views/MainWindow.cs windows/tests/VoxScribe.App.Tests/SuggestionUiTests.cs
git commit -m "windows: dictionary suggestions from cleanup rewrites, add or dismiss"
```

---

## Out of scope (deliberate, Wave 1)

- Dedicated undo hotkey / tray item — UNDO key in the voice band covers it; add an `IHotkeySource` + settings chord when asked.
- Persisting dismissed suggestions across restarts — in-memory set; add a small JSON file when it annoys.
- Multi-word suggestion mining — two-pointer resync only; upgrade to word LCS when needed.
- Diff-based correction — Journal's `Retract`/`InjectedText` shape is the prepared seam; nothing more built now.
