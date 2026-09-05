using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>The theme keys and the accent swatches.</summary>
internal static class AppearanceSection
{
    /// <summary>The curated accent swatches — Void Glass cyan first, its default.</summary>
    private static readonly string[] AccentChoices =
        ["#4FD8E8", "#5A8CF5", "#4FE8A0", "#F06AD8", "#E8B44F"];

    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save)
    {
        var dots = new List<(string Hex, Border Dot)>();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Base };
        foreach (var hex in AccentChoices)
        {
            var dot = new Border
            {
                Width = Tokens.Material.SwatchSize,
                Height = Tokens.Material.SwatchSize,
                CornerRadius = new CornerRadius(Tokens.Material.SwatchSize / 2),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderThickness = new Thickness(Tokens.Border.Ring),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            dot.PointerPressed += (_, _) =>
            {
                save(settings.Data with { AccentColor = hex });
                MarkSelectedAccent(settings, dots);
            };
            dots.Add((hex, dot));
            row.Children.Add(dot);
        }

        MarkSelectedAccent(settings, dots);

        return Panels.Section("APPEARANCE", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                BuildThemeRow(settings, save),
                Panels.Note("Theme — takes effect at next start."),
                row,
                Panels.Note("Accent colour — tints the dictation pill and highlights. Applies immediately."),
            },
        });
    }

    /// <summary>One key per theme; the saved one is engaged.</summary>
    private static StackPanel BuildThemeRow(AppSettings settings, Action<SettingsData> save)
    {
        var keys = new List<(string Id, Button Key)>();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Snug };
        foreach (var (id, label) in Themes.Choices)
        {
            var key = Panels.DeckButton(label);
            key.Click += (_, _) =>
            {
                save(settings.Data with { Theme = id });
                MarkSelectedTheme(settings, keys);
            };
            keys.Add((id, key));
            row.Children.Add(key);
        }

        MarkSelectedTheme(settings, keys);
        return row;
    }

    /// <summary>Full ink and a solid edge on the saved theme's key; the others recede.</summary>
    private static void MarkSelectedTheme(AppSettings settings, List<(string Id, Button Key)> keys)
    {
        foreach (var (id, key) in keys)
        {
            var selected = string.Equals(id, settings.Data.Theme, StringComparison.OrdinalIgnoreCase);
            key.Foreground = selected
                ? Tokens.Brushes.Ink
                : Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Muted);
            key.BorderBrush = selected
                ? Tokens.Brushes.Ink
                : Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Outline);
        }
    }

    /// <summary>Rings the swatch matching the saved accent; clears the others.</summary>
    private static void MarkSelectedAccent(AppSettings settings, List<(string Hex, Border Dot)> dots)
    {
        foreach (var (hex, dot) in dots)
            dot.BorderBrush = string.Equals(hex, settings.Data.AccentColor, StringComparison.OrdinalIgnoreCase)
                ? Tokens.Brushes.Ink
                : Avalonia.Media.Brushes.Transparent;
    }
}
