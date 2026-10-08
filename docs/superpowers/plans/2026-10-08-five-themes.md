# Five Themes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the three old themes and the Void Glass look with five selectable themes (Paper, Orb, Tide, Mono, Fluent). Each has a light and a dark palette and curated accent variants, and all of them share one sidebar window (Home · History · Dictionary · Settings). Each theme gets its own dictation-pill silhouette. Theme, variant and Windows light/dark changes repaint live.

**Architecture:** A theme is data: a `ThemeDefinition` record (two palettes, accent variants, fonts, and a handful of layout knobs). `Themes.Apply(themeId, variantId, dark)` writes the resolved values into the existing mutable `Tokens.*` and raises `Themes.Changed`. Windows rebuild their C#-built content when that event fires. Views keep reading only `Tokens`, plus `Themes.Active` for the knobs. The pill keeps `HudWindow`'s engine-polling state machine and hands each frame to a per-theme `PillFace`.

**Tech Stack:** C# / .NET 10, Avalonia 11.3.20 (C#-built views, FluentTheme), xUnit v2 + Shouldly + Avalonia.Headless.XUnit 11.3.20, bundled OFL fonts as `avares://` resources.

## Global Constraints

Every task's requirements implicitly include this section. Read it fully before starting any task.

**Where and how**
- Worktree: `C:/Users/guill/orca/workspaces/vox-scribe/themes` (branch `Arylmera/five-themes`). All paths below are relative to it. Run every dotnet command from `windows/`.
- Verification, which is exactly what CI runs (`.github/workflows/windows.yml`). Each task ends with all three green:
  ```bash
  cd C:/Users/guill/orca/workspaces/vox-scribe/themes/windows
  dotnet format VoxScribe.sln --verify-no-changes
  dotnet build VoxScribe.sln --configuration Release --no-incremental -warnaserror
  dotnet test VoxScribe.sln --no-build --configuration Release
  ```
  - If the format gate fails, run `dotnet format VoxScribe.sln`, review the diff, and re-run all three.
  - `--no-incremental` is mandatory. Roslyn does not re-emit analyzer warnings on a cached build.
  - Baseline before this plan: build clean, 202 tests passing (24 Dictionary + 139 Core + 39 App).
- `Directory.Build.props` sets `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild` and `GenerateDocumentationFile` (src projects only). Consequences:
  - Every **public** type and member in `src/` needs a `///` summary, or CS1591 fails the build.
  - New App types are `internal` unless something public must expose them. `VoxScribe.App` has `InternalsVisibleTo VoxScribe.App.Tests`.
  - Number and date formatting needs an explicit `CultureInfo` (CA1305).
  - Seal classes. Make static what can be static.
- Commit at the end of each task with a bash heredoc (`git commit -F - <<'EOF' … EOF`). No backticks inside `-m`. Never `--no-verify`. Do not tag, push or release.
- Tests that call `Themes.Apply` must be `[AvaloniaFact]`, never `[Fact]`, so they run serialized on the headless UI thread. They must restore `Themes.Apply(Themes.DefaultId, null, false)` in a `finally`.
- From Task 4 on, a test that builds a `MainWindow` sets `window.ExitAllowed = true; window.Close();` when it is done (in a `finally` where the test already has one). Otherwise every leftover window stays subscribed to `Themes.Changed` and rebuilds on each later `Apply`. Tests written before Task 4 that Task 4 or a later task touches must be updated the same way.

**Project law (from `AGENTS.md`)**
- `windows/src/VoxScribe.App/Design/DesignTokens.cs` owns every colour, size, radius and duration. **Views contain no literal values.** A number that only one control's own drawing arithmetic uses may be a named `private const` in that control. Theme data (palette hex in `ThemeCatalog`) is not a view literal.
- Views are built in C#, not XAML.
- `VoxScribe.Platform.Windows` stays logic-free. No new Win32 code anywhere. Light/dark detection uses Avalonia's `Application.ActualThemeVariant` / `ActualThemeVariantChanged` only.
- Pinned packages stay as they are: NAudio 2.3.0, Avalonia.Headless.XUnit 11.3.20, org.k2fsa.sherpa.onnx 1.13.5. No new NuGet packages.
- Keep all non-UI behaviour unchanged: engine, hooks, injection, dictionary, history storage, `KeyboardHook`.

**Themes (binding decisions)**
- Exactly 5 themes, in this order. Ids are `paper`, `orb`, `tide`, `mono`, `fluent`; labels are `Paper`, `Orb`, `Tide`, `Mono`, `Fluent`. They replace `deep-field`, `signal-house`, `manuscript` and Void Glass.
- **Default theme: `paper`.** Any unknown, old or missing theme id resolves to `paper`. An unknown or missing variant id resolves to the theme's first variant.
- Every theme has a light and a dark palette. The app follows Windows light/dark through `Application.Current.ActualThemeVariant == ThemeVariant.Dark`.
- Accent variants, written as id = light / dark. Orb variants also carry a second gradient stop, written as `(+ light2 / dark2)`.
  - Paper: plum `#6A3D9A/#C3A4EC`, ink-blue `#2F4C80/#9DB6E8`, moss `#4D6A35/#A9C98C`, ochre `#8A5A12/#E0B062`.
  - Orb: nebula `#5B3FD6/#8C74FF (+#0E8FA8/#5CE1F2)`, aurora `#0D7C5F/#3FE0B0 (+#2F6FD6/#7FA8FF)`, emerald `#127E3E/#3DDC84 (+#4E7F12/#B8F25C)`, ion `#2350D8/#6F92FF (+#6A36D1/#B58CFF)`, solar `#A35A00/#FF9F3D (+#8A6D00/#FFE07A)`.
    - aurora and emerald light values are darkened from the mockup's `#0F8A6A` / `#13803F` so they reach 4.5:1 on Orb's light ground.
  - Tide: lagoon `#0B6FA0/#6CCDF5`, lilac `#5F4BC0/#B9A9FF`, mint `#0B7360/#6FE3C2`, fern `#2E7A2E/#8EDB7A`, peach `#9A4E14/#FFB27A`.
  - Mono: lime `#3F6B00/#C6FF3D`, phosphor `#0A7A3B/#39FF88`, cyan `#006B7A/#3DF0FF`, violet `#5A2DB8/#B69CFF`, amber `#8A5200/#FFB13D`.
  - Fluent: system (label "Windows blue", fixed values, does not read the OS accent) `#005FB8/#60CDFF`, teal `#00777A/#4CD9D9`, orchid `#8240B3/#D3A6FF`, forest `#107C10/#6CCB5F`.
  - Each theme's green variant is Paper moss, Orb emerald, Tide fern, Mono phosphor and Fluent forest.
- The old free-form `AccentColor` setting is removed. Old files that still carry it load fine; the value is ignored.
- Live repaint: switching theme, variant or Windows light/dark rebuilds each window's content. No restart, no DynamicResource migration.

**Fonts**
- Bundled OFL fonts are static TTFs under `windows/src/VoxScribe.App/Assets/Fonts/<Family>/`, with each family's `OFL.txt`.
  - Instrument Serif: Regular, Italic.
  - Geist: Regular, Medium, SemiBold.
  - Geist Mono: Regular, Medium.
  - Figtree: Regular, Medium, SemiBold, Bold, ExtraBold.
  - JetBrains Mono: Regular, Medium, Bold, ExtraBold.
- The existing `<AvaloniaResource Include="Assets\**" />` already embeds them. Do not add a second entry.
- Fluent uses system `Segoe UI Variable Text` / `Segoe UI Variable Display` and `Cascadia Mono`.

**Colour doctrine (replaces the old one)**
- Red `#E5484D` (`Tokens.Colors.Record`, shared by all themes, settable by none) is used **only** for the recording dot. Nothing else is red. Connection failures and warnings use `Tokens.Colors.Caution`, which is never red.
- The rule "amber and green are instrumentation only" is **dropped**.
  - `Positive` is shared by all themes: light `#107C10`, dark `#6CCB5F`.
  - `Caution` is shared by all themes: light `#8A5200`, dark `#FFB13D`.
- WCAG AA, enforced by `windows/tests/VoxScribe.App.Tests/Design/ContrastTests.cs` for all 5 themes × light/dark × every variant. 4.5:1 for:
  - ink and inkMuted on ground, surface, surfaceRaised and hover
  - accent on ground and surface
  - onAccent on accentFill
  - ink, inkMuted and accent on the pill fill
  - positive and caution on ground and surface
  - accent on accent-tint over surface, for themes whose `NavSelection` is `Tint`
- Accent-coloured text is never placed on `Hover` or on tint-over-ground. The exception is the Tide hero stat (large text, checked at 3:1). The record red on ground is checked at 3:1.

**Window skeleton**
- One main window. The sidebar runs Home · History · Dictionary · Settings.
- The old transport (record key, VU meter, big counter, hero panel, bottom deck) is gone.
- `SettingsWindow` is removed. Settings is a page with 6 tabs, in order: General, Speech, Shortcuts, Typing, Cleanup, Appearance. There is no Voice chat section on this branch.
- The tray's "Settings…" opens the main window on the Settings page.
- Home page, top to bottom:
  - headline
  - status line: `Ready · <model> · <cleanup> · Hold <chord> to dictate`
  - 4 stats: words this week (rolling 7 days), average WPM, time saved vs typing at 40 WPM, streak in days
  - the last 5 dictations, each with Copy and Type-again buttons, plus a "See all" link to History
  - a chord reminder footer: Raw / Cleanup / Undo / Command
- Stats math lives in `VoxScribe.Core.HomeStats`, which is platform-neutral and unit-tested.

**Pill**
- Hidden at rest. Never takes focus: `ShowActivated = false`, `Focusable = false`, `IsHitTestVisible = false`, and `Overlay.MakeOverlay` stays.
- What it shows:
  - the red dot, only while recording
  - a per-theme level visual
  - a one-line, tail-only streaming preview that never grows taller with long text
  - the mode badge and timer as secondary text
- Plain translucency only. No system blur or acrylic.
- The OS window has a fixed size: `Tokens.Size.PillWindowWidth` = 520, `Tokens.Size.PillWindowHeight` = 180. The face is bottom-centred inside it. Expand/collapse animates the face's `Width` over `Tokens.Motion.PillExpand` = 150 ms. Only the level visual (wave, stroke, meter, orb, halo) otherwise moves.
- Silhouettes. The specs are `.superpowers/mockups/<Theme>-Pill.dc.html`; read the one for your task.
  - Paper: a paper slip tilted −0.6°. The level is a pen stroke on a ruled line. The preview is in Instrument Serif italic.
  - Orb: no body and no waveform. A glowing orb whose diameter follows the level, from 36 px at silence to 76 px at peak, with two halos while speaking. The red dot sits on its rim. The preview is in a small glass capsule beside it.
  - Tide: a soft white (light) / navy `#16303F` (dark) capsule with a liquid wave and fill.
  - Mono: a square status line with a 16-cell block meter and a shell-prompt preview with a block cursor. A hard 4 px offset shadow in light mode only.
  - Fluent: a native Win11 flyout with an accent mic button whose halo grows with the level.

**Out of scope**
- git tag, push, release and install (the controller does these).
- Code signing.
- Mica on the main window.
- Reading the Windows accent colour.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `windows/src/VoxScribe.App/Views/MainWindow.cs` | Window shell: title strip, nav, page host, rebuild-on-theme | 1 (trim), 4, 6, 7 |
| `windows/src/VoxScribe.App/Controls/VuMeter.cs`, `RecordButton.cs`, `ViewModels/MainWindowViewModel.cs` | deleted | 1 |
| `windows/src/VoxScribe.App/Design/FontFaces.cs` | Bundled font families | 2 |
| `windows/src/VoxScribe.App/Assets/Fonts/**` | TTF + OFL.txt | 2 |
| `windows/src/VoxScribe.App/Design/ThemeModel.cs` | `Palette`, `AccentVariant`, `ThemeDefinition`, knob enums | 3 |
| `windows/src/VoxScribe.App/Design/ThemeCatalog.cs` | The five theme definitions | 3 |
| `windows/src/VoxScribe.App/Design/Themes.cs` | `Apply`, `Changed`, `Find` | 3 |
| `windows/src/VoxScribe.App/Design/DesignTokens.cs` | Tokens (settable theme values, new tokens) | 1, 3, 6, 7, 9 |
| `windows/src/VoxScribe.Core/AppSettings.cs` | `Theme` default `paper`, `AccentVariant`, no `AccentColor` | 3 |
| `windows/src/VoxScribe.App/App.axaml.cs` | Live theme wiring, tray → Settings page | 4, 7 |
| `windows/src/VoxScribe.Core/HomeStats.cs` | Stats math | 5 |
| `windows/src/VoxScribe.App/Views/Shell.cs` | Nav, selection painting, icons, orb mark, page titles | 6 |
| `windows/src/VoxScribe.App/Views/HomePage.cs` | Home page | 6 |
| `windows/src/VoxScribe.App/Controls/DashedRule.cs` | Dashed hairline | 6 |
| `windows/src/VoxScribe.App/Views/SettingsPage.cs` | Settings tabs + chord recorder (moved from `SettingsWindow.cs`, deleted) | 7 |
| `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs` | Theme cards + variant swatches | 3, 4, 8 |
| `windows/src/VoxScribe.App/Views/HudWindow.cs` | Pill window + state machine | 3, 4, 9 |
| `windows/src/VoxScribe.App/Views/Pill/*.cs` | `PillFace` + five faces | 9, 10 |
| `AGENTS.md`, `windows/README.md` | Doctrine and docs | 11 |
| `windows/Directory.Version.props` | 1.4.0 | 12 |

---

### Task 1: Remove the transport and the old theme layout knobs

The transport (record key, VU meter, counter, hero panel, bottom deck) and the per-theme layout switches (`TransportDock`, `TranscriptStyle`, `ShowRail`, `NeedleGauge`) go away. The three old palettes stay until Task 3. This task only deletes code.

**Files:**
- Modify: `windows/src/VoxScribe.App/Views/MainWindow.cs` (full replacement below)
- Modify: `windows/src/VoxScribe.App/Design/Themes.cs`
- Modify: `windows/src/VoxScribe.App/Views/TranscriptionsView.cs`
- Modify: `windows/src/VoxScribe.App/Design/DesignTokens.cs`
- Delete: `windows/src/VoxScribe.App/Controls/VuMeter.cs`, `windows/src/VoxScribe.App/Controls/RecordButton.cs`, `windows/src/VoxScribe.App/ViewModels/MainWindowViewModel.cs`
- Test: `windows/tests/VoxScribe.App.Tests/UiTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `MainWindow` keeps `MainWindow()`, `MainWindow(Composition?)` and `ExitAllowed`, and loses `ToggleRecording`, `IsRecording`, `RecordLamp` and `Meter`. `Themes` keeps `Default`, `Choices`, `ActiveId`, `Apply(string?)` and `PillRadius`.

- [ ] **Step 1: Update the tests first**

In `windows/tests/VoxScribe.App.Tests/UiTests.cs`:
- Delete `MainWindowTests.Record_toggles_the_lamp_and_the_meter_together`.
- Delete `EquipmentTests.Meter_renders_without_throwing`.
- In `DesignSystemTests.Nothing_is_red_unless_it_asks_to_be`, delete the line `new MainWindow().RecordLamp.LampColor.ShouldBe(Tokens.Colors.Record);`.
- Replace the body of `Every_theme_builds_its_own_window_layout` with:

```csharp
        try
        {
            foreach (var (id, _) in Themes.Choices)
            {
                Themes.Apply(id);
                var window = new MainWindow();
                window.Show();
                window.Bounds.Width.ShouldBeGreaterThan(0, $"theme {id}");
            }
        }
        finally
        {
            Themes.Apply(Themes.Default);
        }
```

- Replace the whole `Needle_ballistics_keep_the_vu_character_but_track_live_speech` test with:

```csharp
    [AvaloniaFact]
    public void Level_gain_lifts_speech_into_view()
    {
        // Speech RMS lives around 0.02–0.15; without gain the pill's level visual barely moves.
        Tokens.Motion.LevelGain.ShouldBeGreaterThan(1);
    }
```

- In `KeyHitAreaTests.Custom_keys_are_hit_testable_across_their_whole_face`, change the array to `Button[] keys = [new TransportKey(), new RailKey("M4,10 V14")];`.

- [ ] **Step 2: Delete the transport controls**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes/windows/src/VoxScribe.App
git rm Controls/VuMeter.cs Controls/RecordButton.cs ViewModels/MainWindowViewModel.cs
```

- [ ] **Step 3: Replace `windows/src/VoxScribe.App/Views/MainWindow.cs` entirely**

```csharp
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views;

/// <summary>
/// The main window: a navigation rail, a title strip in the extended chrome, and the open section.
/// </summary>
/// <remarks>
/// Built in code rather than XAML, deliberately: every value comes from <see cref="Tokens"/>.
/// </remarks>
public sealed class MainWindow : Window
{
    private const string WaveIcon = "M4,10 V14 M8,7 V17 M12,4 V20 M16,8 V16 M20,10 V14";
    private const string BookIcon = "M5,4 H16 A3,3 0 0 1 19,7 V20 H8 A3,3 0 0 1 5,17 Z M9,9 H15";
    private const string GearIcon =
        "M19,12 a7,7 0 0 0 -0.1,-1.2 l2,-1.6 -2,-3.4 -2.4,1 a7,7 0 0 0 -2,-1.2 L14,3 h-4 "
        + "l-0.5,2.6 a7,7 0 0 0 -2,1.2 l-2.4,-1 -2,3.4 2,1.6 A7,7 0 0 0 5,12 a7,7 0 0 0 "
        + "0.1,1.2 l-2,1.6 2,3.4 2.4,-1 a7,7 0 0 0 2,1.2 L10,21 h4 l0.5,-2.6 a7,7 0 0 0 "
        + "2,-1.2 l2.4,1 2,-3.4 -2,-1.6 A7,7 0 0 0 19,12 Z M15,12 a3,3 0 1 1 -6,0 "
        + "a3,3 0 0 1 6,0";
    private const string MicIcon = "M12,4 V13 M8,8 V11 M16,8 V11 M12,17 V20 M7,13 a5,5 0 0 0 10,0";

    private readonly Composition? _composition;
    private readonly ContentControl _sectionHost = new();
    private readonly RailKey _transcriptionsKey;
    private readonly RailKey _dictionaryKey;

    private Control? _transcriptionsView;
    private Control? _dictionaryView;

    /// <summary>Set just before an explicit quit so the hide-to-tray guard steps aside.</summary>
    public bool ExitAllowed { get; set; }

    /// <summary>Builds a window with no engine behind it. Used by headless tests.</summary>
    public MainWindow() : this(null) { }

    /// <summary>Builds the window over <paramref name="composition"/>.</summary>
    public MainWindow(Composition? composition)
    {
        _composition = composition;

        Title = "Vox-Scribe";
        MinWidth = Tokens.Size.MainMinWidth;
        MinHeight = Tokens.Size.MainMinHeight;
        Width = Tokens.Size.MainWidth;
        Height = Tokens.Size.MainHeight;
        Background = Tokens.Brushes.Chassis;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
            new Uri("avares://VoxScribe.App/Assets/app.ico")));

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = Tokens.Material.TitleBarHeight;

        // The close button hides to the tray: a closed Avalonia window is destroyed and the
        // tray's "Show" could never bring it back. Real exit sets ExitAllowed first.
        Closing += (_, e) =>
        {
            if (ExitAllowed) return;
            e.Cancel = true;
            Hide();
        };

        _transcriptionsKey = new RailKey(WaveIcon) { IsEngaged = true };
        Avalonia.Automation.AutomationProperties.SetName(_transcriptionsKey, "Transcriptions");
        _dictionaryKey = new RailKey(BookIcon);
        Avalonia.Automation.AutomationProperties.SetName(_dictionaryKey, "Dictionary");
        _transcriptionsKey.Click += (_, _) => ShowSection(transcriptions: true);
        _dictionaryKey.Click += (_, _) => ShowSection(transcriptions: false);

        Content = BuildLayout();
        ShowSection(transcriptions: true);

        Opacity = Tokens.Motion.FadeInFrom;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = Tokens.Motion.FadeIn },
        };
        Loaded += (_, _) => Opacity = 1;

        _composition?.Engine?.Start();
    }

    private DockPanel BuildLayout()
    {
        var root = new DockPanel();
        root.Children.Add(Panels.Docked(BuildRail(), Dock.Left));

        var content = new DockPanel();
        content.Children.Add(Panels.Docked(BuildTitleStrip(), Dock.Top));
        if (_composition is not null && !Composition.IsModelInstalled)
        {
            content.Children.Add(Panels.Docked(BuildModelBanner(), Dock.Top));
        }

        _sectionHost.Margin = new Thickness(
            Tokens.Space.Roomy, Tokens.Space.Snug, Tokens.Space.Roomy, Tokens.Space.Roomy);
        content.Children.Add(_sectionHost);

        root.Children.Add(content);
        return root;
    }

    private Border BuildRail()
    {
        var badge = new Border
        {
            Width = Tokens.Material.BadgeSize,
            Height = Tokens.Material.BadgeSize,
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            Background = new SolidColorBrush(Tokens.Colors.Accent),
            IsHitTestVisible = false,
            Margin = new Thickness(0, 0, 0, Tokens.Space.Roomy),
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(MicIcon),
                Stroke = Tokens.Brushes.Chassis,
                StrokeThickness = Tokens.Material.BadgeIconStroke,
                StrokeLineCap = PenLineCap.Round,
                Width = Tokens.Material.BadgeIconSize,
                Height = Tokens.Material.BadgeIconSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var settings = new RailKey(GearIcon) { HorizontalAlignment = HorizontalAlignment.Center };
        settings.Click += (_, _) => ShowSettings();

        var rail = new DockPanel { LastChildFill = false };
        rail.Children.Add(Panels.Docked(new StackPanel
        {
            Spacing = Tokens.Space.Tight,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { badge, _transcriptionsKey, _dictionaryKey },
        }, Dock.Top));
        rail.Children.Add(Panels.Docked(settings, Dock.Bottom));

        return new Border
        {
            Width = Tokens.Material.RailWidth,
            Background = Tokens.Brushes.Panel,
            Padding = new Thickness(0, Tokens.Space.Base),
            Child = rail,
        };
    }

    private static Border BuildTitleStrip() => new()
    {
        Height = Tokens.Material.TitleBarHeight,
        Padding = new Thickness(Tokens.Space.Roomy, 0, Tokens.Material.CaptionButtonsReserve, 0),
        Child = new TextBlock
        {
            Text = "Vox-Scribe",
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            FontWeight = FontWeight.SemiBold,
            Foreground = Tokens.Brushes.Ink,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        },
    };

    private static BrushedPanel BuildModelBanner() => new()
    {
        Margin = new Thickness(Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Base),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            Margin = new Thickness(Tokens.Space.Base),
            Children =
            {
                new Lamp
                {
                    IsLit = true,
                    LampColor = Tokens.Colors.MeterAmber,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = "Speech model not installed — Vox-Scribe cannot transcribe yet. "
                         + "See Settings, or docs/PARAKEET-WINDOWS.md.",
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Label,
                    Foreground = Tokens.Brushes.Ink,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        },
    };

    private void ShowSection(bool transcriptions)
    {
        _transcriptionsKey.IsEngaged = transcriptions;
        _dictionaryKey.IsEngaged = !transcriptions;

        if (_composition is null)
        {
            _sectionHost.Content = Panels.EmptyState(
                transcriptions ? "NO RECORDINGS" : "DICTIONARY EMPTY",
                transcriptions ? "Hold the push-to-talk key and speak." : "Add words it keeps getting wrong.");
            return;
        }

        // Built once and reused: rebuilding would drop the user's search text.
        if (transcriptions)
        {
            _transcriptionsView ??= new TranscriptionsView(_composition.Transcripts);
            _sectionHost.Content = _transcriptionsView;
        }
        else
        {
            _dictionaryView ??= new DictionaryView(_composition.Dictionary, _composition.Transcripts);
            _sectionHost.Content = _dictionaryView;
        }
    }

    private void ShowSettings()
    {
        if (_composition is null) return;
        _ = new SettingsWindow(_composition.Settings, _composition.Engine).ShowDialog(this);
    }
}
```

- [ ] **Step 4: Strip the layout knobs from `windows/src/VoxScribe.App/Design/Themes.cs`**
  - Delete the enums `TransportDock` and `TranscriptStyle`, including their doc comments and the stray `<summary>` block above `TransportDock`.
  - Delete the properties `Transport`, `Transcripts`, `ShowRail` and `NeedleGauge`.
  - In `DeepField()`, `SignalHouse()` and `Manuscript()`, delete the lines assigning `Transport`, `Transcripts`, `ShowRail` and `NeedleGauge`.
  - Keep `PillRadius` and everything else.

- [ ] **Step 5: Make `TranscriptionsView` always use the bare hairline row**

In `windows/src/VoxScribe.App/Views/TranscriptionsView.cs`:
- Delete `TakeNumber`, `BuildTakeRow` and `BuildJournalRow`.
- Replace the tail of `Refresh()`, from `// The journal groups by day` to the end of the method, with:

```csharp
        foreach (var record in records) _list.Children.Add(BuildRow(record));
    }
```

- Change `BuildRow`'s signature to `private Border BuildRow(TranscriptRecord record)` and replace its final `return Themes.Transcripts switch { … };` with `return BuildBareRow(record, text, actions);`.

- [ ] **Step 6: Delete tokens that only the transport used**

In `windows/src/VoxScribe.App/Design/DesignTokens.cs`, delete these members:
- `Fonts.CounterLarge`, `Fonts.CounterHero`
- `Material.RecordKeySize`, `Material.RecordLensSize`
- `Motion.MeterFrame`, `Motion.PanelPoll`, `Motion.NeedleAttackSeconds`, `Motion.NeedleReleaseSeconds`, `Motion.NeedleOvershoot`

Then confirm nothing else uses them:

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes/windows
grep -rn "CounterLarge\|CounterHero\|RecordKeySize\|RecordLensSize\|MeterFrame\|PanelPoll\|Needle" --include=*.cs src tests
```

Expected: no output.

- [ ] **Step 7: Run the Global Constraints verification**

Expected: build clean, all tests green (App test count drops by 2).

- [ ] **Step 8: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
refactor: remove the transport and per-theme layout switches

The record key, VU meter and counter leave the main window; themes no longer
re-architect the layout. Groundwork for the five-theme skeleton.
EOF
```

---

### Task 2: Bundle the OFL fonts

**Files:**
- Create: `windows/src/VoxScribe.App/Assets/Fonts/{InstrumentSerif,Geist,GeistMono,Figtree,JetBrainsMono}/*.ttf` and `OFL.txt`
- Create: `windows/src/VoxScribe.App/Design/FontFaces.cs`
- Test: `windows/tests/VoxScribe.App.Tests/Design/FontFacesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class FontFaces` with `FontFamily` properties `InstrumentSerif`, `Geist`, `GeistMono`, `Figtree`, `JetBrainsMono`, `SegoeText`, `SegoeDisplay` and `CascadiaMono`, plus `IReadOnlyList<string> Files` (relative asset paths).

- [ ] **Step 1: Download the fonts**

Every URL below was checked and returns HTTP 200. These are the upstream static builds; google/fonts ships only variable `[wght]` files, and Avalonia 11 does not reliably select weights from a variable axis.

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes/windows/src/VoxScribe.App/Assets
mkdir -p Fonts/InstrumentSerif Fonts/Geist Fonts/GeistMono Fonts/Figtree Fonts/JetBrainsMono
GF=https://raw.githubusercontent.com/google/fonts/main/ofl/instrumentserif
GE=https://raw.githubusercontent.com/vercel/geist-font/main
FT=https://raw.githubusercontent.com/erikdkennedy/figtree/master
JB=https://raw.githubusercontent.com/JetBrains/JetBrainsMono/master
set -e
for f in InstrumentSerif-Regular.ttf InstrumentSerif-Italic.ttf OFL.txt; do curl -fsSL -o Fonts/InstrumentSerif/$f $GF/$f; done
for w in Regular Medium SemiBold; do curl -fsSL -o Fonts/Geist/Geist-$w.ttf $GE/fonts/Geist/ttf/Geist-$w.ttf; done
curl -fsSL -o Fonts/Geist/OFL.txt $GE/OFL.txt
for w in Regular Medium; do curl -fsSL -o Fonts/GeistMono/GeistMono-$w.ttf $GE/fonts/GeistMono/ttf/GeistMono-$w.ttf; done
curl -fsSL -o Fonts/GeistMono/OFL.txt $GE/OFL.txt
for w in Regular Medium SemiBold Bold ExtraBold; do curl -fsSL -o Fonts/Figtree/Figtree-$w.ttf $FT/fonts/ttf/Figtree-$w.ttf; done
curl -fsSL -o Fonts/Figtree/OFL.txt $FT/OFL.txt
for w in Regular Medium Bold ExtraBold; do curl -fsSL -o Fonts/JetBrainsMono/JetBrainsMono-$w.ttf $JB/fonts/ttf/JetBrainsMono-$w.ttf; done
curl -fsSL -o Fonts/JetBrainsMono/OFL.txt $JB/OFL.txt
find Fonts -type f | sort
```

Expected: 21 files (16 `.ttf`, 5 `OFL.txt`), each non-empty. `VoxScribe.App.csproj` already has `<AvaloniaResource Include="Assets\**" />`, so do not edit the csproj.

- [ ] **Step 2: Write the failing test**

Create `windows/tests/VoxScribe.App.Tests/Design/FontFacesTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Shouldly;
using VoxScribe.App.Design;

namespace VoxScribe.App.Tests.Design;

/// <summary>
/// The bundled fonts are embedded resources. The headless platform draws with a stub font
/// manager, so a glyph lookup would prove nothing; what can fail here is a file missing from
/// the assembly.
/// </summary>
public sealed class FontFacesTests
{
    [AvaloniaFact]
    public void Every_bundled_font_file_is_embedded()
    {
        FontFaces.Files.Count.ShouldBe(16);
        foreach (var file in FontFaces.Files)
        {
            AssetLoader.Exists(new Uri($"avares://VoxScribe.App/{file}")).ShouldBeTrue(file);
        }
    }

    [AvaloniaFact]
    public void Every_family_ships_its_licence()
    {
        foreach (var family in new[] { "InstrumentSerif", "Geist", "GeistMono", "Figtree", "JetBrainsMono" })
        {
            AssetLoader.Exists(new Uri($"avares://VoxScribe.App/Assets/Fonts/{family}/OFL.txt")).ShouldBeTrue(family);
        }
    }
}
```

- [ ] **Step 3: Run it to watch it fail**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes/windows
dotnet test tests/VoxScribe.App.Tests --filter FullyQualifiedName~FontFacesTests
```

Expected: compile error, `FontFaces` does not exist.

- [ ] **Step 4: Create `windows/src/VoxScribe.App/Design/FontFaces.cs`**

```csharp
using Avalonia.Media;

namespace VoxScribe.App.Design;

/// <summary>
/// The typefaces the five themes are set in. Four OFL families ship inside the assembly
/// (folder URI + <c>#Family Name</c>, the name embedded in the TTF); Fluent uses the faces
/// Windows 11 already has.
/// </summary>
internal static class FontFaces
{
    private const string Root = "avares://VoxScribe.App/Assets/Fonts/";

    /// <summary>Paper's display serif.</summary>
    public static FontFamily InstrumentSerif { get; } = new(Root + "InstrumentSerif#Instrument Serif");

    /// <summary>Paper and Orb body face.</summary>
    public static FontFamily Geist { get; } = new(Root + "Geist#Geist");

    /// <summary>Paper, Orb and Tide mono face.</summary>
    public static FontFamily GeistMono { get; } = new(Root + "GeistMono#Geist Mono");

    /// <summary>Tide's display and body face.</summary>
    public static FontFamily Figtree { get; } = new(Root + "Figtree#Figtree");

    /// <summary>Mono's only face.</summary>
    public static FontFamily JetBrainsMono { get; } = new(Root + "JetBrainsMono#JetBrains Mono");

    /// <summary>Fluent body text — system font.</summary>
    public static FontFamily SegoeText { get; } = new("Segoe UI Variable Text, Segoe UI, sans-serif");

    /// <summary>Fluent display text — system font.</summary>
    public static FontFamily SegoeDisplay { get; } = new("Segoe UI Variable Display, Segoe UI, sans-serif");

    /// <summary>Fluent mono — system font.</summary>
    public static FontFamily CascadiaMono { get; } = new("Cascadia Mono, Consolas, monospace");

    /// <summary>Every bundled TTF, relative to the assembly root. The embedding test walks this.</summary>
    public static IReadOnlyList<string> Files { get; } =
    [
        "Assets/Fonts/InstrumentSerif/InstrumentSerif-Regular.ttf",
        "Assets/Fonts/InstrumentSerif/InstrumentSerif-Italic.ttf",
        "Assets/Fonts/Geist/Geist-Regular.ttf",
        "Assets/Fonts/Geist/Geist-Medium.ttf",
        "Assets/Fonts/Geist/Geist-SemiBold.ttf",
        "Assets/Fonts/GeistMono/GeistMono-Regular.ttf",
        "Assets/Fonts/GeistMono/GeistMono-Medium.ttf",
        "Assets/Fonts/Figtree/Figtree-Regular.ttf",
        "Assets/Fonts/Figtree/Figtree-Medium.ttf",
        "Assets/Fonts/Figtree/Figtree-SemiBold.ttf",
        "Assets/Fonts/Figtree/Figtree-Bold.ttf",
        "Assets/Fonts/Figtree/Figtree-ExtraBold.ttf",
        "Assets/Fonts/JetBrainsMono/JetBrainsMono-Regular.ttf",
        "Assets/Fonts/JetBrainsMono/JetBrainsMono-Medium.ttf",
        "Assets/Fonts/JetBrainsMono/JetBrainsMono-Bold.ttf",
        "Assets/Fonts/JetBrainsMono/JetBrainsMono-ExtraBold.ttf",
    ];
}
```

- [ ] **Step 5: Run the test, then the Global Constraints verification**

Expected: `FontFacesTests` passes (2 tests) and everything else is green.

- [ ] **Step 6: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
feat: bundle Instrument Serif, Geist, Geist Mono, Figtree and JetBrains Mono

Static upstream TTFs with their OFL licences, embedded as Avalonia resources.
EOF
```

- [ ] **Step 7 (manual, note it in the task report):** In Task 3, when a theme is first painted on a real Windows machine, confirm the face actually changes. If a family name after `#` is wrong, Avalonia silently falls back to the default font. Check with Windows' font viewer (double-click the TTF): the title is the family name.

---
### Task 3: Theme model, the five themes, settings migration, contrast audit

**Files:**
- Create: `windows/src/VoxScribe.App/Design/ThemeModel.cs`
- Create: `windows/src/VoxScribe.App/Design/ThemeCatalog.cs`
- Replace: `windows/src/VoxScribe.App/Design/Themes.cs`
- Modify: `windows/src/VoxScribe.App/Design/DesignTokens.cs` (the `Colors`, `Brushes`, `Fonts` and `Radius` nested classes)
- Modify: `windows/src/VoxScribe.Core/AppSettings.cs` (the `AccentColor` and `Theme` properties)
- Modify: `windows/src/VoxScribe.App/Composition.cs`, `windows/src/VoxScribe.App/Views/HudWindow.cs`, `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs`
- Modify (rename only): every `.cs` under `windows/src/VoxScribe.App` that uses `MeterGreen`, `MeterAmber` or `MeterRed`
- Replace: `windows/tests/VoxScribe.App.Tests/Design/ContrastTests.cs`
- Modify: `windows/tests/VoxScribe.App.Tests/UiTests.cs`, `windows/tests/VoxScribe.App.Tests/AppearanceSectionTests.cs`
- Create: `windows/tests/VoxScribe.Core.Tests/SettingsMigrationTests.cs`

**Interfaces:**
- Consumes: `FontFaces.*` (Task 2).
- Produces. Later tasks rely on these exact names:
  - `internal sealed record Palette(uint Ground, uint Surface, uint SurfaceRaised, uint Border, uint Hover, uint Ink, uint InkMuted, uint AccentInk, uint PillFill, uint PillEdge, uint PillRule)`. The first eight are `0xRRGGBB`; the last three are `0xAARRGGBB`.
  - `internal sealed record AccentVariant(string Id, string Label, uint Light, uint Dark, uint? LightSecondary = null, uint? DarkSecondary = null)`
  - Enums:
    - `NavStyle { Sidebar, Rail, FloatingCard }`
    - `Selection { Hover, Tint, AccentFill, InkFill, Bar, Underline }`
    - `StatStyle { Ruled, Cards }`
    - `HeroStyle { Accent, AccentItalic, Fill, Tint }`
    - `ListStyle { Hairline, Zebra, Card, Dashed }`
    - `PillKind { Paper, Orb, Tide, Mono, Fluent }`
  - `internal sealed record ThemeDefinition`, with the init properties listed in Step 3.
  - `internal static class Themes`:
    - `const string DefaultId = "paper"`
    - `IReadOnlyList<ThemeDefinition> All`
    - `ThemeDefinition Active`, `AccentVariant ActiveVariant`, `bool IsDark`
    - `event EventHandler? Changed`
    - `ThemeDefinition Find(string? id)`
    - `bool Apply(string? themeId, string? variantId, bool dark)`, which returns false and raises nothing when the values are unchanged.
  - `Tokens.Colors` gains settable `AccentSecondary`, `AccentFill`, `OnAccent`, `AccentTint`, `PillFill`, `PillEdge`, `PillRule`, `Positive` and `Caution`. `Record` becomes `#E5484D`. `MeterGreen`, `MeterAmber`, `MeterRed` and `MeterFace` are removed. `Accent` becomes `internal set`.
  - `Tokens.Brushes` gains `Accent`, `AccentFill`, `OnAccent`, `InkSecondary`, `Seam` and `Hover`.
  - `Tokens.Fonts.Grotesque`, `Prose` and `Mono` become settable, and `Display` and `Row` (= 14) are added.
  - `Tokens.Radius.Chip` and `Panel` become settable properties, and `Pill` is added.
  - `SettingsData.Theme` defaults to `"paper"`. `SettingsData.AccentVariant` (`string?`) is new. `AccentColor` is gone.

- [ ] **Step 1: Write the failing Core migration test**

Create `windows/tests/VoxScribe.Core.Tests/SettingsMigrationTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>Settings files from before the five themes keep loading.</summary>
public sealed class SettingsMigrationTests
{
    [Fact]
    public void A_void_glass_era_settings_file_still_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "Theme": "deep-field", "AccentColor": "#4FD8E8", "KeepHistory": false }""");
        try
        {
            var data = new AppSettings(path).Data;

            data.KeepHistory.ShouldBeFalse("the rest of the file must survive the retired keys");
            data.Theme.ShouldBe("deep-field", "the raw id is kept; the app resolves unknown ids to Paper");
            data.AccentVariant.ShouldBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_fresh_install_is_paper_with_its_first_variant() =>
        new SettingsData().ShouldSatisfyAllConditions(
            d => d.Theme.ShouldBe("paper"),
            d => d.AccentVariant.ShouldBeNull());
}
```

Run `dotnet test tests/VoxScribe.Core.Tests --filter FullyQualifiedName~SettingsMigrationTests`. Expected: compile error, `AccentVariant` is not defined.

- [ ] **Step 2: Update `SettingsData` in `windows/src/VoxScribe.Core/AppSettings.cs`**

Delete the `AccentColor` property and its doc comment. Replace the `Theme` property and its doc comment with:

```csharp
    /// <summary>
    /// Visual theme id: "paper" (the default), "orb", "tide", "mono" or "fluent". Unknown
    /// values — including the retired "deep-field", "signal-house" and "manuscript" — fall back
    /// to Paper, so old or hand-edited files keep working.
    /// </summary>
    public string Theme { get; init; } = "paper";

    /// <summary>
    /// Accent variant id within the theme ("plum", "moss", …), or null for the theme's first.
    /// An id the theme does not offer falls back the same way.
    /// </summary>
    public string? AccentVariant { get; init; }
```

`System.Text.Json` skips unknown members by default, so an old `"AccentColor"` key is ignored. The Step 1 test proves it. Re-run that test: PASS.

- [ ] **Step 3: Create `windows/src/VoxScribe.App/Design/ThemeModel.cs`**

```csharp
using Avalonia.Media;

namespace VoxScribe.App.Design;

/// <summary>
/// One mode (light or dark) of a theme. The first eight are <c>0xRRGGBB</c>; the pill
/// colours carry alpha as <c>0xAARRGGBB</c>.
/// </summary>
internal sealed record Palette(
    uint Ground, uint Surface, uint SurfaceRaised, uint Border, uint Hover,
    uint Ink, uint InkMuted, uint AccentInk,
    uint PillFill, uint PillEdge, uint PillRule);

/// <summary>
/// A curated accent: one value for light mode and one for dark. Orb's gradients carry a
/// second stop; every other theme leaves it null and the primary is used.
/// </summary>
internal sealed record AccentVariant(
    string Id, string Label, uint Light, uint Dark, uint? LightSecondary = null, uint? DarkSecondary = null);

/// <summary>How the navigation is framed.</summary>
internal enum NavStyle
{
    /// <summary>A labelled column on the window ground, hairline on its right.</summary>
    Sidebar,

    /// <summary>A narrow icon rail with captions under the icons.</summary>
    Rail,

    /// <summary>A labelled column floating as a surface card.</summary>
    FloatingCard,
}

/// <summary>How a selected nav item or tab is marked.</summary>
internal enum Selection
{
    /// <summary>Hover fill, ink text.</summary>
    Hover,

    /// <summary>Accent tint fill, accent text.</summary>
    Tint,

    /// <summary>Accent fill, on-accent text.</summary>
    AccentFill,

    /// <summary>Ink fill, ground-coloured text.</summary>
    InkFill,

    /// <summary>Hover fill, ink text, accent bar on the leading edge.</summary>
    Bar,

    /// <summary>No fill, ink text, accent underline.</summary>
    Underline,
}

/// <summary>How the four Home stats are framed.</summary>
internal enum StatStyle
{
    /// <summary>Cells separated by hairlines, ruled top and bottom.</summary>
    Ruled,

    /// <summary>Separate surface cards.</summary>
    Cards,
}

/// <summary>How the hero stat (time saved) stands out.</summary>
internal enum HeroStyle
{
    /// <summary>Accent numerals.</summary>
    Accent,

    /// <summary>Accent italic numerals.</summary>
    AccentItalic,

    /// <summary>Accent-filled cell, on-accent numerals.</summary>
    Fill,

    /// <summary>Accent-tinted cell, accent numerals.</summary>
    Tint,
}

/// <summary>How the recent-dictation rows are framed.</summary>
internal enum ListStyle
{
    /// <summary>Hairline under each row.</summary>
    Hairline,

    /// <summary>Alternate rows on a surface fill.</summary>
    Zebra,

    /// <summary>All rows inside one surface card, hairlines between.</summary>
    Card,

    /// <summary>Dashed rule above each row, log style.</summary>
    Dashed,
}

/// <summary>Which pill silhouette the theme wears.</summary>
internal enum PillKind
{
    /// <summary>Tilted paper slip, pen-stroke level.</summary>
    Paper,

    /// <summary>Bodiless glowing orb, diameter = level.</summary>
    Orb,

    /// <summary>Soft capsule, liquid wave.</summary>
    Tide,

    /// <summary>Square status line, block meter.</summary>
    Mono,

    /// <summary>Win11 flyout, mic halo.</summary>
    Fluent,
}

/// <summary>
/// A theme is data: two palettes, its accents, its faces, and a few layout knobs the shared
/// skeleton reads. Nothing here is a view.
/// </summary>
internal sealed record ThemeDefinition
{
    /// <summary>Settings id.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Label { get; init; }

    /// <summary>One-line description for the Appearance picker.</summary>
    public required string Description { get; init; }

    /// <summary>Light-mode palette.</summary>
    public required Palette Light { get; init; }

    /// <summary>Dark-mode palette.</summary>
    public required Palette Dark { get; init; }

    /// <summary>Curated accents; the first is the default.</summary>
    public required IReadOnlyList<AccentVariant> Variants { get; init; }

    /// <summary>Headlines, wordmark.</summary>
    public required FontFamily Display { get; init; }

    /// <summary>Weight of headlines.</summary>
    public required FontWeight DisplayWeight { get; init; }

    /// <summary>Body and controls.</summary>
    public required FontFamily Body { get; init; }

    /// <summary>Times, keycaps, badges.</summary>
    public required FontFamily Mono { get; init; }

    /// <summary>Face of the Home stat numerals.</summary>
    public required FontFamily StatNumerals { get; init; }

    /// <summary>Weight of the Home stat numerals.</summary>
    public required FontWeight StatWeight { get; init; }

    /// <summary>Home headline size.</summary>
    public required double HeadlineSize { get; init; }

    /// <summary>Home stat numeral size.</summary>
    public required double StatSize { get; init; }

    /// <summary>Fixed Home headline, or null for a time-of-day greeting.</summary>
    public string? Headline { get; init; }

    /// <summary>Cards and wells.</summary>
    public required double CardRadius { get; init; }

    /// <summary>Buttons, chips, nav items.</summary>
    public required double ButtonRadius { get; init; }

    /// <summary>The pill body.</summary>
    public required double PillRadius { get; init; }

    /// <summary>Navigation frame.</summary>
    public required NavStyle Nav { get; init; }

    /// <summary>Navigation column width.</summary>
    public required double NavWidth { get; init; }

    /// <summary>Selected nav item marking.</summary>
    public required Selection NavSelection { get; init; }

    /// <summary>Selected settings tab marking.</summary>
    public required Selection TabSelection { get; init; }

    /// <summary>Home stats frame.</summary>
    public required StatStyle Stats { get; init; }

    /// <summary>Hero stat treatment.</summary>
    public required HeroStyle HeroStat { get; init; }

    /// <summary>Recent rows frame.</summary>
    public required ListStyle List { get; init; }

    /// <summary>Labels, nav, badges and footer set in capitals.</summary>
    public bool Uppercase { get; init; }

    /// <summary>The page area sits on a surface layer with a rounded top-left corner.</summary>
    public bool ContentOnSurface { get; init; }

    /// <summary>The Home header sits in a surface card with a large orb.</summary>
    public bool HeaderCard { get; init; }

    /// <summary>Accent fills use the dark-mode (neon) value in both modes.</summary>
    public bool AccentFillFromDark { get; init; }

    /// <summary>Wordmark, first run (ink). Empty hides the wordmark.</summary>
    public string WordmarkLead { get; init; } = string.Empty;

    /// <summary>Wordmark, accented run.</summary>
    public string WordmarkAccent { get; init; } = string.Empty;

    /// <summary>Wordmark, last run (ink).</summary>
    public string WordmarkTail { get; init; } = string.Empty;

    /// <summary>Wordmark size.</summary>
    public double WordmarkSize { get; init; }

    /// <summary>The accented wordmark run is italic.</summary>
    public bool WordmarkAccentItalic { get; init; }

    /// <summary>Pill silhouette.</summary>
    public required PillKind Pill { get; init; }
}
```

- [ ] **Step 4: Create `windows/src/VoxScribe.App/Design/ThemeCatalog.cs`**

Every hex value is copied from `.superpowers/mockups/<Theme>-System.dc.html` `renderVals()` and `<Theme>-Home.dc.html` / `Main.dc.html`. The exceptions are Orb aurora/emerald light (darkened for AA, see Global Constraints) and the pill colours (from `<Theme>-Pill.dc.html`).

```csharp
using Avalonia.Media;

namespace VoxScribe.App.Design;

/// <summary>The five themes, in picker order. The first is the default.</summary>
internal static class ThemeCatalog
{
    /// <summary>All themes.</summary>
    public static IReadOnlyList<ThemeDefinition> All { get; } = [Paper(), Orb(), Tide(), Mono(), Fluent()];

    private static ThemeDefinition Paper() => new()
    {
        Id = "paper", Label = "Paper", Description = "Warm editorial",
        Light = new(0xF4EFE6, 0xFBF8F2, 0xFFFFFF, 0xE3DBCD, 0xEAE2D3, 0x1E1A15, 0x6B6153, 0xFFFFFF,
            PillFill: 0xFFFBF8F2, PillEdge: 0xFFE3DBCD, PillRule: 0xFFD9CFBE),
        Dark = new(0x17130F, 0x211B16, 0x2A231C, 0x3A322A, 0x2C251E, 0xEFE7DA, 0xA89A86, 0x17130F,
            PillFill: 0xFF2A231C, PillEdge: 0xFF3A322A, PillRule: 0xFF4A4036),
        Variants =
        [
            new("plum", "Plum", 0x6A3D9A, 0xC3A4EC),
            new("ink-blue", "Ink blue", 0x2F4C80, 0x9DB6E8),
            new("moss", "Moss", 0x4D6A35, 0xA9C98C),
            new("ochre", "Ochre", 0x8A5A12, 0xE0B062),
        ],
        Display = FontFaces.InstrumentSerif, DisplayWeight = FontWeight.Normal,
        Body = FontFaces.Geist, Mono = FontFaces.GeistMono,
        StatNumerals = FontFaces.InstrumentSerif, StatWeight = FontWeight.Normal,
        HeadlineSize = 48, StatSize = 40, Headline = null,
        CardRadius = 12, ButtonRadius = 999, PillRadius = 3,
        Nav = NavStyle.Sidebar, NavWidth = 212, NavSelection = Selection.Hover, TabSelection = Selection.InkFill,
        Stats = StatStyle.Ruled, HeroStat = HeroStyle.AccentItalic, List = ListStyle.Hairline,
        WordmarkLead = "Vox", WordmarkAccent = "scribe", WordmarkSize = 30, WordmarkAccentItalic = true,
        Pill = PillKind.Paper,
    };

    private static ThemeDefinition Orb() => new()
    {
        Id = "orb", Label = "Orb", Description = "Luminous dark",
        Light = new(0xEEF0F5, 0xF8F9FC, 0xFFFFFF, 0xDADEE7, 0xE3E6EE, 0x10131A, 0x596070, 0xFFFFFF,
            PillFill: 0xE0FFFFFF, PillEdge: 0x1410131A, PillRule: 0xFFDADEE7),
        Dark = new(0x07080B, 0x0E1015, 0x151821, 0x22262F, 0x1A1E27, 0xE8EAF0, 0x8B91A0, 0x07080B,
            PillFill: 0xEB0E1015, PillEdge: 0x14FFFFFF, PillRule: 0xFF22262F),
        Variants =
        [
            new("nebula", "Nebula", 0x5B3FD6, 0x8C74FF, 0x0E8FA8, 0x5CE1F2),
            new("aurora", "Aurora", 0x0D7C5F, 0x3FE0B0, 0x2F6FD6, 0x7FA8FF),
            new("emerald", "Emerald", 0x127E3E, 0x3DDC84, 0x4E7F12, 0xB8F25C),
            new("ion", "Ion", 0x2350D8, 0x6F92FF, 0x6A36D1, 0xB58CFF),
            new("solar", "Solar", 0xA35A00, 0xFF9F3D, 0x8A6D00, 0xFFE07A),
        ],
        Display = FontFaces.Geist, DisplayWeight = FontWeight.SemiBold,
        Body = FontFaces.Geist, Mono = FontFaces.GeistMono,
        StatNumerals = FontFaces.GeistMono, StatWeight = FontWeight.Medium,
        HeadlineSize = 34, StatSize = 30, Headline = "Ready when you are.",
        CardRadius = 14, ButtonRadius = 10, PillRadius = 999,
        Nav = NavStyle.Rail, NavWidth = 84, NavSelection = Selection.Hover, TabSelection = Selection.Hover,
        Stats = StatStyle.Cards, HeroStat = HeroStyle.Accent, List = ListStyle.Zebra,
        HeaderCard = true,
        Pill = PillKind.Orb,
    };

    private static ThemeDefinition Tide() => new()
    {
        Id = "tide", Label = "Tide", Description = "Soft and airy",
        Light = new(0xEAF4F8, 0xFFFFFF, 0xFFFFFF, 0xD3E5EE, 0xDCEDF5, 0x0D2433, 0x4D6878, 0xFFFFFF,
            PillFill: 0xFFFFFFFF, PillEdge: 0x00000000, PillRule: 0xFFD3E5EE),
        Dark = new(0x0A1822, 0x122733, 0x16303F, 0x21404F, 0x1A3645, 0xE2F1F8, 0x8FB0C2, 0x0A1822,
            PillFill: 0xFF16303F, PillEdge: 0x00000000, PillRule: 0xFF21404F),
        Variants =
        [
            new("lagoon", "Lagoon", 0x0B6FA0, 0x6CCDF5),
            new("lilac", "Lilac", 0x5F4BC0, 0xB9A9FF),
            new("mint", "Mint", 0x0B7360, 0x6FE3C2),
            new("fern", "Fern", 0x2E7A2E, 0x8EDB7A),
            new("peach", "Peach", 0x9A4E14, 0xFFB27A),
        ],
        Display = FontFaces.Figtree, DisplayWeight = FontWeight.ExtraBold,
        Body = FontFaces.Figtree, Mono = FontFaces.GeistMono,
        StatNumerals = FontFaces.Figtree, StatWeight = FontWeight.ExtraBold,
        HeadlineSize = 36, StatSize = 32, Headline = null,
        CardRadius = 20, ButtonRadius = 999, PillRadius = 999,
        Nav = NavStyle.FloatingCard, NavWidth = 196, NavSelection = Selection.Tint, TabSelection = Selection.AccentFill,
        Stats = StatStyle.Cards, HeroStat = HeroStyle.Tint, List = ListStyle.Card,
        WordmarkLead = "VoxScribe", WordmarkSize = 19,
        Pill = PillKind.Tide,
    };

    private static ThemeDefinition Mono() => new()
    {
        Id = "mono", Label = "Mono", Description = "Terminal brutalist",
        Light = new(0xF2F2EE, 0xFAFAF7, 0xFFFFFF, 0xCFCFC8, 0xE7E7E2, 0x0B0B0B, 0x5C5C58, 0x0B0B0B,
            PillFill: 0xFFFAFAF7, PillEdge: 0xFF0B0B0B, PillRule: 0xFFCFCFC8),
        Dark = new(0x0B0B0B, 0x111111, 0x161616, 0x232323, 0x1C1C1C, 0xF2F2EE, 0x8A8A85, 0x0B0B0B,
            PillFill: 0xFF0B0B0B, PillEdge: 0xFF3A3A3A, PillRule: 0xFF232323),
        Variants =
        [
            new("lime", "Lime", 0x3F6B00, 0xC6FF3D),
            new("phosphor", "Phosphor", 0x0A7A3B, 0x39FF88),
            new("cyan", "Cyan", 0x006B7A, 0x3DF0FF),
            new("violet", "Violet", 0x5A2DB8, 0xB69CFF),
            new("amber", "Amber", 0x8A5200, 0xFFB13D),
        ],
        Display = FontFaces.JetBrainsMono, DisplayWeight = FontWeight.ExtraBold,
        Body = FontFaces.JetBrainsMono, Mono = FontFaces.JetBrainsMono,
        StatNumerals = FontFaces.JetBrainsMono, StatWeight = FontWeight.ExtraBold,
        HeadlineSize = 30, StatSize = 32, Headline = "> ready_",
        CardRadius = 0, ButtonRadius = 0, PillRadius = 0,
        Nav = NavStyle.Sidebar, NavWidth = 200, NavSelection = Selection.AccentFill, TabSelection = Selection.InkFill,
        Stats = StatStyle.Ruled, HeroStat = HeroStyle.Fill, List = ListStyle.Dashed,
        Uppercase = true, AccentFillFromDark = true,
        WordmarkLead = "VOX", WordmarkAccent = "/", WordmarkTail = "SCRIBE", WordmarkSize = 15,
        Pill = PillKind.Mono,
    };

    private static ThemeDefinition Fluent() => new()
    {
        Id = "fluent", Label = "Fluent", Description = "Native Windows 11",
        Light = new(0xEEF0F3, 0xF9F9F9, 0xFFFFFF, 0xE5E5E5, 0xEAEAEA, 0x1B1B1B, 0x5F5F5F, 0xFFFFFF,
            PillFill: 0xFFF9F9F9, PillEdge: 0x0F000000, PillRule: 0xFFE5E5E5),
        Dark = new(0x1F2125, 0x272727, 0x2D2D2D, 0x1C1C1C, 0x2D2D2D, 0xFFFFFF, 0xC5C5C5, 0x000000,
            PillFill: 0xFF2C2C2C, PillEdge: 0x14FFFFFF, PillRule: 0xFF1C1C1C),
        Variants =
        [
            new("system", "Windows blue", 0x005FB8, 0x60CDFF),
            new("teal", "Teal", 0x00777A, 0x4CD9D9),
            new("orchid", "Orchid", 0x8240B3, 0xD3A6FF),
            new("forest", "Forest", 0x107C10, 0x6CCB5F),
        ],
        Display = FontFaces.SegoeDisplay, DisplayWeight = FontWeight.SemiBold,
        Body = FontFaces.SegoeText, Mono = FontFaces.CascadiaMono,
        StatNumerals = FontFaces.SegoeDisplay, StatWeight = FontWeight.SemiBold,
        HeadlineSize = 28, StatSize = 28, Headline = "Home",
        CardRadius = 8, ButtonRadius = 4, PillRadius = 8,
        Nav = NavStyle.Sidebar, NavWidth = 220, NavSelection = Selection.Bar, TabSelection = Selection.Underline,
        Stats = StatStyle.Cards, HeroStat = HeroStyle.Accent, List = ListStyle.Card,
        ContentOnSurface = true,
        Pill = PillKind.Fluent,
    };
}
```

- [ ] **Step 5: Replace `windows/src/VoxScribe.App/Design/Themes.cs` entirely**

```csharp
using Avalonia.Media;

namespace VoxScribe.App.Design;

/// <summary>
/// The active theme. <see cref="Apply"/> resolves a theme, a variant and the light/dark mode
/// into <see cref="Tokens"/> and raises <see cref="Changed"/>; windows rebuild on that event.
/// </summary>
/// <remarks>
/// A pure function of its three arguments: the app passes Windows' mode in, tests pass
/// <c>dark: true</c> directly. Red is not a theme value — <see cref="Tokens.Colors.Record"/>
/// is the same in every theme and nothing here can touch it.
/// </remarks>
internal static class Themes
{
    /// <summary>Settings id of the default theme.</summary>
    public const string DefaultId = "paper";

    private const byte TintAlphaLight = 0x1A;
    private const byte TintAlphaDark = 0x1F;
    private const uint PositiveLight = 0x107C10;
    private const uint PositiveDark = 0x6CCB5F;
    private const uint CautionLight = 0x8A5200;
    private const uint CautionDark = 0xFFB13D;

    private static bool _applied;

    /// <summary>The selectable themes, in picker order.</summary>
    public static IReadOnlyList<ThemeDefinition> All => ThemeCatalog.All;

    /// <summary>The theme currently painted.</summary>
    public static ThemeDefinition Active { get; private set; } = ThemeCatalog.All[0];

    /// <summary>The accent variant currently painted.</summary>
    public static AccentVariant ActiveVariant { get; private set; } = ThemeCatalog.All[0].Variants[0];

    /// <summary>Whether the dark palette is painted.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Raised after <see cref="Apply"/> changed anything. May fire on any thread that calls Apply.</summary>
    public static event EventHandler? Changed;

    /// <summary>The theme with <paramref name="id"/>, or the default for an unknown or retired id.</summary>
    public static ThemeDefinition Find(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>
    /// Paints <paramref name="themeId"/> / <paramref name="variantId"/> in the light or dark
    /// palette. Returns false, and raises nothing, when that is already what is painted.
    /// </summary>
    public static bool Apply(string? themeId, string? variantId, bool dark)
    {
        var theme = Find(themeId);
        var variant = theme.Variants.FirstOrDefault(
            v => string.Equals(v.Id, variantId, StringComparison.OrdinalIgnoreCase)) ?? theme.Variants[0];

        if (_applied && ReferenceEquals(theme, Active) && ReferenceEquals(variant, ActiveVariant) && dark == IsDark)
        {
            return false;
        }

        _applied = true;
        Active = theme;
        ActiveVariant = variant;
        IsDark = dark;

        var p = dark ? theme.Dark : theme.Light;

        Tokens.Colors.Chassis = Tokens.Colors.Rgb(p.Ground);
        Tokens.Colors.Panel = Tokens.Colors.Rgb(p.Surface);
        Tokens.Colors.Deck = Tokens.Colors.Rgb(p.Surface);
        Tokens.Colors.Cap = Tokens.Colors.Rgb(p.SurfaceRaised);
        Tokens.Colors.Seam = Tokens.Colors.Rgb(p.Border);
        Tokens.Colors.Hover = Tokens.Colors.Rgb(p.Hover);
        Tokens.Colors.Ink = Tokens.Colors.Rgb(p.Ink);
        Tokens.Colors.InkOnDeck = Tokens.Colors.Rgb(p.Ink);
        Tokens.Colors.InkSecondary = Tokens.Colors.Rgb(p.InkMuted);
        Tokens.Colors.Silkscreen = Tokens.Colors.Rgb(p.InkMuted);
        Tokens.Colors.OnAccent = Tokens.Colors.Rgb(p.AccentInk);
        Tokens.Colors.PillFill = Tokens.Colors.Argb(p.PillFill);
        Tokens.Colors.PillEdge = Tokens.Colors.Argb(p.PillEdge);
        Tokens.Colors.PillRule = Tokens.Colors.Argb(p.PillRule);
        Tokens.Colors.Glass = Tokens.Colors.PillFill;
        Tokens.Colors.RecordIdle = Tokens.Colors.Hover;

        var accent = Tokens.Colors.Rgb(dark ? variant.Dark : variant.Light);
        Tokens.Colors.Accent = accent;
        Tokens.Colors.AccentSecondary = Tokens.Colors.Rgb(
            (dark ? variant.DarkSecondary : variant.LightSecondary) ?? (dark ? variant.Dark : variant.Light));
        Tokens.Colors.AccentFill = theme.AccentFillFromDark ? Tokens.Colors.Rgb(variant.Dark) : accent;
        Tokens.Colors.AccentTint = Color.FromArgb(dark ? TintAlphaDark : TintAlphaLight, accent.R, accent.G, accent.B);
        Tokens.Colors.Positive = Tokens.Colors.Rgb(dark ? PositiveDark : PositiveLight);
        Tokens.Colors.Caution = Tokens.Colors.Rgb(dark ? CautionDark : CautionLight);

        Tokens.Radius.Chip = theme.ButtonRadius;
        Tokens.Radius.Panel = theme.CardRadius;
        Tokens.Radius.Pill = theme.PillRadius;

        Tokens.Fonts.Grotesque = theme.Body;
        Tokens.Fonts.Prose = theme.Body;
        Tokens.Fonts.Mono = theme.Mono;
        Tokens.Fonts.Display = theme.Display;

        Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }
}
```

- [ ] **Step 6: Update `windows/src/VoxScribe.App/Design/DesignTokens.cs`**

Replace the whole nested `Colors` class with the block below. Its defaults equal Paper light, so code that never calls `Apply` (headless tests) paints a valid theme.

```csharp
    /// <summary>
    /// Surfaces, inks and accents. Settable values belong to the active theme and are written by
    /// <see cref="Themes.Apply"/>; defaults equal Paper light. <see cref="Record"/> is law.
    /// </summary>
    public static class Colors
    {
        /// <summary>The window ground (theme <c>ground</c>).</summary>
        public static Color Chassis { get; internal set; } = Rgb(0xF4EFE6);

        /// <summary>A card on the ground (theme <c>surface</c>).</summary>
        public static Color Panel { get; internal set; } = Rgb(0xFBF8F2);

        /// <summary>Lists and inputs (theme <c>surface</c>).</summary>
        public static Color Deck { get; internal set; } = Rgb(0xFBF8F2);

        /// <summary>Buttons and raised chips (theme <c>surfaceRaised</c>).</summary>
        public static Color Cap { get; internal set; } = Rgb(0xFFFFFF);

        /// <summary>Hairline border (theme <c>border</c>).</summary>
        public static Color Seam { get; internal set; } = Rgb(0xE3DBCD);

        /// <summary>Row under the pointer, selected nav (theme <c>hover</c>).</summary>
        public static Color Hover { get; internal set; } = Rgb(0xEAE2D3);

        /// <summary>Primary text (theme <c>ink</c>).</summary>
        public static Color Ink { get; internal set; } = Rgb(0x1E1A15);

        /// <summary>Supporting text (theme <c>inkMuted</c>).</summary>
        public static Color InkSecondary { get; internal set; } = Rgb(0x6B6153);

        /// <summary>Section labels (theme <c>inkMuted</c>).</summary>
        public static Color Silkscreen { get; internal set; } = Rgb(0x6B6153);

        /// <summary>Text on lists and inputs (theme <c>ink</c>).</summary>
        public static Color InkOnDeck { get; internal set; } = Rgb(0x1E1A15);

        /// <summary>
        /// The recording dot. The only red in the app, identical in every theme, and nothing
        /// else may use it.
        /// </summary>
        public static Color Record => Rgb(0xE5484D);

        /// <summary>Accent for text and strokes on the ground (variant, by mode).</summary>
        public static Color Accent { get; internal set; } = Rgb(0x6A3D9A);

        /// <summary>Second gradient stop (Orb); equals <see cref="Accent"/> elsewhere.</summary>
        public static Color AccentSecondary { get; internal set; } = Rgb(0x6A3D9A);

        /// <summary>Accent used as a fill behind <see cref="OnAccent"/> text.</summary>
        public static Color AccentFill { get; internal set; } = Rgb(0x6A3D9A);

        /// <summary>Text and glyphs on <see cref="AccentFill"/> (theme <c>accentInk</c>).</summary>
        public static Color OnAccent { get; internal set; } = Rgb(0xFFFFFF);

        /// <summary>The accent at ~10% alpha, for tinted fills.</summary>
        public static Color AccentTint { get; internal set; } = Color.FromArgb(0x1A, 0x6A, 0x3D, 0x9A);

        /// <summary>The dictation pill's body (may carry alpha).</summary>
        public static Color PillFill { get; internal set; } = Argb(0xFFFBF8F2);

        /// <summary>The pill's edge (may carry alpha; transparent for Tide).</summary>
        public static Color PillEdge { get; internal set; } = Argb(0xFFE3DBCD);

        /// <summary>Rules and unlit cells inside the pill.</summary>
        public static Color PillRule { get; internal set; } = Argb(0xFFD9CFBE);

        /// <summary>A good outcome (connection OK, model found). Never red.</summary>
        public static Color Positive { get; internal set; } = Rgb(0x107C10);

        /// <summary>A warning or a failure. Never red: red means recording.</summary>
        public static Color Caution { get; internal set; } = Rgb(0x8A5200);

        /// <summary>Old pill body; removed with the old pill in Task 9.</summary>
        public static Color Glass { get; internal set; } = Argb(0xFFFBF8F2);

        /// <summary>Old pill idle lamp; removed with the old pill in Task 9.</summary>
        public static Color RecordIdle { get; internal set; } = Rgb(0xEAE2D3);

        /// <summary>Lens highlights. Always used with an opacity.</summary>
        public static Color Specular { get; internal set; } = Avalonia.Media.Colors.White;

        /// <summary>WCAG 2.1 contrast ratio, 1:1 to 21:1. AA text needs 4.5:1, large text 3:1.</summary>
        public static double GetContrastRatio(Color foreground, Color background)
        {
            var l1 = GetRelativeLuminance(foreground);
            var l2 = GetRelativeLuminance(background);
            return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        }

        private static double GetRelativeLuminance(Color c)
        {
            static double Channel(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        }

        internal static Color Rgb(uint hex) => Color.FromRgb(
            (byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));

        internal static Color Argb(uint hex) => Color.FromUInt32(hex);
    }
```

Replace the whole nested `Brushes` class with:

```csharp
    /// <summary>Brushes for the colours above, allocated per call so none outlives a theme change.</summary>
    public static class Brushes
    {
        /// <inheritdoc cref="Colors.Chassis"/>
        public static IBrush Chassis => new SolidColorBrush(Colors.Chassis);

        /// <inheritdoc cref="Colors.Panel"/>
        public static IBrush Panel => new SolidColorBrush(Colors.Panel);

        /// <inheritdoc cref="Colors.Deck"/>
        public static IBrush Deck => new SolidColorBrush(Colors.Deck);

        /// <inheritdoc cref="Colors.Seam"/>
        public static IBrush Seam => new SolidColorBrush(Colors.Seam);

        /// <inheritdoc cref="Colors.Hover"/>
        public static IBrush Hover => new SolidColorBrush(Colors.Hover);

        /// <inheritdoc cref="Colors.Ink"/>
        public static IBrush Ink => new SolidColorBrush(Colors.Ink);

        /// <inheritdoc cref="Colors.InkSecondary"/>
        public static IBrush InkSecondary => new SolidColorBrush(Colors.InkSecondary);

        /// <inheritdoc cref="Colors.Silkscreen"/>
        public static IBrush Silkscreen => new SolidColorBrush(Colors.Silkscreen);

        /// <inheritdoc cref="Colors.InkOnDeck"/>
        public static IBrush InkOnDeck => new SolidColorBrush(Colors.InkOnDeck);

        /// <inheritdoc cref="Colors.Accent"/>
        public static IBrush Accent => new SolidColorBrush(Colors.Accent);

        /// <inheritdoc cref="Colors.AccentFill"/>
        public static IBrush AccentFill => new SolidColorBrush(Colors.AccentFill);

        /// <inheritdoc cref="Colors.OnAccent"/>
        public static IBrush OnAccent => new SolidColorBrush(Colors.OnAccent);

        /// <inheritdoc cref="Colors.Record"/>
        public static IBrush Record => new SolidColorBrush(Colors.Record);

        /// <summary>Ink at a chosen level of de-emphasis. See <see cref="Emphasis"/>.</summary>
        public static IBrush InkOnDeckAt(double emphasis) => new SolidColorBrush(Colors.InkOnDeck, emphasis);
    }
```

In the nested `Fonts` class:
- Replace the `Grotesque`, `Prose` and `Mono` properties with the block below.
- Delete the old Segoe/Cascadia remarks.
- Add `Row` after `Body`.

```csharp
        /// <summary>The active theme's body face. (The name is historical; it is not always a grotesque.)</summary>
        public static FontFamily Grotesque { get; internal set; } = FontFaces.Geist;

        /// <summary>The active theme's display face: headlines, wordmark.</summary>
        public static FontFamily Display { get; internal set; } = FontFaces.InstrumentSerif;

        /// <summary>The spoken word — transcript rows. Follows the theme's body face.</summary>
        public static FontFamily Prose { get; internal set; } = FontFaces.Geist;

        /// <summary>Readouts and timings. Monospaced so digits don't shift as they tick.</summary>
        public static FontFamily Mono { get; internal set; } = FontFaces.GeistMono;
```

```csharp
        /// <summary>Row text, nav labels.</summary>
        public const double Row = 14;
```

Replace the whole nested `Radius` class with:

```csharp
    /// <summary>Corner radii. Chip and Panel follow the theme (Mono is square, Paper is round).</summary>
    public static class Radius
    {
        /// <summary>Buttons, chips, badges, nav items (theme <c>radius.button</c>).</summary>
        public static double Chip { get; internal set; } = 999;

        /// <summary>Cards and wells (theme <c>radius.card</c>).</summary>
        public static double Panel { get; internal set; } = 12;

        /// <summary>The pill body (theme <c>radius.pill</c>).</summary>
        public static double Pill { get; internal set; } = 3;

        /// <summary>Navigation rail keys (removed with the rail in Task 6).</summary>
        public const double RailKey = 12;
    }
```

Delete the class-level remark "One rule that is not negotiable: red means recording. Nothing else is red." and replace it with `<para>Red is used only for the recording dot. Nothing else is red.</para>`. Also delete `Material.NoticeEdgeOpacity`'s mention of "instrumentation colour" in its summary; change it to "How strongly a caution colour tints the outline of a notice."

- [ ] **Step 7: Rename the instrumentation colours at their call sites**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes/windows/src/VoxScribe.App
grep -rl "MeterGreen\|MeterAmber\|MeterRed" --include=*.cs . | xargs sed -i 's/Tokens\.Colors\.MeterGreen/Tokens.Colors.Positive/g; s/Tokens\.Colors\.MeterAmber/Tokens.Colors.Caution/g; s/Tokens\.Colors\.MeterRed/Tokens.Colors.Caution/g'
grep -rn "Meter\(Green\|Amber\|Red\|Face\)" --include=*.cs . ../../tests
```

Expected: no output. `ConnectionTester`'s failure lamp is now `Caution`, not red.

- [ ] **Step 8: Wire the theme at startup in `windows/src/VoxScribe.App/Composition.cs`**

Delete the `ApplyAccent` method. In `Create()`, replace the six lines from `// Theme first, before any window exists` through `settings.Changed += (_, _) => ApplyAccent(settings.Data.AccentColor);` with:

```csharp
        // Theme first, before any window exists. Task 4 moves this into App and makes it live.
        Themes.Apply(settings.Data.Theme, settings.Data.AccentVariant,
            Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
```

Remove `using Avalonia.Media;` if nothing else in the file uses it.

- [ ] **Step 9: Point the old pill at the new radius token**

In `windows/src/VoxScribe.App/Views/HudWindow.cs`, replace `new CornerRadius(Themes.PillRadius)` with `new CornerRadius(Tokens.Radius.Pill)`.

- [ ] **Step 10: Replace `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs` with an interim version**

This version shows the theme keys only. Task 4 removes the restart, Task 8 builds the real picker. The background `Task.Run` polling loop is gone: it touched controls off the UI thread. The busy check now happens at click time.

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>The theme keys.</summary>
internal static class AppearanceSection
{
    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save, DictationEngine? engine = null) =>
        Panels.Section("APPEARANCE", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                BuildThemeRow(settings, save, engine),
                Panels.Note("Theme — pick one, then APPLY restarts Vox-Scribe with it."),
            },
        });

    // No engine (platform layer absent) means nothing can be recording.
    private static bool IsBusy(DictationEngine? engine) => engine is { State: not DictationState.Idle };

    private static StackPanel BuildThemeRow(AppSettings settings, Action<SettingsData> save, DictationEngine? engine)
    {
        var keys = new List<(string Id, Button Key)>();

        var apply = Panels.DeckButton("APPLY — RESTARTS VOX-SCRIBE");
        Avalonia.Automation.AutomationProperties.SetName(apply, "Apply theme changes");
        apply.Click += (_, _) =>
        {
            if (IsBusy(engine)) return;
            (Application.Current as App)?.Restart();
        };

        void SyncApply() => apply.IsVisible = Themes.Find(settings.Data.Theme).Id != Themes.Active.Id;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Snug };
        foreach (var theme in Themes.All)
        {
            var key = Panels.DeckButton(theme.Label.ToUpperInvariant());
            Avalonia.Automation.AutomationProperties.SetName(key, $"Theme: {theme.Label}");
            key.Click += (_, _) =>
            {
                if (IsBusy(engine)) return;
                save(settings.Data with { Theme = theme.Id, AccentVariant = null });
                Mark(settings, keys);
                SyncApply();
            };
            keys.Add((theme.Id, key));
            row.Children.Add(key);
        }

        row.Children.Add(apply);
        Mark(settings, keys);
        SyncApply();
        return row;
    }

    private static void Mark(AppSettings settings, List<(string Id, Button Key)> keys)
    {
        var saved = Themes.Find(settings.Data.Theme).Id;
        foreach (var (id, key) in keys)
        {
            var selected = id == saved;
            key.Foreground = selected ? Tokens.Brushes.Ink : Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Muted);
            key.BorderBrush = selected ? Tokens.Brushes.Ink : Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Outline);
        }
    }
}
```

- [ ] **Step 11: Replace `windows/tests/VoxScribe.App.Tests/Design/ContrastTests.cs` entirely**

```csharp
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Shouldly;
using VoxScribe.App.Design;

namespace VoxScribe.App.Tests.Design;

/// <summary>
/// WCAG AA for every text/ground pair the views actually draw, across all five themes, both
/// modes and every accent variant. A failure lists every offending pair at once.
/// </summary>
public sealed class ContrastTests
{
    private const double Text = 4.5;
    private const double Large = 3.0;

    [AvaloniaFact]
    public void Every_theme_mode_and_variant_meets_WCAG_AA()
    {
        var failures = new List<string>();
        try
        {
            foreach (var theme in Themes.All)
            foreach (var variant in theme.Variants)
            foreach (var dark in new[] { false, true })
            {
                Themes.Apply(theme.Id, variant.Id, dark);
                var where = $"{theme.Id}/{variant.Id}/{(dark ? "dark" : "light")}";
                var c = new
                {
                    Tokens.Colors.Chassis, Tokens.Colors.Panel, Tokens.Colors.Cap, Tokens.Colors.Hover,
                    Tokens.Colors.Ink, Tokens.Colors.InkSecondary, Tokens.Colors.Accent,
                    Tokens.Colors.AccentFill, Tokens.Colors.OnAccent, Tokens.Colors.AccentTint,
                    Pill = Opaque(Tokens.Colors.PillFill), Tokens.Colors.Positive, Tokens.Colors.Caution,
                };

                void Check(string pair, Color fg, Color bg, double min)
                {
                    var ratio = Tokens.Colors.GetContrastRatio(fg, bg);
                    if (ratio < min) failures.Add($"{where}: {pair} {ratio:F2}:1 < {min}:1");
                }

                foreach (var (name, ground) in new[] { ("ground", c.Chassis), ("surface", c.Panel), ("raised", c.Cap), ("hover", c.Hover) })
                {
                    Check($"ink/{name}", c.Ink, ground, Text);
                    Check($"muted/{name}", c.InkSecondary, ground, Text);
                }

                Check("accent/ground", c.Accent, c.Chassis, Text);
                Check("accent/surface", c.Accent, c.Panel, Text);
                Check("onAccent/accentFill", c.OnAccent, c.AccentFill, Text);
                Check("ink/pill", c.Ink, c.Pill, Text);
                Check("muted/pill", c.InkSecondary, c.Pill, Text);
                Check("accent/pill", c.Accent, c.Pill, Text);
                Check("positive/ground", c.Positive, c.Chassis, Text);
                Check("positive/surface", c.Positive, c.Panel, Text);
                Check("caution/ground", c.Caution, c.Chassis, Text);
                Check("caution/surface", c.Caution, c.Panel, Text);
                Check("record/ground", Tokens.Colors.Record, c.Chassis, Large);

                if (theme.NavSelection == Selection.Tint)
                {
                    Check("accent/tint-over-surface", c.Accent, Over(c.AccentTint, c.Panel), Text);
                }

                if (theme.HeroStat == HeroStyle.Tint)
                {
                    Check("accent/tint-over-ground (hero, large)", c.Accent, Over(c.AccentTint, c.Chassis), Large);
                }
            }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
    }

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);

    private static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round((t * a) + (b * (1 - a)));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }
}
```

- [ ] **Step 12: Update `windows/tests/VoxScribe.App.Tests/UiTests.cs` `DesignSystemTests`**

Replace `Record_red_is_the_void_glass_value`, `Radii_stay_generous_enough_to_read_as_void_glass`, `Every_theme_keeps_the_record_red_and_stays_readable` and `Every_theme_builds_its_own_window_layout` (and the `Luminance` helper) with:

```csharp
    [AvaloniaFact]
    public void Record_red_is_E5484D()
    {
        var red = Tokens.Colors.Record;
        (red.R, red.G, red.B).ShouldBe(((byte)0xE5, (byte)0x48, (byte)0x4D));
    }

    [AvaloniaFact]
    public void Token_defaults_are_paper_light()
    {
        // Headless tests never call Apply; the defaults must be a real theme, not a stale one.
        var defaults = (Tokens.Colors.Chassis, Tokens.Colors.Ink, Tokens.Colors.Accent, Tokens.Colors.PillFill);
        try
        {
            Themes.Apply("orb", null, true);
            Themes.Apply("paper", null, false);
            (Tokens.Colors.Chassis, Tokens.Colors.Ink, Tokens.Colors.Accent, Tokens.Colors.PillFill).ShouldBe(defaults);
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }

    [AvaloniaFact]
    public void Five_themes_in_order_each_with_a_green_variant()
    {
        Themes.All.Select(t => t.Id).ShouldBe(["paper", "orb", "tide", "mono", "fluent"]);
        var greens = new Dictionary<string, string>
        {
            ["paper"] = "moss", ["orb"] = "emerald", ["tide"] = "fern", ["mono"] = "phosphor", ["fluent"] = "forest",
        };
        foreach (var theme in Themes.All) theme.Variants.Select(v => v.Id).ShouldContain(greens[theme.Id]);
    }

    [AvaloniaFact]
    public void Retired_or_unknown_ids_fall_back_to_paper_and_its_first_variant()
    {
        try
        {
            foreach (var id in new string?[] { "deep-field", "signal-house", "manuscript", "no-such-theme", null })
            {
                Themes.Apply(id, "#4FD8E8", false);
                Themes.Active.Id.ShouldBe("paper");
                Themes.ActiveVariant.Id.ShouldBe("plum");
            }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }

    [AvaloniaFact]
    public void Applying_what_is_already_painted_changes_nothing()
    {
        var raised = 0;
        void Count(object? s, EventArgs e) => raised++;
        Themes.Changed += Count;
        try
        {
            Themes.Apply("tide", "mint", true);
            raised = 0;
            Themes.Apply("tide", "mint", true).ShouldBeFalse();
            raised.ShouldBe(0);
            Themes.Apply("tide", "mint", false).ShouldBeTrue();
            raised.ShouldBe(1);
        }
        finally
        {
            Themes.Changed -= Count;
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }

    [AvaloniaFact]
    public void Every_theme_builds_the_main_window_light_and_dark()
    {
        try
        {
            foreach (var theme in Themes.All)
            foreach (var dark in new[] { false, true })
            {
                Themes.Apply(theme.Id, null, dark);
                Tokens.Colors.Record.ShouldBe(Avalonia.Media.Color.FromRgb(0xE5, 0x48, 0x4D), theme.Id);
                var window = new MainWindow();
                window.Show();
                window.Bounds.Width.ShouldBeGreaterThan(0, theme.Id);
            }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }
```

In `windows/tests/VoxScribe.App.Tests/AppearanceSectionTests.cs`, change `var other = Themes.Choices.First(c => c.Id != settings.Data.Theme);` to `var other = Themes.All.First(t => t.Id != settings.Data.Theme);`. The rest of that test is unchanged.

- [ ] **Step 13: Run the Global Constraints verification**

Expected: all green.
- If `ContrastTests` fails, its message lists every pair. Do **not** change a hex value to make it pass without recording the change in the task report. The values above were pre-checked to pass.
- Fix any `Themes.Choices` / `Themes.ActiveId` / `Themes.Default` / `Themes.PillRadius` leftovers the compiler reports: `grep -rn "Themes\.\(Choices\|ActiveId\|Default\b\|PillRadius\)" --include=*.cs windows`.

- [ ] **Step 14: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
feat: five themes (Paper, Orb, Tide, Mono, Fluent) with light/dark palettes and variants

Themes are data; Apply resolves theme, variant and mode into Tokens. Paper is
the default and every retired id falls back to it. Record red is E5484D and
nothing else is red; the free-form accent setting is gone. A contrast audit
covers every theme, mode and variant.
EOF
```

---
### Task 4: Live theme, variant and Windows light/dark switching

**Files:**
- Modify: `windows/src/VoxScribe.App/App.axaml.cs`
- Modify: `windows/src/VoxScribe.App/Composition.cs`
- Modify: `windows/src/VoxScribe.App/Views/MainWindow.cs`
- Modify: `windows/src/VoxScribe.App/Views/HudWindow.cs`
- Modify: `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs`
- Modify: `windows/src/VoxScribe.App/Controls/Lamp.cs`, `TransportKey.cs`, `BrushedPanel.cs`
- Test: `windows/tests/VoxScribe.App.Tests/LiveThemeTests.cs` (create)

**Interfaces:**
- Consumes: `Themes.Apply(string?, string?, bool)`, `Themes.Changed`, `SettingsData.Theme`, `SettingsData.AccentVariant` (Task 3).
- Produces:
  - `App` re-applies the theme on `ActualThemeVariantChanged` and on `AppSettings.Changed`.
  - `MainWindow` rebuilds its content on `Themes.Changed`, posted to the UI thread, and unsubscribes in `OnClosed`.
  - `App.Restart()` is deleted.
  - `Lamp`, `TransportKey` and `BrushedPanel` read their theme-dependent defaults in their constructors, not at static init.

- [ ] **Step 1: Write the failing tests**

Create `windows/tests/VoxScribe.App.Tests/LiveThemeTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Shouldly;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.App.Views;

namespace VoxScribe.AppTests;

/// <summary>Theme, variant and light/dark changes repaint without a restart.</summary>
public sealed class LiveThemeTests
{
    [AvaloniaFact]
    public void A_theme_change_rebuilds_the_main_window()
    {
        var window = new MainWindow();
        window.Show();
        var before = window.Content;
        try
        {
            Themes.Apply("mono", null, dark: true);
            Dispatcher.UIThread.RunJobs();

            window.Content.ShouldNotBeSameAs(before);
            window.Background.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Color.FromRgb(0x0B, 0x0B, 0x0B));
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
            Dispatcher.UIThread.RunJobs();
            window.ExitAllowed = true;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Shared_controls_take_the_theme_painted_when_they_are_built()
    {
        try
        {
            Themes.Apply("mono", null, dark: true);
            new Lamp().LampColor.ShouldBe(Tokens.Colors.Silkscreen);
            new TransportKey().EngagedColor.ShouldBe(Tokens.Colors.Ink);
            new BrushedPanel().CornerRadius.ShouldBe(0);
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }
}
```

Run `dotnet test tests/VoxScribe.App.Tests --filter FullyQualifiedName~LiveThemeTests`.

Expected: both tests FAIL. The window content is unchanged, and `Lamp` still carries Paper's colour from static init.

- [ ] **Step 2: Read theme-dependent defaults at construction**
- `windows/src/VoxScribe.App/Controls/Lamp.cs`:
  - Change the `LampColorProperty` registration default from `Tokens.Colors.Silkscreen` to `default`.
  - Add `LampColor = Tokens.Colors.Silkscreen;` as the first line of the constructor.
- `windows/src/VoxScribe.App/Controls/TransportKey.cs`:
  - Change the `EngagedColorProperty` registration default from `Tokens.Colors.Ink` to `default`.
  - Add `EngagedColor = Tokens.Colors.Ink;` at the end of the constructor.
- `windows/src/VoxScribe.App/Controls/BrushedPanel.cs`:
  - Change the `CornerRadiusProperty` registration default from `Tokens.Radius.Panel` to `0`.
  - Add the constructor:

```csharp
    /// <summary>Creates a panel at the active theme's card radius.</summary>
    public BrushedPanel() => CornerRadius = Tokens.Radius.Panel;
```

- [ ] **Step 3: Move theme application into `App` and make it live**

In `windows/src/VoxScribe.App/Composition.cs`, delete the two `Themes.Apply(...)` lines added in Task 3, together with their comment.

In `windows/src/VoxScribe.App/App.axaml.cs`:
- Add the usings `using Avalonia.Styling;`, `using Avalonia.Threading;` and `using VoxScribe.App.Design;`.
- Replace `_composition = Composition.Create();` / `_main = new MainWindow(_composition);` with:

```csharp
            _composition = Composition.Create();

            // The theme follows three inputs: the saved theme, the saved variant, and Windows'
            // light/dark mode. Apply is a no-op when nothing changed, so a settings save that
            // touches something else (a chord, an endpoint) does not rebuild any window.
            var settings = _composition.Settings;
            void ApplyTheme() => Themes.Apply(
                settings.Data.Theme, settings.Data.AccentVariant, ActualThemeVariant == ThemeVariant.Dark);
            ApplyTheme();
            ActualThemeVariantChanged += (_, _) => ApplyTheme();
            settings.Changed += (_, _) => Dispatcher.UIThread.Post(ApplyTheme);

            _main = new MainWindow(_composition);
```

- Delete the `Restart()` method and its doc comment. `Program.cs`'s `--restarted` handling stays; it is harmless.

- [ ] **Step 4: Rebuild `MainWindow` on `Themes.Changed`**

In `windows/src/VoxScribe.App/Views/MainWindow.cs` (the Task 1 version):
- Add `using Avalonia.Threading;`.
- Change the three fields `_sectionHost`, `_transcriptionsKey` and `_dictionaryKey` from `readonly` to mutable:

```csharp
    private ContentControl _sectionHost = new();
    private RailKey _transcriptionsKey = new(WaveIcon);
    private RailKey _dictionaryKey = new(BookIcon);
    private readonly EventHandler _onThemeChanged;
```

- In the constructor, delete the block from `_transcriptionsKey = new RailKey(WaveIcon) { IsEngaged = true };` through `ShowSection(transcriptions: true);`, and put this in its place:

```csharp
        // Views are built in C# and cache brushes, so a theme change rebuilds the content.
        // Posted: Changed can fire from inside a click handler in Settings.
        _onThemeChanged = (_, _) => Dispatcher.UIThread.Post(Rebuild);
        Themes.Changed += _onThemeChanged;
        Rebuild();
```

- Add these two members:

```csharp
    /// <summary>Rebuilds the whole window in the current theme, keeping the open section.</summary>
    private void Rebuild()
    {
        var transcriptions = _sectionHost.Content is null || _transcriptionsKey.IsEngaged;

        // A control can have one parent: every rebuild starts from fresh instances. The cached
        // section views hold the old theme's brushes, so they go too.
        _transcriptionsView = null;
        _dictionaryView = null;
        _sectionHost = new ContentControl();
        _transcriptionsKey = new RailKey(WaveIcon);
        _dictionaryKey = new RailKey(BookIcon);
        Avalonia.Automation.AutomationProperties.SetName(_transcriptionsKey, "Transcriptions");
        Avalonia.Automation.AutomationProperties.SetName(_dictionaryKey, "Dictionary");
        _transcriptionsKey.Click += (_, _) => ShowSection(transcriptions: true);
        _dictionaryKey.Click += (_, _) => ShowSection(transcriptions: false);

        Background = Tokens.Brushes.Chassis;
        Content = BuildLayout();
        ShowSection(transcriptions);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        Themes.Changed -= _onThemeChanged;
        base.OnClosed(e);
    }
```

- [ ] **Step 5: Repaint the old pill between dictations**

In `windows/src/VoxScribe.App/Views/HudWindow.cs`:
- Add the field `private bool _themeStale;`.
- At the end of the constructor, add `Themes.Changed += (_, _) => _themeStale = true;`.
- In `Sync()`, inside the `if (state == DictationState.Idle)` branch, directly after `if (IsVisible) Hide();`, insert `if (_themeStale) RepaintTheme();`.
- Add this method:

```csharp
    /// <summary>Picks up a theme change. Only called while hidden, never mid-dictation.</summary>
    private void RepaintTheme()
    {
        _themeStale = false;
        _shown = null;
        _shell.Background = new SolidColorBrush(Tokens.Colors.Glass);
        _shell.CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        _preview.FontFamily = Tokens.Fonts.Prose;
        _preview.Foreground = Tokens.Brushes.InkOnDeck;
        _mode.FontFamily = Tokens.Fonts.Mono;
        _timer.FontFamily = Tokens.Fonts.Mono;
        _hint.FontFamily = Tokens.Fonts.Mono;
    }
```

- [ ] **Step 6: Drop the restart from `AppearanceSection`**

In `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs`:
- Delete the `apply` button, `SyncApply` and both calls to it, plus `row.Children.Add(apply);`.
- Remove `using Avalonia;` if it is now unused.
- Change the note to `Panels.Note("Theme — applies immediately and follows Windows light and dark mode.")`.

Confirm nothing calls `Restart` any more: `grep -rn "Restart()" --include=*.cs windows/src` should print no output.

- [ ] **Step 7: Run the Global Constraints verification**

Expected: `LiveThemeTests` passes and everything else stays green.

- [ ] **Step 8: Manual check (record the result in the task report)**

On this Windows machine:

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes/windows
dotnet run --project src/VoxScribe.App -c Release
```

Toggle *Settings → Personalisation → Colours → Choose your mode* between Light and Dark. The main window must repaint within a second, with no restart. This is the only way to verify the `ActualThemeVariantChanged` wiring, because the headless platform never reports dark.

The Settings *dialog* does not repaint while open. That is expected until Task 7 folds it into the window.

- [ ] **Step 9: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
feat: themes switch live and follow Windows light/dark mode

App re-applies on ActualThemeVariantChanged and settings changes; Apply is a
no-op when nothing moved. The main window rebuilds on Themes.Changed, the pill
repaints between dictations, and the restart-to-apply path is gone.
EOF
```

---

### Task 5: Home stats calculator (Core, TDD)

**Files:**
- Create: `windows/src/VoxScribe.Core/HomeStats.cs`
- Test: `windows/tests/VoxScribe.Core.Tests/HomeStatsTests.cs`

**Interfaces:**
- Consumes: `TranscriptRecord` (`At`, `AudioSeconds`, `Text`), unchanged.
- Produces:
  - `public sealed record HomeStatsResult(int WordsThisWeek, int AverageWpm, TimeSpan TimeSaved, int StreakDays)`
  - `public static class HomeStats` with:
    - `const double TypingWordsPerMinute = 40`
    - `static TimeSpan Week`
    - `static HomeStatsResult Compute(IEnumerable<TranscriptRecord> records, DateTimeOffset now)`
    - `static int CountWords(string text)`
    - `static string FormatDuration(TimeSpan span)`

Semantics, which the tests pin:
- **Week:** records with `now − 7 days < At ≤ now` (rolling).
- **Words:** a whitespace split.
- **WPM:** words ÷ (AudioSeconds ÷ 60), over the week's records with `AudioSeconds > 0`, rounded half away from zero. 0 when there is no audio.
- **Time saved:** `max(0, weekWords ÷ 40 − weekAudioSeconds ÷ 60)` minutes.
- **Streak:** consecutive calendar days with at least one record, in `now`'s offset. It ends today, or yesterday if there is nothing yet today, so the streak survives until the day is over.

- [ ] **Step 1: Write the failing tests**

Create `windows/tests/VoxScribe.Core.Tests/HomeStatsTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>The four Home numbers, from transcript history alone.</summary>
public sealed class HomeStatsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 15, 0, 0, TimeSpan.FromHours(2));

    private static TranscriptRecord Take(DateTimeOffset at, int words, double seconds = 0) => new()
    {
        At = at,
        Text = string.Join(' ', Enumerable.Repeat("word", words)),
        AudioSeconds = seconds,
    };

    [Fact]
    public void No_history_is_all_zeros()
    {
        HomeStats.Compute([], Now).ShouldBe(new HomeStatsResult(0, 0, TimeSpan.Zero, 0));
    }

    [Fact]
    public void Words_count_only_the_rolling_week()
    {
        var stats = HomeStats.Compute(
            [Take(Now.AddHours(-1), 10), Take(Now.AddDays(-6.9), 5), Take(Now.AddDays(-7.1), 100)], Now);

        stats.WordsThisWeek.ShouldBe(15);
    }

    [Fact]
    public void Words_split_on_any_whitespace()
    {
        HomeStats.CountWords("  deux   mots\tet\nquatre ").ShouldBe(4);
        HomeStats.CountWords(string.Empty).ShouldBe(0);
    }

    [Fact]
    public void Pace_is_words_over_spoken_minutes_and_ignores_untimed_takes()
    {
        var stats = HomeStats.Compute(
            [Take(Now.AddHours(-1), 150, seconds: 60), Take(Now.AddHours(-2), 75, seconds: 30), Take(Now.AddHours(-3), 999)],
            Now);

        stats.AverageWpm.ShouldBe(150);
    }

    [Fact]
    public void Time_saved_is_typing_at_40_wpm_minus_speaking_time()
    {
        // 400 words typed at 40 wpm = 10 min; spoken in 1 min; saved 9 min.
        HomeStats.Compute([Take(Now.AddHours(-1), 400, seconds: 60)], Now)
            .TimeSaved.ShouldBe(TimeSpan.FromMinutes(9));
    }

    [Fact]
    public void Time_saved_is_never_negative()
    {
        // 4 words would take 6 s to type but took 60 s to say.
        HomeStats.Compute([Take(Now.AddHours(-1), 4, seconds: 60)], Now).TimeSaved.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Streak_counts_consecutive_days_ending_today()
    {
        var stats = HomeStats.Compute(
            [Take(Now, 1), Take(Now.AddDays(-1), 1), Take(Now.AddDays(-2), 1), Take(Now.AddDays(-4), 1)], Now);

        stats.StreakDays.ShouldBe(3);
    }

    [Fact]
    public void A_streak_survives_until_the_day_is_over()
    {
        HomeStats.Compute([Take(Now.AddDays(-1), 1), Take(Now.AddDays(-2), 1)], Now).StreakDays.ShouldBe(2);
    }

    [Fact]
    public void A_missed_day_breaks_the_streak()
    {
        HomeStats.Compute([Take(Now.AddDays(-2), 1), Take(Now.AddDays(-3), 1)], Now).StreakDays.ShouldBe(0);
    }

    [Fact]
    public void Days_are_calendar_days_in_the_callers_offset()
    {
        // 23:30 UTC on the 7th is 01:30 on the 8th at +02:00 — that is today, not yesterday.
        var lateUtc = new DateTimeOffset(2026, 10, 7, 23, 30, 0, TimeSpan.Zero);
        HomeStats.Compute([Take(lateUtc, 1)], Now).StreakDays.ShouldBe(1);
    }

    [Fact]
    public void Durations_read_as_minutes_then_hours()
    {
        HomeStats.FormatDuration(TimeSpan.FromMinutes(9)).ShouldBe("9 min");
        HomeStats.FormatDuration(TimeSpan.FromMinutes(72)).ShouldBe("1h 12");
        HomeStats.FormatDuration(TimeSpan.Zero).ShouldBe("0 min");
    }
}
```

- [ ] **Step 2: Run them to watch them fail**

`dotnet test tests/VoxScribe.Core.Tests --filter FullyQualifiedName~HomeStatsTests`

Expected: compile error, `HomeStats` does not exist.

- [ ] **Step 3: Create `windows/src/VoxScribe.Core/HomeStats.cs`**

```csharp
using System.Globalization;

namespace VoxScribe.Core;

/// <summary>The four numbers on the Home page.</summary>
/// <param name="WordsThisWeek">Words dictated in the rolling last 7 days.</param>
/// <param name="AverageWpm">Speaking pace over the week's timed dictations.</param>
/// <param name="TimeSaved">Typing time at 40 wpm minus speaking time, never negative.</param>
/// <param name="StreakDays">Consecutive days with a dictation, ending today or yesterday.</param>
public sealed record HomeStatsResult(int WordsThisWeek, int AverageWpm, TimeSpan TimeSaved, int StreakDays);

/// <summary>Computes <see cref="HomeStatsResult"/> from transcript history. Pure; the clock is passed in.</summary>
public static class HomeStats
{
    /// <summary>The typing speed dictation is compared against.</summary>
    public const double TypingWordsPerMinute = 40;

    /// <summary>The rolling window "this week" covers.</summary>
    public static TimeSpan Week { get; } = TimeSpan.FromDays(7);

    /// <summary>Computes the stats as of <paramref name="now"/>; days are counted in its offset.</summary>
    public static HomeStatsResult Compute(IEnumerable<TranscriptRecord> records, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);

        var since = now - Week;
        var words = 0;
        var timedWords = 0;
        var seconds = 0.0;
        var days = new HashSet<DateTime>();

        foreach (var record in records)
        {
            days.Add(record.At.ToOffset(now.Offset).Date);
            if (record.At <= since || record.At > now) continue;

            var count = CountWords(record.Text);
            words += count;
            if (record.AudioSeconds > 0)
            {
                timedWords += count;
                seconds += record.AudioSeconds;
            }
        }

        var wpm = seconds > 0
            ? (int)Math.Round(timedWords / (seconds / 60), MidpointRounding.AwayFromZero)
            : 0;
        var saved = TimeSpan.FromMinutes(Math.Max(0, (words / TypingWordsPerMinute) - (seconds / 60)));

        return new HomeStatsResult(words, wpm, saved, Streak(days, now.Date));
    }

    /// <summary>Whitespace-separated words.</summary>
    public static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>"9 min" under an hour, "1h 12" from an hour up.</summary>
    public static string FormatDuration(TimeSpan span) => span.TotalMinutes < 60
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes} min")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes:00}");

    private static int Streak(HashSet<DateTime> days, DateTime today)
    {
        var day = days.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (days.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }
}
```

- [ ] **Step 4: Run the tests, then the Global Constraints verification**

Expected: 11 `HomeStatsTests` pass and everything else is green.

- [ ] **Step 5: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
feat: HomeStats computes words, pace, time saved and streak from history

Platform-neutral and clock-injected, so the Home page's numbers are unit-tested.
EOF
```

---
### Task 6: The sidebar skeleton and the Home page

Visual spec:
- Read `.superpowers/mockups/Main.dc.html` (Paper Home), `Orb-Home.dc.html`, `Tide-Home.dc.html`, `Mono-Home.dc.html` and `Fluent-Home.dc.html`.
- One skeleton serves all five. The differences are the `ThemeDefinition` knobs from Task 3: `Nav`, `NavWidth`, `NavSelection`, `Stats`, `HeroStat`, `List`, `HeaderCard`, `ContentOnSurface`, `Uppercase`, `Headline`, `HeadlineSize`, `StatSize`, `StatNumerals` and the wordmark fields.
- Do not add per-theme branches beyond these knobs.

**Files:**
- Replace: `windows/src/VoxScribe.App/Views/MainWindow.cs`
- Create: `windows/src/VoxScribe.App/Views/Shell.cs`
- Create: `windows/src/VoxScribe.App/Views/HomePage.cs`
- Create: `windows/src/VoxScribe.App/Controls/DashedRule.cs`
- Modify: `windows/src/VoxScribe.App/Views/Panels.cs` (add `LinkButton`, `IconButton`, `Chord`)
- Modify: `windows/src/VoxScribe.App/Views/TranscriptionsView.cs`, `windows/src/VoxScribe.App/Views/DictionaryView.cs` (add `SearchText`)
- Modify: `windows/src/VoxScribe.App/Composition.cs` (expose `Injector`)
- Modify: `windows/src/VoxScribe.App/Design/DesignTokens.cs`
- Delete: `windows/src/VoxScribe.App/Controls/RailKey.cs`
- Test: `windows/tests/VoxScribe.App.Tests/UiTests.cs` (`MainWindowTests`, `KeyHitAreaTests`), `windows/tests/VoxScribe.App.Tests/HomePageTests.cs` (create)

**Interfaces:**
- Consumes:
  - `HomeStats.Compute`, `HomeStats.FormatDuration`, `HomeStatsResult` (Task 5)
  - `Themes.Active` and its knobs, `Themes.Changed` (Task 3/4)
  - `Tokens.Brushes.Accent/AccentFill/OnAccent/InkSecondary/Seam/Hover`, `Tokens.Colors.AccentTint/AccentSecondary/Specular`, `Tokens.Fonts.Display/Row` (Task 3)
- Produces:
  - `public enum AppPage { Home, History, Dictionary, Settings }`, in `MainWindow.cs`.
  - `MainWindow.ShowPage(AppPage page)`, `MainWindow.CurrentPage`, `MainWindow.ExitAllowed`.
  - `internal sealed class NavButton : Button` (`Icon` init property).
  - `internal static class Shell`:
    - icon constants `HomeIcon`, `HistoryIcon`, `DictionaryIcon`, `SettingsIcon`, `CopyIcon`, `RetypeIcon`
    - `Caps(string)`, `Icon(string data, double size, double stroke, IBrush brush)`
    - `Nav(Action<AppPage>, IDictionary<AppPage, NavButton>)`
    - `Paint(Button, Selection, bool selected)`
    - `Orb(double size, bool core)`
    - `PageTitle(string)`
  - `internal sealed class HomePage(TranscriptStore? transcripts, AppSettings? settings, Action<AppPage> navigate, Func<string, Task> retype)`.
  - `Panels.LinkButton(string)`, `Panels.IconButton(string pathData, string name)`, `Panels.Chord(int[]? keys)` (returns `"not set"` when empty).
  - `internal sealed class DashedRule : Control` (`Stroke` property).
  - `Composition.Injector` (`ITextInjector?`).
  - `TranscriptionsView.SearchText` and `DictionaryView.SearchText` (`string` get/set).
  - Interim: `ShowPage(AppPage.Settings)` still opens the `SettingsWindow` dialog. Task 7 replaces this with a page.

- [ ] **Step 1: Tokens**

In `windows/src/VoxScribe.App/Design/DesignTokens.cs`:
- Delete `Radius.RailKey` and the `Material` members `RailWidth`, `RailKeySize`, `RailIconSize`, `RailIconStroke`, `BadgeSize`, `BadgeIconSize` and `BadgeIconStroke`.
- Add to `Material`:

```csharp
        /// <summary>A sidebar nav item's height.</summary>
        public const double NavItemHeight = 40;

        /// <summary>A rail nav item (Orb): width.</summary>
        public const double RailItemWidth = 60;

        /// <summary>A rail nav item (Orb): height, icon over caption.</summary>
        public const double RailItemHeight = 56;

        /// <summary>Nav icon canvas.</summary>
        public const double NavIconSize = 18;

        /// <summary>Nav icon stroke.</summary>
        public const double NavIconStroke = 1.6;

        /// <summary>A recent-dictation row on Home.</summary>
        public const double RowHeight = 46;

        /// <summary>The time column of a recent row.</summary>
        public const double RowTimeWidth = 44;

        /// <summary>Copy / Type-again buttons.</summary>
        public const double RowButtonSize = 32;

        /// <summary>Icon inside a row button.</summary>
        public const double RowIconSize = 15;

        /// <summary>Row icon stroke.</summary>
        public const double RowIconStroke = 1.7;

        /// <summary>The orb mark at the head of Orb's rail.</summary>
        public const double OrbMarkSize = 34;

        /// <summary>The orb in Orb's Home header card.</summary>
        public const double HeroOrbSize = 132;
```

- Add to `Motion`:

```csharp
        /// <summary>
        /// After minimising for "Type again", how long focus is given to return to the previous
        /// window before the text is sent. Hand-tuned; raise it if the text lands nowhere.
        /// </summary>
        public static TimeSpan RetypeSettle { get; } = TimeSpan.FromMilliseconds(350);
```

- [ ] **Step 2: Delete the rail key**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes/windows/src/VoxScribe.App
git rm Controls/RailKey.cs
```

In `windows/tests/VoxScribe.App.Tests/UiTests.cs` `KeyHitAreaTests`, change the array to `Button[] keys = [new TransportKey()];`.

- [ ] **Step 3: Expose the injector from `Composition`**

In `windows/src/VoxScribe.App/Composition.cs`:
- Add a constructor parameter `ITextInjector? injector` after `bool platformAvailable`, plus the property below.
- Change the final `return new Composition(settings, dictionary, transcripts, engine, available);` to `return new Composition(settings, dictionary, transcripts, engine, available, injector);`.

```csharp
    /// <summary>The text injector the engine types with, or null without a platform layer. Used by "Type again".</summary>
    public ITextInjector? Injector { get; }
```

- [ ] **Step 4: Add `SearchText` to both list views**

In `windows/src/VoxScribe.App/Views/TranscriptionsView.cs` and `windows/src/VoxScribe.App/Views/DictionaryView.cs`, add:

```csharp
    /// <summary>The search box's text, so a theme rebuild can carry it over.</summary>
    public string SearchText
    {
        get => _search.Text ?? string.Empty;
        set => _search.Text = value;
    }
```

- [ ] **Step 5: Create `windows/src/VoxScribe.App/Controls/DashedRule.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>A one-pixel dashed rule, for log-style lists (Mono). Border has no dashed stroke.</summary>
internal sealed class DashedRule : Control
{
    private const double Dash = 4;
    private const double Gap = 3;

    /// <summary>Creates a rule in the seam colour.</summary>
    public DashedRule()
    {
        Height = Tokens.Border.Hairline;
        Stroke = Tokens.Brushes.Seam;
    }

    /// <summary>The dash colour.</summary>
    public IBrush Stroke { get; set; }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Stroke, Tokens.Border.Hairline, new DashStyle([Dash, Gap], 0));
        var y = Bounds.Height / 2;
        context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }
}
```

- [ ] **Step 6: Add the three helpers to `windows/src/VoxScribe.App/Views/Panels.cs`**

Add these to the `Panels` class. `Panels` already has `using Avalonia.Media;`. Add `using Avalonia.Automation;`.

```csharp
    /// <summary>An accent text link ("See all history →").</summary>
    public static Button LinkButton(string text) => new()
    {
        Content = text,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Body,
        Foreground = Tokens.Brushes.Accent,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A square icon-only button with an accessible name and tooltip.</summary>
    public static Button IconButton(string pathData, string name)
    {
        var button = new Button
        {
            Width = Tokens.Material.RowButtonSize,
            Height = Tokens.Material.RowButtonSize,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = Shell.Icon(pathData, Tokens.Material.RowIconSize, Tokens.Material.RowIconStroke, Tokens.Brushes.InkSecondary),
        };
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }

    /// <summary>A chord as key names joined by " + ", or "not set".</summary>
    public static string Chord(int[]? keys) =>
        keys is { Length: > 0 } ? string.Join(" + ", keys.Select(PlatformFactory.KeyDisplayName)) : "not set";
```

- [ ] **Step 7: Create `windows/src/VoxScribe.App/Views/Shell.cs`**

Icon paths are the 24-unit stroke icons from the mockups.

```csharp
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace VoxScribe.App.Views;

/// <summary>A nav item whose icon is recoloured with its label.</summary>
internal sealed class NavButton : Button
{
    /// <summary>Keeps Fluent's Button template; a subclass would otherwise have no theme.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>The stroke icon.</summary>
    public IconPath? Icon { get; init; }
}

/// <summary>The skeleton's shared furniture: navigation, selection marking, icons, the orb mark.</summary>
internal static class Shell
{
    /// <summary>Home.</summary>
    public const string HomeIcon = "M4 11l8-7 8 7v9a1 1 0 0 1-1 1h-4v-6h-6v6H5a1 1 0 0 1-1-1z";

    /// <summary>History (clock).</summary>
    public const string HistoryIcon = "M12 3.5a8.5 8.5 0 1 0 0 17a8.5 8.5 0 1 0 0-17M12 7.5V12l3 2";

    /// <summary>Dictionary (book).</summary>
    public const string DictionaryIcon = "M5 4.5h10a4 4 0 0 1 4 4V20H9a4 4 0 0 1-4-4zM9 9h6M9 13h4";

    /// <summary>Settings (sliders).</summary>
    public const string SettingsIcon =
        "M4 7h10M18 7h2M4 17h4M12 17h8M16 5a2 2 0 1 0 0 4a2 2 0 1 0 0-4M10 15a2 2 0 1 0 0 4a2 2 0 1 0 0-4";

    /// <summary>Copy.</summary>
    public const string CopyIcon = "M8 8h12v12H8zM16 8V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h3";

    /// <summary>Type again.</summary>
    public const string RetypeIcon = "M9 14l-5-5 5-5M4 9h10a6 6 0 0 1 0 12h-3";

    private const double OrbCoreX = 0.35;
    private const double OrbCoreY = 0.3;
    private const double OrbRadius = 0.65;
    private const double OrbSecondaryStop = 0.22;
    private const double OrbAccentStop = 0.58;
    private const double OrbFadeStop = 0.74;

    /// <summary>The text, in capitals when the theme sets labels in capitals.</summary>
    public static string Caps(string text) => Themes.Active.Uppercase ? text.ToUpperInvariant() : text;

    /// <summary>A stroke icon.</summary>
    public static IconPath Icon(string data, double size, double stroke, IBrush brush) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = brush,
        StrokeThickness = stroke,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>A page's title, in the theme's display face.</summary>
    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Display,
        FontWeight = Themes.Active.DisplayWeight,
        FontSize = Themes.Active.HeadlineSize,
        Foreground = Tokens.Brushes.Ink,
        Margin = new Thickness(0, 0, 0, Tokens.Space.Roomy),
    };

    /// <summary>
    /// The navigation column for the active theme, filling <paramref name="items"/> with one
    /// button per page. Selection is painted by the caller with <see cref="Paint"/>.
    /// </summary>
    public static Control Nav(Action<AppPage> navigate, IDictionary<AppPage, NavButton> items)
    {
        var theme = Themes.Active;
        var rail = theme.Nav == NavStyle.Rail;
        var stack = new StackPanel { Spacing = Tokens.Space.Tight };

        if (rail)
        {
            var mark = Orb(Tokens.Material.OrbMarkSize, core: false);
            mark.Margin = new Thickness(0, 0, 0, Tokens.Space.Wide);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(mark);
        }
        else if (Wordmark() is { } wordmark)
        {
            stack.Children.Add(wordmark);
        }

        foreach (var page in Enum.GetValues<AppPage>())
        {
            var item = Item(page, rail);
            item.Click += (_, _) => navigate(page);
            items[page] = item;
            stack.Children.Add(item);
        }

        return theme.Nav switch
        {
            NavStyle.Rail => new Border
            {
                Width = theme.NavWidth,
                Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Tight),
                Child = stack,
            },
            NavStyle.FloatingCard => new Border
            {
                Width = theme.NavWidth,
                Margin = new Thickness(Tokens.Space.Wide, 0, 0, Tokens.Space.Wide),
                Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Wide),
                Background = Tokens.Brushes.Panel,
                CornerRadius = new CornerRadius(Tokens.Radius.Panel),
                Child = stack,
            },
            _ => new Border
            {
                Width = theme.NavWidth,
                Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Wide),
                BorderBrush = Tokens.Brushes.Seam,
                BorderThickness = new Thickness(0, 0, theme.ContentOnSurface ? 0 : Tokens.Border.Hairline, 0),
                Child = stack,
            },
        };
    }

    /// <summary>Marks <paramref name="item"/> selected or not, in <paramref name="style"/>.</summary>
    public static void Paint(Button item, Selection style, bool selected)
    {
        item.BorderBrush = null;
        item.BorderThickness = new Thickness(0);

        if (!selected)
        {
            item.Background = Brushes.Transparent;
            item.Foreground = Tokens.Brushes.InkSecondary;
        }
        else
        {
            switch (style)
            {
                case Selection.Tint:
                    item.Background = new SolidColorBrush(Tokens.Colors.AccentTint);
                    item.Foreground = Tokens.Brushes.Accent;
                    break;
                case Selection.AccentFill:
                    item.Background = Tokens.Brushes.AccentFill;
                    item.Foreground = Tokens.Brushes.OnAccent;
                    break;
                case Selection.InkFill:
                    item.Background = Tokens.Brushes.Ink;
                    item.Foreground = Tokens.Brushes.Chassis;
                    break;
                case Selection.Bar:
                    item.Background = Tokens.Brushes.Hover;
                    item.Foreground = Tokens.Brushes.Ink;
                    item.BorderBrush = Tokens.Brushes.Accent;
                    item.BorderThickness = new Thickness(Tokens.Border.Ring, 0, 0, 0);
                    break;
                case Selection.Underline:
                    item.Background = Brushes.Transparent;
                    item.Foreground = Tokens.Brushes.Ink;
                    item.BorderBrush = Tokens.Brushes.Accent;
                    item.BorderThickness = new Thickness(0, 0, 0, Tokens.Border.Ring);
                    break;
                default:
                    item.Background = Tokens.Brushes.Hover;
                    item.Foreground = Tokens.Brushes.Ink;
                    break;
            }
        }

        if (item is NavButton { Icon: { } icon }) icon.Stroke = item.Foreground;
    }

    /// <summary>
    /// A radial-gradient orb in the accent: the Orb theme's mark. With <paramref name="core"/>
    /// it has the white hot spot of the Home header's large orb.
    /// </summary>
    public static Ellipse Orb(double size, bool core)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(OrbCoreX, OrbCoreY, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(OrbCoreX, OrbCoreY, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(OrbRadius, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(OrbRadius, RelativeUnit.Relative),
        };
        if (core) brush.GradientStops.Add(new GradientStop(Tokens.Colors.Specular, 0));
        brush.GradientStops.Add(new GradientStop(Tokens.Colors.AccentSecondary, core ? OrbSecondaryStop : 0));
        brush.GradientStops.Add(new GradientStop(Tokens.Colors.Accent, OrbAccentStop));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, OrbFadeStop));

        return new Ellipse { Width = size, Height = size, Fill = brush, IsHitTestVisible = false };
    }

    private static string PageIcon(AppPage page) => page switch
    {
        AppPage.History => HistoryIcon,
        AppPage.Dictionary => DictionaryIcon,
        AppPage.Settings => SettingsIcon,
        _ => HomeIcon,
    };

    private static NavButton Item(AppPage page, bool rail)
    {
        var icon = Icon(PageIcon(page), Tokens.Material.NavIconSize, Tokens.Material.NavIconStroke, Tokens.Brushes.InkSecondary);
        var label = new TextBlock
        {
            Text = Caps(page.ToString()),
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = rail ? Tokens.Fonts.Caption : Tokens.Fonts.Row,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };

        var item = new NavButton
        {
            Icon = icon,
            Content = new StackPanel
            {
                Orientation = rail ? Orientation.Vertical : Orientation.Horizontal,
                Spacing = rail ? Tokens.Space.Tight : Tokens.Space.Base,
                HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, label },
            },
            Height = rail ? Tokens.Material.RailItemHeight : Tokens.Material.NavItemHeight,
            Width = rail ? Tokens.Material.RailItemWidth : double.NaN,
            HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            HorizontalContentAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(rail ? 0 : Tokens.Space.Base, 0),
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            Background = Brushes.Transparent,
        };
        AutomationProperties.SetName(item, page.ToString());
        return item;
    }

    private static StackPanel? Wordmark()
    {
        var theme = Themes.Active;
        if (theme.WordmarkLead.Length == 0) return null;

        TextBlock Run(string text, IBrush brush, bool italic) => new()
        {
            Text = text,
            FontFamily = Tokens.Fonts.Display,
            FontWeight = theme.DisplayWeight,
            FontSize = theme.WordmarkSize,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = brush,
        };

        var mark = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(Tokens.Space.Base, 0, 0, Tokens.Space.Wide),
            IsHitTestVisible = false,
        };
        mark.Children.Add(Run(theme.WordmarkLead, Tokens.Brushes.Ink, italic: false));
        if (theme.WordmarkAccent.Length > 0)
        {
            mark.Children.Add(Run(theme.WordmarkAccent, Tokens.Brushes.Accent, theme.WordmarkAccentItalic));
        }

        if (theme.WordmarkTail.Length > 0) mark.Children.Add(Run(theme.WordmarkTail, Tokens.Brushes.Ink, italic: false));
        return mark;
    }
}
```

- [ ] **Step 8: Create `windows/src/VoxScribe.App/Views/HomePage.cs`**

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// Home: a headline and status line, this week's four numbers, the last few dictations, and
/// the chords. Rebuilt on every history or settings change — it is small.
/// </summary>
internal sealed class HomePage : UserControl
{
    private const int RecentCount = 5;
    private const int HeroIndex = 2;
    private const int MorningEnds = 12;
    private const int AfternoonEnds = 18;

    private readonly TranscriptStore? _transcripts;
    private readonly AppSettings? _settings;
    private readonly Action<AppPage> _navigate;
    private readonly Func<string, Task> _retype;
    private readonly EventHandler _onChanged;

    /// <summary>Builds the page. Null stores give an empty page (headless tests).</summary>
    public HomePage(TranscriptStore? transcripts, AppSettings? settings, Action<AppPage> navigate, Func<string, Task> retype)
    {
        _transcripts = transcripts;
        _settings = settings;
        _navigate = navigate;
        _retype = retype;

        // Transcripts.Changed fires on the engine's worker thread: always marshal.
        _onChanged = (_, _) => Dispatcher.UIThread.Post(Refresh);
        if (_transcripts is not null) _transcripts.Changed += _onChanged;
        if (_settings is not null) _settings.Changed += _onChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            if (_transcripts is not null) _transcripts.Changed -= _onChanged;
            if (_settings is not null) _settings.Changed -= _onChanged;
        };

        Refresh();
    }

    private void Refresh()
    {
        var data = _settings?.Data ?? new SettingsData();
        IReadOnlyList<TranscriptRecord> records = _transcripts?.Records ?? [];
        var stats = HomeStats.Compute(records, DateTimeOffset.Now);

        var page = new DockPanel();
        page.Children.Add(Panels.Docked(BuildFooter(data), Dock.Bottom));
        page.Children.Add(Panels.Docked(new StackPanel
        {
            Spacing = Tokens.Space.Wide,
            Children = { BuildHeader(data), BuildStats(stats) },
        }, Dock.Top));
        page.Children.Add(new ScrollViewer { Content = BuildRecent(records) });
        Content = page;
    }

    private Control BuildHeader(SettingsData data)
    {
        var theme = Themes.Active;
        var headline = new TextBlock
        {
            Text = theme.Headline ?? Greeting(DateTime.Now.Hour),
            FontFamily = Tokens.Fonts.Display,
            FontWeight = theme.DisplayWeight,
            FontSize = theme.HeadlineSize,
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.Wrap,
        };

        var status = Panels.Row(
            Tokens.Space.Snug,
            new Ellipse
            {
                Width = Tokens.Material.LampBullet,
                Height = Tokens.Material.LampBullet,
                Fill = Tokens.Brushes.Ink,
                VerticalAlignment = VerticalAlignment.Center,
            },
            new TextBlock
            {
                Text = StatusLine(data),
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Body,
                Foreground = Tokens.Brushes.InkSecondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });

        var text = new StackPanel { Spacing = Tokens.Space.Base, Children = { headline, status } };
        if (!theme.HeaderCard) return text;

        var card = new DockPanel();
        card.Children.Add(Panels.Docked(Shell.Orb(Tokens.Material.HeroOrbSize, core: true), Dock.Right));
        text.VerticalAlignment = VerticalAlignment.Center;
        card.Children.Add(text);
        return new Border
        {
            Background = Tokens.Brushes.Panel,
            BorderBrush = Tokens.Brushes.Seam,
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(Tokens.Radius.Panel),
            Padding = new Thickness(Tokens.Space.Panel, Tokens.Space.Wide),
            Child = card,
        };
    }

    private static string Greeting(int hour) =>
        hour < MorningEnds ? "Good morning." : hour < AfternoonEnds ? "Good afternoon." : "Good evening.";

    private string StatusLine(SettingsData data)
    {
        var model = data.SttEndpoint is { Length: > 0 }
            ? $"Remote · {data.SttModel}"
            : _settings is null || Composition.IsModelInstalled ? "Parakeet · local" : "No speech model — see Settings";
        var cleanup = data.CleanupEndpoint is { Length: > 0 } ? $"Cleanup via {data.CleanupModel}" : "Cleanup off";
        return Shell.Caps($"Ready · {model} · {cleanup} · Hold {Panels.Chord(data.ResolvedPushToTalkKeys)} to dictate");
    }

    private static Control BuildStats(HomeStatsResult stats)
    {
        var theme = Themes.Active;
        (string Label, string Value, string Unit)[] cells =
        [
            ("Words this week", stats.WordsThisWeek.ToString("N0", CultureInfo.InvariantCulture), string.Empty),
            ("Average pace", stats.AverageWpm.ToString(CultureInfo.InvariantCulture), "wpm"),
            ("Time saved", HomeStats.FormatDuration(stats.TimeSaved), string.Empty),
            ("Streak", stats.StreakDays.ToString(CultureInfo.InvariantCulture), stats.StreakDays == 1 ? "day" : "days"),
        ];

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = theme.Stats == StatStyle.Cards ? Tokens.Space.Base : 0,
        };
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = BuildStat(cells[i].Label, cells[i].Value, cells[i].Unit, hero: i == HeroIndex, first: i == 0);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        if (theme.Stats == StatStyle.Cards) return grid;
        return new Border
        {
            BorderBrush = Tokens.Brushes.Seam,
            BorderThickness = new Thickness(0, Tokens.Border.Hairline, 0, Tokens.Border.Hairline),
            Child = grid,
        };
    }

    private static Border BuildStat(string label, string value, string unit, bool hero, bool first)
    {
        var theme = Themes.Active;
        var fill = hero && theme.HeroStat == HeroStyle.Fill;
        var tint = hero && theme.HeroStat == HeroStyle.Tint;
        var ink = fill ? Tokens.Brushes.OnAccent : hero ? Tokens.Brushes.Accent : Tokens.Brushes.Ink;
        var muted = fill ? Tokens.Brushes.OnAccent : Tokens.Brushes.InkSecondary;

        var number = new TextBlock
        {
            Inlines = new InlineCollection
            {
                new Run(value)
                {
                    FontFamily = theme.StatNumerals,
                    FontWeight = theme.StatWeight,
                    FontSize = theme.StatSize,
                    FontStyle = hero && theme.HeroStat == HeroStyle.AccentItalic ? FontStyle.Italic : FontStyle.Normal,
                    Foreground = ink,
                },
                new Run(unit.Length > 0 ? " " + Shell.Caps(unit) : string.Empty)
                {
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Body,
                    Foreground = muted,
                },
            },
        };

        var cell = new Border
        {
            Padding = new Thickness(Tokens.Space.Roomy),
            Child = new StackPanel
            {
                Spacing = Tokens.Space.Snug,
                Children =
                {
                    new TextBlock
                    {
                        Text = Shell.Caps(label),
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Label,
                        Foreground = muted,
                    },
                    number,
                },
            },
        };

        if (theme.Stats == StatStyle.Cards)
        {
            cell.Background = Tokens.Brushes.Panel;
            cell.BorderBrush = Tokens.Brushes.Seam;
            cell.BorderThickness = new Thickness(Tokens.Border.Hairline);
            cell.CornerRadius = new CornerRadius(Tokens.Radius.Panel);
        }
        else if (!first)
        {
            cell.BorderBrush = Tokens.Brushes.Seam;
            cell.BorderThickness = new Thickness(Tokens.Border.Hairline, 0, 0, 0);
        }

        if (fill) cell.Background = Tokens.Brushes.AccentFill;
        if (tint) cell.Background = new SolidColorBrush(Tokens.Colors.AccentTint);
        return cell;
    }

    private Control BuildRecent(IReadOnlyList<TranscriptRecord> records)
    {
        var theme = Themes.Active;
        var title = new TextBlock
        {
            Text = Shell.Caps("Recent"),
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Row,
            FontWeight = FontWeight.SemiBold,
            Foreground = Tokens.Brushes.Ink,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var seeAll = Panels.LinkButton(theme.Uppercase ? "[ALL HISTORY]" : "See all history →");
        seeAll.Click += (_, _) => _navigate(AppPage.History);

        var list = new StackPanel();
        var recent = records.Take(RecentCount).ToList();
        if (recent.Count == 0)
        {
            list.Children.Add(Panels.EmptyState("NO DICTATIONS YET", "Hold your shortcut anywhere and speak."));
        }

        for (var i = 0; i < recent.Count; i++) list.Children.Add(BuildRow(recent[i], i));

        Control body = theme.List == ListStyle.Card
            ? new Border
            {
                Background = Tokens.Brushes.Panel,
                BorderBrush = Tokens.Brushes.Seam,
                BorderThickness = new Thickness(Tokens.Border.Hairline),
                CornerRadius = new CornerRadius(Tokens.Radius.Panel),
                Padding = new Thickness(Tokens.Space.Snug),
                Child = list,
            }
            : list;

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Margin = new Thickness(0, Tokens.Space.Wide, 0, Tokens.Space.Base),
            Children = { Panels.SplitRow(title, seeAll), body },
        };
    }

    private Control BuildRow(TranscriptRecord record, int index)
    {
        var theme = Themes.Active;
        var time = new TextBlock
        {
            Text = record.At.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture),
            FontFamily = Tokens.Fonts.Mono,
            FontSize = Tokens.Fonts.Label,
            Foreground = Tokens.Brushes.InkSecondary,
            Width = Tokens.Material.RowTimeWidth,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock
        {
            Text = record.Text,
            FontFamily = Tokens.Fonts.Prose,
            FontSize = Tokens.Fonts.Row,
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Tokens.Space.Base, 0),
        };

        var copy = Panels.IconButton(Shell.CopyIcon, "Copy");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(record.Text).ConfigureAwait(true);
            }
        };
        var again = Panels.IconButton(Shell.RetypeIcon, "Type again");
        again.Click += async (_, _) => await _retype(record.Text).ConfigureAwait(true);

        var badge = Badge(cleaned: record.RawText is not null);
        badge.Margin = new Thickness(0, 0, Tokens.Space.Snug, 0);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            Height = Tokens.Material.RowHeight,
        };
        Control[] cells = [time, text, badge, copy, again];
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        var row = new Border { Padding = new Thickness(Tokens.Space.Snug, 0), Child = grid };
        switch (theme.List)
        {
            case ListStyle.Hairline:
                row.BorderBrush = Tokens.Brushes.Seam;
                row.BorderThickness = new Thickness(0, 0, 0, Tokens.Border.Hairline);
                break;
            case ListStyle.Zebra:
                if (index % 2 == 0) row.Background = Tokens.Brushes.Panel;
                row.CornerRadius = new CornerRadius(Tokens.Radius.Chip);
                break;
            case ListStyle.Card:
                if (index > 0)
                {
                    row.BorderBrush = Tokens.Brushes.Seam;
                    row.BorderThickness = new Thickness(0, Tokens.Border.Hairline, 0, 0);
                }

                break;
            case ListStyle.Dashed:
                return new StackPanel { Children = { new DashedRule(), row } };
        }

        return row;
    }

    /// <summary>RAW or CLEAN. History does not record command mode, so CMD is never shown here.</summary>
    private static Border Badge(bool cleaned) => new()
    {
        CornerRadius = new CornerRadius(Tokens.Radius.Chip),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        BorderBrush = cleaned ? Tokens.Brushes.Accent : Tokens.Brushes.Seam,
        Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Hair),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = cleaned ? "CLEAN" : "RAW",
            FontFamily = Tokens.Fonts.Mono,
            FontSize = Tokens.Fonts.Caption,
            LetterSpacing = Tokens.Fonts.SilkscreenTracking,
            Foreground = cleaned ? Tokens.Brushes.Accent : Tokens.Brushes.InkSecondary,
        },
    };

    private static WrapPanel BuildFooter(SettingsData data)
    {
        var footer = new WrapPanel
        {
            ItemSpacing = Tokens.Space.Wide,
            LineSpacing = Tokens.Space.Tight,
            Margin = new Thickness(0, Tokens.Space.Base, 0, 0),
        };

        (string Label, int[]? Keys)[] chords =
        [
            ("Raw", data.ResolvedPushToTalkKeys),
            ("Cleanup", data.CleanupPushToTalkKeys),
            ("Undo", data.UndoKeys),
            ("Command", data.CommandKeys),
        ];
        foreach (var (label, keys) in chords)
        {
            footer.Children.Add(new TextBlock
            {
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Label,
                Inlines = new InlineCollection
                {
                    new Run(Shell.Caps(label) + " ") { Foreground = Tokens.Brushes.InkSecondary },
                    new Run(Shell.Caps(Panels.Chord(keys))) { Foreground = Tokens.Brushes.Ink, FontWeight = FontWeight.Medium },
                },
            });
        }

        return footer;
    }
}
```

- [ ] **Step 9: Replace `windows/src/VoxScribe.App/Views/MainWindow.cs` entirely**

```csharp
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>The main window's pages, in navigation order.</summary>
public enum AppPage
{
    /// <summary>Status, stats, recent dictations.</summary>
    Home,

    /// <summary>All transcripts.</summary>
    History,

    /// <summary>Correction rules.</summary>
    Dictionary,

    /// <summary>Settings, in tabs.</summary>
    Settings,
}

/// <summary>
/// The main window: a title strip in the extended chrome, the theme's navigation, and the
/// open page. Rebuilt whole on <see cref="Themes.Changed"/>.
/// </summary>
/// <remarks>
/// Built in code rather than XAML, deliberately: every value comes from <see cref="Tokens"/>.
/// </remarks>
public sealed class MainWindow : Window
{
    private readonly Composition? _composition;
    private readonly Dictionary<AppPage, NavButton> _nav = [];
    private readonly EventHandler _onThemeChanged;

    private ContentControl _host = new();
    private TranscriptionsView? _history;
    private DictionaryView? _dictionary;
    private Control? _historyPage;
    private Control? _dictionaryPage;
    private string _historySearch = string.Empty;
    private string _dictionarySearch = string.Empty;
    private AppPage _page = AppPage.Home;

    /// <summary>Set just before an explicit quit so the hide-to-tray guard steps aside.</summary>
    public bool ExitAllowed { get; set; }

    /// <summary>The page on screen.</summary>
    public AppPage CurrentPage => _page;

    /// <summary>Builds a window with no engine behind it. Used by headless tests.</summary>
    public MainWindow() : this(null) { }

    /// <summary>Builds the window over <paramref name="composition"/>.</summary>
    public MainWindow(Composition? composition)
    {
        _composition = composition;

        Title = "Vox-Scribe";
        MinWidth = Tokens.Size.MainMinWidth;
        MinHeight = Tokens.Size.MainMinHeight;
        Width = Tokens.Size.MainWidth;
        Height = Tokens.Size.MainHeight;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
            new Uri("avares://VoxScribe.App/Assets/app.ico")));

        // The system keeps its caption buttons; the app paints the rest of the chrome.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = Tokens.Material.TitleBarHeight;

        // Close hides to the tray: a closed Avalonia window cannot be shown again. Real exit
        // sets ExitAllowed first (tray Quit).
        Closing += (_, e) =>
        {
            if (ExitAllowed) return;
            e.Cancel = true;
            Hide();
        };

        // Posted: Changed can fire from inside a click handler on the Appearance tab.
        _onThemeChanged = (_, _) => Dispatcher.UIThread.Post(Rebuild);
        Themes.Changed += _onThemeChanged;
        Rebuild();

        Opacity = Tokens.Motion.FadeInFrom;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = Tokens.Motion.FadeIn },
        };
        Loaded += (_, _) => Opacity = 1;

        _composition?.Engine?.Start();
    }

    /// <summary>Opens <paramref name="page"/> and marks it in the navigation.</summary>
    public void ShowPage(AppPage page)
    {
        if (page == AppPage.Settings)
        {
            // Interim until Task 7 folds Settings into the window.
            if (_composition is not null)
            {
                _ = new SettingsWindow(_composition.Settings, _composition.Engine).ShowDialog(this);
            }

            return;
        }

        _page = page;
        foreach (var (p, item) in _nav) Shell.Paint(item, Themes.Active.NavSelection, p == page);

        _host.Content = page switch
        {
            AppPage.History => HistoryPage(),
            AppPage.Dictionary => DictionaryPage(),
            _ => new HomePage(_composition?.Transcripts, _composition?.Settings, ShowPage, RetypeAsync),
        };
    }

    /// <summary>Rebuilds every control in the current theme, keeping the page and the search texts.</summary>
    private void Rebuild()
    {
        // A control has one parent and the old ones carry the old theme's brushes: start fresh.
        _historySearch = _history?.SearchText ?? _historySearch;
        _dictionarySearch = _dictionary?.SearchText ?? _dictionarySearch;
        _history = null;
        _dictionary = null;
        _historyPage = null;
        _dictionaryPage = null;
        _nav.Clear();
        _host = new ContentControl();

        var content = new Border
        {
            Padding = new Thickness(Tokens.Space.Panel, Tokens.Space.Base, Tokens.Space.Panel, Tokens.Space.Wide),
            Child = _host,
        };
        if (Themes.Active.ContentOnSurface)
        {
            content.Background = Tokens.Brushes.Panel;
            content.BorderBrush = Tokens.Brushes.Seam;
            content.BorderThickness = new Thickness(Tokens.Border.Hairline, Tokens.Border.Hairline, 0, 0);
            content.CornerRadius = new CornerRadius(Tokens.Radius.Panel, 0, 0, 0);
        }

        var root = new DockPanel();
        root.Children.Add(Panels.Docked(BuildTitleStrip(), Dock.Top));
        root.Children.Add(Panels.Docked(Shell.Nav(ShowPage, _nav), Dock.Left));
        root.Children.Add(content);

        Background = Tokens.Brushes.Chassis;
        Content = root;
        ShowPage(_page);
    }

    private static Border BuildTitleStrip() => new()
    {
        Height = Tokens.Material.TitleBarHeight,
        Padding = new Thickness(Tokens.Space.Roomy, 0, Tokens.Material.CaptionButtonsReserve, 0),
        Child = new TextBlock
        {
            Text = "Vox-Scribe",
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            Foreground = Tokens.Brushes.InkSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        },
    };

    private Control HistoryPage()
    {
        if (_composition is null) return Panels.EmptyState("NO RECORDINGS", "Hold the push-to-talk key and speak.");

        // Cached: the frame is the view's parent and must not be rebuilt on every visit.
        _history ??= new TranscriptionsView(_composition.Transcripts) { SearchText = _historySearch };
        return _historyPage ??= Framed("History", _history);
    }

    private Control DictionaryPage()
    {
        if (_composition is null) return Panels.EmptyState("DICTIONARY EMPTY", "Add words it keeps getting wrong.");

        _dictionary ??= new DictionaryView(_composition.Dictionary, _composition.Transcripts) { SearchText = _dictionarySearch };
        return _dictionaryPage ??= Framed("Dictionary", _dictionary);
    }

    private static DockPanel Framed(string title, Control body) => new()
    {
        Children = { Panels.Docked(Shell.PageTitle(Shell.Caps(title)), Dock.Top), body },
    };

    /// <summary>
    /// "Type again": the button gave focus to this window, so the text would land here. Minimise
    /// first, give focus a moment to return to the previous app, then type.
    /// </summary>
    private async Task RetypeAsync(string text)
    {
        if (_composition?.Injector is not { } injector) return;
        if (_composition.Engine is { State: not DictationState.Idle }) return;

        WindowState = WindowState.Minimized;
        await Task.Delay(Tokens.Motion.RetypeSettle).ConfigureAwait(true);
        await injector.InjectAsync(text, CancellationToken.None).ConfigureAwait(true);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        Themes.Changed -= _onThemeChanged;
        base.OnClosed(e);
    }
}
```

Remove `using Avalonia.Media;` if the compiler or format gate reports it unused.

- [ ] **Step 10: Tests**

In `windows/tests/VoxScribe.App.Tests/UiTests.cs`, add these to `MainWindowTests`, keeping the two existing tests:

```csharp
    [AvaloniaFact]
    public void Navigation_lists_the_four_pages_in_order()
    {
        var window = new MainWindow();
        window.Show();

        window.GetVisualDescendants().OfType<NavButton>()
            .Select(b => Avalonia.Automation.AutomationProperties.GetName(b))
            .ShouldBe(["Home", "History", "Dictionary", "Settings"]);
        window.CurrentPage.ShouldBe(AppPage.Home);
    }

    [AvaloniaFact]
    public void Every_page_opens_in_every_theme_light_and_dark()
    {
        try
        {
            foreach (var theme in Themes.All)
            foreach (var dark in new[] { false, true })
            {
                Themes.Apply(theme.Id, null, dark);
                var window = new MainWindow();
                window.Show();
                foreach (var page in new[] { AppPage.History, AppPage.Dictionary, AppPage.Home })
                {
                    window.ShowPage(page);
                    window.CurrentPage.ShouldBe(page, $"{theme.Id} {page}");
                }
            }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }

    [AvaloniaFact]
    public void Shell_icons_parse()
    {
        foreach (var data in new[] { Shell.HomeIcon, Shell.HistoryIcon, Shell.DictionaryIcon, Shell.SettingsIcon, Shell.CopyIcon, Shell.RetypeIcon })
        {
            Should.NotThrow(() => Avalonia.Media.Geometry.Parse(data), data);
        }
    }
```

Create `windows/tests/VoxScribe.App.Tests/HomePageTests.cs`:

```csharp
using System.IO;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

public sealed class HomePageTests
{
    private static TranscriptStore Store(int records, bool cleaned = false)
    {
        var store = new TranscriptStore(Path.Combine(Directory.CreateTempSubdirectory().FullName, "t.jsonl"));
        for (var i = 0; i < records; i++)
        {
            store.Add(new TranscriptRecord
            {
                At = DateTimeOffset.Now.AddMinutes(-i),
                Text = "one two three four five",
                AudioSeconds = 2,
                RawText = cleaned ? "one two tree four five" : null,
            });
        }

        return store;
    }

    private static Window Host(HomePage page)
    {
        var window = new Window { Content = page };
        window.Show();
        return window;
    }

    private static IEnumerable<Button> Named(Window w, string name) =>
        w.GetVisualDescendants().OfType<Button>().Where(b => AutomationProperties.GetName(b) == name);

    [AvaloniaFact]
    public void Shows_the_last_five_dictations_with_copy_and_type_again()
    {
        var window = Host(new HomePage(Store(7), null, _ => { }, _ => Task.CompletedTask));

        Named(window, "Copy").Count().ShouldBe(5);
        Named(window, "Type again").Count().ShouldBe(5);
    }

    [AvaloniaFact]
    public void Words_this_week_are_counted_from_history()
    {
        var window = Host(new HomePage(Store(7), null, _ => { }, _ => Task.CompletedTask));

        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Inlines?.OfType<Run>().Any(r => r.Text == "35") == true)
            .ShouldBeTrue("7 dictations × 5 words");
    }

    [AvaloniaFact]
    public void A_cleaned_dictation_is_badged_clean()
    {
        var window = Host(new HomePage(Store(1, cleaned: true), null, _ => { }, _ => Task.CompletedTask));

        window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "CLEAN").ShouldBeTrue();
    }

    [AvaloniaFact]
    public void See_all_opens_history()
    {
        AppPage? went = null;
        var window = Host(new HomePage(Store(1), null, p => went = p, _ => Task.CompletedTask));

        var link = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content is string s && s.StartsWith("See all", StringComparison.Ordinal));
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        went.ShouldBe(AppPage.History);
    }

    [AvaloniaFact]
    public void Empty_history_says_so()
    {
        var window = Host(new HomePage(Store(0), null, _ => { }, _ => Task.CompletedTask));

        window.GetVisualDescendants().OfType<Silkscreen>().Any(s => s.Text == "NO DICTATIONS YET").ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Builds_in_every_theme_light_and_dark()
    {
        try
        {
            foreach (var theme in Themes.All)
            foreach (var dark in new[] { false, true })
            {
                Themes.Apply(theme.Id, null, dark);
                Should.NotThrow(() => Host(new HomePage(Store(3, cleaned: true), null, _ => { }, _ => Task.CompletedTask)), theme.Id);
            }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }
}
```

- [ ] **Step 11: Run the Global Constraints verification**

Also confirm nothing references the deleted rail: `grep -rn "RailKey\|RailWidth\|BadgeSize" --include=*.cs windows` should print no output.

- [ ] **Step 12: Manual check (record in the task report)**

`dotnet run --project src/VoxScribe.App -c Release`. For each theme, set `"Theme"` in `%LOCALAPPDATA%\VoxScribe\settings.json` (or wait for Task 8's picker) and check:
- Nav, Home stats, rows and footer match the theme's `*-Home` mockup in spirit.
- The bundled fonts render. Instrument Serif headline on Paper, JetBrains Mono everywhere on Mono.
- "Type again" types into the previously focused app (Notepad).

- [ ] **Step 13: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
feat: one sidebar skeleton for all themes, with a Home page

Home shows status, words/pace/time saved/streak from history, the last five
dictations with Copy and Type again, and the chords. Navigation, stats and
rows take their framing from theme knobs.
EOF
```

---

### Task 7: Settings folded into the window as a tabbed page

**Files:**
- Create: `windows/src/VoxScribe.App/Views/SettingsPage.cs` (the logic moves from `SettingsWindow.cs`)
- Delete: `windows/src/VoxScribe.App/Views/SettingsWindow.cs`
- Modify: `windows/src/VoxScribe.App/Views/MainWindow.cs`, `windows/src/VoxScribe.App/App.axaml.cs`, `windows/src/VoxScribe.App/Design/DesignTokens.cs`
- Test: `windows/tests/VoxScribe.App.Tests/UiTests.cs` (replace `SettingsWindowTests`)

**Interfaces:**
- Consumes:
  - `Shell.Paint`, `Shell.PageTitle`, `Shell.Caps`, `AppPage`, `MainWindow.ShowPage` (Task 6)
  - the six section builders in `Views/Settings/` (unchanged signatures)
- Produces:
  - `internal enum SettingsTab { General, Speech, Shortcuts, Typing, Cleanup, Appearance }`
  - `internal sealed class SettingsPage : UserControl` with:
    - ctor `(AppSettings settings, DictationEngine? engine, SettingsTab tab)`
    - `SettingsTab Tab { get; }`
    - `void Select(SettingsTab tab)`
    - `void CancelRecording()`
  - Tab buttons carry automation names `"Settings tab: <Tab>"`.
  - `Tokens.Material.TabHeight` = 34.

- [ ] **Step 1: Write the failing tests**

In `windows/tests/VoxScribe.App.Tests/UiTests.cs`, replace the whole `SettingsWindowTests` class with:

```csharp
/// <summary>Settings is a page of the main window, in six tabs.</summary>
public sealed class SettingsPageTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"voxscribe-settings-{Guid.NewGuid():N}.json");

    /// <inheritdoc />
    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [AvaloniaFact]
    public void Six_tabs_in_order_each_showing_its_section()
    {
        var page = new SettingsPage(new AppSettings(_path), null, SettingsTab.General);
        var window = new Window { Content = page };
        window.Show();

        page.GetVisualDescendants().OfType<Button>()
            .Select(b => Avalonia.Automation.AutomationProperties.GetName(b))
            .Where(n => n?.StartsWith("Settings tab: ", StringComparison.Ordinal) == true)
            .ShouldBe([
                "Settings tab: General", "Settings tab: Speech", "Settings tab: Shortcuts",
                "Settings tab: Typing", "Settings tab: Cleanup", "Settings tab: Appearance",
            ]);

        foreach (var tab in Enum.GetValues<SettingsTab>())
        {
            page.Select(tab);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            page.Tab.ShouldBe(tab);
            page.GetVisualDescendants().OfType<Silkscreen>().Where(s => s.IsLarge).Select(s => s.Text)
                .ShouldContain(tab.ToString().ToUpperInvariant());
        }
    }

    [AvaloniaFact]
    public void The_section_area_scrolls()
    {
        var page = new SettingsPage(new AppSettings(_path), null, SettingsTab.Shortcuts);
        new Window { Content = page }.Show();

        page.GetVisualDescendants().OfType<ScrollViewer>().ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public void The_main_window_opens_settings_as_a_page()
    {
        var window = new MainWindow();
        window.Show();

        window.ShowPage(AppPage.Settings);

        window.CurrentPage.ShouldBe(AppPage.Settings);
    }
}
```

Run `dotnet test tests/VoxScribe.App.Tests --filter FullyQualifiedName~SettingsPageTests`. Expected: compile error, `SettingsPage` does not exist.

- [ ] **Step 2: Token**

In `windows/src/VoxScribe.App/Design/DesignTokens.cs`:
- Add to `Material`:

```csharp
        /// <summary>A settings tab's height.</summary>
        public const double TabHeight = 34;
```

- Delete `Size.SettingsWidth`, `Size.SettingsHeight`, `Size.SettingsMinWidth` and `Size.SettingsMinHeight`.

- [ ] **Step 3: Create `windows/src/VoxScribe.App/Views/SettingsPage.cs`**

The chord-recorder methods are moved verbatim from `SettingsWindow.cs`. The new parts are the tabs, `Select`, and cancelling on detach.

```csharp
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Settings;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>The settings tabs, in display order.</summary>
internal enum SettingsTab
{
    /// <summary>History and start-up.</summary>
    General,

    /// <summary>Model, device, remote STT.</summary>
    Speech,

    /// <summary>The four chords.</summary>
    Shortcuts,

    /// <summary>Where and when text is typed.</summary>
    Typing,

    /// <summary>The cleanup model.</summary>
    Cleanup,

    /// <summary>Theme and accent.</summary>
    Appearance,
}

/// <summary>Settings as a page of the main window: six tabs over the existing sections.</summary>
internal sealed class SettingsPage : UserControl
{
    /// <summary>Escape cancels a recording rather than becoming the trigger.</summary>
    private const int VkEscape = 0x1B;

    /// <summary>Right Alt is AltGr on many European layouts; warn rather than forbid.</summary>
    private const int VkRightAlt = 0xA5;

    private readonly AppSettings _settings;
    private readonly Dictionary<ShortcutSlot, TransportKey> _keys = [];
    private readonly TextBlock _keyWarning;
    private readonly Dictionary<SettingsTab, Control> _sections = [];
    private readonly Dictionary<SettingsTab, Button> _tabs = [];
    private readonly ContentControl _host = new();
    private readonly List<int> _captured = [];
    private readonly HashSet<int> _held = [];

    private ShortcutSlot _recordingSlot;
    private IDisposable? _recorder;

    /// <summary>Builds the page with <paramref name="tab"/> open.</summary>
    public SettingsPage(AppSettings settings, DictationEngine? engine, SettingsTab tab)
    {
        _settings = settings;

        foreach (var slot in Enum.GetValues<ShortcutSlot>())
        {
            var key = new TransportKey();
            key.Click += (_, _) =>
            {
                if (_recorder is null) StartRecording(slot); else CancelRecording();
            };
            _keys[slot] = key;
        }

        _keyWarning = new TextBlock
        {
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            Foreground = new SolidColorBrush(Tokens.Colors.Caution),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        // Built once: the shortcut recorders live inside their section, and a control can only
        // be parented once — swapping tabs re-hosts these instances.
        _sections[SettingsTab.General] = GeneralSection.Build(_settings, Save);
        _sections[SettingsTab.Speech] = SpeechSection.Build(_settings, Save);
        _sections[SettingsTab.Shortcuts] = ShortcutsSection.Build(_settings, Save, _keys, _keyWarning);
        _sections[SettingsTab.Typing] = TypingSection.Build(_settings, Save);
        _sections[SettingsTab.Cleanup] = CleanupSection.Build(_settings, Save);
        _sections[SettingsTab.Appearance] = AppearanceSection.Build(_settings, Save, engine);

        var bar = new WrapPanel
        {
            ItemSpacing = Tokens.Space.Tight,
            LineSpacing = Tokens.Space.Tight,
            Margin = new Thickness(0, 0, 0, Tokens.Space.Roomy),
        };
        foreach (var t in Enum.GetValues<SettingsTab>())
        {
            var button = new Button
            {
                Content = Shell.Caps(t.ToString()),
                Height = Tokens.Material.TabHeight,
                Padding = new Thickness(Tokens.Space.Base, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(Tokens.Radius.Chip),
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Body,
            };
            AutomationProperties.SetName(button, $"Settings tab: {t}");
            button.Click += (_, _) => Select(t);
            _tabs[t] = button;
            bar.Children.Add(button);
        }

        KeyboardNavigation.SetTabNavigation(_host, KeyboardNavigationMode.Cycle);
        Content = new DockPanel
        {
            Children =
            {
                Panels.Docked(Shell.PageTitle(Shell.Caps("Settings")), Dock.Top),
                Panels.Docked(bar, Dock.Top),
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = _host,
                },
            },
        };

        // A page can leave the screen without being closed (page switch, theme rebuild, hide to
        // tray); a live key-capture hook must never outlive it.
        DetachedFromVisualTree += (_, _) => CancelRecording();

        ShowAllChords();
        Select(tab);
    }

    /// <summary>The open tab.</summary>
    public SettingsTab Tab { get; private set; }

    /// <summary>Opens <paramref name="tab"/>.</summary>
    public void Select(SettingsTab tab)
    {
        if (tab != SettingsTab.Shortcuts) CancelRecording();
        Tab = tab;
        foreach (var (t, button) in _tabs) Shell.Paint(button, Themes.Active.TabSelection, t == tab);
        _host.Content = _sections[tab];
    }

    /// <summary>Stops a live chord recording, if any, and restores the key labels.</summary>
    public void CancelRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        foreach (var key in _keys.Values) key.IsEngaged = false;
        ShowAllChords();
    }

    // ---- Chord recorder: moved verbatim from SettingsWindow ----

    private int[]? Chord(ShortcutSlot slot) => slot switch
    {
        ShortcutSlot.Raw => _settings.Data.ResolvedPushToTalkKeys,
        ShortcutSlot.Cleanup => _settings.Data.CleanupPushToTalkKeys,
        ShortcutSlot.Undo => _settings.Data.UndoKeys,
        ShortcutSlot.Command => _settings.Data.CommandKeys,
        _ => null,
    };

    private void SaveChord(ShortcutSlot slot, int[]? chord)
    {
        var data = _settings.Data;
        Save(slot switch
        {
            // The raw slot cannot be unbound; a null here never happens (Escape cancels).
            ShortcutSlot.Raw => data with { PushToTalkKeys = chord, PushToTalkKey = chord![0] },
            ShortcutSlot.Cleanup => data with { CleanupPushToTalkKeys = chord },
            ShortcutSlot.Undo => data with { UndoKeys = chord },
            ShortcutSlot.Command => data with { CommandKeys = chord },
            _ => data,
        });
    }

    private void StartRecording(ShortcutSlot slot)
    {
        _recordingSlot = slot;
        _captured.Clear();
        _held.Clear();

        // Events arrive on the hook thread; every touch of the UI below is posted.
        _recorder = PlatformFactory.StartKeyCapture((key, isDown) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnRecordedKey(key, isDown)));

        if (_recorder is null) return; // off Windows, or the hook failed to install

        Recording.IsEngaged = true;
        Recording.Content = "PRESS YOUR KEY(S)…";
    }

    private void OnRecordedKey(int key, bool isDown)
    {
        if (_recorder is null) return;

        if (isDown)
        {
            if (key == VkEscape && _captured.Count == 0)
            {
                // On an optional slot Escape means "unbind"; the raw slot only cancels.
                var slot = _recordingSlot;
                CancelRecording();
                if (slot != ShortcutSlot.Raw) SaveChord(slot, null);
                ShowAllChords();
                return;
            }

            if (!_captured.Contains(key)) _captured.Add(key);
            _held.Add(key);
            Recording.Content = ChordLabel(_captured);
            return;
        }

        _held.Remove(key);

        // The chord is whatever was held together; the last release commits it.
        if (_captured.Count > 0 && _held.Count == 0) CommitRecording();
    }

    private TransportKey Recording => _keys[_recordingSlot];

    private void CommitRecording()
    {
        var chord = _captured.ToArray();
        var slot = _recordingSlot;
        CancelRecording();

        SaveChord(slot, chord);
        ShowAllChords();
    }

    private void ShowAllChords()
    {
        foreach (var (slot, key) in _keys)
        {
            key.Content = Chord(slot) is { Length: > 0 } chord ? ChordLabel(chord) : "NOT BOUND";
        }

        var altGr = _keys.Keys.Any(slot => Chord(slot)?.Contains(VkRightAlt) == true);
        _keyWarning.Text = altGr
            ? "Right Alt is AltGr on many European layouts — binding it here will interfere "
            + "with typing @, €, \\ and |."
            : string.Empty;
        _keyWarning.IsVisible = altGr;
    }

    private static string ChordLabel(IReadOnlyList<int> chord) =>
        string.Join(" + ", chord.Select(PlatformFactory.KeyDisplayName));

    private void Save(SettingsData data) => _settings.Update(data);
}
```

Remove any `using` the compiler reports as unused.

- [ ] **Step 4: Host the page in `MainWindow`**

In `windows/src/VoxScribe.App/Views/MainWindow.cs`:
- Add the field `private SettingsTab _settingsTab = SettingsTab.General;`.
- Replace the whole `ShowPage` method with:

```csharp
    /// <summary>Opens <paramref name="page"/> and marks it in the navigation.</summary>
    public void ShowPage(AppPage page)
    {
        // Leaving Settings: remember the tab and make sure no key-capture hook survives.
        if (_host.Content is SettingsPage leaving)
        {
            _settingsTab = leaving.Tab;
            leaving.CancelRecording();
        }

        _page = page;
        foreach (var (p, item) in _nav) Shell.Paint(item, Themes.Active.NavSelection, p == page);

        _host.Content = page switch
        {
            AppPage.History => HistoryPage(),
            AppPage.Dictionary => DictionaryPage(),
            AppPage.Settings => _composition is null
                ? Panels.EmptyState("SETTINGS", "Nothing to configure without an engine.")
                : new SettingsPage(_composition.Settings, _composition.Engine, _settingsTab),
            _ => new HomePage(_composition?.Transcripts, _composition?.Settings, ShowPage, RetypeAsync),
        };
    }
```

- At the top of `Rebuild()`, before `_historySearch = …`, insert:

```csharp
        // A theme picked on the Appearance tab must come back on the Appearance tab.
        if (_host.Content is SettingsPage open)
        {
            _settingsTab = open.Tab;
            open.CancelRecording();
        }
```

- In the `Closing` handler, insert `(_host.Content as SettingsPage)?.CancelRecording();` just before `Hide();`.

- [ ] **Step 5: Tray → Settings page, and delete the dialog**

In `windows/src/VoxScribe.App/App.axaml.cs`, replace `OnTraySettings` with:

```csharp
    private void OnTraySettings(object? sender, EventArgs e)
    {
        ShowMain();
        _main?.ShowPage(AppPage.Settings);
    }
```

Then delete the dialog and check nothing references it:

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes/windows
git rm src/VoxScribe.App/Views/SettingsWindow.cs
grep -rn "SettingsWindow\|SettingsWidth\|SettingsHeight\|SettingsMin" --include=*.cs src tests
```

Expected: no output.

- [ ] **Step 6: Run the Global Constraints verification**

- [ ] **Step 7: Manual check (record in the task report)**

`dotnet run --project src/VoxScribe.App -c Release`:
- Tray → Settings… opens the window on Settings.
- Record a chord on Shortcuts, then switch to Home mid-recording. Press keys: nothing is recorded (the hook was released).
- Pick a theme on Appearance. The window repaints and stays on Appearance.

- [ ] **Step 8: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
feat: settings becomes a tabbed page of the main window

Six tabs (General, Speech, Shortcuts, Typing, Cleanup, Appearance); the tray
opens the window on Settings. The chord recorder is cancelled whenever the page
leaves the screen. SettingsWindow is gone.
EOF
```

---
### Task 8: Appearance — theme picker and variant swatches, live

Visual spec: the "Accent variants (light · dark)" half-and-half swatches in any `.superpowers/mockups/*-System.dc.html`.

**Files:**
- Replace: `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs`
- Modify: `windows/src/VoxScribe.App/Design/DesignTokens.cs`
- Replace: `windows/tests/VoxScribe.App.Tests/AppearanceSectionTests.cs`

**Interfaces:**
- Consumes:
  - `Themes.All`, `Themes.Find`, `Themes.IsDark`
  - `ThemeDefinition.Light/Dark/Variants/Label/Description/Display/DisplayWeight/CardRadius`
  - `AccentVariant.Id/Label/Light/Dark`
  - `Tokens.Colors.Rgb`
  - `SettingsData.Theme/AccentVariant`
- Produces:
  - `AppearanceSection.Build(AppSettings, Action<SettingsData>, DictationEngine?)`, same signature as before.
  - Theme cards carry the automation name `"Theme: <Label>"`; swatches carry `"Accent: <Label>"`.
- Live repaint already works: save → `AppSettings.Changed` → `App` applies → `Themes.Changed` → `MainWindow.Rebuild` restores Settings/Appearance (Tasks 4 and 7). This task adds no wiring.

- [ ] **Step 1: Write the failing tests**

Replace `windows/tests/VoxScribe.App.Tests/AppearanceSectionTests.cs` with:

```csharp
using System.IO;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Shouldly;
using VoxScribe.App.Views.Settings;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

public sealed class AppearanceSectionTests
{
    private static AppSettings Settings(string theme = "paper", string? variant = null)
    {
        var settings = new AppSettings(Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json"));
        settings.Update(settings.Data with { Theme = theme, AccentVariant = variant });
        return settings;
    }

    private static Button Named(Control section, string name) =>
        section.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Five_theme_cards_in_order()
    {
        var section = AppearanceSection.Build(Settings(), _ => { }, engine: null);

        section.GetLogicalDescendants().OfType<Button>()
            .Select(b => AutomationProperties.GetName(b))
            .Where(n => n?.StartsWith("Theme: ", StringComparison.Ordinal) == true)
            .ShouldBe(["Theme: Paper", "Theme: Orb", "Theme: Tide", "Theme: Mono", "Theme: Fluent"]);
    }

    [AvaloniaFact]
    public void Picking_a_theme_saves_it_and_resets_the_variant()
    {
        var settings = Settings("paper", "moss");
        var section = AppearanceSection.Build(settings, settings.Update, engine: null);

        Click(Named(section, "Theme: Tide"));

        settings.Data.Theme.ShouldBe("tide");
        settings.Data.AccentVariant.ShouldBeNull("a Paper variant means nothing to Tide");
    }

    [AvaloniaFact]
    public void Swatches_are_exactly_the_saved_themes_variants()
    {
        var section = AppearanceSection.Build(Settings("mono"), _ => { }, engine: null);

        section.GetLogicalDescendants().OfType<Button>()
            .Select(b => AutomationProperties.GetName(b))
            .Where(n => n?.StartsWith("Accent: ", StringComparison.Ordinal) == true)
            .ShouldBe(["Accent: Lime", "Accent: Phosphor", "Accent: Cyan", "Accent: Violet", "Accent: Amber"]);
    }

    [AvaloniaFact]
    public void Picking_a_swatch_saves_the_variant()
    {
        var settings = Settings("paper");
        var section = AppearanceSection.Build(settings, settings.Update, engine: null);

        Click(Named(section, "Accent: Moss"));

        settings.Data.Theme.ShouldBe("paper");
        settings.Data.AccentVariant.ShouldBe("moss");
    }
}
```

Run `dotnet test tests/VoxScribe.App.Tests --filter FullyQualifiedName~AppearanceSectionTests`. Expected: the swatch tests FAIL, because there are no `Accent:` buttons yet.

- [ ] **Step 2: Tokens**

Add to `Material` in `windows/src/VoxScribe.App/Design/DesignTokens.cs`:

```csharp
        /// <summary>A theme card in Appearance.</summary>
        public const double ThemeCardWidth = 150;

        /// <summary>The mini preview at the top of a theme card.</summary>
        public const double ThemePreviewHeight = 56;

        /// <summary>Largest corner a theme preview takes from its theme.</summary>
        public const double ThemePreviewRadius = 8;

        /// <summary>The accent bar inside a theme preview: thickness.</summary>
        public const double ThemePreviewBar = 4;

        /// <summary>The accent bar inside a theme preview: length.</summary>
        public const double ThemePreviewBarWidth = 40;
```

`SwatchSize` (30) already exists and is reused for the variant swatches.

- [ ] **Step 3: Replace `windows/src/VoxScribe.App/Views/Settings/AppearanceSection.cs`**

```csharp
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>
/// Theme cards and the saved theme's accent swatches. A pick is saved; App re-applies the
/// theme and the window rebuilds on the same tab, so the selection marks are always fresh.
/// </summary>
internal static class AppearanceSection
{
    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save, DictationEngine? engine = null)
    {
        var saved = Themes.Find(settings.Data.Theme);
        var savedVariant = saved.Variants.FirstOrDefault(
            v => string.Equals(v.Id, settings.Data.AccentVariant, StringComparison.OrdinalIgnoreCase)) ?? saved.Variants[0];

        var cards = new WrapPanel { ItemSpacing = Tokens.Space.Base, LineSpacing = Tokens.Space.Base };
        foreach (var theme in Themes.All)
        {
            cards.Children.Add(ThemeCard(theme, ReferenceEquals(theme, saved), () =>
            {
                if (IsBusy(engine)) return;
                save(settings.Data with { Theme = theme.Id, AccentVariant = null });
            }));
        }

        var swatches = new WrapPanel { ItemSpacing = Tokens.Space.Base, LineSpacing = Tokens.Space.Base };
        foreach (var variant in saved.Variants)
        {
            swatches.Children.Add(Swatch(variant, ReferenceEquals(variant, savedVariant), () =>
            {
                if (IsBusy(engine)) return;
                save(settings.Data with { Theme = saved.Id, AccentVariant = variant.Id });
            }));
        }

        return Panels.Section("APPEARANCE", new StackPanel
        {
            Spacing = Tokens.Space.Base,
            Children =
            {
                Panels.Labelled("THEME", cards),
                Panels.Labelled("ACCENT", swatches),
                Panels.Note("Applies immediately. Light or dark follows Windows "
                    + "(Settings → Personalisation → Colours)."),
            },
        });
    }

    // A rebuild mid-dictation would be harmless, but a pick then would surprise; wait for idle.
    private static bool IsBusy(DictationEngine? engine) => engine is { State: not DictationState.Idle };

    private static Button ThemeCard(ThemeDefinition theme, bool selected, Action pick)
    {
        // The preview shows the theme in the mode Windows is in now, with its first accent.
        var palette = Themes.IsDark ? theme.Dark : theme.Light;
        var accent = Tokens.Colors.Rgb(Themes.IsDark ? theme.Variants[0].Dark : theme.Variants[0].Light);

        var preview = new Border
        {
            Height = Tokens.Material.ThemePreviewHeight,
            CornerRadius = new CornerRadius(Math.Min(theme.CardRadius, Tokens.Material.ThemePreviewRadius)),
            Background = new SolidColorBrush(Tokens.Colors.Rgb(palette.Ground)),
            BorderBrush = new SolidColorBrush(Tokens.Colors.Rgb(palette.Border)),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            Padding = new Thickness(Tokens.Space.Snug),
            Child = new StackPanel
            {
                Spacing = Tokens.Space.Tight,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Aa",
                        FontFamily = theme.Display,
                        FontWeight = theme.DisplayWeight,
                        FontSize = Tokens.Fonts.Row,
                        Foreground = new SolidColorBrush(Tokens.Colors.Rgb(palette.Ink)),
                    },
                    new Border
                    {
                        Height = Tokens.Material.ThemePreviewBar,
                        Width = Tokens.Material.ThemePreviewBarWidth,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        CornerRadius = new CornerRadius(Tokens.Space.Hair),
                        Background = new SolidColorBrush(accent),
                    },
                },
            },
        };

        var card = new Button
        {
            Width = Tokens.Material.ThemeCardWidth,
            Padding = new Thickness(Tokens.Space.Snug),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(Tokens.Border.Ring),
            BorderBrush = selected ? Tokens.Brushes.Accent : Brushes.Transparent,
            CornerRadius = new CornerRadius(Tokens.Radius.Panel),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = Tokens.Space.Tight,
                Children =
                {
                    preview,
                    new TextBlock
                    {
                        Text = theme.Label,
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Body,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Tokens.Brushes.Ink,
                    },
                    new TextBlock
                    {
                        Text = theme.Description,
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Label,
                        Foreground = Tokens.Brushes.InkSecondary,
                    },
                },
            },
        };
        AutomationProperties.SetName(card, $"Theme: {theme.Label}");
        card.Click += (_, _) => pick();
        return card;
    }

    private static Button Swatch(AccentVariant variant, bool selected, Action pick)
    {
        var half = Tokens.Material.SwatchSize / 2;
        var dot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new Border
                {
                    Width = half, Height = Tokens.Material.SwatchSize,
                    CornerRadius = new CornerRadius(half, 0, 0, half),
                    Background = new SolidColorBrush(Tokens.Colors.Rgb(variant.Light)),
                },
                new Border
                {
                    Width = half, Height = Tokens.Material.SwatchSize,
                    CornerRadius = new CornerRadius(0, half, half, 0),
                    Background = new SolidColorBrush(Tokens.Colors.Rgb(variant.Dark)),
                },
            },
        };

        var swatch = new Button
        {
            Padding = new Thickness(Tokens.Space.Tight),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(Tokens.Border.Ring),
            BorderBrush = selected ? Tokens.Brushes.Ink : Brushes.Transparent,
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            Content = new StackPanel
            {
                Spacing = Tokens.Space.Tight,
                Children =
                {
                    dot,
                    new TextBlock
                    {
                        Text = variant.Label,
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Label,
                        Foreground = Tokens.Brushes.InkSecondary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
        AutomationProperties.SetName(swatch, $"Accent: {variant.Label}");
        ToolTip.SetTip(swatch, $"{variant.Label} — left half light mode, right half dark mode");
        swatch.Click += (_, _) => pick();
        return swatch;
    }
}
```

- [ ] **Step 4: Run the Global Constraints verification**

- [ ] **Step 5: Manual check (record in the task report)**

`dotnet run --project src/VoxScribe.App -c Release`. Then:
- Settings → Appearance: click each of the five themes. Each repaints the window instantly and stays on Appearance.
- Click each swatch: the accent changes.
- Flip Windows light/dark: everything follows.

- [ ] **Step 6: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
feat: Appearance picks one of five themes and its accent variant, live

Theme cards preview each theme in the current Windows mode; swatches show each
variant's light and dark value side by side.
EOF
```

---

### Task 9: Pill framework, plus the Paper, Tide and Fluent silhouettes

Visual spec: `.superpowers/mockups/Paper-Pill.dc.html`, `Tide-Pill.dc.html` and `Fluent-Pill.dc.html`. Each shows Recording / Streaming / Cleaning-up, in light and dark. Sizes, paddings, shadows and font sizes below are copied from them. They are private constants of each face, which is the token rule's own-arithmetic exception.

**Files:**
- Replace: `windows/src/VoxScribe.App/Views/HudWindow.cs`. Keep the `internal static class Overlay` block at the bottom of the current file **unchanged**, copied as-is.
- Create: `windows/src/VoxScribe.App/Views/Pill/PillFace.cs`
- Create: `windows/src/VoxScribe.App/Views/Pill/PaperPill.cs`
- Create: `windows/src/VoxScribe.App/Views/Pill/TidePill.cs`
- Create: `windows/src/VoxScribe.App/Views/Pill/FluentPill.cs`
- Modify: `windows/src/VoxScribe.App/Design/DesignTokens.cs`, `windows/src/VoxScribe.App/Design/Themes.cs`
- Test: `windows/tests/VoxScribe.App.Tests/PillTests.cs` (create)

**Interfaces:**
- Consumes:
  - `Themes.Active.Pill` (`PillKind`), `Themes.IsDark`, `Themes.Changed`
  - `Tokens.Colors.PillFill/PillEdge/PillRule/Accent/AccentTint/AccentFill/OnAccent/Record`
  - `Tokens.Radius.Pill/Panel`
  - `Tokens.Fonts.*`, `Panels.Docked`
- Produces. Task 10 relies on these exact names:
  - `internal enum PillPhase { Recording, Working, Notice, Latency }`
  - `internal readonly record struct PillState(PillPhase Phase, double Level, TimeSpan Elapsed, string Mode, string Text)`. `Level` is 0–1, already perceptual. `Mode` is `"RAW"`, `"CLEAN"` or `"CMD"`. `Elapsed` is the latency in the Latency phase. `Text` is the full preview or notice; faces show only its tail.
  - `internal abstract class PillFace : Border` with:
    - `abstract Control RecordDot { get; }`
    - `abstract void Update(PillState state)`
    - `static PillFace Create(PillKind kind)`
    - `internal static StreamGeometry Arc(Point centre, double radius, double startDegrees, double sweepDegrees)`
    - protected helpers `Tail`, `Clock`, `Seconds`, `Timer`, `Dot`, `Text`
  - `Tokens.Size.PillWindowWidth` = 520, `Tokens.Size.PillWindowHeight` = 180, `Tokens.Motion.PillExpand` = 150 ms.
  - Interim: `PillFace.Create` maps `Orb` and `Mono` to `FluentPill` until Task 10.

- [ ] **Step 1: Write the failing tests**

Create `windows/tests/VoxScribe.App.Tests/PillTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Pill;

namespace VoxScribe.AppTests;

/// <summary>The pill's doctrine, for every theme in both modes.</summary>
public sealed class PillTests
{
    private static readonly string LongText =
        string.Join(' ', Enumerable.Repeat("on se retrouve jeudi pour la revue du sprint", 30));

    private static void EachTheme(Action<ThemeDefinition, PillFace> check)
    {
        try
        {
            foreach (var dark in new[] { false, true })
            foreach (var theme in Themes.All)
            {
                Themes.Apply(theme.Id, null, dark);
                var face = PillFace.Create(theme.Pill);
                var window = new Window { Content = face, Width = Tokens.Size.PillWindowWidth, Height = Tokens.Size.PillWindowHeight };
                window.Show();
                check(theme, face);
                window.Close();
            }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }

    [AvaloniaFact]
    public void The_red_dot_shows_only_while_recording() => EachTheme((theme, face) =>
    {
        foreach (var phase in Enum.GetValues<PillPhase>())
        {
            face.Update(new PillState(phase, 0.6, TimeSpan.FromSeconds(4), "RAW", "bonjour"));
            face.RecordDot.IsVisible.ShouldBe(phase == PillPhase.Recording, $"{theme.Id} {phase}");
        }
    });

    [AvaloniaFact]
    public void A_long_preview_never_makes_the_pill_taller() => EachTheme((theme, face) =>
    {
        face.Update(new PillState(PillPhase.Recording, 0.3, TimeSpan.FromSeconds(2), "RAW", "court"));
        face.Measure(Avalonia.Size.Infinity);
        var height = face.DesiredSize.Height;

        face.Update(new PillState(PillPhase.Recording, 0.3, TimeSpan.FromSeconds(9), "RAW", LongText));
        face.Measure(Avalonia.Size.Infinity);

        face.DesiredSize.Height.ShouldBe(height, theme.Id);
    });

    [AvaloniaFact]
    public void Faces_never_take_focus_or_clicks() => EachTheme((theme, face) =>
    {
        face.Focusable.ShouldBeFalse(theme.Id);
        face.IsHitTestVisible.ShouldBeFalse(theme.Id);
    });

    [AvaloniaFact]
    public void Nothing_in_the_pill_is_red_but_the_dot() => EachTheme((theme, face) =>
    {
        face.Update(new PillState(PillPhase.Recording, 0.8, TimeSpan.FromSeconds(1), "CLEAN", "texte"));
        var red = Tokens.Colors.Record;
        var painted = face.GetVisualDescendants().OfType<Control>().Where(c =>
            (c is Shape { Fill: ISolidColorBrush f } && f.Color == red) ||
            (c is Border { Background: ISolidColorBrush b } && b.Color == red));

        painted.ShouldAllBe(c => ReferenceEquals(c, face.RecordDot), theme.Id);
    });
}
```

Run `dotnet test tests/VoxScribe.App.Tests --filter FullyQualifiedName~PillTests`. Expected: compile error, the `VoxScribe.App.Views.Pill` namespace does not exist.

- [ ] **Step 2: Tokens and theme cleanup**

In `windows/src/VoxScribe.App/Design/DesignTokens.cs`:
- Delete `Colors.Glass`, `Colors.RecordIdle`, `Material.GlassEdgeOpacity`, `Material.PillLampSize`, `Material.PillBarsHeight`, `Material.PillRadius`, `Size.PillWidth`, `Size.PillCompactHeight` and `Size.PillPreviewHeight`.
- Add to `Size`:

```csharp
        /// <summary>
        /// The pill's OS window: fixed, transparent and click-through, with room for the widest
        /// face, Orb's halos (1.5 × 76 px), Paper's tilt and the Mono/Fluent shadows. The face
        /// animates inside it; the window never resizes.
        /// </summary>
        public const double PillWindowWidth = 520;

        /// <summary>The pill's OS window height. See <see cref="PillWindowWidth"/>.</summary>
        public const double PillWindowHeight = 180;
```

- Add to `Motion`:

```csharp
        /// <summary>The pill widening for the preview line, and narrowing back.</summary>
        public static TimeSpan PillExpand { get; } = TimeSpan.FromMilliseconds(150);
```

In `windows/src/VoxScribe.App/Design/Themes.cs` `Apply`, delete the two lines that set `Tokens.Colors.Glass` and `Tokens.Colors.RecordIdle`.

- [ ] **Step 3: Create `windows/src/VoxScribe.App/Views/Pill/PillFace.cs`**

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>What the pill is showing.</summary>
internal enum PillPhase
{
    /// <summary>The key is held: red dot, live level.</summary>
    Recording,

    /// <summary>Transcribing / cleaning the tail.</summary>
    Working,

    /// <summary>A failure notice lingering after the engine idled.</summary>
    Notice,

    /// <summary>The felt latency lingering after a clean finish.</summary>
    Latency,
}

/// <summary>One display frame for a pill face.</summary>
/// <param name="Phase">What is happening.</param>
/// <param name="Level">Input level, 0–1, already perceptual (gain + sqrt).</param>
/// <param name="Elapsed">Utterance time; the latency in <see cref="PillPhase.Latency"/>.</param>
/// <param name="Mode">"RAW", "CLEAN" or "CMD".</param>
/// <param name="Text">Preview or notice text, untruncated; faces show its tail on one line.</param>
internal readonly record struct PillState(PillPhase Phase, double Level, TimeSpan Elapsed, string Mode, string Text);

/// <summary>
/// A theme's pill silhouette. Built fresh for each theme (it caches the theme's brushes),
/// fed one <see cref="PillState"/> per frame by <see cref="HudWindow"/>.
/// </summary>
/// <remarks>
/// Never focusable, never hit-testable. Only the level visual moves, plus a
/// <see cref="Tokens.Motion.PillExpand"/> width transition when the preview line appears.
/// </remarks>
internal abstract class PillFace : Border
{
    /// <summary>Sets the doctrine every face shares.</summary>
    protected PillFace()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, Tokens.Space.Wide);
        Focusable = false;
        IsHitTestVisible = false;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = WidthProperty, Duration = Tokens.Motion.PillExpand },
        };
    }

    /// <summary>The recording dot. Visible only in <see cref="PillPhase.Recording"/>.</summary>
    public abstract Control RecordDot { get; }

    /// <summary>Paints one frame.</summary>
    public abstract void Update(PillState state);

    /// <summary>The face for <paramref name="kind"/>, in the active theme.</summary>
    public static PillFace Create(PillKind kind) => kind switch
    {
        PillKind.Paper => new PaperPill(),
        PillKind.Tide => new TidePill(),
        _ => new FluentPill(), // Orb and Mono arrive in Task 10.
    };

    /// <summary>A circular arc, clockwise from <paramref name="startDegrees"/> (0 = 3 o'clock).</summary>
    internal static StreamGeometry Arc(Point centre, double radius, double startDegrees, double sweepDegrees)
    {
        static Point On(Point c, double r, double degrees)
        {
            var a = degrees * Math.PI / 180;
            return new Point(c.X + (r * Math.Cos(a)), c.Y + (r * Math.Sin(a)));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(On(centre, radius, startDegrees), false);
            ctx.ArcTo(On(centre, radius, startDegrees + sweepDegrees), new Size(radius, radius), 0,
                sweepDegrees > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        return geometry;
    }

    /// <summary>The last <paramref name="max"/> characters, led by an ellipsis when cut.</summary>
    protected static string Tail(string text, int max) => text.Length <= max ? text : "…" + text[^max..];

    /// <summary>"0:04".</summary>
    protected static string Clock(TimeSpan t) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}:{t.Seconds:00}");

    /// <summary>"1.2s".</summary>
    protected static string Seconds(TimeSpan t) =>
        string.Create(CultureInfo.InvariantCulture, $"{t.TotalSeconds:0.0}s");

    /// <summary>The timer slot: the latency once done, the clock otherwise.</summary>
    protected static string Timer(PillState s) => s.Phase == PillPhase.Latency ? Seconds(s.Elapsed) : Clock(s.Elapsed);

    /// <summary>A round recording dot in the record red.</summary>
    protected static Ellipse Dot(double size) => new()
    {
        Width = size,
        Height = size,
        Fill = Tokens.Brushes.Record,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A one-line label that never wraps (the pill must not grow).</summary>
    protected static TextBlock Text(FontFamily family, double size, IBrush brush) => new()
    {
        FontFamily = family,
        FontSize = size,
        Foreground = brush,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.None,
        ClipToBounds = true,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
```

- [ ] **Step 4: Create `windows/src/VoxScribe.App/Views/Pill/PaperPill.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>Paper: a paper slip tilted −0.6°, the level written as a pen stroke on a ruled line.</summary>
internal sealed class PaperPill : PillFace
{
    private const double CompactWidth = 320;
    private const double PreviewWidth = 400;
    private const double Tilt = -0.6;
    private const double DotSize = 9;
    private const double HeadSize = 17;
    private const double MetaSize = 11;
    private const double StrokeHeight = 34;
    private const double PreviewSize = 20;
    private const double PreviewLine = 30;
    private const double WorkingInkRatio = 0.62;
    private const int PreviewChars = 40;
    private const uint ShadowArgb = 0x8C1E1408;
    private const double ShadowY = 14;
    private const double ShadowBlur = 30;
    private const double ShadowSpread = -16;

    private readonly Ellipse _dot = Dot(DotSize);
    private readonly TextBlock _head;
    private readonly TextBlock _meta;
    private readonly TextBlock _preview;
    private readonly PenStroke _stroke = new() { Height = StrokeHeight };
    private readonly Border _working;
    private readonly IBrush _ink = Tokens.Brushes.Ink;
    private readonly IBrush _accent = Tokens.Brushes.Accent;

    /// <summary>Builds the slip in the active theme.</summary>
    public PaperPill()
    {
        Width = CompactWidth;
        Background = new SolidColorBrush(Tokens.Colors.PillFill);
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        BoxShadow = new BoxShadows(
            new BoxShadow { OffsetY = Tokens.Border.Hairline, Color = Tokens.Colors.PillEdge },
            [new BoxShadow { OffsetY = ShadowY, Blur = ShadowBlur, Spread = ShadowSpread, Color = Color.FromUInt32(ShadowArgb) }]);
        Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Base, Tokens.Space.Roomy, Tokens.Space.Base);
        RenderTransform = new RotateTransform(Tilt);

        _head = Text(Tokens.Fonts.Display, HeadSize, _ink);
        _head.FontStyle = FontStyle.Italic;
        _meta = Text(Tokens.Fonts.Mono, MetaSize, Tokens.Brushes.InkSecondary);
        _preview = Text(Tokens.Fonts.Display, PreviewSize, _ink);
        _preview.FontStyle = FontStyle.Italic;
        _preview.Height = PreviewLine;
        _preview.IsVisible = false;

        _working = new Border
        {
            Height = Tokens.Border.Ring,
            Width = CompactWidth * WorkingInkRatio,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = _accent,
            IsVisible = false,
        };

        var head = new DockPanel();
        head.Children.Add(Panels.Docked(_meta, Dock.Right));
        head.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children = { _dot, _head },
        });

        Child = new StackPanel
        {
            Spacing = Tokens.Space.Tight,
            Children =
            {
                head,
                _stroke,
                _preview,
                new Border { Height = Tokens.Border.Hairline, Background = new SolidColorBrush(Tokens.Colors.PillRule) },
                _working,
            },
        };
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        _dot.IsVisible = recording;
        _head.Text = state.Phase switch
        {
            PillPhase.Recording => "listening",
            PillPhase.Working => "tidying…",
            PillPhase.Notice => "note",
            _ => "done",
        };
        _head.Foreground = state.Phase == PillPhase.Working ? _accent : _ink;
        _meta.Text = $"{Timer(state)} · {(state.Mode switch { "CLEAN" => "clean", "CMD" => "cmd", _ => "raw" })}";
        _stroke.Push(recording ? state.Level : 0, flat: !recording);
        _working.IsVisible = state.Phase == PillPhase.Working;

        var preview = Tail(state.Text, PreviewChars);
        _preview.Text = preview;
        _preview.IsVisible = preview.Length > 0;
        Width = preview.Length > 0 ? PreviewWidth : CompactWidth;
    }
}

/// <summary>The level as a pen stroke: a scrolling history, newest on the right.</summary>
internal sealed class PenStroke : Control
{
    private const int Points = 50;
    private const double Amplitude = 20;
    private const double FlatAmplitude = 2;
    private const double Baseline = 0.7;
    private const double StrokeWidth = 1.6;
    private const double MinEnvelope = 0.15;
    private const double MinorTick = 0.35;
    private const double JitterFastPitch = 2.2;
    private const double JitterSlowPitch = 0.66;
    private const double JitterFastWeight = 0.6;
    private const double JitterSlowWeight = 0.4;

    private readonly double[] _history = new double[Points];
    private readonly Pen _pen = new(Tokens.Brushes.Ink, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private bool _flat;

    /// <summary>Feeds one frame.</summary>
    public void Push(double level, bool flat)
    {
        Array.Copy(_history, 1, _history, 0, Points - 1);
        _history[^1] = level;
        _flat = flat;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0) return;

        var baseline = Bounds.Height * Baseline;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, baseline), false);
            for (var i = 1; i < Points; i++)
            {
                var t = (double)i / (Points - 1);
                var envelope = Math.Max(MinEnvelope, Math.Sin(Math.PI * t));
                var jitter = Math.Abs((Math.Sin(i * JitterFastPitch) * JitterFastWeight) + (Math.Sin(i * JitterSlowPitch) * JitterSlowWeight));
                var amplitude = _flat ? FlatAmplitude : Amplitude * _history[i];
                var y = baseline - (amplitude * envelope * jitter * (i % 2 == 0 ? 1 : MinorTick));
                ctx.LineTo(new Point(width * t, y));
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, _pen, geometry);
    }
}
```

- [ ] **Step 5: Create `windows/src/VoxScribe.App/Views/Pill/TidePill.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>Tide: a soft capsule with a liquid wave and its fill.</summary>
internal sealed class TidePill : PillFace
{
    private const double CompactWidth = 300;
    private const double PreviewWidth = 400;
    private const double DotSize = 10;
    private const double RowHeight = 30;
    private const double MetaSize = 12;
    private const double BadgeSize = 11;
    private const double WorkingSize = 13;
    private const double PreviewSize = 15;
    private const int PreviewChars = 46;
    private const uint ShadowArgb = 0x660A283C;
    private const double ShadowY = 16;
    private const double ShadowBlur = 36;
    private const double ShadowSpread = -14;

    private readonly Ellipse _dot = Dot(DotSize);
    private readonly TextBlock _working;
    private readonly LiquidWave _wave = new() { Height = RowHeight };
    private readonly TextBlock _timer;
    private readonly TextBlock _badge;
    private readonly TextBlock _preview;

    /// <summary>Builds the capsule in the active theme.</summary>
    public TidePill()
    {
        Width = CompactWidth;
        Background = new SolidColorBrush(Tokens.Colors.PillFill);
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetY = ShadowY, Blur = ShadowBlur, Spread = ShadowSpread, Color = Color.FromUInt32(ShadowArgb),
        });
        Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Snug);

        _working = Text(Tokens.Fonts.Grotesque, WorkingSize, Tokens.Brushes.Accent);
        _working.FontWeight = FontWeight.Bold;
        _working.Text = "Smoothing it out";
        _timer = Text(Tokens.Fonts.Grotesque, MetaSize, Tokens.Brushes.InkSecondary);
        _timer.FontWeight = FontWeight.SemiBold;
        _badge = Text(Tokens.Fonts.Grotesque, BadgeSize, Tokens.Brushes.Accent);
        _badge.FontWeight = FontWeight.Bold;
        _preview = Text(Tokens.Fonts.Grotesque, PreviewSize, Tokens.Brushes.Ink);
        _preview.FontWeight = FontWeight.Medium;
        _preview.IsVisible = false;

        var chip = new Border
        {
            Background = new SolidColorBrush(Tokens.Colors.AccentTint),
            CornerRadius = new CornerRadius(Tokens.Radius.Pill),
            Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Hair),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = _badge,
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = Tokens.Space.Base,
            Height = RowHeight,
        };
        Control[] cells = [_dot, _working, _wave, _timer, chip];
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            row.Children.Add(cells[i]);
        }

        Child = new StackPanel { Spacing = Tokens.Space.Tight, Children = { row, _preview } };
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        var working = state.Phase == PillPhase.Working;
        _dot.IsVisible = recording;
        _working.IsVisible = working;
        _wave.Push(recording ? state.Level : 0, working);
        _timer.Text = Timer(state);
        _badge.Text = state.Mode switch { "CLEAN" => "Clean", "CMD" => "Command", _ => "Raw" };

        var preview = Tail(state.Text, PreviewChars);
        _preview.Text = preview;
        _preview.IsVisible = preview.Length > 0;
        Width = preview.Length > 0 ? PreviewWidth : CompactWidth;
        CornerRadius = new CornerRadius(preview.Length > 0 ? Tokens.Radius.Panel : Tokens.Radius.Pill);
    }
}

/// <summary>A liquid wave whose height follows the level, with a translucent fill beneath.</summary>
internal sealed class LiquidWave : Control
{
    private const double MaxAmplitude = 11;
    private const double WorkingAmplitude = 2;
    private const double Frequency = 0.16;
    private const double WorkingFrequency = 0.08;
    private const double SecondScale = 0.64;
    private const double SecondFrequency = 0.11;
    private const double SecondPhase = 2.1;
    private const double PhaseStep = 0.4;
    private const double Smoothing = 0.35;
    private const double Step = 4;
    private const double FillOpacity = 0.16;
    private const double SecondOpacity = 0.35;
    private const double MainStroke = 2.4;
    private const double SecondStroke = 1.6;

    private double _amplitude;
    private double _phase;
    private bool _working;

    /// <summary>Feeds one frame.</summary>
    public void Push(double level, bool working)
    {
        _working = working;
        var target = working ? WorkingAmplitude : MaxAmplitude * level;
        _amplitude += (target - _amplitude) * Smoothing;
        _phase += PhaseStep;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var mid = height / 2;
        var frequency = _working ? WorkingFrequency : Frequency;
        var accent = Tokens.Colors.Accent;

        context.DrawGeometry(new SolidColorBrush(accent, FillOpacity), null,
            Wave(width, height, mid, _amplitude, frequency, _phase, closed: true));
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent), MainStroke, lineCap: PenLineCap.Round),
            Wave(width, height, mid, _amplitude, frequency, _phase, closed: false));
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent, SecondOpacity), SecondStroke, lineCap: PenLineCap.Round),
            Wave(width, height, mid, _amplitude * SecondScale, SecondFrequency, _phase + SecondPhase, closed: false));
    }

    private static StreamGeometry Wave(
        double width, double height, double mid, double amplitude, double frequency, double phase, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, mid), closed);
            for (var x = Step; x <= width; x += Step)
            {
                var envelope = Math.Sin(Math.PI * x / width);
                ctx.LineTo(new Point(x, mid + (amplitude * envelope * Math.Sin((x * frequency) + phase))));
            }

            if (closed)
            {
                ctx.LineTo(new Point(width, height));
                ctx.LineTo(new Point(0, height));
            }

            ctx.EndFigure(closed);
        }

        return geometry;
    }
}
```

- [ ] **Step 6: Create `windows/src/VoxScribe.App/Views/Pill/FluentPill.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>Fluent: a native Win11 flyout with an accent mic button whose halo follows the level.</summary>
internal sealed class FluentPill : PillFace
{
    private const double CompactWidth = 320;
    private const double PreviewWidth = 420;
    private const double DotSize = 10;
    private const double DotRing = 2;
    private const double TitleSize = 14;
    private const double MetaSize = 12;
    private const double LineSize = 13;
    private const int PreviewChars = 48;
    private const uint NearShadow = 0x24000000;
    private const uint FarShadow = 0x1F000000;
    private const double NearY = 8;
    private const double NearBlur = 16;
    private const double FarBlur = 2;

    private readonly MicHalo _mic = new();
    private readonly Ellipse _dot = Dot(DotSize);
    private readonly TextBlock _title;
    private readonly TextBlock _meta;
    private readonly TextBlock _line;
    private readonly IBrush _ink = Tokens.Brushes.Ink;
    private readonly IBrush _muted = Tokens.Brushes.InkSecondary;

    /// <summary>Builds the flyout in the active theme.</summary>
    public FluentPill()
    {
        Width = CompactWidth;
        Background = new SolidColorBrush(Tokens.Colors.PillFill);
        BorderBrush = new SolidColorBrush(Tokens.Colors.PillEdge);
        BorderThickness = new Thickness(Tokens.Border.Hairline);
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        BoxShadow = new BoxShadows(
            new BoxShadow { OffsetY = NearY, Blur = NearBlur, Color = Color.FromUInt32(NearShadow) },
            [new BoxShadow { Blur = FarBlur, Color = Color.FromUInt32(FarShadow) }]);
        Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Snug, Tokens.Space.Base, Tokens.Space.Snug);

        _dot.Stroke = new SolidColorBrush(Tokens.Colors.PillFill);
        _dot.StrokeThickness = DotRing;
        _dot.HorizontalAlignment = HorizontalAlignment.Right;
        _dot.VerticalAlignment = VerticalAlignment.Top;
        _dot.Margin = new Thickness(0, Tokens.Space.Tight, Tokens.Space.Tight, 0);

        _title = Text(Tokens.Fonts.Grotesque, TitleSize, _ink);
        _title.FontWeight = FontWeight.SemiBold;
        _meta = Text(Tokens.Fonts.Grotesque, MetaSize, _muted);
        _line = Text(Tokens.Fonts.Grotesque, LineSize, _muted);

        var head = new DockPanel();
        head.Children.Add(Panels.Docked(_meta, Dock.Right));
        head.Children.Add(_title);

        var body = new StackPanel { Spacing = Tokens.Space.Hair, VerticalAlignment = VerticalAlignment.Center, Children = { head, _line } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = Tokens.Space.Base };
        row.Children.Add(new Grid { Children = { _mic, _dot } });
        Grid.SetColumn(body, 1);
        row.Children.Add(body);
        Child = row;
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        var working = state.Phase == PillPhase.Working;
        _dot.IsVisible = recording;
        _mic.Push(recording ? state.Level : 0, working);
        _title.Text = state.Phase switch
        {
            PillPhase.Recording => "Listening…",
            PillPhase.Working => "Cleaning up…",
            PillPhase.Notice => "Notice",
            _ => "Done",
        };
        _meta.Text = $"{Timer(state)} · {(state.Mode switch { "CLEAN" => "Cleaned", "CMD" => "Command", _ => "Raw" })}";

        var preview = Tail(state.Text, PreviewChars);
        _line.Text = preview.Length > 0 ? preview : working ? "Polishing the transcript" : "Speak now — release to type";
        _line.Foreground = preview.Length > 0 ? _ink : _muted;
        Width = preview.Length > 0 ? PreviewWidth : CompactWidth;
    }
}

/// <summary>The accent mic button and its level halo; a spinner while working.</summary>
internal sealed class MicHalo : Control
{
    private const double Box = 48;
    private const double ButtonSize = 34;
    private const double HaloMin = 34;
    private const double HaloRange = 14;
    private const double HaloOpacity = 0.22;
    private const double SpinnerSize = 44;
    private const double SpinnerStroke = 2.5;
    private const double SpinnerSweep = 180;
    private const double SpinStep = 12;
    private const double GlyphBox = 16;
    private const double GlyphStroke = 1.3;
    private const string GlyphData =
        "M8,1.5 A2.5,2.5 0 0 1 10.5,4 V7 A2.5,2.5 0 0 1 5.5,7 V4 A2.5,2.5 0 0 1 8,1.5 Z "
        + "M3.5,7.5 A4.5,4.5 0 0 0 12.5,7.5 M8,12 V14.5";

    private readonly Geometry _glyph = Geometry.Parse(GlyphData);
    private double _level;
    private double _spin;
    private bool _working;

    /// <summary>Creates the 48 px mic area.</summary>
    public MicHalo()
    {
        Width = Box;
        Height = Box;
    }

    /// <summary>Feeds one frame.</summary>
    public void Push(double level, bool working)
    {
        _level = level;
        _working = working;
        if (working) _spin = (_spin + SpinStep) % 360;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var halo = (HaloMin + (HaloRange * _level)) / 2;
        context.DrawEllipse(new SolidColorBrush(Tokens.Colors.Accent, HaloOpacity), null, centre, halo, halo);

        if (_working)
        {
            context.DrawGeometry(null, new Pen(Tokens.Brushes.Accent, SpinnerStroke, lineCap: PenLineCap.Round),
                PillFace.Arc(centre, SpinnerSize / 2, _spin, SpinnerSweep));
        }

        context.DrawEllipse(Tokens.Brushes.AccentFill, null, centre, ButtonSize / 2, ButtonSize / 2);
        using (context.PushTransform(Matrix.CreateTranslation(centre.X - (GlyphBox / 2), centre.Y - (GlyphBox / 2))))
        {
            context.DrawGeometry(null, new Pen(Tokens.Brushes.OnAccent, GlyphStroke, lineCap: PenLineCap.Round), _glyph);
        }
    }
}
```

- [ ] **Step 7: Replace `windows/src/VoxScribe.App/Views/HudWindow.cs`**

Write the class below. Then paste the existing `internal static class Overlay { … }` block, with its doc comment, unchanged from the current file below it, and keep `using System.Runtime.InteropServices;`.

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Pill;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// The dictation pill: a fixed, transparent, click-through window at the bottom of the screen
/// hosting the active theme's <see cref="PillFace"/>. Hidden when idle.
/// </summary>
/// <remarks>
/// <para>
/// <b>This window must never take focus.</b> The text lands wherever the caret is, so
/// activating the overlay would redirect the injection into nothing. Hence
/// <see cref="Window.ShowActivated"/> false, nothing focusable, hit-testing off, and
/// <see cref="Overlay.MakeOverlay"/>'s NOACTIVATE / TRANSPARENT styles.
/// </para>
/// <para>
/// Polled at ~30 fps, not pushed: the engine raises Changed at buffer rate on a worker thread.
/// The window never resizes; the face animates inside it. Plain transparency, no acrylic —
/// system blur backs the whole rectangle.
/// </para>
/// </remarks>
public sealed class HudWindow : Window
{
    private readonly DictationEngine _engine;
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _noticeClock = new();
    private readonly Stopwatch _latencyClock = new();
    private readonly DispatcherTimer _timerTick;

    private PillFace _face;
    private bool _faceStale;
    private TimeSpan? _lastLatency;
    private int _frames;

    /// <summary>Builds the pill over <paramref name="engine"/> and starts watching it.</summary>
    public HudWindow(DictationEngine engine)
    {
        _engine = engine;

        // Raised on a worker thread; a torn TimeSpan? read would only garble one frame.
        engine.Completed += (_, result) => _lastLatency = result.ProcessingTime;

        Width = Tokens.Size.PillWindowWidth;
        Height = Tokens.Size.PillWindowHeight;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;

        _face = PillFace.Create(Themes.Active.Pill);
        Content = _face;

        // Swapped only while hidden: a theme change never repaints a dictation in progress.
        Themes.Changed += (_, _) => _faceStale = true;

        _timerTick = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tokens.Motion.PillFrame };
        _timerTick.Tick += (_, _) => Sync();
        _timerTick.Start();
    }

    private string Mode() =>
        _engine.CommandThisUtterance ? "CMD" : _engine.CleaningThisUtterance ? "CLEAN" : "RAW";

    private void Sync()
    {
        var state = _engine.State;

        if (state == DictationState.Idle)
        {
            // A failure notice holds the pill up briefly: the moment the user looks here is the
            // moment their text failed to appear.
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

            // After a clean finish, hold the pill long enough to read the felt latency.
            if (_lastLatency is { } latency)
            {
                if (!_latencyClock.IsRunning) _latencyClock.Restart();
                if (IsVisible && _latencyClock.Elapsed < Tokens.Motion.LatencyLinger)
                {
                    _face.Update(new PillState(PillPhase.Latency, 0, latency, Mode(), string.Empty));
                    return;
                }

                _lastLatency = null;
            }

            _latencyClock.Reset();

            if (IsVisible) Hide();
            if (_faceStale)
            {
                _faceStale = false;
                _face = PillFace.Create(Themes.Active.Pill);
                Content = _face;
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

        if (!IsVisible)
        {
            PositionBottomCenter();
            Show();
            _frames = 0;
            Overlay.MakeOverlay(this);
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
}
```

The Task 4 `RepaintTheme` method and the old `HudBars` class are gone. `Overlay` stays as it was.

- [ ] **Step 8: Run the Global Constraints verification**

Also confirm nothing references the removed tokens:

```bash
grep -rn "Colors\.Glass\|RecordIdle\|GlassEdgeOpacity\|PillLampSize\|PillBarsHeight\|PillCompactHeight\|PillPreviewHeight\|Size\.PillWidth\|HudBars" --include=*.cs windows
```

Expected: no output.

- [ ] **Step 9: Manual check (record in the task report)**

`dotnet run --project src/VoxScribe.App -c Release`. With Paper, Tide and Fluent each selected, hold Right Ctrl and speak a long sentence. Check:
- The pill appears at the bottom without stealing focus. Type into Notepad while holding: the text goes to Notepad.
- The red dot shows only while held.
- The level visual moves with the voice.
- The preview stays on one line showing the tail.
- The width eases open in ~150 ms.
- Releasing shows working, then the latency, then hides.

- [ ] **Step 10: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
feat: per-theme pill faces (Paper slip, Tide capsule, Fluent flyout)

HudWindow keeps its polling state machine and hands each frame to the theme's
PillFace inside a fixed click-through window. The red dot shows only while
recording and the one-line preview never grows the pill.
EOF
```

---

### Task 10: Orb and Mono pill silhouettes

Visual spec: `.superpowers/mockups/Orb-Pill.dc.html` and `Mono-Pill.dc.html`.

**Files:**
- Create: `windows/src/VoxScribe.App/Views/Pill/OrbPill.cs`
- Create: `windows/src/VoxScribe.App/Views/Pill/MonoPill.cs`
- Modify: `windows/src/VoxScribe.App/Views/Pill/PillFace.cs` (`Create`)
- Test: `windows/tests/VoxScribe.App.Tests/PillTests.cs` (one test added)

**Interfaces:**
- Consumes: `PillFace`, `PillState`, `PillPhase`, `PillFace.Arc`, protected helpers `Tail`/`Clock`/`Seconds`/`Timer`/`Dot`/`Text` (Task 9); `DashedRule` (Task 6); `Tokens.Colors.AccentSecondary/AccentFill/PillRule/Specular`; `Themes.IsDark`.
- Produces: `OrbPill`, `OrbGlyph` (`Diameter`, `Push(double level, bool working)`), `MonoPill`, `BlockMeter` (`Show(double level, bool working)`), and `PillFace.Create` mapping all five kinds.

- [ ] **Step 1: Write the failing test**

Add to `windows/tests/VoxScribe.App.Tests/PillTests.cs`:

```csharp
    [AvaloniaFact]
    public void Every_theme_wears_its_own_silhouette()
    {
        PillFace.Create(PillKind.Paper).ShouldBeOfType<PaperPill>();
        PillFace.Create(PillKind.Orb).ShouldBeOfType<OrbPill>();
        PillFace.Create(PillKind.Tide).ShouldBeOfType<TidePill>();
        PillFace.Create(PillKind.Mono).ShouldBeOfType<MonoPill>();
        PillFace.Create(PillKind.Fluent).ShouldBeOfType<FluentPill>();
    }

    [AvaloniaFact]
    public void The_orb_grows_with_the_voice()
    {
        var orb = new OrbGlyph();
        for (var i = 0; i < 30; i++) orb.Push(0, working: false);
        orb.Diameter.ShouldBe(36, 0.5);
        for (var i = 0; i < 30; i++) orb.Push(1, working: false);
        orb.Diameter.ShouldBe(76, 0.5);
    }
```

Run it. Expected: compile error, `OrbPill`, `MonoPill` and `OrbGlyph` do not exist.

- [ ] **Step 2: Create `windows/src/VoxScribe.App/Views/Pill/OrbPill.cs`**

```csharp
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>
/// Orb: no body, no waveform — the orb is the meter. The red dot rides its rim; the preview
/// sits in a small glass capsule beside it.
/// </summary>
internal sealed class OrbPill : PillFace
{
    private const double DotSize = 9;
    private const double CapsuleWidth = 270;
    private const double CapsuleRadius = 16;
    private const double MetaSize = 10;
    private const double LabelSize = 11;
    private const double TextSize = 14;
    private const double RimCos45 = 0.7071;
    private const int PreviewChars = 34;

    private readonly OrbGlyph _orb = new();
    private readonly Ellipse _dot = Dot(DotSize);
    private readonly TextBlock _label;
    private readonly TextBlock _capsuleMeta;
    private readonly TextBlock _capsuleText;
    private readonly Border _capsule;

    /// <summary>Builds the orb in the active theme.</summary>
    public OrbPill()
    {
        Background = null; // No body: the orb is the pill.

        _label = Text(Tokens.Fonts.Mono, LabelSize, Tokens.Brushes.InkSecondary);
        _capsuleMeta = Text(Tokens.Fonts.Mono, MetaSize, Tokens.Brushes.InkSecondary);
        _capsuleMeta.LetterSpacing = Tokens.Fonts.SilkscreenTracking;
        _capsuleText = Text(Tokens.Fonts.Grotesque, TextSize, Tokens.Brushes.Ink);

        _capsule = new Border
        {
            Width = 0,
            IsVisible = false,
            ClipToBounds = true,
            Background = new SolidColorBrush(Tokens.Colors.PillFill),
            BorderBrush = new SolidColorBrush(Tokens.Colors.PillEdge),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(CapsuleRadius),
            Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Snug),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Spacing = Tokens.Space.Hair, Children = { _capsuleMeta, _capsuleText } },
            Transitions = new Transitions
            {
                new DoubleTransition { Property = WidthProperty, Duration = Tokens.Motion.PillExpand },
            },
        };

        var stage = new Canvas
        {
            Width = _orb.Width,
            Height = _orb.Height,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _orb, _dot },
        };

        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            Children = { stage, _label, _capsule },
        };
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        var working = state.Phase == PillPhase.Working;
        _orb.Push(recording ? state.Level : 0, working);

        // The dot sits on the rim at 45°, so it rides out as the orb swells.
        _dot.IsVisible = recording;
        var r = _orb.Diameter / 2;
        var c = _orb.Width / 2;
        Canvas.SetLeft(_dot, c + (r * RimCos45) - (DotSize / 2));
        Canvas.SetTop(_dot, c - (r * RimCos45) - (DotSize / 2));

        var meta = working ? "POLISHING · CLEAN" : $"{Timer(state)} · {state.Mode}";
        var preview = Tail(state.Text, PreviewChars);
        var open = preview.Length > 0;
        _label.Text = meta;
        _label.IsVisible = !open;
        _capsuleMeta.Text = meta;
        _capsuleText.Text = preview;
        _capsule.IsVisible = open;
        _capsule.Width = open ? CapsuleWidth : 0;
    }
}

/// <summary>
/// The glowing orb: diameter follows the level (36 px silence → 76 px peak), two halos ripple
/// out while speaking, a spinner arc circles it while working.
/// </summary>
internal sealed class OrbGlyph : Control
{
    private const double Box = 96;
    private const double MinDiameter = 36;
    private const double DiameterRange = 40;
    private const double WorkingDiameter = 40;
    private const double Smoothing = 0.35;
    private const double SpeakingLevel = 0.05;
    private const double HaloSeconds = 1.6;
    private const double HaloOffset = 0.5;
    private const double HaloFrom = 0.9;
    private const double HaloGrowth = 0.6;
    private const double HaloOpacity = 0.55;
    private const double HaloOuterStroke = 1.5;
    private const double HaloInnerStroke = 1;
    private const double GlowScale = 1.6;
    private const double GlowInner = 0.4;
    private const byte GlowAlpha = 0x88;
    private const double CoreX = 0.34;
    private const double CoreY = 0.28;
    private const double CoreRadius = 0.75;
    private const double SecondaryStop = 0.24;
    private const double AccentStop = 0.62;
    private const double SpinnerDiameter = 64;
    private const double SpinnerStroke = 3;
    private const double SpinnerSweep = 120;
    private const double SpinStep = 10;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _diameter = MinDiameter;
    private double _spin;
    private bool _speaking;
    private bool _working;

    /// <summary>Creates the 96 px stage.</summary>
    public OrbGlyph()
    {
        Width = Box;
        Height = Box;
    }

    /// <summary>The orb's current diameter, for placing the rim dot.</summary>
    public double Diameter => _diameter;

    /// <summary>Feeds one frame.</summary>
    public void Push(double level, bool working)
    {
        var target = working ? WorkingDiameter : MinDiameter + (DiameterRange * level);
        _diameter += (target - _diameter) * Smoothing;
        _speaking = !working && level > SpeakingLevel;
        _working = working;
        if (working) _spin = (_spin + SpinStep) % 360;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var r = _diameter / 2;
        var accent = Tokens.Colors.Accent;
        var second = Tokens.Colors.AccentSecondary;
        var clear = Color.FromArgb(0, accent.R, accent.G, accent.B);

        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(GlowAlpha, accent.R, accent.G, accent.B), GlowInner));
        glow.GradientStops.Add(new GradientStop(clear, 1));
        context.DrawEllipse(glow, null, c, r * GlowScale, r * GlowScale);

        if (_speaking)
        {
            var t0 = _clock.Elapsed.TotalSeconds / HaloSeconds;
            for (var k = 0; k < 2; k++)
            {
                var t = (t0 + (k * HaloOffset)) % 1;
                var hr = r * (HaloFrom + (HaloGrowth * t));
                var pen = new Pen(new SolidColorBrush(k == 0 ? second : accent, HaloOpacity * (1 - t)),
                    k == 0 ? HaloOuterStroke : HaloInnerStroke);
                context.DrawEllipse(null, pen, c, hr, hr);
            }
        }

        if (_working)
        {
            context.DrawGeometry(null, new Pen(new SolidColorBrush(second), SpinnerStroke, lineCap: PenLineCap.Round),
                PillFace.Arc(c, SpinnerDiameter / 2, _spin, SpinnerSweep));
        }

        var body = new RadialGradientBrush
        {
            Center = new RelativePoint(CoreX, CoreY, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(CoreX, CoreY, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(CoreRadius, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(CoreRadius, RelativeUnit.Relative),
        };
        body.GradientStops.Add(new GradientStop(Tokens.Colors.Specular, 0));
        body.GradientStops.Add(new GradientStop(second, SecondaryStop));
        body.GradientStops.Add(new GradientStop(accent, AccentStop));
        body.GradientStops.Add(new GradientStop(clear, 1));
        context.DrawEllipse(body, null, c, r, r);
    }
}
```

- [ ] **Step 3: Create `windows/src/VoxScribe.App/Views/Pill/MonoPill.cs`**

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>
/// Mono: a square status line — REC block, timer, 16-cell block meter, mode tag — and a
/// shell-prompt preview with a block cursor. Hard offset shadow in light mode only.
/// </summary>
internal sealed class MonoPill : PillFace
{
    private const double CompactWidth = 360;
    private const double PreviewWidth = 420;
    private const double RowHeight = 34;
    private const double DotSize = 8;
    private const double TextSize = 12;
    private const double CursorWidth = 8;
    private const double CursorHeight = 14;
    private const double MeterHeight = 14;
    private const double ShadowOffset = 4;
    private const int PreviewChars = 44;

    private readonly Border _dot;
    private readonly TextBlock _state;
    private readonly TextBlock _timer;
    private readonly TextBlock _badge;
    private readonly TextBlock _preview;
    private readonly BlockMeter _meter = new() { Height = MeterHeight, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _previewRow;
    private readonly IBrush _ink = Tokens.Brushes.Ink;
    private readonly IBrush _muted = Tokens.Brushes.InkSecondary;
    private readonly IBrush _accent = Tokens.Brushes.Accent;

    /// <summary>Builds the status line in the active theme.</summary>
    public MonoPill()
    {
        Width = CompactWidth;
        Background = new SolidColorBrush(Tokens.Colors.PillFill);
        BorderBrush = new SolidColorBrush(Tokens.Colors.PillEdge);
        BorderThickness = new Thickness(Tokens.Border.Hairline);
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        if (!Themes.IsDark)
        {
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = ShadowOffset, OffsetY = ShadowOffset, Color = Tokens.Colors.Ink });
        }

        _dot = new Border { Width = DotSize, Height = DotSize, Background = Tokens.Brushes.Record, VerticalAlignment = VerticalAlignment.Center };
        _state = Text(Tokens.Fonts.Mono, TextSize, _ink);
        _state.FontWeight = FontWeight.Bold;
        _timer = Text(Tokens.Fonts.Mono, TextSize, _muted);
        _badge = Text(Tokens.Fonts.Mono, TextSize, _ink);
        _preview = Text(Tokens.Fonts.Mono, TextSize, _ink);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = Tokens.Space.Snug,
            Height = RowHeight,
            Margin = new Thickness(Tokens.Space.Base, 0),
        };
        Control[] cells =
        [
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Tight, Children = { _dot, _state } },
            _timer,
            _meter,
            _badge,
        ];
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            row.Children.Add(cells[i]);
        }

        var prompt = Text(Tokens.Fonts.Mono, TextSize, _accent);
        prompt.Text = ">";
        prompt.Margin = new Thickness(0, 0, Tokens.Space.Snug, 0);
        var cursor = new Border { Width = CursorWidth, Height = CursorHeight, Background = _ink, VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel { Margin = new Thickness(Tokens.Space.Base, Tokens.Space.Snug) };
        line.Children.Add(Panels.Docked(prompt, Dock.Left));
        line.Children.Add(Panels.Docked(cursor, Dock.Right));
        line.Children.Add(_preview);

        _previewRow = new StackPanel
        {
            IsVisible = false,
            Children = { new DashedRule { Stroke = new SolidColorBrush(Tokens.Colors.PillRule) }, line },
        };

        Child = new StackPanel { Children = { row, _previewRow } };
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        var working = state.Phase == PillPhase.Working;
        _dot.IsVisible = recording;
        _state.Text = state.Phase switch
        {
            PillPhase.Recording => "REC",
            PillPhase.Working => "CLEAN",
            PillPhase.Notice => "NOTE",
            _ => "DONE",
        };
        _state.Foreground = working ? _accent : _ink;
        _timer.Text = state.Phase == PillPhase.Latency
            ? Seconds(state.Elapsed)
            : string.Create(CultureInfo.InvariantCulture, $"{(int)state.Elapsed.TotalMinutes:00}:{state.Elapsed.Seconds:00}");
        _meter.Show(recording ? state.Level : 0, working);
        _badge.Text = $"[{state.Mode}]";
        _badge.Foreground = working ? _muted : _ink;

        var preview = Tail(state.Text, PreviewChars);
        _preview.Text = preview;
        _previewRow.IsVisible = preview.Length > 0;
        Width = preview.Length > 0 ? PreviewWidth : CompactWidth;
    }
}

/// <summary>Sixteen square cells: lit count = level. While working, a fixed run in the accent.</summary>
internal sealed class BlockMeter : Control
{
    private const int Cells = 16;
    private const int WorkingCells = 9;
    private const double Gap = 2;

    private int _lit;
    private bool _working;

    /// <summary>Feeds one frame; repaints only when the lit count changes.</summary>
    public void Show(double level, bool working)
    {
        var lit = working ? WorkingCells : (int)Math.Round(level * Cells, MidpointRounding.AwayFromZero);
        if (lit == _lit && working == _working) return;
        _lit = lit;
        _working = working;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var cell = (Bounds.Width - (Gap * (Cells - 1))) / Cells;
        if (cell <= 0) return;

        // Neon cells on black in dark mode; ink cells on paper in light mode (the mockup).
        var on = _working ? Tokens.Brushes.Accent : Themes.IsDark ? Tokens.Brushes.AccentFill : Tokens.Brushes.Ink;
        var off = new SolidColorBrush(Tokens.Colors.PillRule);
        for (var i = 0; i < Cells; i++)
        {
            context.DrawRectangle(i < _lit ? on : off, null, new Rect(i * (cell + Gap), 0, cell, Bounds.Height));
        }
    }
}
```

- [ ] **Step 4: Map all five kinds in `PillFace.Create`**

```csharp
    /// <summary>The face for <paramref name="kind"/>, in the active theme.</summary>
    public static PillFace Create(PillKind kind) => kind switch
    {
        PillKind.Paper => new PaperPill(),
        PillKind.Orb => new OrbPill(),
        PillKind.Tide => new TidePill(),
        PillKind.Mono => new MonoPill(),
        _ => new FluentPill(),
    };
```

- [ ] **Step 5: Run the Global Constraints verification**

The Task 9 doctrine tests (dot, height, focus, no-red) now also cover Orb and Mono.

- [ ] **Step 6: Manual check (record in the task report)**

Same as Task 9 Step 9, with Orb and then Mono:
- Orb: no capsule body around the orb. The orb swells from ~36 to ~76 px with the voice. Halos ripple only while speaking, and the red dot rides the rim.
- Mono: square corners, the meter lights with the voice, and the shadow appears only in light mode.

- [ ] **Step 7: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A windows
git commit -F - <<'EOF'
feat: Orb and Mono pill silhouettes

Orb is a bodiless glowing orb whose diameter is the level, with halos while
speaking and the red dot on its rim. Mono is a square status line with a
16-cell block meter and a shell-prompt preview.
EOF
```

---

### Task 11: Colour doctrine guard and docs

**Files:**
- Create: `windows/tests/VoxScribe.App.Tests/Design/DoctrineTests.cs`
- Modify: `AGENTS.md` (the "What this is" paragraph and the whole "Design system" section)
- Modify: `windows/README.md` (status blurb)
- Modify: `windows/src/VoxScribe.App/App.axaml` (comment only)

**Interfaces:**
- Consumes: `Themes.All`, `Themes.Apply`, every public static `Color` property on `Tokens.Colors`.
- Produces: nothing for later tasks.

- [ ] **Step 1: Write the doctrine test**

Create `windows/tests/VoxScribe.App.Tests/Design/DoctrineTests.cs`:

```csharp
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Shouldly;
using VoxScribe.App.Design;

namespace VoxScribe.App.Tests.Design;

/// <summary>
/// Red means recording, and only the recording dot is red. Every themable colour token, in
/// every theme, mode and variant, is checked — a red accent or a red warning is a build failure.
/// </summary>
public sealed class DoctrineTests
{
    private const double RedHueWindow = 15;
    private const double MinSaturation = 0.5;
    private const double MinValue = 0.35;
    private const byte VisibleAlpha = 0x40;

    private static bool IsRed(Color c)
    {
        if (c.A < VisibleAlpha) return false;
        var hsv = c.ToHsv();
        return (hsv.H <= RedHueWindow || hsv.H >= 360 - RedHueWindow) && hsv.S >= MinSaturation && hsv.V >= MinValue;
    }

    [AvaloniaFact]
    public void The_record_dot_is_red()
    {
        IsRed(Tokens.Colors.Record).ShouldBeTrue("the detector must recognise the one red it allows");
    }

    [AvaloniaFact]
    public void No_other_colour_token_is_red_in_any_theme_mode_or_variant()
    {
        var tokens = typeof(Tokens.Colors).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(Color) && p.Name != nameof(Tokens.Colors.Record))
            .ToList();
        tokens.Count.ShouldBeGreaterThan(15, "the reflection must actually find the tokens");

        var failures = new List<string>();
        try
        {
            foreach (var theme in Themes.All)
            foreach (var variant in theme.Variants)
            foreach (var dark in new[] { false, true })
            {
                Themes.Apply(theme.Id, variant.Id, dark);
                foreach (var token in tokens)
                {
                    var color = (Color)token.GetValue(null)!;
                    if (IsRed(color)) failures.Add($"{theme.Id}/{variant.Id}/{(dark ? "dark" : "light")}: {token.Name} {color}");
                }
            }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
    }
}
```

- [ ] **Step 2: Prove the gate can fail, then revert**

Temporarily change Paper's plum light value in `ThemeCatalog.cs` from `0x6A3D9A` to `0xC0262D`. Run:

```bash
dotnet test tests/VoxScribe.App.Tests --filter FullyQualifiedName~DoctrineTests
```

Expected: FAIL, listing `paper/plum/light: Accent …`. Restore `0x6A3D9A` and re-run. Expected: PASS.

- [ ] **Step 3: Update `AGENTS.md`**

In "What this is", replace "a dictation pill, tray, start-at-login, an installer, and the Void Glass theme with a user-selectable accent." with:

> a dictation pill, tray, start-at-login, an installer, and five selectable themes (Paper, Orb, Tide, Mono, Fluent), each with light and dark palettes and curated accents.

Replace the whole "## Design system" section, from its heading up to the `---` before "## Windows specifics", with:

```markdown
## Design system

`windows/src/VoxScribe.App/Design/DesignTokens.cs` defines every colour, size, radius and
duration token. **Views must not contain literal values.** If a component needs a number that
isn't a token, add the token rather than inlining it. A number only one control's own drawing
uses (a pill face's width, a wave's frequency) is a named `private const` in that control.

**Five themes, one skeleton.** A theme is data — `Design/ThemeCatalog.cs`: a light and a dark
`Palette`, curated `AccentVariant`s (each with a light and a dark value), its fonts, and a
handful of layout knobs (`ThemeModel.cs`). `Themes.Apply(theme, variant, dark)` writes them into
`Tokens` and raises `Themes.Changed`; every window rebuilds its C#-built content on that
event. The app follows Windows light/dark through Avalonia's `ActualThemeVariant` — no Win32.
Paper is the default; any unknown or retired theme id falls back to it.

The main window is one sidebar shell (Home · History · Dictionary · Settings) for every theme.
Only the dictation pill differs per theme: `Views/Pill/*Pill.cs`, one `PillFace` each, fed by
`HudWindow`'s unchanged polling state machine. The mockups that specify all of it live in
`.superpowers/mockups/`.

Fonts: Instrument Serif, Geist, Geist Mono, Figtree and JetBrains Mono are bundled under
`Assets/Fonts/` with their OFL licences; Fluent uses system Segoe UI Variable and Cascadia Mono.

Two rules that are not negotiable, pinned by `VoxScribe.App.Tests/Design/`:

- **Red means recording.** `#E5484D` is the recording dot and nothing else, in every theme —
  `DoctrineTests` scans every colour token of every theme, mode and variant.
- **WCAG AA.** `ContrastTests` checks every text/ground pair the views draw, 4.5:1, across all
  themes, modes and variants. Accent text never sits on `Hover`.

Green and amber are ordinary colours now (`Positive`, `Caution`, accent variants) — the old
"instrumentation only" rule is gone.
```

- [ ] **Step 4: Update `windows/README.md`**

In the status blurb near the top, replace "and the Void Glass theme." with "and five selectable themes (Paper, Orb, Tide, Mono, Fluent) that follow Windows light/dark mode."

Then run `grep -n "Void Glass\|deep-field\|SettingsWindow\|accent colour" windows/README.md AGENTS.md`. Rewrite any remaining sentence it finds so it describes the five-theme design.

- [ ] **Step 5: Update the `App.axaml` comment**

In `windows/src/VoxScribe.App/App.axaml`, replace the comment under `RequestedThemeVariant="Default"` with:

```xml
  <!-- Default, not a fixed variant: the app follows Windows light/dark. App.axaml.cs re-applies
       the VoxScribe theme on ActualThemeVariantChanged. -->
```

- [ ] **Step 6: Run the Global Constraints verification**

- [ ] **Step 7: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add -A AGENTS.md windows
git commit -F - <<'EOF'
docs: five-theme design system; test that only the record dot is red

DoctrineTests scans every colour token across all themes, modes and variants.
AGENTS.md and the README describe the new themes and drop the
instrumentation-only rule for green and amber.
EOF
```

---

### Task 12: Version bump to 1.4.0

**Files:**
- Modify: `windows/Directory.Version.props`

- [ ] **Step 1: Bump the version**

Change `<Version>1.3.0</Version>` to `<Version>1.4.0</Version>`.

- [ ] **Step 2: Run the Global Constraints verification**

- [ ] **Step 3: Commit**

```bash
cd C:/Users/guill/orca/workspaces/vox-scribe/themes
git add windows/Directory.Version.props
git commit -F - <<'EOF'
chore: bump version to 1.4.0
EOF
```

Do not tag, push or build the installer. The controller does that.

---

## Self-review

**Spec coverage**

| Decision | Task |
|---|---|
| 5 themes replace 3 + Void Glass, default Paper, old/missing id falls back | 3 |
| One skeleton + knobs; per-theme pill only | 3 (knobs), 6 (skeleton), 9–10 (pills) |
| Sidebar Home · History · Dictionary · Settings; transport removed | 1, 6 |
| Settings as 6 tabs, `SettingsWindow` removed, tray → Settings page | 7 |
| Home: status line, 4 stats, last 5 with Copy / Type again, See all, chord footer | 6 |
| Stats in a platform-neutral, tested class | 5 |
| Light + dark per theme, follows Windows via `ActualThemeVariant` | 3, 4 |
| Curated variants, green in each, light + dark values; free-form accent removed | 3, 8 |
| Live repaint by rebuild | 4, 6, 7, 9 |
| Red only for the recording dot; amber/green rule dropped; `UiTests` + `AGENTS.md` | 3, 9, 11 |
| Pill: hidden at rest, no focus, red dot, level visual, one-line tail preview, mode + timer, no acrylic, 150 ms expand, per-theme silhouettes | 9, 10 |
| Bundled OFL fonts via avares, with download URLs and licences | 2 |
| WCAG AA across themes × modes × variants | 3 |
| Non-UI behaviour untouched; existing tests kept or adapted | all (tests adapted in 1, 3, 6, 7, 8) |
| Version 1.4.0 | 12 |

**Placeholder scan.** No TBD or "similar to". Every code step carries its code. The only references to existing code are the `Overlay` class (kept verbatim) and the chord-recorder methods, which are reproduced in full.

**Type consistency.** These names are the same in every task that uses them: `Themes.Apply(string?, string?, bool)`, `Themes.DefaultId`, `ThemeDefinition` knobs, `PillState(Phase, Level, Elapsed, Mode, Text)`, `PillFace.Create/RecordDot/Update/Arc`, `AppPage`, `SettingsTab`, `SettingsPage(AppSettings, DictationEngine?, SettingsTab)`, `HomePage(TranscriptStore?, AppSettings?, Action<AppPage>, Func<string, Task>)`, `Shell.Paint(Button, Selection, bool)`, `Panels.Chord(int[]?)` and `Tokens.Colors.Accent/AccentFill/OnAccent/AccentTint/AccentSecondary/PillFill/PillEdge/PillRule/Positive/Caution`.
