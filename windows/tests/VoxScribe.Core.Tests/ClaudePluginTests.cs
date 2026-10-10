using VoxScribe.Core;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>The Settings button drives the claude CLI; these pin what it runs and how it reads the answer.</summary>
public sealed class ClaudePluginTests
{
    private sealed class FakeCli
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, (int, string)> Answers { get; } = [];

        public Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> args, CancellationToken _)
        {
            var line = string.Join(' ', args);
            Calls.Add(line);
            return Task.FromResult(Answers.TryGetValue(line, out var a) ? a : (0, ""));
        }
    }

    [Fact]
    public void Installed_is_read_from_the_plugin_list()
    {
        ClaudePlugin.IsInstalled("""[{"id":"caveman@caveman"},{"id":"voxscribe@vox-scribe","enabled":true}]""").ShouldBe(true);
        ClaudePlugin.IsInstalled("""[{"id":"caveman@caveman"}]""").ShouldBe(false);
        ClaudePlugin.IsInstalled("not json").ShouldBeNull();
    }

    [Fact]
    public async Task Install_adds_the_marketplace_then_installs()
    {
        var cli = new FakeCli();

        (await ClaudePlugin.InstallAsync(cli.RunAsync, CancellationToken.None)).ShouldBeNull();

        cli.Calls.ShouldBe([
            "plugin marketplace add Arylmera/vox-scribe",
            "plugin install voxscribe@vox-scribe --scope user",
        ]);
    }

    /// <summary>A marketplace already added makes "add" fail; the install must still run.</summary>
    [Fact]
    public async Task Install_carries_on_when_the_marketplace_exists()
    {
        var cli = new FakeCli();
        cli.Answers["plugin marketplace add Arylmera/vox-scribe"] = (1, "already exists");

        (await ClaudePlugin.InstallAsync(cli.RunAsync, CancellationToken.None)).ShouldBeNull();
        cli.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_failed_install_returns_the_cli_message()
    {
        var cli = new FakeCli();
        cli.Answers["plugin install voxscribe@vox-scribe --scope user"] = (1, "network down");

        (await ClaudePlugin.InstallAsync(cli.RunAsync, CancellationToken.None)).ShouldBe("network down");
    }

    [Fact]
    public async Task Uninstall_removes_only_the_plugin()
    {
        var cli = new FakeCli();

        (await ClaudePlugin.UninstallAsync(cli.RunAsync, CancellationToken.None)).ShouldBeNull();
        cli.Calls.ShouldBe(["plugin uninstall voxscribe@vox-scribe"]);
    }

    [Fact]
    public async Task A_missing_cli_is_unknown_state()
    {
        var cli = new FakeCli();
        cli.Answers["plugin list --json"] = (ClaudePlugin.NotFound, "claude not found");

        (await ClaudePlugin.CheckAsync(cli.RunAsync, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public void The_terminal_line_is_the_exact_env_entry() =>
        ClaudePlugin.TerminalFlagLine.ShouldBe("\"CLAUDE_CODE_ENABLE_FUNCTION_HOOKS\": \"1\"");
}
