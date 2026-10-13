using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Design;
using VoxScribe.App.Tests.Design;
using VoxScribe.App.Views.Chibi;
using VoxScribe.App.Views.Pill;
using Xunit;

namespace VoxScribe.AppTests;

/// <summary>Every mascot, every phase, both modes.</summary>
public sealed class ChibiTests
{
    private static readonly TimeSpan[] Moments =
        [TimeSpan.Zero, TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(1.7), TimeSpan.FromSeconds(3.9)];

    private static void EachMascot(Action<Mascot, ChibiFace> check)
    {
        try
        {
            foreach (var dark in new[] { false, true })
                foreach (var mascot in Mascots.All)
                {
                    Themes.Apply(Themes.DefaultId, null, dark);
                    var face = mascot.Create(Tokens.Colors.Accent);
                    var window = new Window { Content = face, SizeToContent = SizeToContent.WidthAndHeight };
                    window.Show();
                    check(mascot, face);
                    window.Close();
                }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }

    private static IEnumerable<Color> Colours(Control c) => (c switch
    {
        Shape s => new[] { s.Fill, s.Stroke },
        TextBlock t => new[] { t.Foreground },
        Border b => new[] { b.Background, b.BorderBrush },
        _ => [],
    }).SelectMany(BrushColours);

    // Every colour a brush can paint: a solid colour, or each stop of a gradient.
    private static IEnumerable<Color> BrushColours(IBrush? brush) => brush switch
    {
        ISolidColorBrush s => [s.Color],
        IGradientBrush g => g.GradientStops.Select(stop => stop.Color),
        _ => [],
    };

    [AvaloniaFact]
    public void Every_mascot_draws_every_phase_at_any_moment() => EachMascot((mascot, face) =>
    {
        foreach (var phase in Enum.GetValues<PillPhase>())
            foreach (var now in Moments)
            {
                Should.NotThrow(() => face.Update(new PillState(phase, 0.7, now, "CLEAN", "bonjour"), now), $"{mascot.Id} {phase}");
            }
    });

    [AvaloniaFact]
    public void The_red_dot_shows_only_while_recording() => EachMascot((mascot, face) =>
    {
        foreach (var phase in Enum.GetValues<PillPhase>())
        {
            face.Update(new PillState(phase, 0.6, TimeSpan.FromSeconds(4), "RAW", "bonjour"), TimeSpan.FromSeconds(1));
            face.RecordDot.IsVisible.ShouldBe(phase == PillPhase.Recording, $"{mascot.Id} {phase}");
        }
    });

    [AvaloniaFact]
    public void Nothing_on_a_mascot_is_red_but_the_dot() => EachMascot((mascot, face) =>
    {
        foreach (var phase in Enum.GetValues<PillPhase>())
        {
            face.Update(new PillState(phase, 0.8, TimeSpan.FromSeconds(1), "CLEAN", "texte"), TimeSpan.FromSeconds(1));
            var red = face.GetVisualDescendants().OfType<Control>()
                .Where(c => !ReferenceEquals(c, face.RecordDot)
                    && Colours(c).Any(colour => DoctrineTests.IsRed(colour) && !IsClawdTerracotta(mascot, colour)))
                .Select(c => c.GetType().Name);
            red.ShouldBeEmpty($"{mascot.Id} {phase}");
        }
    });

    [AvaloniaFact]
    public void Characters_are_lit_in_relief_except_pixel_art_Clawd() => EachMascot((mascot, face) =>
    {
        face.Update(new PillState(PillPhase.Idle, 0, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
        var fills = face.GetVisualDescendants().OfType<Shape>().Select(s => s.Fill).ToList();

        if (mascot.Id == "clawd") fills.OfType<IGradientBrush>().ShouldBeEmpty("pixel art stays flat");
        else fills.OfType<IRadialGradientBrush>().ShouldNotBeEmpty(mascot.Id);
    });

    [AvaloniaFact]
    public void The_bubble_speaks_only_when_there_is_something_to_say() => EachMascot((mascot, face) =>
    {
        face.Update(new PillState(PillPhase.Idle, 0, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
        face.Bubble.IsVisible.ShouldBeFalse(mascot.Id);

        face.Update(new PillState(PillPhase.Recording, 0.4, TimeSpan.FromSeconds(2), "RAW", "bonjour"), TimeSpan.Zero);
        face.Bubble.IsVisible.ShouldBeTrue(mascot.Id);
        string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)).ShouldContain("bonjour");
    });

    [AvaloniaFact]
    public void The_latency_shows_the_cleanup_split() => EachMascot((mascot, face) =>
    {
        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "CLEAN", string.Empty,
            Cleanup: TimeSpan.FromSeconds(0.7)), TimeSpan.Zero);
        var text = string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        text.ShouldContain("1.2s", customMessage: mascot.Id);
        text.ShouldContain("0.7s", customMessage: mascot.Id);

        face.Update(new PillState(PillPhase.Latency, 0, TimeSpan.FromSeconds(1.2), "EDIT", string.Empty,
            Cleanup: TimeSpan.FromSeconds(0.7)), TimeSpan.Zero);
        string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .ToLowerInvariant().ShouldNotContain("clean", customMessage: mascot.Id);
    });

    [AvaloniaFact]
    public void A_clean_working_phase_says_clean_and_a_raw_one_does_not() => EachMascot((mascot, face) =>
    {
        face.Update(new PillState(PillPhase.Working, 0.4, TimeSpan.FromSeconds(3), "RAW", "bonjour"), TimeSpan.Zero);
        string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .ToLowerInvariant().ShouldNotContain("clean", customMessage: mascot.Id);

        face.Update(new PillState(PillPhase.Working, 0.4, TimeSpan.FromSeconds(3), "CLEAN", "bonjour"), TimeSpan.Zero);
        string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))
            .ToLowerInvariant().ShouldContain("clean", customMessage: mascot.Id);
    });

    [AvaloniaFact]
    public void A_lingering_notice_keeps_its_subject() => EachMascot((mascot, face) =>
    {
        const string Notice = "Nothing was typed: the focused window refused the text, try again in a moment";
        face.Update(new PillState(PillPhase.Notice, 0, TimeSpan.Zero, "RAW", Notice), TimeSpan.Zero);

        face.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.TextTrimming == TextTrimming.CharacterEllipsis)
            .Text.ShouldStartWith("Nothing was", customMessage: mascot.Id);
    });

    [AvaloniaFact]
    public void The_pose_follows_the_phase() => EachMascot((mascot, face) =>
    {
        (PillPhase Phase, ChibiPose Pose)[] map =
        [
            (PillPhase.Idle, ChibiPose.Idle), (PillPhase.Recording, ChibiPose.Listen), (PillPhase.Working, ChibiPose.Think),
            (PillPhase.Latency, ChibiPose.Done), (PillPhase.Notice, ChibiPose.Worried),
        ];
        foreach (var (phase, pose) in map)
        {
            face.Update(new PillState(phase, 0, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
            face.Pose.ShouldBe(pose, mascot.Id);
        }
    });

    [AvaloniaFact]
    public void A_mascot_can_be_grabbed() => EachMascot((mascot, face) =>
    {
        face.IsHitTestVisible.ShouldBeTrue(mascot.Id);
        face.Focusable.ShouldBeFalse(mascot.Id);
    });

    [Fact]
    public void Clawd_wears_the_official_terracotta_and_nothing_else_does()
    {
        // The owner chose Anthropic's exact terracotta over a nudged one (2026-10-13), knowing
        // it sits inside the red window. The exemption is this one colour on Clawd only.
        ClawdChibi.Body.ShouldBe(0xD97757u);
        DoctrineTests.IsRed(Tokens.Colors.Rgb(ClawdChibi.Body)).ShouldBeTrue("if this flips, drop the exemption");
        Tokens.Colors.Rgb(ClawdChibi.Body).ShouldNotBe(Tokens.Colors.Record);
    }

    // Owner-approved exemption (2026-10-13): Clawd's body is Anthropic's official terracotta,
    // which the red detector flags. Only that exact colour, only on Clawd.
    private static bool IsClawdTerracotta(Mascot mascot, Color colour) =>
        mascot.Id == "clawd" && colour == Tokens.Colors.Rgb(ClawdChibi.Body);

    private static int ShapesIn(ChibiFace face, PillPhase phase)
    {
        face.Update(new PillState(phase, 0.5, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
        return face.GetVisualDescendants().OfType<Shape>().Count();
    }

    private static string TextIn(ChibiFace face, PillPhase phase)
    {
        face.Update(new PillState(phase, 0.5, TimeSpan.Zero, "RAW", string.Empty), TimeSpan.Zero);
        return string.Join(' ', face.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }

    [AvaloniaTheory]
    [InlineData("servo-skull", PillPhase.Recording, 26)]
    [InlineData("netrunner", PillPhase.Working, 21)]
    [InlineData("wizard", PillPhase.Recording, 22)]
    [InlineData("owl", PillPhase.Latency, 19)]
    [InlineData("cat", PillPhase.Working, 32)]
    [InlineData("cat", PillPhase.Latency, 21)]
    internal void Each_signature_draws_in_its_pose(string id, PillPhase phase, int atLeast)
    {
        var face = Mascots.Find(id)!.Create(Tokens.Colors.Accent);
        var window = new Window { Content = face, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            window.Show();
            ShapesIn(face, phase).ShouldBeGreaterThanOrEqualTo(atLeast, id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("clawd", PillPhase.Working, "Thinking…")]
    [InlineData("mic", PillPhase.Recording, "ON AIR")]
    [InlineData("slime", PillPhase.Recording, "b o n j")]
    [InlineData("dragon", PillPhase.Idle, "z z Z")]
    internal void A_signature_says_its_word(string id, PillPhase phase, string word)
    {
        var face = Mascots.Find(id)!.Create(Tokens.Colors.Accent);
        var window = new Window { Content = face, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            window.Show();
            TextIn(face, phase).ShouldContain(word, Case.Sensitive, customMessage: id);
        }
        finally
        {
            window.Close();
        }
    }
}
