using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views.Pill;

/// <summary>
/// Mono: a square status line — REC block, timer, 16-cell block meter, mode tag — and a
/// shell-prompt preview with a block cursor. Hard offset shadow in light mode only.
/// </summary>
internal sealed class MonoPill : PillFace
{
    private const double CompactWidth = 360;
    private const double PreviewWidth = 420;
    private const double RowHeight = 34;
    private const double DotSize = 8;
    private const double TextSize = 12;
    private const double CursorWidth = 8;
    private const double CursorHeight = 14;
    private const double MeterHeight = 14;
    private const double ShadowOffset = 4;
    private const int PreviewChars = 44;

    private readonly Border _dot;
    private readonly TextBlock _state;
    private readonly TextBlock _timer;
    private readonly TextBlock _badge;
    private readonly TextBlock _preview;
    private readonly TailClip _previewHost;
    private readonly BlockMeter _meter = new() { Height = MeterHeight, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _previewRow;
    private readonly IBrush _ink = Tokens.Brushes.Ink;
    private readonly IBrush _muted = Tokens.Brushes.InkSecondary;
    private readonly IBrush _accent = Tokens.Brushes.Accent;

    /// <summary>Builds the status line in the active theme.</summary>
    public MonoPill()
    {
        Width = CompactWidth;
        Background = new SolidColorBrush(Tokens.Colors.PillFill);
        BorderBrush = new SolidColorBrush(Tokens.Colors.PillEdge);
        BorderThickness = new Thickness(Tokens.Border.Hairline);
        CornerRadius = new CornerRadius(Tokens.Radius.Pill);
        if (!Themes.IsDark)
        {
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = ShadowOffset, OffsetY = ShadowOffset, Color = Tokens.Colors.Ink });
        }

        _dot = new Border { Width = DotSize, Height = DotSize, Background = Tokens.Brushes.Record, VerticalAlignment = VerticalAlignment.Center };
        _state = Text(Tokens.Fonts.Mono, TextSize, _ink);
        _state.FontWeight = FontWeight.Bold;
        _timer = Text(Tokens.Fonts.Mono, TextSize, _muted);
        _badge = Text(Tokens.Fonts.Mono, TextSize, _ink);
        _preview = Text(Tokens.Fonts.Mono, TextSize, _ink);
        _previewHost = TailHost(_preview);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = Tokens.Space.Snug,
            Height = RowHeight,
            Margin = new Thickness(Tokens.Space.Base, 0),
        };
        Control[] cells =
        [
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Tight, Children = { _dot, _state } },
            _timer,
            _meter,
            _badge,
        ];
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            row.Children.Add(cells[i]);
        }

        var prompt = Text(Tokens.Fonts.Mono, TextSize, _accent);
        prompt.Text = ">";
        prompt.Margin = new Thickness(0, 0, Tokens.Space.Snug, 0);
        var cursor = new Border { Width = CursorWidth, Height = CursorHeight, Background = _ink, VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel { Margin = new Thickness(Tokens.Space.Base, Tokens.Space.Snug) };
        line.Children.Add(Panels.Docked(prompt, Dock.Left));
        line.Children.Add(Panels.Docked(cursor, Dock.Right));
        line.Children.Add(_previewHost);

        _previewRow = new StackPanel
        {
            IsVisible = false,
            Children = { new DashedRule { Stroke = new SolidColorBrush(Tokens.Colors.PillRule) }, line },
        };

        Child = new StackPanel { Children = { row, _previewRow } };
    }

    /// <inheritdoc />
    public override Control RecordDot => _dot;

    /// <inheritdoc />
    public override void Update(PillState state)
    {
        var recording = state.Phase == PillPhase.Recording;
        var working = state.Phase == PillPhase.Working;
        _dot.IsVisible = recording;
        _state.Text = state.Phase switch
        {
            PillPhase.Recording => "REC",
            PillPhase.Working => "CLEAN",
            PillPhase.Notice => "NOTE",
            _ => "DONE",
        };
        _state.Foreground = working ? _accent : _ink;
        _timer.Text = state.Phase == PillPhase.Latency
            ? Seconds(state.Elapsed)
            : string.Create(CultureInfo.InvariantCulture, $"{(int)state.Elapsed.TotalMinutes:00}:{state.Elapsed.Seconds:00}");
        _meter.Show(recording ? state.Level : 0, working);
        _badge.Text = $"[{state.Mode}]";
        _badge.Foreground = working ? _muted : _ink;

        var showPreview = HasPreview(state);
        if (showPreview) ShowPreview(_previewHost, _preview, state, PreviewChars);
        _previewRow.IsVisible = showPreview;
        Width = showPreview ? PreviewWidth : CompactWidth;
    }
}

/// <summary>Sixteen square cells: lit count = level. While working, a fixed run in the accent.</summary>
internal sealed class BlockMeter : Control
{
    private const int Cells = 16;
    private const int WorkingCells = 9;
    private const double Gap = 2;

    // Captured once, not re-read from Tokens on every frame: a theme change must repaint only
    // between dictations, never recolour a live meter mid-utterance.
    private readonly IBrush _accentOn = Tokens.Brushes.Accent;
    private readonly IBrush _idleOn = Themes.IsDark ? Tokens.Brushes.AccentFill : Tokens.Brushes.Ink;
    private readonly IBrush _off = new SolidColorBrush(Tokens.Colors.PillRule);

    private int _lit;
    private bool _working;

    /// <summary>Feeds one frame; repaints only when the lit count changes.</summary>
    public void Show(double level, bool working)
    {
        var lit = working ? WorkingCells : (int)Math.Round(level * Cells, MidpointRounding.AwayFromZero);
        if (lit == _lit && working == _working) return;
        _lit = lit;
        _working = working;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var cell = (Bounds.Width - (Gap * (Cells - 1))) / Cells;
        if (cell <= 0) return;

        // Neon cells on black in dark mode; ink cells on paper in light mode (the mockup).
        var on = _working ? _accentOn : _idleOn;
        for (var i = 0; i < Cells; i++)
        {
            context.DrawRectangle(i < _lit ? on : _off, null, new Rect(i * (cell + Gap), 0, cell, Bounds.Height));
        }
    }
}
