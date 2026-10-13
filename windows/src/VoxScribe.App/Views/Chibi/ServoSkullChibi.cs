using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Servo-skull: a floating skull with a bionic eye in a brass ring, cables, and a parchment
/// that unrolls when the text lands — a servo-scribe. Accent: lens, antenna tip, vox grille.
/// </summary>
internal sealed class ServoSkullChibi(Color accent) : ChibiFace(accent)
{
    private const uint Brass = 0xC9A44C;
    private const uint Bone = 0xE9E1CC;
    private const uint Shade = 0xB9AE93;
    private const uint Socket = 0x2A2522;
    private const uint LensWell = 0x1E2A2A;
    private const uint Cable = 0x4A4F5A;
    private const uint Jaw = 0x7A8090;
    private const uint Parchment = 0xEFE3C2;
    private const uint ParchmentInk = 0x8B7A55;
    private const uint LensGlint = 0xE8FFF0;
    private const uint Quill = 0xF4F1EA;
    private const double FieldOpacity = 0.25;
    private const double DimLens = 0.45;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var listen = pose == ChibiPose.Listen;

        s.Path("M44 92 q-6 14 -2 26 M76 92 q8 10 4 24", null, Rgb(Cable), 2.5);
        if (pose == ChibiPose.Done)
        {
            s.Path("M50 100 h20 v34 q-10 4 -20 0z", Rgb(Parchment));
            s.Path("M53 106 h14 M53 111 h14 M53 116 h14 M53 121 h14 M53 126 h14", null, Rgb(ParchmentInk), 1.2);
        }
        else
        {
            s.Path("M50 100 h20 v12 q-10 4 -20 0z", Rgb(Parchment));
            s.Path("M53 105 h14", null, Rgb(ParchmentInk), 1.2);
        }

        s.Rect(46, 94, 28, 7, 3, Rgb(Brass));
        var field = s.Ellipse(60, 104, 12, 3, Accent);
        field.Opacity = FieldOpacity;
        if (listen) s.Move(field, ChibiMove.Pulse);

        s.Path("M34 60 q0 -34 26 -34 q26 0 26 34 q0 12 -8 18 v12 h-36 v-12 q-8 -6 -8 -18z", Rgb(Bone));
        s.Path("M60 28 v10", null, Rgb(Shade), 1.5);
        s.Circle(48, 58, 8, Rgb(Socket));
        if (pose == ChibiPose.Done) s.Path("M42 59 q6 -7 12 0", null, Rgb(Bone), 3);
        s.Path("M57 68 l3 -6 3 6z", Rgb(Socket));

        var ring = s.Circle(72, 58, 12, null);
        ring.Stroke = Rgb(Brass);
        ring.StrokeThickness = 4;
        s.Circle(72, 58, 9, Rgb(LensWell));
        switch (pose)
        {
            case ChibiPose.Idle:
                s.Circle(72, 58, 3, Accent).Opacity = DimLens;
                break;
            case ChibiPose.Think:
                var focus = s.Begin();
                s.Path("M72 51 a7 7 0 0 1 7 7", null, Accent, 2.5);
                s.Path("M72 65 a7 7 0 0 1 -7 -7", null, Accent, 2.5);
                s.End();
                s.Move(focus, ChibiMove.Spin, origin: new Point(72, 58));
                break;
            default:
                var lens = s.Circle(72, 58, 5.5, Accent);
                if (listen) s.Move(Glow(lens), ChibiMove.Pulse);
                s.Circle(70, 56, 1.8, Rgb(LensGlint));
                break;
        }

        s.Path("M82 50 l10 -6", null, Rgb(Brass), 3);
        s.Path("M38 40 q-10 -10 -4 -18", null, Rgb(Cable), 2);
        var tip = s.Circle(34, 22, 2.5, Accent);
        if (listen) s.Move(tip, ChibiMove.Pulse);

        s.Rect(44, 76, 32, 14, 3, Rgb(Jaw));
        double[] bars = [48, 54, 60, 66];
        for (var i = 0; i < bars.Length; i++)
        {
            if (listen) s.Move(s.Rect(bars[i], 80, 4, 9, 1, Accent), ChibiMove.Eq, i);
            else s.Rect(bars[i], 81, 4, 7, 1, Rgb(Shade));
        }

        // The servo-scribe writes: a mechadendrite scribbles on the parchment while you speak.
        if (listen)
        {
            var arm = s.Begin();
            s.Path("M80 92 q14 4 10 22", null, Rgb(Cable), 2.5);
            s.Path("M88 112 l-14 10 2 2 14 -10z", Rgb(Quill));
            s.Path("M74 122 l-2 3", null, Rgb(Socket), 1.5);
            s.End();
            s.Move(arm, ChibiMove.Wag, origin: new Point(82, 92));
        }
    }
}
