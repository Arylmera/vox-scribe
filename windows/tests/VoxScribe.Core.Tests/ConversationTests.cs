using System.Text.Json;
using VoxScribe.Abstractions;
using VoxScribe.Core;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>Conversation mode: which turns are read, and what goes on screen instead of being said.</summary>
public sealed class ConversationTests : IDisposable
{
    private const string Session = "session-a";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vox-turn-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class SilentPlayer : IAudioPlayer
    {
        public Task PlayAsync(byte[] wav, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static string Line(object value) => JsonSerializer.Serialize(value);

    private static string User(string text) => Line(new { type = "user", message = new { content = text } });

    private static string ToolResult() =>
        Line(new { type = "user", message = new { content = new object[] { new { type = "tool_result", content = "ok" } } } });

    private static string Tool(string name, object input, bool sidechain = false) => Line(new
    {
        type = "assistant",
        isSidechain = sidechain,
        message = new { content = new object[] { new { type = "tool_use", name, input } } },
    });

    [Fact]
    public void Fenced_code_is_lifted_out_with_its_language()
    {
        var blocks = TurnPanel.CodeBlocks("Voici :\n```csharp\nvar a = 1;\n  var b = 2;\n```\nEt ceci :\n```\nls\n```").ToList();

        blocks.ShouldBe([new PanelBlock("csharp", "var a = 1;\n  var b = 2;", false), new PanelBlock("code", "ls", false)]);
    }

    [Fact]
    public void A_reply_without_a_fence_shows_nothing() =>
        TurnPanel.CodeBlocks("Rien à voir, juste `inline`.").ShouldBeEmpty();

    [Fact]
    public void An_unclosed_fence_runs_to_the_end() =>
        TurnPanel.CodeBlocks("```py\nprint(1)").Single().Body.ShouldBe("print(1)");

    [Fact]
    public void Only_the_edits_since_the_user_last_spoke_are_shown()
    {
        var blocks = TurnPanel.Edits([
            User("first"),
            Tool("Edit", new { file_path = "C:\\repo\\old.cs", old_string = "a", new_string = "b" }),
            User("second"),
            Tool("Edit", new { file_path = "C:\\repo\\new.cs", old_string = "x\ny", new_string = "z" }),
            ToolResult(),
            Tool("Write", new { file_path = "/repo/made.md", content = "hello\n" }),
        ]);

        blocks.ShouldBe([
            new PanelBlock("new.cs", "- x\n- y\n+ z", true),
            new PanelBlock("made.md", "+ hello", true),
        ]);
    }

    [Fact]
    public void A_subagent_and_other_tools_are_not_the_turn_edits()
    {
        TurnPanel.Edits([
            User("go"),
            Tool("Edit", new { file_path = "a.cs", old_string = "a", new_string = "b" }, sidechain: true),
            Tool("Read", new { file_path = "b.cs" }),
            "not json",
        ]).ShouldBeEmpty();
    }

    [Fact]
    public void MultiEdit_lists_each_change()
    {
        var input = new { file_path = "a.cs", edits = new[] { new { old_string = "1", new_string = "2" }, new { old_string = "3", new_string = "4" } } };

        TurnPanel.Edits([Tool("MultiEdit", input)]).Single().Body.ShouldBe("- 1\n+ 2\n- 3\n+ 4");
    }

    [Fact]
    public void A_huge_block_is_cut_and_says_so()
    {
        var body = string.Join('\n', Enumerable.Range(0, 450));

        var shown = TurnPanel.CodeBlocks($"```\n{body}\n```").Single().Body.Split('\n');

        shown.Length.ShouldBe(401);
        shown[^1].ShouldBe("… 50 more lines");
    }

    [Fact]
    public void The_status_says_whether_conversation_is_on()
    {
        Directory.CreateDirectory(_dir);
        using var bridge = new ClaudeBridge(_dir) { Conversation = () => true };
        bridge.Start();

        WaitFor(() => File.Exists(Path.Combine(_dir, "status.json")));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "status.json")));
        json.RootElement.GetProperty("conversation").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void The_band_can_flip_conversation()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var json = Line(new { session_id = Session, action = "conversation", ts = now });

        ClaudeBridge.ParseMic(json, now).ShouldBe((Session, MicAction.Conversation, now));
    }

    private (ReadAloud Reader, List<IReadOnlyList<PanelBlock>> Shown, List<string> Failures) Reader(Func<string, bool>? accept, bool enabled = true)
    {
        // No endpoint: a reading that got through stops at once and says so, which is the proof.
        var reader = new ReadAloud(() => new SettingsData { ReadAloud = enabled }, new SilentPlayer()) { AcceptTurn = accept };
        var shown = new List<IReadOnlyList<PanelBlock>>();
        var failures = new List<string>();
        reader.Panel += (_, blocks) => { lock (shown) shown.Add(blocks); };
        reader.Failed += (_, message) => { lock (failures) failures.Add(message); };
        reader.Watch(_dir);
        return (reader, shown, failures);
    }

    private void Turn(object request) => File.WriteAllText(Path.Combine(_dir, "request.json"), Line(request));

    private static void WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 300 && !done(); i++) Thread.Sleep(10);
        done().ShouldBeTrue();
    }

    [Fact]
    public void An_accepted_turn_shows_its_code_and_is_read_from_its_own_text()
    {
        string? asked = null;
        var (reader, shown, failures) = Reader(s => { asked = s; return true; });
        using (reader)
        {
            Turn(new { command = "turn", text = "Voilà.\n```js\nx()\n```", session_id = Session, ts = 1 });

            WaitFor(() => { lock (failures) return failures.Count > 0; });
        }

        asked.ShouldBe(Session);
        shown.Single().ShouldBe([new PanelBlock("js", "x()", false)]);
        failures.Single().ShouldContain("no gateway endpoint");
    }

    [Theory]
    [InlineData(false, true)] // another session, or conversation off
    [InlineData(true, false)] // read-aloud turned off altogether
    public void A_refused_turn_is_neither_read_nor_shown(bool accepted, bool enabled)
    {
        var (reader, shown, failures) = Reader(_ => accepted, enabled);
        using (reader)
        {
            Turn(new { command = "turn", text = "```\ncode\n```", session_id = Session, ts = 1 });
            Thread.Sleep(500);
        }

        shown.ShouldBeEmpty();
        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Without_the_app_rule_no_turn_is_read()
    {
        var (reader, shown, failures) = Reader(null);
        using (reader)
        {
            Turn(new { command = "turn", text = "Bonjour.", session_id = Session, ts = 1 });
            Thread.Sleep(500);
        }

        shown.ShouldBeEmpty();
        failures.ShouldBeEmpty();
    }

    [Fact]
    public void A_request_caught_half_written_is_read_once_whole()
    {
        var (reader, shown, failures) = Reader(_ => true);
        using (reader)
        {
            var path = Path.Combine(_dir, "request.json");
            File.WriteAllText(path, "{\"command\":\"turn\",");
            Thread.Sleep(200); // past the debounce: the first read sees half a file

            var whole = Line(new { command = "turn", text = "Bonjour.", session_id = Session, ts = 1 });
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream)) writer.Write(whole);

            WaitFor(() => { lock (failures) return failures.Count > 0; });
        }

        shown.ShouldNotBeEmpty();
        failures.ShouldHaveSingleItem().ShouldContain("no gateway endpoint");
    }

    [Fact]
    public void Any_shortcut_clears_the_panel()
    {
        var (reader, shown, _) = Reader(_ => true);
        using (reader)
        {
            reader.Stop();

            WaitFor(() => { lock (shown) return shown.Count == 1; });
        }

        shown.Single().ShouldBeEmpty();
    }
}
