using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>Paper: a paper slip tilted −0.6°, the level written as a pen stroke on a ruled line.</summary>
internal sealed class PaperPill : PillFace
{
    private const double CompactWidth = 320;
    private const double PreviewWidth = 400;
    private const double Tilt = -0.6;
    private const double DotSize = 9;
    private const double HeadSize = 17;
    private const double MetaSize = 11;
    private const double StrokeHeight = 34;
    private const double PreviewSize = 20;
    private const double PreviewLine = 30;
    private const double WorkingInkRatio = 0.62;
    private const int PreviewChars = 40;
    private const uint ShadowArgb = 0x8C1E1408;
    private const double ShadowY = 14;
    private const double ShadowBlur = 30;
    private const double ShadowSpread = -16;

    private readonly Ellipse _dot = Dot(DotSize);
    private readonly TextBlock _head;
    private readonly TextBlock _meta;
    private readonly TextBlock _preview;
    private readonly TailClip _previewHost;
    private readonly PenStroke _stroke = new() { Height = StrokeHeight };
    private readonly Border _working;
    private readonly IBrush _ink = Tokens.Brushes.Ink;
    private readonly IBrush _accent = Tokens.Brushes.Accent;

    /// <summary>Builds the slip in the active theme.</summary>
    public PaperPill()
    {
        Width = CompactWidth;
        Background = new SolidColorBrush(Tokens.Colors.PillFill);
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        BoxShadow = new BoxShadows(
            new BoxShadow { OffsetY = Tokens.Border.Hairline, Color = Tokens.Colors.PillEdge },
            [new BoxShadow { OffsetY = ShadowY, Blur = ShadowBlur, Spread = ShadowSpread, Color = Color.FromUInt32(ShadowArgb) }]);
        Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Base, Tokens.Space.Roomy, Tokens.Space.Base);
        RenderTransform = new RotateTransform(Tilt);

        _head = Text(Tokens.Fonts.Display, HeadSize, _ink);
        _head.FontStyle = FontStyle.Italic;
        _meta = Text(Tokens.Fonts.Mono, MetaSize, Tokens.Brushes.InkSecondary);
        _preview = Text(Tokens.Fonts.Display, PreviewSize, _ink);
        _preview.FontStyle = FontStyle.Italic;
        _preview.Height = PreviewLine;
        _previewHost = TailHost(_preview);
        _previewHost.IsVisible = false;

        _working = new Border
        {
            Height = Tokens.Border.Ring,
            Width = CompactWidth * WorkingInkRatio,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = _accent,
            IsVisible = false,
        };

        var head = new DockPanel();
        head.Children.Add(Panels.Docked(_meta, Dock.Right));
        head.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children = { _dot, _head },
        });

        Child = new StackPanel
        {
            Spacing = Tokens.Space.Tight,
            Children =
            {
                head,
                _stroke,
                _previewHost,
                new Border { Height = Tokens.Border.Hairline, Background = new SolidColorBrush(Tokens.Colors.PillRule) },
                _working,
            },
        };
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        _dot.IsVisible = recording;
        _head.Text = state.Phase switch
        {
            PillPhase.Recording => "listening",
            PillPhase.Working => state.Mode == "CLEAN" ? "tidying…" : "transcribing…",
            PillPhase.Notice => "note",
            _ => "done",
        };
        _head.Foreground = state.Phase == PillPhase.Working ? _accent : _ink;
        _meta.Text = $"{Timer(state)} · {(state.Mode switch { "CLEAN" => "clean", "CMD" => "cmd", _ => "raw" })}";
        _stroke.Push(recording ? state.Level : 0, flat: !recording);
        _working.IsVisible = state.Phase == PillPhase.Working;

        var showPreview = HasPreview(state);
        if (showPreview) ShowPreview(_previewHost, _preview, state, PreviewChars);
        _previewHost.IsVisible = showPreview;
        Width = showPreview ? PreviewWidth : CompactWidth;
    }
}

/// <summary>The level as a pen stroke: a scrolling history, newest on the right.</summary>
internal sealed class PenStroke : Control
{
    private const int Points = 50;
    private const double Amplitude = 20;
    private const double FlatAmplitude = 2;
    private const double Baseline = 0.7;
    private const double StrokeWidth = 1.6;
    private const double MinEnvelope = 0.15;
    private const double MinorTick = 0.35;
    private const double JitterFastPitch = 2.2;
    private const double JitterSlowPitch = 0.66;
    private const double JitterFastWeight = 0.6;
    private const double JitterSlowWeight = 0.4;

    private readonly double[] _history = new double[Points];
    private readonly Pen _pen = new(Tokens.Brushes.Ink, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private bool _flat;

    /// <summary>Feeds one frame.</summary>
    public void Push(double level, bool flat)
    {
        Array.Copy(_history, 1, _history, 0, Points - 1);
        _history[^1] = level;
        _flat = flat;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0) return;

        var baseline = Bounds.Height * Baseline;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, baseline), false);
            for (var i = 1; i < Points; i++)
            {
                var t = (double)i / (Points - 1);
                var envelope = Math.Max(MinEnvelope, Math.Sin(Math.PI * t));
                var jitter = Math.Abs((Math.Sin(i * JitterFastPitch) * JitterFastWeight) + (Math.Sin(i * JitterSlowPitch) * JitterSlowWeight));
                var amplitude = _flat ? FlatAmplitude : Amplitude * _history[i];
                var y = baseline - (amplitude * envelope * jitter * (i % 2 == 0 ? 1 : MinorTick));
                ctx.LineTo(new Point(width * t, y));
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, _pen, geometry);
    }
}
