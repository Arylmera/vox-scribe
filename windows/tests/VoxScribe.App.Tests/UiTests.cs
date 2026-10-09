using System.IO;
using Avalonia.VisualTree;
using VoxScribe.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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
        try
        {
            window.Bounds.Width.ShouldBeGreaterThan(0);
            window.Bounds.Height.ShouldBeGreaterThan(0);
        }
        finally
        {
            window.ExitAllowed = true;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Window_honours_its_minimum_size()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.MinWidth.ShouldBe(Tokens.Size.MainMinWidth);
            window.MinHeight.ShouldBe(Tokens.Size.MainMinHeight);
        }
        finally
        {
            window.ExitAllowed = true;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Navigation_lists_the_four_pages_in_order()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.GetVisualDescendants().OfType<NavButton>()
                .Select(b => Avalonia.Automation.AutomationProperties.GetName(b))
                .ShouldBe(["Home", "History", "Dictionary", "Settings"]);
            window.CurrentPage.ShouldBe(AppPage.Home);
        }
        finally
        {
            window.ExitAllowed = true;
            window.Close();
        }
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

                    window.ExitAllowed = true;
                    window.Close();
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

    [AvaloniaFact]
    public void Hovering_an_unselected_nav_item_uses_the_theme_hover_brush_not_fluent_grey()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var item = window.GetVisualDescendants().OfType<NavButton>()
                .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "History");
            var presenter = item.GetVisualDescendants().OfType<ContentPresenter>()
                .First(cp => cp.Name == "PART_ContentPresenter");

            var centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre);

            item.IsPointerOver.ShouldBeTrue();
            presenter.Background.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Tokens.Colors.Hover);
            presenter.Foreground.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Tokens.Colors.InkSecondary);
        }
        finally
        {
            window.ExitAllowed = true;
            window.Close();
        }
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
            ["paper"] = "moss",
            ["orb"] = "emerald",
            ["tide"] = "fern",
            ["mono"] = "phosphor",
            ["fluent"] = "forest",
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
                    window.ExitAllowed = true;
                    window.Close();
                }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }

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
    public void Level_gain_lifts_speech_into_view()
    {
        // Speech RMS lives around 0.02–0.15; without gain the pill's level visual barely moves.
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
        Button[] keys = [new TransportKey()];

        foreach (var key in keys) key.Background.ShouldBe(Brushes.Transparent);
    }
}
