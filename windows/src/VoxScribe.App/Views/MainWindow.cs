using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views;

/// <summary>
/// The main window: a navigation rail, a title strip in the extended chrome, and the open section.
/// </summary>
/// <remarks>
/// Built in code rather than XAML, deliberately: every value comes from <see cref="Tokens"/>.
/// </remarks>
public sealed class MainWindow : Window
{
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
    private readonly ContentControl _sectionHost = new();
    private readonly RailKey _transcriptionsKey;
    private readonly RailKey _dictionaryKey;

    private Control? _transcriptionsView;
    private Control? _dictionaryView;

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

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = Tokens.Material.TitleBarHeight;

        // The close button hides to the tray: a closed Avalonia window is destroyed and the
        // tray's "Show" could never bring it back. Real exit sets ExitAllowed first.
        Closing += (_, e) =>
        {
            if (ExitAllowed) return;
            e.Cancel = true;
            Hide();
        };

        _transcriptionsKey = new RailKey(WaveIcon) { IsEngaged = true };
        Avalonia.Automation.AutomationProperties.SetName(_transcriptionsKey, "Transcriptions");
        _dictionaryKey = new RailKey(BookIcon);
        Avalonia.Automation.AutomationProperties.SetName(_dictionaryKey, "Dictionary");
        _transcriptionsKey.Click += (_, _) => ShowSection(transcriptions: true);
        _dictionaryKey.Click += (_, _) => ShowSection(transcriptions: false);

        Content = BuildLayout();
        ShowSection(transcriptions: true);

        Opacity = Tokens.Motion.FadeInFrom;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = Tokens.Motion.FadeIn },
        };
        Loaded += (_, _) => Opacity = 1;

        _composition?.Engine?.Start();
    }

    private DockPanel BuildLayout()
    {
        var root = new DockPanel();
        root.Children.Add(Panels.Docked(BuildRail(), Dock.Left));

        var content = new DockPanel();
        content.Children.Add(Panels.Docked(BuildTitleStrip(), Dock.Top));
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

    private Border BuildRail()
    {
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

        var settings = new RailKey(GearIcon) { HorizontalAlignment = HorizontalAlignment.Center };
        settings.Click += (_, _) => ShowSettings();

        var rail = new DockPanel { LastChildFill = false };
        rail.Children.Add(Panels.Docked(new StackPanel
        {
            Spacing = Tokens.Space.Tight,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { badge, _transcriptionsKey, _dictionaryKey },
        }, Dock.Top));
        rail.Children.Add(Panels.Docked(settings, Dock.Bottom));

        return new Border
        {
            Width = Tokens.Material.RailWidth,
            Background = Tokens.Brushes.Panel,
            Padding = new Thickness(0, Tokens.Space.Base),
            Child = rail,
        };
    }

    private static Border BuildTitleStrip() => new()
    {
        Height = Tokens.Material.TitleBarHeight,
        Padding = new Thickness(Tokens.Space.Roomy, 0, Tokens.Material.CaptionButtonsReserve, 0),
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
                transcriptions ? "Hold the push-to-talk key and speak." : "Add words it keeps getting wrong.");
            return;
        }

        // Built once and reused: rebuilding would drop the user's search text.
        if (transcriptions)
        {
            _transcriptionsView ??= new TranscriptionsView(_composition.Transcripts);
            _sectionHost.Content = _transcriptionsView;
        }
        else
        {
            _dictionaryView ??= new DictionaryView(_composition.Dictionary, _composition.Transcripts);
            _sectionHost.Content = _dictionaryView;
        }
    }

    private void ShowSettings()
    {
        if (_composition is null) return;
        _ = new SettingsWindow(_composition.Settings, _composition.Engine).ShowDialog(this);
    }
}
