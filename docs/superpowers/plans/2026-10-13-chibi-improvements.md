# Chibi Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Soft 3D relief and ground shadows for the nine desktop mascots, plus one signature animation each.

**Architecture:** Shading lives in one place: `ChibiFace.Rgb(uint)` returns a lit radial gradient unless a character opts out (`Shaded => false`, Clawd). The ground shadow is a fixed layer under the bobbing figure. Three new pure moves (`Swivel`, `Ring`, `Fall`) join `ChibiMotion`; each character gains a few shapes in its `Draw`.

**Tech Stack:** C# / .NET 10, Avalonia 11.3, xUnit + Avalonia.Headless.XUnit, Shouldly.

**Spec:** `docs/superpowers/specs/2026-10-13-chibi-improvements-design.md`

## Global Constraints

- Build/test from `windows/`: `dotnet test VoxScribe.CrossPlatform.slnf`; warnings gate `dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror` (0 warnings).
- **Red means recording.** No character colour, solid or any gradient stop, may satisfy `DoctrineTests.IsRed`, except the owner-approved exemption: Clawd's body `#D97757`, on Clawd only (`ChibiTests.IsClawdTerracotta`, unchanged). Amber is `#FFC857`, never red.
- **No literal colours in views** except a character's own drawing colours as named `private const uint` in that character. Coordinates in path data and shape calls are drawing data and stay inline, as in the existing characters.
- Numbers formatted into path strings go through `FormattableString.Invariant`.
- ImplicitUsings is on: an explicit `Path`/`Rectangle` type name must be qualified (`Avalonia.Controls.Shapes.…`). Test files need `using Xunit;`.
- Keep each file's existing line endings (LF). No attribution lines in commits. Commit after each task; the controller pushes.

---

### Task 1: Shading, ground shadow, new moves, red scan over gradients

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/Chibi/ChibiMotion.cs`, `Sketch.cs`, `ChibiFace.cs`, `SlimeChibi.cs`, `ClawdChibi.cs`
- Test: `windows/tests/VoxScribe.App.Tests/ChibiMotionTests.cs`, `windows/tests/VoxScribe.App.Tests/ChibiTests.cs`

**Interfaces — Produces:**
- `ChibiMotion.Swivel(double t) -> double` (±40°, 2.4 s, whole degrees); `Ring(double t, int index) -> (double Scale, double Opacity)`; `Fall(double t, int index) -> (double Y, double Opacity)`
- `ChibiMove.Swivel`, `ChibiMove.Ring`, `ChibiMove.Fall`
- `ChibiFace`: `protected IBrush Rgb(uint rgb)` (now an instance method returning a lit `RadialGradientBrush`, or a solid brush when `Shaded` is false); `protected virtual bool Shaded => true`
- `ClawdChibi`: `protected override bool Shaded => false`

- [ ] **Step 1: Failing motion tests** — append to `ChibiMotionTests`:

```csharp
    [Fact]
    public void Swivel_swings_forty_degrees_in_whole_degrees()
    {
        foreach (var t in Seconds(5))
        {
            var a = ChibiMotion.Swivel(t);
            a.ShouldBeInRange(-40, 40);
            a.ShouldBe(Math.Round(a));
        }

        Seconds(2.4).Max(ChibiMotion.Swivel).ShouldBeGreaterThan(35);
    }

    [Fact]
    public void Rings_grow_and_fade()
    {
        foreach (var t in Seconds(3))
            for (var i = 0; i < 3; i++)
            {
                var (scale, opacity) = ChibiMotion.Ring(t, i);
                scale.ShouldBeInRange(0.9 - 1e-9, 1.5 + 1e-9);
                opacity.ShouldBeInRange(-1e-9, 0.8 + 1e-9);
            }

        ChibiMotion.Ring(0, 0).Scale.ShouldBeLessThan(ChibiMotion.Ring(0.9, 0).Scale);
        ChibiMotion.Ring(0, 0).Opacity.ShouldBeGreaterThan(ChibiMotion.Ring(0.9, 0).Opacity);
    }

    [Fact]
    public void Letters_fall_and_fade()
    {
        foreach (var t in Seconds(3))
            for (var i = 0; i < 4; i++)
            {
                var (y, opacity) = ChibiMotion.Fall(t, i);
                y.ShouldBeInRange(-1e-9, 52 + 1e-9);
                opacity.ShouldBeInRange(-1e-9, 1 + 1e-9);
            }

        ChibiMotion.Fall(1.0, 0).Y.ShouldBeGreaterThan(ChibiMotion.Fall(0.2, 0).Y);
    }
```

Run `cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter "FullyQualifiedName~ChibiMotionTests"` → build error (members missing).

- [ ] **Step 2: Implement the moves** — in `ChibiMotion.cs` add constants next to the others:

```csharp
    private const double SwivelPeriod = 2.4;
    private const double SwivelDegrees = 40;
    private const double RingPeriod = 1.2;
    private const double RingFrom = 0.9;
    private const double RingTo = 1.5;
    private const double RingStart = 0.8;
    private const double RingStagger = 0.4;
    private const double FallPeriod = 1.4;
    private const double FallDepth = 52;
    private const double FallStagger = 0.35;
```

and methods at the end of the class:

```csharp
    /// <summary>A head swivel of ±40° over 2.4 s, whole degrees.</summary>
    public static double Swivel(double t) => Swing(t, SwivelPeriod, SwivelDegrees);

    /// <summary>A ring spreading from 0.9× to 1.5× and fading, staggered by <paramref name="index"/>.</summary>
    public static (double Scale, double Opacity) Ring(double t, int index)
    {
        var p = (t + (index * RingStagger)) % RingPeriod / RingPeriod;
        return (Quantize(RingFrom + ((RingTo - RingFrom) * p), ScaleStep), Quantize(RingStart * (1 - p), ScaleStep));
    }

    /// <summary>A letter falling 52 px and fading, staggered by <paramref name="index"/>.</summary>
    public static (double Y, double Opacity) Fall(double t, int index)
    {
        var p = (t + (index * FallStagger)) % FallPeriod / FallPeriod;
        return (Quantize(FallDepth * p, HalfPixel), Quantize(1 - p, ScaleStep));
    }
```

In `Sketch.cs`, add to `ChibiMove` (after `Wave`, with doc summaries in the existing style): `Swivel` ("Swings ±40° (a swivelling head)."), `Ring` ("Spreads and fades (sound rings)."), `Fall` ("Drops and fades (letters falling in)."). In `Sketch.Move`'s transform switch, add `ChibiMove.Swivel` to the `RotateTransform` arm and `ChibiMove.Fall` to the `TranslateTransform` arm (`Ring` takes the default `ScaleTransform`).

In `ChibiFace.Animate`, add cases:

```csharp
                case ChibiMove.Swivel:
                    ((RotateTransform)target.RenderTransform!).Angle = ChibiMotion.Swivel(t);
                    break;
                case ChibiMove.Ring:
                    var (spread, fade) = ChibiMotion.Ring(t, index);
                    var ring = (ScaleTransform)target.RenderTransform!;
                    ring.ScaleX = spread;
                    ring.ScaleY = spread;
                    target.Opacity = fade;
                    break;
                case ChibiMove.Fall:
                    var (drop, gone) = ChibiMotion.Fall(t, index);
                    ((TranslateTransform)target.RenderTransform!).Y = drop;
                    target.Opacity = gone;
                    break;
```

Run the motion tests → PASS.

- [ ] **Step 3: Failing face tests** — in `ChibiTests.cs`, replace `Colours` with a version that reads gradient stops:

```csharp
    private static IEnumerable<Color> Colours(Control c) => (c switch
    {
        Shape s => new[] { s.Fill, s.Stroke },
        TextBlock t => new[] { t.Foreground },
        Border b => new[] { b.Background, b.BorderBrush },
        _ => [],
    }).SelectMany(BrushColours);

    // Every colour a brush can paint: a solid colour, or each stop of a gradient.
    private static IEnumerable<Color> BrushColours(IBrush? brush) => brush switch
    {
        ISolidColorBrush s => [s.Color],
        IGradientBrush g => g.GradientStops.Select(stop => stop.Color),
        _ => [],
    };
```

and add:

```csharp
    [AvaloniaFact]
    public void Characters_are_lit_in_relief_except_pixel_art_Clawd() => EachMascot((mascot, face) =>
    {
        face.Update(new PillState(PillPhase.Idle, 0, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
        var fills = face.GetVisualDescendants().OfType<Shape>().Select(s => s.Fill).ToList();

        if (mascot.Id == "clawd") fills.OfType<IGradientBrush>().ShouldBeEmpty("pixel art stays flat");
        else fills.OfType<IRadialGradientBrush>().ShouldNotBeEmpty(mascot.Id);
    });
```

Run `--filter "FullyQualifiedName~ChibiTests"` → the new test FAILS (no gradients yet).

- [ ] **Step 4: Implement shading and the ground shadow** — in `ChibiFace.cs`:

Add constants:

```csharp
    private const double LightX = 0.35;
    private const double LightY = 0.28;
    private const double ShadeRadius = 0.8;
    private const double ShadeMid = 0.55;
    private const double Highlight = 0.28;
    private const double Shadow = 0.22;
    private const double GroundX = 60;
    private const double GroundY = 134;
    private const double GroundRx = 30;
    private const double GroundRy = 4.5;
    private const double GroundOpacity = 0.28;
```

Replace `Rgb`:

```csharp
    /// <summary>Whether this character's colours are lit in relief. Pixel art opts out.</summary>
    protected virtual bool Shaded => true;

    /// <summary>
    /// A brush for a character colour <c>0xRRGGBB</c>: lit from the top-left as a radial gradient
    /// (lighter, the colour, darker), or solid when <see cref="Shaded"/> is false.
    /// </summary>
    protected IBrush Rgb(uint rgb)
    {
        var colour = Tokens.Colors.Rgb(rgb);
        if (!Shaded) return new SolidColorBrush(colour);

        var light = new RelativePoint(LightX, LightY, RelativeUnit.Relative);
        return new RadialGradientBrush
        {
            Center = light,
            GradientOrigin = light,
            RadiusX = new RelativeScalar(ShadeRadius, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(ShadeRadius, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Mix(colour, Colors.White, Highlight), 0),
                new GradientStop(colour, ShadeMid),
                new GradientStop(Mix(colour, Colors.Black, Shadow), 1),
            },
        };
    }

    private static Color Mix(Color from, Color to, double k) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * k)),
        (byte)Math.Round(from.G + ((to.G - from.G) * k)),
        (byte)Math.Round(from.B + ((to.B - from.B) * k)));
```

If `RadiusX`/`RadiusY`/`RelativeScalar` do not exist in this Avalonia version, use the `Radius = ShadeRadius` property instead and record the deviation.

Ground shadow: in the constructor, replace `Children = { new Viewbox { Child = _figure }, _dot }` with:

```csharp
            Children = { new Viewbox { Child = new Panel { Children = { Ground(), _figure } } }, _dot },
```

and add:

```csharp
    // A fixed shadow under the figure: it stays on the ground while the figure bobs and hops.
    private static Canvas Ground()
    {
        var shadow = new Ellipse
        {
            Width = 2 * GroundRx,
            Height = 2 * GroundRy,
            Fill = Brushes.Black,
            Opacity = GroundOpacity,
        };
        Canvas.SetLeft(shadow, GroundX - GroundRx);
        Canvas.SetTop(shadow, GroundY - GroundRy);
        return new Canvas { Width = Sketch.Width, Height = Sketch.Height, Children = { shadow } };
    }
```

`Rgb` is now an instance method; every caller already sits in an instance `Draw` or `Redraw`. `Brushes.Black` is solid black, which the red detector ignores.

In `SlimeChibi.cs`, delete the `Shadow`/`ShadowOpacity` constants and the `s.Ellipse(60, 132, 38, 4, Rgb(Shadow))…` line (the base draws the shadow now).

In `ClawdChibi.cs`, add:

```csharp
    /// <inheritdoc />
    protected override bool Shaded => false;
```

- [ ] **Step 5: Run and prove the red gate reads gradients** — `dotnet test VoxScribe.CrossPlatform.slnf` → all PASS. Then temporarily set `SlimeChibi.Gel` to `0xE5484D`, run `--filter "FullyQualifiedName~Nothing_on_a_mascot_is_red"` → FAIL (the gradient's stops are red; the old solid-only reader would have missed it). Revert, re-run → PASS. Record both in the report.

- [ ] **Step 6: Warnings gate, commit**

```bash
cd windows && dotnet build VoxScribe.CrossPlatform.slnf --no-incremental -warnaserror
cd .. && git add windows docs && git commit -m "feat: mascots in soft relief, ground shadow, swivel/ring/fall moves"
```

---

### Task 2: Servo-skull quill, Netrunner holo screen, Wizard grimoire

**Files:** Modify `windows/src/VoxScribe.App/Views/Chibi/ServoSkullChibi.cs`, `NetrunnerChibi.cs`, `WizardChibi.cs`; test: `ChibiTests.cs`.

**Interfaces — Consumes:** Task 1's `Rgb` (instance), `ChibiMove.Wag/Pulse/Flap`, `Sketch.Begin/End/Move/Path/Rect`.

- [ ] **Step 1: Failing test** — append to `ChibiTests`:

```csharp
    private static int ShapesIn(ChibiFace face, PillPhase phase)
    {
        face.Update(new PillState(phase, 0.5, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
        return face.GetVisualDescendants().OfType<Shape>().Count();
    }

    [AvaloniaTheory]
    [InlineData("servo-skull", PillPhase.Recording, 44)]
    [InlineData("netrunner", PillPhase.Working, 22)]
    [InlineData("wizard", PillPhase.Recording, 30)]
    public void Each_signature_draws_in_its_pose(string id, PillPhase phase, int atLeast)
    {
        var face = Mascots.Find(id)!.Create(Tokens.Colors.Accent);
        var window = new Window { Content = face, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            window.Show();
            ShapesIn(face, phase).ShouldBeGreaterThanOrEqualTo(atLeast, id);
        }
        finally
        {
            window.Close();
        }
    }
```

Before implementing, run it and note each mascot's current shape count in that phase; then set each `atLeast` to **current count + the number of shapes this task adds** (servo +3, netrunner +6, wizard +4) so the test fails now and passes after. Record the counts in the report.

- [ ] **Step 2: Servo-skull** — add `private const uint Quill = 0xF4F1EA;` and, at the end of `Draw`:

```csharp
        // The servo-scribe writes: a mechadendrite scribbles on the parchment while you speak.
        if (listen)
        {
            var arm = s.Begin();
            s.Path("M80 92 q14 4 10 22", null, Rgb(Cable), 2.5);
            s.Path("M88 112 l-14 10 2 2 14 -10z", Rgb(Quill));
            s.Path("M74 122 l-2 3", null, Rgb(Socket), 1.5);
            s.End();
            s.Move(arm, ChibiMove.Wag, origin: new Point(82, 92));
        }
```

- [ ] **Step 3: Netrunner** — add `private const double HoloFill = 0.18;` and `private const double HoloEdge = 0.85;`; at the end of `Draw`:

```csharp
        // A holo screen floats in front while the text is transcribed; lines type in, staggered.
        if (pose == ChibiPose.Think)
        {
            const string Panel = "M70 96 l40 -8 v26 l-40 8z";
            s.Path(Panel, Accent).Opacity = HoloFill;
            s.Path(Panel, null, Accent, 1).Opacity = HoloEdge;
            for (var i = 0; i < 4; i++)
            {
                double y = 100 + (i * 5);
                double length = 26 - (i * 4);
                var line = s.Path(FormattableString.Invariant($"M74 {y} l{length} {-length / 5}"), null, Accent, 1.8);
                s.Move(line, ChibiMove.Pulse, i);
            }
        }
```

- [ ] **Step 4: Wizard** — add constants `Cover = 0x7A4E2D`, `Paper = 0xF4ECD8`, `Page = 0xFFF8E7`, `PageEdge = 0xD9CBA8`, `Script = 0xB49A6A`, `private const double PageEdgeWidth = 0.6;`, `private const double ScriptWidth = 0.8;`; at the end of `Draw`:

```csharp
        // An open grimoire floats in front while you speak; its right page turns.
        if (pose == ChibiPose.Listen)
        {
            s.Path("M34 112 q13 -6 26 0 q13 -6 26 0 v14 q-13 -6 -26 0 q-13 -6 -26 0z", Rgb(Cover));
            s.Path("M36 111 q12 -5 24 0 v12 q-12 -5 -24 0z M60 111 q12 -5 24 0 v12 q-12 -5 -24 0z", Rgb(Paper));
            s.Path("M40 114 h14 M40 117 h14 M40 120 h14", null, Rgb(Script), ScriptWidth);
            var page = s.Path("M60 111 q12 -5 24 0 v12 q-12 -5 -24 0z", Rgb(Page), Rgb(PageEdge), PageEdgeWidth);
            s.Move(page, ChibiMove.Flap, origin: new Point(60, 117));
        }
```

- [ ] **Step 5:** `dotnet test VoxScribe.CrossPlatform.slnf` → PASS; warnings gate; commit `feat: servo-skull quill, netrunner holo screen, wizard grimoire`.

---

### Task 3: Clawd spinner and trot, Owl swivel and wink, Mic ON AIR

**Files:** Modify `ClawdChibi.cs`, `OwlChibi.cs`, `MicChibi.cs`; test `ChibiTests.cs`.

- [ ] **Step 1: Failing tests** — append to `ChibiTests`:

```csharp
    private static string TextIn(ChibiFace face, PillPhase phase)
    {
        face.Update(new PillState(phase, 0.5, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
        return string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }

    [AvaloniaTheory]
    [InlineData("clawd", PillPhase.Working, "Thinking…")]
    [InlineData("mic", PillPhase.Recording, "ON AIR")]
    public void A_signature_says_its_word(string id, PillPhase phase, string word)
    {
        var face = Mascots.Find(id)!.Create(Tokens.Colors.Accent);
        var window = new Window { Content = face, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            window.Show();
            TextIn(face, phase).ShouldContain(word, customMessage: id);
        }
        finally
        {
            window.Close();
        }
    }
```

and add `[InlineData("owl", PillPhase.Latency, <current + 2>)]` to `Each_signature_draws_in_its_pose` (the wink replaces one path with two shapes plus a glint: count before, then set current + 2). Run → FAIL.

- [ ] **Step 2: Clawd** — add `private const uint Caption = 0xCFD3DC;` and `private const double SpinnerSize = 18;`, `private const double CaptionSize = 7;`. Replace the leg loop `foreach (var x in new[] { 3, 5, 10, 12 }) Pixel(s, x, 6, 1, 2, body);` with:

```csharp
        // Four legs; while idle they trot in opposition, one pixel at a time.
        int[] legs = [3, 5, 10, 12];
        for (var i = 0; i < legs.Length; i++)
        {
            var leg = Pixel(s, legs[i], 6, 1, 2, body);
            if (pose == ChibiPose.Idle) s.Move(leg, ChibiMove.Wave, i % 2);
        }
```

and at the end of `Draw`:

```csharp
        // Claude Code's own spinner while thinking.
        if (pose == ChibiPose.Think)
        {
            s.Move(s.Text("✻", 50, 20, 20, SpinnerSize, body), ChibiMove.Spin);
            s.Text("Thinking…", 30, 42, 60, CaptionSize, Rgb(Caption));
        }
```

- [ ] **Step 3: Owl** — in `Draw`, change the thinking head move from `ChibiMove.Tilt` to `ChibiMove.Swivel` (same origin). Replace the Done eyes `HappyEyes(s, 48, 72, 68, ink);` with a wink:

```csharp
                // A wink: one happy eye, one open.
                s.Path("M42 68 q6 -7 12 0", null, ink, 3.5);
                s.Circle(72, 66, 5, ink);
                s.Circle(70, 64, 2, Rgb(Glint));
```

- [ ] **Step 4: Mic** — add constants `Plaque = 0x2A2D38`, `Amber = 0xFFC857`, `private const double OnAirSize = 8;`, `private const double RingWidth = 1.2;`; at the end of `Draw`:

```csharp
        // ON AIR lights up (amber, never red) and rings spread from the grille while you speak.
        if (pose == ChibiPose.Listen)
        {
            var plaque = s.Rect(38, 4, 44, 12, 3, Rgb(Plaque));
            plaque.Stroke = Rgb(Amber);
            plaque.StrokeThickness = 1;
            s.Move(s.Text("ON AIR", 38, 5, 44, OnAirSize, Rgb(Amber)), ChibiMove.Pulse);
            for (var i = 0; i < 3; i++)
            {
                var ring = s.Circle(60, 47, 26, null);
                ring.Stroke = Rgb(Amber);
                ring.StrokeThickness = RingWidth;
                s.Move(ring, ChibiMove.Ring, i);
            }
        }
```

- [ ] **Step 5:** tests PASS; warnings gate; commit `feat: clawd spinner and trot, owl swivel and wink, mic on air`.

---

### Task 4: Cat keyboard and maneki paw, Slime swallows words, Dragon naps

**Files:** Modify `CatChibi.cs`, `SlimeChibi.cs`, `DragonChibi.cs`; test `ChibiTests.cs`.

- [ ] **Step 1: Failing tests** — add `InlineData` rows to `Each_signature_draws_in_its_pose` for `("cat", PillPhase.Working, current + 10)`, `("cat", PillPhase.Latency, current + 2)`; and to `A_signature_says_its_word` for `("slime", PillPhase.Recording, "b")` and `("dragon", PillPhase.Idle, "Z")`. Measure current counts first, as in Task 2. Run → FAIL.

- [ ] **Step 2: Cat** — add `Keyboard = 0x3A3F4B`, `Keycap = 0x9AA0AD`; at the end of `Draw`:

```csharp
        // Keyboard cat while thinking; a maneki-neko paw when done.
        if (pose == ChibiPose.Think)
        {
            s.Rect(34, 120, 52, 10, 2, Rgb(Keyboard));
            for (var i = 0; i < 6; i++) s.Rect(37 + (i * 8), 122, 6, 3, 1, Rgb(Keycap));
            s.Move(s.Ellipse(48, 118, 6, 4, Rgb(Cream)), ChibiMove.Wave, 0);
            s.Move(s.Ellipse(72, 118, 6, 4, Rgb(Cream)), ChibiMove.Wave, 1);
        }

        if (pose == ChibiPose.Done)
        {
            s.Ellipse(88, 70, 7, 9, fur);
            s.Ellipse(88, 66, 5, 4, Rgb(Cream));
        }
```

- [ ] **Step 3: Slime** — add `private const double LetterSize = 10;`; inside the body group, just before `s.End();`:

```csharp
        // It swallows your words: letters fall into the jelly and fade.
        if (pose == ChibiPose.Listen)
        {
            string[] letters = ["b", "o", "n", "j"];
            for (var i = 0; i < letters.Length; i++)
            {
                s.Move(s.Text(letters[i], 44 + (i * 8), 34, 10, LetterSize, ink), ChibiMove.Fall, i);
            }
        }
```

- [ ] **Step 4: Dragon** — add `Snore = 0xCFD3DC`, `private const double SnoreSize = 8;`, `private const double SnoreGrowth = 3;`; at the end of `Draw`:

```csharp
        // Napping: z z Z drift up while it dozes.
        if (pose == ChibiPose.Idle)
        {
            string[] snore = ["z", "z", "Z"];
            for (var i = 0; i < snore.Length; i++)
            {
                s.Move(s.Text(snore[i], 82 + (i * 6), 38 - (i * 6), 14, SnoreSize + (i * SnoreGrowth), Rgb(Snore)), ChibiMove.Rise, i);
            }
        }
```

- [ ] **Step 5:** full suite PASS; warnings gate; commit `feat: keyboard cat, slime swallows words, dragon naps`.
