using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>Fluent: a native Win11 flyout with an accent mic button whose halo follows the level.</summary>
internal sealed class FluentPill : PillFace
{
    private const double CompactWidth = 320;
    private const double PreviewWidth = 420;
    private const double DotSize = 10;
    private const double DotRing = 2;
    private const double TitleSize = 14;
    private const double MetaSize = 12;
    private const double LineSize = 13;
    private const int PreviewChars = 48;
    private const uint NearShadow = 0x24000000;
    private const uint FarShadow = 0x1F000000;
    private const double NearY = 8;
    private const double NearBlur = 16;
    private const double FarBlur = 2;

    private readonly MicHalo _mic = new();
    private readonly Ellipse _dot = Dot(DotSize);
    private readonly TextBlock _title;
    private readonly TextBlock _meta;
    private readonly TextBlock _line;
    private readonly TailClip _lineHost;
    private readonly IBrush _ink = Tokens.Brushes.Ink;
    private readonly IBrush _muted = Tokens.Brushes.InkSecondary;

    /// <summary>Builds the flyout in the active theme.</summary>
    public FluentPill()
    {
        Width = CompactWidth;
        Background = new SolidColorBrush(Tokens.Colors.PillFill);
        BorderBrush = new SolidColorBrush(Tokens.Colors.PillEdge);
        BorderThickness = new Thickness(Tokens.Border.Hairline);
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        BoxShadow = new BoxShadows(
            new BoxShadow { OffsetY = NearY, Blur = NearBlur, Color = Color.FromUInt32(NearShadow) },
            [new BoxShadow { Blur = FarBlur, Color = Color.FromUInt32(FarShadow) }]);
        Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Snug, Tokens.Space.Base, Tokens.Space.Snug);

        _dot.Stroke = new SolidColorBrush(Tokens.Colors.PillFill);
        _dot.StrokeThickness = DotRing;
        _dot.HorizontalAlignment = HorizontalAlignment.Right;
        _dot.VerticalAlignment = VerticalAlignment.Top;
        _dot.Margin = new Thickness(0, Tokens.Space.Tight, Tokens.Space.Tight, 0);

        _title = Text(Tokens.Fonts.Grotesque, TitleSize, _ink);
        _title.FontWeight = FontWeight.SemiBold;
        _meta = Text(Tokens.Fonts.Grotesque, MetaSize, _muted);
        _line = Text(Tokens.Fonts.Grotesque, LineSize, _muted);
        _lineHost = TailHost(_line);

        var head = new DockPanel();
        head.Children.Add(Panels.Docked(_meta, Dock.Right));
        head.Children.Add(_title);

        var body = new StackPanel { Spacing = Tokens.Space.Hair, VerticalAlignment = VerticalAlignment.Center, Children = { head, _lineHost } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = Tokens.Space.Base };
        row.Children.Add(new Grid { Children = { _mic, _dot } });
        Grid.SetColumn(body, 1);
        row.Children.Add(body);
        Child = row;
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        var working = state.Phase == PillPhase.Working;
        _dot.IsVisible = recording;
        _mic.Push(recording ? state.Level : 0, working);
        _title.Text = state.Phase switch
        {
            PillPhase.Recording => "Listening…",
            PillPhase.Working => state.Mode == "CLEAN" ? "Cleaning up…" : "Transcribing…",
            PillPhase.Notice => "Notice",
            _ => "Done",
        };
        _meta.Text = $"{Timer(state)} · {(state.Mode switch { "CLEAN" => "Cleaned", "CMD" => "Command", _ => "Raw" })}";

        var showPreview = HasPreview(state);
        if (showPreview)
        {
            ShowPreview(_lineHost, _line, state, PreviewChars);
            _line.Foreground = _ink;
        }
        else
        {
            _lineHost.Tail = false;
            _line.HorizontalAlignment = HorizontalAlignment.Stretch;
            _line.TextTrimming = TextTrimming.None;
            _line.Text = working
                ? state.Mode == "CLEAN" ? "Polishing the transcript" : "Transcribing…"
                : "Speak now — release to type";
            _line.Foreground = _muted;
        }

        Width = showPreview ? PreviewWidth : CompactWidth;
    }
}

/// <summary>The accent mic button and its level halo; a spinner while working.</summary>
internal sealed class MicHalo : Control
{
    private const double Box = 48;
    private const double ButtonSize = 34;
    private const double HaloMin = 34;
    private const double HaloRange = 14;
    private const double HaloOpacity = 0.22;
    private const double SpinnerSize = 44;
    private const double SpinnerStroke = 2.5;
    private const double SpinnerSweep = 180;
    private const double SpinStep = 12;
    private const double GlyphBox = 16;
    private const double GlyphStroke = 1.3;
    private const string GlyphData =
        "M8,1.5 A2.5,2.5 0 0 1 10.5,4 V7 A2.5,2.5 0 0 1 5.5,7 V4 A2.5,2.5 0 0 1 8,1.5 Z "
        + "M3.5,7.5 A4.5,4.5 0 0 0 12.5,7.5 M8,12 V14.5";

    private readonly Geometry _glyph = Geometry.Parse(GlyphData);

    // Captured once, not re-read per frame: a theme change must repaint only between
    // dictations, never recolour the halo mid-utterance.
    private readonly IBrush _haloBrush;
    private readonly IBrush _buttonBrush;
    private readonly Pen _spinnerPen;
    private readonly Pen _glyphPen;

    private double _level;
    private double _spin;
    private bool _working;

    /// <summary>Creates the 48 px mic area and captures the active theme's accent brushes.</summary>
    public MicHalo()
    {
        Width = Box;
        Height = Box;
        _haloBrush = new SolidColorBrush(Tokens.Colors.Accent, HaloOpacity);
        _buttonBrush = Tokens.Brushes.AccentFill;
        _spinnerPen = new Pen(Tokens.Brushes.Accent, SpinnerStroke, lineCap: PenLineCap.Round);
        _glyphPen = new Pen(Tokens.Brushes.OnAccent, GlyphStroke, lineCap: PenLineCap.Round);
    }

    /// <summary>Feeds one frame.</summary>
    public void Push(double level, bool working)
    {
        _level = level;
        _working = working;
        if (working) _spin = (_spin + SpinStep) % 360;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var halo = (HaloMin + (HaloRange * _level)) / 2;
        context.DrawEllipse(_haloBrush, null, centre, halo, halo);

        if (_working)
        {
            context.DrawGeometry(null, _spinnerPen, PillFace.Arc(centre, SpinnerSize / 2, _spin, SpinnerSweep));
        }

        context.DrawEllipse(_buttonBrush, null, centre, ButtonSize / 2, ButtonSize / 2);
        using (context.PushTransform(Matrix.CreateTranslation(centre.X - (GlyphBox / 2), centre.Y - (GlyphBox / 2))))
        {
            context.DrawGeometry(null, _glyphPen, _glyph);
        }
    }
}
