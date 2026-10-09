using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using VoxScribe.Abstractions;

namespace VoxScribe.Core;

/// <summary>
/// Reads a Claude Code reply aloud when <c>/parle</c> drops it in the speak folder.
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

    /// <param name="settings">Read at each request, so a toggled setting applies at once.</param>
    /// <param name="player">Plays one WAV clip at a time.</param>
    public ReadAloud(Func<SettingsData> settings, IAudioPlayer player)
    {
        _settings = settings;
        _player = player;
    }

    /// <summary>One short sentence saying why a reading failed or fell back.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>The folder the <c>/parle</c> hook drops <c>request.json</c> into.</summary>
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

    /// <summary>Stops the reading in progress, if any. Safe from any thread, never blocks.</summary>
    /// <remarks>Called from the keyboard hook thread: the cancel — and the device stop it
    /// triggers — run on the pool instead.</remarks>
    public void Stop()
    {
        if (Volatile.Read(ref _reading) is { } reading) _ = Task.Run(reading.Cancel);
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
            if (await ReadRequestAsync(path, cancellationToken).ConfigureAwait(false) is not var (id, text)) return;

            // A late Changed (antivirus, indexer) re-raises the same request; restarting
            // the reading for it would cut the user off mid-sentence.
            lock (_gate)
            {
                if (id == _lastRequest) return;
                _lastRequest = id;
            }

            if (!_settings().ReadAloudEnabled) return;

            var reading = new CancellationTokenSource();
            Interlocked.Exchange(ref _reading, reading)?.Cancel();
            await SpeakAsync(text, reading.Token).ConfigureAwait(false);
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

    /// <summary>The request's identity (its timestamp) and text, or null when unreadable.</summary>
    private static async Task<(string Id, string Text)?> ReadRequestAsync(string path, CancellationToken cancellationToken)
    {
        // The writer may still hold the file for a moment: retry on a sharing violation.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
                var root = json.RootElement;
                var text = root.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) return null;
                var id = root.TryGetProperty("ts", out var ts) ? ts.GetRawText() : Guid.NewGuid().ToString();
                return (id, text);
            }
            catch (IOException) when (attempt < 10)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }

    /// <summary>Speaks <paramref name="markdown"/> through the whole pipeline.</summary>
    /// <remarks>Public for the live harness; the watcher is the normal caller.</remarks>
    public async Task SpeakAsync(string markdown, CancellationToken cancellationToken)
    {
        var settings = _settings();

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
            try
            {
                await foreach (var delta in StreamOralAsync(baseUrl, key, settings, clean, token).ConfigureAwait(false))
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
                foreach (var chunk in splitter.Push(clean)) await sentences.Writer.WriteAsync(chunk, token).ConfigureAwait(false);
            }

            foreach (var chunk in splitter.Flush()) await sentences.Writer.WriteAsync(chunk, token).ConfigureAwait(false);
        }

        async Task SynthesiseAllAsync()
        {
            await foreach (var sentence in sentences.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                await clips.Writer.WriteAsync(
                    await SynthesiseAsync(baseUrl, key, settings, sentence, token).ConfigureAwait(false),
                    token).ConfigureAwait(false);
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
        string baseUrl, string? key, SettingsData settings, string text,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = settings.OralModel,
            ["stream"] = true,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = settings.OralPrompt },
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
        string baseUrl, string? key, SettingsData settings, string text, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = settings.TtsModel,
            ["input"] = text,
            ["voice"] = settings.TtsVoice,
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
        _pickUp?.Cancel();
        _reading?.Cancel();
    }
}
