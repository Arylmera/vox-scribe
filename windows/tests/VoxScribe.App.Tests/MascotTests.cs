using Avalonia;
using Shouldly;
using VoxScribe.App.Views.Chibi;
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
}
