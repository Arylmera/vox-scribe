# Phases 2–4: Execution Roadmap

**Status:** Phase 1 complete ✅. Phase 2 started (accessibility baseline). Phases 2–4 ready for continuation.

## Phase 2: Design & Accessibility (~6 hours)

### 2.1 ✅ Accessibility Baseline — Appearance Settings
- **Done:** AutomationProperties on theme buttons, apply button, accent swatches
- **Commit:** `4e56f53`
- **Impact:** Screen readers now discover controls in Appearance section

### 2.2 TODO: Accessibility on MainWindow Rail (30 min)
**File:** `windows/src/VoxScribe.App/Views/MainWindow.cs` (line ~48–60)

Add AutomationProperties to rail navigation keys:
```csharp
var transcriptionsKey = new RailKey(WaveIcon);
Avalonia.Automation.AutomationProperties.SetName(transcriptionsKey, "Transcriptions");
Avalonia.Automation.AutomationProperties.SetHelpText(transcriptionsKey, "View dictation history and transcripts");

var dictionaryKey = new RailKey(BookIcon);
Avalonia.Automation.AutomationProperties.SetName(dictionaryKey, "Dictionary");
Avalonia.Automation.AutomationProperties.SetHelpText(dictionaryKey, "Manage autocorrect rules");

var settingsKey = new RailKey(GearIcon);
Avalonia.Automation.AutomationProperties.SetName(settingsKey, "Settings");
Avalonia.Automation.AutomationProperties.SetHelpText(settingsKey, "Configure hotkeys, cleanup, appearance");
```

### 2.3 TODO: Record Button Accessibility (15 min)
**File:** `windows/src/VoxScribe.App/Views/MainWindow.cs` (line ~95)

```csharp
_recordKey = new RecordButton();
Avalonia.Automation.AutomationProperties.SetName(_recordKey, "Push to Talk");
Avalonia.Automation.AutomationProperties.SetHelpText(_recordKey, "Hold to record dictation. Release to transcribe and inject");
```

### 2.4 TODO: Contrast Audit (1 hour)
**File:** `windows/src/VoxScribe.App/Design/DesignTokens.cs`

- Measure contrast ratios for all color pairs against background (`#0A0D12`)
- Verify WCAG AA compliance (4.5:1 minimum for text)
- Document minimum values in token comments
- Add CI check: `tests/ContrastChecker.cs` (simple tool)

**Status markers:**
- `InkOnDeck` (#FFFFFF) vs background: ✓ likely PASS
- `InkOnDeck` (#E9EDF2) vs background: ✓ likely PASS
- `Silkscreen` (#8B96A5) vs background: ⚠ CHECK (marginal)
- `Seam` (#2C3544) vs background: ⚠ CHECK (low contrast, outline use only)

### 2.5 TODO: Settings Keyboard Navigation (1.5 hours)
**Files:** `Settings/*.cs`

Add keyboard navigation to Settings tabs:
- Tab between sections
- Arrow keys within lists (dictionary, transcripts)
- Enter to activate controls
- Use Avalonia's `KeyboardNavigation.TabNavigation`

Simple approach: `KeyboardNavigation.SetTabNavigation(StackPanel, KeyboardNavigationMode.Cycle)`

### 2.6 TODO: Enhanced Dictation Feedback (30 min)
**File:** `windows/src/VoxScribe.App/Views/HudWindow.cs`

Add UI feedback during processing:
- Show "Processing..." status on pill while cleanup runs
- Display latency (ms) after text appears
- Add configurable preview character limit (expose as token)

---

## Phase 3: Code Structure — DictationEngine Refactoring (~6 hours)

### Goal
Extract state machines and dependencies from 882-line `DictationEngine` into focused components.

### 3.1 TODO: Extract FocusCapture Logic (2 hours)

**Create:** `windows/src/VoxScribe.Core/FocusCapture.cs`

Move anchor logic into separate class:
```csharp
internal sealed class FocusCapture
{
    private readonly IFocusAnchor? _anchor;
    private Task<IFocusTarget?>? _capture;
    private bool _requested;
    private bool _anchoredThisUtterance;

    public bool IsRequested => _requested;
    public bool WasAnchored => _anchoredThisUtterance;

    public FocusCapture(IFocusAnchor? anchor) { _anchor = anchor; }

    public void RequestOnNext() => _requested = true;
    
    public async Task<IFocusTarget?> CaptureAsync(CancellationToken ct)
    {
        if (!_requested || _anchor is null) return null;
        _capture = _anchor.CaptureAsync(ct);
        return await _capture;
    }

    public void ResetState()
    {
        _requested = false;
        _anchoredThisUtterance = false;
    }
}
```

**Update DictationEngine:**
- Replace `_focusAnchor`, `_anchorCapture`, `_anchoredThisUtterance`, `_anchorRequested` fields with `FocusCapture _focusCapture`
- Update press/release logic to delegate to `_focusCapture`
- Tests: Inject FocusCapture directly; easier to verify anchor behavior in isolation

### 3.2 TODO: Extract InjectionCoordinator (2 hours)

Move injection sequencing logic:
```csharp
internal sealed class InjectionCoordinator
{
    private readonly ITextInjector _injector;
    private readonly IClock _clock;
    private Task _inject = Task.CompletedTask;

    public InjectionCoordinator(ITextInjector injector, IClock clock) { ... }

    public async Task InjectAsync(string text, bool cleanup, CancellationToken ct)
    {
        var start = _clock.Now;
        await _inject; // Wait for prior injection
        _inject = _injector.InjectAsync(text, ct);
        await _inject;
        var elapsed = _clock.Now - start;
        // Report timing
    }
}
```

### 3.3 TODO: Update DictationEngine to Use Coordinators (1 hour)

Replace inline orchestration:
- `State` transitions become clearer (Recording → Transcribing → Idle)
- Injection and focus capture are independent testable components
- Engine facade becomes ~300 lines (was 882)

**Verification:** All 139 Core tests still pass

### 3.4 TODO: Add DictationEngine State Machine Tests (1 hour)

New file: `windows/tests/VoxScribe.Core.Tests/DictationEngineStateTests.cs`

Test scenarios:
- Press → Recording, Release → Transcribing, Complete → Idle
- Double-press during transcribing (no-op)
- Cancel mid-recording clears queued segments
- Focus capture requested on press, resolved on release

---

## Phase 4: UI Refactoring (~8 hours)

### Goal
Extract state logic from large view files (MainWindow 578 lines, HudWindow 523 lines) into ViewModels.

### 4.1 TODO: MainWindowViewModel Skeleton (2 hours)

**Create:** `windows/src/VoxScribe.App/ViewModels/MainWindowViewModel.cs`

```csharp
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly DictationEngine _engine;
    private readonly AppSettings _settings;

    public bool IsRecording => _engine.State == DictationState.Recording;
    public string PartialText => _engine.PartialText;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel(DictationEngine engine, AppSettings settings)
    {
        _engine = engine;
        _settings = settings;
        _engine.Changed += (_, _) => OnPropertyChanged(nameof(IsRecording), nameof(PartialText));
    }

    public void ShowSettings() { ... }
    public void ShowTranscriptions() { ... }
}
```

Bind View properties to ViewModel:
- `RecordKey.IsEnabled = !ViewModel.IsRecording`
- `PartialText.Text = ViewModel.PartialText`
- Reduces MainWindow.cs by 150+ lines

### 4.2 TODO: HudWindowViewModel (2 hours)

Similar pattern for HUD (recording indicator, level display, partial text):
```csharp
public sealed class HudWindowViewModel : INotifyPropertyChanged
{
    public bool IsRecording { ... }
    public float AudioLevel { ... }
    public string DisplayText { ... }
    public Visibility PillVisibility { ... }
}
```

### 4.3 TODO: Extract Settings Layout Logic (2 hours)

Move Settings content building into `SettingsViewModel`:
```csharp
public sealed class SettingsViewModel
{
    public ObservableCollection<SettingSection> Sections { get; }
    // Binds to ItemsControl in view
}

public record SettingSection(string Title, Control Content);
```

Reduces `SettingsWindow.cs` boilerplate.

### 4.4 TODO: Bind Views to ViewModels (2 hours)

Wire up bindings:
- MainWindow: `DataContext = new MainWindowViewModel(_composition.Engine, _composition.Settings)`
- HudWindow: `DataContext = new HudWindowViewModel(_composition.Engine)`
- Run existing tests; verify no regressions

**Verification:** All 31 UI tests pass, MainWindow/HudWindow <300 lines each

---

## Execution Priority

1. **Phase 2.1–2.2** (1h): Quick accessibility wins — enable screen readers
2. **Phase 2.3–2.4** (1.5h): Contrast + keyboard nav baseline
3. **Phase 3.1–3.3** (4h): DictationEngine extract — unblocks testing
4. **Phase 4.1–4.3** (6h): ViewModel extraction — improves clarity

**Total:** ~12.5 hours to complete all phases beyond Phase 1

### Testing Strategy

- Run `dotnet test VoxScribe.CrossPlatform.slnf` after each phase
- Expect all 194 tests to pass (new tests added, old ones persist)
- CI will auto-build on push

### Release Cadence

- **v1.0.1** ✅ Live (Phase 1)
- **v1.0.2** → After Phase 2 (accessibility + feedback improvements)
- **v1.1.0** → After Phase 3–4 (refactoring + architecture improvements)

---

## Success Criteria

- ✅ Screen reader discovers all Settings controls
- ✅ WCAG contrast compliance verified
- ✅ DictationEngine <400 lines, focused responsibilities
- ✅ MainWindow/HudWindow <300 lines each
- ✅ All 194+ tests pass
- ✅ No behavioral regressions
