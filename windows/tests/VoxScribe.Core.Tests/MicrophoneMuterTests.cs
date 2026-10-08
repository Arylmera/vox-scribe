using VoxScribe.Abstractions;
using VoxScribe.Core;
using Shouldly;
using Xunit;

namespace VoxScribe.Core.Tests;

public sealed class MicrophoneMuterTests : IDisposable
{
    private const int Self = 1;
    private readonly string _ledger = Path.Combine(Path.GetTempPath(), $"vox-muted-{Guid.NewGuid():N}.txt");
    private readonly FakeSessions _mixer = new();

    public void Dispose()
    {
        if (File.Exists(_ledger)) File.Delete(_ledger);
    }

    private MicrophoneMuter Muter(params string[] apps) =>
        new(_mixer, () => apps, _ledger, Self);

    [Fact]
    public async Task Mutes_listed_apps_while_recording_and_restores_after()
    {
        _mixer.Add(10, "Discord");
        _mixer.Add(20, "Teams");
        var muter = Muter("discord");

        muter.OnStateChanged(DictationState.Recording);
        await muter.Settled;
        _mixer.IsMuted(10).ShouldBeTrue("listed, matched case-insensitively");
        _mixer.IsMuted(20).ShouldBeFalse("not listed");

        muter.OnStateChanged(DictationState.Transcribing);
        await muter.Settled;
        _mixer.IsMuted(10).ShouldBeFalse("the key is up, Discord hears again");
        File.Exists(_ledger).ShouldBeFalse();
    }

    [Fact]
    public async Task Never_mutes_itself_and_never_unmutes_what_the_user_muted()
    {
        _mixer.Add(Self, "Discord");          // same name as a target — the pid decides
        _mixer.Add(10, "Discord", muted: true);
        var muter = Muter("Discord");

        muter.OnStateChanged(DictationState.Recording);
        await muter.Settled;
        _mixer.IsMuted(Self).ShouldBeFalse("muting our own stream would silence the dictation");

        muter.OnStateChanged(DictationState.Idle);
        await muter.Settled;
        _mixer.IsMuted(10).ShouldBeTrue("it was muted before we came; it stays muted");
    }

    [Fact]
    public async Task Level_updates_inside_a_recording_do_not_remute_or_restore()
    {
        _mixer.Add(10, "Discord");
        var muter = Muter("Discord");

        muter.OnStateChanged(DictationState.Recording);
        muter.OnStateChanged(DictationState.Recording);
        await muter.Settled;

        _mixer.Calls.ShouldBe(["10:True"]);
    }

    [Fact]
    public async Task A_fast_tap_finishes_unmuted_even_when_the_mute_is_slow()
    {
        _mixer.Add(10, "Discord");
        _mixer.SlowMute = true;
        var muter = Muter("Discord");

        muter.OnStateChanged(DictationState.Recording);
        muter.OnStateChanged(DictationState.Idle);
        await muter.Settled;

        _mixer.Calls.ShouldBe(["10:True", "10:False"]);
        _mixer.IsMuted(10).ShouldBeFalse();
    }

    [Fact]
    public async Task One_app_that_refuses_does_not_keep_the_others_muted()
    {
        _mixer.Add(10, "Discord");
        _mixer.Add(20, "Discord");
        var muter = Muter("Discord");

        muter.OnStateChanged(DictationState.Recording);
        await muter.Settled;
        _mixer.Refuse = 10;
        muter.OnStateChanged(DictationState.Idle);
        await muter.Settled;

        _mixer.IsMuted(20).ShouldBeFalse();
    }

    [Fact]
    public async Task A_crash_while_muted_is_repaired_at_the_next_start()
    {
        _mixer.Add(10, "Discord");
        var crashed = Muter("Discord");
        crashed.OnStateChanged(DictationState.Recording);
        await crashed.Settled;                  // ...and the process dies here

        _mixer.Respawn(10, 11);                 // Discord restarted; Windows kept its mute
        Muter("Discord").RecoverFromCrash();

        _mixer.IsMuted(11).ShouldBeFalse();
        File.Exists(_ledger).ShouldBeFalse();
    }

    [Fact]
    public async Task No_apps_listed_means_nothing_is_touched()
    {
        _mixer.Add(10, "Discord");
        var muter = Muter();

        muter.OnStateChanged(DictationState.Recording);
        await muter.Settled;

        _mixer.Calls.ShouldBeEmpty();
    }

    private sealed class FakeSessions : ICaptureSessions
    {
        private readonly List<CaptureSession> _sessions = [];
        public List<string> Calls { get; } = [];
        public bool SlowMute { get; set; }
        public int? Refuse { get; set; }

        public void Add(int pid, string name, bool muted = false) => _sessions.Add(new(pid, name, muted));

        public void Respawn(int oldPid, int newPid)
        {
            var i = _sessions.FindIndex(s => s.ProcessId == oldPid);
            _sessions[i] = _sessions[i] with { ProcessId = newPid };
        }

        public bool IsMuted(int pid) => _sessions.Where(s => s.ProcessId == pid).All(s => s.IsMuted);

        public IReadOnlyList<CaptureSession> List() { lock (_sessions) return [.. _sessions]; }

        public void SetMuted(int processId, bool muted)
        {
            if (Refuse == processId) throw new InvalidOperationException("process exited");
            if (muted && SlowMute) Thread.Sleep(100);
            lock (_sessions)
            {
                Calls.Add($"{processId}:{muted}");
                for (var i = 0; i < _sessions.Count; i++)
                    if (_sessions[i].ProcessId == processId) _sessions[i] = _sessions[i] with { IsMuted = muted };
            }
        }
    }
}
