using System.Text;
using System.Text.Json;

namespace VoxScribe.Core;

/// <summary>
/// Tells a progress note from a final answer in a transcript, one line at a time. A turn writes
/// each block on its own line: text followed by a tool call is a note, and text that nothing
/// follows is the answer, which reaches the app by the plugin instead.
/// </summary>
public sealed class ProgressFilter
{
    private string? _held;

    /// <summary>The note this line confirms, or null.</summary>
    public string? Feed(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || True(root, "isMeta") || True(root, "isSidechain")
                || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var content))
            {
                return null;
            }

            if (type.GetString() == "user")
            {
                // A tool result is a user line too, and does not end the turn's text.
                if (content.ValueKind == JsonValueKind.String || (content.ValueKind == JsonValueKind.Array && content.EnumerateArray().Any(b => Is(b, "text"))))
                    _held = null;
                return null;
            }

            if (type.GetString() != "assistant" || content.ValueKind != JsonValueKind.Array) return null;

            string? note = null;
            foreach (var block in content.EnumerateArray())
            {
                if (Is(block, "text") && block.TryGetProperty("text", out var text) && text.GetString() is { } said && !string.IsNullOrWhiteSpace(said))
                {
                    _held = said;
                }
                else if (Is(block, "tool_use") && _held is not null)
                {
                    note = _held;
                    _held = null;
                }
            }

            return note;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Is(JsonElement block, string kind) =>
        block.ValueKind == JsonValueKind.Object && block.TryGetProperty("type", out var t) && t.GetString() == kind;

    private static bool True(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>
/// Follows the armed session's transcript from the outside, so a fault here can never touch
/// Claude Code. It starts at the end of the file: what was said before is never read.
/// </summary>
public sealed class ProgressWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly Func<string?> _session;
    private readonly Action<string, string> _note;
    private readonly string _projects;
    private readonly Timer? _timer;
    private readonly Lock _ticking = new();
    private string? _followed;
    private string? _path;
    private long _offset;
    private ProgressFilter _filter = new();

    /// <param name="session">The session to follow, or null for none.</param>
    /// <param name="note">Receives the session and each note.</param>
    /// <param name="projects">Claude Code's projects folder.</param>
    /// <param name="timer">False in tests, which call <see cref="Tick"/> themselves.</param>
    public ProgressWatcher(Func<string?> session, Action<string, string> note, string projects, bool timer = true)
    {
        _session = session;
        _note = note;
        _projects = projects;
        if (timer) _timer = new Timer(_ => Tick(), null, Interval, Interval);
    }

    /// <summary>Claude Code's projects folder on this machine.</summary>
    public static string DefaultProjects => Path.Combine(
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
        "projects");

    /// <summary>Reads what the transcript gained since the last tick. Never throws.</summary>
    public void Tick()
    {
        if (!_ticking.TryEnter()) return;
        try
        {
            var session = _session();
            if (session != _followed)
            {
                (_followed, _path, _filter) = (session, null, new ProgressFilter());
            }

            if (session is null) return;

            if (_path is null)
            {
                _path = Find(session);
                if (_path is not null) _offset = new FileInfo(_path).Length;
                return;
            }

            foreach (var line in NewLines(_path))
            {
                if (_filter.Feed(line) is { } note) _note(session, note);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Mid-rotation or locked: the next tick reads from the same offset.
        }
        finally
        {
            _ticking.Exit();
        }
    }

    private string? Find(string session)
    {
        if (!Directory.Exists(_projects)) return null;
        return Directory.EnumerateDirectories(_projects)
            .Select(d => Path.Combine(d, session + ".jsonl"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Complete lines only: a half-written one waits for the next tick.</summary>
    private List<string> NewLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= _offset) return [];

        stream.Position = _offset;
        var bytes = new byte[stream.Length - _offset];
        stream.ReadExactly(bytes);
        var end = Array.LastIndexOf(bytes, (byte)'\n');
        if (end < 0) return [];

        _offset += end + 1;
        return [.. Encoding.UTF8.GetString(bytes, 0, end).Split('\n').Where(l => !string.IsNullOrWhiteSpace(l))];
    }

    /// <inheritdoc />
    public void Dispose() => _timer?.Dispose();
}
