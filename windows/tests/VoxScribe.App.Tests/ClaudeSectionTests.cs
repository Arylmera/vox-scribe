using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Shouldly;
using VoxScribe.App.Controls;
using VoxScribe.App.Views.Settings;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

public sealed class ClaudeSectionTests
{
    private static AppSettings Settings() =>
        new(Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json"));

    private static ClaudePlugin.Runner Cli(List<string> calls, bool installed) => (args, _) =>
    {
        var line = string.Join(' ', args);
        calls.Add(line);
        return Task.FromResult(line == "plugin list --json"
            ? (0, installed ? """[{"id":"voxscribe@vox-scribe"}]""" : "[]")
            : (0, ""));
    };

    private static List<string?> Texts(Control section) =>
        [.. section.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)];

    [AvaloniaFact]
    public void The_section_holds_the_command_window_title_and_the_terminal_line()
    {
        var section = ClaudeSection.Build(Settings(), _ => { }, Cli([], installed: false));

        Texts(section).ShouldContain("COMMAND WINDOW TITLE CONTAINS");
        Texts(section).ShouldContain(ClaudePlugin.TerminalFlagLine);
    }

    [AvaloniaFact]
    public async Task An_installed_plugin_offers_uninstall_and_runs_it()
    {
        var calls = new List<string>();
        var section = ClaudeSection.Build(Settings(), _ => { }, Cli(calls, installed: true));
        new Window { Content = section }.Show(); // attaching is what reads the plugin's state
        var button = section.GetLogicalDescendants().OfType<TransportKey>()
            .Single(b => b.Content as string is "INSTALL" or "UNINSTALL" || b.Content is null);
        for (var i = 0; i < 100 && button.Content is null; i++) await Task.Delay(10);

        button.Content.ShouldBe("UNINSTALL");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 100 && !calls.Contains("plugin uninstall voxscribe@vox-scribe"); i++) await Task.Delay(10);

        calls.ShouldContain("plugin uninstall voxscribe@vox-scribe");
    }

    [AvaloniaFact]
    public async Task A_throwing_cli_is_reported_instead_of_crashing_the_app()
    {
        ClaudePlugin.Runner cli = (_, _) => throw new InvalidOperationException("boom");
        var section = ClaudeSection.Build(Settings(), _ => { }, cli);
        new Window { Content = section }.Show(); // attaching triggers the refresh that throws

        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < deadline && !Texts(section).Any(t => t?.Contains("boom") == true))
            await Task.Delay(10);

        Texts(section).ShouldContain(t => t != null && t.Contains("boom"));
    }
}
