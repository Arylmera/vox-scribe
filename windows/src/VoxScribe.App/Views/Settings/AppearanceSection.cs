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
    public static Control Build(AppSettings settings, Action<SettingsData> save, DictationEngine? engine = null)
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
            var colorName = hex switch
            {
                "#4FD8E8" => "Cyan",
                "#5A8CF5" => "Blue",
                "#4FE8A0" => "Green",
                "#F06AD8" => "Magenta",
                "#E8B44F" => "Amber",
                _ => hex,
            };
            Avalonia.Automation.AutomationProperties.SetName(dot, $"Accent color: {colorName}");
            Avalonia.Automation.AutomationProperties.SetHelpText(dot, $"Select {colorName} accent color. Applies to the dictation pill and highlights.");
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
                BuildThemeRow(settings, save, engine),
                Panels.Note("Theme — pick one, then APPLY restarts Vox-Scribe with it."),
                row,
                Panels.Note("Accent colour — tints the dictation pill and highlights. Applies immediately."),
            },
        });
    }

    // No engine (platform layer absent) means nothing can be recording.
    private static bool IsBusy(DictationEngine? engine) =>
        engine is { State: not DictationState.Idle };

    /// <summary>One key per theme; the saved one is engaged. Picking a theme other than the
    /// one on screen reveals an apply key that restarts the app. Disabled while recording.</summary>
    private static StackPanel BuildThemeRow(AppSettings settings, Action<SettingsData> save, DictationEngine? engine = null)
    {
        var keys = new List<(string Id, Button Key)>();

        var apply = Panels.DeckButton("APPLY — RESTARTS VOX-SCRIBE");
        Avalonia.Automation.AutomationProperties.SetName(apply, "Apply theme changes");
        Avalonia.Automation.AutomationProperties.SetHelpText(apply, "Restarts Vox-Scribe with the selected theme. Only available when a different theme is selected.");
        apply.Click += (_, _) =>
        {
            // A restart mid-dictation would drop the utterance; wait for the engine to settle.
            if (IsBusy(engine)) return;
            (Application.Current as App)?.Restart();
        };

        void SyncApply() => apply.IsVisible =
            !string.Equals(settings.Data.Theme, Themes.ActiveId, StringComparison.OrdinalIgnoreCase);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Snug };
        foreach (var (id, label) in Themes.Choices)
        {
            var key = Panels.DeckButton(label);
            Avalonia.Automation.AutomationProperties.SetName(key, $"Theme: {label}");
            Avalonia.Automation.AutomationProperties.SetHelpText(key, "Select this theme. Changes take effect after restart.");
            key.Click += (_, _) =>
            {
                // Disable changing theme while recording
                if (IsBusy(engine)) return;
                save(settings.Data with { Theme = id });
                MarkSelectedTheme(settings, keys);
                SyncApply();
            };
            keys.Add((id, key));
            row.Children.Add(key);
        }

        row.Children.Add(apply);
        MarkSelectedTheme(settings, keys);
        SyncApply();

        // Disable theme buttons while recording
        if (engine is not null)
        {
            // Simple polling: if this becomes a bottleneck, add property-changed notifications
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(100);
                    var recording = engine.State != DictationState.Idle;
                    foreach (var (_, key) in keys)
                    {
                        key.IsEnabled = !recording;
                    }
                    apply.IsEnabled = !recording;
                }
            });
        }

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
