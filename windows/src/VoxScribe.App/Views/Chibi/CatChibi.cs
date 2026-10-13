using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Cat: a big sitting cat. Ears twitch while you speak, the tail beats while thinking and when
/// happy. Accent: its collar (the bell stays gold).
/// </summary>
internal sealed class CatChibi(Color accent) : ChibiFace(accent)
{
    private const uint Fur = 0xE8A25C;
    private const uint Cream = 0xF7E3C8;
    private const uint EarPink = 0xF4B7A0;
    private const uint Stripe = 0xC8803D;
    private const uint Ink = 0x3A2618;
    private const uint Nose = 0xE8909C;
    private const uint Bell = 0xFFD27A;
    private const uint Glint = 0xFFFFFF;
    private const double WhiskerOpacity = 0.6;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);
        var fur = Rgb(Fur);

        var tail = s.Path("M84 124 q22 -2 20 -26", null, fur, 9);
        if (pose is ChibiPose.Think or ChibiPose.Done) s.Move(tail, ChibiMove.Wag, origin: new Point(84, 124));

        s.Ellipse(60, 112, 26, 20, fur);
        s.Ellipse(60, 116, 14, 13, Rgb(Cream));
        s.Ellipse(48, 130, 8, 4, Rgb(Cream));
        s.Ellipse(72, 130, 8, 4, Rgb(Cream));

        var ears = s.Begin();
        s.Path("M30 60 l2 -30 22 16z M90 60 l-2 -30 -22 16z", fur);
        s.Path("M34 52 l2 -16 10 9z M86 52 l-2 -16 -10 9z", Rgb(EarPink));
        s.End();
        if (pose == ChibiPose.Listen) s.Move(ears, ChibiMove.Pulse);

        s.Ellipse(60, 72, 32, 27, fur);
        s.Path("M40 97 q20 8 40 0", null, Accent, 4);
        s.Circle(60, 103, 3, Rgb(Bell));
        s.Path("M52 48 l2 8 M60 46 v9 M68 48 l-2 8", null, Rgb(Stripe), 3);

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 48, 72, 70, ink);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 48, 72, 72, ink);
                break;
            default:
                var ey = pose is ChibiPose.Think or ChibiPose.Worried ? 66 : 70;
                var leftEye = s.Ellipse(48, ey, 5, 6.5, ink);
                var rightEye = s.Ellipse(72, ey, 5, 6.5, ink);
                if (pose == ChibiPose.Listen)
                {
                    s.Move(leftEye, ChibiMove.Blink);
                    s.Move(rightEye, ChibiMove.Blink);
                }

                s.Circle(49.5, ey - 3, 2, Rgb(Glint));
                s.Circle(73.5, ey - 3, 2, Rgb(Glint));
                break;
        }

        s.Path("M57 77 h6 l-3 3z", Rgb(Nose));
        if (pose == ChibiPose.Listen) s.Move(s.Ellipse(60, 84, 4, 3.5, ink), ChibiMove.Talk);
        else s.Path("M54 80 q3 4 6 0 q3 4 6 0", null, ink, 2.2);

        s.Path("M30 76 l12 2 M30 82 l12 0 M90 76 l-12 2 M90 82 l-12 0", null, ink, 1.2).Opacity = WhiskerOpacity;
    }
}
