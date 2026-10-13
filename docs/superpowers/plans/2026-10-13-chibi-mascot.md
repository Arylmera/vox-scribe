# Chibi Desktop Mascot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An optional, draggable, always-visible chibi character that replaces the dictation pill and reacts to dictation (idle, listening, thinking, done, notice).

**Architecture:** The mascot is a `PillFace` subclass (`ChibiFace`) hosted by the existing `HudWindow`, so it reuses the pill's polling state machine, lingers and text helpers. `HudWindow` gains a mascot mode (never hides, hit-testable, draggable, sized to content, anchored bottom-left at a saved position). Characters are drawn in code on a 120×140 canvas from SVG path strings (`Geometry.Parse`); all motion is a pure function of time and input level, quantised, driven by the existing 33 ms tick.

**Tech Stack:** C# / .NET 10, Avalonia 11.3, xUnit + Avalonia.Headless.XUnit 11.3.20, Shouldly.

**Spec:** `docs/superpowers/specs/2026-10-13-chibi-mascot-design.md` (amended in Task 1 — see there).

## Global Constraints

- Build and test from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf`. CI builds with warnings as errors; before each commit also run `dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror` and fix every warning.
- **Red means recording.** `Tokens.Colors.Record` (`#E5484D`) is the recording dot and nothing else. No character colour may satisfy `DoctrineTests.IsRed` (hue ≤ 15° or ≥ 345°, saturation ≥ 0.5, value ≥ 0.35, alpha ≥ 0x40). Clawd's terracotta is `#D9825A` (hue ≈ 19°), not Anthropic's `#D97757` (hue ≈ 14.8°, caught by the detector).
- **The mascot never takes focus.** Text lands at the caret; `WS_EX_NOACTIVATE` stays on in both modes.
- **No literal values in views** except a control's own drawing numbers/colours as named `private const` in that control. Shared sizes are tokens in `Design/DesignTokens.cs`.
- Every number formatted into a path string goes through `FormattableString.Invariant` (French culture writes `44,5`, which `Geometry.Parse` rejects).
- `VoxScribe.Platform.Windows` is not touched. `Overlay` (P/Invoke in `HudWindow.cs`) already lives in the App project.
- Commit after each task, then `git push` (house rule). No attribution lines in commit messages.
- Files use CRLF line endings and the existing header style (`using` block, file-scoped namespace, XML `<summary>` on every type and non-private member).

## File Structure

| File | Responsibility |
|---|---|
| `src/VoxScribe.Core/AppSettings.cs` (modify) | Five new settings: `Mascot`, `MascotHidden`, `MascotAccent`, `MascotX`, `MascotY` |
| `src/VoxScribe.App/Views/Pill/PillFace.cs` (modify) | `PillPhase.Idle` |
| `src/VoxScribe.App/Views/Chibi/ChibiMotion.cs` (create) | Pure, quantised motion functions |
| `src/VoxScribe.App/Views/Chibi/Sketch.cs` (create) | Drawing helper: shapes on a 120×140 canvas + registry of moving parts |
| `src/VoxScribe.App/Views/Chibi/ChibiFace.cs` (create) | Base face: pose from phase, bob/hop, motions, bubble, recording dot, shared extras |
| `src/VoxScribe.App/Views/Chibi/Mascots.cs` (create) | Catalogue of characters, active-mascot and accent resolution |
| `src/VoxScribe.App/Views/Chibi/MascotPlacement.cs` (create) | Pure: saved anchor → on-screen anchor |
| `src/VoxScribe.App/Views/Chibi/<Name>Chibi.cs` (create ×9) | One character's drawing |
| `src/VoxScribe.App/Views/HudWindow.cs` (modify) | Mascot mode, overlay variant |
| `src/VoxScribe.App/App.axaml`, `App.axaml.cs` (modify) | Wiring, tray item |
| `src/VoxScribe.App/Views/Settings/AppearanceSection.cs` (modify) | Mascot picker and mascot accent |
| `src/VoxScribe.App/Design/DesignTokens.cs` (modify) | Mascot size tokens |
| `tests/VoxScribe.Core.Tests/SettingsMigrationTests.cs` (modify) | Settings defaults and round trip |
| `tests/VoxScribe.App.Tests/ChibiMotionTests.cs`, `MascotTests.cs`, `ChibiTests.cs` (create) | Motion, catalogue/placement, faces |
| `tests/VoxScribe.App.Tests/AppearanceSectionTests.cs` (modify) | Picker |

---

### Task 1: Settings, idle phase, motion, placement, catalogue skeleton (pure parts)

**Files:**
- Modify: `windows/src/VoxScribe.Core/AppSettings.cs` (after `AccentVariant`, ~line 241)
- Modify: `windows/src/VoxScribe.App/Views/Pill/PillFace.cs:13-26`
- Create: `windows/src/VoxScribe.App/Views/Chibi/ChibiMotion.cs`
- Create: `windows/src/VoxScribe.App/Views/Chibi/MascotPlacement.cs`
- Modify: `docs/superpowers/specs/2026-10-13-chibi-mascot-design.md`
- Test: `windows/tests/VoxScribe.Core.Tests/SettingsMigrationTests.cs`, `windows/tests/VoxScribe.App.Tests/ChibiMotionTests.cs`, `windows/tests/VoxScribe.App.Tests/MascotTests.cs`

**Interfaces:**
- Produces:
  - `SettingsData.Mascot: string?`, `MascotHidden: bool`, `MascotAccent: string?`, `MascotX: int?`, `MascotY: int?` (all `init`)
  - `PillPhase.Idle`
  - `static class ChibiMotion` with `Quantize(double value, double step)`, `Bob(double t)`, `Hop(double sinceDone)`, `Blinking(double t)`, `Talk(double level)`, `Eq(double level, int index, double t)`, `Pulse(double t)`, `Spin(double t)`, `Swing(double t, double period, double degrees)`, `Flap(double t)`, `Wave(double t, int index)`, `Squish(double level) -> (double X, double Y)`, `Rise(double t, int index) -> (double Y, double Opacity)` — `t` in seconds
  - `static class MascotPlacement` with `PixelPoint Anchor(PixelPoint? saved, IReadOnlyList<PixelRect> areas, PixelSize size, int margin)` — returns the **bottom-left** corner of the mascot window; `areas[0]` is the primary screen's working area

- [ ] **Step 1: Write the failing settings test**

Append to `windows/tests/VoxScribe.Core.Tests/SettingsMigrationTests.cs`, inside the class:

```csharp
    [Fact]
    public void The_mascot_is_off_by_default() =>
        new SettingsData().ShouldSatisfyAllConditions(
            d => d.Mascot.ShouldBeNull(),
            d => d.MascotHidden.ShouldBeFalse(),
            d => d.MascotAccent.ShouldBeNull(),
            d => d.MascotX.ShouldBeNull(),
            d => d.MascotY.ShouldBeNull());

    [Fact]
    public void Mascot_settings_survive_a_save()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings(path);
            settings.Update(settings.Data with
            {
                Mascot = "slime", MascotHidden = true, MascotAccent = "moss", MascotX = 120, MascotY = 900,
            });

            new AppSettings(path).Data.ShouldSatisfyAllConditions(
                d => d.Mascot.ShouldBe("slime"),
                d => d.MascotHidden.ShouldBeTrue(),
                d => d.MascotAccent.ShouldBe("moss"),
                d => d.MascotX.ShouldBe(120),
                d => d.MascotY.ShouldBe(900));
        }
        finally
        {
            File.Delete(path);
        }
    }
```

- [ ] **Step 2: Run it, expect a compile failure**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~SettingsMigrationTests"`
Expected: build error `'SettingsData' does not contain a definition for 'Mascot'`.

- [ ] **Step 3: Add the settings**

In `windows/src/VoxScribe.Core/AppSettings.cs`, after the `AccentVariant` property (inside `SettingsData`):

```csharp

    /// <summary>
    /// Desktop mascot id ("servo-skull", "netrunner", "wizard", "clawd", "owl", "mic", "cat",
    /// "slime", "dragon"), or null for the dictation pill. An unknown id falls back to the pill.
    /// </summary>
    public string? Mascot { get; init; }

    /// <summary>
    /// The mascot is put away (tray or right-click "hide"). Dictation then shows the pill, so
    /// feedback is never lost.
    /// </summary>
    public bool MascotHidden { get; init; }

    /// <summary>Accent variant id from any theme to pin on the mascot, or null to follow the theme.</summary>
    public string? MascotAccent { get; init; }

    /// <summary>Mascot window's bottom-left corner, in screen pixels, or null for the default corner.</summary>
    public int? MascotX { get; init; }

    /// <summary>See <see cref="MascotX"/>.</summary>
    public int? MascotY { get; init; }
```

- [ ] **Step 4: Run the settings tests, expect PASS**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~SettingsMigrationTests"`
Expected: all pass.

- [ ] **Step 5: Write the failing motion and placement tests**

Create `windows/tests/VoxScribe.App.Tests/ChibiMotionTests.cs`:

```csharp
using Shouldly;
using VoxScribe.App.Views.Chibi;

namespace VoxScribe.AppTests;

/// <summary>Mascot motion is a pure, quantised function of time and level.</summary>
public sealed class ChibiMotionTests
{
    private static IEnumerable<double> Seconds(double until, double step = 0.033)
    {
        for (var t = 0.0; t < until; t += step) yield return t;
    }

    [Fact]
    public void Bob_floats_within_three_pixels_in_half_pixel_steps()
    {
        foreach (var t in Seconds(10))
        {
            var y = ChibiMotion.Bob(t);
            y.ShouldBeInRange(-3 - 1e-9, 1e-9);
            (y * 2).ShouldBe(Math.Round(y * 2), 1e-9);
        }
    }

    [Fact]
    public void Idle_bob_changes_rarely_so_idle_frames_mostly_repaint_nothing()
    {
        // 30 frames a second for 3.2 s (one period): a 0.5 px grid on a 3 px swing allows at
        // most a dozen distinct steps, so most frames set an unchanged value.
        var changes = 0;
        var last = ChibiMotion.Bob(0);
        foreach (var t in Seconds(3.2))
        {
            var y = ChibiMotion.Bob(t);
            if (y != last) changes++;
            last = y;
        }

        changes.ShouldBeLessThan(30);
    }

    [Fact]
    public void Talk_opens_with_the_voice()
    {
        ChibiMotion.Talk(0).ShouldBe(0.3, 1e-9);
        ChibiMotion.Talk(1).ShouldBe(1.1, 1e-9);
        ChibiMotion.Talk(2).ShouldBe(1.1, 1e-9, "level is clamped");
        ChibiMotion.Talk(0.5).ShouldBeGreaterThan(ChibiMotion.Talk(0.2));
    }

    [Fact]
    public void Eyes_blink_briefly_every_four_seconds()
    {
        ChibiMotion.Blinking(1.0).ShouldBeFalse();
        ChibiMotion.Blinking(3.9).ShouldBeTrue();
        Seconds(4).Count(ChibiMotion.Blinking).ShouldBeLessThan(10);
    }

    [Fact]
    public void The_done_hop_jumps_then_settles()
    {
        ChibiMotion.Hop(0).ShouldBe(0);
        ChibiMotion.Hop(0.225).ShouldBeLessThan(-4);
        ChibiMotion.Hop(3).ShouldBeGreaterThan(-0.5);
    }

    [Fact]
    public void Eq_bars_stay_in_range_and_rise_with_level()
    {
        foreach (var t in Seconds(2))
            for (var i = 0; i < 5; i++)
            {
                ChibiMotion.Eq(0, i, t).ShouldBe(0.25, 1e-9);
                ChibiMotion.Eq(1, i, t).ShouldBeInRange(0.25 - 1e-9, 1 + 1e-9);
            }
    }

    [Fact]
    public void Pulse_and_rise_fade_but_never_vanish_or_overflow()
    {
        foreach (var t in Seconds(4))
        {
            ChibiMotion.Pulse(t).ShouldBeInRange(0.35 - 1e-9, 1 + 1e-9);
            var (y, opacity) = ChibiMotion.Rise(t, 0);
            y.ShouldBeInRange(-14 - 1e-9, 1e-9);
            opacity.ShouldBeInRange(-1e-9, 0.9 + 1e-9);
        }
    }

    [Fact]
    public void Squish_flattens_with_the_voice()
    {
        ChibiMotion.Squish(0).ShouldBe((1.0, 1.0));
        var (x, y) = ChibiMotion.Squish(1);
        x.ShouldBeGreaterThan(1);
        y.ShouldBeLessThan(1);
    }
}
```

Create `windows/tests/VoxScribe.App.Tests/MascotTests.cs`:

```csharp
using Avalonia;
using Shouldly;
using VoxScribe.App.Views.Chibi;

namespace VoxScribe.AppTests;

/// <summary>Where the mascot sits.</summary>
public sealed class MascotTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1040);
    private static readonly PixelRect Second = new(1920, 0, 2560, 1400);
    private static readonly PixelSize Size = new(96, 112);
    private const int Margin = 24;

    [Fact]
    public void No_saved_spot_means_bottom_right_of_the_primary_screen() =>
        MascotPlacement.Anchor(null, [Primary, Second], Size, Margin)
            .ShouldBe(new PixelPoint(1920 - 96 - Margin, 1040 - Margin));

    [Fact]
    public void A_saved_spot_on_any_screen_is_kept()
    {
        MascotPlacement.Anchor(new PixelPoint(300, 800), [Primary, Second], Size, Margin).ShouldBe(new PixelPoint(300, 800));
        MascotPlacement.Anchor(new PixelPoint(2500, 1300), [Primary, Second], Size, Margin).ShouldBe(new PixelPoint(2500, 1300));
    }

    [Fact]
    public void A_spot_on_an_unplugged_screen_falls_back_to_the_default_corner() =>
        MascotPlacement.Anchor(new PixelPoint(5000, 800), [Primary], Size, Margin)
            .ShouldBe(new PixelPoint(1920 - 96 - Margin, 1040 - Margin));

    [Fact]
    public void No_screen_at_all_keeps_whatever_was_saved() =>
        MascotPlacement.Anchor(new PixelPoint(10, 10), [], Size, Margin).ShouldBe(new PixelPoint(10, 10));
}
```

- [ ] **Step 6: Run them, expect a compile failure**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~ChibiMotionTests|FullyQualifiedName~MascotTests"`
Expected: build error, `VoxScribe.App.Views.Chibi` does not exist.

- [ ] **Step 7: Implement `ChibiMotion`**

Create `windows/src/VoxScribe.App/Views/Chibi/ChibiMotion.cs`:

```csharp
namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Every mascot motion as a pure function of time (seconds) and input level (0–1).
/// </summary>
/// <remarks>
/// Values are quantised: the mascot is on screen all day, and a property set to the value it
/// already holds raises nothing and repaints nothing, so a coarse grid keeps idle frames free.
/// Driven by <see cref="HudWindow"/>'s existing 33 ms tick rather than Avalonia animations,
/// which would repaint every frame anyway and fight layout over offsets.
/// </remarks>
internal static class ChibiMotion
{
    private const double BobPeriod = 3.2;
    private const double BobDepth = 3;
    private const double HalfPixel = 0.5;
    private const double HopHeight = 8;
    private const double HopBounce = 0.45;
    private const double HopDecay = 1.5;
    private const double BlinkPeriod = 4;
    private const double BlinkLength = 0.15;
    private const double TalkClosed = 0.3;
    private const double TalkRange = 0.8;
    private const double ScaleStep = 0.05;
    private const double EqFloor = 0.25;
    private const double EqWobbleSpeed = 12;
    private const double EqWobbleSpread = 1.7;
    private const double EqWobble = 0.3;
    private const double PulsePeriod = 0.9;
    private const double PulseLow = 0.35;
    private const double SpinDegreesPerSecond = 90;
    private const double SpinStep = 6;
    private const double FlapPeriod = 0.5;
    private const double FlapNarrowest = 0.6;
    private const double WavePeriod = 0.6;
    private const double WaveHeight = 6;
    private const double SquishWiden = 0.06;
    private const double SquishFlatten = 0.1;
    private const double RisePeriod = 1.6;
    private const double RiseHeight = 14;
    private const double RiseStart = 0.9;
    private const double RiseStagger = 0.8;

    /// <summary><paramref name="value"/> on a grid of <paramref name="step"/>.</summary>
    public static double Quantize(double value, double step) => Math.Round(value / step) * step;

    /// <summary>Idle float: 0 to −3 px and back over 3.2 s.</summary>
    public static double Bob(double t) =>
        Quantize(-(BobDepth / 2) * (1 - Math.Cos(2 * Math.PI * t / BobPeriod)), HalfPixel);

    /// <summary>The done hop: bounces of 0.45 s, decaying to rest. Negative is up.</summary>
    public static double Hop(double sinceDone) =>
        Quantize(-HopHeight * Math.Abs(Math.Sin(Math.PI * sinceDone / HopBounce)) * Math.Exp(-sinceDone * HopDecay), HalfPixel);

    /// <summary>True during the last 0.15 s of every 4 s.</summary>
    public static bool Blinking(double t) => t % BlinkPeriod > BlinkPeriod - BlinkLength;

    /// <summary>Mouth height scale: 0.3 closed to 1.1 at full voice.</summary>
    public static double Talk(double level) => Quantize(TalkClosed + (TalkRange * Math.Clamp(level, 0, 1)), ScaleStep);

    /// <summary>Equaliser bar height scale, 0.25–1: the level with a per-bar wobble.</summary>
    public static double Eq(double level, int index, double t)
    {
        var wobble = 1 - EqWobble + (EqWobble * Math.Sin((t * EqWobbleSpeed) + (index * EqWobbleSpread)));
        return Quantize(EqFloor + ((1 - EqFloor) * Math.Clamp(level * wobble, 0, 1)), ScaleStep);
    }

    /// <summary>Glow opacity, 0.35–1 over 0.9 s.</summary>
    public static double Pulse(double t) =>
        Quantize(PulseLow + ((1 - PulseLow) * (0.5 + (0.5 * Math.Cos(2 * Math.PI * t / PulsePeriod)))), ScaleStep);

    /// <summary>A slow turn, 90° a second, in 6° steps.</summary>
    public static double Spin(double t) => Quantize(t * SpinDegreesPerSecond % 360, SpinStep);

    /// <summary>A rocking angle of ±<paramref name="degrees"/> over <paramref name="period"/> seconds, whole degrees.</summary>
    public static double Swing(double t, double period, double degrees) =>
        Math.Round(degrees * Math.Sin(2 * Math.PI * t / period));

    /// <summary>Wing width scale, 0.6–1 over 0.5 s.</summary>
    public static double Flap(double t) =>
        Quantize(FlapNarrowest + ((1 - FlapNarrowest) * (0.5 + (0.5 * Math.Cos(2 * Math.PI * t / FlapPeriod)))), ScaleStep);

    /// <summary>Waving arm, 0 to −6 px, odd arms in opposition. Whole pixels (pixel art).</summary>
    public static double Wave(double t, int index) =>
        Math.Round(-(WaveHeight / 2) * (1 - Math.Cos((2 * Math.PI * t / WavePeriod) + (index * Math.PI))));

    /// <summary>A jelly squash with the voice: wider and flatter.</summary>
    public static (double X, double Y) Squish(double level)
    {
        var l = Math.Clamp(level, 0, 1);
        return (Quantize(1 + (SquishWiden * l), ScaleStep / 5), Quantize(1 - (SquishFlatten * l), ScaleStep / 5));
    }

    /// <summary>A puff or note drifting up 14 px and fading, staggered by <paramref name="index"/>.</summary>
    public static (double Y, double Opacity) Rise(double t, int index)
    {
        var p = (t + (index * RiseStagger)) % RisePeriod / RisePeriod;
        return (Quantize(-RiseHeight * p, HalfPixel), Quantize(RiseStart * (1 - p), ScaleStep));
    }
}
```

- [ ] **Step 8: Implement `MascotPlacement`**

Create `windows/src/VoxScribe.App/Views/Chibi/MascotPlacement.cs`:

```csharp
using Avalonia;

namespace VoxScribe.App.Views.Chibi;

/// <summary>Where the mascot window goes. Pure, so the fallback is testable without screens.</summary>
internal static class MascotPlacement
{
    /// <summary>
    /// The mascot window's bottom-left corner: <paramref name="saved"/> while it is on some
    /// screen, otherwise the bottom-right of the primary working area (<paramref name="areas"/>[0])
    /// inset by <paramref name="margin"/>. A monitor unplugged since the last drag must not
    /// leave the mascot somewhere nobody can see it.
    /// </summary>
    public static PixelPoint Anchor(PixelPoint? saved, IReadOnlyList<PixelRect> areas, PixelSize size, int margin)
    {
        if (areas.Count == 0) return saved ?? default;
        if (saved is { } s && areas.Any(a => a.Contains(new PixelPoint(s.X, s.Y - 1)))) return s;

        var primary = areas[0];
        return new PixelPoint(primary.Right - size.Width - margin, primary.Bottom - margin);
    }
}
```

- [ ] **Step 9: Add `PillPhase.Idle`**

In `windows/src/VoxScribe.App/Views/Pill/PillFace.cs`, add as the **last** member of `PillPhase` (after `Latency`):

```csharp

    /// <summary>Nothing happening. Only the mascot is shown in it; the pill is hidden.</summary>
    Idle,
```

This flows into every `EachTheme` loop in `PillTests` that walks `Enum.GetValues<PillPhase>()`; the pill faces treat `Idle` like any non-recording phase (dot hidden), which is what those tests assert.

- [ ] **Step 10: Amend the spec**

In `docs/superpowers/specs/2026-10-13-chibi-mascot-design.md`, replace the whole `## Animation and rendering` section body with:

```markdown
- All motion (bob, blink, hop, mouth, equaliser, glow pulse, spin, wag, flap, squash, rising
  notes/smoke) is a **pure function of time and input level** (`ChibiMotion`), applied from
  `HudWindow`'s existing 33 ms tick. Values are quantised (half pixels, 6°, 0.05 scale), so an
  idle frame that changes nothing sets unchanged values and repaints nothing.
  Composition/keyframe animations were considered and rejected: continuous motion repaints
  every frame either way, and animating offsets fights layout.
- The `Latency` hop is a decaying bounce.
- Soft glow (`DropShadowEffect`) on the accent part while listening.
- Mockup path data ports directly: Avalonia's `Geometry.Parse` reads SVG path syntax.
- Clawd renders aliased (`RenderOptions.EdgeMode = Aliased`) so its pixels stay crisp.
- Character colours and drawing numbers are `private const` in each character's file; sizes
  shared with the window are tokens.
```

Also in that spec:
- In the Characters table, Clawd's row: replace `terracotta block` with `terracotta block (#D9825A — nudged 4° off Anthropic's #D97757, which the red detector flags)`.
- In `## Window`, replace `Double-click opens the main window.` with `Double-click (the press's ClickCount = 2) opens the main window.`; add a bullet: `- The window sizes to its content (the bubble collapses when idle), anchored at its bottom-left corner, so only the character blocks clicks while idle.`
- In `## Testing`, replace the `HudWindow in mascot mode stays shown…` bullet with `- Mode and placement are pure functions (Mascots.Active, MascotPlacement.Anchor), unit-tested; HudWindow itself is covered by the hand check (it needs a live DictationEngine).`

- [ ] **Step 11: Run all tests, expect PASS**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf`
Expected: all pass (including `PillTests` with the new phase).

- [ ] **Step 12: Warnings gate, commit, push**

```bash
cd windows && dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror
cd .. && git add windows docs/superpowers/specs/2026-10-13-chibi-mascot-design.md
git commit -m "feat: mascot settings, idle phase, motion and placement"
git push
```

---

### Task 2: Sketch, ChibiFace base, the Slime, and the catalogue

**Files:**
- Create: `windows/src/VoxScribe.App/Views/Chibi/Sketch.cs`
- Create: `windows/src/VoxScribe.App/Views/Chibi/ChibiFace.cs`
- Create: `windows/src/VoxScribe.App/Views/Chibi/SlimeChibi.cs`
- Create: `windows/src/VoxScribe.App/Views/Chibi/Mascots.cs`
- Modify: `windows/src/VoxScribe.App/Design/DesignTokens.cs` (class `Size`, after `PillWindowHeight`; class `Material`, after `PillScreenMargin`)
- Test: `windows/tests/VoxScribe.App.Tests/ChibiTests.cs`, `windows/tests/VoxScribe.App.Tests/MascotTests.cs`

**Interfaces:**
- Consumes: `ChibiMotion.*`, `PillPhase.Idle`, `SettingsData.Mascot/MascotHidden/MascotAccent` (Task 1); `PillFace` protected helpers `Dot`, `Text`, `TailHost`, `ShowPreview`, `HasPreview`, `TimerLine`.
- Produces:
  - `enum ChibiPose { Idle, Listen, Think, Done, Worried }`
  - `enum ChibiMove { Talk, Eq, Pulse, Spin, Blink, Wag, Tilt, Flap, Squish, Rise, Wave }`
  - `sealed class Sketch` — `Canvas Root`, `Path(string data, IBrush? fill, IBrush? stroke = null, double width = 0)`, `Ellipse(double cx, double cy, double rx, double ry, IBrush? fill)`, `Circle(double cx, double cy, double r, IBrush? fill)`, `Rect(double x, double y, double w, double h, double radius, IBrush fill)`, `Text(string text, double x, double y, double width, double size, IBrush fill)`, `Canvas Begin()`, `void End()`, `T Move<T>(T target, ChibiMove move, int index = 0, Point? origin = null)`, `IReadOnlyList<Moving> Moving`
  - `abstract class ChibiFace : PillFace` — ctor `(Color accent)`; `protected IBrush Accent`; `protected Color AccentColor`; `protected abstract void Draw(Sketch s, ChibiPose pose)`; `internal void Update(PillState state, TimeSpan now)`; `internal ChibiPose Pose`; `internal Control Bubble`; `protected static SolidColorBrush Rgb(uint rgb)`; `protected static void SleepyEyes(Sketch s, double x1, double x2, double y, IBrush ink, double width = 3)`; `protected static void HappyEyes(Sketch s, double x1, double x2, double y, IBrush ink, double width = 3.5)`; `protected T Glow<T>(T control, Color? colour = null) where T : Control`
  - `sealed record Mascot(string Id, string Label, Func<Color, ChibiFace> Create)`; `static class Mascots` with `IReadOnlyList<Mascot> All`, `Mascot? Find(string? id)`, `Mascot? Active(SettingsData d)`, `Color Accent(SettingsData d)`, `IReadOnlyList<AccentVariant> Accents`
  - Tokens: `Tokens.Size.MascotWidth = 96`, `MascotHeight = 112`, `MascotBubbleWidth = 280`; `Tokens.Material.MascotPreviewSize = 56`

- [ ] **Step 1: Write the failing face and catalogue tests**

Create `windows/tests/VoxScribe.App.Tests/ChibiTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Chibi;
using VoxScribe.App.Views.Pill;

namespace VoxScribe.AppTests;

/// <summary>Every mascot, every phase, both modes.</summary>
public sealed class ChibiTests
{
    // Same detector as Design/DoctrineTests: red hue window, saturated, bright, visible.
    private static bool IsRed(Color c)
    {
        if (c.A < 0x40) return false;
        var hsv = c.ToHsv();
        return (hsv.H <= 15 || hsv.H >= 345) && hsv.S >= 0.5 && hsv.V >= 0.35;
    }

    private static readonly TimeSpan[] Moments =
        [TimeSpan.Zero, TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(1.7), TimeSpan.FromSeconds(3.9)];

    private static void EachMascot(Action<Mascot, ChibiFace> check)
    {
        try
        {
            foreach (var dark in new[] { false, true })
                foreach (var mascot in Mascots.All)
                {
                    Themes.Apply(Themes.DefaultId, null, dark);
                    var face = mascot.Create(Tokens.Colors.Accent);
                    var window = new Window { Content = face, SizeToContent = SizeToContent.WidthAndHeight };
                    window.Show();
                    check(mascot, face);
                    window.Close();
                }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }

    private static IEnumerable<Color> Colours(Control c) => c switch
    {
        Shape s => new[] { s.Fill, s.Stroke }.OfType<ISolidColorBrush>().Select(b => b.Color),
        TextBlock t => new[] { t.Foreground }.OfType<ISolidColorBrush>().Select(b => b.Color),
        Border b => new[] { b.Background, b.BorderBrush }.OfType<ISolidColorBrush>().Select(x => x.Color),
        _ => [],
    };

    [AvaloniaFact]
    public void Every_mascot_draws_every_phase_at_any_moment() => EachMascot((mascot, face) =>
    {
        foreach (var phase in Enum.GetValues<PillPhase>())
            foreach (var now in Moments)
            {
                Should.NotThrow(() => face.Update(new PillState(phase, 0.7, now, "CLEAN", "bonjour"), now), $"{mascot.Id} {phase}");
            }
    });

    [AvaloniaFact]
    public void The_red_dot_shows_only_while_recording() => EachMascot((mascot, face) =>
    {
        foreach (var phase in Enum.GetValues<PillPhase>())
        {
            face.Update(new PillState(phase, 0.6, TimeSpan.FromSeconds(4), "RAW", "bonjour"), TimeSpan.FromSeconds(1));
            face.RecordDot.IsVisible.ShouldBe(phase == PillPhase.Recording, $"{mascot.Id} {phase}");
        }
    });

    [AvaloniaFact]
    public void Nothing_on_a_mascot_is_red_but_the_dot() => EachMascot((mascot, face) =>
    {
        foreach (var phase in Enum.GetValues<PillPhase>())
        {
            face.Update(new PillState(phase, 0.8, TimeSpan.FromSeconds(1), "CLEAN", "texte"), TimeSpan.FromSeconds(1));
            var red = face.GetVisualDescendants().OfType<Control>()
                .Where(c => !ReferenceEquals(c, face.RecordDot) && Colours(c).Any(IsRed))
                .Select(c => c.GetType().Name);
            red.ShouldBeEmpty($"{mascot.Id} {phase}");
        }
    });

    [AvaloniaFact]
    public void The_bubble_speaks_only_when_there_is_something_to_say() => EachMascot((mascot, face) =>
    {
        face.Update(new PillState(PillPhase.Idle, 0, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
        face.Bubble.IsVisible.ShouldBeFalse(mascot.Id);

        face.Update(new PillState(PillPhase.Recording, 0.4, TimeSpan.FromSeconds(2), "RAW", "bonjour"), TimeSpan.Zero);
        face.Bubble.IsVisible.ShouldBeTrue(mascot.Id);
        string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)).ShouldContain("bonjour");
    });

    [AvaloniaFact]
    public void The_latency_shows_the_cleanup_split() => EachMascot((mascot, face) =>
    {
        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "CLEAN", string.Empty,
            Cleanup: TimeSpan.FromSeconds(0.7)), TimeSpan.Zero);
        var text = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        text.ShouldContain("1.2s", customMessage: mascot.Id);
        text.ShouldContain("0.7s", customMessage: mascot.Id);

        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "EDIT", string.Empty,
            Cleanup: TimeSpan.FromSeconds(0.7)), TimeSpan.Zero);
        string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .ToLowerInvariant().ShouldNotContain("clean", customMessage: mascot.Id);
    });

    [AvaloniaFact]
    public void A_clean_working_phase_says_clean_and_a_raw_one_does_not() => EachMascot((mascot, face) =>
    {
        face.Update(new PillState(PillPhase.Working, 0.4, TimeSpan.FromSeconds(3), "RAW", "bonjour"), TimeSpan.Zero);
        string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .ToLowerInvariant().ShouldNotContain("clean", customMessage: mascot.Id);

        face.Update(new PillState(PillPhase.Working, 0.4, TimeSpan.FromSeconds(3), "CLEAN", "bonjour"), TimeSpan.Zero);
        string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .ToLowerInvariant().ShouldContain("clean", customMessage: mascot.Id);
    });

    [AvaloniaFact]
    public void A_lingering_notice_keeps_its_subject() => EachMascot((mascot, face) =>
    {
        const string Notice = "Nothing was typed: the focused window refused the text, try again in a moment";
        face.Update(new PillState(PillPhase.Notice, 0, TimeSpan.Zero, "RAW", Notice), TimeSpan.Zero);

        face.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.TextTrimming == TextTrimming.CharacterEllipsis)
            .Text.ShouldStartWith("Nothing was", customMessage: mascot.Id);
    });

    [AvaloniaFact]
    public void The_pose_follows_the_phase() => EachMascot((mascot, face) =>
    {
        (PillPhase Phase, ChibiPose Pose)[] map =
        [
            (PillPhase.Idle, ChibiPose.Idle), (PillPhase.Recording, ChibiPose.Listen), (PillPhase.Working, ChibiPose.Think),
            (PillPhase.Latency, ChibiPose.Done), (PillPhase.Notice, ChibiPose.Worried),
        ];
        foreach (var (phase, pose) in map)
        {
            face.Update(new PillState(phase, 0, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
            face.Pose.ShouldBe(pose, mascot.Id);
        }
    });

    [AvaloniaFact]
    public void A_mascot_can_be_grabbed() => EachMascot((mascot, face) =>
    {
        face.IsHitTestVisible.ShouldBeTrue(mascot.Id);
        face.Focusable.ShouldBeFalse(mascot.Id);
    });
}
```

Append to `windows/tests/VoxScribe.App.Tests/MascotTests.cs` (inside the class; add `using Avalonia.Headless.XUnit;`, `using VoxScribe.App.Design;`, `using VoxScribe.Core;` at the top):

```csharp
    [Fact]
    public void Ids_are_unique_and_unknown_ids_mean_the_pill()
    {
        Mascots.All.Select(m => m.Id).ShouldBeUnique();
        Mascots.Find("nope").ShouldBeNull();
        Mascots.Find(null).ShouldBeNull();
        Mascots.Active(new SettingsData { Mascot = "nope" }).ShouldBeNull();
    }

    [Fact]
    public void A_hidden_mascot_means_the_pill()
    {
        var id = Mascots.All[0].Id;
        Mascots.Active(new SettingsData { Mascot = id }).ShouldNotBeNull();
        Mascots.Active(new SettingsData { Mascot = id, MascotHidden = true }).ShouldBeNull();
    }

    [Fact]
    public void Accent_variant_ids_are_unique_across_every_theme() =>
        Themes.All.SelectMany(t => t.Variants).Select(v => v.Id).ShouldBeUnique(
            "the mascot accent override stores a bare variant id");

    [AvaloniaFact]
    public void The_mascot_accent_follows_the_theme_unless_pinned()
    {
        try
        {
            Themes.Apply("paper", "plum", dark: false);
            Mascots.Accent(new SettingsData()).ShouldBe(Tokens.Colors.Accent);

            var moss = Themes.All.SelectMany(t => t.Variants).Single(v => v.Id == "moss");
            Mascots.Accent(new SettingsData { MascotAccent = "moss" }).ShouldBe(Tokens.Colors.Rgb(moss.Light));
            Themes.Apply("paper", "plum", dark: true);
            Mascots.Accent(new SettingsData { MascotAccent = "moss" }).ShouldBe(Tokens.Colors.Rgb(moss.Dark));
            Mascots.Accent(new SettingsData { MascotAccent = "retired" }).ShouldBe(Tokens.Colors.Accent);
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }
```

- [ ] **Step 2: Run, expect a compile failure**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~ChibiTests|FullyQualifiedName~MascotTests"`
Expected: build errors — `Mascots`, `ChibiFace`, `ChibiPose` not found.

- [ ] **Step 3: Add the tokens**

In `windows/src/VoxScribe.App/Design/DesignTokens.cs`, class `Size`, after `PillWindowHeight`:

```csharp

        /// <summary>The mascot's drawn width (its 120×140 sketch, scaled).</summary>
        public const double MascotWidth = 96;

        /// <summary>The mascot's drawn height. See <see cref="MascotWidth"/>.</summary>
        public const double MascotHeight = 112;

        /// <summary>The mascot's speech bubble. The mascot window sizes to its content.</summary>
        public const double MascotBubbleWidth = 280;
```

In class `Material`, after `PillScreenMargin`:

```csharp

        /// <summary>A mascot's preview in the Appearance picker.</summary>
        public const double MascotPreviewSize = 56;
```

- [ ] **Step 4: Implement `Sketch`**

Create `windows/src/VoxScribe.App/Views/Chibi/Sketch.cs`:

```csharp
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Chibi;

/// <summary>What a mascot is doing; one drawing per pose.</summary>
internal enum ChibiPose
{
    /// <summary>Dozing on the desktop.</summary>
    Idle,

    /// <summary>The key is held.</summary>
    Listen,

    /// <summary>Transcribing / cleaning the tail.</summary>
    Think,

    /// <summary>The text landed.</summary>
    Done,

    /// <summary>A failure notice is lingering.</summary>
    Worried,
}

/// <summary>How a moving part moves. See <see cref="ChibiMotion"/>.</summary>
internal enum ChibiMove
{
    /// <summary>Height follows the voice (mouths).</summary>
    Talk,

    /// <summary>Height follows the voice with a per-bar wobble, from the bottom.</summary>
    Eq,

    /// <summary>Opacity breathes.</summary>
    Pulse,

    /// <summary>Turns slowly.</summary>
    Spin,

    /// <summary>Closes for a moment every few seconds (eyes).</summary>
    Blink,

    /// <summary>Rocks ±12° (tails).</summary>
    Wag,

    /// <summary>Rocks ±8°, slowly (a thinking head).</summary>
    Tilt,

    /// <summary>Narrows and widens (wings).</summary>
    Flap,

    /// <summary>Widens and flattens with the voice (jelly).</summary>
    Squish,

    /// <summary>Drifts up and fades (smoke, notes).</summary>
    Rise,

    /// <summary>Bobs up and down in whole pixels (pixel arms).</summary>
    Wave,
}

/// <summary>A moving part: the control, how it moves, and its stagger index.</summary>
internal sealed record Moving(Control Target, ChibiMove Move, int Index);

/// <summary>
/// Draws a mascot pose on a 120×140 canvas, the coordinate space of the mockups
/// (<c>.superpowers/brainstorm/…/chibi-all.html</c>), so their SVG path data ports verbatim.
/// </summary>
internal sealed class Sketch
{
    /// <summary>Canvas width, in mockup units.</summary>
    public const double Width = 120;

    /// <summary>Canvas height, in mockup units.</summary>
    public const double Height = 140;

    private readonly Stack<Canvas> _into = new();
    private readonly List<Moving> _moving = [];

    /// <summary>Starts an empty sketch.</summary>
    public Sketch() => _into.Push(Root);

    /// <summary>The drawing.</summary>
    public Canvas Root { get; } = new() { Width = Width, Height = Height };

    /// <summary>The parts the face animates each frame.</summary>
    public IReadOnlyList<Moving> Moving => _moving;

    /// <summary>An SVG path. Stroked paths get round caps and joins.</summary>
    public Path Path(string data, IBrush? fill, IBrush? stroke = null, double width = 0) => Add(new Path
    {
        Data = Geometry.Parse(data),
        Fill = fill,
        Stroke = stroke,
        StrokeThickness = width,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
    });

    /// <summary>An ellipse centred on (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    public Ellipse Ellipse(double cx, double cy, double rx, double ry, IBrush? fill)
    {
        var e = new Ellipse { Width = 2 * rx, Height = 2 * ry, Fill = fill };
        Canvas.SetLeft(e, cx - rx);
        Canvas.SetTop(e, cy - ry);
        return Add(e);
    }

    /// <summary>A circle centred on (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    public Ellipse Circle(double cx, double cy, double r, IBrush? fill) => Ellipse(cx, cy, r, r, fill);

    /// <summary>A rounded rectangle.</summary>
    public Rectangle Rect(double x, double y, double w, double h, double radius, IBrush fill)
    {
        var r = new Rectangle { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Fill = fill };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return Add(r);
    }

    /// <summary>A one-line label centred in a box <paramref name="width"/> wide whose top-left is (x, y).</summary>
    public TextBlock Text(string text, double x, double y, double width, double size, IBrush fill)
    {
        var t = new TextBlock
        {
            Text = text,
            Width = width,
            FontFamily = Tokens.Fonts.Mono,
            FontSize = size,
            Foreground = fill,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        return Add(t);
    }

    /// <summary>Opens a group sharing the canvas coordinates; shapes go into it until <see cref="End"/>.</summary>
    public Canvas Begin()
    {
        var group = Add(new Canvas { Width = Width, Height = Height });
        _into.Push(group);
        return group;
    }

    /// <summary>Closes the group opened by <see cref="Begin"/>.</summary>
    public void End() => _into.Pop();

    /// <summary>
    /// Registers <paramref name="target"/> as a moving part and gives it the transform its move
    /// needs. <paramref name="origin"/> is the pivot in canvas units; by default the part's own
    /// centre (for <see cref="ChibiMove.Eq"/>, its bottom centre). Paths and groups start at the
    /// canvas origin, so their pivot must be given.
    /// </summary>
    public T Move<T>(T target, ChibiMove move, int index = 0, Point? origin = null)
        where T : Control
    {
        target.RenderTransform = move switch
        {
            ChibiMove.Spin or ChibiMove.Wag or ChibiMove.Tilt => new RotateTransform(),
            ChibiMove.Rise or ChibiMove.Wave => new TranslateTransform(),
            ChibiMove.Pulse => null,
            _ => new ScaleTransform(),
        };
        target.RenderTransformOrigin = origin is { } o
            ? new RelativePoint(o.X, o.Y, RelativeUnit.Absolute)
            : move == ChibiMove.Eq ? new RelativePoint(0.5, 1, RelativeUnit.Relative) : RelativePoint.Center;
        _moving.Add(new Moving(target, move, index));
        return target;
    }

    /// <summary>A dash pattern in pixels, converted to Avalonia's multiples of the stroke width.</summary>
    public static AvaloniaList<double> Dashes(double dash, double gap, double strokeWidth) =>
        [dash / strokeWidth, gap / strokeWidth];

    private T Add<T>(T control)
        where T : Control
    {
        _into.Peek().Children.Add(control);
        return control;
    }
}
```

- [ ] **Step 5: Implement `ChibiFace`**

Create `windows/src/VoxScribe.App/Views/Chibi/ChibiFace.cs`:

```csharp
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Pill;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// A desktop mascot: a character drawn per <see cref="ChibiPose"/>, a speech bubble with what
/// the pill would say, and the recording dot. Fed one <see cref="PillState"/> per frame by
/// <see cref="HudWindow"/>, exactly like a pill face, plus <see cref="PillPhase.Idle"/>.
/// </summary>
/// <remarks>
/// Unlike a pill face it is hit-testable (it can be dragged) — but never focusable. Colours
/// are captured at construction; the window swaps faces between dictations, never mid-one.
/// </remarks>
internal abstract class ChibiFace : PillFace
{
    private const double DotSize = 10;
    private const double DotInset = 2;
    private const double BubbleMetaSize = 11;
    private const double BubbleTextSize = 14;
    private const int BubbleChars = 34;
    private const double PulseStagger = 0.2;
    private const double WagPeriod = 1.2;
    private const double WagDegrees = 12;
    private const double TiltPeriod = 2;
    private const double TiltDegrees = 8;
    private const double BlinkClosed = 0.1;
    private const double GlowBlur = 10;
    private const double GlowOpacity = 0.8;
    private const uint DotsGrey = 0xCFD3DC;
    private const uint SweatBlue = 0x8FD3FF;
    private const uint Glint = 0xFFFFFF;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Ellipse _dot = Dot(DotSize);
    private readonly Border _bubble;
    private readonly TextBlock _bubbleHead;
    private readonly TextBlock _bubbleText;
    private readonly TailClip _bubbleHost;
    private readonly Decorator _figure = new();
    private readonly TranslateTransform _lift = new();
    private Sketch _sketch = new();
    private ChibiPose? _pose;
    private TimeSpan _poseStart;

    /// <summary>Builds the mascot with <paramref name="accent"/> on its accent part.</summary>
    protected ChibiFace(Color accent)
    {
        AccentColor = accent;
        Accent = new SolidColorBrush(accent);

        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = default;
        Transitions = null;
        IsHitTestVisible = true;

        // Transparent, not null: the whole face, gaps included, is something to grab.
        Background = Brushes.Transparent;

        _bubbleHead = Text(Tokens.Fonts.Mono, BubbleMetaSize, Tokens.Brushes.InkSecondary);
        _bubbleText = Text(Tokens.Fonts.Grotesque, BubbleTextSize, Tokens.Brushes.Ink);
        _bubbleHost = TailHost(_bubbleText);
        _bubble = new Border
        {
            Width = Tokens.Size.MascotBubbleWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Tokens.Colors.PillFill),
            BorderBrush = new SolidColorBrush(Tokens.Colors.PillEdge),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(Tokens.Radius.Panel),
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            IsVisible = false,
            Child = new StackPanel { Spacing = Tokens.Space.Tight, Children = { _bubbleHead, _bubbleHost } },
        };

        _dot.HorizontalAlignment = HorizontalAlignment.Right;
        _dot.VerticalAlignment = VerticalAlignment.Top;
        _dot.Margin = new Thickness(0, DotInset, DotInset, 0);
        _dot.IsVisible = false;

        _figure.RenderTransform = _lift;
        var figure = new Grid
        {
            Width = Tokens.Size.MascotWidth,
            Height = Tokens.Size.MascotHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { new Viewbox { Child = _figure }, _dot },
        };

        Child = new StackPanel { Spacing = Tokens.Space.Snug, Children = { _bubble, figure } };
    }

    /// <summary>The accent part's colour.</summary>
    protected Color AccentColor { get; }

    /// <summary>The accent part's brush.</summary>
    protected IBrush Accent { get; }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <summary>The pose currently drawn.</summary>
    internal ChibiPose Pose => _pose ?? ChibiPose.Idle;

    /// <summary>The speech bubble.</summary>
    internal Control Bubble => _bubble;

    /// <summary>Draws the character in <paramref name="pose"/>. Called on every pose change.</summary>
    protected abstract void Draw(Sketch s, ChibiPose pose);

    /// <inheritdoc />
    public override void Update(PillState state) => Update(state, _clock.Elapsed);

    /// <summary>Paints one frame at ambient time <paramref name="now"/> (its own clock in the app).</summary>
    internal void Update(PillState state, TimeSpan now)
    {
        var pose = state.Phase switch
        {
            PillPhase.Recording => ChibiPose.Listen,
            PillPhase.Working => ChibiPose.Think,
            PillPhase.Latency => ChibiPose.Done,
            PillPhase.Notice => ChibiPose.Worried,
            _ => ChibiPose.Idle,
        };
        if (pose != _pose)
        {
            _pose = pose;
            _poseStart = now;
            Redraw(pose);
        }

        _dot.IsVisible = state.Phase == PillPhase.Recording;
        Speak(state);
        Animate(now.TotalSeconds, (now - _poseStart).TotalSeconds, state.Phase == PillPhase.Recording ? state.Level : 0);
    }

    /// <summary>A brush for a character colour <c>0xRRGGBB</c>.</summary>
    protected static SolidColorBrush Rgb(uint rgb) => new(Tokens.Colors.Rgb(rgb));

    /// <summary>Two closed, downturned arcs centred on <paramref name="x1"/> and <paramref name="x2"/>.</summary>
    protected static void SleepyEyes(Sketch s, double x1, double x2, double y, IBrush ink, double width = 3) =>
        s.Path(FormattableString.Invariant($"M{x1 - 6} {y} q6 4 12 0 M{x2 - 6} {y} q6 4 12 0"), null, ink, width);

    /// <summary>Two ^^ arcs centred on <paramref name="x1"/> and <paramref name="x2"/>.</summary>
    protected static void HappyEyes(Sketch s, double x1, double x2, double y, IBrush ink, double width = 3.5) =>
        s.Path(FormattableString.Invariant($"M{x1 - 6} {y} q6 -7 12 0 M{x2 - 6} {y} q6 -7 12 0"), null, ink, width);

    /// <summary>A soft glow around <paramref name="control"/>, the accent by default.</summary>
    protected T Glow<T>(T control, Color? colour = null)
        where T : Control
    {
        control.Effect = new DropShadowEffect
        {
            Color = colour ?? AccentColor,
            BlurRadius = GlowBlur,
            OffsetX = 0,
            OffsetY = 0,
            Opacity = GlowOpacity,
        };
        return control;
    }

    private void Redraw(ChibiPose pose)
    {
        _sketch = new Sketch();
        Draw(_sketch, pose);

        switch (pose)
        {
            case ChibiPose.Think:
                var grey = Rgb(DotsGrey);
                _sketch.Move(_sketch.Circle(92, 18, 3, grey), ChibiMove.Pulse, 0);
                _sketch.Move(_sketch.Circle(101, 12, 3, grey), ChibiMove.Pulse, 1);
                _sketch.Move(_sketch.Circle(110, 6, 3, grey), ChibiMove.Pulse, 2);
                break;
            case ChibiPose.Done:
                var sparkles = _sketch.Path(
                    "M14 30 l3 7 7 3 -7 3 -3 7 -3 -7 -7 -3 7 -3z M104 22 l2 5 5 2 -5 2 -2 5 -2 -5 -5 -2 5 -2z", Accent);
                _sketch.Move(sparkles, ChibiMove.Pulse);
                break;
            case ChibiPose.Worried:
                _sketch.Path("M100 28 q7 10 0 15 q-7 -5 0 -15z", Rgb(SweatBlue));
                _sketch.Circle(99, 38, 1.5, Rgb(Glint));
                break;
        }

        _figure.Child = _sketch.Root;
    }

    private void Speak(PillState state)
    {
        _bubble.IsVisible = state.Phase != PillPhase.Idle;
        if (!_bubble.IsVisible) return;

        var modeWord = state.Mode switch { "CLEAN" => "clean", "CMD" => "cmd", "EDIT" => "edit", _ => "raw" };
        _bubbleHead.Text = state.Phase switch
        {
            PillPhase.Working => (state.Mode switch { "CLEAN" => "tidying…", "EDIT" => "editing…", _ => "transcribing…" })
                + " · " + modeWord,
            PillPhase.Notice => "note",
            _ => TimerLine(state, modeWord),
        };

        var show = HasPreview(state);
        if (show) ShowPreview(_bubbleHost, _bubbleText, state, BubbleChars);
        _bubbleHost.IsVisible = show;
    }

    private void Animate(double t, double sincePose, double level)
    {
        _lift.Y = _pose == ChibiPose.Done ? ChibiMotion.Hop(sincePose) : ChibiMotion.Bob(t);

        foreach (var (target, move, index) in _sketch.Moving)
        {
            switch (move)
            {
                case ChibiMove.Talk:
                    ((ScaleTransform)target.RenderTransform!).ScaleY = ChibiMotion.Talk(level);
                    break;
                case ChibiMove.Eq:
                    ((ScaleTransform)target.RenderTransform!).ScaleY = ChibiMotion.Eq(level, index, t);
                    break;
                case ChibiMove.Blink:
                    ((ScaleTransform)target.RenderTransform!).ScaleY = ChibiMotion.Blinking(t) ? BlinkClosed : 1;
                    break;
                case ChibiMove.Flap:
                    ((ScaleTransform)target.RenderTransform!).ScaleX = ChibiMotion.Flap(t);
                    break;
                case ChibiMove.Squish:
                    var (x, y) = ChibiMotion.Squish(level);
                    var squash = (ScaleTransform)target.RenderTransform!;
                    squash.ScaleX = x;
                    squash.ScaleY = y;
                    break;
                case ChibiMove.Pulse:
                    target.Opacity = ChibiMotion.Pulse(t + (index * PulseStagger));
                    break;
                case ChibiMove.Spin:
                    ((RotateTransform)target.RenderTransform!).Angle = ChibiMotion.Spin(t);
                    break;
                case ChibiMove.Wag:
                    ((RotateTransform)target.RenderTransform!).Angle = ChibiMotion.Swing(t, WagPeriod, WagDegrees);
                    break;
                case ChibiMove.Tilt:
                    ((RotateTransform)target.RenderTransform!).Angle = ChibiMotion.Swing(t, TiltPeriod, TiltDegrees);
                    break;
                case ChibiMove.Rise:
                    var (dy, opacity) = ChibiMotion.Rise(t, index);
                    ((TranslateTransform)target.RenderTransform!).Y = dy;
                    target.Opacity = opacity;
                    break;
                case ChibiMove.Wave:
                    ((TranslateTransform)target.RenderTransform!).Y = ChibiMotion.Wave(t, index);
                    break;
            }
        }
    }
}
```

If the analyzers flag the magic numbers in `Redraw` (dots, sparkles, sweat drop), they are drawing coordinates of the 120×140 sketch, like the path data in every character; keep them inline as the characters do, and suppress nothing — if a rule fires, extract them into `private const` with descriptive names (`ThinkDot1X`, …).

- [ ] **Step 6: Implement the Slime**

Create `windows/src/VoxScribe.App/Views/Chibi/SlimeChibi.cs`:

```csharp
using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Slime, Rimuru-style: an almost spherical light-blue drop with a small peak and a big shine.
/// Squashes with the voice. Its body stays blue; the accent is its glow and sparkles.
/// </summary>
internal sealed class SlimeChibi(Color accent) : ChibiFace(accent)
{
    private const uint Gel = 0x7CCBF4;
    private const uint Edge = 0x4FA8DC;
    private const uint Shine = 0xFFFFFF;
    private const uint Ink = 0x1F4E6E;
    private const uint Shadow = 0x000000;
    private const double ShadowOpacity = 0.2;
    private const double UnderOpacity = 0.25;
    private const double ShineOpacity = 0.85;
    private const double ShineTilt = -30;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);
        s.Ellipse(60, 132, 38, 4, Rgb(Shadow)).Opacity = ShadowOpacity;

        var body = s.Begin();
        if (pose == ChibiPose.Listen) s.Move(body, ChibiMove.Squish, origin: new Point(60, 130));

        var gel = s.Path("M60 50 q4 0 8 6 q32 8 34 44 q1 30 -42 30 q-43 0 -42 -30 q2 -36 34 -44 q4 -6 8 -6z", Rgb(Gel), Rgb(Edge), 2);
        if (pose == ChibiPose.Listen) Glow(gel);
        s.Ellipse(60, 122, 34, 7, Rgb(Edge)).Opacity = UnderOpacity;
        var shine = s.Ellipse(40, 76, 9, 6, Rgb(Shine));
        shine.Opacity = ShineOpacity;
        shine.RenderTransform = new RotateTransform(ShineTilt);
        s.Circle(52, 66, 2.5, Rgb(Shine)).Opacity = ShineOpacity;

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 48, 72, 92, ink, 2.6);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 48, 72, 94, ink, 3);
                s.Path("M55 102 q5 5 10 0", null, ink, 2.4);
                break;
            default:
                var ey = pose is ChibiPose.Think or ChibiPose.Worried ? 88 : 92;
                s.Ellipse(48, ey, 3.4, 5.5, ink);
                s.Ellipse(72, ey, 3.4, 5.5, ink);
                if (pose == ChibiPose.Listen) s.Move(s.Ellipse(60, 104, 4, 3, ink), ChibiMove.Talk);
                break;
        }

        s.End();
    }
}
```

- [ ] **Step 7: Implement the catalogue (Slime only for now)**

Create `windows/src/VoxScribe.App/Views/Chibi/Mascots.cs`:

```csharp
using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Chibi;

/// <summary>A selectable mascot.</summary>
/// <param name="Id">Settings id.</param>
/// <param name="Label">Display name.</param>
/// <param name="Create">Builds the face with an accent colour.</param>
internal sealed record Mascot(string Id, string Label, Func<Color, ChibiFace> Create);

/// <summary>The mascot catalogue and the rules for which one, if any, is shown.</summary>
internal static class Mascots
{
    /// <summary>Every mascot, in picker order.</summary>
    public static IReadOnlyList<Mascot> All { get; } =
    [
        new("slime", "Slime", a => new SlimeChibi(a)),
    ];

    /// <summary>Every theme's accent variants: the mascot accent override's choices.</summary>
    public static IReadOnlyList<AccentVariant> Accents { get; } = [.. Themes.All.SelectMany(t => t.Variants)];

    /// <summary>The mascot with <paramref name="id"/>, or null.</summary>
    public static Mascot? Find(string? id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The mascot to show instead of the pill, or null for the pill.</summary>
    public static Mascot? Active(SettingsData d) => d.MascotHidden ? null : Find(d.Mascot);

    /// <summary>The pinned accent in the current light/dark mode, or the theme's accent.</summary>
    public static Color Accent(SettingsData d) =>
        Accents.FirstOrDefault(v => string.Equals(v.Id, d.MascotAccent, StringComparison.OrdinalIgnoreCase)) is { } v
            ? Tokens.Colors.Rgb(Themes.IsDark ? v.Dark : v.Light)
            : Tokens.Colors.Accent;
}
```

- [ ] **Step 8: Run the tests, expect PASS**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~ChibiTests|FullyQualifiedName~MascotTests|FullyQualifiedName~ChibiMotionTests"`
Expected: all pass. If `Nothing_on_a_mascot_is_red_but_the_dot` fails, the message names the control type and phase: fix the colour, never the detector.

- [ ] **Step 9: Prove the red gate can fail**

Temporarily change `SlimeChibi.Gel` to `0xE5484D`, run `--filter "FullyQualifiedName~Nothing_on_a_mascot_is_red"`, expect FAIL naming `Path`. Revert to `0x7CCBF4`, re-run, expect PASS.

- [ ] **Step 10: Full suite, warnings gate, commit, push**

```bash
cd windows && dotnet test VoxScribe.CrossPlatform.slnf && dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror
cd .. && git add windows && git commit -m "feat: chibi face base, sketch, slime and mascot catalogue" && git push
```

---

### Task 3: Mascot mode in HudWindow, app wiring, tray — then the hand check

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/HudWindow.cs` (whole `HudWindow` class, lines 13-188; `Overlay.MakeOverlay`, lines 225-234)
- Modify: `windows/src/VoxScribe.App/App.axaml:19-22`
- Modify: `windows/src/VoxScribe.App/App.axaml.cs:50-52` and add `OnTrayMascot`

**Interfaces:**
- Consumes: `Mascots.Active/Accent/All`, `Mascot.Create`, `ChibiFace`, `MascotPlacement.Anchor`, `PillPhase.Idle`, settings fields.
- Produces: `HudWindow(DictationEngine engine, AppSettings settings, Action showMain, Action showSettings)`; `Overlay.MakeOverlay(Window window, bool clickThrough = true)`.

No automated test: `HudWindow` needs a live `DictationEngine` and the behaviour that matters (focus, click-through, drag) only exists with a real window manager. The pure parts were tested in Tasks 1–2. This task ends with a **hand check that gates every later task**.

- [ ] **Step 1: Overlay sets or clears click-through**

Replace `MakeOverlay` in `windows/src/VoxScribe.App/Views/HudWindow.cs`:

```csharp
    /// <summary>
    /// Stamps the overlay extended styles on. Call each time the window is shown. NOACTIVATE
    /// and TOOLWINDOW always; TRANSPARENT (clicks fall through) only when
    /// <paramref name="clickThrough"/> — the pill, not the mascot, which must be grabbable.
    /// Cleared as well as set, because the same window switches between the two.
    /// </summary>
    public static void MakeOverlay(Window window, bool clickThrough = true)
    {
        var hwnd = HandleOf(window);
        if (hwnd == IntPtr.Zero) return;

        var style = GetWindowLongPtrW(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow;
        style = clickThrough ? style | WsExTransparent : style & ~(nint)WsExTransparent;
        SetWindowLongPtrW(hwnd, GwlExStyle, style);
        KeepOnTop(window);
    }
```

- [ ] **Step 2: Replace the `HudWindow` class**

Replace the `HudWindow` class (from its `/// <summary>` through the closing brace before the `Overlay` doc comment) with:

```csharp
/// <summary>
/// The dictation indicator. In pill mode: a fixed, transparent, click-through window at the
/// bottom of the screen hosting the active theme's <see cref="PillFace"/>, hidden when idle.
/// In mascot mode: a <see cref="ChibiFace"/> that stays on the desktop, can be dragged, and
/// sizes to its content, anchored at its saved bottom-left corner.
/// </summary>
/// <remarks>
/// <para>
/// <b>This window must never take focus.</b> The text lands wherever the caret is, so
/// activating the overlay would redirect the injection into nothing. Hence
/// <see cref="Window.ShowActivated"/> false, nothing focusable, and
/// <see cref="Overlay.MakeOverlay"/>'s NOACTIVATE style in both modes. The pill is also
/// hit-test invisible and TRANSPARENT; the mascot is neither, so it can be grabbed.
/// </para>
/// <para>
/// Polled at ~30 fps, not pushed: the engine raises Changed at buffer rate on a worker thread.
/// Plain transparency, no acrylic — system blur backs the whole rectangle.
/// </para>
/// </remarks>
public sealed class HudWindow : Window
{
    private readonly DictationEngine _engine;
    private readonly AppSettings _settings;
    private readonly Action _showMain;
    private readonly Action _showSettings;
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _noticeClock = new();
    private readonly Stopwatch _latencyClock = new();
    private readonly DispatcherTimer _timerTick;

    private PillFace _face;
    private Mascot? _mascot;
    private (string? Id, bool Hidden, string? Accent) _mascotKey;
    private PixelPoint _anchor;
    private bool _faceStale;
    private TimeSpan? _lastLatency;
    private TimeSpan? _lastCleanup;
    private bool _lastCleanupFailed;
    private int _frames;

    /// <summary>Builds the indicator over <paramref name="engine"/> and starts watching it.</summary>
    /// <param name="engine">The dictation engine to mirror.</param>
    /// <param name="settings">Which indicator, which mascot, and where it sits.</param>
    /// <param name="showMain">Opens the main window (mascot double-click).</param>
    /// <param name="showSettings">Opens Settings → Appearance (mascot menu).</param>
    public HudWindow(DictationEngine engine, AppSettings settings, Action showMain, Action showSettings)
    {
        _engine = engine;
        _settings = settings;
        _showMain = showMain;
        _showSettings = showSettings;

        // Raised on a worker thread; a torn TimeSpan? read would only garble one frame.
        engine.Completed += OnEngineCompleted;

        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _mascotKey = KeyOf(settings.Data);
        _mascot = Mascots.Active(settings.Data);
        _face = CreateFace();
        Content = _face;
        ApplyMode();

        // Swapped only between dictations: a theme or mascot change never repaints one in progress.
        Themes.Changed += OnThemeChanged;
        settings.Changed += OnSettingsChanged;

        // The bubble grows and shrinks the mascot window; keep its bottom-left corner put.
        SizeChanged += (_, e) =>
        {
            if (_mascot is null || !IsVisible) return;
            Position = new PixelPoint(_anchor.X, _anchor.Y - (int)Math.Ceiling(e.NewSize.Height * RenderScaling));
        };

        _timerTick = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tokens.Motion.PillFrame };
        _timerTick.Tick += (_, _) => Sync();
        _timerTick.Start();

        // A singleton for the app's lifetime, but a closed window must still let go of
        // everything it subscribed to, rather than outlive its usefulness pinned by events.
        Closed += (_, _) =>
        {
            _timerTick.Stop();
            Themes.Changed -= OnThemeChanged;
            settings.Changed -= OnSettingsChanged;
            engine.Completed -= OnEngineCompleted;
        };
    }

    private static (string?, bool, string?) KeyOf(SettingsData d) => (d.Mascot, d.MascotHidden, d.MascotAccent);

    private void OnEngineCompleted(object? sender, DictationResult result)
    {
        _lastLatency = result.ProcessingTime;
        _lastCleanup = result.CleanupTime;
        _lastCleanupFailed = result.CleanupFailed == true;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _faceStale = true;

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // A drag saves the position, which raises Changed too; only a change of mascot,
        // visibility or accent rebuilds the face.
        var key = KeyOf(_settings.Data);
        if (key == _mascotKey) return;
        _mascotKey = key;
        _faceStale = true;
    }

    private string Mode() =>
        _engine.CommandThisUtterance ? "CMD"
        : _engine.EditThisUtterance ? "EDIT"
        : _engine.CleaningThisUtterance ? "CLEAN" : "RAW";

    private PillFace CreateFace()
    {
        if (_mascot is null) return PillFace.Create(Themes.Active.Pill);

        var face = _mascot.Create(Mascots.Accent(_settings.Data));
        face.PointerPressed += OnMascotPressed;
        face.ContextMenu = MascotMenu();
        return face;
    }

    private void ApplyMode()
    {
        if (_mascot is null)
        {
            SizeToContent = SizeToContent.Manual;
            Width = Tokens.Size.PillWindowWidth;
            Height = Tokens.Size.PillWindowHeight;
            IsHitTestVisible = false;
        }
        else
        {
            SizeToContent = SizeToContent.WidthAndHeight;
            IsHitTestVisible = true;
        }
    }

    private void Rebuild()
    {
        _faceStale = false;
        var wasMascot = _mascot is not null;
        _mascot = Mascots.Active(_settings.Data);

        // Switching pill ⇄ mascot: hide now, and let the next show apply the new mode's
        // position and overlay styles from scratch.
        if (IsVisible && wasMascot != (_mascot is not null)) Hide();

        _face = CreateFace();
        Content = _face;
        ApplyMode();
    }

    private void Sync()
    {
        var state = _engine.State;

        if (state == DictationState.Idle)
        {
            // A failure notice holds the indicator up briefly: the moment the user looks here
            // is the moment their text failed to appear.
            if (_engine.Notice is { Length: > 0 } notice)
            {
                if (!_noticeClock.IsRunning) _noticeClock.Restart();
                if (IsVisible && _noticeClock.Elapsed < Tokens.Motion.NoticeLinger)
                {
                    _face.Update(new PillState(PillPhase.Notice, 0, _clock.Elapsed, Mode(), notice));
                    return;
                }
            }
            else
            {
                _noticeClock.Reset();
            }

            // After a clean finish, hold long enough to read the felt latency.
            if (_lastLatency is { } latency)
            {
                if (!_latencyClock.IsRunning) _latencyClock.Restart();
                if (IsVisible && _latencyClock.Elapsed < Tokens.Motion.LatencyLinger)
                {
                    _face.Update(new PillState(PillPhase.Latency, 0, latency, Mode(), string.Empty,
                        Cleanup: _lastCleanup, CleanupFailed: _lastCleanupFailed));
                    return;
                }

                _lastLatency = null;
            }

            _latencyClock.Reset();

            if (_faceStale) Rebuild();

            if (_mascot is null)
            {
                if (IsVisible) Hide();
            }
            else
            {
                _face.Update(new PillState(PillPhase.Idle, 0, TimeSpan.Zero, Mode(), string.Empty));
                ShowIfHidden();
            }

            _clock.Reset();
            return;
        }

        var recording = state == DictationState.Recording;
        if (recording && !_clock.IsRunning) _clock.Restart();
        if (!recording) _clock.Stop();

        // The linger counts from the moment the engine idles; a new press forgets the old reading.
        _latencyClock.Reset();
        if (recording) _lastLatency = null;

        // Perceptual lift: speech RMS sits low in [0,1]; gain then sqrt makes it visibly move.
        var level = recording ? Math.Sqrt(Math.Clamp(_engine.Level * Tokens.Motion.LevelGain, 0, 1)) : 0;
        _face.Update(new PillState(
            recording ? PillPhase.Recording : PillPhase.Working, level, _clock.Elapsed, Mode(), _engine.PartialText));

        ShowIfHidden();
    }

    private void ShowIfHidden()
    {
        if (!IsVisible)
        {
            if (_mascot is null) PositionBottomCenter();
            else PlaceMascot();

            Show();
            _frames = 0;
            Overlay.MakeOverlay(this, clickThrough: _mascot is null);
        }
        else if (++_frames % 15 == 0)
        {
            // A window going full-screen pushes itself to the front of the topmost band.
            Overlay.KeepOnTop(this);
        }
    }

    private void PositionBottomCenter()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null) return;

        var area = screen.WorkingArea;
        var width = (int)(Width * screen.Scaling);
        var height = (int)(Height * screen.Scaling);
        var margin = (int)(Tokens.Material.PillScreenMargin * screen.Scaling);

        Position = new PixelPoint(area.X + ((area.Width - width) / 2), area.Y + area.Height - height - margin);
    }

    private void PlaceMascot()
    {
        var scaling = Screens.Primary?.Scaling ?? 1;
        _face.Measure(Size.Infinity);
        var size = new PixelSize(
            (int)Math.Ceiling(_face.DesiredSize.Width * scaling), (int)Math.Ceiling(_face.DesiredSize.Height * scaling));
        var areas = Screens.All.OrderByDescending(s => s.IsPrimary).Select(s => s.WorkingArea).ToList();
        var saved = _settings.Data is { MascotX: { } x, MascotY: { } y } ? new PixelPoint(x, y) : (PixelPoint?)null;

        _anchor = MascotPlacement.Anchor(saved, areas, size, (int)(Tokens.Material.PillScreenMargin * scaling));
        Position = new PixelPoint(_anchor.X, _anchor.Y - size.Height);
    }

    private void OnMascotPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            _showMain();
            return;
        }

        // On Windows the move loop is modal: this returns once the button is released.
        BeginMoveDrag(e);

        var anchor = new PixelPoint(Position.X, Position.Y + (int)Math.Ceiling(Bounds.Height * RenderScaling));
        if (anchor == _anchor) return;

        _anchor = anchor;
        _settings.Update(_settings.Data with { MascotX = anchor.X, MascotY = anchor.Y });
    }

    private ContextMenu MascotMenu()
    {
        var characters = new MenuItem { Header = "Character" };
        foreach (var mascot in Mascots.All)
        {
            var item = new MenuItem
            {
                Header = mascot.Label,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = mascot.Id == _mascot?.Id,
            };
            item.Click += (_, _) => _settings.Update(_settings.Data with { Mascot = mascot.Id });
            characters.Items.Add(item);
        }

        var hide = new MenuItem { Header = "Hide mascot" };
        hide.Click += (_, _) => _settings.Update(_settings.Data with { MascotHidden = true });

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => _showSettings();

        return new ContextMenu { Items = { characters, hide, new Separator(), settings } };
    }
}
```

Add these `using`s at the top of `HudWindow.cs`: `using Avalonia.Input;`, `using VoxScribe.App.Views.Chibi;`. (`Avalonia.Controls`, `Avalonia.Media`, `Avalonia.Threading`, `VoxScribe.Core` are already there.)

- [ ] **Step 3: Wire it in `App`**

In `windows/src/VoxScribe.App/App.axaml.cs`, replace:

```csharp
            // The dictation pill manages its own visibility from the engine state; it only
            // needs to exist. Never becomes MainWindow — it must never own focus.
            if (_composition.Engine is not null) _ = new HudWindow(_composition.Engine);
```

with:

```csharp
            // The dictation indicator (pill or mascot) manages its own visibility from the engine
            // state and the settings; it only needs to exist. Never becomes MainWindow — it must
            // never own focus.
            if (_composition.Engine is not null)
            {
                _ = new HudWindow(_composition.Engine, settings, ShowMain, () =>
                {
                    ShowMain();
                    _main?.ShowSettings(SettingsTab.Appearance);
                });
            }
```

Add, next to `OnTraySettings`:

```csharp
    /// <summary>
    /// Shows or hides the mascot. With none chosen yet, opens Settings → Appearance, where one
    /// is picked.
    /// </summary>
    private void OnTrayMascot(object? sender, EventArgs e)
    {
        if (_composition?.Settings is not { } settings) return;

        if (settings.Data.Mascot is null)
        {
            ShowMain();
            _main?.ShowSettings(SettingsTab.Appearance);
            return;
        }

        settings.Update(settings.Data with { MascotHidden = !settings.Data.MascotHidden });
    }
```

In `windows/src/VoxScribe.App/App.axaml`, after the `Settings…` item:

```xml
            <NativeMenuItem Header="Show/hide mascot" Click="OnTrayMascot" />
```

Also update the comment in `App.axaml.cs` "The pill only shows while dictating, so a read-aloud failure…" — leave it; still true in pill mode and harmless in mascot mode.

- [ ] **Step 4: Build, full suite, warnings gate**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf && dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror`
Expected: all green, no warnings.

Then build the Windows app too (the platform layer is excluded from the slnf): `cd windows && dotnet build VoxScribe.sln -c Release --no-incremental -warnaserror`. Expected: success.

- [ ] **Step 5: Commit and push**

```bash
git add windows && git commit -m "feat: mascot mode in the dictation indicator" && git push
```

- [ ] **Step 6: HAND CHECK — stop here and hand over to the owner**

Build and install the app the usual way (`pwsh windows/build-installer.ps1`, run the produced setup, relaunch). Then, with VoxScribe closed, add `"Mascot": "slime"` to `%LOCALAPPDATA%\VoxScribe\settings.json` and start it. Check, and report each as pass/fail:

1. The slime appears bottom-right of the primary screen and floats gently.
2. Put the caret in a text editor. Drag the slime somewhere else. Dictate. **The text lands in the editor.**
3. Right-click the slime: the menu opens; close it with Esc; dictate again — the text still lands in the editor.
4. Click just above the idle slime (where the bubble appears while dictating): the click reaches the window underneath. If the whole former bubble area blocks clicks, report it.
5. While dictating: the red dot shows, the bubble shows the live text above the slime, and the slime does not jump (its bottom-left stays put).
6. Double-click the slime: the main window opens.
7. Quit and restart: the slime comes back where it was left.
8. Tray → Show/hide mascot: it disappears; dictate → the pill shows as before; tray again → the slime is back.
9. Open a full-screen video (browser F11): the slime stays on top.
10. Second monitor, if any: drag it there, restart, it is still there. Unplug (or change the saved X to 99999): it comes back bottom-right.

If 2 or 3 fail (focus stolen): replace `BeginMoveDrag` with pointer capture + moving `Position` on `PointerMoved`, and report back before continuing. If 4 fails, the window keeps an invisible rectangle: report back; the fallback is to put the bubble in its own click-through child window. **Do not start Task 4 until 2, 3 and 4 pass.**

---

### Task 4: Appearance section — mascot picker and mascot accent

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs`
- Test: `windows/tests/VoxScribe.App.Tests/AppearanceSectionTests.cs`

**Interfaces:**
- Consumes: `Mascots.All/Accents/Accent`, `Mascot.Create`, `PillPhase.Idle`, `Tokens.Material.MascotPreviewSize`.
- Produces: buttons named `"Mascot: Pill"`, `"Mascot: <Label>"`, `"Mascot accent: Theme"`, `"Mascot accent: <Variant label>"`.

- [ ] **Step 1: Write the failing tests**

Append to `windows/tests/VoxScribe.App.Tests/AppearanceSectionTests.cs` (inside the class; add `using VoxScribe.App.Views.Chibi;`):

```csharp
    private static IEnumerable<string?> Names(Control section, string prefix) =>
        section.GetLogicalDescendants().OfType<Button>()
            .Select(b => AutomationProperties.GetName(b))
            .Where(n => n?.StartsWith(prefix, StringComparison.Ordinal) == true);

    [AvaloniaFact]
    public void The_pill_then_every_mascot_in_order()
    {
        var section = AppearanceSection.Build(Settings(), _ => { }, engine: null);

        Names(section, "Mascot: ").ShouldBe(["Mascot: Pill", .. Mascots.All.Select(m => $"Mascot: {m.Label}")]);
    }

    [AvaloniaFact]
    public void Picking_a_mascot_saves_it_and_shows_it_again()
    {
        var settings = Settings();
        settings.Update(settings.Data with { MascotHidden = true });
        var section = AppearanceSection.Build(settings, settings.Update, engine: null);

        Click(Named(section, $"Mascot: {Mascots.All[0].Label}"));

        settings.Data.Mascot.ShouldBe(Mascots.All[0].Id);
        settings.Data.MascotHidden.ShouldBeFalse("picking a mascot means wanting to see it");
    }

    [AvaloniaFact]
    public void Picking_the_pill_clears_the_mascot()
    {
        var settings = Settings();
        settings.Update(settings.Data with { Mascot = Mascots.All[0].Id });
        var section = AppearanceSection.Build(settings, settings.Update, engine: null);

        Click(Named(section, "Mascot: Pill"));

        settings.Data.Mascot.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Mascot_accents_appear_only_with_a_mascot()
    {
        Names(AppearanceSection.Build(Settings(), _ => { }, engine: null), "Mascot accent: ").ShouldBeEmpty();

        var settings = Settings();
        settings.Update(settings.Data with { Mascot = Mascots.All[0].Id });
        Names(AppearanceSection.Build(settings, _ => { }, engine: null), "Mascot accent: ")
            .ShouldBe(["Mascot accent: Theme", .. Mascots.Accents.Select(v => $"Mascot accent: {v.Label}")]);
    }

    [AvaloniaFact]
    public void Picking_a_mascot_accent_pins_it_and_theme_unpins_it()
    {
        var settings = Settings();
        settings.Update(settings.Data with { Mascot = Mascots.All[0].Id });

        Click(Named(AppearanceSection.Build(settings, settings.Update, engine: null), "Mascot accent: Moss"));
        settings.Data.MascotAccent.ShouldBe("moss");

        Click(Named(AppearanceSection.Build(settings, settings.Update, engine: null), "Mascot accent: Theme"));
        settings.Data.MascotAccent.ShouldBeNull();
    }
```

Also check the existing `Swatches_are_exactly_the_saved_themes_variants` still filters on `"Accent: "` — the new names start with `"Mascot accent: "`, which `StartsWith("Accent: ")` does not match. No change needed.

- [ ] **Step 2: Run, expect FAIL**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~AppearanceSectionTests"`
Expected: the five new tests fail (no such buttons).

- [ ] **Step 3: Implement the picker**

In `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs`, add `using VoxScribe.App.Views.Chibi;` and `using VoxScribe.App.Views.Pill;`.

Change the `Swatch` signature and its two name lines so the prefix is a parameter:

```csharp
    private static Button Swatch(AccentVariant variant, bool selected, Action pick, string prefix = "Accent")
```

```csharp
        AutomationProperties.SetName(swatch, $"{prefix}: {variant.Label}");
```

In `Build`, before `return Panels.Section(...)`, add:

```csharp
        var mascots = new WrapPanel { ItemSpacing = Tokens.Space.Base, LineSpacing = Tokens.Space.Base };
        mascots.Children.Add(MascotCard(null, "Pill", settings.Data.Mascot is null, () =>
        {
            if (IsBusy(engine)) return;
            save(settings.Data with { Mascot = null });
        }));
        foreach (var mascot in Mascots.All)
        {
            var selected = string.Equals(mascot.Id, settings.Data.Mascot, StringComparison.OrdinalIgnoreCase);
            mascots.Children.Add(MascotCard(mascot, mascot.Label, selected, () =>
            {
                if (IsBusy(engine)) return;
                save(settings.Data with { Mascot = mascot.Id, MascotHidden = false });
            }));
        }

        var mascotAccents = new WrapPanel { ItemSpacing = Tokens.Space.Base, LineSpacing = Tokens.Space.Base };
        mascotAccents.Children.Add(ThemeAccentChoice(settings.Data.MascotAccent is null, () =>
        {
            if (IsBusy(engine)) return;
            save(settings.Data with { MascotAccent = null });
        }));
        foreach (var variant in Mascots.Accents)
        {
            var selected = string.Equals(variant.Id, settings.Data.MascotAccent, StringComparison.OrdinalIgnoreCase);
            mascotAccents.Children.Add(Swatch(variant, selected, () =>
            {
                if (IsBusy(engine)) return;
                save(settings.Data with { MascotAccent = variant.Id });
            }, "Mascot accent"));
        }
```

Replace the `return Panels.Section(...)` statement at the end of `Build` with the following. The mascot-accent row is only added when a mascot is chosen (an invisible row would still show up in the logical tree the tests walk):

```csharp
        var panel = new StackPanel
        {
            Spacing = Tokens.Space.Base,
            Children =
            {
                Panels.Labelled("THEME", cards),
                Panels.Labelled("ACCENT", swatches),
                Panels.Labelled("DICTATION INDICATOR", mascots),
                Panels.Note("Applies immediately. Light or dark follows Windows "
                    + "(Settings → Personalisation → Colours). Drag the mascot anywhere; "
                    + "right-click it for its menu, double-click to open Vox-Scribe."),
            },
        };
        if (settings.Data.Mascot is not null) panel.Children.Insert(3, Panels.Labelled("MASCOT ACCENT", mascotAccents));

        return Panels.Section("APPEARANCE", panel);
```

Add the two builders at the end of the class:

```csharp
    private static Button MascotCard(Mascot? mascot, string label, bool selected, Action pick)
    {
        Control preview;
        if (mascot is null)
        {
            preview = new Border
            {
                Width = Tokens.Material.MascotPreviewSize,
                Height = Tokens.Material.SwatchSize,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(Tokens.Radius.Pill),
                Background = new SolidColorBrush(Tokens.Colors.PillFill),
                BorderBrush = new SolidColorBrush(Tokens.Colors.PillEdge),
                BorderThickness = new Thickness(Tokens.Border.Hairline),
            };
        }
        else
        {
            var face = mascot.Create(Mascots.Accent(new SettingsData()));
            face.IsHitTestVisible = false;
            face.Update(new PillState(PillPhase.Idle, 0, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
            preview = new Viewbox { Width = Tokens.Material.MascotPreviewSize, Height = Tokens.Material.MascotPreviewSize, Child = face };
        }

        var borderBrush = selected ? Tokens.Brushes.Accent : Brushes.Transparent;
        var card = new Button
        {
            Padding = new Thickness(Tokens.Space.Snug),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(Tokens.Border.Ring),
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(Tokens.Radius.Panel),
            Content = new StackPanel
            {
                Spacing = Tokens.Space.Tight,
                Children =
                {
                    new Border { Height = Tokens.Material.MascotPreviewSize, Child = preview, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock
                    {
                        Text = label,
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Label,
                        Foreground = Tokens.Brushes.InkSecondary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
        AutomationProperties.SetName(card, $"Mascot: {label}");
        Shell.PinHoverStates(card, Tokens.Brushes.Hover, Tokens.Brushes.InkSecondary, borderBrush);
        card.Click += (_, _) => pick();
        return card;
    }

    private static Button ThemeAccentChoice(bool selected, Action pick)
    {
        var borderBrush = selected ? Tokens.Brushes.Ink : Brushes.Transparent;
        var choice = new Button
        {
            Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Tight),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(Tokens.Border.Ring),
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new TextBlock
            {
                Text = "Theme",
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Label,
                Foreground = Tokens.Brushes.InkSecondary,
            },
        };
        AutomationProperties.SetName(choice, "Mascot accent: Theme");
        ToolTip.SetTip(choice, "Follow the theme's accent");
        Shell.PinHoverStates(choice, Tokens.Brushes.Hover, Tokens.Brushes.InkSecondary, borderBrush);
        choice.Click += (_, _) => pick();
        return choice;
    }
```

The preview uses `Mascots.Accent(new SettingsData())` (the theme accent): previews show the character, not the override. The preview's `Update` takes `TimeSpan.Zero`, so every preview is the same still frame.

- [ ] **Step 4: Run, expect PASS; full suite; warnings gate**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf && dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror`
Expected: all green. (`ContrastTests` is unaffected: the cards use existing token pairs.)

- [ ] **Step 5: Commit and push**

```bash
git add windows && git commit -m "feat: mascot picker and mascot accent in Appearance" && git push
```

---

### Task 5: Characters — Servo-skull, Netrunner, Wizard

**Files:**
- Create: `windows/src/VoxScribe.App/Views/Chibi/ServoSkullChibi.cs`, `NetrunnerChibi.cs`, `WizardChibi.cs`
- Modify: `windows/src/VoxScribe.App/Views/Chibi/Mascots.cs` (`All`)
- Test: existing `ChibiTests` (every test iterates `Mascots.All`) and `AppearanceSectionTests.The_pill_then_every_mascot_in_order`.

**Interfaces:**
- Consumes: `ChibiFace`, `Sketch`, `ChibiMove`, `ChibiPose`, `Rgb`, `SleepyEyes`, `HappyEyes`, `Glow`.
- Produces: `ServoSkullChibi(Color)`, `NetrunnerChibi(Color)`, `WizardChibi(Color)`.

- [ ] **Step 1: Register them (the failing step)**

In `Mascots.All`, replace the list with:

```csharp
    [
        new("servo-skull", "Servo-skull", a => new ServoSkullChibi(a)),
        new("netrunner", "Netrunner", a => new NetrunnerChibi(a)),
        new("wizard", "Wizard", a => new WizardChibi(a)),
        new("slime", "Slime", a => new SlimeChibi(a)),
    ];
```

Run: `cd windows && dotnet build VoxScribe.CrossPlatform.slnf`
Expected: build errors, the three classes do not exist.

- [ ] **Step 2: Servo-skull**

Create `windows/src/VoxScribe.App/Views/Chibi/ServoSkullChibi.cs`:

```csharp
using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Servo-skull: a floating skull with a bionic eye in a brass ring, cables, and a parchment
/// that unrolls when the text lands — a servo-scribe. Accent: lens, antenna tip, vox grille.
/// </summary>
internal sealed class ServoSkullChibi(Color accent) : ChibiFace(accent)
{
    private const uint Brass = 0xC9A44C;
    private const uint Bone = 0xE9E1CC;
    private const uint Shade = 0xB9AE93;
    private const uint Socket = 0x2A2522;
    private const uint LensWell = 0x1E2A2A;
    private const uint Cable = 0x4A4F5A;
    private const uint Jaw = 0x7A8090;
    private const uint Parchment = 0xEFE3C2;
    private const uint ParchmentInk = 0x8B7A55;
    private const uint LensGlint = 0xE8FFF0;
    private const double FieldOpacity = 0.25;
    private const double DimLens = 0.45;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var listen = pose == ChibiPose.Listen;

        s.Path("M44 92 q-6 14 -2 26 M76 92 q8 10 4 24", null, Rgb(Cable), 2.5);
        if (pose == ChibiPose.Done)
        {
            s.Path("M50 100 h20 v34 q-10 4 -20 0z", Rgb(Parchment));
            s.Path("M53 106 h14 M53 111 h14 M53 116 h14 M53 121 h14 M53 126 h14", null, Rgb(ParchmentInk), 1.2);
        }
        else
        {
            s.Path("M50 100 h20 v12 q-10 4 -20 0z", Rgb(Parchment));
            s.Path("M53 105 h14", null, Rgb(ParchmentInk), 1.2);
        }

        s.Rect(46, 94, 28, 7, 3, Rgb(Brass));
        var field = s.Ellipse(60, 104, 12, 3, Accent);
        field.Opacity = FieldOpacity;
        s.Move(field, ChibiMove.Pulse);

        s.Path("M34 60 q0 -34 26 -34 q26 0 26 34 q0 12 -8 18 v12 h-36 v-12 q-8 -6 -8 -18z", Rgb(Bone));
        s.Path("M60 28 v10", null, Rgb(Shade), 1.5);
        s.Circle(48, 58, 8, Rgb(Socket));
        if (pose == ChibiPose.Done) s.Path("M42 59 q6 -7 12 0", null, Rgb(Bone), 3);
        s.Path("M57 68 l3 -6 3 6z", Rgb(Socket));

        var ring = s.Circle(72, 58, 12, null);
        ring.Stroke = Rgb(Brass);
        ring.StrokeThickness = 4;
        s.Circle(72, 58, 9, Rgb(LensWell));
        switch (pose)
        {
            case ChibiPose.Idle:
                s.Circle(72, 58, 3, Accent).Opacity = DimLens;
                break;
            case ChibiPose.Think:
                var focus = s.Begin();
                s.Path("M72 51 a7 7 0 0 1 7 7", null, Accent, 2.5);
                s.Path("M72 65 a7 7 0 0 1 -7 -7", null, Accent, 2.5);
                s.End();
                s.Move(focus, ChibiMove.Spin, origin: new Point(72, 58));
                break;
            default:
                var lens = s.Circle(72, 58, 5.5, Accent);
                if (listen) s.Move(Glow(lens), ChibiMove.Pulse);
                s.Circle(70, 56, 1.8, Rgb(LensGlint));
                break;
        }

        s.Path("M82 50 l10 -6", null, Rgb(Brass), 3);
        s.Path("M38 40 q-10 -10 -4 -18", null, Rgb(Cable), 2);
        var tip = s.Circle(34, 22, 2.5, Accent);
        if (listen) s.Move(tip, ChibiMove.Pulse);

        s.Rect(44, 76, 32, 14, 3, Rgb(Jaw));
        double[] bars = [48, 54, 60, 66];
        for (var i = 0; i < bars.Length; i++)
        {
            if (listen) s.Move(s.Rect(bars[i], 80, 4, 9, 1, Accent), ChibiMove.Eq, i);
            else s.Rect(bars[i], 81, 4, 7, 1, Rgb(Shade));
        }
    }
}
```

- [ ] **Step 3: Netrunner**

Create `windows/src/VoxScribe.App/Views/Chibi/NetrunnerChibi.cs`:

```csharp
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Netrunner: neon hair, a hood, and a visor that turns into an equaliser while you speak.
/// Accent: the visor, its trim and the ear light.
/// </summary>
internal sealed class NetrunnerChibi(Color accent) : ChibiFace(accent)
{
    private const uint Hair = 0xE040FB;
    private const uint Hood = 0x2B2F45;
    private const uint Shoe = 0x1A1C2A;
    private const uint Skin = 0xF2C9A8;
    private const uint Visor = 0x0B1020;
    private const uint Lip = 0x5A2A3A;
    private const double DimVisor = 0.9;
    private const double DimLine = 0.5;
    private const double LoadSize = 10;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var listen = pose == ChibiPose.Listen;

        s.Path("M30 100 q30 -10 60 0 v26 h-60z", Rgb(Hood));
        s.Path("M40 100 l20 14 20 -14", null, Accent, 2);
        s.Rect(38, 122, 16, 10, 4, Rgb(Shoe));
        s.Rect(66, 122, 16, 10, 4, Rgb(Shoe));
        s.Path("M24 66 q0 -40 36 -40 q36 0 36 40 q0 34 -36 34 q-36 0 -36 -34z", Rgb(Hood));
        s.Ellipse(60, 68, 28, 26, Rgb(Skin));
        s.Path("M32 54 l6 -22 8 14 6 -20 8 16 8 -18 6 18 8 -12 4 24 q-28 -10 -54 0z", Rgb(Hair));

        var visor = s.Rect(34, 56, 52, 16, 8, Rgb(Visor));
        switch (pose)
        {
            case ChibiPose.Listen:
                double[] bars = [40, 48, 56, 64, 72];
                for (var i = 0; i < bars.Length; i++)
                {
                    s.Move(s.Rect(bars[i], 58, 5, 12, 1.5, i % 2 == 1 ? Rgb(Hair) : Accent), ChibiMove.Eq, i);
                }

                break;
            case ChibiPose.Idle:
                visor.Opacity = DimVisor;
                s.Rect(40, 63, 40, 2, 1, Accent).Opacity = DimLine;
                break;
            case ChibiPose.Done:
                s.Path("M42 67 q6 -7 12 0 M66 67 q6 -7 12 0", null, Accent, 3);
                break;
            default:
                s.Move(s.Text(pose == ChibiPose.Worried ? "ERR" : "LOAD", 34, 57, 52, LoadSize, Accent), ChibiMove.Pulse);
                break;
        }

        if (listen) s.Move(s.Ellipse(60, 84, 5, 4, Rgb(Lip)), ChibiMove.Talk);
        else if (pose == ChibiPose.Done) s.Path("M53 82 q7 7 14 0", null, Rgb(Lip), 3);
        else s.Rect(55, 83, 10, 2.5, 1, Rgb(Lip));

        var ear = s.Circle(90, 70, 3, Accent);
        if (listen) s.Move(Glow(ear), ChibiMove.Pulse);
    }
}
```

- [ ] **Step 4: Wizard**

Create `windows/src/VoxScribe.App/Views/Chibi/WizardChibi.cs`:

```csharp
using Avalonia;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Wizard: starred pointy hat, beard, a staff whose crystal lights up while you speak, and a
/// ring of runes while thinking. Accent: robe and hat.
/// </summary>
internal sealed class WizardChibi(Color accent) : ChibiFace(accent)
{
    private const uint Skin = 0xF2D2B6;
    private const uint Beard = 0xEDEDED;
    private const uint Ink = 0x3A2A20;
    private const uint Staff = 0x8B5A2B;
    private const uint Crystal = 0x8FD3FF;
    private const uint Star = 0xFFD966;
    private const uint Mouth = 0x5A3A30;
    private const uint Glint = 0xFFFFFF;
    private const uint Brim = 0x000000;
    private const double BrimShade = 0.25;
    private const double HaloOpacity = 0.35;
    private const double RuneOpacity = 0.7;
    private const double RuneWidth = 1.5;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);

        if (pose == ChibiPose.Think)
        {
            var runes = s.Circle(60, 70, 50, null);
            runes.Stroke = Rgb(Crystal);
            runes.StrokeThickness = RuneWidth;
            runes.StrokeDashArray = Sketch.Dashes(4, 7, RuneWidth);
            runes.Opacity = RuneOpacity;
            s.Move(runes, ChibiMove.Spin);
        }

        s.Path("M30 128 q4 -34 30 -34 q26 0 30 34z", Accent);
        s.Rect(102, 74, 4, 58, 2, Rgb(Staff));
        if (pose == ChibiPose.Listen)
        {
            var halo = s.Circle(104, 70, 11, Rgb(Crystal));
            halo.Opacity = HaloOpacity;
            s.Move(halo, ChibiMove.Pulse);
        }

        var crystal = s.Path("M104 62 l7 9 -7 9 -7 -9z", Rgb(Crystal));
        if (pose == ChibiPose.Listen) Glow(crystal, Tokens.Colors.Rgb(Crystal));

        s.Ellipse(60, 70, 26, 24, Rgb(Skin));
        s.Path("M38 78 q22 40 44 0 q-6 6 -22 6 q-16 0 -22 -6z", Rgb(Beard));
        if (pose == ChibiPose.Listen) s.Move(s.Ellipse(60, 86, 5, 4, Rgb(Mouth)), ChibiMove.Talk);

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 50, 70, 70, ink);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 50, 70, 72, ink, 3);
                break;
            case ChibiPose.Listen:
                s.Move(s.Circle(50, 70, 5, ink), ChibiMove.Blink);
                s.Move(s.Circle(70, 70, 5, ink), ChibiMove.Blink);
                s.Circle(51.5, 68.5, 1.8, Rgb(Glint));
                s.Circle(71.5, 68.5, 1.8, Rgb(Glint));
                break;
            default:
                s.Circle(51, 68, 5, ink);
                s.Circle(71, 68, 5, ink);
                s.Circle(52, 66, 1.8, Rgb(Glint));
                s.Circle(72, 66, 1.8, Rgb(Glint));
                break;
        }

        const string BrimPath = "M26 56 q34 -12 68 0 q-4 6 -34 6 q-30 0 -34 -6z";
        s.Path(BrimPath, Accent);
        s.Path(BrimPath, Rgb(Brim)).Opacity = BrimShade;
        s.Path("M34 54 q14 -10 26 -46 q8 18 4 22 q20 6 26 24z", Accent);
        s.Path("M56 14 q10 -6 10 4", null, Accent, 6);
        s.Path("M50 40 l2 4 4 1 -4 2 -2 4 -2 -4 -4 -2 4 -1z M70 30 l1.5 3 3 1 -3 1.5 -1.5 3 -1.5 -3 -3 -1.5 3 -1z", Rgb(Star));
    }
}
```

- [ ] **Step 5: Run the face tests, expect PASS**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~ChibiTests|FullyQualifiedName~AppearanceSectionTests"`
Expected: all pass for the four mascots.

- [ ] **Step 6: Full suite, warnings gate, commit, push**

```bash
cd windows && dotnet test VoxScribe.CrossPlatform.slnf && dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror
cd .. && git add windows && git commit -m "feat: servo-skull, netrunner and wizard mascots" && git push
```

---

### Task 6: Characters — Clawd, Owl, Vintage mic

**Files:**
- Create: `windows/src/VoxScribe.App/Views/Chibi/ClawdChibi.cs`, `OwlChibi.cs`, `MicChibi.cs`
- Modify: `windows/src/VoxScribe.App/Views/Chibi/Mascots.cs` (`All`)
- Test: existing `ChibiTests`; add one test to `ChibiTests`.

**Interfaces:**
- Consumes: as Task 5.
- Produces: `ClawdChibi(Color)`, `OwlChibi(Color)`, `MicChibi(Color)`.

- [ ] **Step 1: Register them and add the Clawd colour test (failing)**

In `Mascots.All`, after the wizard and before the slime:

```csharp
        new("clawd", "Clawd", a => new ClawdChibi(a)),
        new("owl", "Owl scribe", a => new OwlChibi(a)),
        new("mic", "Vintage mic", a => new MicChibi(a)),
```

Append to `ChibiTests`:

```csharp
    [Fact]
    public void Clawd_stays_terracotta_but_off_the_recording_red()
    {
        IsRed(Tokens.Colors.Rgb(0xD97757)).ShouldBeTrue("Anthropic's own terracotta sits inside the red window");
        IsRed(Tokens.Colors.Rgb(ClawdChibi.Body)).ShouldBeFalse();
        Tokens.Colors.Rgb(ClawdChibi.Body).ToHsv().H.ShouldBeInRange(16, 25, "still terracotta");
    }
```

Run: `cd windows && dotnet build VoxScribe.CrossPlatform.slnf`
Expected: build errors, the classes do not exist.

- [ ] **Step 2: Clawd**

Create `windows/src/VoxScribe.App/Views/Chibi/ClawdChibi.cs`:

```csharp
using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Clawd, Claude Code's pixel crab: a terracotta block, two eyes, stubby arms, four legs, drawn
/// aliased on a 6-unit grid. Waves its arms while you speak. The body never takes the accent;
/// the listening waves and the sparkles do.
/// </summary>
/// <remarks>
/// <see cref="Body"/> is <c>#D9825A</c>, not Anthropic's <c>#D97757</c>: that one sits at hue
/// 14.8°, inside the red detector's window, and red means recording.
/// </remarks>
internal sealed class ClawdChibi(Color accent) : ChibiFace(accent)
{
    /// <summary>The terracotta, nudged 4° off the recording red.</summary>
    internal const uint Body = 0xD9825A;

    private const uint Eye = 0x1A1A1A;
    private const double Unit = 6;
    private const double OriginX = 12;
    private const double OriginY = 50;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        RenderOptions.SetEdgeMode(s.Root, EdgeMode.Aliased);
        var body = Rgb(Body);
        var eye = Rgb(Eye);

        Pixel(s, 2, 0, 12, 6, body);
        var left = Pixel(s, 0, 3, 2, 2, body);
        var right = Pixel(s, 14, 3, 2, 2, body);
        if (pose == ChibiPose.Listen)
        {
            s.Move(left, ChibiMove.Wave, 0);
            s.Move(right, ChibiMove.Wave, 1);
        }

        foreach (var x in new[] { 3, 5, 10, 12 }) Pixel(s, x, 6, 1, 2, body);

        switch (pose)
        {
            case ChibiPose.Idle:
                Pixel(s, 4, 2, 2, 1, eye);
                Pixel(s, 10, 2, 2, 1, eye);
                break;
            case ChibiPose.Think or ChibiPose.Worried:
                Pixel(s, 5, 1, 1, 2, eye);
                Pixel(s, 11, 1, 1, 2, eye);
                break;
            case ChibiPose.Done:
                foreach (var (x, y) in new[] { (4, 2), (5, 1), (6, 2), (10, 2), (11, 1), (12, 2) }) Pixel(s, x, y, 1, 1, eye);
                break;
            default:
                var eyes = s.Begin();
                Pixel(s, 4, 1, 1, 3, eye);
                Pixel(s, 11, 1, 1, 3, eye);
                s.End();
                s.Move(eyes, ChibiMove.Blink, origin: new Point(OriginX + (8 * Unit), OriginY + (2.5 * Unit)));
                foreach (var (x, y) in new[] { (17, 1), (18, 0), (17, 5), (18, 6) }) s.Move(Pixel(s, x, y, 1, 1, Accent), ChibiMove.Pulse);
                break;
        }
    }

    private static Avalonia.Controls.Shapes.Rectangle Pixel(Sketch s, double x, double y, double w, double h, IBrush fill) =>
        s.Rect(OriginX + (x * Unit), OriginY + (y * Unit), w * Unit, h * Unit, 0, fill);
}
```

- [ ] **Step 3: Owl**

Create `windows/src/VoxScribe.App/Views/Chibi/OwlChibi.cs`:

```csharp
using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Owl scribe: round glasses, ear tufts, a quill under the wing. Eyes widen and the beak talks
/// while you speak; the head tilts while thinking. Accent: the glasses rims.
/// </summary>
internal sealed class OwlChibi(Color accent) : ChibiFace(accent)
{
    private const uint Feather = 0x8A6A4A;
    private const uint Belly = 0xD9C3A0;
    private const uint BellyLine = 0xB89B73;
    private const uint Tuft = 0x6E5236;
    private const uint EyeDisc = 0xF3E6CF;
    private const uint Ink = 0x2B1E14;
    private const uint Beak = 0xE8A33D;
    private const uint Quill = 0xF4F1EA;
    private const uint Glint = 0xFFFFFF;
    private const double RimWidth = 2.5;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);
        var listen = pose == ChibiPose.Listen;

        var head = s.Begin();
        if (pose == ChibiPose.Think) s.Move(head, ChibiMove.Tilt, origin: new Point(60, 100));

        s.Ellipse(60, 88, 34, 40, Rgb(Feather));
        s.Ellipse(60, 100, 22, 24, Rgb(Belly));
        s.Path("M50 96 q4 4 8 0 M62 96 q4 4 8 0 M54 106 q4 4 8 0 M60 114 q4 4 8 0", null, Rgb(BellyLine), 2);
        var tufts = s.Path("M30 52 l-4 -20 16 12z M90 52 l4 -20 -16 12z", Rgb(Tuft));
        if (listen) s.Move(tufts, ChibiMove.Pulse);

        s.Circle(48, 66, 13, Rgb(EyeDisc));
        s.Circle(72, 66, 13, Rgb(EyeDisc));
        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 48, 72, 66, ink);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 48, 72, 68, ink);
                break;
            default:
                var r = listen ? 7 : 5;
                var up = pose is ChibiPose.Think or ChibiPose.Worried;
                s.Circle(48, 66, r, ink);
                s.Circle(72, 66, r, ink);
                s.Circle(up ? 50 : 46, up ? 62 : 64, 2, Rgb(Glint));
                s.Circle(up ? 74 : 70, up ? 62 : 64, 2, Rgb(Glint));
                break;
        }

        foreach (var cx in new[] { 48.0, 72.0 })
        {
            var rim = s.Circle(cx, 66, 13, null);
            rim.Stroke = Accent;
            rim.StrokeThickness = RimWidth;
        }

        s.Path("M61 66 h-2", null, Accent, RimWidth);

        var beak = s.Path(listen ? "M56 78 l4 10 4 -10z" : "M56 78 l4 7 4 -7z", Rgb(Beak));
        if (listen) s.Move(beak, ChibiMove.Talk, origin: new Point(60, 78));

        s.Path("M90 92 q14 -2 18 -22 q-12 6 -16 16z", Rgb(Quill));
        s.Path("M90 94 l16 -22", null, Rgb(Tuft), 1.5);
        s.Path("M48 126 l-3 6 M52 126 v6 M68 126 v6 M72 126 l3 6", null, Rgb(Beak), 3);
        s.End();
    }
}
```

- [ ] **Step 4: Vintage mic**

Create `windows/src/VoxScribe.App/Views/Chibi/MicChibi.cs`:

```csharp
using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Vintage mic: a 1950s chrome broadcast mic with a face. The grille lights up in waves while
/// you speak and notes drift up when the text lands. Accent: grille glow and notes.
/// </summary>
internal sealed class MicChibi(Color accent) : ChibiFace(accent)
{
    private const uint Chrome = 0xC7CCD6;
    private const uint Dark = 0x3A3F4B;
    private const uint Grille = 0x9AA0AD;
    private const uint Blush = 0xF0B8A0;
    private const uint Shine = 0xFFFFFF;
    private const double ShineOpacity = 0.8;
    private const double BlushOpacity = 0.6;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var dark = Rgb(Dark);

        s.Rect(40, 128, 40, 5, 2.5, dark);
        s.Rect(57, 106, 6, 22, 0, dark);
        s.Path("M38 96 q22 16 44 0", null, dark, 4);
        s.Path("M36 50 q0 -30 24 -30 q24 0 24 30 v34 q0 20 -24 20 q-24 0 -24 -20z", Rgb(Chrome));
        s.Path("M42 26 q8 -4 12 -2", null, Rgb(Shine), 3).Opacity = ShineOpacity;
        s.Rect(40, 30, 40, 34, 6, Rgb(Grille));

        double[] slots = [34, 40, 46, 52, 58];
        for (var i = 0; i < slots.Length; i++)
        {
            if (pose == ChibiPose.Listen)
            {
                s.Move(Glow(s.Rect(44 + (i % 2 * 2), slots[i], 32 - (i % 2 * 4), 3, 1.5, Accent)), ChibiMove.Pulse, i);
            }
            else
            {
                s.Rect(44, slots[i], 32, 3, 1.5, dark);
            }
        }

        s.Rect(36, 66, 48, 4, 0, Rgb(Grille));

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 52, 68, 78, dark, 2.5);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 52, 68, 80, dark, 2.5);
                s.Path("M55 88 q5 5 10 0", null, dark, 2.5);
                var note = s.Begin();
                s.Path("M98 40 v-14 l8 -3 v14", null, Accent, 2);
                s.Circle(96, 40, 3, Accent);
                s.Circle(104, 37, 3, Accent);
                s.End();
                s.Move(note, ChibiMove.Rise, 0);
                var quaver = s.Begin();
                s.Path("M20 44 v-12", null, Accent, 2);
                s.Circle(18, 44, 3, Accent);
                s.End();
                s.Move(quaver, ChibiMove.Rise, 1);
                break;
            default:
                var ey = pose is ChibiPose.Think or ChibiPose.Worried ? 76 : 78;
                s.Circle(52, ey, 3.5, dark);
                s.Circle(68, ey, 3.5, dark);
                if (pose == ChibiPose.Listen) s.Move(s.Ellipse(60, 90, 4, 3.5, dark), ChibiMove.Talk);
                break;
        }

        s.Ellipse(45, 86, 4, 2.5, Rgb(Blush)).Opacity = BlushOpacity;
        s.Ellipse(75, 86, 4, 2.5, Rgb(Blush)).Opacity = BlushOpacity;
    }
}
```

The two `Begin()` groups in Done are full-canvas `Canvas`es: `ChibiMove.Rise` only translates them, so no origin is needed.

- [ ] **Step 5: Run, expect PASS**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~ChibiTests|FullyQualifiedName~AppearanceSectionTests"`
Expected: all pass, including `Clawd_stays_terracotta_but_off_the_recording_red`.

- [ ] **Step 6: Full suite, warnings gate, commit, push**

```bash
cd windows && dotnet test VoxScribe.CrossPlatform.slnf && dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror
cd .. && git add windows && git commit -m "feat: clawd, owl and vintage mic mascots" && git push
```

---

### Task 7: Characters — Cat, Dragon

**Files:**
- Create: `windows/src/VoxScribe.App/Views/Chibi/CatChibi.cs`, `DragonChibi.cs`
- Modify: `windows/src/VoxScribe.App/Views/Chibi/Mascots.cs` (`All`)
- Test: existing `ChibiTests`; add one test to `MascotTests`.

**Interfaces:**
- Consumes: as Task 5.
- Produces: `CatChibi(Color)`, `DragonChibi(Color)`; final `Mascots.All` order: servo-skull, netrunner, wizard, clawd, owl, mic, cat, slime, dragon.

- [ ] **Step 1: Register them and pin the final catalogue (failing)**

Replace `Mascots.All` with the final list:

```csharp
    [
        new("servo-skull", "Servo-skull", a => new ServoSkullChibi(a)),
        new("netrunner", "Netrunner", a => new NetrunnerChibi(a)),
        new("wizard", "Wizard", a => new WizardChibi(a)),
        new("clawd", "Clawd", a => new ClawdChibi(a)),
        new("owl", "Owl scribe", a => new OwlChibi(a)),
        new("mic", "Vintage mic", a => new MicChibi(a)),
        new("cat", "Cat", a => new CatChibi(a)),
        new("slime", "Slime", a => new SlimeChibi(a)),
        new("dragon", "Little dragon", a => new DragonChibi(a)),
    ];
```

Append to `MascotTests`:

```csharp
    [Fact]
    public void Nine_mascots_with_the_ids_settings_document() =>
        Mascots.All.Select(m => m.Id).ShouldBe(
            ["servo-skull", "netrunner", "wizard", "clawd", "owl", "mic", "cat", "slime", "dragon"]);
```

Run: `cd windows && dotnet build VoxScribe.CrossPlatform.slnf`
Expected: build errors, `CatChibi`/`DragonChibi` do not exist.

- [ ] **Step 2: Cat**

Create `windows/src/VoxScribe.App/Views/Chibi/CatChibi.cs`:

```csharp
using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Cat: a big sitting cat. Ears twitch while you speak, the tail beats while thinking and when
/// happy. Accent: its collar (the bell stays gold).
/// </summary>
internal sealed class CatChibi(Color accent) : ChibiFace(accent)
{
    private const uint Fur = 0xE8A25C;
    private const uint Cream = 0xF7E3C8;
    private const uint EarPink = 0xF4B7A0;
    private const uint Stripe = 0xC8803D;
    private const uint Ink = 0x3A2618;
    private const uint Nose = 0xE8909C;
    private const uint Bell = 0xFFD27A;
    private const uint Glint = 0xFFFFFF;
    private const double WhiskerOpacity = 0.6;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);
        var fur = Rgb(Fur);

        var tail = s.Path("M84 124 q22 -2 20 -26", null, fur, 9);
        if (pose is ChibiPose.Think or ChibiPose.Done) s.Move(tail, ChibiMove.Wag, origin: new Point(84, 124));

        s.Ellipse(60, 112, 26, 20, fur);
        s.Ellipse(60, 116, 14, 13, Rgb(Cream));
        s.Ellipse(48, 130, 8, 4, Rgb(Cream));
        s.Ellipse(72, 130, 8, 4, Rgb(Cream));

        var ears = s.Begin();
        s.Path("M30 60 l2 -30 22 16z M90 60 l-2 -30 -22 16z", fur);
        s.Path("M34 52 l2 -16 10 9z M86 52 l-2 -16 -10 9z", Rgb(EarPink));
        s.End();
        if (pose == ChibiPose.Listen) s.Move(ears, ChibiMove.Pulse);

        s.Ellipse(60, 72, 32, 27, fur);
        s.Path("M40 97 q20 8 40 0", null, Accent, 4);
        s.Circle(60, 103, 3, Rgb(Bell));
        s.Path("M52 48 l2 8 M60 46 v9 M68 48 l-2 8", null, Rgb(Stripe), 3);

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 48, 72, 70, ink);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 48, 72, 72, ink);
                break;
            default:
                var ey = pose is ChibiPose.Think or ChibiPose.Worried ? 66 : 70;
                var leftEye = s.Ellipse(48, ey, 5, 6.5, ink);
                var rightEye = s.Ellipse(72, ey, 5, 6.5, ink);
                if (pose == ChibiPose.Listen)
                {
                    s.Move(leftEye, ChibiMove.Blink);
                    s.Move(rightEye, ChibiMove.Blink);
                }

                s.Circle(49.5, ey - 3, 2, Rgb(Glint));
                s.Circle(73.5, ey - 3, 2, Rgb(Glint));
                break;
        }

        s.Path("M57 77 h6 l-3 3z", Rgb(Nose));
        if (pose == ChibiPose.Listen) s.Move(s.Ellipse(60, 84, 4, 3.5, ink), ChibiMove.Talk);
        else s.Path("M54 80 q3 4 6 0 q3 4 6 0", null, ink, 2.2);

        s.Path("M30 76 l12 2 M30 82 l12 0 M90 76 l-12 2 M90 82 l-12 0", null, ink, 1.2).Opacity = WhiskerOpacity;
    }
}
```

- [ ] **Step 3: Dragon**

Create `windows/src/VoxScribe.App/Views/Chibi/DragonChibi.cs`:

```csharp
using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Little dragon: wings beat while you speak, nostrils smoke while thinking, a small golden
/// flame when the text lands (gold, never red). Accent: the wing membranes.
/// </summary>
internal sealed class DragonChibi(Color accent) : ChibiFace(accent)
{
    private const uint Scale = 0x5DBB63;
    private const uint DarkScale = 0x3E8E46;
    private const uint Belly = 0xF2E3A4;
    private const uint Ink = 0x1E3320;
    private const uint Snout = 0x7FCC84;
    private const uint Smoke = 0xB8BEC8;
    private const uint Flame = 0xFFC857;
    private const uint FlameCore = 0xFFF0B3;
    private const uint Glint = 0xFFFFFF;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);
        var scale = Rgb(Scale);
        var dark = Rgb(DarkScale);

        s.Path("M78 120 q26 4 30 -12 l4 -6 -10 2", null, scale, 8);
        var leftWing = s.Path("M38 96 q-24 -10 -26 -32 q12 6 18 4 q-2 10 14 18z", Accent);
        var rightWing = s.Path("M82 96 q24 -10 26 -32 q-12 6 -18 4 q2 10 -14 18z", Accent);
        if (pose == ChibiPose.Listen)
        {
            s.Move(leftWing, ChibiMove.Flap, origin: new Point(38, 96));
            s.Move(rightWing, ChibiMove.Flap, origin: new Point(82, 96));
        }

        s.Ellipse(60, 108, 24, 22, scale);
        s.Ellipse(60, 112, 14, 16, Rgb(Belly));
        s.Ellipse(48, 130, 8, 4, dark);
        s.Ellipse(72, 130, 8, 4, dark);
        s.Path("M42 46 l-6 -16 14 10z M78 46 l6 -16 -14 10z", Rgb(Belly));
        s.Ellipse(60, 68, 30, 25, scale);
        s.Path("M56 42 l4 -8 4 8z", dark);

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 50, 72, 68, ink);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 50, 72, 70, ink);
                break;
            default:
                var ey = pose is ChibiPose.Think or ChibiPose.Worried ? 64 : 68;
                s.Ellipse(50, ey, 5, 6.5, ink);
                s.Ellipse(72, ey, 5, 6.5, ink);
                s.Circle(51.5, ey - 3, 2, Rgb(Glint));
                s.Circle(73.5, ey - 3, 2, Rgb(Glint));
                break;
        }

        s.Ellipse(62, 82, 14, 8, Rgb(Snout));
        s.Circle(58, 80, 1.5, ink);
        s.Circle(66, 80, 1.5, ink);
        if (pose == ChibiPose.Listen) s.Move(s.Ellipse(62, 87, 4, 3, ink), ChibiMove.Talk);

        if (pose == ChibiPose.Think)
        {
            s.Move(s.Circle(86, 74, 4, Rgb(Smoke)), ChibiMove.Rise, 0);
            s.Move(s.Circle(90, 70, 3, Rgb(Smoke)), ChibiMove.Rise, 1);
        }

        if (pose == ChibiPose.Done)
        {
            s.Path("M80 82 q16 -6 24 2 q-8 2 -10 8 q-4 -6 -14 -4z", Rgb(Flame));
            s.Path("M82 83 q8 -2 14 2 q-6 1 -7 4 q-3 -4 -7 -3z", Rgb(FlameCore));
        }
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Run: `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~ChibiTests|FullyQualifiedName~MascotTests|FullyQualifiedName~AppearanceSectionTests"`
Expected: all pass for all nine.

- [ ] **Step 5: Full suite, warnings gate, commit, push**

```bash
cd windows && dotnet test VoxScribe.CrossPlatform.slnf && dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror
cd .. && git add windows && git commit -m "feat: cat and dragon mascots" && git push
```

---

### Task 8: Docs and the final hand check

**Files:**
- Modify: `AGENTS.md` (sections "What this is", "Things that look like bugs and are not", "Design system")
- Modify: `windows/README.md` (the feature list / usage section that mentions the pill — find it with `grep -n -i "pill" windows/README.md`)

- [ ] **Step 1: AGENTS.md**

In "What this is", after the sentence listing the five themes, add: `An optional desktop mascot (nine characters, Settings → Appearance) can replace the pill.`

In "Things that look like bugs and are not", add a paragraph:

```markdown
**The mascot window is not click-through, the pill window is.** Same `HudWindow`, two modes.
The mascot must be grabbable, so its window drops `WS_EX_TRANSPARENT` (it keeps
`WS_EX_NOACTIVATE`: dragging or right-clicking it must never move the caret). It sizes to its
content and is anchored at its bottom-left corner, so only the character blocks clicks while
idle. Its motion is quantised and driven by the 33 ms tick on purpose — see `ChibiMotion`.
```

In "Design system", after the "Red means recording" bullet, add:

```markdown
  Mascot colours are `private const` in each character, so `ChibiTests` walks every mascot's
  rendered tree in every phase instead. Clawd is `#D9825A`, not Anthropic's `#D97757`, which
  sits inside the red window.
```

- [ ] **Step 2: README**

Where `windows/README.md` describes the pill, add one sentence: `Prefer a companion? Settings → Appearance → Dictation indicator swaps the pill for a desktop mascot you can drag anywhere (right-click for its menu, double-click to open the app).`

- [ ] **Step 3: Commit and push**

```bash
git add AGENTS.md windows/README.md && git commit -m "docs: the desktop mascot" && git push
```

- [ ] **Step 4: Final hand check with the owner**

Build and install as in Task 3. In Settings → Appearance, pick each of the nine characters in turn and dictate once with each; then pin a mascot accent, switch theme and light/dark mode. Report: each character's four states look right; the accent part follows the theme, and follows the pin when one is set; nothing but the recording dot is red; repeat Task 3 checks 2 and 3 once (focus) on the final build.
