using Avalonia.Controls;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>The two chords and toggle mode. The recorder buttons belong to the window.</summary>
internal static class ShortcutsSection
{
    /// <summary>Builds the section around the window's live chord recorders.</summary>
    public static Control Build(
        AppSettings settings, Action<SettingsData> save,
        TransportKey raw, TransportKey cleanup, TextBlock warning) =>
        Panels.Section("SHORTCUTS", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                raw,
                warning,
                Panels.Note("Click, then press the key — or hold several keys together for a "
                    + "combination; releasing them records it. Escape cancels. The new "
                    + "shortcut works immediately: hold it anywhere to dictate."),
                cleanup,
                Panels.Note("Second shortcut. It records the same way, but sends the "
                    + "transcript through the cleanup model before typing it. The first "
                    + "shortcut stays raw and fast. Escape on this one unbinds it. "
                    + "Binding it for the first time needs a restart."),
                Panels.Toggle("Toggle mode — press once to start, press again to stop",
                    settings.Data.PushToTalkToggle,
                    v => save(settings.Data with { PushToTalkToggle = v })),
            },
        });
}
