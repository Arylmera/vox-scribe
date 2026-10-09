using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>What the pill is showing.</summary>
internal enum PillPhase
{
    /// <summary>The key is held: red dot, live level.</summary>
    Recording,

    /// <summary>Transcribing / cleaning the tail.</summary>
    Working,

    /// <summary>A failure notice lingering after the engine idled.</summary>
    Notice,

    /// <summary>The felt latency lingering after a clean finish.</summary>
    Latency,
}

/// <summary>One display frame for a pill face.</summary>
/// <param name="Phase">What is happening.</param>
/// <param name="Level">Input level, 0–1, already perceptual (gain + sqrt).</param>
/// <param name="Elapsed">Utterance time; the latency in <see cref="PillPhase.Latency"/>.</param>
/// <param name="Mode">"RAW", "CLEAN" or "CMD".</param>
/// <param name="Text">Preview or notice text, untruncated; faces show its tail on one line.</param>
internal readonly record struct PillState(PillPhase Phase, double Level, TimeSpan Elapsed, string Mode, string Text);

/// <summary>
/// A theme's pill silhouette. Built fresh for each theme (it caches the theme's brushes),
/// fed one <see cref="PillState"/> per frame by <see cref="HudWindow"/>.
/// </summary>
/// <remarks>
/// Never focusable, never hit-testable. Only the level visual moves, plus a
/// <see cref="Tokens.Motion.PillExpand"/> width transition when the preview line appears.
/// </remarks>
internal abstract class PillFace : Border
{
    /// <summary>Sets the doctrine every face shares.</summary>
    protected PillFace()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, Tokens.Space.Wide);
        Focusable = false;
        IsHitTestVisible = false;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = WidthProperty, Duration = Tokens.Motion.PillExpand },
        };
    }

    /// <summary>The recording dot. Visible only in <see cref="PillPhase.Recording"/>.</summary>
    public abstract Control RecordDot { get; }

    /// <summary>Paints one frame.</summary>
    public abstract void Update(PillState state);

    /// <summary>The face for <paramref name="kind"/>, in the active theme.</summary>
    public static PillFace Create(PillKind kind) => kind switch
    {
        PillKind.Paper => new PaperPill(),
        PillKind.Tide => new TidePill(),
        _ => new FluentPill(), // Orb and Mono arrive in Task 10.
    };

    /// <summary>A circular arc, clockwise from <paramref name="startDegrees"/> (0 = 3 o'clock).</summary>
    internal static StreamGeometry Arc(Point centre, double radius, double startDegrees, double sweepDegrees)
    {
        static Point On(Point c, double r, double degrees)
        {
            var a = degrees * Math.PI / 180;
            return new Point(c.X + (r * Math.Cos(a)), c.Y + (r * Math.Sin(a)));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(On(centre, radius, startDegrees), false);
            ctx.ArcTo(On(centre, radius, startDegrees + sweepDegrees), new Size(radius, radius), 0,
                sweepDegrees > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        return geometry;
    }

    /// <summary>The last <paramref name="max"/> characters, led by an ellipsis when cut.</summary>
    protected static string Tail(string text, int max) => text.Length <= max ? text : "…" + text[^max..];

    /// <summary>"0:04".</summary>
    protected static string Clock(TimeSpan t) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}:{t.Seconds:00}");

    /// <summary>"1.2s".</summary>
    protected static string Seconds(TimeSpan t) =>
        string.Create(CultureInfo.InvariantCulture, $"{t.TotalSeconds:0.0}s");

    /// <summary>The timer slot: the latency once done, the clock otherwise.</summary>
    protected static string Timer(PillState s) => s.Phase == PillPhase.Latency ? Seconds(s.Elapsed) : Clock(s.Elapsed);

    /// <summary>A round recording dot in the record red.</summary>
    protected static Ellipse Dot(double size) => new()
    {
        Width = size,
        Height = size,
        Fill = Tokens.Brushes.Record,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A one-line label that never wraps (the pill must not grow).</summary>
    protected static TextBlock Text(FontFamily family, double size, IBrush brush) => new()
    {
        FontFamily = family,
        FontSize = size,
        Foreground = brush,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.None,
        ClipToBounds = true,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
