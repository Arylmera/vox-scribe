using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Chibi;
using VoxScribe.App.Views.Pill;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// The dictation indicator. In pill mode: a fixed, transparent, click-through window at the
/// bottom of the screen hosting the active theme's <see cref="PillFace"/>, hidden when idle.
/// In mascot mode: a <see cref="ChibiFace"/> that stays on the desktop, can be dragged, and
/// sizes to its content, anchored at its saved bottom-left corner.
/// </summary>
/// <remarks>
/// <para>
/// <b>This window must never take focus.</b> The text lands wherever the caret is, so
/// activating the overlay would redirect the injection into nothing. Hence
/// <see cref="Window.ShowActivated"/> false, nothing focusable, and
/// <see cref="Overlay.MakeOverlay"/>'s NOACTIVATE style in both modes. The pill is also
/// hit-test invisible and TRANSPARENT; the mascot is neither, so it can be grabbed.
/// </para>
/// <para>
/// Polled at ~30 fps, not pushed: the engine raises Changed at buffer rate on a worker thread.
/// Plain transparency, no acrylic — system blur backs the whole rectangle.
/// </para>
/// </remarks>
public sealed class HudWindow : Window
{
    private readonly DictationEngine _engine;
    private readonly AppSettings _settings;
    private readonly Action _showMain;
    private readonly Action _showSettings;
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _noticeClock = new();
    private readonly Stopwatch _latencyClock = new();
    private readonly DispatcherTimer _timerTick;

    private PillFace _face;
    private Mascot? _mascot;
    private (string? Id, bool Hidden, string? Accent) _mascotKey;
    private PixelPoint _anchor;
    private bool _bubbleToLeft;
    private bool _faceStale;
    private TimeSpan? _lastLatency;
    private TimeSpan? _lastCleanup;
    private bool _lastCleanupFailed;
    private int _frames;
    private bool _wasIdle = true;

    /// <summary>Builds the indicator over <paramref name="engine"/> and starts watching it.</summary>
    /// <param name="engine">The dictation engine to mirror.</param>
    /// <param name="settings">Which indicator, which mascot, and where it sits.</param>
    /// <param name="showMain">Opens the main window (mascot double-click).</param>
    /// <param name="showSettings">Opens Settings → Appearance (mascot menu).</param>
    public HudWindow(DictationEngine engine, AppSettings settings, Action showMain, Action showSettings)
    {
        _engine = engine;
        _settings = settings;
        _showMain = showMain;
        _showSettings = showSettings;

        // Raised on a worker thread; a torn TimeSpan? read would only garble one frame.
        engine.Completed += OnEngineCompleted;

        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _mascotKey = KeyOf(settings.Data);
        _mascot = Mascots.Active(settings.Data);
        _face = CreateFace();
        Content = _face;
        ApplyMode();

        // Swapped only between dictations: a theme or mascot change never repaints one in progress.
        Themes.Changed += OnThemeChanged;
        settings.Changed += OnSettingsChanged;

        // The anchor (bottom-left corner) follows wherever the window actually is, however it
        // got there, so a drag can never be undone by the next resize.
        PositionChanged += (_, e) =>
        {
            if (_mascot is null || !IsVisible) return;
            var windowWidthPx = (int)Math.Ceiling(Bounds.Width * RenderScaling);
            var figureWidthPx = (int)Math.Ceiling(Tokens.Size.MascotWidth * RenderScaling);
            var anchorX = _bubbleToLeft ? e.Point.X + (windowWidthPx - figureWidthPx) : e.Point.X;
            _anchor = new PixelPoint(anchorX, e.Point.Y + (int)Math.Ceiling(Bounds.Height * RenderScaling));
        };

        // The bubble grows and shrinks the mascot window; keep the figure's bottom-left corner
        // put (bottom-right of the window instead, when the bubble opens to the left).
        SizeChanged += (_, e) =>
        {
            if (_mascot is null || !IsVisible) return;
            var windowWidthPx = (int)Math.Ceiling(e.NewSize.Width * RenderScaling);
            var figureWidthPx = (int)Math.Ceiling(Tokens.Size.MascotWidth * RenderScaling);
            Position = new PixelPoint(
                MascotPlacement.WindowX(_anchor.X, windowWidthPx, figureWidthPx, _bubbleToLeft),
                _anchor.Y - (int)Math.Ceiling(e.NewSize.Height * RenderScaling));
        };

        _timerTick = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tokens.Motion.PillFrame };
        _timerTick.Tick += (_, _) => Sync();
        _timerTick.Start();

        // A singleton for the app's lifetime, but a closed window must still let go of
        // everything it subscribed to, rather than outlive its usefulness pinned by events.
        Closed += (_, _) =>
        {
            _timerTick.Stop();
            Themes.Changed -= OnThemeChanged;
            settings.Changed -= OnSettingsChanged;
            engine.Completed -= OnEngineCompleted;
        };
    }

    private static (string?, bool, string?) KeyOf(SettingsData d) => (d.Mascot, d.MascotHidden, d.MascotAccent);

    private void OnEngineCompleted(object? sender, DictationResult result)
    {
        _lastLatency = result.ProcessingTime;
        _lastCleanup = result.CleanupTime;
        _lastCleanupFailed = result.CleanupFailed == true;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _faceStale = true;

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // A drag saves the position, which raises Changed too; only a change of mascot,
        // visibility or accent rebuilds the face.
        var key = KeyOf(_settings.Data);
        if (key == _mascotKey) return;
        _mascotKey = key;
        _faceStale = true;
    }

    private string Mode() =>
        _engine.CommandThisUtterance ? "CMD"
        : _engine.EditThisUtterance ? "EDIT"
        : _engine.CleaningThisUtterance ? "CLEAN" : "RAW";

    private PillFace CreateFace()
    {
        if (_mascot is null) return PillFace.Create(Themes.Active.Pill);

        var face = _mascot.Create(Mascots.Accent(_settings.Data));
        face.BubbleToLeft = _bubbleToLeft;
        face.PointerPressed += OnMascotPressed;
        face.ContextMenu = MascotMenu();
        return face;
    }

    private void ApplyMode()
    {
        if (_mascot is null)
        {
            SizeToContent = SizeToContent.Manual;
            Width = Tokens.Size.PillWindowWidth;
            Height = Tokens.Size.PillWindowHeight;
            IsHitTestVisible = false;
        }
        else
        {
            SizeToContent = SizeToContent.WidthAndHeight;
            IsHitTestVisible = true;
        }
    }

    private void Rebuild()
    {
        _faceStale = false;
        var wasMascot = _mascot is not null;
        _mascot = Mascots.Active(_settings.Data);

        // Switching pill ⇄ mascot: hide now, and let the next show apply the new mode's
        // position and overlay styles from scratch.
        if (IsVisible && wasMascot != (_mascot is not null)) Hide();

        _face = CreateFace();
        Content = _face;
        ApplyMode();
    }

    private void Sync()
    {
        var state = _engine.State;

        if (state == DictationState.Idle)
        {
            // A failure notice holds the indicator up briefly: the moment the user looks here
            // is the moment their text failed to appear.
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

            // After a clean finish, hold long enough to read the felt latency.
            if (_lastLatency is { } latency)
            {
                if (!_latencyClock.IsRunning) _latencyClock.Restart();
                if (IsVisible && _latencyClock.Elapsed < Tokens.Motion.LatencyLinger)
                {
                    _face.Update(new PillState(PillPhase.Latency, 0, latency, Mode(), string.Empty,
                        Cleanup: _lastCleanup, CleanupFailed: _lastCleanupFailed));
                    return;
                }

                _lastLatency = null;
            }

            _latencyClock.Reset();

            if (_faceStale) Rebuild();

            if (_mascot is null)
            {
                if (IsVisible) Hide();
            }
            else
            {
                _face.Update(new PillState(PillPhase.Idle, 0, TimeSpan.Zero, Mode(), string.Empty));
                ShowIfHidden(idle: true);
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

        ShowIfHidden(idle: false);
    }

    /// <summary>
    /// Shows the indicator if it is hidden. While visible and not idle, re-claims the front of
    /// the topmost band: once right away on the first non-idle frame (so a dictation starting
    /// brings it forward over whatever eclipsed it while idle), then every 15 frames as a window
    /// going full-screen can otherwise push itself in front again at any time. While idle, the
    /// mascot keeps plain <c>Topmost</c> and never re-claims, so it stops pushing a
    /// click-blocking window in front of full-screen video or games all day.
    /// </summary>
    private void ShowIfHidden(bool idle)
    {
        if (!IsVisible)
        {
            if (_mascot is null) PositionBottomCenter();
            else PlaceMascot();

            Show();
            _frames = 0;
            _wasIdle = idle;
            Overlay.MakeOverlay(this, clickThrough: _mascot is null);
            return;
        }

        if (idle)
        {
            _wasIdle = true;
            return;
        }

        _frames++;
        if (_wasIdle || _frames % 15 == 0) Overlay.KeepOnTop(this);
        _wasIdle = false;
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

    private void PlaceMascot()
    {
        var saved = _settings.Data is { MascotX: { } x, MascotY: { } y } ? new PixelPoint(x, y) : (PixelPoint?)null;

        // The screen the saved point is actually on, not always Primary, or a mascot saved on a
        // second, differently-scaled monitor starts out positioned (and sized) for the wrong DPI.
        var screen = saved is { } s ? Screens.ScreenFromPoint(new PixelPoint(s.X, s.Y - 1)) : null;
        var scaling = screen?.Scaling ?? Screens.Primary?.Scaling ?? 1;

        _face.Measure(Size.Infinity);
        var size = new PixelSize(
            (int)Math.Ceiling(_face.DesiredSize.Width * scaling), (int)Math.Ceiling(_face.DesiredSize.Height * scaling));
        var areas = Screens.All.OrderByDescending(s => s.IsPrimary).Select(s => s.WorkingArea).ToList();

        _anchor = MascotPlacement.Anchor(saved, areas, size, (int)(Tokens.Material.PillScreenMargin * scaling));

        _bubbleToLeft = MascotPlacement.BubbleToLeft(_anchor, areas);
        if (_face is ChibiFace chibi) chibi.BubbleToLeft = _bubbleToLeft;

        var figureWidthPx = (int)Math.Ceiling(Tokens.Size.MascotWidth * scaling);
        Position = new PixelPoint(
            MascotPlacement.WindowX(_anchor.X, size.Width, figureWidthPx, _bubbleToLeft), _anchor.Y - size.Height);
    }

    private void OnMascotPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            _showMain();
            return;
        }

        // On Windows the move loop is modal: this returns once the button is released, and
        // PositionChanged has kept _anchor current throughout (using today's side — the bubble
        // may be up mid-drag, and the window doesn't resize just because the mascot moved).
        BeginMoveDrag(e);

        var areas = Screens.All.OrderByDescending(s => s.IsPrimary).Select(s => s.WorkingArea).ToList();
        var bubbleToLeft = MascotPlacement.BubbleToLeft(_anchor, areas);
        if (bubbleToLeft != _bubbleToLeft)
        {
            // The side flipped crossing a screen's midline. Re-pin the figure to the window's
            // new edge now, rather than waiting for a SizeChanged that a same-size drag never
            // raises — otherwise the figure jumps and _anchor no longer matches it.
            _bubbleToLeft = bubbleToLeft;
            if (_face is ChibiFace chibi) chibi.BubbleToLeft = _bubbleToLeft;
            var windowWidthPx = (int)Math.Ceiling(Bounds.Width * RenderScaling);
            var figureWidthPx = (int)Math.Ceiling(Tokens.Size.MascotWidth * RenderScaling);
            Position = new PixelPoint(
                MascotPlacement.WindowX(_anchor.X, windowWidthPx, figureWidthPx, _bubbleToLeft), Position.Y);
        }

        if (_settings.Data is { MascotX: { } x, MascotY: { } y } && new PixelPoint(x, y) == _anchor) return;
        _settings.Update(_settings.Data with { MascotX = _anchor.X, MascotY = _anchor.Y });
    }

    private ContextMenu MascotMenu()
    {
        var characters = new MenuItem { Header = "Character" };
        foreach (var mascot in Mascots.All)
        {
            var item = new MenuItem
            {
                Header = mascot.Label,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = mascot.Id == _mascot?.Id,
            };
            item.Click += (_, _) => _settings.Update(_settings.Data with { Mascot = mascot.Id });
            characters.Items.Add(item);
        }

        var hide = new MenuItem { Header = "Hide mascot" };
        hide.Click += (_, _) => _settings.Update(_settings.Data with { MascotHidden = true });

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => _showSettings();

        return new ContextMenu { Items = { characters, hide, new Separator(), settings } };
    }
}

/// <summary>
/// Keeps the dictation indicator (pill or mascot) above everything else on Windows. No-op on
/// other platforms.
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

    /// <summary>
    /// Stamps the overlay extended styles on. Call each time the window is shown. NOACTIVATE
    /// and TOOLWINDOW always; TRANSPARENT (clicks fall through) only when
    /// <paramref name="clickThrough"/> — the pill, not the mascot, which must be grabbable.
    /// Cleared as well as set, because the same window switches between the two.
    /// </summary>
    public static void MakeOverlay(Window window, bool clickThrough = true)
    {
        var hwnd = HandleOf(window);
        if (hwnd == IntPtr.Zero) return;

        var style = GetWindowLongPtrW(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow;
        style = clickThrough ? style | WsExTransparent : style & ~(nint)WsExTransparent;
        SetWindowLongPtrW(hwnd, GwlExStyle, style);
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
