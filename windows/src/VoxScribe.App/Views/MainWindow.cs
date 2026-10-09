using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>The main window's pages, in navigation order.</summary>
public enum AppPage
{
    /// <summary>Status, stats, recent dictations.</summary>
    Home,

    /// <summary>All transcripts.</summary>
    History,

    /// <summary>Correction rules.</summary>
    Dictionary,

    /// <summary>Settings, in tabs.</summary>
    Settings,
}

/// <summary>
/// The main window: a title strip in the extended chrome, the theme's navigation, and the
/// open page. Rebuilt whole on <see cref="Themes.Changed"/>.
/// </summary>
/// <remarks>
/// Built in code rather than XAML, deliberately: every value comes from <see cref="Tokens"/>.
/// </remarks>
public sealed class MainWindow : Window
{
    private readonly Composition? _composition;
    private readonly Dictionary<AppPage, NavButton> _nav = [];
    private readonly EventHandler _onThemeChanged;

    private ContentControl _host = new();
    private TranscriptionsView? _history;
    private DictionaryView? _dictionary;
    private Control? _historyPage;
    private Control? _dictionaryPage;
    private string _historySearch = string.Empty;
    private string _dictionarySearch = string.Empty;
    private AppPage _page = AppPage.Home;
    private SettingsTab _settingsTab = SettingsTab.General;

    /// <summary>Set just before an explicit quit so the hide-to-tray guard steps aside.</summary>
    public bool ExitAllowed { get; set; }

    /// <summary>The page on screen.</summary>
    public AppPage CurrentPage => _page;

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
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
            new Uri("avares://VoxScribe.App/Assets/app.ico")));

        // The system keeps its caption buttons; the app paints the rest of the chrome.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = Tokens.Material.TitleBarHeight;

        // Close hides to the tray: a closed Avalonia window cannot be shown again. Real exit
        // sets ExitAllowed first (tray Quit).
        Closing += (_, e) =>
        {
            if (ExitAllowed) return;
            e.Cancel = true;
            (_host.Content as SettingsPage)?.CancelRecording();
            Hide();
        };

        // Posted: Changed can fire from inside a click handler on the Appearance tab.
        _onThemeChanged = (_, _) => Dispatcher.UIThread.Post(Rebuild);
        Themes.Changed += _onThemeChanged;
        Rebuild();

        Opacity = Tokens.Motion.FadeInFrom;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = Tokens.Motion.FadeIn },
        };
        Loaded += (_, _) => Opacity = 1;

        _composition?.Engine?.Start();
    }

    /// <summary>Opens <paramref name="page"/> and marks it in the navigation.</summary>
    public void ShowPage(AppPage page)
    {
        // Leaving Settings: remember the tab and make sure no key-capture hook survives.
        if (_host.Content is SettingsPage leaving)
        {
            _settingsTab = leaving.Tab;
            leaving.CancelRecording();
        }

        _page = page;
        foreach (var (p, item) in _nav) Shell.Paint(item, Themes.Active.NavSelection, p == page);

        _host.Content = page switch
        {
            AppPage.History => HistoryPage(),
            AppPage.Dictionary => DictionaryPage(),
            AppPage.Settings => _composition is null
                ? Panels.EmptyState("SETTINGS", "Nothing to configure without an engine.")
                : new SettingsPage(_composition.Settings, _composition.Engine, _settingsTab),
            _ => new HomePage(_composition?.Transcripts, _composition?.Settings, ShowPage, RetypeAsync),
        };
    }

    /// <summary>Rebuilds every control in the current theme, keeping the page and the search texts.</summary>
    private void Rebuild()
    {
        // A theme picked on the Appearance tab must come back on the Appearance tab.
        if (_host.Content is SettingsPage open)
        {
            _settingsTab = open.Tab;
            open.CancelRecording();
        }

        // A control has one parent and the old ones carry the old theme's brushes: start fresh.
        _historySearch = _history?.SearchText ?? _historySearch;
        _dictionarySearch = _dictionary?.SearchText ?? _dictionarySearch;
        _history = null;
        _dictionary = null;
        _historyPage = null;
        _dictionaryPage = null;
        _nav.Clear();
        _host = new ContentControl();

        var content = new Border
        {
            Padding = new Thickness(Tokens.Space.Panel, Tokens.Space.Base, Tokens.Space.Panel, Tokens.Space.Wide),
            Child = _host,
        };
        if (Themes.Active.ContentOnSurface)
        {
            content.Background = Tokens.Brushes.Panel;
            content.BorderBrush = Tokens.Brushes.Seam;
            content.BorderThickness = new Thickness(Tokens.Border.Hairline, Tokens.Border.Hairline, 0, 0);
            content.CornerRadius = new CornerRadius(Tokens.Radius.Panel, 0, 0, 0);
        }

        var root = new DockPanel();
        root.Children.Add(Panels.Docked(BuildTitleStrip(), Dock.Top));
        root.Children.Add(Panels.Docked(Shell.Nav(ShowPage, _nav), Dock.Left));
        root.Children.Add(content);

        Background = Tokens.Brushes.Chassis;
        Content = root;
        ShowPage(_page);
    }

    private static Border BuildTitleStrip() => new()
    {
        Height = Tokens.Material.TitleBarHeight,
        Padding = new Thickness(Tokens.Space.Roomy, 0, Tokens.Material.CaptionButtonsReserve, 0),
        Child = new TextBlock
        {
            Text = "Vox-Scribe",
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            Foreground = Tokens.Brushes.InkSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        },
    };

    private Control HistoryPage()
    {
        if (_composition is null) return Panels.EmptyState("NO RECORDINGS", "Hold the push-to-talk key and speak.");

        // Cached: the frame is the view's parent and must not be rebuilt on every visit.
        _history ??= new TranscriptionsView(_composition.Transcripts) { SearchText = _historySearch };
        return _historyPage ??= Framed("History", _history);
    }

    private Control DictionaryPage()
    {
        if (_composition is null) return Panels.EmptyState("DICTIONARY EMPTY", "Add words it keeps getting wrong.");

        _dictionary ??= new DictionaryView(_composition.Dictionary, _composition.Transcripts) { SearchText = _dictionarySearch };
        return _dictionaryPage ??= Framed("Dictionary", _dictionary);
    }

    private static DockPanel Framed(string title, Control body) => new()
    {
        Children = { Panels.Docked(Shell.PageTitle(Shell.Caps(title)), Dock.Top), body },
    };

    /// <summary>
    /// "Type again": the button gave focus to this window, so the text would land here. Minimise
    /// first, give focus a moment to return to the previous app, then type.
    /// </summary>
    private async Task RetypeAsync(string text)
    {
        if (_composition?.Injector is not { } injector) return;
        if (_composition.Engine is { State: not DictationState.Idle }) return;

        WindowState = WindowState.Minimized;
        await Task.Delay(Tokens.Motion.RetypeSettle).ConfigureAwait(true);
        await injector.InjectAsync(text, CancellationToken.None).ConfigureAwait(true);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        Themes.Changed -= _onThemeChanged;
        base.OnClosed(e);
    }
}
