using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// The dictation pill ("Lentille"): a small transparent-glass overlay at the bottom of the
/// screen with a record lamp and mode readout on the left, live level bars in the middle, a
/// timer on the right, and the text as it is transcribed on a line below. Hidden when idle.
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
/// <para>
/// The preview is what makes streaming visible: phrases land here while the user is still
/// talking, whether or not they are also being typed into the focused app. Only the tail of
/// the text is shown — a pill that grows with a long dictation would end up covering the
/// window being dictated into.
/// </para>
/// <para>
/// The glass is plain transparency, not acrylic: system blur backs the whole rectangular
/// window, which would paint square corners behind the rounded pill.
/// </para>
/// </remarks>
public sealed class HudWindow : Window
{
    /// <summary>Height with the readout row only, and with the preview line showing.</summary>
    private const double CompactHeight = 60;
    private const double PreviewHeight = 100;

    private const double PillWidth = 380;

    /// <summary>Characters of transcript kept on screen; older text scrolls off the left.</summary>
    private const int PreviewCharacters = 110;

    private readonly DictationEngine _engine;
    private readonly Border _shell;
    private readonly Ellipse _lamp;
    private readonly HudBars _bars;
    private readonly TextBlock _mode;
    private readonly TextBlock _timer;
    private readonly TextBlock _preview;

    /// <summary>Utterance clock, display-only. Runs while recording, freezes for the tail.</summary>
    private readonly Stopwatch _clock = new();

    /// <summary>Last (cleaning, recording) rendered, so brushes are rebuilt only on a flip.</summary>
    private (bool Cleaning, bool Recording)? _shown;
    private readonly DispatcherTimer _timerTick;

    /// <summary>Display frames since the pill appeared, used to pace the topmost re-assert.</summary>
    private int _frames;

    /// <summary>Builds the pill over <paramref name="engine"/> and starts watching it.</summary>
    public HudWindow(DictationEngine engine)
    {
        _engine = engine;

        Width = PillWidth;
        Height = CompactHeight;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;

        _lamp = new Ellipse
        {
            Width = 7,
            Height = 7,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _mode = new TextBlock
        {
            FontFamily = Tokens.Fonts.Mono,
            FontSize = 10,
            LetterSpacing = Tokens.Fonts.SilkscreenTracking,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _bars = new HudBars { Height = 30, Margin = new Thickness(12, 0) };

        _timer = new TextBlock
        {
            FontFamily = Tokens.Fonts.Mono,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _preview = new TextBlock
        {
            Margin = new Thickness(4, 2, 4, 0),
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = 13,
            Foreground = Tokens.Brushes.InkOnDeck,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            IsVisible = false,
        };

        _shell = new Border
        {
            // Transparent glass: the desktop shows through the pill.
            CornerRadius = new CornerRadius(30),
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x0C, 0x10, 0x16)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 0, 20, 0),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { BuildReadoutRow(), _preview },
            },
        };
        Content = _shell;

        _timerTick = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timerTick.Tick += (_, _) => Sync();
        _timerTick.Start();
    }

    /// <summary>Lamp and mode readout on the left, bars in the middle, timer on the right.</summary>
    private Grid BuildReadoutRow()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Height = CompactHeight,
        };

        var readout = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _lamp, _mode },
        };

        Grid.SetColumn(readout, 0);
        Grid.SetColumn(_bars, 1);
        Grid.SetColumn(_timer, 2);
        row.Children.Add(readout);
        row.Children.Add(_bars);
        row.Children.Add(_timer);

        return row;
    }

    /// <summary>
    /// Paints the readout for the current state. Red lamp while recording (the only red in
    /// the app), a dark lens for the tail; the mode label goes amber while the tail is
    /// worked on, echoing the shimmer.
    /// </summary>
    private void ShowMode(bool cleaning, bool recording)
    {
        if (_shown == (cleaning, recording)) return;
        _shown = (cleaning, recording);

        var mode = cleaning ? "CLEAN" : "RAW";
        _mode.Text = recording ? "REC · " + mode : mode;
        _mode.Foreground = recording
            ? new SolidColorBrush(Tokens.Colors.Ink, 0.82)
            : new SolidColorBrush(Tokens.Colors.MeterAmber);

        _lamp.Fill = recording ? Tokens.Brushes.Record : new SolidColorBrush(Tokens.Colors.RecordIdle);
        _timer.Foreground = recording
            ? new SolidColorBrush(Tokens.Colors.Accent)
            : new SolidColorBrush(Tokens.Colors.Ink, 0.82);

        // Accent-tinted edge while recording, a plain hairline for the tail.
        _shell.BorderBrush = recording
            ? new SolidColorBrush(Tokens.Colors.Accent, 0.35)
            : new SolidColorBrush(Avalonia.Media.Colors.White, 0.14);
    }

    private void Sync()
    {
        var state = _engine.State;

        if (state == DictationState.Idle)
        {
            if (IsVisible) Hide();

            // Forget the painted state: the accent can change while the pill is hidden, and
            // the next utterance must repaint rather than keep a stale brush.
            _shown = null;
            _clock.Reset();
            return;
        }

        var recording = state == DictationState.Recording;
        if (recording && !_clock.IsRunning) _clock.Restart();
        if (!recording) _clock.Stop();

        ShowMode(_engine.CleaningThisUtterance, recording);
        _timer.Text = $"{(int)_clock.Elapsed.TotalMinutes}:{_clock.Elapsed.Seconds:00}";
        _bars.Push(state, _engine.Level);
        ShowPreview(_engine.PartialText);

        if (!IsVisible)
        {
            PositionBottomCenter();
            Show();
            _frames = 0;
            Overlay.MakeOverlay(this);
        }
        else if (++_frames % 15 == 0)
        {
            // Half a second apart: a window going full-screen pushes itself to the front of
            // the topmost band, and nothing tells us it happened. See Overlay.KeepOnTop.
            Overlay.KeepOnTop(this);
        }
    }

    /// <summary>Puts the tail of <paramref name="text"/> on the preview line.</summary>
    private void ShowPreview(string text)
    {
        var wanted = text.Length > PreviewCharacters
            ? "…" + text[^PreviewCharacters..]
            : text;

        if (_preview.Text == wanted) return;

        _preview.Text = wanted;
        _preview.IsVisible = wanted.Length > 0;

        var height = _preview.IsVisible ? PreviewHeight : CompactHeight;
        if (Math.Abs(Height - height) < 0.5) return;

        Height = height;

        // The pill is anchored to the bottom of the screen, so a taller one has to move up
        // to keep its lower edge where it was — otherwise it grows off the screen.
        if (IsVisible) PositionBottomCenter();
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
            // Perceptual lift: RMS of speech sits low in [0,1]; gain then sqrt makes quiet
            // speech visibly move the bars instead of flickering the bottom pixel.
            _history[BarCount - 1] = Math.Sqrt(Math.Clamp(level * Tokens.Motion.LevelGain, 0, 1));
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
        var color = recording ? Tokens.Colors.Accent : Tokens.Colors.MeterAmber;

        var slot = Bounds.Width / BarCount;
        var barWidth = Math.Max(2.0, slot * 0.55);
        var maxBar = Bounds.Height;

        for (var i = 0; i < BarCount; i++)
        {
            var intensity = recording
                ? _history[i]
                : 0.25 + (0.20 * Math.Sin(_phase + (i * 0.45)));

            // Loud bars are solid, quiet ones translucent — the strip breathes with the
            // voice instead of only changing height.
            var brush = new SolidColorBrush(color, recording ? 0.35 + (0.65 * intensity) : 1.0);
            var barHeight = Math.Max(3.0, maxBar * intensity);
            var x = (i * slot) + ((slot - barWidth) / 2);
            var y = (Bounds.Height - barHeight) / 2;

            context.DrawRectangle(
                brush, null,
                new RoundedRect(new Rect(x, y, barWidth, barHeight), barWidth / 2));
        }
    }
}

/// <summary>
/// Keeps the pill above everything else on Windows. No-op on other platforms.
/// </summary>
/// <remarks>
/// <para>
/// Setting <c>Topmost</c> alone is not enough. Topmost is a z-order <i>band</i>, not a
/// promise: whenever a window goes full-screen the shell puts it at the front of that same
/// band, and every overlay already sitting there — ours included — ends up behind it. Nothing
/// notifies us, so the only cure is to claim the front of the band again, which is what every
/// game/meeting overlay on Windows does.
/// </para>
/// <para>
/// The one case this cannot win is a true exclusive-full-screen Direct3D app, which owns the
/// scan-out and is composited by nobody. Borderless full-screen — browsers on F11, video
/// players, Teams, most modern games — is a normal window and is covered here.
/// </para>
/// <para>
/// The extended styles are the other half: NOACTIVATE keeps the pill from ever stealing focus
/// (the load-bearing rule for text injection), TOOLWINDOW keeps it out of Alt-Tab, and
/// TRANSPARENT makes clicks fall through at the OS level rather than only inside Avalonia.
/// </para>
/// </remarks>
internal static class Overlay
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x0000_0020;
    private const int WsExToolWindow = 0x0000_0080;
    private const int WsExNoActivate = 0x0800_0000;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    private static readonly IntPtr HwndTopmost = new(-1);

    /// <summary>Stamps the overlay extended styles on. Call once each time the pill is shown.</summary>
    public static void MakeOverlay(Window window)
    {
        var hwnd = HandleOf(window);
        if (hwnd == IntPtr.Zero) return;

        var style = GetWindowLongPtrW(hwnd, GwlExStyle);
        SetWindowLongPtrW(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow | WsExTransparent);
        KeepOnTop(window);
    }

    /// <summary>Re-claims the front of the topmost band, without moving, resizing or focusing.</summary>
    public static void KeepOnTop(Window window)
    {
        var hwnd = HandleOf(window);
        if (hwnd == IntPtr.Zero) return;

        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private static IntPtr HandleOf(Window window) =>
        OperatingSystem.IsWindows()
            ? window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero
            : IntPtr.Zero;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value);
}
