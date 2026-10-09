using Avalonia.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>Which other apps lose the microphone while you dictate — Discord, Teams, a game.</summary>
internal static class VoiceChatSection
{
    /// <summary>Builds the section.</summary>
    /// <param name="settings">Read for the saved list.</param>
    /// <param name="save">Persists a toggle.</param>
    /// <param name="detected">Names of other apps holding a microphone right now.</param>
    public static Control Build(AppSettings settings, Action<SettingsData> save, Func<IEnumerable<string>> detected)
    {
        var rows = new StackPanel { Spacing = Tokens.Space.Snug };

        void Fill()
        {
            rows.Children.Clear();

            // Saved names stay listed while the app is closed, so a toggle never vanishes
            // just because Discord is not running at the moment.
            var names = settings.Data.MuteAppsWhileDictating
                .Concat(detected())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
                rows.Children.Add(Panels.Note("No other app is using a microphone right now. Open it — or join a call — then refresh."));

            foreach (var name in names)
            {
                var on = settings.Data.MuteAppsWhileDictating.Contains(name, StringComparer.OrdinalIgnoreCase);
                rows.Children.Add(Panels.Toggle(name, on, v => save(settings.Data with
                {
                    MuteAppsWhileDictating = v
                        ? [.. settings.Data.MuteAppsWhileDictating.Append(name).Distinct(StringComparer.OrdinalIgnoreCase)]
                        : [.. settings.Data.MuteAppsWhileDictating.Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase))],
                })));
            }
        }

        var refresh = Panels.DeckButton("REFRESH");
        Avalonia.Automation.AutomationProperties.SetName(refresh, "Refresh the list of apps using a microphone");
        refresh.Click += (_, _) => Fill();

        Fill();

        return Panels.Section("VOICE CHAT", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Note("Ticked apps have their microphone muted while you hold a dictation shortcut, "
                    + "so Discord and the like do not hear you dictating. Released, they hear you again."),
                rows,
                refresh,
            },
        });
    }
}
