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
    public void A_raw_working_phase_claims_no_cleanup() => EachTheme((theme, face) =>
    {
        const string RawCleanupWording = "clean";

        face.Update(new PillState(PillPhase.Working, 0.4, TimeSpan.FromSeconds(3), "RAW", "bonjour"));
        var rawText = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .ToLowerInvariant();
        rawText.Contains(RawCleanupWording, StringComparison.Ordinal).ShouldBeFalse(theme.Id);

        face.Update(new PillState(PillPhase.Working, 0.4, TimeSpan.FromSeconds(3), "CLEAN", "bonjour"));
        var cleanText = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .ToLowerInvariant();
        cleanText.Contains(RawCleanupWording, StringComparison.Ordinal).ShouldBeTrue(theme.Id);
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
    public void The_latency_shows_the_cleanup_split() => EachTheme((theme, face) =>
    {
        // Orb and Tide are too tight for the full "clean 0.7s"/"clean failed" split (see
        // The_cleanup_split_fits_the_pill) and fall back to the spec's short form instead:
        // "1.2s · 0.7s" / "1.2s · raw".
        var shortForm = theme.Pill is PillKind.Orb or PillKind.Tide;

        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "CLEAN", string.Empty,
            Cleanup: TimeSpan.FromSeconds(0.7)));
        var text = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        text.ShouldContain("1.2s", customMessage: theme.Id);
        text.ShouldContain("0.7s", customMessage: theme.Id);

        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "CLEAN", string.Empty,
            Cleanup: TimeSpan.FromSeconds(5), CleanupFailed: true));
        var failed = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        failed.ShouldContain(shortForm ? "raw" : "failed", customMessage: theme.Id);

        // Command-mode dictations are cleaned too (the transcript is tidied before it is
        // submitted); the cleanup split must read as the cleanup's own outcome, never as if
        // the command itself failed.
        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "CMD", string.Empty,
            Cleanup: TimeSpan.FromSeconds(0.7)));
        var cmdOk = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        cmdOk.ShouldContain(shortForm ? "1.2s · 0.7s" : "clean 0.7s", customMessage: theme.Id);
        cmdOk.ToLowerInvariant().ShouldNotContain("cmd failed", customMessage: theme.Id);
        cmdOk.ToLowerInvariant().ShouldNotContain("command failed", customMessage: theme.Id);

        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "CMD", string.Empty,
            Cleanup: TimeSpan.FromSeconds(5), CleanupFailed: true));
        var cmdFailed = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        cmdFailed.ShouldContain(shortForm ? "1.2s · raw" : "clean failed", customMessage: theme.Id);
        cmdFailed.ToLowerInvariant().ShouldNotContain("cmd failed", customMessage: theme.Id);
        cmdFailed.ToLowerInvariant().ShouldNotContain("command failed", customMessage: theme.Id);
    });

    [AvaloniaFact]
    public void The_cleanup_split_fits_the_pill() => EachTheme((theme, face) =>
    {
        // Every face sets an explicit Width, so Measure(Size.Infinity) on the face itself just
        // returns that Width back — it can never catch an overflow, which clips silently
        // (TextTrimming.None, ClipToBounds). Checking one label's Bounds doesn't work either:
        // in Orb (a StackPanel) and Tide/Mono (Auto grid columns) the container always gives
        // the label exactly its own natural width, so DesiredSize <= Bounds is tautological —
        // only Paper/Fluent's DockPanel can actually constrain a label below what it wants.
        // Measuring the face's whole content against the face's own fixed Width (its real
        // budget) holds for all five: if the content naturally needs more than that budget,
        // something is overflowing the pill, in an Auto column, a StackPanel or a dock alike.
        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(12.3), "CLEAN", string.Empty,
            Cleanup: TimeSpan.FromSeconds(10.4)));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Every face hosts its preview/notice line inside a TailClip, which by design can
        // extend past its own slot (cropped on the left, newest word kept on screen) — that is
        // not what this test is about. With an empty Text there is nothing to preview, so each
        // TailClip here holds only a lingering static line (e.g. Fluent's idle "Speak now —
        // release to type" hint), unrelated to the cleanup split. Hide them before measuring so
        // only the dot/title/timer/badge content — where the split actually lives — counts.
        foreach (var host in face.GetVisualDescendants().OfType<TailClip>()) host.IsVisible = false;

        face.Child!.Measure(Avalonia.Size.Infinity);
        var available = face.Width
            - face.Padding.Left - face.Padding.Right
            - face.BorderThickness.Left - face.BorderThickness.Right;
        face.Child.DesiredSize.Width.ShouldBeLessThanOrEqualTo(available + 0.5, theme.Id);
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
