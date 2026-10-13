using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Pill;

namespace VoxScribe.App.Views.Chibi;

/// <summary>
/// A desktop mascot: a character drawn per <see cref="ChibiPose"/>, a speech bubble with what
/// the pill would say, and the recording dot. Fed one <see cref="PillState"/> per frame by
/// <see cref="HudWindow"/>, exactly like a pill face, plus <see cref="PillPhase.Idle"/>.
/// </summary>
/// <remarks>
/// Unlike a pill face it is hit-testable (it can be dragged) — but never focusable. Colours
/// are captured at construction; the window swaps faces between dictations, never mid-one.
/// </remarks>
internal abstract class ChibiFace : PillFace
{
    private const double DotSize = 10;
    private const double DotInset = 2;
    private const double BubbleMetaSize = 11;
    private const double BubbleTextSize = 14;
    private const int BubbleChars = 34;
    private const double PulseStagger = 0.2;
    private const double WagPeriod = 1.2;
    private const double WagDegrees = 12;
    private const double TiltPeriod = 2;
    private const double TiltDegrees = 8;
    private const double BlinkClosed = 0.1;
    private const double GlowBlur = 10;
    private const double GlowOpacity = 0.8;
    private const uint DotsGrey = 0xCFD3DC;
    private const uint SweatBlue = 0x8FD3FF;
    private const uint Glint = 0xFFFFFF;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Ellipse _dot = Dot(DotSize);
    private readonly Border _bubble;
    private readonly TextBlock _bubbleHead;
    private readonly TextBlock _bubbleText;
    private readonly TailClip _bubbleHost;
    private readonly Decorator _figure = new();
    private readonly TranslateTransform _lift = new();
    private Sketch _sketch = new();
    private ChibiPose? _pose;
    private TimeSpan _poseStart;

    /// <summary>Builds the mascot with <paramref name="accent"/> on its accent part.</summary>
    protected ChibiFace(Color accent)
    {
        AccentColor = accent;
        Accent = new SolidColorBrush(accent);

        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = default;
        Transitions = null;
        IsHitTestVisible = true;

        // Transparent, not null: the whole face, gaps included, is something to grab.
        Background = Brushes.Transparent;

        _bubbleHead = Text(Tokens.Fonts.Mono, BubbleMetaSize, Tokens.Brushes.InkSecondary);
        _bubbleText = Text(Tokens.Fonts.Grotesque, BubbleTextSize, Tokens.Brushes.Ink);
        _bubbleHost = TailHost(_bubbleText);
        _bubble = new Border
        {
            Width = Tokens.Size.MascotBubbleWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Tokens.Colors.PillFill),
            BorderBrush = new SolidColorBrush(Tokens.Colors.PillEdge),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(Tokens.Radius.Panel),
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            IsVisible = false,
            Child = new StackPanel { Spacing = Tokens.Space.Tight, Children = { _bubbleHead, _bubbleHost } },
        };

        _dot.HorizontalAlignment = HorizontalAlignment.Right;
        _dot.VerticalAlignment = VerticalAlignment.Top;
        _dot.Margin = new Thickness(0, DotInset, DotInset, 0);
        _dot.IsVisible = false;

        _figure.RenderTransform = _lift;
        var figure = new Grid
        {
            Width = Tokens.Size.MascotWidth,
            Height = Tokens.Size.MascotHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { new Viewbox { Child = _figure }, _dot },
        };

        Child = new StackPanel { Spacing = Tokens.Space.Snug, Children = { _bubble, figure } };
    }

    /// <summary>The accent part's colour.</summary>
    protected Color AccentColor { get; }

    /// <summary>The accent part's brush.</summary>
    protected IBrush Accent { get; }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <summary>The pose currently drawn.</summary>
    internal ChibiPose Pose => _pose ?? ChibiPose.Idle;

    /// <summary>The speech bubble.</summary>
    internal Control Bubble => _bubble;

    /// <summary>Draws the character in <paramref name="pose"/>. Called on every pose change.</summary>
    protected abstract void Draw(Sketch s, ChibiPose pose);

    /// <inheritdoc />
    public override void Update(PillState state) => Update(state, _clock.Elapsed);

    /// <summary>Paints one frame at ambient time <paramref name="now"/> (its own clock in the app).</summary>
    internal void Update(PillState state, TimeSpan now)
    {
        var pose = state.Phase switch
        {
            PillPhase.Recording => ChibiPose.Listen,
            PillPhase.Working => ChibiPose.Think,
            PillPhase.Latency => ChibiPose.Done,
            PillPhase.Notice => ChibiPose.Worried,
            _ => ChibiPose.Idle,
        };
        if (pose != _pose)
        {
            _pose = pose;
            _poseStart = now;
            Redraw(pose);
        }

        _dot.IsVisible = state.Phase == PillPhase.Recording;
        Speak(state);
        Animate(now.TotalSeconds, (now - _poseStart).TotalSeconds, state.Phase == PillPhase.Recording ? state.Level : 0);
    }

    /// <summary>A brush for a character colour <c>0xRRGGBB</c>.</summary>
    protected static SolidColorBrush Rgb(uint rgb) => new(Tokens.Colors.Rgb(rgb));

    /// <summary>Two closed, downturned arcs centred on <paramref name="x1"/> and <paramref name="x2"/>.</summary>
    protected static void SleepyEyes(Sketch s, double x1, double x2, double y, IBrush ink, double width = 3) =>
        s.Path(FormattableString.Invariant($"M{x1 - 6} {y} q6 4 12 0 M{x2 - 6} {y} q6 4 12 0"), null, ink, width);

    /// <summary>Two ^^ arcs centred on <paramref name="x1"/> and <paramref name="x2"/>.</summary>
    protected static void HappyEyes(Sketch s, double x1, double x2, double y, IBrush ink, double width = 3.5) =>
        s.Path(FormattableString.Invariant($"M{x1 - 6} {y} q6 -7 12 0 M{x2 - 6} {y} q6 -7 12 0"), null, ink, width);

    /// <summary>A soft glow around <paramref name="control"/>, the accent by default.</summary>
    protected T Glow<T>(T control, Color? colour = null)
        where T : Control
    {
        control.Effect = new DropShadowEffect
        {
            Color = colour ?? AccentColor,
            BlurRadius = GlowBlur,
            OffsetX = 0,
            OffsetY = 0,
            Opacity = GlowOpacity,
        };
        return control;
    }

    private void Redraw(ChibiPose pose)
    {
        _sketch = new Sketch();
        Draw(_sketch, pose);

        switch (pose)
        {
            case ChibiPose.Think:
                var grey = Rgb(DotsGrey);
                _sketch.Move(_sketch.Circle(92, 18, 3, grey), ChibiMove.Pulse, 0);
                _sketch.Move(_sketch.Circle(101, 12, 3, grey), ChibiMove.Pulse, 1);
                _sketch.Move(_sketch.Circle(110, 6, 3, grey), ChibiMove.Pulse, 2);
                break;
            case ChibiPose.Done:
                var sparkles = _sketch.Path(
                    "M14 30 l3 7 7 3 -7 3 -3 7 -3 -7 -7 -3 7 -3z M104 22 l2 5 5 2 -5 2 -2 5 -2 -5 -5 -2 5 -2z", Accent);
                _sketch.Move(sparkles, ChibiMove.Pulse);
                break;
            case ChibiPose.Worried:
                _sketch.Path("M100 28 q7 10 0 15 q-7 -5 0 -15z", Rgb(SweatBlue));
                _sketch.Circle(99, 38, 1.5, Rgb(Glint));
                break;
        }

        _figure.Child = _sketch.Root;
    }

    private void Speak(PillState state)
    {
        _bubble.IsVisible = state.Phase != PillPhase.Idle;
        if (!_bubble.IsVisible) return;

        var modeWord = state.Mode switch { "CLEAN" => "clean", "CMD" => "cmd", "EDIT" => "edit", _ => "raw" };
        _bubbleHead.Text = state.Phase switch
        {
            PillPhase.Working => (state.Mode switch { "CLEAN" => "tidying…", "EDIT" => "editing…", _ => "transcribing…" })
                + " · " + modeWord,
            PillPhase.Notice => "note",
            _ => TimerLine(state, modeWord),
        };

        var show = HasPreview(state);
        if (show) ShowPreview(_bubbleHost, _bubbleText, state, BubbleChars);
        _bubbleHost.IsVisible = show;
    }

    private void Animate(double t, double sincePose, double level)
    {
        _lift.Y = _pose == ChibiPose.Done ? ChibiMotion.Hop(sincePose) : ChibiMotion.Bob(t);

        foreach (var (target, move, index) in _sketch.Moving)
        {
            switch (move)
            {
                case ChibiMove.Talk:
                    ((ScaleTransform)target.RenderTransform!).ScaleY = ChibiMotion.Talk(level);
                    break;
                case ChibiMove.Eq:
                    ((ScaleTransform)target.RenderTransform!).ScaleY = ChibiMotion.Eq(level, index, t);
                    break;
                case ChibiMove.Blink:
                    ((ScaleTransform)target.RenderTransform!).ScaleY = ChibiMotion.Blinking(t) ? BlinkClosed : 1;
                    break;
                case ChibiMove.Flap:
                    ((ScaleTransform)target.RenderTransform!).ScaleX = ChibiMotion.Flap(t);
                    break;
                case ChibiMove.Squish:
                    var (x, y) = ChibiMotion.Squish(level);
                    var squash = (ScaleTransform)target.RenderTransform!;
                    squash.ScaleX = x;
                    squash.ScaleY = y;
                    break;
                case ChibiMove.Pulse:
                    target.Opacity = ChibiMotion.Pulse(t + (index * PulseStagger));
                    break;
                case ChibiMove.Spin:
                    ((RotateTransform)target.RenderTransform!).Angle = ChibiMotion.Spin(t);
                    break;
                case ChibiMove.Wag:
                    ((RotateTransform)target.RenderTransform!).Angle = ChibiMotion.Swing(t, WagPeriod, WagDegrees);
                    break;
                case ChibiMove.Tilt:
                    ((RotateTransform)target.RenderTransform!).Angle = ChibiMotion.Swing(t, TiltPeriod, TiltDegrees);
                    break;
                case ChibiMove.Rise:
                    var (dy, opacity) = ChibiMotion.Rise(t, index);
                    ((TranslateTransform)target.RenderTransform!).Y = dy;
                    target.Opacity = opacity;
                    break;
                case ChibiMove.Wave:
                    ((TranslateTransform)target.RenderTransform!).Y = ChibiMotion.Wave(t, index);
                    break;
            }
        }
    }
}
