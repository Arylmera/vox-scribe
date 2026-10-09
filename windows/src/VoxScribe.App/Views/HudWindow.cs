using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Pill;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// The dictation pill: a fixed, transparent, click-through window at the bottom of the screen
/// hosting the active theme's <see cref="PillFace"/>. Hidden when idle.
/// </summary>
/// <remarks>
/// <para>
/// <b>This window must never take focus.</b> The text lands wherever the caret is, so
/// activating the overlay would redirect the injection into nothing. Hence
/// <see cref="Window.ShowActivated"/> false, nothing focusable, hit-testing off, and
/// <see cref="Overlay.MakeOverlay"/>'s NOACTIVATE / TRANSPARENT styles.
/// </para>
/// <para>
/// Polled at ~30 fps, not pushed: the engine raises Changed at buffer rate on a worker thread.
/// The window never resizes; the face animates inside it. Plain transparency, no acrylic —
/// system blur backs the whole rectangle.
/// </para>
/// </remarks>
public sealed class HudWindow : Window
{
    private readonly DictationEngine _engine;
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _noticeClock = new();
    private readonly Stopwatch _latencyClock = new();
    private readonly DispatcherTimer _timerTick;

    private PillFace _face;
    private bool _faceStale;
    private TimeSpan? _lastLatency;
    private int _frames;

    /// <summary>Builds the pill over <paramref name="engine"/> and starts watching it.</summary>
    public HudWindow(DictationEngine engine)
    {
        _engine = engine;

        // Raised on a worker thread; a torn TimeSpan? read would only garble one frame.
        engine.Completed += OnEngineCompleted;

        Width = Tokens.Size.PillWindowWidth;
        Height = Tokens.Size.PillWindowHeight;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;

        _face = PillFace.Create(Themes.Active.Pill);
        Content = _face;

        // Swapped only while hidden: a theme change never repaints a dictation in progress.
        Themes.Changed += OnThemeChanged;

        _timerTick = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tokens.Motion.PillFrame };
        _timerTick.Tick += (_, _) => Sync();
        _timerTick.Start();

        // The pill is a singleton for the app's lifetime, but a closed window must still let go
        // of everything it subscribed to, rather than outlive its usefulness pinned by events.
        Closed += (_, _) =>
        {
            _timerTick.Stop();
            Themes.Changed -= OnThemeChanged;
            engine.Completed -= OnEngineCompleted;
        };
    }

    private void OnEngineCompleted(object? sender, DictationResult result) => _lastLatency = result.ProcessingTime;

    private void OnThemeChanged(object? sender, EventArgs e) => _faceStale = true;

    private string Mode() =>
        _engine.CommandThisUtterance ? "CMD" : _engine.CleaningThisUtterance ? "CLEAN" : "RAW";

    private void Sync()
    {
        var state = _engine.State;

        if (state == DictationState.Idle)
        {
            // A failure notice holds the pill up briefly: the moment the user looks here is the
            // moment their text failed to appear.
            if (_engine.Notice is { Length: > 0 } notice)
            {
                if (!_noticeClock.IsRunning) _noticeClock.Restart();
                if (IsVisible && _noticeClock.Elapsed < Tokens.Motion.NoticeLinger)
                {
                    _face.Update(new PillState(PillPhase.Notice, 0, _clock.Elapsed, Mode(), notice));
                    return;
                }
            }
            else
            {
                _noticeClock.Reset();
            }

            // After a clean finish, hold the pill long enough to read the felt latency.
            if (_lastLatency is { } latency)
            {
                if (!_latencyClock.IsRunning) _latencyClock.Restart();
                if (IsVisible && _latencyClock.Elapsed < Tokens.Motion.LatencyLinger)
                {
                    _face.Update(new PillState(PillPhase.Latency, 0, latency, Mode(), string.Empty));
                    return;
                }

                _lastLatency = null;
            }

            _latencyClock.Reset();

            if (IsVisible) Hide();
            if (_faceStale)
            {
                _faceStale = false;
                _face = PillFace.Create(Themes.Active.Pill);
                Content = _face;
            }

            _clock.Reset();
            return;
        }

        var recording = state == DictationState.Recording;
        if (recording && !_clock.IsRunning) _clock.Restart();
        if (!recording) _clock.Stop();

        // The linger counts from the moment the engine idles; a new press forgets the old reading.
        _latencyClock.Reset();
        if (recording) _lastLatency = null;

        // Perceptual lift: speech RMS sits low in [0,1]; gain then sqrt makes it visibly move.
        var level = recording ? Math.Sqrt(Math.Clamp(_engine.Level * Tokens.Motion.LevelGain, 0, 1)) : 0;
        _face.Update(new PillState(
            recording ? PillPhase.Recording : PillPhase.Working, level, _clock.Elapsed, Mode(), _engine.PartialText));

        if (!IsVisible)
        {
            PositionBottomCenter();
            Show();
            _frames = 0;
            Overlay.MakeOverlay(this);
        }
        else if (++_frames % 15 == 0)
        {
            // A window going full-screen pushes itself to the front of the topmost band.
            Overlay.KeepOnTop(this);
        }
    }

    private void PositionBottomCenter()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null) return;

        var area = screen.WorkingArea;
        var width = (int)(Width * screen.Scaling);
        var height = (int)(Height * screen.Scaling);
        var margin = (int)(Tokens.Material.PillScreenMargin * screen.Scaling);

        Position = new PixelPoint(area.X + ((area.Width - width) / 2), area.Y + area.Height - height - margin);
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
