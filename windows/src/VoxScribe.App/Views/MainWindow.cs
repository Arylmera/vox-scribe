using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// The main window — a native-feeling shell with a navigation rail.
/// </summary>
/// <remarks>
/// <para>
/// The Rail layout: client area extended into the title bar (the system draws only the
/// caption buttons), an icon rail down the left edge for sections, and edge-to-edge content.
/// No root margin, no bordered well — separation comes from tone, not lines.
/// </para>
/// <para>
/// Built in code rather than XAML, deliberately. Every value comes from <see cref="Tokens"/>,
/// and XAML makes it far too easy to type a literal <c>Margin="12,8"</c> that silently escapes
/// the design system. In C# a stray number is visible in review.
/// </para>
/// </remarks>
public sealed class MainWindow : Window
{
    // Stroke icons for the rail, 24-unit grid, matching the design canvas.
    private const string WaveIcon = "M4,10 V14 M8,7 V17 M12,4 V20 M16,8 V16 M20,10 V14";
    private const string BookIcon = "M5,4 H16 A3,3 0 0 1 19,7 V20 H8 A3,3 0 0 1 5,17 Z M9,9 H15";
    private const string GearIcon =
        "M19,12 a7,7 0 0 0 -0.1,-1.2 l2,-1.6 -2,-3.4 -2.4,1 a7,7 0 0 0 -2,-1.2 L14,3 h-4 "
        + "l-0.5,2.6 a7,7 0 0 0 -2,1.2 l-2.4,-1 -2,3.4 2,1.6 A7,7 0 0 0 5,12 a7,7 0 0 0 "
        + "0.1,1.2 l-2,1.6 2,3.4 2.4,-1 a7,7 0 0 0 2,1.2 L10,21 h4 l0.5,-2.6 a7,7 0 0 0 "
        + "2,-1.2 l2.4,1 2,-3.4 -2,-1.6 A7,7 0 0 0 19,12 Z M15,12 a3,3 0 1 1 -6,0 "
        + "a3,3 0 0 1 6,0";
    private const string MicIcon = "M12,4 V13 M8,8 V11 M16,8 V11 M12,17 V20 M7,13 a5,5 0 0 0 10,0";

    private readonly Composition? _composition;
    private readonly RecordButton _recordKey;
    private readonly Lamp _recordLamp;
    private readonly VuMeter _meter;
    private readonly TextBlock _counter;
    private readonly ContentControl _sectionHost;
    private readonly RailKey _transcriptionsKey;
    private readonly RailKey _dictionaryKey;
    private readonly DispatcherTimer _counterTimer;

    private Control? _transcriptionsView;
    private Control? _dictionaryView;
    private DateTimeOffset? _startedAt;

    /// <summary>Set just before an explicit quit so the hide-to-tray guard steps aside.</summary>
    public bool ExitAllowed { get; set; }

    /// <summary>Builds a window with no engine behind it. Used by headless tests.</summary>
    public MainWindow() : this(null) { }

    /// <summary>Builds the window over <paramref name="composition"/>.</summary>
    public MainWindow(Composition? composition)
    {
        _composition = composition;

        Title = "Vox-Scribe";
        MinWidth = Tokens.Size.MainMinWidth;
        MinHeight = Tokens.Size.MainMinHeight;
        Width = Tokens.Size.MainWidth;
        Height = Tokens.Size.MainHeight;
        Background = Tokens.Brushes.Chassis;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
            new Uri("avares://VoxScribe.App/Assets/app.ico")));

        // The client area runs into the chrome: the system keeps its caption buttons,
        // the app paints everything else. This is what removes the light system title
        // bar that framed the dark UI.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = Tokens.Material.TitleBarHeight;

        // The close button hides to the tray instead of closing: a closed Avalonia window
        // is destroyed and the tray's "Show" could never bring it back. Real exit goes
        // through the tray menu, which sets ExitAllowed before shutting down.
        Closing += (_, e) =>
        {
            if (ExitAllowed) return;
            e.Cancel = true;
            Hide();
        };

        _recordLamp = new Lamp
        {
            LampColor = Tokens.Colors.Record,
            Width = Tokens.Material.RecordLensSize,
            Height = Tokens.Material.RecordLensSize,
        };
        _recordKey = new RecordButton { Content = _recordLamp };
        _recordKey.Click += (_, _) => ToggleRecording();

        _meter = new VuMeter
        {
            Height = Tokens.Material.RecordKeySize,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _counter = new TextBlock
        {
            Text = "00:00",
            FontFamily = Tokens.Fonts.Mono,
            FontSize = Tokens.Fonts.CounterLarge,
            Foreground = Tokens.Brushes.InkOnDeck,
        };

        _transcriptionsKey = new RailKey(WaveIcon) { IsEngaged = true };
        _dictionaryKey = new RailKey(BookIcon);
        _transcriptionsKey.Click += (_, _) => ShowSection(transcriptions: true);
        _dictionaryKey.Click += (_, _) => ShowSection(transcriptions: false);

        _sectionHost = new ContentControl();

        // The meter and counter are polled rather than pushed. The engine raises Changed on
        // a background thread at buffer rate, and marshalling every one of those to the UI
        // thread would be far more traffic than a display refresh needs.
        _counterTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Tokens.Motion.PanelPoll,
        };
        _counterTimer.Tick += (_, _) => SyncFromEngine();
        _counterTimer.Start();

        Content = BuildLayout();
        ShowSection(transcriptions: true);

        // Fade-in animation on window load
        Opacity = Tokens.Motion.FadeInFrom;
        var transitions = new Transitions();
        transitions.Add(new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = Tokens.Motion.FadeIn,
        });
        Transitions = transitions;
        Loaded += (_, _) => Opacity = 1;

        if (_composition?.Engine is not null) _composition.Engine.Start();
    }

    private DockPanel BuildLayout()
    {
        // Edge to edge: the rail owns the left, the content column owns the rest.
        var root = new DockPanel();
        root.Children.Add(Panels.Docked(BuildRail(), Dock.Left));

        var content = new DockPanel();
        content.Children.Add(Panels.Docked(BuildTitleStrip(), Dock.Top));
        content.Children.Add(Panels.Docked(BuildVoiceBand(), Dock.Top));

        if (_composition is not null && !Composition.IsModelInstalled)
        {
            content.Children.Add(Panels.Docked(BuildModelBanner(), Dock.Top));
        }

        _sectionHost.Margin = new Thickness(
            Tokens.Space.Roomy, Tokens.Space.Snug, Tokens.Space.Roomy, Tokens.Space.Roomy);
        content.Children.Add(_sectionHost);

        root.Children.Add(content);
        return root;
    }

    /// <summary>The navigation rail: app badge, section keys, settings at the foot.</summary>
    private Border BuildRail()
    {
        // Not a button: it identifies the app, it does not do anything. Hit-testing off so it
        // never eats a click the user meant for the key below it.
        var badge = new Border
        {
            Width = Tokens.Material.BadgeSize,
            Height = Tokens.Material.BadgeSize,
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            Background = new SolidColorBrush(Tokens.Colors.Accent),
            IsHitTestVisible = false,
            Margin = new Thickness(0, 0, 0, Tokens.Space.Roomy),
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(MicIcon),
                Stroke = Tokens.Brushes.Chassis,
                StrokeThickness = Tokens.Material.BadgeIconStroke,
                StrokeLineCap = PenLineCap.Round,
                Width = Tokens.Material.BadgeIconSize,
                Height = Tokens.Material.BadgeIconSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var settings = new RailKey(GearIcon);
        settings.Click += (_, _) => ShowSettings();

        var rail = new DockPanel { LastChildFill = false };
        rail.Children.Add(Panels.Docked(new StackPanel
        {
            Spacing = Tokens.Space.Tight,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { badge, _transcriptionsKey, _dictionaryKey },
        }, Dock.Top));
        rail.Children.Add(Panels.Docked(settings, Dock.Bottom));
        settings.HorizontalAlignment = HorizontalAlignment.Center;

        return new Border
        {
            Width = Tokens.Material.RailWidth,
            Background = Tokens.Brushes.Panel,
            Padding = new Thickness(0, Tokens.Space.Base),
            Child = rail,
        };
    }

    /// <summary>
    /// The custom title strip. Sits inside the extended chrome region, so empty space here
    /// is the window's drag handle; the system overlays its caption buttons on the right.
    /// </summary>
    private static Border BuildTitleStrip() => new()
    {
        Height = Tokens.Material.TitleBarHeight,
        Padding = new Thickness(
            Tokens.Space.Roomy, 0, Tokens.Material.CaptionButtonsReserve, 0),
        Child = new TextBlock
        {
            Text = "Vox-Scribe",
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            FontWeight = FontWeight.SemiBold,
            Foreground = Tokens.Brushes.Ink,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        },
    };

    /// <summary>The voice band: record button, full-width level bars, tape counter.</summary>
    private Grid BuildVoiceBand()
    {
        var band = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(
                Tokens.Space.Roomy, Tokens.Space.Snug, Tokens.Space.Roomy, Tokens.Space.Base),
        };

        Grid.SetColumn(_recordKey, 0);
        band.Children.Add(_recordKey);

        _meter.Margin = new Thickness(Tokens.Space.Roomy, 0);
        Grid.SetColumn(_meter, 1);
        band.Children.Add(_meter);

        _counter.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_counter, 2);
        band.Children.Add(_counter);

        return band;
    }

    /// <summary>
    /// A standing notice that the app cannot transcribe yet.
    /// </summary>
    /// <remarks>
    /// Unlike macOS, Windows has no built-in engine to fall back on, so a missing model means
    /// the app does nothing at all. That has to be visible on the front panel rather than
    /// buried in Settings.
    /// </remarks>
    private static BrushedPanel BuildModelBanner() => new()
    {
        Margin = new Thickness(Tokens.Space.Roomy, 0, Tokens.Space.Roomy, Tokens.Space.Base),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Base,
            Margin = new Thickness(Tokens.Space.Base),
            Children =
            {
                new Lamp
                {
                    IsLit = true,
                    LampColor = Tokens.Colors.MeterAmber,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = "Speech model not installed — Vox-Scribe cannot transcribe yet. "
                         + "See Settings, or docs/PARAKEET-WINDOWS.md.",
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Label,
                    Foreground = Tokens.Brushes.Ink,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        },
    };

    private void ShowSection(bool transcriptions)
    {
        _transcriptionsKey.IsEngaged = transcriptions;
        _dictionaryKey.IsEngaged = !transcriptions;

        if (_composition is null)
        {
            _sectionHost.Content = Panels.EmptyState(
                transcriptions ? "NO RECORDINGS" : "DICTIONARY EMPTY",
                transcriptions ? "Press Record to start." : "Add words it keeps getting wrong.");
            return;
        }

        // Built once and reused: rebuilding would drop the user's search text every time
        // they switched tabs.
        if (transcriptions)
        {
            _transcriptionsView ??= new TranscriptionsView(_composition.Transcripts);
            _sectionHost.Content = _transcriptionsView;
        }
        else
        {
            _dictionaryView ??= new DictionaryView(_composition.Dictionary);
            _sectionHost.Content = _dictionaryView;
        }
    }

    private void ShowSettings()
    {
        if (_composition is null) return;
        _ = new SettingsWindow(_composition.Settings).ShowDialog(this);
    }

    /// <summary>Pulls state from the engine onto the panel.</summary>
    private void SyncFromEngine()
    {
        var engine = _composition?.Engine;
        if (engine is null)
        {
            if (_startedAt is not null) UpdateCounter();
            return;
        }

        var recording = engine.State != DictationState.Idle;

        _meter.Level = engine.Level;
        _meter.IsActive = recording;
        _recordLamp.IsLit = engine.State == DictationState.Recording;

        if (recording && _startedAt is null) _startedAt = DateTimeOffset.Now;
        else if (!recording) _startedAt = null;

        UpdateCounter();
    }

    private void UpdateCounter()
    {
        var elapsed = _startedAt is null ? TimeSpan.Zero : DateTimeOffset.Now - _startedAt.Value;
        _counter.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}");
    }

    /// <summary>Toggles the transport. Exposed for headless tests.</summary>
    public void ToggleRecording()
    {
        // With no engine — a headless test, or a machine with no platform layer — the panel
        // still toggles so the visual state can be exercised.
        if (_composition?.Engine is null)
        {
            IsRecording = !IsRecording;
            _recordLamp.IsLit = IsRecording;
            _meter.IsActive = IsRecording;
            _startedAt = IsRecording ? DateTimeOffset.Now : null;
            return;
        }

        // The button is a convenience; the hotkey is the real trigger. Both funnel through
        // the same engine so there is only ever one state machine.
        _composition.Engine.TogglePushToTalk();
        SyncFromEngine();
    }

    /// <summary>Whether the transport is engaged. Exposed for headless tests.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>The record lamp. Exposed for headless tests.</summary>
    public Lamp RecordLamp => _recordLamp;

    /// <summary>The level meter. Exposed for headless tests.</summary>
    public VuMeter Meter => _meter;

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _counterTimer.Stop();
        base.OnClosed(e);
    }
}
