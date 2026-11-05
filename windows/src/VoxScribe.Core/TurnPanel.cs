using System.Text;
using System.Text.Json;

namespace VoxScribe.Core;

/// <summary>One titled piece of the panel: a code block from the reply, or one file's edit.</summary>
/// <param name="Title">The block's language, or the edited file's name.</param>
/// <param name="Body">The text shown, already capped.</param>
/// <param name="Diff">Its lines start with "+ " or "- " and are coloured as a diff.</param>
public sealed record PanelBlock(string Title, string Body, bool Diff);

/// <summary>
/// What conversation mode puts on screen rather than saying: the reply's fenced code and the
/// files the turn edited.
/// </summary>
/// <remarks>
/// The edits come from the turn's own tool calls, not <c>git diff</c>: a session commits as it
/// goes, and its tree may hold other sessions' changes, so a diff at the end of a turn shows
/// either nothing or someone else's work.
/// </remarks>
public static class TurnPanel
{
    /// <summary>A longer block is cut: the panel is for a glance, the session keeps the rest.</summary>
    private const int MaxLines = 400;

    /// <summary>The panel for one finished turn; empty when it holds no code and edited nothing.</summary>
    /// <param name="reply">The reply as Claude Code wrote it.</param>
    /// <param name="transcript">The session's JSONL lines.</param>
    public static IReadOnlyList<PanelBlock> Compose(string reply, IEnumerable<string> transcript) =>
        [.. CodeBlocks(reply), .. Edits(transcript)];

    /// <summary>The fenced blocks of a reply, cut the way <see cref="SpeechText.PreClean"/> drops them.</summary>
    public static IEnumerable<PanelBlock> CodeBlocks(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        List<string>? body = null;
        var language = "";

        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                if (body is null)
                {
                    body = [];
                    language = line.TrimStart('`', '~').Trim();
                }
                else
                {
                    yield return new PanelBlock(language.Length > 0 ? language : "code", Cap(body), false);
                    body = null;
                }

                continue;
            }

            body?.Add(raw);
        }

        // An unclosed fence runs to the end, as it does for the pre-clean.
        if (body is { Count: > 0 }) yield return new PanelBlock(language.Length > 0 ? language : "code", Cap(body), false);
    }

    /// <summary>The Edit, MultiEdit and Write calls since the last thing the user said.</summary>
    /// <remarks>
    /// shortcut: a file changed through Bash (sed, a script) leaves no such call and is not
    /// shown; reading the tool results would be the upgrade if that is ever missed.
    /// </remarks>
    public static IReadOnlyList<PanelBlock> Edits(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var edits = new List<PanelBlock>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
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
                    continue;
                }

                if (type.GetString() == "user")
                {
                    if (FromTheUser(content)) edits.Clear();
                }
                else if (type.GetString() == "assistant" && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        if (Edit(block) is { } edit) edits.Add(edit);
                    }
                }
            }
            catch (JsonException)
            {
                // A partial last line, or not a transcript line at all.
            }
        }

        return edits;
    }

    /// <summary>A tool result also arrives as a user line; only words start a new turn.</summary>
    private static bool FromTheUser(JsonElement content) =>
        content.ValueKind == JsonValueKind.String
        || (content.ValueKind == JsonValueKind.Array && content.EnumerateArray().Any(b => Is(b, "text")));

    private static PanelBlock? Edit(JsonElement block)
    {
        if (!Is(block, "tool_use")
            || !block.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object
            || Text(input, "file_path") is not { Length: > 0 } path)
        {
            return null;
        }

        var lines = new List<string>();
        switch (Text(block, "name"))
        {
            case "Edit":
                Change(input, lines);
                break;
            case "MultiEdit" when input.TryGetProperty("edits", out var edits) && edits.ValueKind == JsonValueKind.Array:
                foreach (var edit in edits.EnumerateArray()) Change(edit, lines);
                break;
            case "Write":
                lines.AddRange(Prefixed("+ ", Text(input, "content")));
                break;
            default:
                return null;
        }

        return new PanelBlock(Path.GetFileName(path.Replace('\\', '/')), Cap(lines), true);
    }

    private static void Change(JsonElement edit, List<string> lines)
    {
        lines.AddRange(Prefixed("- ", Text(edit, "old_string")));
        lines.AddRange(Prefixed("+ ", Text(edit, "new_string")));
    }

    private static IEnumerable<string> Prefixed(string prefix, string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n').Select(l => prefix + l);

    private static string Cap(List<string> lines)
    {
        var body = new StringBuilder(string.Join('\n', lines.Take(MaxLines)));
        if (lines.Count > MaxLines) body.Append("\n… ").Append(lines.Count - MaxLines).Append(" more lines");
        return body.ToString();
    }

    private static bool Is(JsonElement block, string kind) =>
        block.ValueKind == JsonValueKind.Object && Text(block, "type") == kind;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool True(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
