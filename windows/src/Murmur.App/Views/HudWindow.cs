using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Murmur.App.Design;
using Murmur.Core;

namespace Murmur.App.Views;

/// <summary>
/// The dictation pill: a small overlay at the bottom of the screen showing live level bars
/// while recording and a shimmer while transcribing. Hidden when idle.
/// </summary>
/// <remarks>
/// <para>
/// <b>This window must never take focus.</b> The text lands wherever the caret is, so
/// activating the overlay would redirect the injection into nothing — the same load-bearing
/// rule as the macOS HUD panel. Hence <see cref="Window.ShowActivated"/> false, no focusable
/// content, and hit-testing off so clicks fall through to whatever is behind it.
/// </para>
/// <para>
/// Polled, not pushed: the engine raises Changed at buffer rate on a worker thread, and a
/// display only needs ~30 fps. Same pattern as the main window's meter.
/// </para>
/// </remarks>
public sealed class HudWindow : Window
{
    private readonly DictationEngine _engine;
    private readonly HudBars _bars;
    private readonly DispatcherTimer _timer;

    /// <summary>Builds the pill over <paramref name="engine"/> and starts watching it.</summary>
    public HudWindow(DictationEngine engine)
    {
        _engine = engine;

        Width = 320;
        Height = 64;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;

        _bars = new HudBars { Margin = new Thickness(18, 12) };

        Content = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x12, 0x11, 0x10)),
            BorderBrush = new SolidColorBrush(Tokens.Colors.SelectionEdge, 0.4),
            BorderThickness = new Thickness(1),
            Child = _bars,
        };

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += (_, _) => Sync();
        _timer.Start();
    }

    private void Sync()
    {
        var state = _engine.State;

        if (state == DictationState.Idle)
        {
            if (IsVisible) Hide();
            return;
        }

        _bars.Push(state, _engine.Level);

        if (!IsVisible)
        {
            PositionBottomCenter();
            Show();
        }
    }

    private void PositionBottomCenter()
    {
        // Primary, not "screen under the caret" — the caret's screen is unknowable from
        // here, and a fixed spot is what makes the pill glanceable.
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null) return;

        var area = screen.WorkingArea;
        var width = (int)(Width * screen.Scaling);
        var height = (int)(Height * screen.Scaling);
        var margin = (int)(24 * screen.Scaling);

        Position = new PixelPoint(
            area.X + ((area.Width - width) / 2),
            area.Y + area.Height - height - margin);
    }
}

/// <summary>The animated bar strip inside the pill.</summary>
internal sealed class HudBars : Control
{
    private const int BarCount = 32;

    /// <summary>Rolling level history, newest last — drawn as a scrolling waveform.</summary>
    private readonly double[] _history = new double[BarCount];

    private DictationState _state = DictationState.Idle;
    private double _phase;

    /// <summary>Feeds one display frame: the engine state and its current level.</summary>
    public void Push(DictationState state, float level)
    {
        _state = state;

        if (state == DictationState.Recording)
        {
            Array.Copy(_history, 1, _history, 0, BarCount - 1);
            // Perceptual lift: RMS of speech sits low in [0,1]; sqrt makes quiet speech
            // visibly move the bars instead of flickering the bottom pixel.
            _history[BarCount - 1] = Math.Sqrt(Math.Clamp(level, 0f, 1f));
        }
        else
        {
            // Transcribing: a travelling shimmer says "working" without pretending audio
            // is still being heard.
            _phase += 0.35;
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var recording = _state == DictationState.Recording;
        var brush = new SolidColorBrush(recording ? Tokens.Colors.MeterGreen : Tokens.Colors.MeterAmber);

        var slot = Bounds.Width / BarCount;
        var barWidth = Math.Max(2.0, slot * 0.55);
        var maxBar = Bounds.Height;

        for (var i = 0; i < BarCount; i++)
        {
            var intensity = recording
                ? _history[i]
                : 0.25 + (0.20 * Math.Sin(_phase + (i * 0.45)));

            var barHeight = Math.Max(3.0, maxBar * intensity);
            var x = (i * slot) + ((slot - barWidth) / 2);
            var y = (Bounds.Height - barHeight) / 2;

            context.DrawRectangle(
                brush, null,
                new RoundedRect(new Rect(x, y, barWidth, barHeight), barWidth / 2));
        }
    }
}
