using Avalonia.Controls;
using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>Where and when the transcript is typed.</summary>
/// <remarks>
/// Focus anchoring and incremental typing are two settings but only three behaviours: an
/// anchored dictation is always held until release, so "anchor + incremental" types exactly
/// like "anchor" alone. Two check boxes let the user build that combination and wonder why the
/// second one does nothing; one choice of three cannot express it.
/// </remarks>
internal static class TypingSection
{
    private const string Group = "typing-timing";

    private enum Timing
    {
        AnchoredOnRelease,
        AsYouSpeak,
        OnRelease,
    }

    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save)
    {
        var current = settings.Data.AnchorFocus ? Timing.AnchoredOnRelease
            : settings.Data.IncrementalInjection ? Timing.AsYouSpeak
            : Timing.OnRelease;

        void Choose(Timing timing) => save(settings.Data with
        {
            AnchorFocus = timing == Timing.AnchoredOnRelease,
            IncrementalInjection = timing == Timing.AsYouSpeak,
        });

        return Panels.Section("TYPING", new StackPanel
        {
            Spacing = Tokens.Space.Roomy,
            Children =
            {
                Panels.Labelled("WHEN IS THE TEXT TYPED", new StackPanel
                {
                    Spacing = Tokens.Space.Snug,
                    Children =
                    {
                        Option(Timing.AnchoredOnRelease, current, Choose,
                            "On release, in the field where you started",
                            "Switch windows or click elsewhere while you speak: Vox-Scribe brings "
                            + "that field back and types the whole dictation there. The pill shows "
                            + "the words as they arrive."),
                        Option(Timing.AsYouSpeak, current, Choose,
                            "As you speak, phrase by phrase",
                            "Each phrase is typed the moment it is transcribed, into whatever has "
                            + "focus right then — so stay in the field while you talk."),
                        Option(Timing.OnRelease, current, Choose,
                            "On release, wherever you are",
                            "The whole dictation is typed at once into whatever has focus when "
                            + "you let go."),
                        Panels.Note("The cleanup and command shortcuts always type once, on release: "
                            + "the model needs the whole sentence, and text already typed cannot be repaired."),
                    },
                }),
                Panels.Toggle("Spoken punctuation",
                    settings.Data.SpokenPunctuation,
                    v => save(settings.Data with { SpokenPunctuation = v }),
                    hint: "“virgule”, “point d'interrogation”, “à la ligne”, "
                        + "“comma”, “new line” and friends become the marks themselves. "
                        + "French and English."),
            },
        });
    }

    private static RadioButton Option(Timing timing, Timing current, Action<Timing> choose, string label, string hint)
    {
        var button = new RadioButton
        {
            GroupName = Group,
            IsChecked = timing == current,
            Content = new StackPanel
            {
                Spacing = Tokens.Space.Tight,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        FontFamily = Tokens.Fonts.Grotesque,
                        FontSize = Tokens.Fonts.Body,
                        Foreground = Tokens.Brushes.Ink,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    Panels.Note(hint),
                },
            },
        };

        button.IsCheckedChanged += (_, _) =>
        {
            if (button.IsChecked == true) choose(timing);
        };
        return button;
    }
}
