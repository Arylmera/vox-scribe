using Avalonia.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>The tidy-up model the second shortcut sends its transcript through.</summary>
internal static class CleanupSection
{
    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save)
    {
        var endpoint = Panels.Field("http://192.168.1.100:4000/v1  (empty = type it as transcribed)",
            settings.Data.CleanupEndpoint,
            v => save(settings.Data with { CleanupEndpoint = v }));
        var model = Panels.Field("Alias the gateway routes on",
            settings.Data.CleanupModel,
            v => save(settings.Data with { CleanupModel = v ?? "local-light" }));
        var apiKey = Panels.Field("API key (empty = unauthenticated)",
            settings.Data.CleanupApiKey,
            v => save(settings.Data with { CleanupApiKey = v }));
        apiKey.PasswordChar = '•';

        return Panels.Section("CLEANUP", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Note("Sends the finished transcript to a small language model to fix "
                   + "punctuation, capitalisation and filler words before it is typed. "
                   + "Costs one LAN round trip; an unreachable model types the raw text."),
                Panels.Labelled("ENDPOINT", endpoint),
                Panels.Labelled("MODEL", model),
                Panels.Labelled("API KEY", apiKey),
                ConnectionTester.Build(() => (
                    settings.Data.CleanupEndpoint,
                    settings.Data.CleanupModel,
                    settings.Data.CleanupApiKey)),
                Panels.Note("Overrides \"type each phrase as you speak it\": a tidied dictation is "
                   + "always typed once, at the end, because text already in the target "
                   + "window cannot be repaired. Takes effect the next time VoxScribe starts."),
            },
        });
    }
}
