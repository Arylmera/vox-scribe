using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Netrunner: neon hair, a hood, and a visor that turns into an equaliser while you speak.
/// Accent: the visor, its trim and the ear light.
/// </summary>
internal sealed class NetrunnerChibi(Color accent) : ChibiFace(accent)
{
    private const uint Hair = 0xE040FB;
    private const uint Hood = 0x2B2F45;
    private const uint Shoe = 0x1A1C2A;
    private const uint Skin = 0xF2C9A8;
    private const uint Visor = 0x0B1020;
    private const uint Lip = 0x5A2A3A;
    private const double DimVisor = 0.9;
    private const double DimLine = 0.5;
    private const double LoadSize = 10;
    private const double HoloFill = 0.18;
    private const double HoloEdge = 0.85;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var listen = pose == ChibiPose.Listen;

        s.Path("M30 100 q30 -10 60 0 v26 h-60z", Rgb(Hood));
        s.Path("M40 100 l20 14 20 -14", null, Accent, 2);
        s.Rect(38, 122, 16, 10, 4, Rgb(Shoe));
        s.Rect(66, 122, 16, 10, 4, Rgb(Shoe));
        s.Path("M24 66 q0 -40 36 -40 q36 0 36 40 q0 34 -36 34 q-36 0 -36 -34z", Rgb(Hood));
        s.Ellipse(60, 68, 28, 26, Rgb(Skin));
        s.Path("M32 54 l6 -22 8 14 6 -20 8 16 8 -18 6 18 8 -12 4 24 q-28 -10 -54 0z", Rgb(Hair));

        var visor = s.Rect(34, 56, 52, 16, 8, Rgb(Visor));
        switch (pose)
        {
            case ChibiPose.Listen:
                double[] bars = [40, 48, 56, 64, 72];
                for (var i = 0; i < bars.Length; i++)
                {
                    s.Move(s.Rect(bars[i], 58, 5, 12, 1.5, i % 2 == 1 ? Rgb(Hair) : Accent), ChibiMove.Eq, i);
                }

                break;
            case ChibiPose.Idle:
                visor.Opacity = DimVisor;
                s.Rect(40, 63, 40, 2, 1, Accent).Opacity = DimLine;
                break;
            case ChibiPose.Done:
                s.Path("M42 67 q6 -7 12 0 M66 67 q6 -7 12 0", null, Accent, 3);
                break;
            default:
                s.Move(s.Text(pose == ChibiPose.Worried ? "ERR" : "LOAD", 34, 57, 52, LoadSize, Accent), ChibiMove.Pulse);
                break;
        }

        if (listen) s.Move(s.Ellipse(60, 84, 5, 4, Rgb(Lip)), ChibiMove.Talk);
        else if (pose == ChibiPose.Done) s.Path("M53 82 q7 7 14 0", null, Rgb(Lip), 3);
        else s.Rect(55, 83, 10, 2.5, 1, Rgb(Lip));

        var ear = s.Circle(90, 70, 3, Accent);
        if (listen) s.Move(Glow(ear), ChibiMove.Pulse);

        // A holo screen floats in front while the text is transcribed; lines type in, staggered.
        if (pose == ChibiPose.Think)
        {
            const string Panel = "M70 96 l40 -8 v26 l-40 8z";
            s.Path(Panel, Accent).Opacity = HoloFill;
            s.Path(Panel, null, Accent, 1).Opacity = HoloEdge;
            for (var i = 0; i < 4; i++)
            {
                double y = 100 + (i * 5);
                double length = 26 - (i * 4);
                var line = s.Path(FormattableString.Invariant($"M74 {y} l{length} {-length / 5}"), null, Accent, 1.8);
                s.Move(line, ChibiMove.Pulse, i);
            }
        }
    }
}
