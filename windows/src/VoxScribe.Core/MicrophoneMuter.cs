using VoxScribe.Abstractions;

namespace VoxScribe.Core;

/// <summary>
/// Mutes chosen apps' microphone streams while a dictation is recording, so Discord and the
/// like do not broadcast what the user is dictating.
/// </summary>
/// <remarks>
/// <para>
/// Feed it every engine state; it acts only on entering and leaving
/// <see cref="DictationState.Recording"/>. Leaving by release, Escape or an error all restore
/// the same way.
/// </para>
/// <para>
/// <b>Serialized on one chain.</b> State changes arrive on the keyboard-hook thread, which
/// Windows unhooks if it stalls, so the COM work runs on the thread pool — but in order: two
/// independent tasks on a quick tap can finish unmute-then-mute and leave Discord deaf.
/// </para>
/// <para>
/// <b>Only puts back what it took.</b> A stream that was already muted is never touched, and
/// a ledger file names the apps muted right now, so a crash mid-dictation is repaired at the
/// next start instead of leaving Discord muted — Windows remembers per-app mute.
/// </para>
/// </remarks>
public sealed class MicrophoneMuter : IDisposable
{
    private readonly ICaptureSessions _sessions;
    private readonly Func<IReadOnlyCollection<string>> _apps;
    private readonly string _ledgerPath;
    private readonly int _self;
    private readonly Action<string>? _report;

    private readonly Lock _lock = new();
    private Task _tail = Task.CompletedTask;
    private bool _recording;
    private readonly List<int> _muted = [];

    /// <param name="sessions">The mixer.</param>
    /// <param name="apps">Process names to mute, read at each press so Settings applies live.</param>
    /// <param name="ledgerPath">Where the names of currently muted apps are kept for crash repair.</param>
    /// <param name="selfProcessId">Never muted — that would be our own microphone.</param>
    /// <param name="report">Receives a message when the mixer refuses.</param>
    public MicrophoneMuter(
        ICaptureSessions sessions,
        Func<IReadOnlyCollection<string>> apps,
        string ledgerPath,
        int? selfProcessId = null,
        Action<string>? report = null)
    {
        _sessions = sessions;
        _apps = apps;
        _ledgerPath = ledgerPath;
        _self = selfProcessId ?? Environment.ProcessId;
        _report = report;
    }

    /// <summary>The default ledger location.</summary>
    public static string DefaultLedgerPath => DataDirectory.File("muted-apps.txt");

    /// <summary>Completes once every queued mute and restore has run.</summary>
    public Task Settled
    {
        get { lock (_lock) return _tail; }
    }

    /// <summary>Call with every engine state; only Recording edges do anything.</summary>
    public void OnStateChanged(DictationState state)
    {
        lock (_lock)
        {
            var recording = state == DictationState.Recording;
            if (recording == _recording) return;
            _recording = recording;

            Action step = recording ? Mute : Restore;
            _tail = _tail.ContinueWith(_ => Guarded(step), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>Unmutes apps a previous run muted and never restored. Call once at start.</summary>
    public void RecoverFromCrash() => Guarded(() =>
    {
        if (!File.Exists(_ledgerPath)) return;

        var names = new HashSet<string>(File.ReadAllLines(_ledgerPath), StringComparer.OrdinalIgnoreCase);
        foreach (var pid in _sessions.List()
                     .Where(s => s.IsMuted && names.Contains(s.ProcessName))
                     .Select(s => s.ProcessId).Distinct())
        {
            _sessions.SetMuted(pid, false);
        }

        File.Delete(_ledgerPath);
    });

    private void Mute()
    {
        var apps = new HashSet<string>(_apps(), StringComparer.OrdinalIgnoreCase);
        if (apps.Count == 0) return;

        var targets = _sessions.List()
            .Where(s => s.ProcessId != _self && !s.IsMuted && apps.Contains(s.ProcessName))
            .ToList();
        if (targets.Count == 0) return;

        // Ledger before the first mute: a crash between the two must still be repairable.
        File.WriteAllLines(_ledgerPath, targets.Select(s => s.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase));

        // ponytail: mute is per process, so an app with one stream already muted by the user
        // and another not gets both unmuted on restore. Track per session if that ever matters.
        foreach (var pid in targets.Select(s => s.ProcessId).Distinct())
        {
            _sessions.SetMuted(pid, true);
            _muted.Add(pid);
        }
    }

    private void Restore()
    {
        foreach (var pid in _muted)
        {
            // One app that has exited must not keep the others muted.
            try { _sessions.SetMuted(pid, false); }
            catch (Exception e) { _report?.Invoke($"Could not unmute app {pid}: {e.Message}"); }
        }

        _muted.Clear();
        if (File.Exists(_ledgerPath)) File.Delete(_ledgerPath);
    }

    private void Guarded(Action step)
    {
        try { step(); }
        catch (Exception e) { _report?.Invoke($"Muting other apps failed: {e.Message}"); }
    }

    /// <summary>Restores anything muted and waits for it.</summary>
    public void Dispose()
    {
        OnStateChanged(DictationState.Idle);
        Settled.GetAwaiter().GetResult();
    }
}
