using Avalonia.Controls;
using Avalonia.Layout;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>The four chords a user can record.</summary>
internal enum ShortcutSlot
{
    /// <summary>Types the transcript as heard.</summary>
    Raw,

    /// <summary>Types the transcript after the cleanup model.</summary>
    Cleanup,

    /// <summary>Deletes the last dictation's text.</summary>
    Undo,

    /// <summary>Sends the transcript to the command window and submits it.</summary>
    Command,
}

/// <summary>The chords, toggle mode and the command target. The recorder buttons belong to the window.</summary>
internal static class ShortcutsSection
{
    /// <summary>Builds the section around the window's live chord recorders.</summary>
    public static Control Build(
        AppSettings settings, Action<SettingsData> save,
        IReadOnlyDictionary<ShortcutSlot, TransportKey> keys, TextBlock warning)
    {
        var toggle = Panels.Toggle("Toggle mode — press once to start, press again to stop",
            settings.Data.PushToTalkToggle,
            v => save(settings.Data with { PushToTalkToggle = v }));
        var autoStop = AutoStop(settings, save, toggle);

        return Panels.Section("SHORTCUTS", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Labelled("RAW", keys[ShortcutSlot.Raw]),
                warning,
                Panels.Note("Click, then press the key — or hold several keys together for a "
                    + "combination; releasing them records it. Escape cancels. Every shortcut "
                    + "works the moment it is recorded."),
                Panels.Labelled("CLEANUP", keys[ShortcutSlot.Cleanup]),
                Panels.Note("Records the same way, but sends the transcript through the cleanup "
                    + "model before typing it. The raw shortcut stays raw and fast. Escape on "
                    + "this one unbinds it."),
                Panels.Labelled("UNDO", keys[ShortcutSlot.Undo]),
                Panels.Note("After a dictation has been typed: deletes its text from wherever it "
                    + "landed, one dictation deep. Not bound until you record one. Escape unbinds."),
                Panels.Labelled("CANCEL · FIXED KEY", new TransportKey { Content = "ESC", IsHitTestVisible = false, Focusable = false }),
                Panels.Note("While recording: stops the microphone and types nothing. Outside a "
                    + "recording Escape is left alone, so it keeps its usual meaning everywhere."),
                Panels.Labelled("COMMAND", keys[ShortcutSlot.Command]),
                Panels.Note("Dictate at Claude Code instead of at a text field: the transcript "
                    + "(tidied when a cleanup model is set) goes to the Claude Code session armed in "
                    + "its band, or else to the window set under CLAUDE, and is submitted. Escape unbinds."),
                toggle,
                autoStop,
            },
        });
    }

    /// <summary>Off, or 1–10 s of silence that end a toggle dictation. Disabled outside toggle mode.</summary>
    private static StackPanel AutoStop(AppSettings settings, Action<SettingsData> save, CheckBox toggle)
    {
        var choices = Enumerable.Range(0, 11)
            .Select(s => new ComboBoxItem { Content = s == 0 ? "Off" : $"{s} s", Tag = s })
            .ToList();
        var picker = new ComboBox
        {
            ItemsSource = choices,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
            IsEnabled = settings.Data.PushToTalkToggle,
            SelectedItem = choices.FirstOrDefault(c => (int)c.Tag! == settings.Data.AutoStopSilenceSeconds) ?? choices[3],
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: int s } && s != settings.Data.AutoStopSilenceSeconds)
                save(settings.Data with { AutoStopSilenceSeconds = s });
        };
        toggle.IsCheckedChanged += (_, _) => picker.IsEnabled = toggle.IsChecked ?? false;

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Labelled("STOP AFTER SILENCE", picker),
                Panels.Note("Toggle mode only: once you have spoken, this much silence ends the "
                    + "dictation as a second press would."),
            },
        };
    }
}
