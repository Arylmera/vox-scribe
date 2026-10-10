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
                Panels.Toggle("Toggle mode — press once to start, press again to stop",
                    settings.Data.PushToTalkToggle,
                    v => save(settings.Data with { PushToTalkToggle = v })),
            },
        });
    }
}
