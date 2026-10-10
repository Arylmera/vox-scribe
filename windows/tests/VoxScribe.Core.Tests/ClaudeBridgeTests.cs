using System.Text.Json;
using VoxScribe.Core;
using VoxScribe.Testing;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The app side of the Claude Code band: which session is armed, handing it a dictation, and
/// knowing whether it arrived. The module is a stand-in written in each test.
/// </summary>
public sealed class ClaudeBridgeTests : IDisposable
{
    private const string Session = "3f2c9a10-1b2c-4d5e-8f90-a1b2c3d4e5f6";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vox-claude-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ClaudeBridge Build(TimeSpan? ackTimeout = null)
    {
        var bridge = new ClaudeBridge(_dir, _clock, ackTimeout ?? TimeSpan.FromSeconds(2));
        Directory.CreateDirectory(Path.Combine(_dir, "outbox"));
        return bridge;
    }

    private void Arm(string session, TimeSpan age) =>
        File.WriteAllText(Path.Combine(_dir, "target.json"), JsonSerializer.Serialize(new
        {
            session_id = session,
            beat = (_clock.Now - age).ToUnixTimeMilliseconds(),
        }));

    /// <summary>Plays the module: waits for this session's outbox, then acknowledges it.</summary>
    private Task<string> AcknowledgeAsync(string session) => Task.Run(async () =>
    {
        var outbox = Path.Combine(_dir, "outbox", session + ".json");
        for (var i = 0; i < 400 && !File.Exists(outbox); i++) await Task.Delay(10);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(outbox));
        var id = json.RootElement.GetProperty("id").GetString()!;
        await File.WriteAllTextAsync(Path.Combine(_dir, "outbox", id + ".ack"), "");
        return json.RootElement.GetProperty("text").GetString()!;
    });

    [Fact]
    public void Nothing_armed_means_no_target()
    {
        using var bridge = Build();
        bridge.LiveTarget().ShouldBeNull();
    }

    [Fact]
    public void A_fresh_beat_is_a_live_target()
    {
        using var bridge = Build();
        Arm(Session, TimeSpan.FromSeconds(5.9));
        bridge.LiveTarget().ShouldBe(Session);
    }

    /// <summary>A session that crashed while armed must not swallow the next dictation.</summary>
    [Fact]
    public void A_stale_beat_is_no_target()
    {
        using var bridge = Build();
        Arm(Session, TimeSpan.FromSeconds(6.1));
        bridge.LiveTarget().ShouldBeNull();
    }

    /// <summary>The session id becomes a file name: anything but a plain id is refused.</summary>
    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("a/b")]
    [InlineData("")]
    public void A_session_id_that_is_not_a_plain_id_is_no_target(string session)
    {
        using var bridge = Build();
        Arm(session, TimeSpan.Zero);
        bridge.LiveTarget().ShouldBeNull();
    }

    [Fact]
    public void A_corrupt_target_file_is_no_target()
    {
        using var bridge = Build();
        File.WriteAllText(Path.Combine(_dir, "target.json"), "{ not json");
        bridge.LiveTarget().ShouldBeNull();
    }

    [Fact]
    public async Task No_target_writes_nothing()
    {
        using var bridge = Build();

        (await bridge.TryDeliverAsync("bonjour", CancellationToken.None)).ShouldBe(ClaudeDelivery.NoTarget);

        Directory.GetFiles(Path.Combine(_dir, "outbox")).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_acknowledged_dictation_is_delivered_and_cleaned_up()
    {
        using var bridge = Build();
        Arm(Session, TimeSpan.Zero);
        var module = AcknowledgeAsync(Session);

        var result = await bridge.TryDeliverAsync("lance les tests", CancellationToken.None);

        result.ShouldBe(ClaudeDelivery.Delivered);
        (await module).ShouldBe("lance les tests");
        Directory.GetFiles(Path.Combine(_dir, "outbox")).ShouldBeEmpty("outbox and ack are removed once delivered");
    }

    /// <summary>Without an ack the outbox is withdrawn, so a late module cannot submit it.</summary>
    [Fact]
    public async Task No_ack_is_not_acknowledged_and_the_outbox_is_withdrawn()
    {
        using var bridge = Build(TimeSpan.FromMilliseconds(200));
        Arm(Session, TimeSpan.Zero);

        var result = await bridge.TryDeliverAsync("bonjour", CancellationToken.None);

        result.ShouldBe(ClaudeDelivery.NotAcknowledged);
        File.Exists(Path.Combine(_dir, "outbox", Session + ".json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Status_names_the_target_only_for_a_command_in_progress()
    {
        using var bridge = Build();
        Arm(Session, TimeSpan.Zero);

        bridge.PublishStatus(DictationState.Recording, command: true);
        (await StatusAsync("listening")).ShouldBe(Session);

        bridge.PublishStatus(DictationState.Transcribing, command: true);
        (await StatusAsync("transcribing")).ShouldBe(Session);

        bridge.PublishStatus(DictationState.Recording, command: false);
        (await StatusAsync("listening")).ShouldBeNull();

        bridge.PublishStatus(DictationState.Idle, command: false);
        (await StatusAsync("idle")).ShouldBeNull();
    }

    /// <summary>Waits for status.json to reach <paramref name="state"/>; returns its session.</summary>
    private async Task<string?> StatusAsync(string state)
    {
        for (var i = 0; i < 200; i++)
        {
            try
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_dir, "status.json")));
                var root = json.RootElement;
                if (root.GetProperty("state").GetString() == state)
                {
                    root.GetProperty("beat").GetInt64().ShouldBe(_clock.Now.ToUnixTimeMilliseconds());
                    var session = root.GetProperty("session_id");
                    return session.ValueKind == JsonValueKind.Null ? null : session.GetString();
                }
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                // Mid-replace; try again.
            }

            await Task.Delay(10);
        }

        throw new ShouldAssertException($"status.json never reached \"{state}\"");
    }

    [Theory]
    [InlineData("start", MicAction.Start)]
    [InlineData("stop", MicAction.Stop)]
    [InlineData("cancel", MicAction.Cancel)]
    public void A_mic_request_is_read(string action, MicAction expected)
    {
        var now = _clock.Now.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize(new { session_id = Session, action, ts = now - 500 });

        ClaudeBridge.ParseMic(json, now).ShouldBe((Session, expected, now - 500));
    }

    /// <summary>A request left on disk by an earlier run must not start a recording.</summary>
    [Fact]
    public void An_old_mic_request_is_ignored()
    {
        var now = _clock.Now.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize(new { session_id = Session, action = "start", ts = now - 10_001 });

        ClaudeBridge.ParseMic(json, now).ShouldBeNull();
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{\"session_id\":\"a/b\",\"action\":\"start\",\"ts\":0}")]
    [InlineData("{\"session_id\":\"abc\",\"action\":\"explode\",\"ts\":0}")]
    public void A_malformed_mic_request_is_ignored(string json) =>
        ClaudeBridge.ParseMic(json, 0).ShouldBeNull();

    [Fact]
    public async Task A_mic_request_written_after_start_is_raised_once()
    {
        using var bridge = new ClaudeBridge(_dir);
        var seen = new List<MicAction>();
        bridge.Mic += (_, a) => { lock (seen) seen.Add(a); };
        bridge.Start();

        var json = JsonSerializer.Serialize(new { session_id = Session, action = "start", ts = DateTimeOffset.Now.ToUnixTimeMilliseconds() });
        File.WriteAllText(Path.Combine(_dir, "mic.json"), json);
        File.WriteAllText(Path.Combine(_dir, "mic.json"), json); // a second write of the same request

        for (var i = 0; i < 300 && seen.Count == 0; i++) await Task.Delay(10);
        await Task.Delay(300);
        lock (seen) seen.ShouldBe([MicAction.Start]);
    }
}
