using Avalonia.Controls;
using Avalonia.Layout;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Settings;

/// <summary>The voxscribe plugin for Claude Code, and where command dictations go without it.</summary>
internal static class ClaudeSection
{
    /// <summary>Builds the section.</summary>
    /// <param name="settings">User preferences.</param>
    /// <param name="save">Persists a change.</param>
    /// <param name="run">The claude CLI; <see cref="ClaudePlugin.RunAsync"/> in the app, a fake in tests.</param>
    public static Control Build(AppSettings settings, Action<SettingsData> save, ClaudePlugin.Runner run)
    {
        var commandTitle = Panels.Field("Claude",
            settings.Data.CommandWindowTitle,
            v => save(settings.Data with { CommandWindowTitle = v ?? "Claude" }));

        return Panels.Section("CLAUDE CODE", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                PluginBlock(run),
                Panels.Note("The plugin adds /parle and /say, and a band above Claude Code's prompt: "
                    + "arm a session as the command shortcut's target, talk to it with a click, "
                    + "or have its last reply read aloud. It applies to new sessions, or after /reload-plugins."),
                TerminalBlock(),
                Panels.Labelled("COMMAND WINDOW TITLE CONTAINS", commandTitle),
                Panels.Note("When no session is armed, the command shortcut types into the first "
                    + "visible window whose title contains this text. \"Claude\" matches the desktop "
                    + "app and a terminal tab running Claude Code. If no window matches, nothing is "
                    + "typed anywhere and the pill says so."),
            },
        });
    }

    /// <summary>The plugin's state and the one button that flips it.</summary>
    private static StackPanel PluginBlock(ClaudePlugin.Runner run)
    {
        var status = Panels.Note("Checking…");
        status.VerticalAlignment = VerticalAlignment.Center;
        var button = new TransportKey { IsEnabled = false };
        bool? installed = null;

        async Task RefreshAsync()
        {
            installed = await ClaudePlugin.CheckAsync(run, CancellationToken.None);
            status.Text = installed switch
            {
                true => "Installed for Claude Code.",
                false => "Not installed.",
                null => "The claude command was not found, so the plugin cannot be managed from here.",
            };
            button.Content = installed == true ? "UNINSTALL" : "INSTALL";
            button.IsEnabled = installed is not null;
        }

        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            status.Text = installed == true ? "Uninstalling…" : "Installing…";
            var error = installed == true
                ? await ClaudePlugin.UninstallAsync(run, CancellationToken.None)
                : await ClaudePlugin.InstallAsync(run, CancellationToken.None);
            await RefreshAsync();
            if (error is not null) status.Text = $"{status.Text} {error}";
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children = { button, status },
        };
        // Read whenever the tab is shown — the plugin may be changed from a terminal — and never
        // at build: SettingsPage builds every tab, and the UI tests build SettingsPage.
        row.AttachedToVisualTree += (_, _) => _ = RefreshAsync();
        return Panels.Labelled("PLUGIN", row);
    }

    /// <summary>The terminal flag, explained and copied — never written by Vox-Scribe.</summary>
    private static StackPanel TerminalBlock()
    {
        var copy = new TransportKey { Content = "COPY THE LINE" };
        var line = Panels.Note(ClaudePlugin.TerminalFlagLine);
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(ClaudePlugin.TerminalFlagLine);
        };

        return Panels.Labelled("BAND IN A TERMINAL", new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Note("The desktop app draws the band as is. A terminal draws it only with this "
                    + "line in the \"env\" block of ~/.claude/settings.json, then a restart of Claude Code. "
                    + "It turns on module code for every plugin, not just this one, so Vox-Scribe leaves "
                    + "the file to you."),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = Tokens.Space.Snug,
                    Children = { copy, line },
                },
            },
        });
    }
}
