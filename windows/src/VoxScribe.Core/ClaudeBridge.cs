using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoxScribe.Abstractions;

namespace VoxScribe.Core;

/// <summary>What became of a dictation handed to the armed Claude Code session.</summary>
public enum ClaudeDelivery
{
    /// <summary>No live armed session: the caller takes today's window-title path.</summary>
    NoTarget,

    /// <summary>The session's module submitted it.</summary>
    Delivered,

    /// <summary>A session was armed but did not acknowledge in time. Never retried elsewhere.</summary>
    NotAcknowledged,
}

/// <summary>A request from the band's microphone button.</summary>
public enum MicAction
{
    /// <summary>Parler: start a command dictation.</summary>
    Start,

    /// <summary>Envoyer: end it and send.</summary>
    Stop,

    /// <summary>Annuler: throw it away.</summary>
    Cancel,
}

/// <summary>
/// The app side of the voxscribe plugin's band above Claude Code's prompt.
/// </summary>
/// <remarks>
/// <para>
/// Files in <see cref="DefaultDirectory"/>, nothing else: the module writes
/// <c>target.json</c> (the armed session and its beat) and <c>mic.json</c>; the app writes
/// <c>status.json</c> and <c>outbox/&lt;session&gt;.json</c>; the module answers with
/// <c>outbox/&lt;id&gt;.ack</c>.
/// </para>
/// <para>
/// Every rule lives here and not in the module, because the module has no CI. And every
/// file operation swallows IO failures: if this breaks, the command chord falls back to
/// today's window-title path and nothing else notices.
/// </para>
/// </remarks>
public sealed partial class ClaudeBridge : IDisposable
{
    /// <summary>A beat older than this is a session that died while armed.</summary>
    public static TimeSpan Stale { get; } = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AckPoll = TimeSpan.FromMilliseconds(50);

    private readonly string _directory;
    private readonly IClock _clock;
    private readonly TimeSpan _ackTimeout;
    private readonly Lock _gate = new();
    private readonly Lock _writing = new();
    private static readonly TimeSpan MicMaxAge = TimeSpan.FromSeconds(10);
    private string _state = "idle";
    private bool _command;
    private long _since;
    private Timer? _beat;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pickUp;
    private long _lastMic;

    /// <param name="directory">Where the files live; <see cref="DefaultDirectory"/> in the app.</param>
    /// <param name="clock">Judges beats; the system clock by default.</param>
    /// <param name="ackTimeout">How long the module has to acknowledge; 3 s by default.</param>
    public ClaudeBridge(string directory, IClock? clock = null, TimeSpan? ackTimeout = null)
    {
        _directory = directory;
        _clock = clock ?? SystemClock.Instance;
        _ackTimeout = ackTimeout ?? TimeSpan.FromSeconds(3);
        _since = Now;
    }

    /// <summary>The folder the plugin module reads and writes.</summary>
    public static string DefaultDirectory => DataDirectory.File("claude");

    private long Now => _clock.Now.ToUnixTimeMilliseconds();

    private string Outbox => Path.Combine(_directory, "outbox");

    /// <summary>A session id becomes a file name: only a plain id is accepted.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SessionId();

    /// <summary>Creates the folders and starts the status beat.</summary>
    public void Start()
    {
        try
        {
            Directory.CreateDirectory(Outbox);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _watcher = new FileSystemWatcher(_directory, "mic.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        _watcher.Created += OnMicFile;
        _watcher.Changed += OnMicFile;
        _watcher.Renamed += OnMicFile;
        _watcher.EnableRaisingEvents = true;

        _beat = new Timer(_ => WriteStatus(), null, TimeSpan.Zero, Beat);
    }

    /// <summary>The band's microphone button. Raised on a pool thread.</summary>
    public event EventHandler<MicAction>? Mic;

    /// <summary>A mic request, or null when malformed, for an unusable session, or older than 10 s.</summary>
    public static (string Session, MicAction Action, long Ts)? ParseMic(string json, long nowMs)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("session_id", out var s) || s.GetString() is not { } session) return null;
            if (!SessionId().IsMatch(session)) return null;
            if (!root.TryGetProperty("ts", out var t) || !t.TryGetInt64(out var ts)) return null;
            if (nowMs - ts > (long)MicMaxAge.TotalMilliseconds) return null;

            MicAction? action = root.TryGetProperty("action", out var a) ? a.GetString() switch
            {
                "start" => MicAction.Start,
                "stop" => MicAction.Stop,
                "cancel" => MicAction.Cancel,
                _ => null,
            } : null;

            return action is { } act ? (session, act, ts) : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private void OnMicFile(object? sender, FileSystemEventArgs e)
    {
        // One write raises several events: each restarts a short wait, only the last reads.
        var pickUp = new CancellationTokenSource();
        Interlocked.Exchange(ref _pickUp, pickUp)?.Cancel();
        _ = PickUpMicAsync(e.FullPath, pickUp.Token);
    }

    private async Task PickUpMicAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            if (ParseMic(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), Now) is not var (session, action, ts))
                return;

            lock (_gate)
            {
                // A late Changed (antivirus, indexer) re-raises the same request.
                if (ts == _lastMic) return;
                _lastMic = ts;
                if (action == MicAction.Start) MicSession = session;
            }

            Mic?.Invoke(this, action);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            // Replaced by a newer event, or the module still holds the file: its next write retries.
        }
    }

    /// <summary>The armed session when its beat is fresh, else null.</summary>
    public string? LiveTarget()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "target.json")));
            var root = json.RootElement;
            if (!root.TryGetProperty("session_id", out var s) || s.ValueKind != JsonValueKind.String) return null;
            if (!root.TryGetProperty("beat", out var b) || !b.TryGetInt64(out var beat)) return null;

            var session = s.GetString()!;
            if (!SessionId().IsMatch(session)) return null;
            return Now - beat <= (long)Stale.TotalMilliseconds ? session : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Hands <paramref name="text"/> to the armed session and waits for its acknowledgement.
    /// Never throws.
    /// </summary>
    public async Task<ClaudeDelivery> TryDeliverAsync(string text, CancellationToken cancellationToken)
    {
        if (LiveTarget() is not { } session) return ClaudeDelivery.NoTarget;

        var id = Guid.NewGuid().ToString("N");
        var outbox = Path.Combine(Outbox, session + ".json");
        var ack = Path.Combine(Outbox, id + ".ack");

        try
        {
            Directory.CreateDirectory(Outbox);
            WriteAtomic(outbox, Json(w =>
            {
                w.WriteString("id", id);
                w.WriteString("text", text);
                w.WriteNumber("ts", Now);
            }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing reached the module, so the window-title path is still safe.
            return ClaudeDelivery.NoTarget;
        }

        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < _ackTimeout)
        {
            if (File.Exists(ack))
            {
                TryDelete(ack);
                Withdraw(outbox, id);
                return ClaudeDelivery.Delivered;
            }

            try
            {
                await Task.Delay(AckPoll, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Withdrawn so a module that wakes late cannot submit it after the user was told it failed.
        Withdraw(outbox, id);
        return ClaudeDelivery.NotAcknowledged;
    }

    /// <summary>
    /// Records the engine's state for the band. A command in progress names its session.
    /// </summary>
    /// <remarks>
    /// Called from the engine's Changed event, which fires while the microphone is starting:
    /// only fields change here, and the file is written on the pool, so a slow disk or an
    /// antivirus scan can never delay the first syllable of a dictation.
    /// </remarks>
    public void PublishStatus(DictationState state, bool command)
    {
        lock (_gate)
        {
            var name = state switch
            {
                DictationState.Recording => "listening",
                DictationState.Transcribing => "transcribing",
                _ => "idle",
            };
            if (name != _state) _since = Now;
            _state = name;
            _command = command;
            if (state == DictationState.Idle) MicSession = null;
        }

        ThreadPool.QueueUserWorkItem(_ => WriteStatus());
    }

    /// <summary>The session whose band started the dictation in progress, if one did.</summary>
    private string? MicSession { get; set; }

    /// <summary>Writes the latest state, whoever asked: queued writes therefore settle on the newest.</summary>
    private void WriteStatus()
    {
        // One writer at a time: two replacing status.json at once would fight over the temp file.
        lock (_writing)
        {
            string state;
            bool command;
            long since;
            string? mic;
            lock (_gate) (state, command, since, mic) = (_state, _command, _since, MicSession);

            var session = state != "idle" && command ? mic ?? LiveTarget() : null;
            var body = Json(w =>
            {
                w.WriteString("state", state);
                if (session is null) w.WriteNull("session_id");
                else w.WriteString("session_id", session);
                w.WriteNumber("since", since);
                w.WriteNumber("beat", Now);
            });

            try
            {
                WriteAtomic(Path.Combine(_directory, "status.json"), body);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The band then shows "not running" after the stale delay — the truth, near enough.
            }
        }
    }

    /// <summary>
    /// One JSON object, written by hand: the trim analyzer is on for src/, and reflection-based
    /// serialization of anonymous types would fail the warnings-as-errors build.
    /// </summary>
    private static string Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deletes the outbox only if it is still ours: a newer dictation may have replaced it.</summary>
    private static void Withdraw(string outbox, string id)
    {
        try
        {
            using (var json = JsonDocument.Parse(File.ReadAllText(outbox)))
            {
                if (json.RootElement.TryGetProperty("id", out var i) && i.GetString() != id) return;
            }

            File.Delete(outbox);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Already gone (the module deleted it) or locked; a stale outbox is refused by the module anyway.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The module must never read half a file. A reader that opened the target without
    /// <see cref="FileShare.Delete"/> can briefly block the replace: retried rather than lost,
    /// the same pattern as <c>ReadAloud</c>'s retry on a sharing violation.
    /// </summary>
    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 10)
            {
                Thread.Sleep(10);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _beat?.Dispose();
        _pickUp?.Cancel();
    }
}
