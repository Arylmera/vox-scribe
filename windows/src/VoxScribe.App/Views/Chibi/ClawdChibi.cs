using Avalonia;
using Avalonia.Media;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// Clawd, Claude Code's pixel crab: a terracotta block, two eyes, stubby arms, four legs, drawn
/// aliased on a 6-unit grid. Waves its arms while you speak. The body never takes the accent;
/// the listening waves and the sparkles do.
/// </summary>
/// <remarks>
/// <see cref="Body"/> is Anthropic's official terracotta. It sits at hue 14.8°, inside the red
/// detector's window; the owner chose it anyway (2026-10-13), and <c>ChibiTests</c> exempts
/// exactly this colour on Clawd and nothing else. Red still means recording everywhere else.
/// </remarks>
internal sealed class ClawdChibi(Color accent) : ChibiFace(accent)
{
    /// <summary>Anthropic's terracotta, <c>#D97757</c>.</summary>
    internal const uint Body = 0xD97757;

    private const uint Eye = 0x1A1A1A;
    private const double Unit = 6;
    private const double OriginX = 12;
    private const double OriginY = 50;

    /// <inheritdoc />
    protected override void Draw(Sketch s, ChibiPose pose)
    {
        RenderOptions.SetEdgeMode(s.Root, EdgeMode.Aliased);
        var body = Rgb(Body);
        var eye = Rgb(Eye);

        Pixel(s, 2, 0, 12, 6, body);
        var left = Pixel(s, 0, 3, 2, 2, body);
        var right = Pixel(s, 14, 3, 2, 2, body);
        if (pose == ChibiPose.Listen)
        {
            s.Move(left, ChibiMove.Wave, 0);
            s.Move(right, ChibiMove.Wave, 1);
        }

        foreach (var x in new[] { 3, 5, 10, 12 }) Pixel(s, x, 6, 1, 2, body);

        switch (pose)
        {
            case ChibiPose.Idle:
                Pixel(s, 4, 2, 2, 1, eye);
                Pixel(s, 10, 2, 2, 1, eye);
                break;
            case ChibiPose.Think or ChibiPose.Worried:
                Pixel(s, 5, 1, 1, 2, eye);
                Pixel(s, 11, 1, 1, 2, eye);
                break;
            case ChibiPose.Done:
                foreach (var (x, y) in new[] { (4, 2), (5, 1), (6, 2), (10, 2), (11, 1), (12, 2) }) Pixel(s, x, y, 1, 1, eye);
                break;
            default:
                var eyes = s.Begin();
                Pixel(s, 4, 1, 1, 3, eye);
                Pixel(s, 11, 1, 1, 3, eye);
                s.End();
                s.Move(eyes, ChibiMove.Blink, origin: new Point(OriginX + (8 * Unit), OriginY + (2.5 * Unit)));
                foreach (var (x, y) in new[] { (17, 1), (18, 0), (17, 5), (18, 6) }) s.Move(Pixel(s, x, y, 1, 1, Accent), ChibiMove.Pulse);
                break;
        }
    }

    private static Avalonia.Controls.Shapes.Rectangle Pixel(Sketch s, double x, double y, double w, double h, IBrush fill) =>
        s.Rect(OriginX + (x * Unit), OriginY + (y * Unit), w * Unit, h * Unit, 0, fill);
}
