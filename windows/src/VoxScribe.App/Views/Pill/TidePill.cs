using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>Tide: a soft capsule with a liquid wave and its fill.</summary>
internal sealed class TidePill : PillFace
{
    private const double CompactWidth = 300;
    private const double PreviewWidth = 400;
    private const double DotSize = 10;
    private const double RowHeight = 30;
    private const double MetaSize = 12;
    private const double BadgeSize = 11;
    private const double WorkingSize = 13;
    private const double PreviewSize = 15;
    private const int PreviewChars = 46;
    private const uint ShadowArgb = 0x660A283C;
    private const double ShadowY = 16;
    private const double ShadowBlur = 36;
    private const double ShadowSpread = -14;

    private readonly Ellipse _dot = Dot(DotSize);
    private readonly TextBlock _working;
    private readonly LiquidWave _wave = new() { Height = RowHeight };
    private readonly TextBlock _timer;
    private readonly TextBlock _badge;
    private readonly TextBlock _preview;
    private readonly TailClip _previewHost;

    /// <summary>Builds the capsule in the active theme.</summary>
    public TidePill()
    {
        Width = CompactWidth;
        Background = new SolidColorBrush(Tokens.Colors.PillFill);
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetY = ShadowY,
            Blur = ShadowBlur,
            Spread = ShadowSpread,
            Color = Color.FromUInt32(ShadowArgb),
        });
        Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Snug);

        _working = Text(Tokens.Fonts.Grotesque, WorkingSize, Tokens.Brushes.Accent);
        _working.FontWeight = FontWeight.Bold;
        _working.Text = "Smoothing it out";
        _timer = Text(Tokens.Fonts.Grotesque, MetaSize, Tokens.Brushes.InkSecondary);
        _timer.FontWeight = FontWeight.SemiBold;
        _badge = Text(Tokens.Fonts.Grotesque, BadgeSize, Tokens.Brushes.Accent);
        _badge.FontWeight = FontWeight.Bold;
        _preview = Text(Tokens.Fonts.Grotesque, PreviewSize, Tokens.Brushes.Ink);
        _preview.FontWeight = FontWeight.Medium;
        _previewHost = TailHost(_preview);
        _previewHost.IsVisible = false;

        var chip = new Border
        {
            Background = new SolidColorBrush(Tokens.Colors.AccentTint),
            CornerRadius = new CornerRadius(Tokens.Radius.Pill),
            Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Hair),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = _badge,
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = Tokens.Space.Base,
            Height = RowHeight,
        };
        Control[] cells = [_dot, _working, _wave, _timer, chip];
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            row.Children.Add(cells[i]);
        }

        Child = new StackPanel { Spacing = Tokens.Space.Tight, Children = { row, _previewHost } };
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        var working = state.Phase == PillPhase.Working;
        _dot.IsVisible = recording;
        _working.IsVisible = working;
        _wave.Push(recording ? state.Level : 0, working);
        _timer.Text = Timer(state);
        _badge.Text = state.Mode switch { "CLEAN" => "Clean", "CMD" => "Command", _ => "Raw" };

        var showPreview = HasPreview(state);
        if (showPreview) ShowPreview(_previewHost, _preview, state, PreviewChars);
        _previewHost.IsVisible = showPreview;
        Width = showPreview ? PreviewWidth : CompactWidth;
        CornerRadius = new CornerRadius(showPreview ? Tokens.Radius.Panel : Tokens.Radius.Pill);
    }
}

/// <summary>A liquid wave whose height follows the level, with a translucent fill beneath.</summary>
internal sealed class LiquidWave : Control
{
    private const double MaxAmplitude = 11;
    private const double WorkingAmplitude = 2;
    private const double Frequency = 0.16;
    private const double WorkingFrequency = 0.08;
    private const double SecondScale = 0.64;
    private const double SecondFrequency = 0.11;
    private const double SecondPhase = 2.1;
    private const double PhaseStep = 0.4;
    private const double Smoothing = 0.35;
    private const double Step = 4;
    private const double FillOpacity = 0.16;
    private const double SecondOpacity = 0.35;
    private const double MainStroke = 2.4;
    private const double SecondStroke = 1.6;

    // Captured once, not re-read from Tokens on every frame: a theme change must repaint only
    // between dictations, never recolour a wave mid-utterance.
    private readonly IBrush _fillBrush;
    private readonly Pen _mainPen;
    private readonly Pen _secondPen;

    private double _amplitude;
    private double _phase;
    private bool _working;

    /// <summary>Captures the active theme's accent and builds the brushes/pens once.</summary>
    public LiquidWave()
    {
        var accent = Tokens.Colors.Accent;
        _fillBrush = new SolidColorBrush(accent, FillOpacity);
        _mainPen = new Pen(new SolidColorBrush(accent), MainStroke, lineCap: PenLineCap.Round);
        _secondPen = new Pen(new SolidColorBrush(accent, SecondOpacity), SecondStroke, lineCap: PenLineCap.Round);
    }

    /// <summary>Feeds one frame.</summary>
    public void Push(double level, bool working)
    {
        _working = working;
        var target = working ? WorkingAmplitude : MaxAmplitude * level;
        _amplitude += (target - _amplitude) * Smoothing;
        _phase += PhaseStep;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var mid = height / 2;
        var frequency = _working ? WorkingFrequency : Frequency;

        context.DrawGeometry(_fillBrush, null, Wave(width, height, mid, _amplitude, frequency, _phase, closed: true));
        context.DrawGeometry(null, _mainPen, Wave(width, height, mid, _amplitude, frequency, _phase, closed: false));
        context.DrawGeometry(null, _secondPen,
            Wave(width, height, mid, _amplitude * SecondScale, SecondFrequency, _phase + SecondPhase, closed: false));
    }

    private static StreamGeometry Wave(
        double width, double height, double mid, double amplitude, double frequency, double phase, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, mid), closed);
            for (var x = Step; x <= width; x += Step)
            {
                var envelope = Math.Sin(Math.PI * x / width);
                ctx.LineTo(new Point(x, mid + (amplitude * envelope * Math.Sin((x * frequency) + phase))));
            }

            if (closed)
            {
                ctx.LineTo(new Point(width, height));
                ctx.LineTo(new Point(0, height));
            }

            ctx.EndFigure(closed);
        }

        return geometry;
    }
}
