using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views;

/// <summary>
/// Shared panel furniture — search rows, footers, cards, buttons on a dark readout.
/// </summary>
/// <remarks>
/// Every value comes from <see cref="Tokens"/>. Factoring these out is what stops the same
/// padding being typed slightly differently in three views, which is how a design system
/// erodes.
/// </remarks>
internal static class Panels
{
    /// <summary>A search field styled for a dark readout well.</summary>
    public static TextBox SearchBox(string placeholder) => new()
    {
        Watermark = placeholder,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Body,
        Foreground = Tokens.Brushes.InkOnDeck,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The row a search field sits in, with a seam beneath it.</summary>
    public static Border SearchRow(Control search, Control? trailing = null)
    {
        var row = new DockPanel();

        if (trailing is not null)
        {
            DockPanel.SetDock(trailing, Dock.Right);
            row.Children.Add(trailing);
        }

        row.Children.Add(search);

        return new Border
        {
            Background = Tokens.Brushes.Deck,
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
            BorderThickness = new Thickness(0, 0, 0, Tokens.Border.Seam),
            Child = row,
        };
    }

    /// <summary>A footer strip with a count on the left and an action on the right.</summary>
    public static Border Footer(Control leading, Control trailing)
    {
        var row = new DockPanel();
        DockPanel.SetDock(trailing, Dock.Right);
        row.Children.Add(trailing);
        row.Children.Add(leading);

        return new Border
        {
            Background = Tokens.Brushes.Deck,
            Padding = new Thickness(Tokens.Space.Base, Tokens.Space.Snug),
            BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
            BorderThickness = new Thickness(0, Tokens.Border.Seam, 0, 0),
            Child = row,
        };
    }

    /// <summary>
    /// The shell both list views share: a search row on top, a footer at the foot, and the
    /// list scrolling between them.
    /// </summary>
    /// <remarks>
    /// Extracted because the two views had it letter for letter. It is not only duplication:
    /// the order of the children <i>is</i> the rule — a docked panel gives the last child
    /// what is left, so the scroller has to be added last or the footer eats the view.
    /// </remarks>
    public static DockPanel ListShell(Control searchRow, Control footer, Control list) => new()
    {
        Children =
        {
            Docked(searchRow, Dock.Top),
            Docked(footer, Dock.Bottom),
            new ScrollViewer { Content = list },
        },
    };

    /// <summary>The scrolling body of a list view: rows stacked on the deck.</summary>
    public static StackPanel ListBody() => new()
    {
        Spacing = Tokens.Space.Snug,
        Margin = new Thickness(Tokens.Space.Base),
    };

    /// <summary>The "N THINGS" readout that sits in a footer.</summary>
    public static Silkscreen Counter() =>
        new() { Foreground = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Soft) };

    /// <summary>
    /// One row with content on the left and actions on the right.
    /// </summary>
    /// <remarks>
    /// A single-cell grid rather than a <c>DockPanel</c>: the two overlap when the left side
    /// runs long, and the right side is added last so it stays on top and keeps its clicks.
    /// </remarks>
    public static Grid SplitRow(Control leading, Control trailing)
    {
        trailing.HorizontalAlignment = HorizontalAlignment.Right;
        return new Grid { Children = { leading, trailing } };
    }

    /// <summary>A horizontal run of controls at the standard gap.</summary>
    public static StackPanel Row(double spacing, params Control[] children)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = spacing,
            VerticalAlignment = VerticalAlignment.Center,
        };

        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    /// <summary>A small outlined button for use on a dark readout.</summary>
    public static Button DeckButton(string label) => new()
    {
        Content = label,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Silkscreen,
        FontWeight = FontWeight.Medium,
        Foreground = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Muted),
        Background = Brushes.Transparent,
        BorderBrush = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Outline),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        CornerRadius = new CornerRadius(Tokens.Radius.Chip),
        Padding = new Thickness(Tokens.Space.Snug, Tokens.Space.Hair),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>One row of content, on the dark readout surface.</summary>
    public static Border DeckCard(Control content) => new()
    {
        Background = Tokens.Brushes.Deck,
        CornerRadius = new CornerRadius(Tokens.Radius.Panel),
        BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
        BorderThickness = new Thickness(Tokens.Border.Hairline),
        Padding = new Thickness(Tokens.Space.Base),
        Child = content,
    };

    /// <summary>Centred "nothing here yet" copy.</summary>
    public static Control EmptyState(string label, string detail) => new StackPanel
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = Tokens.Space.Snug,
        Margin = new Thickness(0, Tokens.Space.Panel),
        Children =
        {
            new Silkscreen
            {
                Text = label,
                IsLarge = true,
                Foreground = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Soft),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
            new TextBlock
            {
                Text = detail,
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Label,
                Foreground = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Ghost),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        },
    };

    /// <summary>
    /// Docks a control and returns it, so it reads inline in a Children list.
    /// </summary>
    /// <remarks>Not named <c>Dock</c>: that shadows the <see cref="Avalonia.Controls.Dock"/>
    /// enum at every call site.</remarks>
    public static Control Docked(Control control, Dock side)
    {
        DockPanel.SetDock(control, side);
        return control;
    }

    /// <summary>A silkscreen label above a control, the way a panel is printed.</summary>
    public static StackPanel Labelled(string label, Control content) => new()
    {
        Spacing = Tokens.Space.Tight,
        Children = { new Silkscreen { Text = label }, content },
    };

    /// <summary>A settings section: silkscreen title over content on a brushed panel, fading in.</summary>
    public static BrushedPanel Section(string label, Control content)
    {
        var section = new BrushedPanel
        {
            Opacity = Tokens.Motion.SectionFadeInFrom,
            Child = new StackPanel
            {
                Margin = new Thickness(Tokens.Space.Roomy),
                Spacing = Tokens.Space.Base,
                Children = { new Silkscreen { Text = label, IsLarge = true }, content },
            },
        };

        var transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = Tokens.Motion.FadeIn },
        };
        section.Transitions = transitions;
        section.Loaded += (_, _) => section.Opacity = 1;

        return section;
    }

    /// <summary>Secondary explanatory copy.</summary>
    public static TextBlock Note(string text) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Label,
        Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A settings text box that persists on focus loss; empty saves as null.</summary>
    public static TextBox Field(string hint, string? value, Action<string?> onCommit)
    {
        var box = new TextBox
        {
            Text = value ?? string.Empty,
            Watermark = hint,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
        };

        box.LostFocus += (_, _) =>
        {
            var text = box.Text?.Trim();
            onCommit(string.IsNullOrEmpty(text) ? null : text);
        };

        return box;
    }

    /// <summary>A labelled check box, with an optional hint line beneath the label.</summary>
    public static CheckBox Toggle(string label, bool value, Action<bool> onChange, string? hint = null)
    {
        var title = new TextBlock
        {
            Text = label,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.Wrap,
        };

        var box = new CheckBox
        {
            IsChecked = value,
            Content = hint is null
                ? title
                : new StackPanel
                {
                    Spacing = Tokens.Space.Tight,
                    Children = { title, Note(hint) },
                },
        };

        box.IsCheckedChanged += (_, _) => onChange(box.IsChecked ?? false);
        return box;
    }
}
