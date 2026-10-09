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

    [AvaloniaFact]
    public void TailClip_crops_an_overflowing_line_on_the_left()
    {
        // LongText (~1400 chars) is wider than any reasonable host at any font, so this does
        // not depend on PreviewChars, font metrics, or which face is active.
        var text = new TextBlock { Text = LongText, TextWrapping = TextWrapping.NoWrap };
        var host = new TailClip { Tail = true, ClipToBounds = true, Width = 300, Height = 40, Child = text };
        var window = new Window { Content = host, Width = 400, Height = 100 };
        try
        {
            window.Show();

            text.Bounds.X.ShouldBeLessThan(0);
            Math.Abs(text.Bounds.Right - host.Bounds.Width).ShouldBeLessThan(0.5);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_lingering_notice_keeps_its_subject() => EachTheme((theme, face) =>
    {
        var notice = LongText[..75];
        face.Update(new PillState(PillPhase.Notice, 0, TimeSpan.Zero, "RAW", notice));

        var line = face.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.TextTrimming == TextTrimming.CharacterEllipsis);

        line.Text.ShouldStartWith(notice[..10]);
    });

    [AvaloniaFact]
    public void Cleanup_collapses_the_preview_immediately() => EachTheme((theme, face) =>
    {
        // The width eases open over Tokens.Motion.PillExpand; disabling the transition here
        // makes the assertion check the target width, not wherever the animation clock landed.
        face.Transitions = null;

        face.Update(new PillState(PillPhase.Recording, 0.3, TimeSpan.FromSeconds(2), "RAW", "encore un mot"));
        var recordingWidth = face.Width;

        face.Update(new PillState(PillPhase.Working, 0.3, TimeSpan.FromSeconds(2), "RAW", "encore un mot"));

        face.Width.ShouldBeLessThan(recordingWidth, theme.Id);
    });

    [AvaloniaFact]
    public void Every_theme_wears_its_own_silhouette()
    {
        PillFace.Create(PillKind.Paper).ShouldBeOfType<PaperPill>();
        PillFace.Create(PillKind.Orb).ShouldBeOfType<OrbPill>();
        PillFace.Create(PillKind.Tide).ShouldBeOfType<TidePill>();
        PillFace.Create(PillKind.Mono).ShouldBeOfType<MonoPill>();
        PillFace.Create(PillKind.Fluent).ShouldBeOfType<FluentPill>();
    }

    [AvaloniaFact]
    public void The_orb_grows_with_the_voice()
    {
        var orb = new OrbGlyph();
        for (var i = 0; i < 30; i++) orb.Push(0, working: false);
        orb.Diameter.ShouldBe(36, 0.5);
        for (var i = 0; i < 30; i++) orb.Push(1, working: false);
        orb.Diameter.ShouldBe(76, 0.5);
    }

    // A_liquid_wave_keeps_its_accent_once_built / A_mic_halo_keeps_its_accent_brushes_once_built
    // were removed: they read `readonly` fields that can never change once set, so they would
    // stay green even if Render went back to reading Tokens.Colors.Accent live. Headless
    // rendering in this project's test host does not expose per-pixel output cheaply enough
    // to assert what is actually painted (see task-9-report.md's fix-pass-2 section), so the
    // accent-capture behaviour is covered by code review and the constructor-capture pattern
    // itself, not by an automated test.
}
