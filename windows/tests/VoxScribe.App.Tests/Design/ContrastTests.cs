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
                            Tokens.Colors.Chassis,
                            Tokens.Colors.Panel,
                            Tokens.Colors.Cap,
                            Tokens.Colors.Hover,
                            Tokens.Colors.Ink,
                            Tokens.Colors.InkSecondary,
                            Tokens.Colors.Accent,
                            Tokens.Colors.AccentFill,
                            Tokens.Colors.OnAccent,
                            Tokens.Colors.AccentTint,
                            Pill = Opaque(Tokens.Colors.PillFill),
                            Tokens.Colors.Positive,
                            Tokens.Colors.Caution,
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
