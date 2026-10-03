using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>
/// A VU meter: a damped movement, drawn as a strip of segments.
/// </summary>
/// <remarks>
/// <para>
/// The movement is damped rather than driven straight from the signal. A physical VU takes
/// ~300 ms to reach a step and overshoots slightly before settling, and that lag is the
/// instrument's character — kept here even though the strip has no visible needle.
/// </para>
/// <para>
/// The physics live in plain fields stepped by a timer, deliberately kept out of the property
/// system: a styled property invalidated 60 times a second would push a full layout pass each
/// frame for a value only this control's <c>Render</c> ever reads.
/// </para>
/// </remarks>
public sealed class VuMeter : Control
{
    /// <summary>Number of segments in the strip.</summary>
    private const int Segments = 16;

    /// <summary>Segments at the top of the strip that read as the red zone.</summary>
    private const int OverSegments = 2;

    /// <summary>Width of a segment as a fraction of its slot, and its floor in pixels.</summary>
    private const double BarWidthRatio = 0.5;
    private const double MinBarWidth = 2.0;

    /// <summary>Segment height as a fraction of the strip: a ramp rising left to right.</summary>
    private const double BarHeightBase = 0.28;
    private const double BarHeightRamp = 0.5;

    /// <summary>Opacity of an unlit segment while recording, and while idle.</summary>
    private const double UnlitActiveOpacity = 0.18;
    private const double UnlitIdleOpacity = 0.10;

    /// <summary>Integration step and damping of the movement, per frame.</summary>
    private const double NeedleStep = 0.16;
    private const double NeedleDamping = 0.72;

    /// <summary>Current input level, 0…1.</summary>
    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(Level));

    /// <summary>Whether the meter lamp is lit.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<VuMeter, bool>(nameof(IsActive));

    /// <inheritdoc cref="LevelProperty"/>
    public double Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <inheritdoc cref="IsActiveProperty"/>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private double _needle;
    private double _velocity;
    private DispatcherTimer? _ticker;

    static VuMeter() => AffectsRender<VuMeter>(IsActiveProperty);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _ticker = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = Tokens.Motion.MeterFrame,
        };
        _ticker.Tick += (_, _) => { AdvanceNeedle(); InvalidateVisual(); };
        _ticker.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _ticker?.Stop();
        _ticker = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Steps the movement one frame toward the current level.</summary>
    private void AdvanceNeedle()
    {
        // Same perceptual lift as the HUD bars: gain then sqrt, so quiet speech visibly
        // swings the needle instead of trembling at the pin.
        var target = Math.Sqrt(Math.Clamp(Level * Tokens.Motion.LevelGain, 0, 1));
        var rising = target > _needle;
        var time = rising ? Tokens.Motion.NeedleAttackSeconds : Tokens.Motion.NeedleReleaseSeconds;

        var stiffness = 1 / time;
        _velocity += (target - _needle) * stiffness * NeedleStep;
        _velocity *= NeedleDamping;
        _needle = Math.Clamp(_needle + _velocity, 0, 1 + Tokens.Motion.NeedleOvershoot);
    }

    /// <summary>Angular sweep of the needle, degrees off vertical to each side.</summary>
    private const double NeedleSweep = 48;

    /// <summary>Needle pivot below the face bottom, as a fraction of the face height.</summary>
    private const double PivotDrop = 0.55;

    /// <summary>Tick marks across the gauge arc.</summary>
    private const int GaugeTicks = 9;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Dark glass backing.
        var shape = new RoundedRect(bounds, Tokens.Radius.Chip);
        context.DrawRectangle(Tokens.Brushes.MeterFace, null, shape);

        if (Themes.NeedleGauge)
        {
            RenderNeedle(context, bounds);
            var rim = new Pen(new SolidColorBrush(Tokens.Colors.Seam), Tokens.Border.Hairline);
            context.DrawRectangle(null, rim, shape);
            return;
        }

        // Rounded segments rise with the damped level; the last few are the red zone.
        // The accent is read per frame, never cached: it is a live user setting.
        var accent = Tokens.Colors.Accent;
        var inset = Tokens.Space.Snug;
        var slot = (bounds.Width - (inset * 2)) / Segments;
        var barWidth = Math.Max(MinBarWidth, slot * BarWidthRatio);
        var lit = _needle * Segments;

        for (var i = 0; i < Segments; i++)
        {
            var over = i >= Segments - OverSegments;
            var on = i < lit;
            var color = over ? Tokens.Colors.MeterRed : accent;

            var height = bounds.Height
                * (BarHeightBase + (BarHeightRamp * (i + 1) / Segments));
            var x = inset + (i * slot) + ((slot - barWidth) / 2);
            var y = (bounds.Height - height) / 2;

            var opacity = on ? 1.0 : (IsActive ? UnlitActiveOpacity : UnlitIdleOpacity);

            context.DrawRectangle(
                new SolidColorBrush(color, opacity), null,
                new RoundedRect(new Rect(x, y, barWidth, height), barWidth / 2));
        }

        var frame = new Pen(new SolidColorBrush(Tokens.Colors.Seam), Tokens.Border.Hairline);
        context.DrawRectangle(null, frame, shape);
    }

    /// <summary>
    /// The Signal House movement: a real needle swinging over an arc of ticks, driven by
    /// the same damped physics as the segment strip.
    /// </summary>
    private void RenderNeedle(DrawingContext context, Rect bounds)
    {
        // The pivot sits below the face, so only the top of the swing is visible — the
        // classic bench-meter framing.
        var pivot = new Point(bounds.Width / 2, bounds.Height * (1 + PivotDrop));
        var reach = bounds.Height * (PivotDrop + 0.85);

        // Tick marks along the arc; the last two live in the red zone.
        for (var i = 0; i < GaugeTicks; i++)
        {
            var fraction = i / (double)(GaugeTicks - 1);
            var angle = (-NeedleSweep + (2 * NeedleSweep * fraction)) * Math.PI / 180;
            var over = i >= GaugeTicks - OverSegments;

            var outer = new Point(
                pivot.X + (reach * Math.Sin(angle)), pivot.Y - (reach * Math.Cos(angle)));
            var inner = new Point(
                pivot.X + (reach * 0.92 * Math.Sin(angle)), pivot.Y - (reach * 0.92 * Math.Cos(angle)));

            var tick = new Pen(
                new SolidColorBrush(
                    over ? Tokens.Colors.MeterRed : Tokens.Colors.InkOnDeck,
                    over ? 1.0 : Tokens.Emphasis.Outline),
                Tokens.Border.Hairline);
            context.DrawLine(tick, inner, outer);
        }

        // The needle itself, in ink — the accent stays out of the instrument.
        var needleAngle = (-NeedleSweep + (2 * NeedleSweep * Math.Min(_needle, 1)))
            * Math.PI / 180;
        var tip = new Point(
            pivot.X + (reach * 0.97 * Math.Sin(needleAngle)),
            pivot.Y - (reach * 0.97 * Math.Cos(needleAngle)));

        var needle = new Pen(
            new SolidColorBrush(Tokens.Colors.InkOnDeck, IsActive ? 1.0 : Tokens.Emphasis.Soft),
            Tokens.Border.Ring);
        using (context.PushClip(bounds))
        {
            context.DrawLine(needle, pivot, tip);
        }
    }
}
