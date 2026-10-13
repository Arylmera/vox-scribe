using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Owl scribe: round glasses, ear tufts, a quill under the wing. Eyes widen and the beak talks
/// while you speak; the head tilts while thinking. Accent: the glasses rims.
/// </summary>
internal sealed class OwlChibi(Color accent) : ChibiFace(accent)
{
    private const uint Feather = 0x8A6A4A;
    private const uint Belly = 0xD9C3A0;
    private const uint BellyLine = 0xB89B73;
    private const uint Tuft = 0x6E5236;
    private const uint EyeDisc = 0xF3E6CF;
    private const uint Ink = 0x2B1E14;
    private const uint Beak = 0xE8A33D;
    private const uint Quill = 0xF4F1EA;
    private const uint Glint = 0xFFFFFF;
    private const double RimWidth = 2.5;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        var ink = Rgb(Ink);
        var listen = pose == ChibiPose.Listen;

        var head = s.Begin();
        if (pose == ChibiPose.Think) s.Move(head, ChibiMove.Swivel, origin: new Point(60, 100));

        s.Ellipse(60, 88, 34, 40, Rgb(Feather));
        s.Ellipse(60, 100, 22, 24, Rgb(Belly));
        s.Path("M50 96 q4 4 8 0 M62 96 q4 4 8 0 M54 106 q4 4 8 0 M60 114 q4 4 8 0", null, Rgb(BellyLine), 2);
        var tufts = s.Path("M30 52 l-4 -20 16 12z M90 52 l4 -20 -16 12z", Rgb(Tuft));
        if (listen) s.Move(tufts, ChibiMove.Pulse);

        s.Circle(48, 66, 13, Rgb(EyeDisc));
        s.Circle(72, 66, 13, Rgb(EyeDisc));
        switch (pose)
        {
            case ChibiPose.Idle:
                SleepyEyes(s, 48, 72, 66, ink);
                break;
            case ChibiPose.Done:
                // A wink: one happy eye, one open.
                s.Path("M42 68 q6 -7 12 0", null, ink, 3.5);
                s.Circle(72, 66, 5, ink);
                s.Circle(70, 64, 2, Rgb(Glint));
                break;
            default:
                var r = listen ? 7 : 5;
                var up = pose is ChibiPose.Think or ChibiPose.Worried;
                s.Circle(48, 66, r, ink);
                s.Circle(72, 66, r, ink);
                s.Circle(up ? 50 : 46, up ? 62 : 64, 2, Rgb(Glint));
                s.Circle(up ? 74 : 70, up ? 62 : 64, 2, Rgb(Glint));
                break;
        }

        foreach (var cx in new[] { 48.0, 72.0 })
        {
            var rim = s.Circle(cx, 66, 13, null);
            rim.Stroke = Accent;
            rim.StrokeThickness = RimWidth;
        }

        s.Path("M61 66 h-2", null, Accent, RimWidth);

        var beak = s.Path(listen ? "M56 78 l4 10 4 -10z" : "M56 78 l4 7 4 -7z", Rgb(Beak));
        if (listen) s.Move(beak, ChibiMove.Talk, origin: new Point(60, 78));

        s.Path("M90 92 q14 -2 18 -22 q-12 6 -16 16z", Rgb(Quill));
        s.Path("M90 94 l16 -22", null, Rgb(Tuft), 1.5);
        s.Path("M48 126 l-3 6 M52 126 v6 M68 126 v6 M72 126 l3 6", null, Rgb(Beak), 3);
        s.End();
    }
}
