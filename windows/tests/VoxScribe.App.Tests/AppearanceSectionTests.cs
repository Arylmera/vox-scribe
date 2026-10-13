using System.IO;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Shouldly;
using VoxScribe.App.Views;
using VoxScribe.App.Views.Chibi;
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

    [AvaloniaFact]
    public void A_mascot_pick_refreshes_the_open_page()
    {
        var settings = Settings();
        var page = new SettingsPage(settings, null, SettingsTab.Appearance);
        var window = new Window { Content = page };
        try
        {
            window.Show();
            Names(page, "Mascot accent: ").ShouldBeEmpty();

            Click(Named(page, $"Mascot: {Mascots.All[0].Label}"));

            Names(page, "Mascot accent: ").ShouldNotBeEmpty("the accent row appears as soon as a mascot is picked");
        }
        finally
        {
            window.Close();
        }
    }
}
