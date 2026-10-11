# Edit a selection by voice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Select text, hold the Edit chord, say an instruction, release — the selection is replaced by the model's rewrite; Undo restores the original.

**Architecture:** One new platform primitive (`ITextInjector.CopySelectionAsync`, logic-free). Everything else is platform-neutral Core (`ChatCompletion` extracted from `TextCleaner`, `TextEditor`, an edit branch in `DictationEngine`, an `InjectionJournal` that remembers what an edit replaced), tested with the fakes. The App wires a chord, two settings and the pill's `EDIT` mode.

**Tech Stack:** C# / .NET 10, Avalonia 11.3.20, xUnit 2.9.3 + Shouldly, Avalonia.Headless.XUnit.

**Spec:** `docs/superpowers/specs/2026-10-11-edit-selection-by-voice-design.md`

## Global Constraints

- Work from `windows/`. Test command: `dotnet test VoxScribe.CrossPlatform.slnf` (`--filter "FullyQualifiedName~<Class>"` to narrow).
- Gate before every commit that touches `VoxScribe.Platform.Windows`, and at the end: `dotnet format VoxScribe.sln --verify-no-changes` and `dotnet build VoxScribe.sln -c Release --no-incremental -warnaserror`. Analyzers are strict (e.g. CA1859, CA1031 need the same handling the surrounding code uses).
- `VoxScribe.Platform.Windows` stays logic-free; `VoxScribe.Core` stays free of Windows APIs (CA1416 is an error).
- Views contain no literal colours/sizes — `Tokens.*` only. UI strings are English.
- Notice texts, verbatim: `Nothing selected`, `Selection too long`, `Set a cleanup endpoint to edit by voice`, `Edit failed — <reason>`.
- Max selection: `8000` characters.
- Match surrounding comment density and XML-doc style (`/// <summary>` on public members).
- One commit per task, conventional message, no attribution lines; `git push` after each.
- Known pre-existing flaky tests under full-suite load (ClaudeBridgeTests file-in-use; DictationEngineTests yield-count waits): rerun once, mention it.

---

### Task 1: Platform primitive — copy the selection

**Files:**
- Modify: `windows/src/VoxScribe.Abstractions/Interfaces.cs` (`ITextInjector`)
- Modify: `windows/src/VoxScribe.Platform.Windows/SendInputTextInjector.cs`
- Modify: `windows/src/VoxScribe.Testing/Fakes.cs` (`RecordingTextInjector`)

**Interfaces:**
- Produces: `ValueTask<string?> ITextInjector.CopySelectionAsync(CancellationToken cancellationToken)`; `RecordingTextInjector.Selection { get; set; }` (string?), `RecordingTextInjector.Copies { get; }` (int).

No unit test can exercise the Win32 path (it is checked by hand, see the end). The gate here is the full Release `-warnaserror` build plus the existing suite.

- [ ] **Step 1: Interface** — add to `ITextInjector`:

```csharp
    /// <summary>
    /// Copies the focused control's selection as text, leaving the user's clipboard as it was.
    /// </summary>
    /// <returns>The selected text, or null when nothing was copied (no selection, or no text in it).</returns>
    ValueTask<string?> CopySelectionAsync(CancellationToken cancellationToken);
```

- [ ] **Step 2: Fake** — in `RecordingTextInjector`:

```csharp
    /// <summary>What <see cref="CopySelectionAsync"/> returns: the "selected" text, or null for none.</summary>
    public string? Selection { get; set; }

    /// <summary>Number of <see cref="CopySelectionAsync"/> calls.</summary>
    public int Copies { get; private set; }

    /// <inheritdoc />
    public ValueTask<string?> CopySelectionAsync(CancellationToken cancellationToken)
    {
        Copies++;
        return ValueTask.FromResult(Selection);
    }
```

- [ ] **Step 3: Windows implementation** in `SendInputTextInjector`:
  - Add the import next to the other clipboard imports (same attribute style they use):

```csharp
    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
```

    (If the file uses `[DllImport]` + `extern` rather than `LibraryImport`, follow the file.)
  - Generalise `PressCtrlV` into a private `PressCtrl(int virtualKey)` with the same held-modifier lifting, and keep `public static bool PressCtrlV() => PressCtrl(VK_V);`. Add `private const int VK_C = 0x43;` beside `VK_V`.
  - Add a named settle constant beside `PasteSettle`:

```csharp
    /// <summary>How long the target gets to answer Ctrl+C before the clipboard is read.</summary>
    private static readonly TimeSpan CopySettle = TimeSpan.FromMilliseconds(150);
```

  - The method:

```csharp
    /// <inheritdoc />
    /// <remarks>
    /// Ctrl+C, then the clipboard sequence number says whether anything was copied — an app
    /// with no selection copies nothing and the number does not move. The user's text is put
    /// back afterwards, as <see cref="PasteAsync"/> does.
    /// ponytail: only text is saved and restored, as for paste.
    /// </remarks>
    public async ValueTask<string?> CopySelectionAsync(CancellationToken cancellationToken)
    {
        var previous = ReadClipboardText();
        var before = GetClipboardSequenceNumber();

        if (!PressCtrl(VK_C)) return null;
        await Task.Delay(CopySettle, cancellationToken).ConfigureAwait(false);

        if (GetClipboardSequenceNumber() == before) return null;

        var copied = ReadClipboardText();
        if (previous is not null) WriteClipboardText(previous);
        return string.IsNullOrEmpty(copied) ? null : copied;
    }
```

- [ ] **Step 4: Gate** — full suite, `dotnet format ... --verify-no-changes`, Release `-warnaserror` build of `VoxScribe.sln`. All green.
- [ ] **Step 5: Commit** — `feat: injector can copy the focused selection`

---

### Task 2: Extract the chat call from TextCleaner

**Files:**
- Create: `windows/src/VoxScribe.Core/ChatCompletion.cs`
- Modify: `windows/src/VoxScribe.Core/TextCleaner.cs`
- Test: existing `windows/tests/VoxScribe.Core.Tests/TextCleanerTests.cs` — **must pass unmodified**

**Interfaces:**
- Produces: `internal static class ChatCompletion` with
  `Task<ChatReply> SendAsync(Uri endpoint, string model, string? apiKey, string system, string user, int maxTokens, TimeSpan timeout, CancellationToken cancellationToken)` and
  `internal readonly record struct ChatReply(string? Content, string? Failure);` — `Failure` is a short reason **without** a prefix (`gateway answered 503`, `gateway did not answer in time`, or the exception message); `Content` is `choices[0].message.content` or null when the body has the wrong shape; exactly one of "Failure set" / "HTTP success" holds. `TextCleaner.Content(string body)` stays public (tests use it) and `ChatCompletion` calls it.

Pure refactor: `TextCleaner`'s outward behaviour, including its `onFailure` messages (`Cleanup skipped — gateway answered {code}`, `Cleanup skipped — gateway did not answer in time`, `Cleanup skipped — {message}`), stays byte-identical.

- [ ] **Step 1:** Move the static `HttpClient Http`, the JSON body construction (model, temperature 0, max_tokens, system+user messages), the linked timeout, the request/auth header, the status check and the catch into `ChatCompletion.SendAsync`. Keep the existing comments (trimming/IL2026 note, "unreachable gateway, sleeping Mac…") with the code they describe.

```csharp
namespace VoxScribe.Core;

/// <summary>What one chat call produced: the reply's content, or why there is none.</summary>
/// <param name="Content"><c>choices[0].message.content</c>; null when the body was not that shape.</param>
/// <param name="Failure">Why the call failed, without a prefix; null when the gateway answered 2xx.</param>
internal readonly record struct ChatReply(string? Content, string? Failure);

/// <summary>One OpenAI-compatible <c>/chat/completions</c> call, shared by cleanup and edit.</summary>
internal static class ChatCompletion
{
    // (the HttpClient and its comment move here unchanged)

    /// <summary>Sends <paramref name="system"/> + <paramref name="user"/>; never throws.</summary>
    public static async Task<ChatReply> SendAsync(
        Uri endpoint, string model, string? apiKey, string system, string user,
        int maxTokens, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // body built as JsonObject exactly as TextCleaner did, with ["max_tokens"] = maxTokens
        // linked CTS + CancelAfter(timeout)
        // try: send; non-success → new ChatReply(null, $"gateway answered {(int)response.StatusCode}");
        //      success → new ChatReply(TextCleaner.Content(answer), null)
        // catch (Exception e): new ChatReply(null, e is TaskCanceledException
        //      ? "gateway did not answer in time" : e.Message)
    }
}
```

  (The comments above mark where the moved code goes; write the real code — it is `TextCleaner.TryCleanAsync`'s current body.)
- [ ] **Step 2:** `TextCleaner.TryCleanAsync` becomes:

```csharp
    public async Task<CleanupOutcome> TryCleanAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return new CleanupOutcome(text, Succeeded: true);

        var reply = await ChatCompletion.SendAsync(
            _endpoint, _model, _apiKey, SystemPrompt, text,
            // Enough for the repaired text and nothing more. A model that starts explaining
            // itself gets cut off, and Vet() then throws the fragment away.
            maxTokens: 64 + (text.Length / 2), TimeoutFor(text), cancellationToken).ConfigureAwait(false);

        if (reply.Failure is { } failure)
        {
            _onFailure?.Invoke($"Cleanup skipped — {failure}");
            return new CleanupOutcome(text, Succeeded: false);
        }

        return Vet(text, reply.Content) is { } repaired
            ? new CleanupOutcome(repaired, Succeeded: true)
            : new CleanupOutcome(text, Succeeded: false);
    }
```

  Remove now-unused usings/fields from `TextCleaner`.
- [ ] **Step 3:** Run `--filter "FullyQualifiedName~TextCleanerTests"` — all pass with no test edits; then full suite.
- [ ] **Step 4: Commit** — `refactor: share the chat call between cleanup and future edit`

---

### Task 3: TextEditor

**Files:**
- Create: `windows/src/VoxScribe.Core/TextEditor.cs`
- Test: create `windows/tests/VoxScribe.Core.Tests/TextEditorTests.cs`

**Interfaces:**
- Consumes: `ChatCompletion.SendAsync`, `ChatReply` (Task 2), `TextCleaner.TimeoutFor`.
- Produces: `public sealed record EditOutcome(string? Text, string? Failure);`, `public sealed class TextEditor(string baseUrl, string model, string? apiKey)` with `Task<EditOutcome> EditAsync(string selection, string instruction, CancellationToken)`, `public static string? Vet(string? candidate)`, `public static string Request(string selection, string instruction)`.

- [ ] **Step 1: Failing tests:**

```csharp
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>The edit pass: an instruction applied to a selection, with only a mechanical guard.</summary>
public sealed class TextEditorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("<think>hmm</think>Hello")]
    public void An_unusable_answer_vets_as_null(string? answer) => TextEditor.Vet(answer).ShouldBeNull();

    [Fact]
    public void A_fence_around_the_answer_is_stripped() =>
        TextEditor.Vet("```\nHello world\n```").ShouldBe("Hello world");

    /// <summary>No overlap guard: a translation shares no words with what was selected.</summary>
    [Fact]
    public void A_translation_is_kept() =>
        TextEditor.Vet("The build passes").ShouldBe("The build passes");

    [Fact]
    public void The_request_carries_instruction_and_text()
    {
        var request = TextEditor.Request("le build passe", "traduis en anglais");

        request.ShouldContain("traduis en anglais");
        request.ShouldContain("<<<\nle build passe\n>>>");
    }

    [Fact]
    public async Task An_unreachable_gateway_fails_with_a_reason()
    {
        var editor = new TextEditor("http://127.0.0.1:9/v1", "local-light", null);

        var outcome = await editor.EditAsync("le build passe", "traduis en anglais", CancellationToken.None);

        outcome.Text.ShouldBeNull();
        outcome.Failure.ShouldNotBeNullOrWhiteSpace();
    }
}
```

- [ ] **Step 2: Run, expect compile failure.**
- [ ] **Step 3: Implement** `TextEditor.cs`:

```csharp
namespace VoxScribe.Core;

/// <summary>What an edit produced: the rewrite, or why there is none.</summary>
/// <param name="Text">The text to put in place of the selection; null on failure.</param>
/// <param name="Failure">A short reason, for the pill's notice; null on success.</param>
public sealed record EditOutcome(string? Text, string? Failure);

/// <summary>
/// Applies a spoken instruction to selected text through the cleanup gateway.
/// </summary>
/// <remarks>
/// Unlike <see cref="TextCleaner"/> there is no word-overlap guard — "translate to English"
/// legitimately replaces every word — and no fallback text: on any failure nothing is typed
/// and the selection stays as it was.
/// </remarks>
public sealed class TextEditor(string baseUrl, string model, string? apiKey)
{
    private const string SystemPrompt =
        "You edit text. The user gives an instruction and a text between <<< and >>>. Apply the "
        + "instruction to the text and return only the resulting text: no markers, no quotes, no "
        + "explanation. Keep the text's language unless the instruction asks for another one, and "
        + "keep its line breaks unless the instruction changes them.";

    private readonly Uri _endpoint = new(baseUrl.TrimEnd('/') + "/chat/completions");

    /// <summary>The user message: the instruction, then the text between markers.</summary>
    public static string Request(string selection, string instruction) =>
        $"Instruction: {instruction}\n\nText:\n<<<\n{selection}\n>>>";

    /// <summary>Rewrites <paramref name="selection"/> as <paramref name="instruction"/> says; never throws.</summary>
    public async Task<EditOutcome> EditAsync(string selection, string instruction, CancellationToken cancellationToken)
    {
        var reply = await ChatCompletion.SendAsync(
            _endpoint, model, apiKey, SystemPrompt, Request(selection, instruction),
            // A translation can come out longer than it went in; a token is several characters.
            maxTokens: 256 + selection.Length,
            TextCleaner.TimeoutFor(selection), cancellationToken).ConfigureAwait(false);

        if (reply.Failure is { } failure) return new EditOutcome(null, failure);
        return Vet(reply.Content) is { } edited
            ? new EditOutcome(edited, null)
            : new EditOutcome(null, "the model's answer was unusable");
    }

    /// <summary>
    /// The mechanical guard: null for an empty answer or a leaked reasoning block; strips one
    /// surrounding code fence and echoed markers.
    /// </summary>
    public static string? Vet(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        if (candidate.Contains("<think", StringComparison.OrdinalIgnoreCase)) return null;

        var text = candidate.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal) && text.EndsWith("```", StringComparison.Ordinal) && text.Length > 6)
        {
            var firstBreak = text.IndexOf('\n', StringComparison.Ordinal);
            text = firstBreak < 0 ? string.Empty : text[(firstBreak + 1)..^3].Trim();
        }

        if (text.StartsWith("<<<", StringComparison.Ordinal) && text.EndsWith(">>>", StringComparison.Ordinal))
            text = text[3..^3].Trim();

        return text.Length == 0 ? null : text;
    }
}
```

- [ ] **Step 4: Run `--filter "FullyQualifiedName~TextEditorTests"`, then full suite.**
- [ ] **Step 5: Commit** — `feat: text editor applies a spoken instruction to a selection`

---

### Task 4: The journal remembers what an edit replaced; Undo restores it

**Files:**
- Modify: `windows/src/VoxScribe.Core/InjectionJournal.cs`
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs` (`UndoLastDictationAsync`)
- Test: `windows/tests/VoxScribe.Core.Tests/InjectionJournalTests.cs` (append) and `DictationEngineTests.cs` (append)

**Interfaces:**
- Produces: `InjectionJournal.Record(string injected, string? replaced = null)`, `string? InjectionJournal.Replaced { get; }`.

- [ ] **Step 1: Failing tests.** Journal:

```csharp
    [Fact]
    public void An_edit_remembers_what_it_replaced_until_the_next_dictation()
    {
        var journal = new InjectionJournal();
        journal.BeginDictation();
        journal.Record("Hello", replaced: "Bonjour");

        journal.Replaced.ShouldBe("Bonjour");

        journal.BeginDictation();
        journal.Replaced.ShouldBeNull();
    }
```

Engine (uses the file's `Build` helper; no audio needed — drive the journal directly):

```csharp
    [Fact]
    public async Task Undo_after_an_edit_puts_the_original_back()
    {
        var injector = new RecordingTextInjector();
        await using var engine = Build(FakeAudioCapture.Tone(0.1), new FakeHotkeySource(), new FakeTranscriber(), injector);
        engine.Journal.BeginDictation();
        engine.Journal.Record("Hello", replaced: "Bonjour");

        (await engine.UndoLastDictationAsync(CancellationToken.None)).ShouldBeTrue();

        injector.Backspaces.ShouldBe(5);
        injector.Injected.ShouldBe(["Bonjour"]);
        (await engine.UndoLastDictationAsync(CancellationToken.None)).ShouldBeFalse("one undo deep");
    }
```

- [ ] **Step 2: Run, expect compile failure.**
- [ ] **Step 3: Implement.** Journal: a `private string? _replaced;` cleared in `BeginDictation`; `Record` gains `string? replaced = null` and sets `_replaced` when non-null (inside the lock); `Replaced` reads it under the lock. Update the class summary to mention it.
  Engine `UndoLastDictationAsync`:

```csharp
        var text = Journal.InjectedText;
        if (text.Length == 0) return false;
        var replaced = Journal.Replaced;

        if (!await BackspaceAsync(text, cancellationToken).ConfigureAwait(false)) return false;
        if (replaced is null) return true;

        // An edit replaced a selection: put it back, then forget, so a second undo does nothing.
        // ponytail: one backspace per grapheme — undoing an 8 000-character edit takes about a
        // second; selecting the result would need a platform selection API.
        var restored = await _injector.InjectAsync(replaced, cancellationToken).ConfigureAwait(false);
        Journal.BeginDictation();
        return restored;
```

  Update the method's `<summary>` to say an edit's original is re-typed.
- [ ] **Step 4: Run both test classes, then full suite.**
- [ ] **Step 5: Commit** — `feat: undo after an edit restores the original text`

---

### Task 5: The engine's edit path

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs`
- Test: create `windows/tests/VoxScribe.Core.Tests/EditModeTests.cs`

**Interfaces:**
- Consumes: `ITextInjector.CopySelectionAsync`, `RecordingTextInjector.Selection/Copies` (Task 1), `EditOutcome` (Task 3), `Journal.Record(.., replaced)` (Task 4).
- Produces: constructor parameter `IHotkeySource? editHotkey = null` (appended last); `Func<string, string, CancellationToken, Task<EditOutcome>>? Edit { get; set; }` (selection, instruction); `bool EditThisUtterance { get; }`; `public const int MaxEditSelection = 8000;`.

- [ ] **Step 1: Failing tests** — `EditModeTests.cs`:

```csharp
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
```

  Check `Notice` is the engine's public notice property name (it is set by `ReportNotice`); adjust if it is named differently.
- [ ] **Step 2: Run, expect compile failure.**
- [ ] **Step 3: Implement** in `DictationEngine.cs`:
  - Field `private readonly IHotkeySource? _editHotkey;`; constructor parameter `IHotkeySource? editHotkey = null` (last); wire like `commandHotkey` (`Pressed += OnEditPressed`, `Released += OnReleased`); unsubscribe in `DisposeAsync` like the others.
  - Property and constant:

```csharp
    /// <summary>
    /// Rewrites a selection: (selection, instruction) → the edit. Null when no endpoint is
    /// configured, which makes the Edit chord explain itself instead of recording.
    /// </summary>
    public Func<string, string, CancellationToken, Task<EditOutcome>>? Edit { get; set; }

    /// <summary>True while the current or last utterance came from the Edit chord.</summary>
    public bool EditThisUtterance { get; private set; }

    /// <summary>Longest selection an edit will send; past it the edit is refused.</summary>
    public const int MaxEditSelection = 8000;
```

  - `Pressed` becomes `Pressed(bool cleanup, bool command, bool edit, object? sender, EventArgs e)`; the three existing callers pass `edit: false`. Inside the idle branch set `EditThisUtterance = edit;`. In `BeginCommand` set `EditThisUtterance = false;`.
  - Handler:

```csharp
    // An edit is never cleaned (the instruction is not the text) and anchors like a dictation:
    // the selection lives in the window that had focus at press.
    private void OnEditPressed(object? sender, EventArgs e)
    {
        if (State == DictationState.Idle && Edit is null)
        {
            ReportNotice("Set a cleanup endpoint to edit by voice");
            return;
        }

        Pressed(cleanup: false, command: false, edit: true, sender, e);
    }
```

  - `InjectIncrementally` → `IncrementalInjection && !_cleanThisUtterance && !EditThisUtterance && !_anchoredThisUtterance`.
  - Extract the anchored-focus restore at the end of `ProcessAsync` into `private async Task RestoreAnchorAsync()` (same two lines and comment) and call it there.
  - In `ProcessAsync`, right after `var text = joined.ToString();`:

```csharp
        if (EditThisUtterance)
        {
            await EditSelectionAsync(text, releasedAt, spoken).ConfigureAwait(false);
            return;
        }
```

  - The branch:

```csharp
    /// <summary>
    /// Copies the selection, applies <paramref name="instruction"/> to it and types the result
    /// over it. Every refusal is a notice and types nothing: there is no "raw" fallback for an
    /// edit, and the selection must be left as it was.
    /// </summary>
    private async Task EditSelectionAsync(string instruction, DateTimeOffset releasedAt, Segment[] spoken)
    {
        await RestoreAnchorAsync().ConfigureAwait(false);

        var selection = await _injector.CopySelectionAsync(CancellationToken.None).ConfigureAwait(false);
        if (string.IsNullOrEmpty(selection))
        {
            ReportNotice("Nothing selected");
            return;
        }

        if (selection.Length > MaxEditSelection)
        {
            ReportNotice("Selection too long");
            return;
        }

        var startedAt = _clock.Now;
        var outcome = await Edit!(selection, instruction, CancellationToken.None).ConfigureAwait(false);
        var took = _clock.Now - startedAt;

        if (outcome.Text is not { } edited)
        {
            ReportNotice($"Edit failed — {outcome.Failure ?? "no answer"}");
            return;
        }

        Completed?.Invoke(this, new DictationResult(
            At: releasedAt,
            AudioDuration: TimeSpan.FromSeconds((double)_capturedSamples / AudioChunk.SampleRate),
            ProcessingTime: _clock.Now - releasedAt,
            Text: edited,
            Corrections: [.. spoken.SelectMany(s => s.Corrections)],
            RawText: instruction,
            CleanupTime: took,
            CleanupFailed: false));

        if (await _injector.InjectAsync(edited, CancellationToken.None).ConfigureAwait(false))
            Journal.Record(edited, replaced: selection);
    }
```

  `Edit!` is safe: the press path refuses to start without it, and a settings change that clears it mid-utterance is the only gap — guard with `if (Edit is not { } edit) { ReportNotice("Set a cleanup endpoint to edit by voice"); return; }` instead of `!` if the analyzers complain, and prefer that form anyway.
- [ ] **Step 4: Run `--filter "FullyQualifiedName~EditModeTests"`, then the full suite** (existing engine tests must be untouched except for the three `Pressed` callers inside the engine itself).
- [ ] **Step 5: Commit** — `feat: the edit chord rewrites the selection`

---

### Task 6: Settings, chord and wiring

**Files:**
- Modify: `windows/src/VoxScribe.Core/AppSettings.cs` (after `CommandKeys`, and after `CleanupApiKey`)
- Modify: `windows/src/VoxScribe.App/Composition.cs`
- Modify: `windows/src/VoxScribe.App/Views/Settings/ShortcutsSection.cs` (`ShortcutSlot`, the section's rows)
- Modify: `windows/src/VoxScribe.App/Views/SettingsPage.cs` (`Chord(slot)` and `SaveChord` switches)
- Modify: `windows/src/VoxScribe.App/Views/Settings/CleanupSection.cs`
- Test: `windows/tests/VoxScribe.Core.Tests/SettingsMigrationTests.cs` (append)

**Interfaces:**
- Consumes: `DictationEngine(..., editHotkey:)`, `DictationEngine.Edit`, `TextEditor` (Tasks 3, 5).
- Produces: `int[]? SettingsData.EditKeys { get; init; }`, `string? SettingsData.EditModel { get; init; }`, `ShortcutSlot.Edit`.

- [ ] **Step 1: Failing test:**

```csharp
    [Fact]
    public void Edit_is_unbound_and_uses_the_cleanup_model_by_default() =>
        new SettingsData().ShouldSatisfyAllConditions(
            d => d.EditKeys.ShouldBeNull(),
            d => d.EditModel.ShouldBeNull());
```

- [ ] **Step 2: Run, expect compile failure.**
- [ ] **Step 3: Implement.**
  - `AppSettings.cs`:

```csharp
    /// <summary>The Edit chord: rewrites the selection as the spoken instruction says. Null = unbound.</summary>
    public int[]? EditKeys { get; init; }
```

```csharp
    /// <summary>Gateway alias for edits; null or empty uses <see cref="CleanupModel"/>.</summary>
    public string? EditModel { get; init; }
```

  - `Composition.cs`:
    - `var editHotkey = PlatformFactory.CreateHotkeySource(Chord(settings.Data.EditKeys));` beside `commandHotkey`; pass `editHotkey: editHotkey` to the engine.
    - In `Rearm()`: `var edit = Chord(settings.Data.EditKeys);`, add `edit` to every existing entry's others list, and add `(editHotkey, edit, [raw, clean, undo, command])`.
    - Beside `BuildCleanup`:

```csharp
            Func<string, string, CancellationToken, Task<EditOutcome>>? BuildEdit() =>
                settings.Data.CleanupEndpoint is { Length: > 0 } editEndpoint
                    ? new TextEditor(
                        editEndpoint,
                        settings.Data.EditModel is { Length: > 0 } editModel ? editModel : settings.Data.CleanupModel,
                        settings.Data.CleanupApiKey).EditAsync
                    : null;
```

      `engine.Edit = BuildEdit();` after `engine.Cleanup = ...`, and `live.Edit = BuildEdit();` in the settings-changed handler.
    - Add `editHotkey` to the read-aloud stop list (`new[] { hotkey, cleanupHotkey, commandHotkey, cancelHotkey }`).
  - `ShortcutSlot`: add `/// <summary>Rewrites the selection as the instruction says.</summary> Edit,` after `Command`.
  - `SettingsPage.cs`: `ShortcutSlot.Edit => _settings.Data.EditKeys,` and `ShortcutSlot.Edit => data with { EditKeys = chord },`.
  - `ShortcutsSection.cs`, after the COMMAND note:

```csharp
                Panels.Labelled("EDIT", keys[ShortcutSlot.Edit]),
                Panels.Note("Select text anywhere, hold this and say what to do with it — "
                    + "\"translate to English\", \"shorter\", \"fix the typos\". The selection is "
                    + "replaced; Undo puts it back. Uses the cleanup endpoint. Escape unbinds."),
```

  - `CleanupSection.cs`: an edit-model field after `MODEL`:

```csharp
        var editModel = Panels.Field("Empty = the cleanup model",
            settings.Data.EditModel,
            v => save(settings.Data with { EditModel = v }));
```

    and `Panels.Labelled("EDIT MODEL", editModel),` after `Panels.Labelled("MODEL", model),`.
- [ ] **Step 4: Run the full suite** (SettingsPage/UiTests build the page — they must still pass), then the Release `-warnaserror` build of `VoxScribe.sln`.
- [ ] **Step 5: Commit** — `feat: Edit chord and edit model in settings`

---

### Task 7: The pill knows Edit

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/HudWindow.cs` (`Mode()`)
- Modify: `windows/src/VoxScribe.App/Views/Pill/PillFace.cs` (`PillState` Mode doc, `CleanupTail`)
- Modify: `windows/src/VoxScribe.App/Views/Pill/PaperPill.cs`, `FluentPill.cs`, `TidePill.cs` (mode words and working texts); `OrbPill.cs`, `MonoPill.cs` already print `state.Mode` and need no mode-word change
- Test: `windows/tests/VoxScribe.App.Tests/PillTests.cs` (append)

**Interfaces:**
- Consumes: `DictationEngine.EditThisUtterance` (Task 5).

A failed edit never reaches the latency phase (it raises a notice and no `Completed`), so only the success split exists for Edit: `1.2s · edit 0.7s` (Orb/Tide short form `1.2s · 0.7s`).

- [ ] **Step 1: Failing tests:**

```csharp
    [AvaloniaFact]
    public void Edit_names_itself_while_working() => EachTheme((theme, face) =>
    {
        face.Update(new PillState(PillPhase.Working, 0.4, TimeSpan.FromSeconds(3), "EDIT", "traduis"));
        var text = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)).ToLowerInvariant();

        text.ShouldContain("edit", customMessage: theme.Id);
        text.ShouldNotContain("clean", customMessage: theme.Id);
    });

    [AvaloniaFact]
    public void An_edit_latency_says_edit_not_clean() => EachTheme((theme, face) =>
    {
        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "EDIT", string.Empty,
            Cleanup: TimeSpan.FromSeconds(0.7)));
        var text = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)).ToLowerInvariant();

        text.ShouldContain("0.7s", customMessage: theme.Id);
        text.ShouldNotContain("clean", customMessage: theme.Id);
    });
```

- [ ] **Step 2: Run `--filter "FullyQualifiedName~PillTests"`, expect FAIL.**
- [ ] **Step 3: Implement.**
  - `HudWindow.Mode()`: `_engine.CommandThisUtterance ? "CMD" : _engine.EditThisUtterance ? "EDIT" : _engine.CleaningThisUtterance ? "CLEAN" : "RAW"`.
  - `PillState` Mode doc: `"RAW", "CLEAN", "CMD" or "EDIT"`.
  - `CleanupTail`: the word follows the mode — `" · " + (s.Mode == "EDIT" ? "edit" : "clean") + (...)`; update its doc to `" · clean 0.7s" / " · edit 0.7s" / " · clean failed"`.
  - Mode words: Paper `"EDIT" => "edit"`, Fluent `"EDIT" => "Edit"`, Tide badge `"EDIT" => "Edit"`.
  - Working texts (where a face names the work): Paper `"EDIT"` → `"editing…"`; Fluent → `"Editing…"` and its working-preview line → `"Rewriting the selection"`; Tide → `"Rewriting"`; Orb's `WORKING · EDIT` and Mono's `EDIT` come from `state.Mode` already. Keep each face's existing casing and the `CLEAN` texts unchanged.
- [ ] **Step 4: Run PillTests (including the existing width test), then the full suite.** Then the final gate: `dotnet format VoxScribe.sln --verify-no-changes` and `dotnet build VoxScribe.sln -c Release --no-incremental -warnaserror`.
- [ ] **Step 5: Commit** — `feat: the pill shows Edit`

---

## By hand (owner, after the last task)

- Bind the Edit chord. In Notepad, Chrome (a textarea), VS Code, Word and a terminal: select a sentence, hold the chord, say "traduis en anglais", release → replaced. Undo → original back.
- Nothing selected → `Nothing selected`, nothing typed. Copy something first, run an edit: the clipboard still holds it afterwards.
- Stop the gateway → `Edit failed — …`, selection untouched.
