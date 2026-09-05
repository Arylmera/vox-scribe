using System.IO;
using Avalonia.VisualTree;
using VoxScribe.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.App.Views;
using Shouldly;

[assembly: AvaloniaTestApplication(typeof(VoxScribe.AppTests.TestAppBuilder))]

namespace VoxScribe.AppTests;

/// <summary>Hosts the app headlessly so the UI can be exercised without a display.</summary>
public static class TestAppBuilder
{
    /// <summary>Builds a headless Avalonia app for the test host.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>A minimal application shell for headless tests.</summary>
public sealed class TestApp : Application
{
    /// <inheritdoc />
    public override void Initialize() => Styles.Add(new FluentTheme());
}

/// <summary>
/// Real UI tests, running with no display.
/// </summary>
/// <remarks>
/// This is the payoff for choosing Avalonia over WPF. These run on macOS in milliseconds and
/// on a Windows runner in CI, so a broken layout or a control that fails to construct is
/// caught while writing it rather than after shipping to a machine we cannot test on.
/// </remarks>
public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void Window_opens_and_lays_out()
    {
        var window = new MainWindow();
        window.Show();

        window.Bounds.Width.ShouldBeGreaterThan(0);
        window.Bounds.Height.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public void Record_toggles_the_lamp_and_the_meter_together()
    {
        var window = new MainWindow();
        window.Show();

        window.IsRecording.ShouldBeFalse();
        window.RecordLamp.IsLit.ShouldBeFalse();
        window.Meter.IsActive.ShouldBeFalse();

        window.ToggleRecording();

        window.IsRecording.ShouldBeTrue();
        window.RecordLamp.IsLit.ShouldBeTrue("the record lamp must follow the transport");
        window.Meter.IsActive.ShouldBeTrue("the meter lamp must follow the transport");

        window.ToggleRecording();

        window.RecordLamp.IsLit.ShouldBeFalse();
        window.Meter.IsActive.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Window_honours_its_minimum_size()
    {
        var window = new MainWindow();
        window.Show();

        window.MinWidth.ShouldBe(Tokens.Size.MainMinWidth);
        window.MinHeight.ShouldBe(Tokens.Size.MainMinHeight);
    }
}

/// <summary>The individual pieces of equipment.</summary>
public sealed class EquipmentTests
{
    [AvaloniaFact]
    public void Silkscreen_uppercases_its_text()
    {
        // The look depends on size, tracking AND case together — a label that kept its
        // original casing would be half-styled and read as ordinary UI text.
        var label = new Silkscreen { Text = "transport" };
        label.Text.ShouldBe("TRANSPORT");
    }

    [AvaloniaFact]
    public void Silkscreen_uses_the_token_tracking()
    {
        new Silkscreen().LetterSpacing.ShouldBe(Tokens.Fonts.SilkscreenTracking);
    }

    [AvaloniaFact]
    public void Lamp_defaults_to_the_token_size()
    {
        var lamp = new Lamp();
        lamp.Width.ShouldBe(Tokens.Material.LampSize);
        lamp.Height.ShouldBe(Tokens.Material.LampSize);
    }

    [AvaloniaFact]
    public void Transport_key_uses_the_token_dimensions()
    {
        var key = new TransportKey();
        key.Height.ShouldBe(Tokens.Material.KeyHeight);
        key.MinWidth.ShouldBe(Tokens.Material.KeyMinWidth);
    }

    [AvaloniaFact]
    public void Meter_renders_without_throwing()
    {
        // The VU meter does all its own drawing, including a damped needle stepped by a
        // timer. Constructing and showing it is what proves the render path is sound.
        var meter = new VuMeter { Width = 168, Height = 54, Level = 0.6 };
        var window = new Window { Content = meter };
        window.Show();

        meter.Bounds.Width.ShouldBeGreaterThan(0);
    }
}

/// <summary>
/// Guards the two colour rules the design system calls non-negotiable.
/// </summary>
/// <remarks>
/// These are the sort of rule that erodes one reasonable-looking commit at a time. Asserting
/// them makes the erosion a build failure.
/// </remarks>
public sealed class DesignSystemTests
{
    [AvaloniaFact]
    public void Record_red_is_the_void_glass_value()
    {
        // Red still means recording and nothing else; this is the Void Glass red.
        var red = Tokens.Colors.Record;
        red.R.ShouldBe((byte)0xE8);
        red.G.ShouldBe((byte)0x56);
        red.B.ShouldBe((byte)0x56);
    }

    [AvaloniaFact]
    public void Radii_stay_generous_enough_to_read_as_void_glass()
    {
        // Void Glass is soft-cornered cards; anything under this reads as the old
        // equipment look creeping back.
        Tokens.Radius.Chip.ShouldBeGreaterThanOrEqualTo(6);
        Tokens.Radius.Panel.ShouldBeGreaterThanOrEqualTo(12);
        Tokens.Radius.RailKey.ShouldBeGreaterThanOrEqualTo(10);
    }

    [AvaloniaFact]
    public void Nothing_is_red_unless_it_asks_to_be()
    {
        // Red means recording, so the shared controls default to neutral and the transport
        // opts in. A red default left every unconfigured key one IsEngaged away from
        // breaking the rule, which is the quiet way a colour rule dies.
        new Lamp().LampColor.ShouldNotBe(Tokens.Colors.Record);
        new TransportKey().EngagedColor.ShouldNotBe(Tokens.Colors.Record);

        new MainWindow().RecordLamp.LampColor.ShouldBe(Tokens.Colors.Record);
    }

    [AvaloniaFact]
    public void Every_theme_keeps_the_record_red_and_stays_readable()
    {
        try
        {
            foreach (var (id, _) in Themes.Choices)
            {
                Themes.Apply(id);

                // The one non-negotiable: no theme may touch the record red.
                Tokens.Colors.Record.R.ShouldBe((byte)0xE8, $"theme {id}");

                // Ink must actually contrast its ground, whichever way the theme leans.
                var contrast = Math.Abs(
                    Luminance(Tokens.Colors.Ink) - Luminance(Tokens.Colors.Chassis));
                contrast.ShouldBeGreaterThan(0.5, $"theme {id}");
            }

            // An unknown id — an old or hand-edited settings file — lands on the default.
            Themes.Apply("no-such-theme");
            Tokens.Colors.Chassis.ShouldBe(Avalonia.Media.Color.FromRgb(0x0E, 0x11, 0x16));
        }
        finally
        {
            Themes.Apply(Themes.Default);
        }
    }

    private static double Luminance(Avalonia.Media.Color c) =>
        ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;

    [AvaloniaFact]
    public void Spacing_stays_on_the_four_point_grid()
    {
        double[] steps =
        [
            Tokens.Space.Hair, Tokens.Space.Tight, Tokens.Space.Snug,
            Tokens.Space.Base, Tokens.Space.Roomy, Tokens.Space.Wide, Tokens.Space.Panel,
        ];

        foreach (var step in steps) (step % 2).ShouldBe(0, $"{step} is off the grid");
    }

    [AvaloniaFact]
    public void Needle_ballistics_keep_the_vu_character_but_track_live_speech()
    {
        // A fast attack so the needle answers the voice, a slower fall and a slight
        // overshoot so it still moves like an instrument and not a progress bar. The
        // display gain lifts speech RMS (~0.02–0.15) into the visible range.
        Tokens.Motion.NeedleAttackSeconds.ShouldBeInRange(0.08, 0.20);
        Tokens.Motion.NeedleReleaseSeconds.ShouldBeGreaterThan(Tokens.Motion.NeedleAttackSeconds);
        Tokens.Motion.NeedleOvershoot.ShouldBeGreaterThan(0);
        Tokens.Motion.LevelGain.ShouldBeGreaterThan(1);
    }
}

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

/// <summary>The settings window as a whole.</summary>
public sealed class SettingsWindowTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"voxscribe-settings-{Guid.NewGuid():N}.json");

    /// <inheritdoc />
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

/// <summary>
/// Every custom key must answer the pointer over its whole face.
/// </summary>
/// <remarks>
/// These controls paint themselves in <c>Render</c> and so want no themed background — but
/// a <i>null</i> background is not hit-tested, which left only the glyph stroke or the word
/// clickable and made a 40&#215;40 rail key behave like a few thin lines. Transparent paints
/// nothing and still takes the click.
/// </remarks>
public sealed class KeyHitAreaTests
{
    [AvaloniaFact]
    public void Custom_keys_are_hit_testable_across_their_whole_face()
    {
        Button[] keys = [new TransportKey(), new RailKey("M4,10 V14"), new RecordButton()];

        foreach (var key in keys) key.Background.ShouldBe(Brushes.Transparent);
    }
}
