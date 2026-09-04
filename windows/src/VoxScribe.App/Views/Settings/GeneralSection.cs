using Avalonia.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>History and start-up.</summary>
internal static class GeneralSection
{
    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save) =>
        Panels.Section("GENERAL", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Toggle("Keep a transcript history", settings.Data.KeepHistory,
                    v => save(settings.Data with { KeepHistory = v })),
                Panels.Toggle("Start Vox-Scribe when I log in, minimised to the tray",
                    PlatformFactory.IsLaunchAtLoginEnabled(),
                    PlatformFactory.SetLaunchAtLogin),
            },
        });
}
