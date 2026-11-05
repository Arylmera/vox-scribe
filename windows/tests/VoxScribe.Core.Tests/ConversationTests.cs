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

    [Fact]
    public void Fenced_code_is_lifted_out_with_its_language()
    {
        var blocks = TurnPanel.CodeBlocks("Voici :\n```csharp\nvar a = 1;\n  var b = 2;\n```\nEt ceci :\n```\nls\n```").ToList();

        blocks.ShouldBe([new PanelBlock("csharp", "var a = 1;\n  var b = 2;"), new PanelBlock("code", "ls")]);
    }

    [Fact]
    public void A_reply_without_a_fence_shows_nothing() =>
        TurnPanel.CodeBlocks("Rien à voir, juste `inline`.").ShouldBeEmpty();

    [Fact]
    public void An_unclosed_fence_runs_to_the_end() =>
        TurnPanel.CodeBlocks("```py\nprint(1)").Single().Body.ShouldBe("print(1)");

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
        shown.Single().ShouldBe([new PanelBlock("js", "x()")]);
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

    [Theory]
    [InlineData(ClaudeDelivery.NotAcknowledged, false, true)]
    [InlineData(ClaudeDelivery.NotAcknowledged, true, true)]
    [InlineData(ClaudeDelivery.NoTarget, true, true)]
    [InlineData(ClaudeDelivery.NoTarget, false, false)]
    [InlineData(ClaudeDelivery.Delivered, true, false)]
    public void Only_a_lost_dictation_sounds_the_alert(ClaudeDelivery delivery, bool conversation, bool lost) =>
        ReadAloud.IsLost(delivery, conversation).ShouldBe(lost);

    [Fact]
    public void The_alert_is_a_playable_clip_of_a_third_of_a_second()
    {
        var wav = WavTempo.FallingTone();

        System.Text.Encoding.ASCII.GetString(wav, 0, 4).ShouldBe("RIFF");
        ((wav.Length - 44) / 2.0 / 24000).ShouldBe(1.0 / 3, 0.01);
        wav.Skip(44).ShouldContain(b => b != 0);
    }

    [Fact]
    public async Task A_failing_sound_device_never_reaches_the_dictation()
    {
        using var reader = new ReadAloud(() => new SettingsData(), new BrokenPlayer());

        await reader.AlertAsync();
    }

    private sealed class BrokenPlayer : IAudioPlayer
    {
        public Task PlayAsync(byte[] wav, CancellationToken cancellationToken) => throw new IOException("no device");
    }

    [Fact]
    public void Progress_voice_is_on_unless_turned_off()
    {
        new SettingsData().ProgressVoiceEnabled.ShouldBeTrue();
        new SettingsData { ProgressVoice = false }.ProgressVoiceEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Quick_start_takes_the_first_sentence_and_keeps_the_rest()
    {
        SpeechText.SplitFirst("C'est fait. Le test passe. Rien d'autre.").ShouldBe(("C'est fait.", "Le test passe. Rien d'autre."));
        SpeechText.SplitFirst("Une seule phrase.").ShouldBe(("Une seule phrase.", ""));
        SpeechText.SplitFirst("").ShouldBe(("", ""));
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
