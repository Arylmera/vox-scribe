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
