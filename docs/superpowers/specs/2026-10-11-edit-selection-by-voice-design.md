# Edit a selection by voice

Date: 2026-10-11 · Status: approved design, not yet implemented

Select text anywhere, hold the **Edit** chord, say an instruction ("translate to English",
"shorter", "fix the typos"), release: the selection is replaced by the model's rewrite.

## Behaviour

1. Hold the Edit chord (unbound by default, recorded in Settings → Shortcuts like the others),
   speak the instruction, release.
2. The instruction is transcribed; dictionary corrections and spoken punctuation apply to it
   as to any dictation. Incremental injection never applies to Edit (like Cleanup).
3. Focus anchoring restores the window that had focus at press, as for every dictation.
4. The selection is copied **at release** — not at press: while the chord is held its
   modifiers are down, and a synthesized Ctrl+Insert would arrive as Ctrl+Shift+Insert or
   Ctrl+Alt+Insert. The copy itself is Ctrl+Insert, not Ctrl+C: with nothing selected, Ctrl+C
   is a terminal's interrupt (Windows Terminal, a shell running Claude Code) — firing it on
   every Edit release would kill whatever command is running there.
5. Outcomes:
   - **Nothing selected** → nothing is typed; the pill shows the notice `Nothing selected`.
   - **Selection longer than 8 000 characters** → nothing is typed; notice `Selection too long`.
   - **No cleanup endpoint configured** → the chord still records like any other (so the pill
     is visible); nothing is copied or typed; notice `Set a cleanup endpoint to edit by voice`
     is raised **at release**, before the selection is copied.
   - **Model call fails** (timeout, HTTP/network error, unusable answer) → nothing is typed,
     the selection is left as it was; notice says why (same wording style as cleanup's).
   - **Success** → the result is injected while the selection is still active, so it replaces it.
6. **Undo** after an edit backspaces the result and re-injects the original selection.
   After an ordinary dictation Undo is unchanged.
7. The pill shows the badge `EDIT` (each face in its own casing: Paper `edit`, Fluent `Edit`,
   Orb/Mono `EDIT`, Tide badge `Edit`), and in the latency phase the edit call's time
   reuses the cleanup split (`1.2s · clean 0.7s` → for Edit: `1.2s · edit 0.7s`; Orb/Tide
   short form `1.2s · 0.7s`). A failed edit shows its notice instead — it never reaches the
   latency phase.
8. History records the instruction as `RawText`, the result as `Text`, and the call's time and
   outcome in `CleanupSeconds` / `CleanupFailed` (they mean "the model call" for Edit).
   Nothing is recorded when nothing was typed.

## Design

### Platform (logic-free)

`ITextInjector` gains:

```csharp
/// Copies the focused control's selection: saves the clipboard, sends Ctrl+Insert (not
/// Ctrl+C — a terminal reads that as its interrupt even with no selection), waits briefly,
/// reads the text if the clipboard sequence number moved, restores the clipboard.
/// Returns null when nothing was copied (no selection, or no text in it).
ValueTask<string?> CopySelectionAsync(CancellationToken cancellationToken);
```

`SendInputTextInjector` implements it with the clipboard helpers it already has
(`ReadClipboardText`/`WriteClipboardText`, `OpenClipboardWithRetry`) plus
`GetClipboardSequenceNumber`, and the same modifier handling its paste path uses. The settle
delay is a named constant. The platform layer stays logic-free: no retries beyond the existing
clipboard-open retry, no decisions.

`RecordingTextInjector` (VoxScribe.Testing) gains a settable `Selection` it returns.

### Core

- **`ChatCompletion`** (new, internal to Core): the HTTP plumbing extracted from `TextCleaner`
  — builds the OpenAI-compatible body, sends with a timeout, returns the content string or a
  failure reason. `TextCleaner` is refactored onto it with **no behaviour change**
  (its existing tests must pass unmodified).
- **`TextEditor`**: `Task<EditOutcome> EditAsync(string selection, string instruction, CancellationToken)`
  with `record EditOutcome(string? Text, string? Failure)`. Prompt: a system message that says
  apply the instruction to the text and return only the edited text; the user message carries
  the instruction and the text, clearly delimited. Timeout: `TextCleaner.TimeoutFor(selection)`.
  `max_tokens` sized for the selection (translation can grow it — allow ~2× + margin).
  Guard (`TextEditor.Vet`): reject empty/whitespace and any answer containing `<think`; strip
  one surrounding ``` fence pair; **no** word-overlap check (a translation shares no words).
- **`DictationEngine`**:
  - an `editHotkey` constructor parameter (like the other optional hotkeys) and an
    `OnEditPressed` → `Pressed(...)` path setting an `_editThisUtterance` flag;
    `EditThisUtterance` is exposed for the pill like `CommandThisUtterance`.
  - `Func<string, string, CancellationToken, Task<EditOutcome>>? Edit` property (null when no
    endpoint) — the chord still records when it is null; the edit branch's own top guard
    raises the endpoint notice at release, before anything is copied.
  - In `ProcessAsync`, the edit branch: restore the anchored focus → `CopySelectionAsync` →
    notices for null / too long → `Edit(selection, instruction)` timed with `IClock` →
    failure notice, or inject the result and record it in the journal together with the
    original selection.
  - `MaxEditSelection = 8000` constant.
- **`InjectionJournal`**: `Record(string injected, string? replaced = null)` and a
  `Replaced` property (the original selection of the last dictation, null otherwise),
  cleared by `BeginDictation`. `UndoLastDictationAsync`: after backspacing, if `Replaced` is
  set, inject it.
  - ponytail: undo backspaces one keystroke per grapheme, so undoing an 8 000-character edit
    takes about a second; selecting the result instead would need a platform selection API.

### App

- `AppSettings`: `int[]? EditKeys` (unbound default), `string? EditModel` (null/empty →
  `CleanupModel`). `ShortcutSlot.Edit` and its row in `ShortcutsSection`; the chord
  participates in `Composition`'s blocker computation like every other chord
  (the longer chord wins — see AGENTS.md).
- `CleanupSection`: an `EDIT MODEL` field with the note "Empty uses the cleanup model."
- `Composition`: builds `TextEditor` from the cleanup endpoint/key and `EditModel ?? CleanupModel`,
  rebuilt on settings change like `BuildCleanup`; wires `editHotkey`.
- `HudWindow.Mode()` returns `"EDIT"` when `EditThisUtterance`; each pill face maps it.
  `PillFace.CleanupTail`/short form take the word from the mode (`clean` vs `edit`).

## Testing

- `TextEditor`: Vet rejects empty and `<think`, strips a fence, keeps a translation that shares
  no words with the input; an unreachable gateway yields a failure with a reason.
- `TextCleaner`: existing suite unchanged after the `ChatCompletion` extraction.
- `DictationEngine` (fakes): selection replaced by the result; no selection → nothing injected
  + notice; > 8 000 chars → nothing injected + notice; edit failure → nothing injected +
  notice; no endpoint → the chord still records, notice at release, nothing copied; Undo after an edit backspaces the
  result and re-injects the original; Undo after a plain dictation unchanged; the Edit chord
  never injects incrementally.
- `InjectionJournal`: `Replaced` set by an edit, cleared by the next dictation.
- Chords: Edit is added to `Composition.Rearm`'s blocker lists (wiring; `Blockers` itself is
  already covered by `ChordOverlapTests`).
- Pill: `EDIT` badge and the edit split in every face; widths still fit (the existing
  per-face width test).
- By hand (no fake can prove these): Ctrl+Insert capture and replacement in Notepad, Chrome,
  VS Code, Word, a terminal with and without a selection (nothing interrupted); the user's
  clipboard is intact afterwards; Undo restores the original.

## Out of scope

Generating text when nothing is selected; select-all fallback; a separate gateway for Edit;
a preview/confirm step before replacing.
