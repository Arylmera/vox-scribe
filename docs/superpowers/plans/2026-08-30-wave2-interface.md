# Wave 2 — Interface & Information Access Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox ('- [ ]') syntax for tracking.

**Goal:** Make failure states, live transcription, usage stats, and stored history visible and actionable — the app must never start fine and silently do nothing.

**Architecture:** A new `AppHealth` model in `VoxScribe.Core` collects named part statuses (fed by `Composition` from existing components) and is rendered by a new `HealthPanel` in the MainWindow content column and an amber badge on the dictation pill. Stats (`TranscriptStats`) and export (`TranscriptMarkdown`) are pure Core functions over `TranscriptStore` records; a `Cleaned` flag flows from `DictationEngine` through `TranscriptRecord` to power the raw/clean split. Views stay thin, token-driven, and are tested headlessly.

**Tech Stack:** .NET 10, Avalonia 11.3 (headless tests via Avalonia.Headless.XUnit 11.3.20), xUnit v2 + Shouldly, System.Text.Json source-gen, existing fakes in `VoxScribe.Testing`.

## Global Constraints

- .NET 10, Avalonia UI; build/test with: cd windows && dotnet test VoxScribe.CrossPlatform.slnf
- NAudio pinned 2.3.0; Avalonia.Headless.XUnit pinned 11.3.20; org.k2fsa.sherpa.onnx pinned 1.13.5 (never reference Microsoft.ML.OnnxRuntime)
- VoxScribe.Platform.Windows stays logic-free; all logic in platform-neutral projects behind interfaces (net10.0, CA1416 guards)
- Views must not contain literal values — every colour/size/radius/duration comes from Design/DesignTokens.cs; add tokens rather than inlining
- Red means recording, nothing else is red; amber/green are instrumentation only (UiTests.cs pins this)
- shared/dictionary-test-vectors.json is the spec for correction behaviour: change vectors first, watch red, then make green
- Dictionary regexes stay in the ICU/.NET safe subset, RegexOptions.CultureInvariant, NFC normalization
- Anything touching PushToTalkHook or real injection must be flagged "manual test required"

**Wave ordering.** This plan assumes Wave 1 (`2026-08-30-wave1-quick-wins.md`) is merged. Wave 1 already touched three files this plan also edits, and its work must be preserved, not overwritten:

- `DictationResult` / `TranscriptRecord` / `Composition`'s record initializer gained `RawText` — see the overlap note in Task 5.
- `MainWindow.BuildVoiceBand()` gained an UNDO button in a fourth grid column; leave it in place when editing the window.
- `DictionaryView`'s constructor gained a `TranscriptStore` parameter for the suggestions section.

**Conventions used below:** test names are `Snake_case_sentences`, test classes are `sealed` with a doc comment saying why they exist, namespaces `VoxScribe.CoreTests` / `VoxScribe.AppTests`. All run commands are from the repo root. A test that references a not-yet-written type "fails" as a compile error (`CS0246`) — that is the expected red for the first step of each task.

---

### Task 1: AppHealth model in Core

**Files:**
- Create: `windows/src/VoxScribe.Core/AppHealth.cs`
- Create: `windows/tests/VoxScribe.Core.Tests/AppHealthTests.cs`

**Interfaces:**
- Produces: `public enum HealthStatus { Ok, Degraded, Failed }`
- Produces: `public sealed record HealthItem(string Part, HealthStatus Status, string Remedy)`
- Produces: `public sealed class AppHealth` with `void Report(string part, HealthStatus status, string remedy = "")`, `IReadOnlyList<HealthItem> Problems { get; }`, `HealthStatus Worst { get; }`, `event EventHandler? Changed`
- Produces: `public static class GatewayProbe` with `static Task<bool> CheckAsync(string baseUrl, CancellationToken cancellationToken)`
- Consumes: nothing platform-specific (plain net10.0)

**Steps:**

- [ ] Write the failing tests in `windows/tests/VoxScribe.Core.Tests/AppHealthTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;

namespace VoxScribe.CoreTests;

/// <summary>
/// The app must never start fine and silently do nothing: every failing part gets a name,
/// a status, and a remedy, and recovery clears it. This pins that contract.
/// </summary>
public sealed class AppHealthTests
{
    [Fact]
    public void Empty_health_is_ok()
    {
        var health = new AppHealth();

        health.Worst.ShouldBe(HealthStatus.Ok);
        health.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Failure_surfaces_part_and_remedy()
    {
        var health = new AppHealth();
        health.Report("Microphone", HealthStatus.Failed, "Plug in a microphone.");

        health.Worst.ShouldBe(HealthStatus.Failed);
        var item = health.Problems.ShouldHaveSingleItem();
        item.Part.ShouldBe("Microphone");
        item.Remedy.ShouldBe("Plug in a microphone.");
    }

    [Fact]
    public void Reporting_ok_clears_a_problem()
    {
        var health = new AppHealth();
        health.Report("Microphone", HealthStatus.Failed, "Plug in a microphone.");
        health.Report("Microphone", HealthStatus.Ok);

        health.Worst.ShouldBe(HealthStatus.Ok);
        health.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Failed_sorts_before_degraded()
    {
        var health = new AppHealth();
        health.Report("Cleanup gateway", HealthStatus.Degraded, "Checking…");
        health.Report("Speech model", HealthStatus.Failed, "Install the model.");

        health.Problems[0].Part.ShouldBe("Speech model");
        health.Problems[1].Part.ShouldBe("Cleanup gateway");
        health.Worst.ShouldBe(HealthStatus.Failed);
    }

    [Fact]
    public void Changed_fires_on_report()
    {
        var health = new AppHealth();
        var fired = 0;
        health.Changed += (_, _) => fired++;

        health.Report("Microphone", HealthStatus.Failed, "Plug in a microphone.");

        fired.ShouldBe(1);
    }

    [Fact]
    public async Task Gateway_probe_rejects_a_malformed_url_without_touching_the_network()
    {
        var reachable = await GatewayProbe.CheckAsync("not a url", CancellationToken.None);

        reachable.ShouldBeFalse();
    }
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~AppHealthTests"` — expect compile failure: `error CS0246: The type or namespace name 'AppHealth' could not be found`.
- [ ] Implement `windows/src/VoxScribe.Core/AppHealth.cs`:

```csharp
namespace VoxScribe.Core;

/// <summary>How healthy one part of the app is.</summary>
public enum HealthStatus
{
    /// <summary>Working.</summary>
    Ok,

    /// <summary>Working with a caveat, or still being checked.</summary>
    Degraded,

    /// <summary>Not working; the app cannot do its job through this part.</summary>
    Failed,
}

/// <summary>One part's status: its name, how it is, and what to do about it.</summary>
public sealed record HealthItem(string Part, HealthStatus Status, string Remedy);

/// <summary>
/// The app's health board. Startup wiring and background probes report named parts here;
/// the main window and the dictation pill render what they find.
/// </summary>
/// <remarks>
/// Exists because the app can start fine and silently do nothing — no mic, no model, no
/// platform layer, unreachable gateway. Every failure must name the part and a remedy.
/// Thread-safe: probes report from worker threads while the UI reads snapshots.
/// </remarks>
public sealed class AppHealth
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HealthItem> _items = new(StringComparer.Ordinal);

    /// <summary>Raised whenever any part's status changes. May fire on a worker thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Sets (or replaces) the status of one named part.</summary>
    public void Report(string part, HealthStatus status, string remedy = "")
    {
        lock (_lock)
        {
            _items[part] = new HealthItem(part, status, remedy);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Everything that is not Ok, worst first. A snapshot — safe to iterate.</summary>
    public IReadOnlyList<HealthItem> Problems
    {
        get
        {
            lock (_lock)
            {
                return [.. _items.Values
                    .Where(i => i.Status != HealthStatus.Ok)
                    .OrderByDescending(i => i.Status)
                    .ThenBy(i => i.Part, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>The worst status on the board; Ok when nothing was reported.</summary>
    public HealthStatus Worst
    {
        get
        {
            lock (_lock)
            {
                return _items.Count == 0 ? HealthStatus.Ok : _items.Values.Max(i => i.Status);
            }
        }
    }
}

/// <summary>
/// Answers "is that gateway reachable at all?" for a configured remote endpoint.
/// </summary>
/// <remarks>
/// Any HTTP response counts as reachable — LiteLLM answers 404 on its base URL and that
/// still proves the machine is up. Only a transport failure (refused, timeout, DNS) or a
/// malformed URL is unreachable.
/// </remarks>
public static class GatewayProbe
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>True when <paramref name="baseUrl"/> answered anything over HTTP.</summary>
    public static async Task<bool> CheckAsync(string baseUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;

        try
        {
            using var response = await Http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~AppHealthTests"` — expect 6 passed.
- [ ] Run the full suite: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green.
- [ ] Commit: `git add -A && git commit -m "core: AppHealth board and gateway probe"`

---

### Task 2: Composition feeds AppHealth

**Files:**
- Modify: `windows/src/VoxScribe.App/Composition.cs`

**Interfaces:**
- Produces: `public AppHealth Health { get; }` on `Composition`
- Consumes: `AppHealth.Report(string, HealthStatus, string)`, `GatewayProbe.CheckAsync(string, CancellationToken)` (Task 1); `PlatformFactory.ListCaptureDevices()` returning `KeyValuePair<string,string>[]`; `UnavailableTranscriber` (already in this file)

This is wiring only — no new logic beyond calling Task 1's Core code, so no new unit test; the UI tests in Task 3 exercise the rendered result, and the existing suite pins that Composition still builds.

**Steps:**

- [ ] In `Composition.cs`, extend the private constructor and property list. The constructor gains one parameter and one assignment (keep every existing parameter as-is):

```csharp
    private Composition(
        AppSettings settings,
        DictionaryFile dictionary,
        TranscriptStore transcripts,
        DictationEngine? engine,
        bool platformAvailable,
        AppHealth health)
    {
        Settings = settings;
        Dictionary = dictionary;
        Transcripts = transcripts;
        Engine = engine;
        IsPlatformAvailable = platformAvailable;
        Health = health;
    }
```

  and add next to the other properties:

```csharp
    /// <summary>The health board every surface renders from.</summary>
    public AppHealth Health { get; }
```

- [ ] In `Composition.Create()`, right after `var transcripts = new TranscriptStore(TranscriptStore.DefaultPath);`, add:

```csharp
        var health = new AppHealth();
```

- [ ] After the line `var available = capture is not null && hotkey is not null && injector is not null;`, add the startup reports:

```csharp
        // On Windows a missing platform layer is a broken install; elsewhere it is normal.
        if (OperatingSystem.IsWindows() && !available)
        {
            health.Report(
                "Platform layer", HealthStatus.Failed,
                "VoxScribe.Platform.Windows.dll failed to load — reinstall Vox-Scribe.");
        }

        if (available && PlatformFactory.ListCaptureDevices().Length == 0)
        {
            health.Report(
                "Microphone", HealthStatus.Failed,
                "No capture device found — plug in a microphone or pick one in Settings.");
        }
```

- [ ] Inside the `if (available)` block, immediately after the `ITranscriber transcriber = ...` assignment, add:

```csharp
            if (transcriber is UnavailableTranscriber)
            {
                health.Report(
                    "Speech model", HealthStatus.Failed,
                    "Install the Parakeet model (docs/PARAKEET-WINDOWS.md) "
                    + "or set an STT endpoint in Settings.");
            }
```

- [ ] Still in `Create()`, just before `return new Composition(...)`, kick the gateway probes (fire-and-forget; results land on the board when they arrive):

```csharp
        if (settings.Data.SttEndpoint is { Length: > 0 } sttUrl)
        {
            _ = ProbeAsync(health, "Remote STT gateway", sttUrl,
                "Gateway unreachable — start it, or check SttEndpoint in Settings.");
        }

        if (settings.Data.CleanupEndpoint is { Length: > 0 } cleanupUrl)
        {
            _ = ProbeAsync(health, "Cleanup gateway", cleanupUrl,
                "Gateway unreachable — start it, or check CleanupEndpoint in Settings.");
        }
```

  and add the helper as a private static method on `Composition`:

```csharp
    /// <summary>Reports a configured gateway as checking, then as Ok or Failed.</summary>
    private static async Task ProbeAsync(AppHealth health, string part, string url, string remedy)
    {
        health.Report(part, HealthStatus.Degraded, "Checking…");
        var reachable = await GatewayProbe.CheckAsync(url, CancellationToken.None).ConfigureAwait(false);
        health.Report(part, reachable ? HealthStatus.Ok : HealthStatus.Failed, reachable ? "" : remedy);
    }
```

- [ ] Change the return statement to `return new Composition(settings, dictionary, transcripts, engine, available, health);`.
- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (no behaviour change visible to existing tests).
- [ ] Commit: `git add -A && git commit -m "windows: Composition feeds the AppHealth board"`

---

### Task 3: Health surfaces — HealthPanel in MainWindow, amber badge on the pill

**Files:**
- Create: `windows/src/VoxScribe.App/Views/HealthPanel.cs`
- Create: `windows/tests/VoxScribe.App.Tests/HealthPanelTests.cs`
- Modify: `windows/src/VoxScribe.App/Views/MainWindow.cs`
- Modify: `windows/src/VoxScribe.App/Views/HudWindow.cs`
- Modify: `windows/src/VoxScribe.App/App.axaml.cs`

**Interfaces:**
- Produces: `public sealed class HealthPanel : UserControl` with ctor `HealthPanel(AppHealth health)`, `IReadOnlyList<string> Lines { get; }` (exposed for headless tests), `static Avalonia.Media.Color LampColor { get; }`
- Produces: `public static string? HealthBadge(AppHealth? health)` on `HudWindow`
- Consumes: `AppHealth` / `HealthStatus` / `HealthItem` (Task 1), `Composition.Health` (Task 2), `Tokens.Colors.MeterAmber`, `Panels`, `Lamp`, `BrushedPanel`

The health colour is **amber** (`Tokens.Colors.MeterAmber`) everywhere — red stays reserved for recording, and a test pins that.

**Steps:**

- [ ] Write the failing tests in `windows/tests/VoxScribe.App.Tests/HealthPanelTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Shouldly;
using VoxScribe.App.Design;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

/// <summary>
/// Errors must name the failing part and a remedy, in amber — never red, which is reserved
/// for recording. The panel disappears entirely when everything works.
/// </summary>
public sealed class HealthPanelTests
{
    [AvaloniaFact]
    public void Hidden_when_everything_is_ok()
    {
        var panel = new HealthPanel(new AppHealth());

        panel.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Failure_names_part_and_remedy()
    {
        var health = new AppHealth();
        health.Report("Microphone", HealthStatus.Failed, "Plug in a microphone.");

        var panel = new HealthPanel(health);

        panel.IsVisible.ShouldBeTrue();
        panel.Lines.ShouldHaveSingleItem().ShouldBe("Microphone — Plug in a microphone.");
    }

    [AvaloniaFact]
    public void Recovery_hides_the_panel()
    {
        var health = new AppHealth();
        health.Report("Microphone", HealthStatus.Failed, "Plug in a microphone.");
        var panel = new HealthPanel(health);

        health.Report("Microphone", HealthStatus.Ok);
        Dispatcher.UIThread.RunJobs();

        panel.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Health_is_amber_never_red()
    {
        HealthPanel.LampColor.ShouldBe(Tokens.Colors.MeterAmber);
        HealthPanel.LampColor.ShouldNotBe(Tokens.Colors.Record);
    }

    [Fact]
    public void Pill_badge_names_the_worst_failed_part()
    {
        var health = new AppHealth();
        health.Report("Speech model", HealthStatus.Failed, "Install the model.");

        HudWindow.HealthBadge(health).ShouldBe("⚠ SPEECH MODEL");
    }

    [Fact]
    public void Pill_badge_is_null_when_healthy_or_absent()
    {
        var health = new AppHealth();
        health.Report("Cleanup gateway", HealthStatus.Degraded, "Checking…");

        HudWindow.HealthBadge(health).ShouldBeNull();
        HudWindow.HealthBadge(null).ShouldBeNull();
    }
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~HealthPanelTests"` — expect compile failure (`CS0246: 'HealthPanel'` and no `HealthBadge` on `HudWindow`).
- [ ] Implement `windows/src/VoxScribe.App/Views/HealthPanel.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// The health readout in the main window: one amber row per failing part, each naming the
/// part and its remedy. Collapses to nothing when everything works.
/// </summary>
/// <remarks>
/// Amber, deliberately: red means recording and nothing else in this app is red. Health
/// changes can arrive from worker threads (gateway probes), so refreshes are marshalled.
/// </remarks>
public sealed class HealthPanel : UserControl
{
    private readonly AppHealth _health;
    private readonly StackPanel _rows;

    /// <summary>The health colour. Exposed so tests can pin "amber, never red".</summary>
    public static Color LampColor => Tokens.Colors.MeterAmber;

    /// <summary>The rendered "Part — Remedy" lines. Exposed for headless tests.</summary>
    public IReadOnlyList<string> Lines { get; private set; } = [];

    /// <summary>Builds the panel over <paramref name="health"/> and starts watching it.</summary>
    public HealthPanel(AppHealth health)
    {
        _health = health;

        _rows = new StackPanel { Spacing = Tokens.Space.Snug, Margin = new Thickness(Tokens.Space.Base) };
        Content = new BrushedPanel { Child = _rows };

        _health.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        var problems = _health.Problems;

        IsVisible = problems.Count > 0;
        Lines = [.. problems.Select(p => $"{p.Part} — {p.Remedy}")];

        _rows.Children.Clear();
        foreach (var problem in problems)
        {
            _rows.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Tokens.Space.Base,
                Children =
                {
                    new Lamp
                    {
                        IsLit = problem.Status == HealthStatus.Failed,
                        LampColor = LampColor,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = $"{problem.Part} — {problem.Remedy}",
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Label,
                        Foreground = Tokens.Brushes.Ink,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            });
        }
    }
}
```

- [ ] In `MainWindow.cs`, replace the model banner with the health panel. In `BuildLayout()`, replace this block:

```csharp
        if (_composition is not null && !Composition.IsModelInstalled)
        {
            content.Children.Add(Panels.Docked(BuildModelBanner(), Dock.Top));
        }
```

  with:

```csharp
        if (_composition is not null)
        {
            content.Children.Add(Panels.Docked(new HealthPanel(_composition.Health)
            {
                Margin = new Thickness(
                    Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Base),
            }, Dock.Top));
        }
```

  Delete the now-unused `BuildModelBanner()` method (the missing-model case is a health item from Task 2). Leave `Composition.IsModelInstalled` in place — it is public API and Settings-side code may use it.
- [ ] In `MainWindow.cs`, surface a failed hotkey hook install. Replace the last constructor line:

```csharp
        if (_composition?.Engine is not null) _composition.Engine.Start();
```

  with:

```csharp
        if (_composition?.Engine is not null && !_composition.Engine.Start())
        {
            _composition.Health.Report(
                "Hotkey", HealthStatus.Failed,
                "The keyboard hook could not install — restart Vox-Scribe "
                + "or close software that grabs the keyboard.");
        }
```

  (add `using VoxScribe.Core;` if not already present — it is).
- [ ] In `HudWindow.cs`, add the health badge. Add a field and widen the constructor:

```csharp
    private readonly AppHealth? _health;
```

```csharp
    /// <summary>Builds the pill over <paramref name="engine"/> and starts watching it.</summary>
    public HudWindow(DictationEngine engine, AppHealth? health = null)
    {
        _engine = engine;
        _health = health;
```

  Add the public helper (below `ShowPreview`):

```csharp
    /// <summary>
    /// The warning shown in place of the mode label when a part has failed, or null when
    /// healthy. Amber, not red — red is recording. Exposed for tests.
    /// </summary>
    public static string? HealthBadge(AppHealth? health)
    {
        var failing = health?.Problems.FirstOrDefault(p => p.Status == HealthStatus.Failed);
        return failing is null ? null : "⚠ " + failing.Part.ToUpperInvariant();
    }
```

  In `Sync()`, immediately after the `ShowMode(_engine.CleaningThisUtterance, recording);` line, add:

```csharp
        if (HealthBadge(_health) is { } badge && _mode.Text != badge)
        {
            _mode.Text = badge;
            _mode.Foreground = new SolidColorBrush(Tokens.Colors.MeterAmber);
            _shown = null;   // forces ShowMode to repaint the normal readout once health recovers
        }
```

- [ ] In `App.axaml.cs`, pass the board to the pill:

```csharp
            if (_composition.Engine is not null) _ = new HudWindow(_composition.Engine, _composition.Health);
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~HealthPanelTests"` — expect 6 passed.
- [ ] Run the full suite: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (existing `MainWindowTests` still pass with the banner gone).
- [ ] **Manual test required** (real Windows): rename the model directory → amber "Speech model" row with remedy appears in the main window; press the hotkey → pill mode label shows `⚠ SPEECH MODEL` in amber, lamp behaviour otherwise unchanged. Restore the model directory afterwards.
- [ ] Commit: `git add -A && git commit -m "windows: health panel in the rail window, amber badge on the pill"`

---

### Task 4: Pin the pill's live partial-text preview

The preview already exists (`HudWindow.ShowPreview` polls `DictationEngine.PartialText` — the engine's partial-hypothesis surface is the `PartialText` property refreshed by the `Changed` event, and the pill's 33 ms poll reads it). What is missing is a test: the tail-trimming is inline and unpinned. Extract it as a pure function and pin it. No behaviour change; the shipped 110-character tail stays (the "~40 chars" in the scope predates this code — the helper is length-parameterised so either policy is one constant away).

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/HudWindow.cs`
- Create: `windows/tests/VoxScribe.App.Tests/HudPreviewTests.cs`

**Interfaces:**
- Produces: `public static string PreviewTail(string text, int characters)` on `HudWindow`
- Consumes: `DictationEngine.PartialText` (already consumed by `Sync()`)

**Steps:**

- [ ] Write the failing tests in `windows/tests/VoxScribe.App.Tests/HudPreviewTests.cs`:

```csharp
using Shouldly;
using VoxScribe.App.Views;

namespace VoxScribe.AppTests;

/// <summary>
/// The pill shows the tail of the live transcription on one line, ellipsis at the front —
/// text scrolls off the left, never grows the pill sideways. Pins the trimming rule.
/// </summary>
public sealed class HudPreviewTests
{
    [Fact]
    public void Short_text_is_untouched()
    {
        HudWindow.PreviewTail("hello world", 110).ShouldBe("hello world");
    }

    [Fact]
    public void Exact_length_text_is_untouched()
    {
        HudWindow.PreviewTail(new string('a', 40), 40).ShouldBe(new string('a', 40));
    }

    [Fact]
    public void Long_text_keeps_the_tail_with_a_leading_ellipsis()
    {
        var tail = HudWindow.PreviewTail(new string('a', 200) + " and the end", 40);

        tail.Length.ShouldBe(41);   // 40 kept characters plus the ellipsis
        tail.ShouldStartWith("…");
        tail.ShouldEndWith(" and the end");
    }

    [Fact]
    public void Empty_text_stays_empty()
    {
        HudWindow.PreviewTail(string.Empty, 110).ShouldBe(string.Empty);
    }
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~HudPreviewTests"` — expect compile failure (`'HudWindow' does not contain a definition for 'PreviewTail'`).
- [ ] In `HudWindow.cs`, add the pure helper and use it. Add below `ShowPreview`:

```csharp
    /// <summary>
    /// The tail of <paramref name="text"/> that fits the preview line: the last
    /// <paramref name="characters"/> characters with an ellipsis in front when older text
    /// scrolled off. Exposed for tests.
    /// </summary>
    public static string PreviewTail(string text, int characters) =>
        text.Length > characters ? "…" + text[^characters..] : text;
```

  and change the first statement of `ShowPreview` from:

```csharp
        var wanted = text.Length > PreviewCharacters
            ? "…" + text[^PreviewCharacters..]
            : text;
```

  to:

```csharp
        var wanted = PreviewTail(text, PreviewCharacters);
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~HudPreviewTests"` — expect 4 passed.
- [ ] Run the full suite: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green.
- [ ] Commit: `git add -A && git commit -m "windows: pin the pill preview tail as a pure function"`

---

### Task 5: Cleaned flag from engine to history

The dashboard's raw/clean split needs to know which dictations went through the cleanup pass. Today that fact dies inside `ProcessAsync`. Carry it on `DictationResult` and persist it on `TranscriptRecord` (nullable — old JSONL lines deserialize to `null`, which counts as raw).

**Files:**
- Modify: `windows/src/VoxScribe.Core/DictationEngine.cs`
- Modify: `windows/src/VoxScribe.Core/TranscriptStore.cs`
- Modify: `windows/src/VoxScribe.App/Composition.cs`
- Modify: `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs`

**Interfaces:**
- Produces: `public sealed record DictationResult(DateTimeOffset At, TimeSpan AudioDuration, TimeSpan ProcessingTime, string Text, IReadOnlyList<AppliedCorrection> Corrections, string? RawText = null, bool Cleaned = false)`
- Produces: `public bool? Cleaned { get; init; }` on `TranscriptRecord`

**Wave-1 overlap — read before editing.** Wave 1 already added a trailing optional positional `string? RawText = null` to `DictationResult` and a `public string? RawText { get; init; }` to `TranscriptRecord`, and already sets `RawText` in Composition's `TranscriptRecord` initializer. `Cleaned` goes **after** `RawText` in the positional list (a new parameter may only be appended, never inserted), and every code block below preserves the existing `RawText` lines. If `RawText` is absent from the file you are editing, Wave 1 was not merged — stop and merge it first.
- Consumes: `FakeAudioCapture`, `FakeHotkeySource`, `FakeTranscriber`, `RecordingTextInjector`, `FakeClock` from `VoxScribe.Testing`

**Steps:**

- [ ] Append the failing tests to `windows/tests/VoxScribe.Core.Tests/DictationEngineTests.cs` (inside the existing `DictationEngineTests` class, which already has the usings and the spin-wait style):

```csharp
    [Fact]
    public async Task Cleanup_hotkey_marks_the_result_cleaned()
    {
        var capture = new FakeAudioCapture(FakeAudioCapture.Tone(2));
        var plain = new FakeHotkeySource();
        var cleanup = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = new DictationEngine(
            capture, plain, new FakeTranscriber("hello world"), injector,
            () => [], new FakeClock(), cleanupHotkey: cleanup);
        engine.Cleanup = (text, _) => Task.FromResult(text.ToUpperInvariant());

        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        cleanup.Press();
        for (var i = 0; i < 2000 && engine.State != DictationState.Recording; i++) await Task.Yield();
        for (var i = 0; i < 20000 && engine.Level == 0; i++) await Task.Yield();
        cleanup.Release();
        for (var i = 0; i < 20000 && engine.State != DictationState.Idle; i++) await Task.Yield();

        result.ShouldNotBeNull();
        result.Cleaned.ShouldBeTrue();
        result.Text.ShouldBe("HELLO WORLD");
    }

    [Fact]
    public async Task Plain_hotkey_result_is_raw()
    {
        var capture = new FakeAudioCapture(FakeAudioCapture.Tone(2));
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(capture, hotkey, new FakeTranscriber("hello"), injector);
        engine.Cleanup = (text, _) => Task.FromResult(text.ToUpperInvariant());

        DictationResult? result = null;
        engine.Completed += (_, r) => result = r;

        await DictateAsync(hotkey, engine);

        result.ShouldNotBeNull();
        result.Cleaned.ShouldBeFalse();
        result.Text.ShouldBe("hello");
    }
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~DictationEngineTests"` — expect compile failure (`'DictationResult' does not contain a definition for 'Cleaned'`).
- [ ] In `DictationEngine.cs`, extend the record (optional parameter, so no other call site breaks):

```csharp
/// <summary>One completed dictation.</summary>
public sealed record DictationResult(
    DateTimeOffset At,
    TimeSpan AudioDuration,
    TimeSpan ProcessingTime,
    string Text,
    IReadOnlyList<AppliedCorrection> Corrections,
    string? RawText = null,
    bool Cleaned = false);
```

- [ ] In `ProcessAsync()`, replace the cleanup-and-build block **as Wave 1 left it**:

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

        var result = new DictationResult(
            At: releasedAt,
            AudioDuration: TimeSpan.FromSeconds((double)_capturedSamples / AudioChunk.SampleRate),
            ProcessingTime: _clock.Now - releasedAt,
            Text: text,
            Corrections: [.. spoken.SelectMany(s => s.Corrections)],
            RawText: rawText);
```

  with:

```csharp
        string? rawText = null;
        var cleaned = false;
        if (_cleanThisUtterance && Cleanup is { } cleanup)
        {
            var beforeCleanup = text;
            text = await cleanup(text, CancellationToken.None).ConfigureAwait(false);
            cleaned = true;
            if (!string.Equals(text, beforeCleanup, StringComparison.Ordinal))
            {
                rawText = beforeCleanup;
            }
        }

        var result = new DictationResult(
            At: releasedAt,
            AudioDuration: TimeSpan.FromSeconds((double)_capturedSamples / AudioChunk.SampleRate),
            ProcessingTime: _clock.Now - releasedAt,
            Text: text,
            Corrections: [.. spoken.SelectMany(s => s.Corrections)],
            RawText: rawText,
            Cleaned: cleaned);
```

  (`Cleaned` is true whenever the cleanup pass ran; `RawText` stays null when it ran but changed nothing — the two answer different questions and must not be collapsed.)

- [ ] In `TranscriptStore.cs`, add to `TranscriptRecord` (after `Corrections`):

```csharp
    /// <summary>
    /// Whether the cleanup pass ran on this dictation. Null on records written before the
    /// flag existed — treated as raw.
    /// </summary>
    public bool? Cleaned { get; init; }
```

  (`TranscriptJsonContext` is source-generated from the record — no context change needed.)
- [ ] In `Composition.cs`, in the `engine.Completed` handler, add the field to the record initializer:

```csharp
                transcripts.Add(new TranscriptRecord
                {
                    At = result.At,
                    AudioSeconds = result.AudioDuration.TotalSeconds,
                    ProcessingSeconds = result.ProcessingTime.TotalSeconds,
                    Text = result.Text,
                    Corrections = result.Corrections.Count > 0 ? result.Corrections : null,
                    RawText = result.RawText,
                    Cleaned = result.Cleaned,
                });
```

  (`RawText` is Wave 1's line — keep it; you are only adding `Cleaned`.)

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~DictationEngineTests"` — expect all passed, including the two new tests.
- [ ] Run the full suite: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (SelfTest storage round-trip still passes: the new property is nullable and optional).
- [ ] Commit: `git add -A && git commit -m "core: carry the cleaned flag from engine to history"`

---

### Task 6: TranscriptStats in Core

**Files:**
- Create: `windows/src/VoxScribe.Core/TranscriptStats.cs`
- Create: `windows/tests/VoxScribe.Core.Tests/TranscriptStatsTests.cs`

**Interfaces:**
- Produces: `public sealed record TranscriptStats(int WordsToday, int WordsThisWeek, int DictationsToday, int DictationsThisWeek, double AverageWordsPerDictation, int CleanCount, int RawCount)` with `static TranscriptStats Compute(IReadOnlyList<TranscriptRecord> records, DateTimeOffset now)` and `static int CountWords(string text)`
- Consumes: `TranscriptRecord` (incl. `Cleaned` from Task 5)

Day bucketing converts each record into `now`'s own offset (`At.ToOffset(now.Offset)`) rather than `ToLocalTime()`, so tests are deterministic on any machine timezone. The week starts Monday.

**Steps:**

- [ ] Write the failing tests in `windows/tests/VoxScribe.Core.Tests/TranscriptStatsTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;

namespace VoxScribe.CoreTests;

/// <summary>
/// The dashboard numbers are a pure function of the history and "now" — pinned here so the
/// view can stay dumb. Week starts Monday; day math runs in now's own offset so these tests
/// pass identically on any machine timezone.
/// </summary>
public sealed class TranscriptStatsTests
{
    // Friday 2026-08-28 noon UTC; its week runs Monday 08-24 through Sunday 08-30.
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private static TranscriptRecord At(DateTimeOffset at, string text, bool? cleaned = null) =>
        new() { At = at, Text = text, Cleaned = cleaned };

    [Fact]
    public void Empty_history_is_all_zero()
    {
        TranscriptStats.Compute([], Now).ShouldBe(new TranscriptStats(0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void Words_are_bucketed_by_day_and_week()
    {
        var stats = TranscriptStats.Compute(
        [
            At(Now.AddHours(-1), "one two three"),   // today: 3 words
            At(Now.AddDays(-2), "four five"),        // Wednesday: this week only
            At(Now.AddDays(-10), "six"),             // last week: total only
        ], Now);

        stats.WordsToday.ShouldBe(3);
        stats.WordsThisWeek.ShouldBe(5);
        stats.DictationsToday.ShouldBe(1);
        stats.DictationsThisWeek.ShouldBe(2);
        stats.AverageWordsPerDictation.ShouldBe(2.0);   // 6 words over 3 dictations
    }

    [Fact]
    public void Cleaned_flag_splits_raw_from_clean()
    {
        var stats = TranscriptStats.Compute(
        [
            At(Now, "a", cleaned: true),
            At(Now, "b", cleaned: false),
            At(Now, "c"),                    // legacy record: null counts as raw
        ], Now);

        stats.CleanCount.ShouldBe(1);
        stats.RawCount.ShouldBe(2);
    }

    [Fact]
    public void Record_offsets_do_not_skew_day_bucketing()
    {
        // 23:30 at UTC+2 on the 27th is 21:30 UTC — still yesterday in Now's frame.
        var stats = TranscriptStats.Compute(
            [At(new DateTimeOffset(2026, 8, 27, 23, 30, 0, TimeSpan.FromHours(2)), "x")], Now);

        stats.DictationsToday.ShouldBe(0);
        stats.DictationsThisWeek.ShouldBe(1);
    }

    [Fact]
    public void Word_counting_ignores_extra_whitespace()
    {
        TranscriptStats.CountWords("  one   two\tthree\n").ShouldBe(3);
        TranscriptStats.CountWords("").ShouldBe(0);
    }
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~TranscriptStatsTests"` — expect compile failure (`CS0246: 'TranscriptStats'`).
- [ ] Implement `windows/src/VoxScribe.Core/TranscriptStats.cs`:

```csharp
namespace VoxScribe.Core;

/// <summary>
/// Usage numbers for the dashboard, computed from the transcript history.
/// </summary>
/// <remarks>
/// Pure: history in, numbers out, "now" injected — so the whole dashboard is testable
/// without a UI. Day bucketing runs in <c>now</c>'s own offset rather than machine local
/// time, which keeps the function deterministic under test on any timezone. The week
/// starts Monday. A record whose <see cref="TranscriptRecord.Cleaned"/> is null predates
/// the flag and counts as raw.
/// </remarks>
public sealed record TranscriptStats(
    int WordsToday,
    int WordsThisWeek,
    int DictationsToday,
    int DictationsThisWeek,
    double AverageWordsPerDictation,
    int CleanCount,
    int RawCount)
{
    /// <summary>Computes the numbers for <paramref name="records"/> as of <paramref name="now"/>.</summary>
    public static TranscriptStats Compute(IReadOnlyList<TranscriptRecord> records, DateTimeOffset now)
    {
        var today = now.Date;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));   // back to Monday

        int wordsToday = 0, wordsWeek = 0, countToday = 0, countWeek = 0, totalWords = 0, clean = 0;

        foreach (var record in records)
        {
            var words = CountWords(record.Text);
            totalWords += words;
            if (record.Cleaned == true) clean++;

            var day = record.At.ToOffset(now.Offset).Date;
            if (day == today)
            {
                wordsToday += words;
                countToday++;
            }

            if (day >= weekStart && day <= today)
            {
                wordsWeek += words;
                countWeek++;
            }
        }

        return new TranscriptStats(
            wordsToday, wordsWeek, countToday, countWeek,
            records.Count == 0 ? 0 : (double)totalWords / records.Count,
            clean, records.Count - clean);
    }

    /// <summary>Words = whitespace-separated runs. The same rough count everyone expects.</summary>
    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~TranscriptStatsTests"` — expect 5 passed.
- [ ] Run the full suite: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green.
- [ ] Commit: `git add -A && git commit -m "core: TranscriptStats — pure dashboard numbers"`

---

### Task 7: DashboardView and a third rail section

**Files:**
- Create: `windows/src/VoxScribe.App/Views/DashboardView.cs`
- Create: `windows/tests/VoxScribe.App.Tests/DashboardViewTests.cs`
- Modify: `windows/src/VoxScribe.App/Views/MainWindow.cs`
- Modify: `windows/src/VoxScribe.App/Design/DesignTokens.cs`

**Interfaces:**
- Produces: `public sealed class DashboardView : UserControl` with ctor `DashboardView(TranscriptStore store, IClock? clock = null)` and `public TranscriptStats? Stats { get; }` (exposed for headless tests)
- Produces: token `Tokens.Material.StatCardMinWidth`
- Consumes: `TranscriptStats.Compute` (Task 6), `TranscriptStore.Records/Changed`, `IClock`/`SystemClock.Instance` from `VoxScribe.Abstractions`, `Panels.DeckCard`, `Silkscreen`

**Steps:**

- [ ] Write the failing tests in `windows/tests/VoxScribe.App.Tests/DashboardViewTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Shouldly;
using VoxScribe.Abstractions;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

/// <summary>
/// The dashboard is TranscriptStats rendered as cards; the view adds nothing but layout.
/// Pins that it computes from the store and follows store changes.
/// </summary>
public sealed class DashboardViewTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now => now;
    }

    private static TranscriptStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl"));

    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [AvaloniaFact]
    public void Computes_stats_from_the_store()
    {
        var store = TempStore();
        store.Add(new TranscriptRecord { At = Now.AddHours(-1), Text = "hello brave new world", Cleaned = true });

        var view = new DashboardView(store, new FixedClock(Now));

        view.Stats.ShouldNotBeNull();
        view.Stats.WordsToday.ShouldBe(4);
        view.Stats.DictationsToday.ShouldBe(1);
        view.Stats.CleanCount.ShouldBe(1);
        view.Stats.RawCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public void Follows_store_changes()
    {
        var store = TempStore();
        var view = new DashboardView(store, new FixedClock(Now));
        view.Stats.ShouldNotBeNull();
        view.Stats.DictationsToday.ShouldBe(0);

        store.Add(new TranscriptRecord { At = Now, Text = "one two" });
        Dispatcher.UIThread.RunJobs();

        view.Stats.WordsToday.ShouldBe(2);
    }
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~DashboardViewTests"` — expect compile failure (`CS0246: 'DashboardView'`).
- [ ] Add the stat-card width token to `windows/src/VoxScribe.App/Design/DesignTokens.cs`, inside `Tokens.Material`:

```csharp
        /// <summary>Minimum width of one dashboard stat card, so the wrap grid stays even.</summary>
        public const double StatCardMinWidth = 150;
```

- [ ] Implement `windows/src/VoxScribe.App/Views/DashboardView.cs`:

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoxScribe.Abstractions;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// The dashboard section: usage stat cards computed from the transcript history.
/// </summary>
/// <remarks>
/// All computation lives in <see cref="TranscriptStats"/>; this view only lays cards out.
/// The store's Changed can fire from the engine's worker thread, so refreshes marshal to
/// the UI thread — same rule as <see cref="TranscriptionsView"/>.
/// </remarks>
public sealed class DashboardView : UserControl
{
    private readonly TranscriptStore _store;
    private readonly IClock _clock;
    private readonly WrapPanel _cards;

    /// <summary>The last computed numbers. Exposed for headless tests.</summary>
    public TranscriptStats? Stats { get; private set; }

    /// <summary>Builds the dashboard over <paramref name="store"/>.</summary>
    public DashboardView(TranscriptStore store, IClock? clock = null)
    {
        _store = store;
        _clock = clock ?? SystemClock.Instance;

        _cards = new WrapPanel
        {
            ItemSpacing = Tokens.Space.Base,
            LineSpacing = Tokens.Space.Base,
            Margin = new Thickness(Tokens.Space.Base),
        };
        Content = new ScrollViewer { Content = _cards };

        _store.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        var stats = TranscriptStats.Compute(_store.Records, _clock.Now);
        Stats = stats;

        _cards.Children.Clear();
        _cards.Children.Add(StatCard("WORDS TODAY", Whole(stats.WordsToday)));
        _cards.Children.Add(StatCard("WORDS THIS WEEK", Whole(stats.WordsThisWeek)));
        _cards.Children.Add(StatCard("DICTATIONS TODAY", Whole(stats.DictationsToday)));
        _cards.Children.Add(StatCard("DICTATIONS THIS WEEK", Whole(stats.DictationsThisWeek)));
        _cards.Children.Add(StatCard(
            "AVG WORDS / DICTATION",
            stats.AverageWordsPerDictation.ToString("0.0", CultureInfo.CurrentCulture)));
        _cards.Children.Add(StatCard("RAW / CLEAN", $"{Whole(stats.RawCount)} / {Whole(stats.CleanCount)}"));
    }

    private static string Whole(int value) => value.ToString(CultureInfo.CurrentCulture);

    private static Border StatCard(string label, string value)
    {
        var card = Panels.DeckCard(new StackPanel
        {
            Spacing = Tokens.Space.Tight,
            Children =
            {
                new Silkscreen
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Tokens.Colors.InkOnDeck, 0.55),
                },
                new TextBlock
                {
                    Text = value,
                    FontFamily = Tokens.Fonts.Mono,
                    FontSize = Tokens.Fonts.CounterLarge,
                    Foreground = Tokens.Brushes.InkOnDeck,
                },
            },
        });
        card.MinWidth = Tokens.Material.StatCardMinWidth;
        return card;
    }
}
```

- [ ] In `MainWindow.cs`, add the dashboard as a rail section. Add a chart icon constant next to the others:

```csharp
    private const string ChartIcon = "M4,20 V12 M9,20 V6 M14,20 V9 M19,20 V4";
```

  Add fields next to the existing rail keys and cached views:

```csharp
    private readonly RailKey _dashboardKey;
    private Control? _dashboardView;
```

  In the constructor, replace the rail-key wiring block:

```csharp
        _transcriptionsKey = new RailKey(WaveIcon) { IsEngaged = true };
        _dictionaryKey = new RailKey(BookIcon);
        _transcriptionsKey.Click += (_, _) => ShowSection(transcriptions: true);
        _dictionaryKey.Click += (_, _) => ShowSection(transcriptions: false);
```

  with:

```csharp
        _dashboardKey = new RailKey(ChartIcon);
        _transcriptionsKey = new RailKey(WaveIcon) { IsEngaged = true };
        _dictionaryKey = new RailKey(BookIcon);
        _dashboardKey.Click += (_, _) => ShowSection(Section.Dashboard);
        _transcriptionsKey.Click += (_, _) => ShowSection(Section.Transcriptions);
        _dictionaryKey.Click += (_, _) => ShowSection(Section.Dictionary);
```

  Change `ShowSection(transcriptions: true);` later in the constructor to `ShowSection(Section.Transcriptions);`. In `BuildRail()`, add `_dashboardKey` to the top stack's children, before `_transcriptionsKey`:

```csharp
            Children = { badge, _dashboardKey, _transcriptionsKey, _dictionaryKey },
```

  Replace the whole `ShowSection(bool transcriptions)` method with:

```csharp
    private enum Section
    {
        Dashboard,
        Transcriptions,
        Dictionary,
    }

    private void ShowSection(Section section)
    {
        _dashboardKey.IsEngaged = section == Section.Dashboard;
        _transcriptionsKey.IsEngaged = section == Section.Transcriptions;
        _dictionaryKey.IsEngaged = section == Section.Dictionary;

        if (_composition is null)
        {
            _sectionHost.Content = section switch
            {
                Section.Dashboard => Panels.EmptyState("NO STATS", "Dictate something first."),
                Section.Transcriptions => Panels.EmptyState("NO RECORDINGS", "Press Record to start."),
                _ => Panels.EmptyState("DICTIONARY EMPTY", "Add words it keeps getting wrong."),
            };
            return;
        }

        // Built once and reused: rebuilding would drop the user's search text every time
        // they switched tabs.
        _sectionHost.Content = section switch
        {
            Section.Dashboard => _dashboardView ??= new DashboardView(_composition.Transcripts),
            Section.Transcriptions => _transcriptionsView ??= new TranscriptionsView(_composition.Transcripts),
            _ => _dictionaryView ??= new DictionaryView(_composition.Dictionary),
        };
    }
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~DashboardViewTests"` — expect 2 passed.
- [ ] Run the full suite: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green (existing `MainWindowTests` construct the window and must survive the rail change).
- [ ] Commit: `git add -A && git commit -m "windows: dashboard section with usage stat cards"`

---### Task 8: Enriched history — re-inject and markdown export

Search and one-click copy already exist in `TranscriptionsView` (search box filters via `TranscriptStore.Search`; COPY flashes COPIED). This task adds the two missing pieces: a per-row RE-INJECT button through `ITextInjector`, and export of the *filtered* list to a markdown file via the save dialog. Markdown rendering is pure Core.

**Files:**
- Create: `windows/src/VoxScribe.Core/TranscriptMarkdown.cs`
- Create: `windows/tests/VoxScribe.Core.Tests/TranscriptMarkdownTests.cs`
- Create: `windows/tests/VoxScribe.App.Tests/TranscriptionsViewTests.cs`
- Modify: `windows/src/VoxScribe.App/Composition.cs`
- Modify: `windows/src/VoxScribe.App/Views/TranscriptionsView.cs`
- Modify: `windows/src/VoxScribe.App/Views/MainWindow.cs`
- Modify: `windows/src/VoxScribe.App/Design/DesignTokens.cs`

**Interfaces:**
- Produces: `public static class TranscriptMarkdown` with `static string ToMarkdown(IReadOnlyList<TranscriptRecord> records)`
- Produces: `public ITextInjector? Injector { get; }` on `Composition`
- Produces: on `TranscriptionsView` — ctor `TranscriptionsView(TranscriptStore store, ITextInjector? injector = null)`, `public TimeSpan ReinjectDelay { get; set; }`, `public Task ReinjectAsync(TranscriptRecord record)`
- Produces: token `Tokens.Motion.ReinjectGrace`
- Consumes: `ITextInjector.InjectAsync(string, CancellationToken)`, `TranscriptStore.Search`, Avalonia `IStorageProvider.SaveFilePickerAsync`

**Steps:**

- [ ] Write the failing Core test in `windows/tests/VoxScribe.Core.Tests/TranscriptMarkdownTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;

namespace VoxScribe.CoreTests;

/// <summary>
/// The export is a pure text rendering of the (already filtered) history — pinned so the
/// view's save-dialog wiring has nothing to get wrong but plumbing.
/// </summary>
public sealed class TranscriptMarkdownTests
{
    [Fact]
    public void Renders_one_section_per_record()
    {
        var markdown = TranscriptMarkdown.ToMarkdown(
        [
            new TranscriptRecord
            {
                At = new DateTimeOffset(2026, 8, 28, 10, 30, 0, TimeSpan.Zero),
                Text = "hello world",
            },
            new TranscriptRecord
            {
                At = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero),
                Text = "second entry",
            },
        ]);

        markdown.ShouldStartWith("# Vox-Scribe transcripts");
        markdown.ShouldContain("## 2026-08-28 10:30");
        markdown.ShouldContain("hello world");
        markdown.ShouldContain("## 2026-08-27 09:00");
        markdown.ShouldContain("second entry");
    }

    [Fact]
    public void Empty_history_is_just_the_heading()
    {
        var markdown = TranscriptMarkdown.ToMarkdown([]);

        markdown.ShouldBe("# Vox-Scribe transcripts" + Environment.NewLine + Environment.NewLine);
    }
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~TranscriptMarkdownTests"` — expect compile failure (`CS0246: 'TranscriptMarkdown'`).
- [ ] Implement `windows/src/VoxScribe.Core/TranscriptMarkdown.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace VoxScribe.Core;

/// <summary>
/// Renders transcript records as a markdown document, one heading per dictation.
/// </summary>
/// <remarks>
/// Timestamps are written in the record's own stored offset rather than converted to local
/// time: the export is a portable document and must not change depending on the machine
/// that wrote it — the same determinism rule as <see cref="TranscriptStats"/>.
/// </remarks>
public static class TranscriptMarkdown
{
    /// <summary>The markdown for <paramref name="records"/>, in the order given.</summary>
    public static string ToMarkdown(IReadOnlyList<TranscriptRecord> records)
    {
        var builder = new StringBuilder();
        builder.Append("# Vox-Scribe transcripts").AppendLine().AppendLine();

        foreach (var record in records)
        {
            builder
                .Append("## ")
                .Append(record.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                .AppendLine()
                .AppendLine()
                .Append(record.Text)
                .AppendLine()
                .AppendLine();
        }

        return builder.ToString();
    }
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~TranscriptMarkdownTests"` — expect 2 passed.
- [ ] Write the failing view test in `windows/tests/VoxScribe.App.Tests/TranscriptionsViewTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using Shouldly;
using VoxScribe.Abstractions;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

/// <summary>
/// Re-inject sends the stored text back through the injector after a grace pause. Pinned
/// through the public method the button calls, with the pause zeroed for the test.
/// </summary>
public sealed class TranscriptionsViewTests
{
    private sealed class FakeInjector : ITextInjector
    {
        public List<string> Injected { get; } = [];

        public ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
        {
            Injected.Add(text);
            return ValueTask.FromResult(true);
        }
    }

    private static TranscriptStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl"));

    [AvaloniaFact]
    public async Task Reinject_sends_the_stored_text_through_the_injector()
    {
        var store = TempStore();
        var record = new TranscriptRecord { Text = "stored text" };
        store.Add(record);
        var injector = new FakeInjector();
        var view = new TranscriptionsView(store, injector) { ReinjectDelay = TimeSpan.Zero };

        await view.ReinjectAsync(record);

        injector.Injected.ShouldHaveSingleItem().ShouldBe("stored text");
    }

    [AvaloniaFact]
    public async Task Reinject_without_an_injector_does_nothing()
    {
        var store = TempStore();
        var record = new TranscriptRecord { Text = "stored text" };
        store.Add(record);
        var view = new TranscriptionsView(store) { ReinjectDelay = TimeSpan.Zero };

        await view.ReinjectAsync(record);   // must not throw
    }
}
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~TranscriptionsViewTests"` — expect compile failure (no two-argument ctor, no `ReinjectAsync`).
- [ ] Add the grace-pause token to `windows/src/VoxScribe.App/Design/DesignTokens.cs`, inside `Tokens.Motion`:

```csharp
        /// <summary>
        /// Pause before re-injecting stored text, giving the user time to focus the window
        /// the text should land in.
        /// </summary>
        public static readonly TimeSpan ReinjectGrace = TimeSpan.FromSeconds(3);
```

- [ ] In `Composition.cs`, expose the injector. Add a parameter to the private constructor (after `AppHealth health`) and a property:

```csharp
        ITextInjector? injector)
```

  with assignment `Injector = injector;`, plus:

```csharp
    /// <summary>The text injector, or null when no platform layer is available.</summary>
    public ITextInjector? Injector { get; }
```

  and change the `Create()` return to `return new Composition(settings, dictionary, transcripts, engine, available, health, injector);`.
- [ ] In `TranscriptionsView.cs`, take the injector and add the export/re-inject wiring. Change the field block and constructor head to:

```csharp
    private readonly TranscriptStore _store;
    private readonly ITextInjector? _injector;
    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly Silkscreen _count;

    /// <summary>Pause before a re-inject fires. A token by default; zeroed in tests.</summary>
    public TimeSpan ReinjectDelay { get; set; } = Tokens.Motion.ReinjectGrace;

    /// <summary>Builds the view over <paramref name="store"/>.</summary>
    public TranscriptionsView(TranscriptStore store, ITextInjector? injector = null)
    {
        _store = store;
        _injector = injector;
```

  (add `using VoxScribe.Abstractions;` and `using Avalonia.Platform.Storage;` to the usings). Then, in the constructor, replace the `Content = new DockPanel {...}` block with one that hangs an EXPORT button off the search row:

```csharp
        var export = Panels.DeckButton("EXPORT");
        export.Click += async (_, _) => await ExportAsync().ConfigureAwait(true);

        Content = new DockPanel
        {
            Children =
            {
                Panels.Docked(Panels.SearchRow(_search, export), Dock.Top),
                Panels.Docked(Panels.Footer(_count, clear), Dock.Bottom),
                new ScrollViewer { Content = _list },
            },
        };
```

  In `BuildRow`, extend the actions strip: after the `delete` button is built, add:

```csharp
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Tight,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { copy, delete },
        };

        if (_injector is not null)
        {
            var reinject = Panels.DeckButton("RE-INJECT");
            reinject.Click += async (_, _) =>
            {
                reinject.Content = "FOCUS TARGET…";
                await ReinjectAsync(record).ConfigureAwait(true);
                reinject.Content = "RE-INJECT";
            };
            actions.Children.Insert(0, reinject);
        }
```

  (this replaces the existing `actions` initializer — the `copy` and `delete` construction above it stays as-is). Add the two methods at the bottom of the class:

```csharp
    /// <summary>
    /// Types <paramref name="record"/>'s text at the caret after a grace pause, so the user
    /// can focus the window it should land in. No-op when there is no injector.
    /// </summary>
    public async Task ReinjectAsync(TranscriptRecord record)
    {
        if (_injector is null) return;

        await Task.Delay(ReinjectDelay).ConfigureAwait(true);
        await _injector.InjectAsync(record.Text, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Writes the currently filtered list to a markdown file the user picks.</summary>
    private async Task ExportAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export transcriptions",
            SuggestedFileName = "transcriptions.md",
            DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }],
        }).ConfigureAwait(true);
        if (file is null) return;

        var markdown = TranscriptMarkdown.ToMarkdown(_store.Search(_search.Text ?? string.Empty));
        await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(markdown).ConfigureAwait(true);
    }
```

- [ ] In `MainWindow.cs` `ShowSection`, pass the injector through:

```csharp
            Section.Transcriptions => _transcriptionsView ??= new TranscriptionsView(
                _composition.Transcripts, _composition.Injector),
```

- [ ] Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~TranscriptionsViewTests"` — expect 2 passed.
- [ ] Run the full suite: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` — expect all green.
- [ ] **Manual test required** (real Windows — real `SendInputTextInjector`): open Notepad, click RE-INJECT on a history row, focus Notepad within 3 s → the stored text is typed at the caret. Also click EXPORT with a search filter active → the saved .md contains only the filtered entries.
- [ ] Commit Core first, then the view wiring:
  - `git add windows/src/VoxScribe.Core/TranscriptMarkdown.cs windows/tests/VoxScribe.Core.Tests/TranscriptMarkdownTests.cs && git commit -m "core: markdown rendering of transcript history"`
  - `git add -A && git commit -m "windows: re-inject and markdown export in the history view"`

---

## Done means

- All tasks committed, `cd windows && dotnet test VoxScribe.CrossPlatform.slnf` fully green.
- Amber (never red) health rows in the main window name every failing part with a remedy; the pill shows `⚠ PART` while unhealthy.
- The pill preview-tail rule is pinned by tests; behaviour unchanged.
- Dashboard section shows words/dictations today & this week, average words per dictation, and raw/clean split, all from `TranscriptStats`.
- History rows offer COPY (existing), DELETE (existing), RE-INJECT (new, manual-tested), and the filtered list exports to markdown (manual-tested).
