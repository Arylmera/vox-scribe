using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Vintage mic: a 1950s chrome broadcast mic with a face. The grille lights up in waves while
/// you speak and notes drift up when the text lands. Accent: grille glow and notes.
/// </summary>
internal sealed class MicChibi(Color accent) : ChibiFace(accent)
{
    private const uint Chrome = 0xC7CCD6;
    private const uint Dark = 0x3A3F4B;
    private const uint Grille = 0x9AA0AD;
    private const uint Blush = 0xF0B8A0;
    private const uint Shine = 0xFFFFFF;
    private const double ShineOpacity = 0.8;
    private const double BlushOpacity = 0.6;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var dark = Rgb(Dark);

        s.Rect(40, 128, 40, 5, 2.5, dark);
        s.Rect(57, 106, 6, 22, 0, dark);
        s.Path("M38 96 q22 16 44 0", null, dark, 4);
        s.Path("M36 50 q0 -30 24 -30 q24 0 24 30 v34 q0 20 -24 20 q-24 0 -24 -20z", Rgb(Chrome));
        s.Path("M42 26 q8 -4 12 -2", null, Rgb(Shine), 3).Opacity = ShineOpacity;
        s.Rect(40, 30, 40, 34, 6, Rgb(Grille));

        double[] slots = [34, 40, 46, 52, 58];
        for (var i = 0; i < slots.Length; i++)
        {
            if (pose == ChibiPose.Listen)
            {
                s.Move(Glow(s.Rect(44 + (i % 2 * 2), slots[i], 32 - (i % 2 * 4), 3, 1.5, Accent)), ChibiMove.Pulse, i);
            }
            else
            {
                s.Rect(44, slots[i], 32, 3, 1.5, dark);
            }
        }

        s.Rect(36, 66, 48, 4, 0, Rgb(Grille));

        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 52, 68, 78, dark, 2.5);
                break;
            case ChibiPose.Done:
                HappyEyes(s, 52, 68, 80, dark, 2.5);
                s.Path("M55 88 q5 5 10 0", null, dark, 2.5);
                var note = s.Begin();
                s.Path("M98 40 v-14 l8 -3 v14", null, Accent, 2);
                s.Circle(96, 40, 3, Accent);
                s.Circle(104, 37, 3, Accent);
                s.End();
                s.Move(note, ChibiMove.Rise, 0);
                var quaver = s.Begin();
                s.Path("M20 44 v-12", null, Accent, 2);
                s.Circle(18, 44, 3, Accent);
                s.End();
                s.Move(quaver, ChibiMove.Rise, 1);
                break;
            default:
                var ey = pose is ChibiPose.Think or ChibiPose.Worried ? 76 : 78;
                s.Circle(52, ey, 3.5, dark);
                s.Circle(68, ey, 3.5, dark);
                if (pose == ChibiPose.Listen) s.Move(s.Ellipse(60, 90, 4, 3.5, dark), ChibiMove.Talk);
                break;
        }

        s.Ellipse(45, 86, 4, 2.5, Rgb(Blush)).Opacity = BlushOpacity;
        s.Ellipse(75, 86, 4, 2.5, Rgb(Blush)).Opacity = BlushOpacity;
    }
}
