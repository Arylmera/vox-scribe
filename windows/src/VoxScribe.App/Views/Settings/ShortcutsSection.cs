using Avalonia.Controls;
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
        var commandTitle = Panels.Field("Claude",
            settings.Data.CommandWindowTitle,
            v => save(settings.Data with { CommandWindowTitle = v ?? "Claude" }));

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
                Panels.Note("Deletes the last dictation's text from wherever it landed, without "
                    + "opening this window. Same as the UNDO key in the voice band. Escape unbinds."),
                Panels.Labelled("COMMAND", keys[ShortcutSlot.Command]),
                Panels.Note("Dictate at Claude Code instead of at a text field: the transcript "
                    + "(tidied when a cleanup model is set) is typed into the window below and "
                    + "submitted with Return, whatever has focus. Escape unbinds."),
                Panels.Labelled("COMMAND WINDOW TITLE CONTAINS", commandTitle),
                Panels.Note("The first visible window whose title contains this text. \"Claude\" "
                    + "matches the desktop app and a terminal tab running Claude Code. If no "
                    + "window matches, nothing is typed anywhere and the pill says so."),
                Panels.Toggle("Toggle mode — press once to start, press again to stop",
                    settings.Data.PushToTalkToggle,
                    v => save(settings.Data with { PushToTalkToggle = v })),
            },
        });
    }
}
