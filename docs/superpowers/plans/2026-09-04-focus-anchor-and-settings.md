# Focus Anchor + Settings Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Text lands in the field that had focus when the shortcut was pressed, wherever the user has wandered by release; and the settings window becomes resizable, scrollable, and split into one file per section with a toggle (and hints) for the new behaviour.

**Architecture:** Two new interfaces in Abstractions (`IFocusAnchor` captures, `IFocusTarget` restores itself). `DictationEngine` starts a capture at press, awaits it at release, restores just before the final inject, and suppresses incremental typing while anchored. The Windows side is a thin COM-interop class (`UiAutomationFocusAnchor`) using `SetForegroundWindow` + UI Automation `SetFocus`, resolved by name through `PlatformFactory` like the injector. Settings: shared helpers move to `Panels`, sections become static builders under `Views/Settings/`, the window gets a `ScrollViewer` and size tokens.

**Tech Stack:** .NET 10, Avalonia 11.3.20 (headless tests via Avalonia.Headless.XUnit), xUnit 2.9.3 + Shouldly, Win32 P/Invoke + `[ComImport]` UI Automation.

Spec: `docs/superpowers/specs/2026-09-04-focus-anchor-and-settings-design.md`.

## Global Constraints

- Build/test: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` (must stay green after every task). `dotnet build VoxScribe.sln` only works on Windows.
- `VoxScribe.Platform.Windows` is the only project allowed to call Win32 and must stay logic-free; decisions live in Core behind interfaces.
- **No new NuGet packages and no `Microsoft.WindowsDesktop.App` / `UseWPF` reference** — the app publishes self-contained and copies the platform DLL loosely; a desktop framework reference would not ship.
- Views must not contain literal values: every colour/size/radius/duration comes from `windows/src/VoxScribe.App/Design/DesignTokens.cs`; add tokens rather than inlining.
- `shared/dictionary-test-vectors.json` is untouched by this work.
- Independent of the four 2026-08-30 wave plans: do not consume or define `InjectionJournal`, `BackspaceAsync`, `RawText`, `AppHealth`.
- Every public member gets an XML doc comment (the repo treats missing docs as warnings-as-errors).
- Commit after each task; commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Task dependency graph (for parallel execution)

```
Task 1 (interfaces, fakes, setting, factory hook)
  ├── Task 2 (engine + Composition)          ─┐
  └── Task 3 (Windows UI Automation anchor)  ─┤ parallel
Task 4 (Panels helpers + size tokens)         ─┘ parallel with 1–3
  └── Task 5 (settings split, needs 1 + 4)
Task 6 (manual Windows verification + docs, needs 2 + 3 + 5)
```

---

### Task 1: Focus-anchor contract, fakes, setting and factory hook

**Files:**
- Modify: `windows/src/VoxScribe.Abstractions/Interfaces.cs` (append after `ITextInjector`, ~line 75)
- Modify: `windows/src/VoxScribe.Core/AppSettings.cs` (add `AnchorFocus` after `IncrementalInjection`, ~line 57)
- Modify: `windows/src/VoxScribe.Testing/Fakes.cs` (append after `RecordingTextInjector`, ~line 197)
- Modify: `windows/src/VoxScribe.App/PlatformFactory.cs` (add `CreateFocusAnchor` after `CreateTextInjector`, ~line 209)
- Test: `windows/tests/VoxScribe.Core.Tests/StorageTests.cs` (inside `AppSettingsTests`, ~line 240)

**Interfaces:**
- Produces: `IFocusAnchor.CaptureAsync(CancellationToken) → ValueTask<IFocusTarget?>`; `IFocusTarget.RestoreAsync(CancellationToken) → ValueTask<bool>`; `SettingsData.AnchorFocus` (bool, default true); `FakeFocusAnchor` / `FakeFocusTarget` in `VoxScribe.Testing`; `PlatformFactory.CreateFocusAnchor() → IFocusAnchor?`.

- [ ] **Step 1: Write the failing settings test**

In `windows/tests/VoxScribe.Core.Tests/StorageTests.cs`, inside `AppSettingsTests`, add:

```csharp
    [Fact]
    public void Anchor_focus_defaults_on_and_round_trips()
    {
        new AppSettings(_path).Data.AnchorFocus.ShouldBeTrue();

        var settings = new AppSettings(_path);
        settings.Update(settings.Data with { AnchorFocus = false });

        new AppSettings(_path).Data.AnchorFocus.ShouldBeFalse();
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter Anchor_focus_defaults_on_and_round_trips`
Expected: build error `'SettingsData' does not contain a definition for 'AnchorFocus'`.

- [ ] **Step 3: Add the setting**

In `windows/src/VoxScribe.Core/AppSettings.cs`, directly after `public bool IncrementalInjection { get; init; }`:

```csharp
    /// <summary>
    /// Whether text goes to the field that had focus when the shortcut was pressed, rather
    /// than wherever focus is at release.
    /// </summary>
    /// <remarks>
    /// On by default: it lets the user switch windows or click elsewhere while speaking.
    /// While on, it overrides <see cref="IncrementalInjection"/> — phrases are held and typed
    /// together at release, because typing them as they land would send them to whatever the
    /// user is clicking on at that moment.
    /// </remarks>
    public bool AnchorFocus { get; init; } = true;
```

- [ ] **Step 4: Add the interfaces**

In `windows/src/VoxScribe.Abstractions/Interfaces.cs`, after the `ITextInjector` interface:

```csharp
/// <summary>
/// Remembers where dictated text should land, so the user is free to look elsewhere while
/// speaking.
/// </summary>
/// <remarks>
/// Capture happens at shortcut press, restore just before the text is typed. Both are best
/// effort: a null capture or a false restore means "type wherever focus is now", which is
/// the behaviour the app had before anchoring existed.
/// </remarks>
public interface IFocusAnchor
{
    /// <summary>Captures the current target, or null if there is none or it took too long.</summary>
    ValueTask<IFocusTarget?> CaptureAsync(CancellationToken cancellationToken);
}

/// <summary>A captured focus target that can bring itself back.</summary>
public interface IFocusTarget
{
    /// <summary>Brings the window forward and re-focuses the control.</summary>
    /// <returns>False if the target could not be restored; the caller types anyway.</returns>
    ValueTask<bool> RestoreAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Add the fakes**

In `windows/src/VoxScribe.Testing/Fakes.cs`, after `RecordingTextInjector`:

```csharp
/// <summary>A focus anchor that records when it was captured and restored.</summary>
public sealed class FakeFocusAnchor : IFocusAnchor
{
    private readonly RecordingTextInjector _injector;

    /// <summary>Builds an anchor that watches <paramref name="injector"/> to prove ordering.</summary>
    public FakeFocusAnchor(RecordingTextInjector injector) => _injector = injector;

    /// <summary>When true, <see cref="CaptureAsync"/> returns null — a failed capture.</summary>
    public bool CaptureReturnsNull { get; set; }

    /// <summary>How many captures were requested.</summary>
    public int Captures { get; private set; }

    /// <summary>Every target handed out, in order.</summary>
    public List<FakeFocusTarget> Targets { get; } = [];

    /// <inheritdoc />
    public ValueTask<IFocusTarget?> CaptureAsync(CancellationToken cancellationToken)
    {
        Captures++;
        if (CaptureReturnsNull) return ValueTask.FromResult<IFocusTarget?>(null);

        var target = new FakeFocusTarget(_injector);
        Targets.Add(target);
        return ValueTask.FromResult<IFocusTarget?>(target);
    }
}

/// <summary>A target that notes how much had already been typed when it was restored.</summary>
public sealed class FakeFocusTarget : IFocusTarget
{
    private readonly RecordingTextInjector _injector;

    /// <summary>Builds a target watching <paramref name="injector"/>.</summary>
    public FakeFocusTarget(RecordingTextInjector injector) => _injector = injector;

    /// <summary>How many times <see cref="RestoreAsync"/> ran.</summary>
    public int Restores { get; private set; }

    /// <summary>
    /// Number of strings the injector had received when the first restore ran, or -1 if
    /// never restored. Zero proves restore happened before typing.
    /// </summary>
    public int InjectedWhenRestored { get; private set; } = -1;

    /// <inheritdoc />
    public ValueTask<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        if (Restores++ == 0) InjectedWhenRestored = _injector.Injected.Count;
        return ValueTask.FromResult(true);
    }
}
```

- [ ] **Step 6: Add the factory hook**

In `windows/src/VoxScribe.App/PlatformFactory.cs`, after `CreateTextInjector`:

```csharp
    /// <summary>Creates the UI Automation focus anchor, or null off Windows.</summary>
    public static IFocusAnchor? CreateFocusAnchor() =>
        Create<IFocusAnchor>("UiAutomationFocusAnchor", []);
```

(`Create` returns null when the type is absent, so this compiles and runs before Task 3 lands.)

- [ ] **Step 7: Run the full suite**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf`
Expected: all green, including `Anchor_focus_defaults_on_and_round_trips`.

- [ ] **Step 8: Commit**

```bash
git add windows/src/VoxScribe.Abstractions/Interfaces.cs windows/src/VoxScribe.Core/AppSettings.cs windows/src/VoxScribe.Testing/Fakes.cs windows/src/VoxScribe.App/PlatformFactory.cs windows/tests/VoxScribe.Core.Tests/StorageTests.cs
git commit -F - <<'EOF'
core: focus-anchor contract, AnchorFocus setting, test fakes

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 2: Engine anchors at press and restores before typing

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs` (ctor ~L111–125, fields ~L53, `InjectIncrementally` ~L194, `BeginAsync` ~L262–285, `ProcessAsync` ~L393–397)
- Modify: `windows/src/VoxScribe.App/Composition.cs` (engine construction ~L126–133, live settings handler ~L158)
- Test: `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`

**Interfaces:**
- Consumes: `IFocusAnchor`, `IFocusTarget`, `FakeFocusAnchor`, `FakeFocusTarget`, `SettingsData.AnchorFocus`, `PlatformFactory.CreateFocusAnchor()` (Task 1).
- Produces: `DictationEngine(…, IHotkeySource? cleanupHotkey = null, IFocusAnchor? focusAnchor = null)`; `DictationEngine.AnchorFocus { get; set; }`.

- [ ] **Step 1: Write the failing tests**

In `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`, add a second builder overload next to `Build` and five tests:

```csharp
    private static DictationEngine BuildAnchored(
        FakeHotkeySource hotkey,
        ITranscriber transcriber,
        RecordingTextInjector injector,
        FakeFocusAnchor anchor,
        bool anchorFocus = true,
        bool incremental = false) =>
        new(FakeAudioCapture.Tone(1.0), hotkey, transcriber, injector, () => [], new FakeClock(),
            focusAnchor: anchor)
        {
            AnchorFocus = anchorFocus,
            IncrementalInjection = incremental,
        };

    [Fact]
    public async Task Anchor_is_captured_at_press_and_restored_before_typing()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        await using var engine = BuildAnchored(hotkey, new FakeTranscriber("hello there"), injector, anchor);

        await DictateAsync(hotkey, engine);

        anchor.Captures.ShouldBe(1);
        anchor.Targets.ShouldHaveSingleItem();
        anchor.Targets[0].Restores.ShouldBe(1);
        anchor.Targets[0].InjectedWhenRestored.ShouldBe(0);
        injector.Injected.ShouldBe(["hello there"]);
    }

    [Fact]
    public async Task Anchoring_holds_phrases_until_release_even_when_incremental_is_on()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        await using var engine = BuildAnchored(
            hotkey, new FakeTranscriber("hello there"), injector, anchor, incremental: true);

        await DictateAsync(hotkey, engine);

        // One string, typed after restore — not one per phrase while the user was elsewhere.
        injector.Injected.ShouldBe(["hello there"]);
        anchor.Targets[0].InjectedWhenRestored.ShouldBe(0);
    }

    [Fact]
    public async Task Anchor_off_neither_captures_nor_restores()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        await using var engine = BuildAnchored(
            hotkey, new FakeTranscriber("hello there"), injector, anchor, anchorFocus: false);

        await DictateAsync(hotkey, engine);

        anchor.Captures.ShouldBe(0);
        injector.Injected.ShouldBe(["hello there"]);
    }

    [Fact]
    public async Task Failed_capture_still_types_into_current_focus()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector) { CaptureReturnsNull = true };

        await using var engine = BuildAnchored(hotkey, new FakeTranscriber("hello there"), injector, anchor);

        await DictateAsync(hotkey, engine);

        anchor.Captures.ShouldBe(1);
        anchor.Targets.ShouldBeEmpty();
        injector.Injected.ShouldBe(["hello there"]);
    }

    [Fact]
    public async Task Empty_utterance_does_not_steal_foreground()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        var anchor = new FakeFocusAnchor(injector);

        await using var engine = BuildAnchored(hotkey, new FakeTranscriber(""), injector, anchor);

        await DictateAsync(hotkey, engine);

        anchor.Captures.ShouldBe(1);
        anchor.Targets.ShouldHaveSingleItem();
        anchor.Targets[0].Restores.ShouldBe(0);
        injector.Injected.ShouldBeEmpty();
    }
```

If `FakeTranscriber("")` yields a segment the engine drops as silence before transcribing, that is fine: the assertion is only that restore never ran.

- [ ] **Step 2: Run to verify they fail**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter DictationEngineTests`
Expected: build error — no `focusAnchor` parameter, no `AnchorFocus` property.

- [ ] **Step 3: Implement in the engine**

In `windows/src/VoxScribe.Core/DictationEngine.cs`:

Fields, next to `_injector`:

```csharp
    private readonly IFocusAnchor? _focusAnchor;

    /// <summary>The capture started at press, awaited at release. Null when not anchoring.</summary>
    private Task<IFocusTarget?>? _anchorCapture;

    /// <summary>Fixed at press, like the cleanup flag, so a mid-utterance toggle cannot double-type.</summary>
    private bool _anchoredThisUtterance;
```

Constructor — add a trailing parameter and doc:

```csharp
    /// <param name="focusAnchor">
    /// Optional. Remembers the focused field at press so the text can be typed there at
    /// release even if the user has moved on. Null means text goes wherever focus is.
    /// </param>
    public DictationEngine(
        IAudioCapture capture,
        IHotkeySource hotkey,
        ITranscriber transcriber,
        ITextInjector injector,
        Func<IReadOnlyList<DictionaryEntry>> dictionary,
        IClock? clock = null,
        IHotkeySource? cleanupHotkey = null,
        IFocusAnchor? focusAnchor = null)
    {
        // …existing assignments…
        _focusAnchor = focusAnchor;
```

Property, next to `IncrementalInjection`:

```csharp
    /// <summary>
    /// Whether to type into the field that had focus at press. Overrides
    /// <see cref="IncrementalInjection"/> for the utterance: phrases are held and typed
    /// together at release, because typing them as they land would send them wherever the
    /// user is clicking at that moment.
    /// </summary>
    public bool AnchorFocus { get; set; }
```

`InjectIncrementally`:

```csharp
    private bool InjectIncrementally =>
        IncrementalInjection && !_cleanThisUtterance && !_anchoredThisUtterance;
```

`BeginAsync`, inside the gate, right after the `lock (_segments) { … }` block and before `_capturedSamples = 0;`:

```csharp
            // Started, not awaited: capture must never delay the first audio chunk. It is
            // collected at release, just before typing.
            _anchoredThisUtterance = AnchorFocus && _focusAnchor is not null;
            _anchorCapture = _anchoredThisUtterance
                ? _focusAnchor!.CaptureAsync(CancellationToken.None).AsTask()
                : null;
```

`ProcessAsync`, replace the final inject:

```csharp
        // In incremental mode every segment was typed as it landed, so there is nothing left
        // to type — injecting here would double the whole utterance.
        if (InjectIncrementally) return;

        // Bring the anchored field back first. A failed restore is deliberate silence: the
        // fallback is to type where focus is now, which is what the app always did.
        if (_anchorCapture is { } capture && await capture.ConfigureAwait(false) is { } target)
            await target.RestoreAsync(CancellationToken.None).ConfigureAwait(false);

        await _injector.InjectAsync(text, CancellationToken.None).ConfigureAwait(false);
```

Note the existing early `if (spoken.Length == 0) return;` above already guarantees no restore for an empty utterance — keep it above the restore.

- [ ] **Step 4: Run the engine tests**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter DictationEngineTests`
Expected: all pass, including the five new ones.

- [ ] **Step 5: Wire Composition**

In `windows/src/VoxScribe.App/Composition.cs`:

Next to `var injector = PlatformFactory.CreateTextInjector();`:

```csharp
        var focusAnchor = PlatformFactory.CreateFocusAnchor();
```

Engine construction:

```csharp
            engine = new DictationEngine(
                capture!, hotkey!, transcriber, injector!,
                () => dictionary.Entries,
                cleanupHotkey: cleanupHotkey,
                focusAnchor: focusAnchor);

            engine.ToggleMode = settings.Data.PushToTalkToggle;
            engine.IncrementalInjection = settings.Data.IncrementalInjection;
            engine.AnchorFocus = settings.Data.AnchorFocus;
```

Inside the `settings.Changed` handler, after `live.IncrementalInjection = …;`:

```csharp
                live.AnchorFocus = settings.Data.AnchorFocus;
```

- [ ] **Step 6: Full suite**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf`
Expected: green. (On Windows also `dotnet build VoxScribe.sln` — green.)

- [ ] **Step 7: Commit**

```bash
git add windows/src/VoxScribe.Core/DictationEngine.cs windows/src/VoxScribe.App/Composition.cs windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs
git commit -F - <<'EOF'
core: anchor the focused field at press, restore it before typing

Anchoring wins over incremental typing for the utterance, so nothing
lands in whatever the user is clicking on while they speak.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 3: Windows implementation via UI Automation COM interop

**Files:**
- Create: `windows/src/VoxScribe.Platform.Windows/UiAutomationFocusAnchor.cs`

**Interfaces:**
- Consumes: `IFocusAnchor`, `IFocusTarget` (Task 1); `PushToTalkHook.InjectedTag` is *not* needed (no keys are sent).
- Produces: `VoxScribe.Platform.Windows.UiAutomationFocusAnchor` with a public parameterless constructor (resolved by name by `PlatformFactory.CreateFocusAnchor()`).

No automated test can exercise this (CI cannot drive foreground windows); Task 6 holds the manual test. Keep it plumbing-only.

- [ ] **Step 1: Write the class**

```csharp
using System.Runtime.InteropServices;
using VoxScribe.Abstractions;

namespace VoxScribe.Platform.Windows;

/// <summary>
/// Remembers the foreground window and the UI Automation focused element at press, and
/// brings both back at release.
/// </summary>
/// <remarks>
/// <para>
/// Re-activating the window alone makes Windows restore the window's <i>own</i> idea of its
/// last focused control, which is wrong the moment the user clicked another field in the
/// same window while speaking. UI Automation's focused element identifies the field itself
/// across Win32, WPF, Electron and Chromium, and <c>SetFocus</c> restores it. The caret is
/// the control's business.
/// </para>
/// <para>
/// <b>Direct COM interop, on purpose.</b> <c>System.Windows.Automation</c> lives in the
/// WindowsDesktop framework, which this self-contained publish does not ship. The
/// interfaces below are declared as a <i>prefix</i> of the real vtables; that is valid as
/// long as only declared slots are called, and only <c>GetFocusedElement</c> and
/// <c>SetFocus</c> are.
/// </para>
/// <para>
/// Same UIPI caveat as <c>SendInput</c>: an elevated target ignores all of this silently and
/// the text goes wherever focus is. Every call runs on a pool thread under a timeout so a
/// frozen target cannot stall the dictation.
/// </para>
/// </remarks>
public sealed class UiAutomationFocusAnchor : IFocusAnchor
{
    /// <summary>Longest a capture may take before it is abandoned.</summary>
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Longest a restore may take, all steps included.</summary>
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMilliseconds(600);

    /// <summary>How long to wait for the window to actually come forward.</summary>
    private static readonly TimeSpan ForegroundWait = TimeSpan.FromMilliseconds(300);

    /// <summary>Interval between foreground checks.</summary>
    private static readonly TimeSpan ForegroundPoll = TimeSpan.FromMilliseconds(10);

    /// <summary>Pause after focusing, so the target's own focus handling finishes first.</summary>
    private static readonly TimeSpan FocusSettle = TimeSpan.FromMilliseconds(40);

    [ComImport]
    [Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation;

    /// <summary>Prefix of IUIAutomation: only <see cref="GetFocusedElement"/> is called.</summary>
    [ComImport]
    [Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2);
        int CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2);
        IUIAutomationElement GetRootElement();
        IUIAutomationElement ElementFromHandle(IntPtr hwnd);
        IUIAutomationElement ElementFromPoint(long pt);
        IUIAutomationElement GetFocusedElement();
    }

    /// <summary>Prefix of IUIAutomationElement: only <see cref="SetFocus"/> is called.</summary>
    [ComImport]
    [Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    private readonly IUIAutomation? _automation = TryCreateAutomation();

    private static IUIAutomation? TryCreateAutomation()
    {
        try { return (IUIAutomation)new CUIAutomation(); }
        catch (COMException) { return null; }
        catch (InvalidCastException) { return null; }
    }

    /// <inheritdoc />
    public async ValueTask<IFocusTarget?> CaptureAsync(CancellationToken cancellationToken)
    {
        var work = Task.Run(() =>
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            IUIAutomationElement? element = null;
            try { element = _automation?.GetFocusedElement(); }
            catch (COMException) { /* no element; the window alone is still worth restoring */ }

            return new Target(hwnd, element);
        }, cancellationToken);

        var finished = await Task.WhenAny(work, Task.Delay(CaptureTimeout, cancellationToken))
            .ConfigureAwait(false);

        return finished == work && work.Status == TaskStatus.RanToCompletion ? work.Result : null;
    }

    private sealed class Target : IFocusTarget
    {
        private readonly IntPtr _hwnd;
        private readonly IUIAutomationElement? _element;

        public Target(IntPtr hwnd, IUIAutomationElement? element)
        {
            _hwnd = hwnd;
            _element = element;
        }

        public async ValueTask<bool> RestoreAsync(CancellationToken cancellationToken)
        {
            var work = Task.Run(Restore, cancellationToken);
            var finished = await Task.WhenAny(work, Task.Delay(RestoreTimeout, cancellationToken))
                .ConfigureAwait(false);

            return finished == work && work.Status == TaskStatus.RanToCompletion && work.Result;
        }

        private bool Restore()
        {
            if (!IsWindow(_hwnd)) return false;

            var forward = GetForegroundWindow() == _hwnd || BringForward();

            // Not fatal: with the window forward, Windows has already restored its last
            // focused child, which is right whenever the user did not click another field.
            try { _element?.SetFocus(); }
            catch (COMException) { }

            Thread.Sleep(FocusSettle);
            return forward;
        }

        private bool BringForward()
        {
            SetForegroundWindow(_hwnd);
            if (WaitForeground()) return true;

            // A background process may not steal foreground. Borrowing the current
            // foreground thread's input queue is the documented-by-folklore way round it.
            var current = GetForegroundWindow();
            var ours = GetCurrentThreadId();
            var theirs = GetWindowThreadProcessId(current, IntPtr.Zero);
            if (theirs == 0 || theirs == ours) return false;

            AttachThreadInput(ours, theirs, true);
            try
            {
                SetForegroundWindow(_hwnd);
                return WaitForeground();
            }
            finally
            {
                AttachThreadInput(ours, theirs, false);
            }
        }

        private bool WaitForeground()
        {
            var deadline = Environment.TickCount64 + (long)ForegroundWait.TotalMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                if (GetForegroundWindow() == _hwnd) return true;
                Thread.Sleep(ForegroundPoll);
            }

            return GetForegroundWindow() == _hwnd;
        }
    }
}
```

- [ ] **Step 2: Build on Windows**

Run: `cd windows && dotnet build VoxScribe.sln`
Expected: green, no warnings about missing XML docs (private members need none). If the analyzer flags `CA1416` on a call, wrap the class in `[SupportedOSPlatform("windows")]` like the other platform classes in this project.

- [ ] **Step 3: Smoke it by hand (quick, full pass is Task 6)**

Run the app (`dotnet run --project src/VoxScribe.App`), click into Notepad, press the chord, click into a browser address bar, speak, release. Expected: Notepad comes forward and receives the text.

- [ ] **Step 4: Commit**

```bash
git add windows/src/VoxScribe.Platform.Windows/UiAutomationFocusAnchor.cs
git commit -F - <<'EOF'
windows: UI Automation focus anchor

Foreground window plus focused element captured at press, restored
before typing. Direct COM interop, because the self-contained publish
does not ship the WindowsDesktop framework.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 4: Shared settings helpers in `Panels` and window size tokens

**Files:**
- Modify: `windows/src/VoxScribe.App/Design/DesignTokens.cs` (new `Size` class after `Motion`, ~L345+)
- Modify: `windows/src/VoxScribe.App/Views/Panels.cs` (append helpers)
- Test: `windows/tests/VoxScribe.App.Tests/UiTests.cs`

**Interfaces:**
- Produces: `Tokens.Size.SettingsWidth = 540`, `SettingsHeight = 720`, `SettingsMinWidth = 480`, `SettingsMinHeight = 480`; `Panels.Section(string label, Control content) → BrushedPanel`; `Panels.Note(string) → TextBlock`; `Panels.Field(string hint, string? value, Action<string?> onCommit) → TextBox`; `Panels.Toggle(string label, bool value, Action<bool> onChange, string? hint = null) → CheckBox`.

These are copies of the private helpers currently in `SettingsWindow.cs` (`Section` L565, `Note` L594, `Field` L498, `Toggle` L602). Task 5 deletes the originals; until then both exist and nothing conflicts because the originals are private.

- [ ] **Step 1: Write the failing test**

In `windows/tests/VoxScribe.App.Tests/UiTests.cs`, add a class:

```csharp
/// <summary>The shared settings furniture.</summary>
public sealed class PanelsTests
{
    [AvaloniaFact]
    public void Toggle_with_hint_shows_the_hint_under_the_label()
    {
        var toggle = Panels.Toggle("Do the thing", true, _ => { }, hint: "Because reasons.");

        var content = toggle.Content.ShouldBeOfType<StackPanel>();
        content.Children.Count.ShouldBe(2);
        content.Children[0].ShouldBeOfType<TextBlock>().Text.ShouldBe("Do the thing");
        content.Children[1].ShouldBeOfType<TextBlock>().Text.ShouldBe("Because reasons.");
    }

    [AvaloniaFact]
    public void Toggle_without_hint_is_a_single_label()
    {
        var toggle = Panels.Toggle("Do the thing", false, _ => { });

        toggle.Content.ShouldBeOfType<TextBlock>().Text.ShouldBe("Do the thing");
    }

    [AvaloniaFact]
    public void Toggle_reports_changes()
    {
        bool? seen = null;
        var toggle = Panels.Toggle("Do the thing", false, v => seen = v);

        toggle.IsChecked = true;

        seen.ShouldBe(true);
    }
}
```

`Panels` is `internal`; check `VoxScribe.App.csproj` for an existing `InternalsVisibleTo` for `VoxScribe.App.Tests` (the `EquipmentTests` already reach internal controls if so). If absent, add to the App csproj:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="VoxScribe.App.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter PanelsTests`
Expected: build error `'Panels' does not contain a definition for 'Toggle'`.

- [ ] **Step 3: Add the tokens**

In `DesignTokens.cs`, after the `Motion` class:

```csharp
    /// <summary>Window sizes.</summary>
    public static class Size
    {
        /// <summary>Settings window, initial width.</summary>
        public const double SettingsWidth = 540;

        /// <summary>Settings window, initial height — under a laptop screen, so it scrolls.</summary>
        public const double SettingsHeight = 720;

        /// <summary>Narrowest the settings window may be dragged.</summary>
        public const double SettingsMinWidth = 480;

        /// <summary>Shortest the settings window may be dragged.</summary>
        public const double SettingsMinHeight = 480;
    }
```

- [ ] **Step 4: Add the helpers to `Panels`**

Append inside `Panels` (add `using Avalonia.Animation;` and `using VoxScribe.App.Design;` if not present):

```csharp
    /// <summary>A settings section: silkscreen title over content on a brushed panel, fading in.</summary>
    public static BrushedPanel Section(string label, Control content)
    {
        var section = new BrushedPanel
        {
            Opacity = 0.8, // Start slightly faded
            Child = new StackPanel
            {
                Margin = new Thickness(Tokens.Space.Roomy),
                Spacing = Tokens.Space.Base,
                Children = { new Silkscreen { Text = label, IsLarge = true }, content },
            },
        };

        var transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = Tokens.Motion.FadeIn },
        };
        section.Transitions = transitions;
        section.Loaded += (_, _) => section.Opacity = 1;

        return section;
    }

    /// <summary>Secondary explanatory copy.</summary>
    public static TextBlock Note(string text) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Label,
        Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A settings text box that persists on focus loss; empty saves as null.</summary>
    public static TextBox Field(string hint, string? value, Action<string?> onCommit)
    {
        var box = new TextBox
        {
            Text = value ?? string.Empty,
            Watermark = hint,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
        };

        box.LostFocus += (_, _) =>
        {
            var text = box.Text?.Trim();
            onCommit(string.IsNullOrEmpty(text) ? null : text);
        };

        return box;
    }

    /// <summary>A labelled check box, with an optional hint line beneath the label.</summary>
    public static CheckBox Toggle(string label, bool value, Action<bool> onChange, string? hint = null)
    {
        var title = new TextBlock
        {
            Text = label,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.Wrap,
        };

        var box = new CheckBox
        {
            IsChecked = value,
            Content = hint is null
                ? title
                : new StackPanel
                {
                    Spacing = Tokens.Space.Tight,
                    Children = { title, Note(hint) },
                },
        };

        box.IsCheckedChanged += (_, _) => onChange(box.IsChecked ?? false);
        return box;
    }
```

`Section` and `Toggle` are verbatim moves of the `SettingsWindow` originals apart from the hint and `TextWrapping`; the `Opacity = 0.8` literal was already there and is a ratio, not a token — leave it.

- [ ] **Step 5: Run the tests**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter PanelsTests`
Expected: 3 pass. Then the full suite: green (`DesignSystemTests` must still pass — if it flags the `0.8`, move it to `Tokens.Material` as `SectionRestingOpacity` and reference it).

- [ ] **Step 6: Commit**

```bash
git add windows/src/VoxScribe.App/Design/DesignTokens.cs windows/src/VoxScribe.App/Views/Panels.cs windows/tests/VoxScribe.App.Tests/UiTests.cs windows/src/VoxScribe.App/VoxScribe.App.csproj
git commit -F - <<'EOF'
windows: shared settings helpers in Panels, window size tokens

Toggle gains an optional hint line.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 5: Split, regroup, scroll and resize the settings window

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/SettingsWindow.cs` (becomes the shell; ~619 → ~200 lines)
- Create: `windows/src/VoxScribe.App/Views/Settings/ShortcutsSection.cs`
- Create: `windows/src/VoxScribe.App/Views/Settings/TypingSection.cs`
- Create: `windows/src/VoxScribe.App/Views/Settings/CleanupSection.cs`
- Create: `windows/src/VoxScribe.App/Views/Settings/SpeechSection.cs`
- Create: `windows/src/VoxScribe.App/Views/Settings/GeneralSection.cs`
- Create: `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs`
- Create: `windows/src/VoxScribe.App/Views/Settings/ConnectionTester.cs`
- Test: `windows/tests/VoxScribe.App.Tests/UiTests.cs`

**Interfaces:**
- Consumes: `SettingsData.AnchorFocus` (Task 1); `Panels.Section/Note/Field/Toggle`, `Tokens.Size.*` (Task 4).
- Produces: `internal static class XxxSection { public static Control Build(AppSettings settings, Action<SettingsData> save) }` for Typing, Cleanup, Speech, General, Appearance; `ShortcutsSection.Build(AppSettings settings, Action<SettingsData> save, TransportKey raw, TransportKey cleanup, TextBlock warning)`; `ConnectionTester.Build(Func<(string? Endpoint, string Model, string? Key)> read) → StackPanel`.

- [ ] **Step 1: Write the failing window test**

In `UiTests.cs`, add:

```csharp
/// <summary>The settings window as a whole.</summary>
public sealed class SettingsWindowTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"voxscribe-settings-{Guid.NewGuid():N}.json");

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [AvaloniaFact]
    public void Opens_resizable_and_scrollable_with_sections_in_order()
    {
        var window = new SettingsWindow(new AppSettings(_path));
        window.Show();

        window.CanResize.ShouldBeTrue();
        window.Bounds.Width.ShouldBeGreaterThan(0);
        window.Content.ShouldBeOfType<ScrollViewer>();

        var labels = window.GetVisualDescendants()
            .OfType<Silkscreen>()
            .Where(s => s.IsLarge)
            .Select(s => s.Text)
            .ToArray();

        labels.ShouldBe(["SHORTCUTS", "TYPING", "CLEANUP", "SPEECH", "GENERAL", "APPEARANCE"]);
    }
}
```

Add `using Avalonia.VisualTree;`, `using VoxScribe.Core;`, `using System.IO;` at the top if missing.

- [ ] **Step 2: Run to verify it fails**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter SettingsWindowTests`
Expected: fails — `CanResize` is false and the labels are `PUSH TO TALK, APPEARANCE, MICROPHONE, MODEL, REMOTE SERVER, CLEANUP, BEHAVIOUR`.

- [ ] **Step 3: Extract `ConnectionTester`**

Create `Views/Settings/ConnectionTester.cs` and move the whole `ConnectionTester` method (currently `SettingsWindow.cs` L404–~495, together with any private helper it alone uses, such as the HTTP probe) into it unchanged, as:

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Settings;

/// <summary>
/// The TEST CONNECTION row: button, lamp and verdict, reading its endpoint fresh on each
/// click so both the transcription and cleanup sections share one implementation.
/// </summary>
internal static class ConnectionTester
{
    public static StackPanel Build(Func<(string? Endpoint, string Model, string? Key)> read)
    {
        // …body verbatim from SettingsWindow.ConnectionTester, with Note(…) → Panels.Note(…)
    }
}
```

- [ ] **Step 4: Create the six section files**

Each file: `namespace VoxScribe.App.Views.Settings;`, usings `Avalonia.Controls`, `Avalonia.Layout`, `Avalonia.Media`, `VoxScribe.App.Controls`, `VoxScribe.App.Design`, `VoxScribe.Core` (plus `VoxScribe.Speech` for Speech). Bodies are the existing builders with `_settings` → `settings`, `Save(` → `save(`, `Note(` → `Panels.Note(`, `Field(` → `Panels.Field(`, `Toggle(` → `Panels.Toggle(`, `Labeled(` → `Panels.Labelled(`, `ConnectionTester(` → `ConnectionTester.Build(`.

`ShortcutsSection.cs` (content of the old PUSH TO TALK section, L196–216):

```csharp
/// <summary>The two chords and toggle mode. The recorder buttons belong to the window.</summary>
internal static class ShortcutsSection
{
    public static Control Build(
        AppSettings settings, Action<SettingsData> save,
        TransportKey raw, TransportKey cleanup, TextBlock warning) =>
        Panels.Section("SHORTCUTS", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                raw,
                warning,
                Panels.Note("Click, then press the key — or hold several keys together for a "
                    + "combination; releasing them records it. Escape cancels. The new "
                    + "shortcut works immediately: hold it anywhere to dictate."),
                cleanup,
                Panels.Note("Second shortcut. It records the same way, but sends the "
                    + "transcript through the cleanup model before typing it. The first "
                    + "shortcut stays raw and fast. Escape on this one unbinds it. "
                    + "Binding it for the first time needs a restart."),
                Panels.Toggle("Toggle mode — press once to start, press again to stop",
                    settings.Data.PushToTalkToggle,
                    v => save(settings.Data with { PushToTalkToggle = v })),
            },
        });
}
```

`TypingSection.cs`:

```csharp
/// <summary>Where and when the transcript is typed.</summary>
internal static class TypingSection
{
    public static Control Build(AppSettings settings, Action<SettingsData> save) =>
        Panels.Section("TYPING", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Toggle("Type transcripts into the focused app", settings.Data.InjectText,
                    v => save(settings.Data with { InjectText = v })),
                Panels.Toggle("Type into the field that had focus when you pressed the shortcut",
                    settings.Data.AnchorFocus,
                    v => save(settings.Data with { AnchorFocus = v }),
                    hint: "You can switch windows or click elsewhere while speaking. On release "
                        + "Vox-Scribe brings that field back and types there."),
                Panels.Toggle("Type each phrase as you speak it, not all at the end (raw only)",
                    settings.Data.IncrementalInjection,
                    v => save(settings.Data with { IncrementalInjection = v }),
                    hint: "While the option above is on, phrases are held and typed together on "
                        + "release, so nothing lands in the wrong window."),
            },
        });
}
```

`CleanupSection.cs`: `Build(settings, save)` returning `Panels.Section("CLEANUP", <old BuildCleanupSection body>)`.

`SpeechSection.cs`: the old Microphone, Model and Remote builders become three private static methods `Microphone(settings, save)`, `Model()`, `Remote(settings, save)` (bodies verbatim), and:

```csharp
    public static Control Build(AppSettings settings, Action<SettingsData> save) =>
        Panels.Section("SPEECH", new StackPanel
        {
            Spacing = Tokens.Space.Base,
            Children =
            {
                Panels.Labelled("MICROPHONE", Microphone(settings, save)),
                Panels.Labelled("MODEL", Model()),
                Panels.Labelled("REMOTE SERVER", Remote(settings, save)),
            },
        });
```

`GeneralSection.cs`:

```csharp
/// <summary>History and start-up.</summary>
internal static class GeneralSection
{
    public static Control Build(AppSettings settings, Action<SettingsData> save) =>
        Panels.Section("GENERAL", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Toggle("Keep a transcript history", settings.Data.KeepHistory,
                    v => save(settings.Data with { KeepHistory = v })),
                Panels.Toggle("Start Vox-Scribe when I log in, minimised to the tray",
                    PlatformFactory.IsLaunchAtLoginEnabled(),
                    PlatformFactory.SetLaunchAtLogin),
            },
        });
}
```

`AppearanceSection.cs`: `Build(settings, save)` returning `Panels.Section("APPEARANCE", <old BuildAppearanceSection body>)`, with `AccentChoices` moved in as a private static field.

- [ ] **Step 5: Reduce `SettingsWindow` to the shell**

Keep: the fields, the constructor's button/warning setup, `OnClosed`, `StartRecording`, `OnRecordedKey`, `Recording`, `CommitRecording`, `CancelRecording`, `ShowCleanupChord`, `ShowChord`, `ChordLabel`, `Save`. Delete: `BuildContent`, every `Build*Section`, `ConnectionTester`, `Field`, `Labeled`, `Section`, `Note`, `Toggle`, `AccentChoices`, and the now-unused usings (`System.Net.Http*`, `System.Text.Json`, `Avalonia.Animation`, `VoxScribe.Speech`). Add `using VoxScribe.App.Views.Settings;`.

Constructor window setup becomes:

```csharp
        Title = "Vox-Scribe Settings";
        Width = Tokens.Size.SettingsWidth;
        Height = Tokens.Size.SettingsHeight;
        MinWidth = Tokens.Size.SettingsMinWidth;
        MinHeight = Tokens.Size.SettingsMinHeight;
        SizeToContent = SizeToContent.Manual;
        CanResize = true;
        Background = Tokens.Brushes.Chassis;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
```

and, replacing `Content = BuildContent();`:

```csharp
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(Tokens.Space.Panel),
                Spacing = Tokens.Space.Wide,
                Children =
                {
                    ShortcutsSection.Build(_settings, Save, _hotkeyButton, _cleanupHotkeyButton, _keyWarning),
                    TypingSection.Build(_settings, Save),
                    CleanupSection.Build(_settings, Save),
                    SpeechSection.Build(_settings, Save),
                    GeneralSection.Build(_settings, Save),
                    AppearanceSection.Build(_settings, Save),
                },
            },
        };
```

Update the class doc comment to `/// <summary>Settings: shortcuts, typing, cleanup, speech, general, appearance.</summary>`.

- [ ] **Step 6: Run the tests**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf`
Expected: green, including `SettingsWindowTests` and `DesignSystemTests` (no literals crept into the new files — `30` in the appearance swatches was already there; if `DesignSystemTests` flags it, replace with a `Tokens.Size.Swatch = 30` token).

- [ ] **Step 7: Look at it (Windows)**

Run: `cd windows && dotnet run --project src/VoxScribe.App`, open Settings from the rail gear. Expected: window opens at 540×720, drags larger and smaller down to 480×480, scrolls vertically, six sections in order, both hints visible under their toggles, chord recording still works on both buttons.

- [ ] **Step 8: Commit**

```bash
git add windows/src/VoxScribe.App/Views/SettingsWindow.cs windows/src/VoxScribe.App/Views/Settings/ windows/tests/VoxScribe.App.Tests/UiTests.cs
git commit -F - <<'EOF'
windows: settings window resizable, scrollable, one file per section

Sections regrouped: Shortcuts, Typing (with the new focus-anchor toggle
and hints), Cleanup, Speech, General, Appearance.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 6: Manual Windows verification and docs

**Files:**
- Modify: `AGENTS.md` (add to "Things that look like bugs and are not")
- Modify: `windows/README.md` (feature list, if it has one)

This task needs a real Windows session with a microphone. It is the only place the Windows anchor is proven.

- [ ] **Step 1: Publish and install**

```bash
cd windows && dotnet publish src/VoxScribe.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish && iscc installer/voxscribe.iss
```

Install the resulting `installer/Output/VoxScribe-Setup-*.exe` (the repo's usual reinstall-after-change loop).

- [ ] **Step 2: Manual matrix**

Every row: click into the *start* field, press the chord, do the *wander*, speak "the quick brown fox", release.

| Start field | Wander | Expected |
|---|---|---|
| Notepad | Alt-Tab to a browser, click its address bar | Notepad comes forward, text at its caret |
| Browser text area (Chromium) | Click a different text box in the same page | Original text area gets the text |
| VS Code editor | Click the terminal panel in the same window | Editor gets the text, not the terminal |
| VoxScribe's own dictionary editor | Click the desktop | Dictionary field gets the text |
| Notepad, then **close Notepad** while speaking | — | Text goes to whatever has focus; no crash, no hang |
| Notepad, setting **off** | Click a browser field | Text goes to the browser field (old behaviour) |
| Notepad, incremental **on**, anchor **on** | Click a browser field | Nothing typed while speaking; whole text in Notepad at release |
| Notepad, cleanup chord | Click a browser field | Cleaned text in Notepad |
| An elevated Notepad (run as admin) | Click a browser field | Text goes to the browser field; documented limitation |

Record any row that fails in the PR description; a failing foreground step points at `BringForward` (try increasing `ForegroundWait` before anything else).

- [ ] **Step 3: Document the non-bug**

In `AGENTS.md`, under "Things that look like bugs and are not", add:

```markdown
**Incremental typing goes quiet when focus anchoring is on.** By design: with anchoring
the phrases are held and typed together at release, so they land in the anchored field and
not in whatever the user was clicking on. Turn anchoring off in Settings › Typing to get
phrase-by-phrase typing back. The pill badge reports what will actually happen.

**`UiAutomationFocusAnchor` declares only a prefix of the COM vtables.** Intentional — only
`GetFocusedElement` and `SetFocus` are called, and a prefix is valid for `[ComImport]`. Do
not "complete" the interfaces, and do not replace them with `System.Windows.Automation`:
that lives in the WindowsDesktop framework, which the self-contained publish does not ship.
```

- [ ] **Step 4: Commit**

```bash
git add AGENTS.md windows/README.md
git commit -F - <<'EOF'
docs: focus anchoring — behaviour notes and manual test matrix

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

## Done means

- `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` green; `dotnet build VoxScribe.sln` green on Windows.
- Every row of the Task 6 matrix behaves as stated, except the elevated row which is a documented limitation.
- `SettingsWindow.cs` is under ~220 lines, no `Build*Section` methods remain in it.
- No new NuGet package, no `UseWPF`, no literal sizes in views.
