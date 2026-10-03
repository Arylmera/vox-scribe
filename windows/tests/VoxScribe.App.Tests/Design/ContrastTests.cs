using Avalonia.Media;
using VoxScribe.App.Design;
using Xunit;

namespace VoxScribe.App.Tests.Design;

/// <summary>
/// Verifies WCAG AA contrast compliance for all design tokens.
/// Minimum 4.5:1 for normal text, 3:1 for large/UI elements.
/// </summary>
public class ContrastTests
{
    private const double MinTextContrast = 4.5;
    private const double MinUIContrast = 3.0;

    [Fact]
    public void Ink_OnChassis_MeetsWCAG_AA()
    {
        var ratio = Tokens.Colors.GetContrastRatio(Tokens.Colors.Ink, Tokens.Colors.Chassis);
        Assert.True(ratio >= MinTextContrast, $"Ink on Chassis: {ratio:F2}:1 (need {MinTextContrast}:1)");
    }

    [Fact]
    public void InkSecondary_OnChassis_MeetsWCAG_AA()
    {
        var ratio = Tokens.Colors.GetContrastRatio(Tokens.Colors.InkSecondary, Tokens.Colors.Chassis);
        Assert.True(ratio >= MinTextContrast, $"InkSecondary on Chassis: {ratio:F2}:1 (need {MinTextContrast}:1)");
    }

    [Fact]
    public void Silkscreen_OnChassis_MeetsWCAG_AA()
    {
        var ratio = Tokens.Colors.GetContrastRatio(Tokens.Colors.Silkscreen, Tokens.Colors.Chassis);
        Assert.True(ratio >= MinTextContrast, $"Silkscreen on Chassis: {ratio:F2}:1 (need {MinTextContrast}:1)");
    }

    [Fact]
    public void InkOnDeck_OnDeck_MeetsWCAG_AAA()
    {
        var ratio = Tokens.Colors.GetContrastRatio(Tokens.Colors.InkOnDeck, Tokens.Colors.Deck);
        Assert.True(ratio >= MinTextContrast, $"InkOnDeck on Deck: {ratio:F2}:1 (need {MinTextContrast}:1)");
    }

    [Fact]
    public void Record_OnChassis_MeetsWCAG_AA()
    {
        var ratio = Tokens.Colors.GetContrastRatio(Tokens.Colors.Record, Tokens.Colors.Chassis);
        Assert.True(ratio >= MinUIContrast, $"Record on Chassis: {ratio:F2}:1 (need {MinUIContrast}:1)");
    }
}
