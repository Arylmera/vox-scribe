using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Chibi;

/// <summary>What a mascot is doing; one drawing per pose.</summary>
internal enum ChibiPose
{
    /// <summary>Dozing on the desktop.</summary>
    Idle,

    /// <summary>The key is held.</summary>
    Listen,

    /// <summary>Transcribing / cleaning the tail.</summary>
    Think,

    /// <summary>The text landed.</summary>
    Done,

    /// <summary>A failure notice is lingering.</summary>
    Worried,
}

/// <summary>How a moving part moves. See <see cref="ChibiMotion"/>.</summary>
internal enum ChibiMove
{
    /// <summary>Height follows the voice (mouths).</summary>
    Talk,

    /// <summary>Height follows the voice with a per-bar wobble, from the bottom.</summary>
    Eq,

    /// <summary>Opacity breathes.</summary>
    Pulse,

    /// <summary>Turns slowly.</summary>
    Spin,

    /// <summary>Closes for a moment every few seconds (eyes).</summary>
    Blink,

    /// <summary>Rocks ±12° (tails).</summary>
    Wag,

    /// <summary>Rocks ±8°, slowly (a thinking head).</summary>
    Tilt,

    /// <summary>Narrows and widens (wings).</summary>
    Flap,

    /// <summary>Widens and flattens with the voice (jelly).</summary>
    Squish,

    /// <summary>Drifts up and fades (smoke, notes).</summary>
    Rise,

    /// <summary>Bobs up and down in whole pixels (pixel arms).</summary>
    Wave,
}

/// <summary>A moving part: the control, how it moves, and its stagger index.</summary>
internal sealed record Moving(Control Target, ChibiMove Move, int Index);

/// <summary>
/// Draws a mascot pose on a 120×140 canvas, the coordinate space of the mockups
/// (<c>.superpowers/brainstorm/…/chibi-all.html</c>), so their SVG path data ports verbatim.
/// </summary>
internal sealed class Sketch
{
    /// <summary>Canvas width, in mockup units.</summary>
    public const double Width = 120;

    /// <summary>Canvas height, in mockup units.</summary>
    public const double Height = 140;

    private readonly Stack<Canvas> _into = new();
    private readonly List<Moving> _moving = [];

    /// <summary>Starts an empty sketch.</summary>
    public Sketch() => _into.Push(Root);

    /// <summary>The drawing.</summary>
    public Canvas Root { get; } = new() { Width = Width, Height = Height };

    /// <summary>The parts the face animates each frame.</summary>
    public IReadOnlyList<Moving> Moving => _moving;

    /// <summary>An SVG path. Stroked paths get round caps and joins.</summary>
    public Avalonia.Controls.Shapes.Path Path(string data, IBrush? fill, IBrush? stroke = null, double width = 0) => Add(new Avalonia.Controls.Shapes.Path
    {
        Data = Geometry.Parse(data),
        Fill = fill,
        Stroke = stroke,
        StrokeThickness = width,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
    });

    /// <summary>An ellipse centred on (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    public Ellipse Ellipse(double cx, double cy, double rx, double ry, IBrush? fill)
    {
        var e = new Ellipse { Width = 2 * rx, Height = 2 * ry, Fill = fill };
        Canvas.SetLeft(e, cx - rx);
        Canvas.SetTop(e, cy - ry);
        return Add(e);
    }

    /// <summary>A circle centred on (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    public Ellipse Circle(double cx, double cy, double r, IBrush? fill) => Ellipse(cx, cy, r, r, fill);

    /// <summary>A rounded rectangle.</summary>
    public Rectangle Rect(double x, double y, double w, double h, double radius, IBrush fill)
    {
        var r = new Rectangle { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Fill = fill };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return Add(r);
    }

    /// <summary>A one-line label centred in a box <paramref name="width"/> wide whose top-left is (x, y).</summary>
    public TextBlock Text(string text, double x, double y, double width, double size, IBrush fill)
    {
        var t = new TextBlock
        {
            Text = text,
            Width = width,
            FontFamily = Tokens.Fonts.Mono,
            FontSize = size,
            Foreground = fill,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        return Add(t);
    }

    /// <summary>Opens a group sharing the canvas coordinates; shapes go into it until <see cref="End"/>.</summary>
    public Canvas Begin()
    {
        var group = Add(new Canvas { Width = Width, Height = Height });
        _into.Push(group);
        return group;
    }

    /// <summary>Closes the group opened by <see cref="Begin"/>.</summary>
    public void End() => _into.Pop();

    /// <summary>
    /// Registers <paramref name="target"/> as a moving part and gives it the transform its move
    /// needs. <paramref name="origin"/> is the pivot in canvas units; by default the part's own
    /// centre (for <see cref="ChibiMove.Eq"/>, its bottom centre). Paths and groups start at the
    /// canvas origin, so their pivot must be given.
    /// </summary>
    public T Move<T>(T target, ChibiMove move, int index = 0, Point? origin = null)
        where T : Control
    {
        target.RenderTransform = move switch
        {
            ChibiMove.Spin or ChibiMove.Wag or ChibiMove.Tilt => new RotateTransform(),
            ChibiMove.Rise or ChibiMove.Wave => new TranslateTransform(),
            ChibiMove.Pulse => null,
            _ => new ScaleTransform(),
        };
        target.RenderTransformOrigin = origin is { } o
            ? new RelativePoint(o.X, o.Y, RelativeUnit.Absolute)
            : move == ChibiMove.Eq ? new RelativePoint(0.5, 1, RelativeUnit.Relative) : RelativePoint.Center;
        _moving.Add(new Moving(target, move, index));
        return target;
    }

    /// <summary>A dash pattern in pixels, converted to Avalonia's multiples of the stroke width.</summary>
    public static AvaloniaList<double> Dashes(double dash, double gap, double strokeWidth) =>
        [dash / strokeWidth, gap / strokeWidth];

    private T Add<T>(T control)
        where T : Control
    {
        _into.Peek().Children.Add(control);
        return control;
    }
}
