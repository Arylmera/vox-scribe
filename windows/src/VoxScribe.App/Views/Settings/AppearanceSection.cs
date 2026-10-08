using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>The theme keys.</summary>
internal static class AppearanceSection
{
    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save, DictationEngine? engine = null) =>
        Panels.Section("APPEARANCE", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                BuildThemeRow(settings, save, engine),
                Panels.Note("Theme — pick one, then APPLY restarts Vox-Scribe with it."),
            },
        });

    // No engine (platform layer absent) means nothing can be recording.
    private static bool IsBusy(DictationEngine? engine) => engine is { State: not DictationState.Idle };

    private static StackPanel BuildThemeRow(AppSettings settings, Action<SettingsData> save, DictationEngine? engine)
    {
        var keys = new List<(string Id, Button Key)>();

        var apply = Panels.DeckButton("APPLY — RESTARTS VOX-SCRIBE");
        Avalonia.Automation.AutomationProperties.SetName(apply, "Apply theme changes");
        apply.Click += (_, _) =>
        {
            if (IsBusy(engine)) return;
            (Application.Current as App)?.Restart();
        };

        void SyncApply() => apply.IsVisible = Themes.Find(settings.Data.Theme).Id != Themes.Active.Id;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Snug };
        foreach (var theme in Themes.All)
        {
            var key = Panels.DeckButton(theme.Label.ToUpperInvariant());
            Avalonia.Automation.AutomationProperties.SetName(key, $"Theme: {theme.Label}");
            key.Click += (_, _) =>
            {
                if (IsBusy(engine)) return;
                save(settings.Data with { Theme = theme.Id, AccentVariant = null });
                Mark(settings, keys);
                SyncApply();
            };
            keys.Add((theme.Id, key));
            row.Children.Add(key);
        }

        row.Children.Add(apply);
        Mark(settings, keys);
        SyncApply();
        return row;
    }

    private static void Mark(AppSettings settings, List<(string Id, Button Key)> keys)
    {
        var saved = Themes.Find(settings.Data.Theme).Id;
        foreach (var (id, key) in keys)
        {
            var selected = id == saved;
            key.Foreground = selected ? Tokens.Brushes.Ink : Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Muted);
            key.BorderBrush = selected ? Tokens.Brushes.Ink : Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Outline);
        }
    }
}
