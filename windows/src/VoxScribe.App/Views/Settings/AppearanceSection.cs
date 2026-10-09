using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>
/// Theme cards and the saved theme's accent swatches. A pick is saved; App re-applies the
/// theme and the window rebuilds on the same tab, so the selection marks are always fresh.
/// </summary>
internal static class AppearanceSection
{
    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save, DictationEngine? engine = null)
    {
        var saved = Themes.Find(settings.Data.Theme);
        var savedVariant = saved.Variants.FirstOrDefault(
            v => string.Equals(v.Id, settings.Data.AccentVariant, StringComparison.OrdinalIgnoreCase)) ?? saved.Variants[0];

        var cards = new WrapPanel { ItemSpacing = Tokens.Space.Base, LineSpacing = Tokens.Space.Base };
        foreach (var theme in Themes.All)
        {
            cards.Children.Add(ThemeCard(theme, ReferenceEquals(theme, saved), () =>
            {
                if (IsBusy(engine)) return;
                save(settings.Data with { Theme = theme.Id, AccentVariant = null });
            }));
        }

        var swatches = new WrapPanel { ItemSpacing = Tokens.Space.Base, LineSpacing = Tokens.Space.Base };
        foreach (var variant in saved.Variants)
        {
            swatches.Children.Add(Swatch(variant, ReferenceEquals(variant, savedVariant), () =>
            {
                if (IsBusy(engine)) return;
                save(settings.Data with { Theme = saved.Id, AccentVariant = variant.Id });
            }));
        }

        return Panels.Section("APPEARANCE", new StackPanel
        {
            Spacing = Tokens.Space.Base,
            Children =
            {
                Panels.Labelled("THEME", cards),
                Panels.Labelled("ACCENT", swatches),
                Panels.Note("Applies immediately. Light or dark follows Windows "
                    + "(Settings → Personalisation → Colours)."),
            },
        });
    }

    // A rebuild mid-dictation would be harmless, but a pick then would surprise; wait for idle.
    private static bool IsBusy(DictationEngine? engine) => engine is { State: not DictationState.Idle };

    private static Button ThemeCard(ThemeDefinition theme, bool selected, Action pick)
    {
        // The preview shows the theme in the mode Windows is in now, with its first accent.
        var palette = Themes.IsDark ? theme.Dark : theme.Light;
        var accent = Tokens.Colors.Rgb(Themes.IsDark ? theme.Variants[0].Dark : theme.Variants[0].Light);

        var preview = new Border
        {
            Height = Tokens.Material.ThemePreviewHeight,
            CornerRadius = new CornerRadius(Math.Min(theme.CardRadius, Tokens.Material.ThemePreviewRadius)),
            Background = new SolidColorBrush(Tokens.Colors.Rgb(palette.Ground)),
            BorderBrush = new SolidColorBrush(Tokens.Colors.Rgb(palette.Border)),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            Padding = new Thickness(Tokens.Space.Snug),
            Child = new StackPanel
            {
                Spacing = Tokens.Space.Tight,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Aa",
                        FontFamily = theme.Display,
                        FontWeight = theme.DisplayWeight,
                        FontSize = Tokens.Fonts.Row,
                        Foreground = new SolidColorBrush(Tokens.Colors.Rgb(palette.Ink)),
                    },
                    new Border
                    {
                        Height = Tokens.Material.ThemePreviewBar,
                        Width = Tokens.Material.ThemePreviewBarWidth,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        CornerRadius = new CornerRadius(Tokens.Space.Hair),
                        Background = new SolidColorBrush(accent),
                    },
                },
            },
        };

        var borderBrush = selected ? Tokens.Brushes.Accent : Brushes.Transparent;
        var card = new Button
        {
            Width = Tokens.Material.ThemeCardWidth,
            Padding = new Thickness(Tokens.Space.Snug),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(Tokens.Border.Ring),
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(Tokens.Radius.Panel),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = Tokens.Space.Tight,
                Children =
                {
                    preview,
                    new TextBlock
                    {
                        Text = theme.Label,
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Body,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Tokens.Brushes.Ink,
                    },
                    new TextBlock
                    {
                        Text = theme.Description,
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Label,
                        Foreground = Tokens.Brushes.InkSecondary,
                    },
                },
            },
        };
        AutomationProperties.SetName(card, $"Theme: {theme.Label}");

        // Fluent's Button theme would otherwise grey the card on hover; pin the theme's flat
        // hover fill instead, same as other unselected controls (IconButton, Shell nav). The
        // selection ring is unaffected — it is the border, pinned separately below.
        Shell.PinHoverStates(card, Tokens.Brushes.Hover, Tokens.Brushes.Ink, borderBrush);
        card.Click += (_, _) => pick();
        return card;
    }

    private static Button Swatch(AccentVariant variant, bool selected, Action pick)
    {
        var half = Tokens.Material.SwatchSize / 2;
        var dot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new Border
                {
                    Width = half, Height = Tokens.Material.SwatchSize,
                    CornerRadius = new CornerRadius(half, 0, 0, half),
                    Background = new SolidColorBrush(Tokens.Colors.Rgb(variant.Light)),
                },
                new Border
                {
                    Width = half, Height = Tokens.Material.SwatchSize,
                    CornerRadius = new CornerRadius(0, half, half, 0),
                    Background = new SolidColorBrush(Tokens.Colors.Rgb(variant.Dark)),
                },
            },
        };

        var borderBrush = selected ? Tokens.Brushes.Ink : Brushes.Transparent;
        var swatch = new Button
        {
            Padding = new Thickness(Tokens.Space.Tight),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(Tokens.Border.Ring),
            BorderBrush = borderBrush,
            CornerRadius = new CornerRadius(Tokens.Radius.Chip),
            Content = new StackPanel
            {
                Spacing = Tokens.Space.Tight,
                Children =
                {
                    dot,
                    new TextBlock
                    {
                        Text = variant.Label,
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Label,
                        Foreground = Tokens.Brushes.InkSecondary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
        AutomationProperties.SetName(swatch, $"Accent: {variant.Label}");
        ToolTip.SetTip(swatch, $"{variant.Label} — left half light mode, right half dark mode");

        // Same reasoning as the theme card: pin the flat hover fill so Fluent's default grey
        // never shows, leaving the selection ring (the border, pinned separately) alone.
        Shell.PinHoverStates(swatch, Tokens.Brushes.Hover, Tokens.Brushes.InkSecondary, borderBrush);
        swatch.Click += (_, _) => pick();
        return swatch;
    }
}
