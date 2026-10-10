using Avalonia.Controls;
using Avalonia.Layout;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>History, start-up, and the installed version with its updates.</summary>
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
                UpdatesBlock(settings, save),
            },
        });

    /// <summary>The installed version, CHECK FOR UPDATES, and INSTALL when one is on offer.</summary>
    private static StackPanel UpdatesBlock(AppSettings settings, Action<SettingsData> save)
    {
        var status = Panels.Note("");
        status.VerticalAlignment = VerticalAlignment.Center;
        var check = new TransportKey { Content = "CHECK FOR UPDATES" };
        var install = new TransportKey();
        var busy = false;

        void Show()
        {
            if (busy) return;
            var installed = Updates.Installed.ToString(3);
            status.Text = Updates.Available is { } update
                ? $"Version {installed}. Version {update.Version.ToString(3)} is available."
                : $"Version {installed}.";
            install.IsVisible = Updates.Available is not null;
            install.Content = Updates.Available is { } u ? $"INSTALL {u.Version.ToString(3)}" : null;
        }

        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            status.Text = "Checking…";
            try
            {
                var found = await Updates.CheckAsync();
                Show();
                if (found is null) status.Text = $"Version {Updates.Installed.ToString(3)}, up to date.";
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            {
                status.Text = $"Could not reach GitHub: {e.Message}";
            }

            check.IsEnabled = true;
        };

        install.Click += async (_, _) =>
        {
            if (Updates.Available is not { } update) return;
            busy = true;
            check.IsEnabled = install.IsEnabled = false;
            try
            {
                // Progress<T> captures the UI context here, so Report lands on the UI thread.
                var report = new Progress<double>(f => status.Text = $"Downloading {update.Version.ToString(3)}… {f * 100:F0}%");
                await Updates.InstallAsync(update, report);
                status.Text = "Installing — Vox-Scribe restarts in a moment.";
            }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException
                or System.ComponentModel.Win32Exception)
            {
                busy = false;
                status.Text = $"Update failed: {e.Message}";
                check.IsEnabled = install.IsEnabled = true;
            }
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children = { check, install, status },
        };

        // Subscribed only while on screen, like the page views: a rebuilt section must not
        // keep the discarded one alive.
        void OnChanged(object? sender, EventArgs e) => Show();
        row.AttachedToVisualTree += (_, _) => { Updates.Changed += OnChanged; Show(); };
        row.DetachedFromVisualTree += (_, _) => Updates.Changed -= OnChanged;
        Show();

        return Panels.Labelled("UPDATES", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                row,
                Panels.Toggle("Check for updates when Vox-Scribe starts", settings.Data.CheckUpdatesAtStartup,
                    v => save(settings.Data with { CheckUpdatesAtStartup = v }),
                    hint: "Only offers: nothing is downloaded or installed until you press INSTALL. "
                        + "The installer is verified against the SHA-256 published with the release."),
            },
        });
    }
}
