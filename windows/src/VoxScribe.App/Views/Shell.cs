using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace VoxScribe.App.Views;

/// <summary>A nav item whose icon is recoloured with its label.</summary>
internal sealed class NavButton : Button
{
    /// <summary>Keeps Fluent's Button template; a subclass would otherwise have no theme.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>The stroke icon.</summary>
    public IconPath? Icon { get; init; }
}

/// <summary>The skeleton's shared furniture: navigation, selection marking, icons, the orb mark.</summary>
internal static class Shell
{
    /// <summary>Home.</summary>
    public const string HomeIcon = "M4 11l8-7 8 7v9a1 1 0 0 1-1 1h-4v-6h-6v6H5a1 1 0 0 1-1-1z";

    /// <summary>History (clock).</summary>
    public const string HistoryIcon = "M12 3.5a8.5 8.5 0 1 0 0 17a8.5 8.5 0 1 0 0-17M12 7.5V12l3 2";

    /// <summary>Dictionary (book).</summary>
    public const string DictionaryIcon = "M5 4.5h10a4 4 0 0 1 4 4V20H9a4 4 0 0 1-4-4zM9 9h6M9 13h4";

    /// <summary>Settings (sliders).</summary>
    public const string SettingsIcon =
        "M4 7h10M18 7h2M4 17h4M12 17h8M16 5a2 2 0 1 0 0 4a2 2 0 1 0 0-4M10 15a2 2 0 1 0 0 4a2 2 0 1 0 0-4";

    /// <summary>Copy.</summary>
    public const string CopyIcon = "M8 8h12v12H8zM16 8V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h3";

    /// <summary>Type again.</summary>
    public const string RetypeIcon = "M9 14l-5-5 5-5M4 9h10a6 6 0 0 1 0 12h-3";

    private const double OrbCoreX = 0.35;
    private const double OrbCoreY = 0.3;
    private const double OrbRadius = 0.65;
    private const double OrbSecondaryStop = 0.22;
    private const double OrbAccentStop = 0.58;
    private const double OrbFadeStop = 0.74;

    /// <summary>The text, in capitals when the theme sets labels in capitals.</summary>
    public static string Caps(string text) => Themes.Active.Uppercase ? text.ToUpperInvariant() : text;

    /// <summary>A stroke icon.</summary>
    public static IconPath Icon(string data, double size, double stroke, IBrush brush) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = brush,
        StrokeThickness = stroke,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>A page's title, in the theme's display face.</summary>
    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Display,
        FontWeight = Themes.Active.DisplayWeight,
        FontSize = Themes.Active.HeadlineSize,
        Foreground = Tokens.Brushes.Ink,
        Margin = new Thickness(0, 0, 0, Tokens.Space.Roomy),
    };

    /// <summary>
    /// The navigation column for the active theme, filling <paramref name="items"/> with one
    /// button per page. Selection is painted by the caller with <see cref="Paint"/>.
    /// </summary>
    public static Control Nav(Action<AppPage> navigate, IDictionary<AppPage, NavButton> items)
    {
        var theme = Themes.Active;
        var rail = theme.Nav == NavStyle.Rail;
        var stack = new StackPanel { Spacing = Tokens.Space.Tight };

        if (rail)
        {
            var mark = Orb(Tokens.Material.OrbMarkSize, core: false);
            mark.Margin = new Thickness(0, 0, 0, Tokens.Space.Wide);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(mark);
        }
        else if (Wordmark() is { } wordmark)
        {
            stack.Children.Add(wordmark);
        }

        foreach (var page in Enum.GetValues<AppPage>())
        {
            var item = Item(page, rail);
            item.Click += (_, _) => navigate(page);
            items[page] = item;
            stack.Children.Add(item);
        }

        return theme.Nav switch
        {
            NavStyle.Rail => new Border
            {
                Width = theme.NavWidth,
                Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Tight),
                Child = stack,
            },
            NavStyle.FloatingCard => new Border
            {
                Width = theme.NavWidth,
                Margin = new Thickness(Tokens.Space.Wide, 0, 0, Tokens.Space.Wide),
                Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Wide),
                Background = Tokens.Brushes.Panel,
                CornerRadius = new CornerRadius(Tokens.Radius.Panel),
                Child = stack,
            },
            _ => new Border
            {
                Width = theme.NavWidth,
                Padding = new Thickness(Tokens.Space.Roomy, Tokens.Space.Wide),
                BorderBrush = Tokens.Brushes.Seam,
                BorderThickness = new Thickness(0, 0, theme.ContentOnSurface ? 0 : Tokens.Border.Hairline, 0),
                Child = stack,
            },
        };
    }

    /// <summary>Marks <paramref name="item"/> selected or not, in <paramref name="style"/>.</summary>
    public static void Paint(Button item, Selection style, bool selected)
    {
        item.BorderBrush = null;
        item.BorderThickness = new Thickness(0);

        if (!selected)
        {
            item.Background = Brushes.Transparent;
            item.Foreground = Tokens.Brushes.InkSecondary;
        }
        else
        {
            switch (style)
            {
                case Selection.Tint:
                    item.Background = new SolidColorBrush(Tokens.Colors.AccentTint);
                    item.Foreground = Tokens.Brushes.Accent;
                    break;
                case Selection.AccentFill:
                    item.Background = Tokens.Brushes.AccentFill;
                    item.Foreground = Tokens.Brushes.OnAccent;
                    break;
                case Selection.InkFill:
                    item.Background = Tokens.Brushes.Ink;
                    item.Foreground = Tokens.Brushes.Chassis;
                    break;
                case Selection.Bar:
                    item.Background = Tokens.Brushes.Hover;
                    item.Foreground = Tokens.Brushes.Ink;
                    item.BorderBrush = Tokens.Brushes.Accent;
                    item.BorderThickness = new Thickness(Tokens.Border.Ring, 0, 0, 0);
                    break;
                case Selection.Underline:
                    item.Background = Brushes.Transparent;
                    item.Foreground = Tokens.Brushes.Ink;
                    item.BorderBrush = Tokens.Brushes.Accent;
                    item.BorderThickness = new Thickness(0, 0, 0, Tokens.Border.Ring);
                    break;
                default:
                    item.Background = Tokens.Brushes.Hover;
                    item.Foreground = Tokens.Brushes.Ink;
                    break;
            }
        }

        if (item is NavButton { Icon: { } icon }) icon.Stroke = item.Foreground;
    }

    /// <summary>
    /// A radial-gradient orb in the accent: the Orb theme's mark. With <paramref name="core"/>
    /// it has the white hot spot of the Home header's large orb.
    /// </summary>
    public static Ellipse Orb(double size, bool core)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(OrbCoreX, OrbCoreY, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(OrbCoreX, OrbCoreY, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(OrbRadius, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(OrbRadius, RelativeUnit.Relative),
        };
        if (core) brush.GradientStops.Add(new GradientStop(Tokens.Colors.Specular, 0));
        brush.GradientStops.Add(new GradientStop(Tokens.Colors.AccentSecondary, core ? OrbSecondaryStop : 0));
        brush.GradientStops.Add(new GradientStop(Tokens.Colors.Accent, OrbAccentStop));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, OrbFadeStop));

        return new Ellipse { Width = size, Height = size, Fill = brush, IsHitTestVisible = false };
    }

    private static string PageIcon(AppPage page) => page switch
    {
        AppPage.History => HistoryIcon,
        AppPage.Dictionary => DictionaryIcon,
        AppPage.Settings => SettingsIcon,
        _ => HomeIcon,
    };

    private static NavButton Item(AppPage page, bool rail)
    {
        var icon = Icon(PageIcon(page), Tokens.Material.NavIconSize, Tokens.Material.NavIconStroke, Tokens.Brushes.InkSecondary);
        var label = new TextBlock
        {
            Text = Caps(page.ToString()),
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = rail ? Tokens.Fonts.Caption : Tokens.Fonts.Row,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };

        var item = new NavButton
        {
            Icon = icon,
            Content = new StackPanel
            {
                Orientation = rail ? Orientation.Vertical : Orientation.Horizontal,
                Spacing = rail ? Tokens.Space.Tight : Tokens.Space.Base,
                HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, label },
            },
            Height = rail ? Tokens.Material.RailItemHeight : Tokens.Material.NavItemHeight,
            Width = rail ? Tokens.Material.RailItemWidth : double.NaN,
            HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            HorizontalContentAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(rail ? 0 : Tokens.Space.Base, 0),
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            Background = Brushes.Transparent,
        };
        AutomationProperties.SetName(item, page.ToString());
        return item;
    }

    private static StackPanel? Wordmark()
    {
        var theme = Themes.Active;
        if (theme.WordmarkLead.Length == 0) return null;

        TextBlock Run(string text, IBrush brush, bool italic) => new()
        {
            Text = text,
            FontFamily = Tokens.Fonts.Display,
            FontWeight = theme.DisplayWeight,
            FontSize = theme.WordmarkSize,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = brush,
        };

        var mark = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(Tokens.Space.Base, 0, 0, Tokens.Space.Wide),
            IsHitTestVisible = false,
        };
        mark.Children.Add(Run(theme.WordmarkLead, Tokens.Brushes.Ink, italic: false));
        if (theme.WordmarkAccent.Length > 0)
        {
            mark.Children.Add(Run(theme.WordmarkAccent, Tokens.Brushes.Accent, theme.WordmarkAccentItalic));
        }

        if (theme.WordmarkTail.Length > 0) mark.Children.Add(Run(theme.WordmarkTail, Tokens.Brushes.Ink, italic: false));
        return mark;
    }
}
