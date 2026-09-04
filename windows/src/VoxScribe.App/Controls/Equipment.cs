using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>
/// A glass card: flat translucent surface, soft corners, hairline border.
/// </summary>
/// <remarks>
/// The Void Glass building block — depth comes from the layered surface and its border,
/// never from bevels or texture.
/// </remarks>
public sealed class BrushedPanel : Decorator
{
    /// <summary>Corner radius of the card.</summary>
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<BrushedPanel, double>(nameof(CornerRadius), Tokens.Radius.Panel);

    /// <inheritdoc cref="CornerRadiusProperty"/>
    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    static BrushedPanel() => AffectsRender<BrushedPanel>(CornerRadiusProperty);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var shape = new RoundedRect(bounds, CornerRadius);
        context.DrawRectangle(Tokens.Brushes.Panel, null, shape);

        var seam = new Pen(new SolidColorBrush(Tokens.Colors.Seam), Tokens.Border.Seam);
        context.DrawRectangle(null, seam, shape);
    }
}

/// <summary>
/// A silkscreened panel label: small, uppercase, tightly tracked.
/// </summary>
/// <remarks>
/// The uppercasing happens here rather than at the call site so a label can never be
/// half-styled — the look depends on all three of size, tracking and case.
/// </remarks>
public sealed class Silkscreen : TextBlock
{
    /// <summary>Uses the larger silkscreen size.</summary>
    public static readonly StyledProperty<bool> IsLargeProperty =
        AvaloniaProperty.Register<Silkscreen, bool>(nameof(IsLarge));

    /// <inheritdoc cref="IsLargeProperty"/>
    public bool IsLarge
    {
        get => GetValue(IsLargeProperty);
        set => SetValue(IsLargeProperty, value);
    }

    /// <summary>Creates an empty label.</summary>
    public Silkscreen()
    {
        FontFamily = Tokens.Fonts.Grotesque;
        FontSize = Tokens.Fonts.Silkscreen;
        FontWeight = FontWeight.Medium;
        LetterSpacing = Tokens.Fonts.SilkscreenTracking;
        Foreground = Tokens.Brushes.Silkscreen;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty && Text is { } text)
        {
            var upper = text.ToUpperInvariant();
            if (!string.Equals(text, upper, StringComparison.Ordinal)) Text = upper;
        }
        else if (change.Property == IsLargeProperty)
        {
            FontSize = IsLarge ? Tokens.Fonts.SilkscreenLarge : Tokens.Fonts.Silkscreen;
        }
    }
}

/// <summary>
/// An indicator lamp behind a lens.
/// </summary>
/// <remarks>
/// A lit lamp gets a specular dot, not a bloom. The brief rules out glow, and real lamps read
/// as lit because of the highlight on the lens rather than light spilling past it.
/// </remarks>
public sealed class Lamp : Control
{
    /// <summary>Opacity of the rim drawn around the lens.</summary>
    private const double RimOpacity = 0.7;

    /// <summary>Specular dot radius, as a fraction of the lens diameter.</summary>
    private const double DotRadiusRatio = 0.15;

    /// <summary>Where the dot sits, as a fraction of the lens radius — up and to the left.</summary>
    private const double DotOffsetX = 0.30;
    private const double DotOffsetY = 0.32;

    /// <summary>Whether the lamp is lit.</summary>
    public static readonly StyledProperty<bool> IsLitProperty =
        AvaloniaProperty.Register<Lamp, bool>(nameof(IsLit));

    /// <summary>
    /// The lamp's colour when lit. Neutral by default — <b>red means recording</b>, so the
    /// one lamp that means that says so itself rather than inheriting it from every lamp.
    /// </summary>
    public static readonly StyledProperty<Color> LampColorProperty =
        AvaloniaProperty.Register<Lamp, Color>(nameof(LampColor), Tokens.Colors.Silkscreen);

    /// <inheritdoc cref="IsLitProperty"/>
    public bool IsLit
    {
        get => GetValue(IsLitProperty);
        set => SetValue(IsLitProperty, value);
    }

    /// <inheritdoc cref="LampColorProperty"/>
    public Color LampColor
    {
        get => GetValue(LampColorProperty);
        set => SetValue(LampColorProperty, value);
    }

    static Lamp() => AffectsRender<Lamp>(IsLitProperty, LampColorProperty);

    /// <summary>Creates a lamp at the token size.</summary>
    public Lamp()
    {
        Width = Tokens.Material.LampSize;
        Height = Tokens.Material.LampSize;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = size / 2;

        var lens = new SolidColorBrush(LampColor, IsLit ? 1 : Tokens.Material.LampUnlitOpacity);
        context.DrawEllipse(lens, null, centre, radius, radius);

        var rim = new Pen(new SolidColorBrush(Tokens.Colors.Seam, RimOpacity), Tokens.Border.Hairline);
        context.DrawEllipse(null, rim, centre, radius, radius);

        if (!IsLit) return;

        var specular = new SolidColorBrush(Tokens.Colors.Specular, Tokens.Material.LampSpecular);
        var dot = size * DotRadiusRatio;
        context.DrawEllipse(
            specular, null,
            new Point(centre.X - (radius * DotOffsetX), centre.Y - (radius * DotOffsetY)),
            dot, dot);
    }
}

/// <summary>
/// A transport key: a rounded pill button.
/// </summary>
/// <remarks>
/// Engaged tints the pill with the engaged colour; pressed darkens it. Flat and quiet,
/// the Void Glass way.
/// </remarks>
public sealed class TransportKey : Button
{
    /// <summary>How strongly the engaged colour tints the pill's face and its edge.</summary>
    private const double EngagedFillOpacity = 0.16;
    private const double EngagedEdgeOpacity = 0.55;

    /// <summary>How far the face dims while the key is held down.</summary>
    private const double PressedFaceOpacity = 0.6;

    /// <summary>Whether this key is latched down.</summary>
    public static readonly StyledProperty<bool> IsEngagedProperty =
        AvaloniaProperty.Register<TransportKey, bool>(nameof(IsEngaged));

    /// <summary>
    /// Label and tint colour when engaged. Ink by default: a key is latched, not recording,
    /// and a red default made every unconfigured key one <c>IsEngaged</c> away from breaking
    /// the rule that nothing but the transport is red.
    /// </summary>
    public static readonly StyledProperty<Color> EngagedColorProperty =
        AvaloniaProperty.Register<TransportKey, Color>(nameof(EngagedColor), Tokens.Colors.Ink);

    /// <inheritdoc cref="IsEngagedProperty"/>
    public bool IsEngaged
    {
        get => GetValue(IsEngagedProperty);
        set => SetValue(IsEngagedProperty, value);
    }

    /// <inheritdoc cref="EngagedColorProperty"/>
    public Color EngagedColor
    {
        get => GetValue(EngagedColorProperty);
        set => SetValue(EngagedColorProperty, value);
    }

    static TransportKey() => AffectsRender<TransportKey>(IsEngagedProperty, IsPressedProperty);

    /// <summary>Creates a key at the token dimensions.</summary>
    public TransportKey()
    {
        MinWidth = Tokens.Material.KeyMinWidth;
        Height = Tokens.Material.KeyHeight;
        Padding = new Thickness(Tokens.Space.Base, 0);
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        // Transparent, never null: a null background is not hit-tested, so only the
        // content — a glyph stroke, a word — would answer the pointer and the rest of the
        // key would silently swallow clicks. The face is painted in Render either way.
        Background = Brushes.Transparent;
        BorderBrush = null;
        FontFamily = Tokens.Fonts.Grotesque;
        FontSize = Tokens.Fonts.Silkscreen;
        FontWeight = FontWeight.Medium;
        Foreground = Tokens.Brushes.Ink;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // A full pill: the radius is half the height, whatever the height is.
        var shape = new RoundedRect(bounds, bounds.Height / 2);

        var fill = IsEngaged
            ? new SolidColorBrush(EngagedColor, EngagedFillOpacity)
            : new SolidColorBrush(Tokens.Colors.Cap, IsPressed ? PressedFaceOpacity : 1.0);
        context.DrawRectangle(fill, null, shape);

        var edge = IsEngaged
            ? new SolidColorBrush(EngagedColor, EngagedEdgeOpacity)
            : new SolidColorBrush(Tokens.Colors.Seam);
        context.DrawRectangle(null, new Pen(edge, Tokens.Border.Hairline), shape);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Foreground is updated HERE, not in Render. Assigning a property during the render
        // pass invalidates the visual mid-pass, and Avalonia throws "Visual was invalidated
        // during the render pass" rather than merely logging it. Render must be a pure
        // function of current state.
        if (change.Property == IsEngagedProperty || change.Property == EngagedColorProperty)
        {
            Foreground = new SolidColorBrush(IsEngaged ? EngagedColor : Tokens.Colors.Ink);
        }
    }
}

/// <summary>
/// A navigation rail key: a square icon button on the left rail.
/// </summary>
/// <remarks>
/// Engaged tints the key with the accent and recolours its stroke icon; otherwise it sits
/// flat on the rail with a silkscreen-grey icon. No borders — the rail separates by tone.
/// </remarks>
public sealed class RailKey : Button
{
    /// <summary>How faintly the accent washes the active key. A tint, not a fill.</summary>
    private const double EngagedWashOpacity = 0.10;

    /// <summary>Whether this key is the active section.</summary>
    public static readonly StyledProperty<bool> IsEngagedProperty =
        AvaloniaProperty.Register<RailKey, bool>(nameof(IsEngaged));

    /// <inheritdoc cref="IsEngagedProperty"/>
    public bool IsEngaged
    {
        get => GetValue(IsEngagedProperty);
        set => SetValue(IsEngagedProperty, value);
    }

    static RailKey() => AffectsRender<RailKey>(IsEngagedProperty, IsPressedProperty);

    /// <summary>Creates a key holding a stroke icon parsed from SVG path data.</summary>
    public RailKey(string iconPathData)
    {
        Width = Tokens.Material.RailKeySize;
        Height = Tokens.Material.RailKeySize;
        // Transparent, never null: a null background is not hit-tested, so only the
        // content — a glyph stroke, a word — would answer the pointer and the rest of the
        // key would silently swallow clicks. The face is painted in Render either way.
        Background = Brushes.Transparent;
        BorderBrush = null;
        Padding = new Thickness(0);
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;

        Content = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(iconPathData),
            Stroke = Tokens.Brushes.Silkscreen,
            StrokeThickness = Tokens.Material.RailIconStroke,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Fill = null,
            Width = Tokens.Material.RailIconSize,
            Height = Tokens.Material.RailIconSize,
            Stretch = Stretch.Uniform,
        };
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var shape = new RoundedRect(bounds, Tokens.Radius.RailKey);

        if (IsEngaged)
        {
            context.DrawRectangle(
                new SolidColorBrush(Tokens.Colors.Accent, EngagedWashOpacity), null, shape);
        }
        else if (IsPressed || IsPointerOver)
        {
            context.DrawRectangle(new SolidColorBrush(Tokens.Colors.Hover), null, shape);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Recoloured here, not in Render — see TransportKey for why.
        if (change.Property == IsEngagedProperty
            && Content is Avalonia.Controls.Shapes.Path icon)
        {
            icon.Stroke = IsEngaged
                ? new SolidColorBrush(Tokens.Colors.Accent)
                : Tokens.Brushes.Silkscreen;
        }
    }
}

/// <summary>
/// The round record button in the voice band: a red-tinted disc holding the record lamp.
/// </summary>
public sealed class RecordButton : Button
{
    /// <summary>How strongly red tints the disc at rest, and while it is held down.</summary>
    private const double DiscOpacity = 0.14;
    private const double PressedDiscOpacity = 0.24;

    static RecordButton() => AffectsRender<RecordButton>(IsPressedProperty);

    /// <summary>Creates the button at the token size.</summary>
    public RecordButton()
    {
        Width = Tokens.Material.RecordKeySize;
        Height = Tokens.Material.RecordKeySize;
        // Transparent, never null: a null background is not hit-tested, so only the
        // content — a glyph stroke, a word — would answer the pointer and the rest of the
        // key would silently swallow clicks. The face is painted in Render either way.
        Background = Brushes.Transparent;
        BorderBrush = null;
        Padding = new Thickness(0);
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var fill = new SolidColorBrush(
            Tokens.Colors.Record, IsPressed ? PressedDiscOpacity : DiscOpacity);
        context.DrawEllipse(fill, null, centre, size / 2, size / 2);
    }
}

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

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Dark glass backing.
        var shape = new RoundedRect(bounds, Tokens.Radius.Chip);
        context.DrawRectangle(Tokens.Brushes.MeterFace, null, shape);

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
}
