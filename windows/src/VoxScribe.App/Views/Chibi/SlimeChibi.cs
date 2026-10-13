using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Slime, Rimuru-style: an almost spherical light-blue drop with a small peak and a big shine.
/// Squashes with the voice. Its body stays blue; the accent is its glow and sparkles.
/// </summary>
internal sealed class SlimeChibi(Color accent) : ChibiFace(accent)
{
    private const uint Gel = 0x7CCBF4;
    private const uint Edge = 0x4FA8DC;
    private const uint Shine = 0xFFFFFF;
    private const uint Ink = 0x1F4E6E;
    private const uint Shadow = 0x000000;
    private const double ShadowOpacity = 0.2;
    private const double UnderOpacity = 0.25;
    private const double ShineOpacity = 0.85;
    private const double ShineTilt = -30;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);
        s.Ellipse(60, 132, 38, 4, Rgb(Shadow)).Opacity = ShadowOpacity;

        var body = s.Begin();
        if (pose == ChibiPose.Listen) s.Move(body, ChibiMove.Squish, origin: new Point(60, 130));

        var gel = s.Path("M60 50 q4 0 8 6 q32 8 34 44 q1 30 -42 30 q-43 0 -42 -30 q2 -36 34 -44 q4 -6 8 -6z", Rgb(Gel), Rgb(Edge), 2);
        if (pose == ChibiPose.Listen) Glow(gel);
        s.Ellipse(60, 122, 34, 7, Rgb(Edge)).Opacity = UnderOpacity;
        var shine = s.Ellipse(40, 76, 9, 6, Rgb(Shine));
        shine.Opacity = ShineOpacity;
        shine.RenderTransform = new RotateTransform(ShineTilt);
        s.Circle(52, 66, 2.5, Rgb(Shine)).Opacity = ShineOpacity;

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 48, 72, 92, ink, 2.6);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 48, 72, 94, ink, 3);
                s.Path("M55 102 q5 5 10 0", null, ink, 2.4);
                break;
            default:
                var ey = pose is ChibiPose.Think or ChibiPose.Worried ? 88 : 92;
                s.Ellipse(48, ey, 3.4, 5.5, ink);
                s.Ellipse(72, ey, 3.4, 5.5, ink);
                if (pose == ChibiPose.Listen) s.Move(s.Ellipse(60, 104, 4, 3, ink), ChibiMove.Talk);
                break;
        }

        s.End();
    }
}
