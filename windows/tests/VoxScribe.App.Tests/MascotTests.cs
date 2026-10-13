using Avalonia;
using Avalonia.Headless.XUnit;
using Shouldly;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Chibi;
using VoxScribe.Core;
using Xunit;

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

    [Fact]
    public void Nine_mascots_with_the_ids_settings_document() =>
        Mascots.All.Select(m => m.Id).ShouldBe(
            ["servo-skull", "netrunner", "wizard", "clawd", "owl", "mic", "cat", "slime", "dragon"]);
}
