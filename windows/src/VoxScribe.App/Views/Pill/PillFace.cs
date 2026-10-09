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

    /// <summary>
    /// Whether the preview/notice line has anything to show. False while cleaning (the
    /// <see cref="PillPhase.Working"/> phase collapses to the compact width, matching the
    /// mockups), even if the engine's last partial text is still sitting there.
    /// </summary>
    protected static bool HasPreview(PillState state) => state.Phase != PillPhase.Working && state.Text.Length > 0;

    /// <summary>
    /// Hosts the preview/notice line and clips it to its own bounds. Paired with
    /// <see cref="ShowPreview"/>: a plain Avalonia panel clamps a right-aligned, non-wrapping
    /// child to the panel's own width during Measure, so the child's <i>bounds</i> never
    /// actually overflow — it just draws its full (unclamped) text starting at that clamped
    /// box's left edge, which crops the newest words on the right. <see cref="TailClip.Tail"/>
    /// instead measures the child at its natural width and arranges it flush with the host's
    /// right edge, so an overflowing line truly extends past the host's left edge and is
    /// cropped there, keeping the newest word — the one just spoken — on screen.
    /// </summary>
    protected static TailClip TailHost(TextBlock text) => new() { ClipToBounds = true, Child = text };

    /// <summary>
    /// Paints <paramref name="text"/> for the preview/notice line inside <paramref name="host"/>.
    /// A lingering failure notice is shown head-first, trimmed on the right with an ellipsis —
    /// the subject of a notice is at its start. A streaming preview is shown tail-first
    /// (<paramref name="host"/> in <see cref="TailClip.Tail"/> mode, untrimmed) so the newest
    /// word is always visible.
    /// </summary>
    protected static void ShowPreview(TailClip host, TextBlock text, PillState state, int tailChars)
    {
        if (state.Phase == PillPhase.Notice)
        {
            host.Tail = false;
            text.HorizontalAlignment = HorizontalAlignment.Stretch;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Text = state.Text;
        }
        else
        {
            host.Tail = true;
            text.TextTrimming = TextTrimming.None;
            text.Text = Tail(state.Text, tailChars);
        }
    }

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

/// <summary>
/// A one-child host used by <see cref="PillFace.ShowPreview"/>. In <see cref="Tail"/> mode the
/// child is measured at its natural (unconstrained) width and arranged flush with the host's
/// right edge: an overflowing line truly extends past the host's left edge, where
/// <see cref="Visual.ClipToBounds"/> crops it, keeping the newest word on screen. Outside Tail
/// mode (a lingering notice) it behaves like an ordinary <see cref="Decorator"/>, stretching
/// the child to the host's width so the child's own <see cref="TextBlock.TextTrimming"/> can
/// ellipsise it instead.
/// </summary>
internal sealed class TailClip : Decorator
{
    /// <summary>True while streaming a preview tail-first; false for a head-first notice.</summary>
    public bool Tail { get; set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!Tail) return base.MeasureOverride(availableSize);

        Child?.Measure(Size.Infinity);
        var childSize = Child?.DesiredSize ?? default;
        var width = double.IsInfinity(availableSize.Width) ? childSize.Width : availableSize.Width;
        return new Size(width, childSize.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!Tail || Child is not { } child) return base.ArrangeOverride(finalSize);

        var x = finalSize.Width - child.DesiredSize.Width;
        child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
        return finalSize;
    }
}
