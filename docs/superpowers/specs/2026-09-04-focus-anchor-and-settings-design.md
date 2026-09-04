# Focus anchor + settings window refactor — design

Date: 2026-09-04. Status: approved by the owner in conversation; this is the written record.

Two pieces of work, shipped together because the second is where the first gets its
toggle:

1. **Focus anchor** — the field that had focus when the shortcut was pressed receives the
   text on release, even if the user switched windows or clicked elsewhere while speaking.
2. **Settings window refactor** — resizable, scrollable, one file per section, shared
   helpers moved to `Panels`, sections regrouped.

Independent of the four wave plans dated 2026-08-30; nothing here consumes or redefines
their types.

---

## 1. Focus anchor

### Behaviour

- On shortcut **press**, VoxScribe remembers the *target*: the foreground window and the
  focused control inside it.
- While recording, the user may do anything: switch windows, click other fields, open
  VoxScribe's own windows.
- On **release**, before typing, VoxScribe brings the target window to the foreground,
  re-focuses the remembered control, then types exactly as today through `ITextInjector`.
- The user is left on the target after typing. No second focus switch back.
- If the target cannot be captured or restored (elevated window, app frozen, window
  closed), VoxScribe types into whatever has focus at release — today's behaviour — and
  otherwise stays silent.
- **Anchoring wins over incremental typing.** When anchoring is on, phrases are held and
  typed together on release, because typing them as they land would send them to whatever
  the user is clicking on. The engine's existing non-incremental path is exactly that.
- Setting: `AnchorFocus`, default **on**. Off restores today's behaviour bit for bit.
- Capture never delays recording: it runs concurrently with the start of audio capture and
  is awaited only at release.

### Why UI Automation for the control, and not only the window

Re-activating the window handle alone makes Windows and the app restore *their* idea of
the last focused control. That covers "switch window and come back" but not "click a
different field in the same window", because the app now remembers the new field. UI
Automation's focused element is the one thing that identifies the field itself across
Win32, WPF, Electron and Chromium, and its `SetFocus` restores it. The caret position is
the control's own business; every mainstream editor restores it.

### Why not write through UI Automation

`SendInputTextInjector` already documents this: `TextPattern` cannot insert, `ValuePattern`
replaces whole fields and is unsupported on multi-line controls. Input has to be simulated,
so the target must genuinely have focus. Hence foreground + focus + SendInput.

### Cost

Two calls per dictation, nothing between them and nothing while idle. Capture is one
cross-process COM round trip (typically 1–10 ms). Restore is `SetForegroundWindow`, a short
bounded wait for the window to actually come forward, one `SetFocus` round trip, then the
existing typing. The only real risk is a hang on a frozen target, so both calls run on a
pool thread under a timeout and fall back to today's behaviour.

### Interfaces (VoxScribe.Abstractions)

```csharp
/// <summary>Remembers where text should land, so the user is free to look elsewhere.</summary>
public interface IFocusAnchor
{
    /// <summary>Captures the current target, or null if there is none or it took too long.</summary>
    ValueTask<IFocusTarget?> CaptureAsync(CancellationToken cancellationToken);
}

/// <summary>A captured target that can be brought back.</summary>
public interface IFocusTarget
{
    /// <summary>Brings the window forward and re-focuses the control.</summary>
    /// <returns>False if the target could not be restored; the caller types anyway.</returns>
    ValueTask<bool> RestoreAsync(CancellationToken cancellationToken);
}
```

No opaque `object` handles: the target restores itself, so Core never casts.

### Engine (VoxScribe.Core.DictationEngine)

- Constructor gains an optional `IFocusAnchor? focusAnchor = null` parameter, **appended
  after** `cleanupHotkey` so every existing call site compiles unchanged.
- New property `public bool AnchorFocus { get; set; }`, mirrored live from settings by
  `Composition` exactly like `IncrementalInjection`.
- `BeginAsync`, inside the gate, after the segmenter reset: if `AnchorFocus && _focusAnchor
  is not null`, start `_anchorCapture = _focusAnchor.CaptureAsync(CancellationToken.None)`
  and set `_anchoredThisUtterance = true`; otherwise both are cleared. Fixed at press, like
  `_cleanThisUtterance`, so flipping the setting mid-utterance cannot double-type.
- `InjectIncrementally` becomes `IncrementalInjection && !_cleanThisUtterance &&
  !_anchoredThisUtterance`.
- `ProcessAsync`, immediately before the final `_injector.InjectAsync(text, …)` and only
  when there is text to type: `if (_anchorCapture is { } capture && await capture is { }
  target) await target.RestoreAsync(CancellationToken.None)`. The boolean result is ignored
  on purpose; the fallback is to type where we are.
- Nothing else changes. `PartialText`, `Completed`, history, cleanup all behave as today.

### Windows implementation (VoxScribe.Platform.Windows.UiAutomationFocusAnchor)

- Direct COM interop, **no new package and no WPF framework reference**. The app is
  published self-contained with the platform DLL copied loosely; a `Microsoft.WindowsDesktop.App`
  reference on the platform project would not ship its assemblies. `[ComImport]` interfaces
  declared as a *prefix* of the real vtables are valid as long as only declared slots are
  called:
  - `CUIAutomation` coclass `ff48dba4-60ef-4201-aa87-54103eef594e`
  - `IUIAutomation` `30cbe57d-d9d0-452a-ab13-7ac5ac4825ee`, slots in order:
    `CompareElements, CompareRuntimeIds, GetRootElement, ElementFromHandle,
    ElementFromPoint, GetFocusedElement` — only the last is used.
  - `IUIAutomationElement` `d22108aa-8ac5-49a5-837b-37bbb3d7591e`, slot 0 `SetFocus` —
    the only one used.
- **Capture**: `GetForegroundWindow()` plus `IUIAutomation.GetFocusedElement()`, both on
  `Task.Run` with a 250 ms timeout (`CaptureTimeout` constant). Timeout or any COM failure
  returns null. A foreground handle that belongs to our own process is still a valid target
  (dictating into VoxScribe's own dictionary editor must work).
- **Restore**: on `Task.Run` with a 600 ms overall timeout (`RestoreTimeout`):
  1. If `GetForegroundWindow()` already equals the anchored handle, skip to step 3.
  2. `SetForegroundWindow(hwnd)`. Poll `GetForegroundWindow()` every 10 ms
     (`ForegroundPoll`) for up to 300 ms (`ForegroundWait`). If it never matches, retry once
     with the `AttachThreadInput` bridge (attach our thread to the current foreground
     window's thread, `SetForegroundWindow`, detach) and poll again. A window that is gone
     (`IsWindow` false) fails immediately.
  3. `IUIAutomationElement.SetFocus()` on the captured element. A COM failure here is not
     fatal: the window is forward and Windows has restored its last focused child, which is
     right in the common case.
  4. `Thread.Sleep(FocusSettle)` (40 ms) so the target's own focus handling completes
     before the first keystroke arrives.
  5. Return true if step 2 succeeded, else false.
- Same UIPI caveat as `SendInput`: an elevated target silently ignores all of this and the
  text goes wherever focus is. Documented in the class remarks, not handled.
- `PlatformFactory.CreateFocusAnchor()` resolves it by name like the injector and returns
  null off Windows. `Composition` passes it to the engine.
- Stays logic-free in the AGENTS.md sense: timeouts, polling and fallback are plain Win32
  plumbing, and everything decidable (when to anchor, incremental suppression, ordering) is
  in Core under test.

### Tests

- `VoxScribe.Testing`: `FakeFocusAnchor : IFocusAnchor` returning a `FakeFocusTarget` that
  records how many strings the `RecordingTextInjector` had received at the moment
  `RestoreAsync` ran (`InjectedWhenRestored`) and counts restores. A `CaptureReturnsNull`
  switch simulates a failed capture.
- `DictationEngineTests`:
  - anchor on → exactly one capture, one restore, `InjectedWhenRestored == 0`, text typed
    once.
  - anchor on + incremental on → restore runs, text typed **once** at the end, not per
    phrase (this is the regression the "anchoring wins" rule exists for).
  - anchor off → no capture, no restore.
  - capture returns null → text still typed once, no restore.
  - nothing spoken → no restore (no foreground steal for an empty utterance).
- `StorageTests` round-trips `AnchorFocus` through the source-generated JSON context as
  every other flag already does; verify the default is true on a missing key.
- Windows implementation: **manual test only**, listed in the plan. CI cannot exercise
  SendInput or UI Automation.

---

## 2. Settings window refactor

### Window

- `CanResize = true`. `SizeToContent = Manual`. Opens at `Tokens.Size.SettingsWidth ×
  SettingsHeight` (540 × 720) with `MinWidth/MinHeight` at `SettingsMinWidth/MinHeight`
  (480 × 480). Four new tokens in a new `Tokens.Size` class; `Views must not contain literal
  values` still holds.
- Content is hosted in a `ScrollViewer` with `VerticalScrollBarVisibility = Auto` and
  horizontal disabled. Sections stretch to the width.
- Remains a modal dialog opened from the rail's gear key. Not moved into a rail section:
  the chord recorder installs a live keyboard hook tied to the window's lifetime and a
  dialog is the right scope for that.

### Files

```
Views/SettingsWindow.cs            shell: window setup, scroll host, section list,
                                   chord recorder (needs the window lifetime), Save()
Views/Settings/ShortcutsSection.cs two chords, toggle mode        (takes the recorder buttons + warning from the shell)
Views/Settings/TypingSection.cs    inject, anchor, phrase-by-phrase
Views/Settings/CleanupSection.cs   endpoint, model, key, status probe   (moved verbatim)
Views/Settings/SpeechSection.cs    microphone, model, remote server as three labelled sub-blocks
Views/Settings/GeneralSection.cs   history, start at login
Views/Settings/AppearanceSection.cs accent swatches                (moved verbatim)
```

Each section file is `internal static class XxxSection` with one `public static Control
Build(AppSettings settings, Action<SettingsData> save, …)`; extra parameters only where the
shell owns state (`ShortcutsSection` receives the two `TransportKey` buttons and the warning
`TextBlock`, because the recorder mutates them). Namespace `VoxScribe.App.Views.Settings`.

### Shared helpers → `Panels`

`Section`, `Note`, `Field` and `Toggle` move from `SettingsWindow` to `Panels` unchanged in
appearance. `Toggle` gains an optional `string? hint = null`; when present, a `Note`-styled
`TextBlock` in `Tokens.Colors.InkSecondary` at `Tokens.Fonts.Label` is stacked under the
label with `Tokens.Space.Tight` spacing, indented to align with the label text. No new
control type.

### Order and copy

1. **SHORTCUTS** — raw chord, cleanup chord, "Toggle mode — press once to start, press
   again to stop".
2. **TYPING**
   - "Type transcripts into the focused app" (`InjectText`).
   - "Type into the field that had focus when you pressed the shortcut" (`AnchorFocus`),
     hint: *"You can switch windows or click elsewhere while speaking. On release
     Vox-Scribe brings that field back and types there."*
   - "Type each phrase as you speak it, not all at the end (raw only)"
     (`IncrementalInjection`), hint: *"While the option above is on, phrases are held and
     typed together on release, so nothing lands in the wrong window."*
3. **CLEANUP** — unchanged.
4. **SPEECH** — sub-blocks `Microphone`, `Model`, `Remote server` using `Panels.Labelled`.
5. **GENERAL** — "Keep a transcript history", "Start Vox-Scribe when I log in, minimised to
   the tray".
6. **APPEARANCE** — accent swatches.

Every control keeps its exact behaviour and settings key. Only placement, hosting and the
two hints are new.

### Tests (`VoxScribe.App.Tests`, Avalonia headless)

- `SettingsWindow` opens, has positive bounds, `CanResize` true, a `ScrollViewer` in its
  visual tree, and six `BrushedPanel` sections in the order above (by their `Silkscreen`
  label text).
- `Panels.Toggle` with a hint yields a `CheckBox` whose content contains the hint text;
  without a hint, no second `TextBlock`.
- Existing `DesignSystemTests` keep guarding literals in views.

---

## Out of scope

- Returning focus to where the user was after typing.
- A pinned or explicit "select this field" gesture; the anchor is always the focus at
  press.
- Handling elevated targets.
- Moving settings into the main window's rail.
