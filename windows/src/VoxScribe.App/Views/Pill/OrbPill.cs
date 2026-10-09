using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>
/// Orb: no body, no waveform — the orb is the meter. The red dot rides its rim; the preview
/// sits in a small glass capsule beside it.
/// </summary>
internal sealed class OrbPill : PillFace
{
    private const double DotSize = 9;
    private const double CompactWidth = 240;
    private const double CapsuleWidth = 270;
    private const double CapsuleRadius = 16;
    private const double MetaSize = 10;
    private const double LabelSize = 11;
    private const double TextSize = 14;
    private const double RimCos45 = 0.7071;
    private const int PreviewChars = 34;

    private readonly OrbGlyph _orb = new();
    private readonly Ellipse _dot = Dot(DotSize);
    private readonly TextBlock _label;
    private readonly TextBlock _capsuleMeta;
    private readonly TextBlock _capsuleText;
    private readonly TailClip _capsuleTextHost;
    private readonly Border _capsule;

    /// <summary>Builds the orb in the active theme.</summary>
    public OrbPill()
    {
        Width = CompactWidth;
        Background = null; // No body: the orb is the pill.

        _label = Text(Tokens.Fonts.Mono, LabelSize, Tokens.Brushes.InkSecondary);
        _capsuleMeta = Text(Tokens.Fonts.Mono, MetaSize, Tokens.Brushes.InkSecondary);
        _capsuleMeta.LetterSpacing = Tokens.Fonts.SilkscreenTracking;
        _capsuleText = Text(Tokens.Fonts.Grotesque, TextSize, Tokens.Brushes.Ink);
        _capsuleTextHost = TailHost(_capsuleText);

        _capsule = new Border
        {
            Width = 0,
            IsVisible = false,
            ClipToBounds = true,
            Background = new SolidColorBrush(Tokens.Colors.PillFill),
            BorderBrush = new SolidColorBrush(Tokens.Colors.PillEdge),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            CornerRadius = new CornerRadius(CapsuleRadius),
            Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Snug),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Spacing = Tokens.Space.Hair, Children = { _capsuleMeta, _capsuleTextHost } },
            Transitions = new Transitions
            {
                new DoubleTransition { Property = WidthProperty, Duration = Tokens.Motion.PillExpand },
            },
        };

        var stage = new Canvas
        {
            Width = _orb.Width,
            Height = _orb.Height,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _orb, _dot },
        };

        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            Children = { stage, _label, _capsule },
        };
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        var working = state.Phase == PillPhase.Working;
        _orb.Push(recording ? state.Level : 0, working);

        // The dot sits on the rim at 45°, so it rides out as the orb swells.
        _dot.IsVisible = recording;
        var r = _orb.Diameter / 2;
        var c = _orb.Width / 2;
        Canvas.SetLeft(_dot, c + (r * RimCos45) - (DotSize / 2));
        Canvas.SetTop(_dot, c - (r * RimCos45) - (DotSize / 2));

        var meta = working ? "POLISHING · CLEAN" : $"{Timer(state)} · {state.Mode}";
        var open = HasPreview(state);
        _label.Text = meta;
        _label.IsVisible = !open;
        _capsuleMeta.Text = meta;
        if (open) ShowPreview(_capsuleTextHost, _capsuleText, state, PreviewChars);
        _capsule.IsVisible = open;
        _capsule.Width = open ? CapsuleWidth : 0;
        Width = open ? _orb.Width + Tokens.Space.Base + CapsuleWidth : CompactWidth;
    }
}

/// <summary>
/// The glowing orb: diameter follows the level (36 px silence → 76 px peak), two halos ripple
/// out while speaking, a spinner arc circles it while working.
/// </summary>
internal sealed class OrbGlyph : Control
{
    private const double Box = 96;
    private const double MinDiameter = 36;
    private const double DiameterRange = 40;
    private const double WorkingDiameter = 40;
    private const double Smoothing = 0.35;
    private const double SpeakingLevel = 0.05;
    private const double HaloSeconds = 1.6;
    private const double HaloOffset = 0.5;
    private const double HaloFrom = 0.9;
    private const double HaloGrowth = 0.6;
    private const double HaloOpacity = 0.55;
    private const double HaloOuterStroke = 1.5;
    private const double HaloInnerStroke = 1;
    private const double GlowScale = 1.6;
    private const double GlowInner = 0.4;
    private const byte GlowAlpha = 0x88;
    private const double CoreX = 0.34;
    private const double CoreY = 0.28;
    private const double CoreRadius = 0.75;
    private const double SecondaryStop = 0.24;
    private const double AccentStop = 0.62;
    private const double SpinnerDiameter = 64;
    private const double SpinnerStroke = 3;
    private const double SpinnerSweep = 120;
    private const double SpinStep = 10;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // Captured once, not re-read from Tokens on every frame: a theme change must repaint only
    // between dictations, never recolour a live orb mid-utterance.
    private readonly Color _accent = Tokens.Colors.Accent;
    private readonly Color _second = Tokens.Colors.AccentSecondary;
    private readonly Color _specular = Tokens.Colors.Specular;
    private readonly Color _clear;
    private readonly RadialGradientBrush _glow;
    private readonly RadialGradientBrush _body;
    private readonly Pen _spinnerPen;

    private double _diameter = MinDiameter;
    private double _spin;
    private bool _speaking;
    private bool _working;

    /// <summary>Creates the 96 px stage and captures the active theme's brushes/pens once.</summary>
    public OrbGlyph()
    {
        Width = Box;
        Height = Box;

        _clear = Color.FromArgb(0, _accent.R, _accent.G, _accent.B);

        _glow = new RadialGradientBrush();
        _glow.GradientStops.Add(new GradientStop(Color.FromArgb(GlowAlpha, _accent.R, _accent.G, _accent.B), GlowInner));
        _glow.GradientStops.Add(new GradientStop(_clear, 1));

        _body = new RadialGradientBrush
        {
            Center = new RelativePoint(CoreX, CoreY, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(CoreX, CoreY, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(CoreRadius, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(CoreRadius, RelativeUnit.Relative),
        };
        _body.GradientStops.Add(new GradientStop(_specular, 0));
        _body.GradientStops.Add(new GradientStop(_second, SecondaryStop));
        _body.GradientStops.Add(new GradientStop(_accent, AccentStop));
        _body.GradientStops.Add(new GradientStop(_clear, 1));

        _spinnerPen = new Pen(new SolidColorBrush(_second), SpinnerStroke, lineCap: PenLineCap.Round);
    }

    /// <summary>The orb's current diameter, for placing the rim dot.</summary>
    public double Diameter => _diameter;

    /// <summary>Feeds one frame.</summary>
    public void Push(double level, bool working)
    {
        var target = working ? WorkingDiameter : MinDiameter + (DiameterRange * level);
        _diameter += (target - _diameter) * Smoothing;
        _speaking = !working && level > SpeakingLevel;
        _working = working;
        if (working) _spin = (_spin + SpinStep) % 360;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var r = _diameter / 2;

        context.DrawEllipse(_glow, null, c, r * GlowScale, r * GlowScale);

        if (_speaking)
        {
            var t0 = _clock.Elapsed.TotalSeconds / HaloSeconds;
            for (var k = 0; k < 2; k++)
            {
                var t = (t0 + (k * HaloOffset)) % 1;
                var hr = r * (HaloFrom + (HaloGrowth * t));
                var pen = new Pen(new SolidColorBrush(k == 0 ? _second : _accent, HaloOpacity * (1 - t)),
                    k == 0 ? HaloOuterStroke : HaloInnerStroke);
                context.DrawEllipse(null, pen, c, hr, hr);
            }
        }

        if (_working)
        {
            context.DrawGeometry(null, _spinnerPen, PillFace.Arc(c, SpinnerDiameter / 2, _spin, SpinnerSweep));
        }

        context.DrawEllipse(_body, null, c, r, r);
    }
}
