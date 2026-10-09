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
