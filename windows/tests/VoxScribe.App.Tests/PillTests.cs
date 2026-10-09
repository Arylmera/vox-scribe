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
