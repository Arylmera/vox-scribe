using Avalonia.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>Where and when the transcript is typed.</summary>
internal static class TypingSection
{
    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save) =>
        Panels.Section("TYPING", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Toggle("Type transcripts into the focused app", settings.Data.InjectText,
                    v => save(settings.Data with { InjectText = v })),
                Panels.Toggle("Type into the field that had focus when you pressed the shortcut",
                    settings.Data.AnchorFocus,
                    v => save(settings.Data with { AnchorFocus = v }),
                    hint: "You can switch windows or click elsewhere while speaking. On release "
                        + "Vox-Scribe brings that field back and types there."),
                Panels.Toggle("Type each phrase as you speak it, not all at the end (raw only)",
                    settings.Data.IncrementalInjection,
                    v => save(settings.Data with { IncrementalInjection = v }),
                    hint: "While the option above is on, phrases are held and typed together on "
                        + "release, so nothing lands in the wrong window."),
            },
        });
}
