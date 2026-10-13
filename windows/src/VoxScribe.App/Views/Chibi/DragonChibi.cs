using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Little dragon: wings beat while you speak, nostrils smoke while thinking, a small golden
/// flame when the text lands (gold, never red). Accent: the wing membranes.
/// </summary>
internal sealed class DragonChibi(Color accent) : ChibiFace(accent)
{
    private const uint Scale = 0x5DBB63;
    private const uint DarkScale = 0x3E8E46;
    private const uint Belly = 0xF2E3A4;
    private const uint Ink = 0x1E3320;
    private const uint Snout = 0x7FCC84;
    private const uint Smoke = 0xB8BEC8;
    private const uint Flame = 0xFFC857;
    private const uint FlameCore = 0xFFF0B3;
    private const uint Glint = 0xFFFFFF;
    private const uint Snore = 0xCFD3DC;
    private const double SnoreSize = 8;
    private const double SnoreGrowth = 3;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);
        var scale = Rgb(Scale);
        var dark = Rgb(DarkScale);

        s.Path("M78 120 q26 4 30 -12 l4 -6 -10 2", null, scale, 8);
        var leftWing = s.Path("M38 96 q-24 -10 -26 -32 q12 6 18 4 q-2 10 14 18z", Accent);
        var rightWing = s.Path("M82 96 q24 -10 26 -32 q-12 6 -18 4 q2 10 -14 18z", Accent);
        if (pose == ChibiPose.Listen)
        {
            s.Move(leftWing, ChibiMove.Flap, origin: new Point(38, 96));
            s.Move(rightWing, ChibiMove.Flap, origin: new Point(82, 96));
        }

        s.Ellipse(60, 108, 24, 22, scale);
        s.Ellipse(60, 112, 14, 16, Rgb(Belly));
        s.Ellipse(48, 130, 8, 4, dark);
        s.Ellipse(72, 130, 8, 4, dark);
        s.Path("M42 46 l-6 -16 14 10z M78 46 l6 -16 -14 10z", Rgb(Belly));
        s.Ellipse(60, 68, 30, 25, scale);
        s.Path("M56 42 l4 -8 4 8z", dark);

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 50, 72, 68, ink);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 50, 72, 70, ink);
                break;
            default:
                var ey = pose is ChibiPose.Think or ChibiPose.Worried ? 64 : 68;
                s.Ellipse(50, ey, 5, 6.5, ink);
                s.Ellipse(72, ey, 5, 6.5, ink);
                s.Circle(51.5, ey - 3, 2, Rgb(Glint));
                s.Circle(73.5, ey - 3, 2, Rgb(Glint));
                break;
        }

        s.Ellipse(62, 82, 14, 8, Rgb(Snout));
        s.Circle(58, 80, 1.5, ink);
        s.Circle(66, 80, 1.5, ink);
        if (pose == ChibiPose.Listen) s.Move(s.Ellipse(62, 87, 4, 3, ink), ChibiMove.Talk);

        if (pose == ChibiPose.Think)
        {
            s.Move(s.Circle(86, 74, 4, Rgb(Smoke)), ChibiMove.Rise, 0);
            s.Move(s.Circle(90, 70, 3, Rgb(Smoke)), ChibiMove.Rise, 1);
        }

        if (pose == ChibiPose.Done)
        {
            s.Path("M80 82 q16 -6 24 2 q-8 2 -10 8 q-4 -6 -14 -4z", Rgb(Flame));
            s.Path("M82 83 q8 -2 14 2 q-6 1 -7 4 q-3 -4 -7 -3z", Rgb(FlameCore));
        }

        // Napping: z z Z drift up while it dozes.
        if (pose == ChibiPose.Idle)
        {
            string[] snore = ["z", "z", "Z"];
            for (var i = 0; i < snore.Length; i++)
            {
                s.Move(s.Text(snore[i], 82 + (i * 6), 38 - (i * 6), 14, SnoreSize + (i * SnoreGrowth), Rgb(Snore)), ChibiMove.Rise, i);
            }
        }
    }
}
