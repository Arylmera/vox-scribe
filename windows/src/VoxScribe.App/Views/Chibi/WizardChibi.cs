using Avalonia;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Wizard: starred pointy hat, beard, a staff whose crystal lights up while you speak, and a
/// ring of runes while thinking. Accent: robe and hat.
/// </summary>
internal sealed class WizardChibi(Color accent) : ChibiFace(accent)
{
    private const uint Skin = 0xF2D2B6;
    private const uint Beard = 0xEDEDED;
    private const uint Ink = 0x3A2A20;
    private const uint Staff = 0x8B5A2B;
    private const uint Crystal = 0x8FD3FF;
    private const uint Star = 0xFFD966;
    private const uint Mouth = 0x5A3A30;
    private const uint Glint = 0xFFFFFF;
    private const uint Brim = 0x000000;
    private const uint Cover = 0x7A4E2D;
    private const uint Paper = 0xF4ECD8;
    private const uint Page = 0xFFF8E7;
    private const uint PageEdge = 0xD9CBA8;
    private const uint Script = 0xB49A6A;
    private const double BrimShade = 0.25;
    private const double HaloOpacity = 0.35;
    private const double RuneOpacity = 0.7;
    private const double RuneWidth = 1.5;
    private const double PageEdgeWidth = 0.6;
    private const double ScriptWidth = 0.8;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);

        if (pose == ChibiPose.Think)
        {
            var runes = s.Circle(60, 70, 50, null);
            runes.Stroke = Rgb(Crystal);
            runes.StrokeThickness = RuneWidth;
            runes.StrokeDashArray = Sketch.Dashes(4, 7, RuneWidth);
            runes.Opacity = RuneOpacity;
            s.Move(runes, ChibiMove.Spin);
        }

        s.Path("M30 128 q4 -34 30 -34 q26 0 30 34z", Accent);
        s.Rect(102, 74, 4, 58, 2, Rgb(Staff));
        if (pose == ChibiPose.Listen)
        {
            var halo = s.Circle(104, 70, 11, Rgb(Crystal));
            halo.Opacity = HaloOpacity;
            s.Move(halo, ChibiMove.Pulse);
        }

        var crystal = s.Path("M104 62 l7 9 -7 9 -7 -9z", Rgb(Crystal));
        if (pose == ChibiPose.Listen) Glow(crystal, Tokens.Colors.Rgb(Crystal));

        s.Ellipse(60, 70, 26, 24, Rgb(Skin));
        s.Path("M38 78 q22 40 44 0 q-6 6 -22 6 q-16 0 -22 -6z", Rgb(Beard));
        if (pose == ChibiPose.Listen) s.Move(s.Ellipse(60, 86, 5, 4, Rgb(Mouth)), ChibiMove.Talk);

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 50, 70, 70, ink);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 50, 70, 72, ink, 3);
                break;
            case ChibiPose.Listen:
                s.Move(s.Circle(50, 70, 5, ink), ChibiMove.Blink);
                s.Move(s.Circle(70, 70, 5, ink), ChibiMove.Blink);
                s.Circle(51.5, 68.5, 1.8, Rgb(Glint));
                s.Circle(71.5, 68.5, 1.8, Rgb(Glint));
                break;
            default:
                s.Circle(51, 68, 5, ink);
                s.Circle(71, 68, 5, ink);
                s.Circle(52, 66, 1.8, Rgb(Glint));
                s.Circle(72, 66, 1.8, Rgb(Glint));
                break;
        }

        const string BrimPath = "M26 56 q34 -12 68 0 q-4 6 -34 6 q-30 0 -34 -6z";
        s.Path(BrimPath, Accent);
        s.Path(BrimPath, Rgb(Brim)).Opacity = BrimShade;
        s.Path("M34 54 q14 -10 26 -46 q8 18 4 22 q20 6 26 24z", Accent);
        s.Path("M56 14 q10 -6 10 4", null, Accent, 6);
        s.Path("M50 40 l2 4 4 1 -4 2 -2 4 -2 -4 -4 -2 4 -1z M70 30 l1.5 3 3 1 -3 1.5 -1.5 3 -1.5 -3 -3 -1.5 3 -1z", Rgb(Star));

        // An open grimoire floats in front while you speak; its right page turns.
        if (pose == ChibiPose.Listen)
        {
            s.Path("M34 112 q13 -6 26 0 q13 -6 26 0 v14 q-13 -6 -26 0 q-13 -6 -26 0z", Rgb(Cover));
            s.Path("M36 111 q12 -5 24 0 v12 q-12 -5 -24 0z M60 111 q12 -5 24 0 v12 q-12 -5 -24 0z", Rgb(Paper));
            s.Path("M40 114 h14 M40 117 h14 M40 120 h14", null, Rgb(Script), ScriptWidth);
            var page = s.Path("M60 111 q12 -5 24 0 v12 q-12 -5 -24 0z", Rgb(Page), Rgb(PageEdge), PageEdgeWidth);
            s.Move(page, ChibiMove.Flap, origin: new Point(60, 117));
        }
    }
}
