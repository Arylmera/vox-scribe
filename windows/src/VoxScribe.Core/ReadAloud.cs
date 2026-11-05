using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using VoxScribe.Abstractions;

namespace VoxScribe.Core;

/// <summary>
/// Reads a Claude Code reply aloud when the <c>/parle</c> or <c>/say</c> hook asks for it.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline: pre-clean → stream the <c>oral</c> rewrite → cut chunks as they arrive → <c>tts</c>
/// per chunk, the next one rendering while the current one plays. An <c>oral</c> failure or
/// empty answer speaks the pre-cleaned text instead — worse, never silent. A <c>tts</c>
/// failure ends the reading and is reported.
/// </para>
/// <para>
/// One reading at a time: a new request or <see cref="Stop"/> cancels the one in progress.
/// Everything here runs off the UI thread; <see cref="Failed"/> is raised on a pool thread.
/// </para>
/// </remarks>
public sealed class ReadAloud : IDisposable
{
    /// <summary>
    /// Shared, bearer per request, like <c>TextCleaner</c>. The timeout covers the wait for
    /// response headers only on the streamed call, and the <c>oral</c> first token is
    /// 5–10 s warm and over 30 s cold.
    /// </summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private const string RequestName = "request.json";

    private readonly Func<SettingsData> _settings;
    private readonly IAudioPlayer _player;
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pickUp;
    private CancellationTokenSource? _reading;
    private string? _lastRequest;
    private ProgressWatcher? _progress;
    private string? _pendingNote;
    private bool _speakingNotes;
    private bool _answering;

    /// <param name="settings">Read at each request, so a toggled setting applies at once.</param>
    /// <param name="player">Plays one WAV clip at a time.</param>
    public ReadAloud(Func<SettingsData> settings, IAudioPlayer player)
    {
        _settings = settings;
        _player = player;
    }

    /// <summary>Said after a turn that put something on screen, instead of spelling it out.</summary>
    private const string OnScreen = "Le code est à l'écran.";

    /// <summary>One short sentence saying why a reading failed or fell back.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>
    /// What a conversation turn shows, or empty to clear the panel. Raised on a pool thread.
    /// </summary>
    public event EventHandler<IReadOnlyList<PanelBlock>>? Panel;

    /// <summary>
    /// Whether a finished turn of this session is read. Conversation mode's whole rule, set by
    /// the app; unset, no turn is ever read.
    /// </summary>
    public Func<string, bool>? AcceptTurn { get; set; }

    /// <summary>The folder the plugin hook drops <c>request.json</c> into.</summary>
    public static string DefaultDirectory => DataDirectory.File("speak");

    /// <summary>Starts watching <paramref name="directory"/> for requests.</summary>
    /// <remarks>
    /// A request already on disk is never read: it is from an earlier session. The hook
    /// replaces the file atomically (temp file renamed over it), hence Renamed as well.
    /// </remarks>
    public void Watch(string directory)
    {
        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Whether this delivery lost a dictation the user cannot see failing.</summary>
    /// <remarks>With no session armed, a dictation outside conversation is simply typed: nothing is lost.</remarks>
    public static bool IsLost(ClaudeDelivery delivery, bool conversation) =>
        delivery == ClaudeDelivery.NotAcknowledged || (delivery == ClaudeDelivery.NoTarget && conversation);

    /// <summary>A short falling tone: the dictation did not arrive. Never throws.</summary>
    public async Task AlertAsync()
    {
#pragma warning disable CA1031 // a failing sound device must never reach the dictation
        try
        {
            await _player.PlayAsync(WavTempo.FallingTone(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Fail($"Alert sound failed — {e.Message}");
        }
#pragma warning restore CA1031
    }

    /// <summary>Speaks the progress notes of <paramref name="session"/>'s transcript as they appear.</summary>
    public void FollowProgress(Func<string?> session, string projects) =>
        _progress = new ProgressWatcher(session, Note, projects);

    /// <summary>
    /// One note, as written: no rewrite, so the voice keeps up with the work. One at a time, and
    /// only the newest waits; the final answer goes first and drops what is waiting.
    /// </summary>
    private void Note(string session, string note)
    {
        var settings = _settings();
        if (AcceptTurn?.Invoke(session) != true || !settings.ReadAloudEnabled || !settings.ProgressVoiceEnabled) return;

        lock (_gate)
        {
            if (_answering) return;
            _pendingNote = note;
            if (_speakingNotes) return;
            _speakingNotes = true;
        }

        _ = SpeakNotesAsync();
    }

    private async Task SpeakNotesAsync()
    {
        while (true)
        {
            string? note;
            lock (_gate)
            {
                note = _pendingNote;
                _pendingNote = null;
                if (note is null || _answering)
                {
                    _speakingNotes = false;
                    return;
                }
            }

            try
            {
                var reading = new CancellationTokenSource();
                Interlocked.Exchange(ref _reading, reading)?.Cancel();
                await SpeakAsync(note, SpeechText.LooksEnglish(note), reading.Token, plain: true).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopped, or the answer took over.
            }
            catch (Exception e)
            {
                Fail($"Read-aloud failed — {e.Message}");
            }
        }
    }

    /// <summary>Stops the reading in progress, if any. Safe from any thread, never blocks.</summary>
    /// <remarks>Called from the keyboard hook thread: the cancel — and the device stop it
    /// triggers — run on the pool instead.</remarks>
    public void Stop()
    {
        lock (_gate) _pendingNote = null;
        if (Volatile.Read(ref _reading) is { } reading) _ = Task.Run(reading.Cancel);
        if (Panel is { } panel) _ = Task.Run(() => panel(this, []));
    }

    private void OnFileEvent(object? sender, FileSystemEventArgs e)
    {
        if (!string.Equals(e.Name, RequestName, StringComparison.OrdinalIgnoreCase)) return;

        // Debounce: one replace raises several events, so each restarts a short wait and
        // only the last one reads the file.
        var pickUp = new CancellationTokenSource();
        Interlocked.Exchange(ref _pickUp, pickUp)?.Cancel();
        _ = PickUpAsync(e.FullPath, pickUp.Token);
    }

    private async Task PickUpAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            if (await ReadRequestAsync(path, cancellationToken).ConfigureAwait(false) is not var (id, text, english, session)) return;

            // A late Changed (antivirus, indexer) re-raises the same request; restarting
            // the reading for it would cut the user off mid-sentence.
            lock (_gate)
            {
                if (id == _lastRequest) return;
                _lastRequest = id;
            }

            if (!_settings().ReadAloudEnabled) return;

            if (session is not null)
            {
                if (AcceptTurn?.Invoke(session) != true) return;
                var blocks = TurnPanel.CodeBlocks(text).ToList();
                Panel?.Invoke(this, blocks);
                if (blocks.Count > 0) text += "\n\n" + OnScreen;
            }

            lock (_gate)
            {
                _pendingNote = null;
                _answering = true;
            }

            try
            {
                var reading = new CancellationTokenSource();
                Interlocked.Exchange(ref _reading, reading)?.Cancel();
                await SpeakAsync(text, english || (session is not null && SpeechText.LooksEnglish(text)), reading.Token, quickStart: session is not null).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) _answering = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer event or request.
        }
        catch (Exception e)
        {
            Fail($"Read-aloud failed — {e.Message}");
        }
    }

    /// <summary>
    /// The request's identity (its timestamp), the reply, whether <c>/say</c> asked for English,
    /// and for a finished turn its session; null when unreadable or there is nothing to read.
    /// </summary>
    /// <remarks>
    /// A turn carries its reply as <c>text</c>: at the moment the turn ends the transcript may
    /// not hold it yet.
    /// </remarks>
    private static async Task<(string Id, string Text, bool English, string? Session)?> ReadRequestAsync(string path, CancellationToken cancellationToken)
    {
        // The writer may still hold the file for a moment, or be halfway through it (the module
        // has no atomic write, and a turn carries a whole reply): retry on either.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
                var root = json.RootElement;
                var transcript = root.TryGetProperty("transcript_path", out var t) ? t.GetString() : null;
                var command = root.TryGetProperty("command", out var c) ? c.GetString() : null;
                var turn = string.Equals(command, "turn", StringComparison.OrdinalIgnoreCase);
                if (!turn && (transcript is not { Length: > 0 } || !File.Exists(transcript))) return null;
                var text = turn
                    ? root.TryGetProperty("text", out var said) ? said.GetString() : null
                    : LastReply(ReadShared(transcript!));
                if (string.IsNullOrWhiteSpace(text)) return null;
                var session = !turn ? null : root.TryGetProperty("session_id", out var s) ? s.GetString() ?? "" : "";
                var id = root.TryGetProperty("ts", out var ts) ? ts.GetRawText() : Guid.NewGuid().ToString();
                var english = string.Equals(command, "say", StringComparison.OrdinalIgnoreCase);
                return (id, text, english, session);
            }
            catch (Exception e) when ((e is IOException or JsonException) && attempt < 10)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }

    /// <summary>Claude Code may be appending to the transcript while it is read.</summary>
    private static IEnumerable<string> ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line) yield return line;
    }

    /// <summary>Wrappers Claude Code puts around injected context, never part of a reply.</summary>
    private static readonly Regex Noise = new(
        @"<(system-reminder|command-message|command-name|local-command-[a-z-]+|task-notification|persisted-output)(?:\s[^>]*)?>.*?</\1>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>The text of the last assistant line of a Claude Code JSONL transcript, or "".</summary>
    /// <remarks>
    /// Only the last line: a turn writes each block on its own line, and the earlier text
    /// lines are progress notes between tool calls. Sidechain (subagent) and meta lines are
    /// not the conversation. Unparseable lines are skipped.
    /// </remarks>
    public static string LastReply(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var last = "";
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("type", out var type) || type.GetString() != "assistant"
                    || IsTrue(root, "isMeta") || IsTrue(root, "isSidechain")
                    || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                    || !message.TryGetProperty("content", out var content))
                {
                    continue;
                }

                var text = content.ValueKind switch
                {
                    JsonValueKind.String => content.GetString() ?? "",
                    JsonValueKind.Array => string.Join("\n", content.EnumerateArray()
                        .Where(b => b.ValueKind == JsonValueKind.Object
                            && b.TryGetProperty("type", out var k) && k.GetString() == "text"
                            && b.TryGetProperty("text", out var v) && v.ValueKind == JsonValueKind.String)
                        .Select(b => b.GetProperty("text").GetString())),
                    _ => "",
                };
                text = Noise.Replace(text, "").Trim();
                if (text.Length > 0) last = text;
            }
            catch (JsonException)
            {
                // A partial last line, or not a transcript line at all.
            }
        }

        return last;
    }

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Speaks <paramref name="markdown"/> through the whole pipeline.</summary>
    /// <param name="markdown">The reply as Claude Code wrote it.</param>
    /// <param name="english">Spoken in English (<c>/say</c>) rather than French (<c>/parle</c>).</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <param name="quickStart">
    /// Conversation mode: the first sentence is spoken as written while the rewrite works on the
    /// rest, so the voice does not wait for the rewrite's first token.
    /// </param>
    /// <param name="plain">Spoken as written, with no rewrite at all.</param>
    /// <remarks>Public for the live harness; the watcher is the normal caller.</remarks>
    public async Task SpeakAsync(string markdown, bool english, CancellationToken cancellationToken, bool quickStart = false, bool plain = false)
    {
        var settings = _settings();
        var prompt = english ? settings.EnglishOralPrompt : settings.OralPrompt;
        var voice = english ? settings.EnglishVoice : settings.TtsVoice;

        // The cleanup endpoint is the chat gateway; machines that only set STT use that one.
        var (endpoint, key) = settings.CleanupEndpoint is { Length: > 0 } cleanup
            ? (cleanup, settings.CleanupApiKey)
            : (settings.SttEndpoint, settings.SttApiKey);
        if (endpoint is not { Length: > 0 })
        {
            Fail("Read-aloud skipped — no gateway endpoint set (Settings → CLEANUP)");
            return;
        }

        var baseUrl = endpoint.TrimEnd('/');
        var clean = SpeechText.PreClean(markdown);
        if (clean.Length == 0) return;
        var (first, rest) = plain ? (clean, "") : quickStart ? SpeechText.SplitFirst(clean) : ("", clean);

        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = run.Token;
        var clock = Stopwatch.StartNew();
        double? firstToken = null, firstAudio = null;
        var chunks = 0;

        var sentences = Channel.CreateUnbounded<string>();

        // Capacity 1: the next clip renders while the current one plays, and no further.
        var clips = Channel.CreateBounded<byte[]>(1);

        async Task ProduceAsync()
        {
            var splitter = new SpeechSplitter();
            var spoken = false;
            var failed = false;
            if (first.Length > 0) await sentences.Writer.WriteAsync(first, token).ConfigureAwait(false);
            if (rest.Length == 0) return;

            try
            {
                await foreach (var delta in StreamOralAsync(baseUrl, key, settings.OralModel, prompt, rest, token).ConfigureAwait(false))
                {
                    firstToken ??= clock.Elapsed.TotalSeconds;
                    spoken |= !string.IsNullOrWhiteSpace(delta);
                    foreach (var chunk in splitter.Push(delta)) await sentences.Writer.WriteAsync(chunk, token).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                Fail(spoken
                    ? $"Read-aloud rewrite cut short — {Reason(e)}"
                    : $"Read-aloud rewrite failed, reading the reply as written — {Reason(e)}");
                failed = true;
            }

            if (!spoken)
            {
                if (!failed) Fail("Read-aloud rewrite came back empty, reading the reply as written");
                splitter = new SpeechSplitter();
                foreach (var chunk in splitter.Push(rest)) await sentences.Writer.WriteAsync(chunk, token).ConfigureAwait(false);
            }

            foreach (var chunk in splitter.Flush()) await sentences.Writer.WriteAsync(chunk, token).ConfigureAwait(false);
        }

        async Task SynthesiseAllAsync()
        {
            await foreach (var sentence in sentences.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                var clip = await SynthesiseAsync(baseUrl, key, settings.TtsModel, voice, sentence, token).ConfigureAwait(false);
                await clips.Writer.WriteAsync(WavTempo.Apply(clip, settings.ReadingRate * WavTempo.EvenRate(sentence.Length, clip)), token).ConfigureAwait(false);
            }
        }

        async Task PlayAllAsync()
        {
            await foreach (var clip in clips.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                // A channel hands over a buffered item even after cancellation.
                token.ThrowIfCancellationRequested();
                firstAudio ??= clock.Elapsed.TotalSeconds;
                chunks++;
                await _player.PlayAsync(clip, token).ConfigureAwait(false);
            }
        }

        // Each stage closes its output when it ends, however it ends; a failing stage
        // cancels the others.
        async Task Stage(Func<Task> body, ChannelWriter<string>? closesSentences, ChannelWriter<byte[]>? closesClips)
        {
            try
            {
                await body().ConfigureAwait(false);
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                Fail($"Read-aloud stopped — {Reason(e)}");
                _ = AlertAsync(); // a reply that goes silent is as lost as a dictation
                await run.CancelAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopped or replaced.
            }
            finally
            {
                closesSentences?.TryComplete();
                closesClips?.TryComplete();
            }
        }

        await Task.WhenAll(
            Stage(ProduceAsync, sentences.Writer, null),
            Stage(SynthesiseAllAsync, null, clips.Writer),
            Stage(PlayAllAsync, null, null)).ConfigureAwait(false);

        Trace.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Read-aloud: first token {firstToken:0.0} s, first audio {firstAudio:0.0} s, {chunks} chunks, done {clock.Elapsed.TotalSeconds:0.0} s{(token.IsCancellationRequested ? " (stopped)" : "")}"));
    }

    /// <summary>Streams the <c>oral</c> rewrite as content deltas.</summary>
    private static async IAsyncEnumerable<string> StreamOralAsync(
        string baseUrl, string? key, string model, string prompt, string text,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = true,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = prompt },
                new JsonObject { ["role"] = "user", ["content"] = text }),
        };

        using var request = Post(baseUrl + "/chat/completions", key, body);
        using var response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"gateway answered {(int)response.StatusCode}");

        using var reader = new StreamReader(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));

        // ponytail: a stream that stalls after its headers waits until the next request or a
        // stop cancels it; add a per-read timeout if that is ever seen.
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;
            if (Delta(data) is { Length: > 0 } delta) yield return delta;
        }
    }

    /// <summary>Pulls <c>choices[0].delta.content</c> from one SSE event, or null.</summary>
    public static string? Delta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("choices", out var choices)
                   && choices.GetArrayLength() > 0
                   && choices[0].TryGetProperty("delta", out var delta)
                   && delta.TryGetProperty("content", out var content)
                ? content.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Renders one chunk as a WAV clip through the <c>tts</c> alias.</summary>
    private static async Task<byte[]> SynthesiseAsync(
        string baseUrl, string? key, string model, string voice, string text, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["input"] = text,
            ["voice"] = voice,
            ["response_format"] = "wav",
        };

        using var request = Post(baseUrl + "/audio/speech", key, body);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"speech synthesis answered {(int)response.StatusCode}");

        var clip = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // A synthesis error upstream can come back as 200 with an empty body.
        return clip.Length > 44 ? clip : throw new HttpRequestException("speech synthesis returned an empty clip");
    }

    private static HttpRequestMessage Post(string url, string? key, JsonObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private static string Reason(Exception e) =>
        e is TaskCanceledException ? "gateway did not answer in time" : e.Message;

    private void Fail(string message) => Failed?.Invoke(this, message);

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _progress?.Dispose();
        _pickUp?.Cancel();
        _reading?.Cancel();
    }
}
