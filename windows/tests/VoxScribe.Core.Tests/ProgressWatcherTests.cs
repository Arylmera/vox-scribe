using System.Text.Json;
using VoxScribe.Core;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>Progress notes are spoken; the final answer, subagents and old text are not.</summary>
public sealed class ProgressWatcherTests : IDisposable
{
    private const string Session = "session-a";
    private readonly string _projects = Path.Combine(Path.GetTempPath(), "vox-progress-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;

    public ProgressWatcherTests()
    {
        Directory.CreateDirectory(Path.Combine(_projects, "C--repo"));
        _file = Path.Combine(_projects, "C--repo", Session + ".jsonl");
    }

    public void Dispose() => Directory.Delete(_projects, recursive: true);

    private static string Text(string said, bool sidechain = false) => JsonSerializer.Serialize(new
    {
        type = "assistant",
        isSidechain = sidechain,
        message = new { content = new object[] { new { type = "text", text = said } } },
    });

    private static string Tool(bool sidechain = false) => JsonSerializer.Serialize(new
    {
        type = "assistant",
        isSidechain = sidechain,
        message = new { content = new object[] { new { type = "tool_use", name = "Bash" } } },
    });

    private static string User(string said) => JsonSerializer.Serialize(new { type = "user", message = new { content = said } });

    private static string Result() => JsonSerializer.Serialize(new
    {
        type = "user",
        message = new { content = new object[] { new { type = "tool_result", content = "ok" } } },
    });

    private static List<string> Notes(params string[] lines)
    {
        var filter = new ProgressFilter();
        return [.. lines.Select(filter.Feed).OfType<string>()];
    }

    [Fact]
    public void Text_before_a_tool_is_a_note_and_the_last_text_is_not() =>
        Notes(User("go"), Text("Je lance les tests."), Tool(), Result(), Text("Tout est vert."))
            .ShouldBe(["Je lance les tests."]);

    [Fact]
    public void Several_tools_after_one_note_say_it_once() =>
        Notes(Text("Je regarde."), Tool(), Result(), Tool(), Result()).ShouldBe(["Je regarde."]);

    [Fact]
    public void A_final_answer_never_leaks_into_the_next_turn() =>
        Notes(Text("Réponse finale."), User("merci"), Tool()).ShouldBeEmpty();

    [Fact]
    public void A_subagent_says_nothing() =>
        Notes(Text("Sous-agent.", sidechain: true), Tool(sidechain: true)).ShouldBeEmpty();

    [Fact]
    public void Junk_is_skipped() => Notes("not json", "{").ShouldBeEmpty();

    [Fact]
    public void It_starts_at_the_end_and_waits_for_a_whole_line()
    {
        File.WriteAllText(_file, Text("Ancien.") + "\n" + Tool() + "\n");
        var heard = new List<(string, string)>();
        using var watcher = new ProgressWatcher(() => Session, (s, n) => heard.Add((s, n)), _projects, timer: false);

        watcher.Tick();
        File.AppendAllText(_file, Text("Nouveau.") + "\n");
        var tool = Tool();
        File.AppendAllText(_file, tool[..10]);
        watcher.Tick();
        heard.ShouldBeEmpty();

        File.AppendAllText(_file, tool[10..] + "\n");
        watcher.Tick();
        heard.ShouldBe([(Session, "Nouveau.")]);
    }

    [Fact]
    public void Nothing_armed_reads_nothing()
    {
        File.WriteAllText(_file, "");
        var heard = new List<string>();
        using var watcher = new ProgressWatcher(() => null, (_, n) => heard.Add(n), _projects, timer: false);

        watcher.Tick();
        File.AppendAllText(_file, Text("Note.") + "\n" + Tool() + "\n");
        watcher.Tick();

        heard.ShouldBeEmpty();
    }
}
