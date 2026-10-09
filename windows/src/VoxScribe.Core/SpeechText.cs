using System.Text;
using System.Text.RegularExpressions;

namespace VoxScribe.Core;

/// <summary>
/// Text shaping for read-aloud: the deterministic pre-clean that runs before the oral
/// rewrite, so the model never sees what it must not say.
/// </summary>
/// <remarks>
/// Rules from the read-aloud spec, tuned on real replies: fenced code dropped, URLs dropped,
/// inline code unwrapped (a path becomes its last segment), table separator rows dropped and
/// table rows read as their cells joined by ", ", heading / bullet / numbering markers and
/// bold markers stripped. It is also what gets spoken when the rewrite fails.
/// </remarks>
public static partial class SpeechText
{
    /// <summary>Applies the pre-clean rules to a markdown reply.</summary>
    public static string PreClean(string markdown)
    {
        var lines = new List<string>();
        var inFence = false;

        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;

            if (line.StartsWith('|'))
            {
                if (TableSeparator().IsMatch(line)) continue;
                line = string.Join(", ", line.Trim('|').Split('|')
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0));
            }

            line = LineMarker().Replace(line, string.Empty);
            lines.Add(line);
        }

        var text = string.Join('\n', lines);
        text = Url().Replace(text, string.Empty);
        text = InlineCode().Replace(text, m => LastSegment(m.Groups[1].Value));
        text = text.Replace("**", string.Empty, StringComparison.Ordinal)
                   .Replace("__", string.Empty, StringComparison.Ordinal);

        // Blank-line runs left by dropped blocks collapse to one paragraph break.
        return BlankRuns().Replace(text, "\n\n").Trim();
    }

    /// <summary>The last segment of a path, or the text itself when it holds no separator.</summary>
    private static string LastSegment(string code)
    {
        if (code.IndexOfAny(['/', '\\']) < 0) return code;
        var parts = code.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : string.Empty;
    }

    [GeneratedRegex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"^(#{1,6}\s+|[-*+]\s+|\d+[.)]\s+)")]
    private static partial Regex LineMarker();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();

    [GeneratedRegex(@"`([^`\n]*)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();
}

/// <summary>
/// Cuts speech into chunks for pipelined synthesis, as the text streams in.
/// </summary>
/// <remarks>
/// <para>
/// A chunk only ever ends at a sentence end — a cut mid-sentence sounds broken when spoken.
/// A sentence ends at a run of <c>. ! ? …</c> followed by a space and then not by a lowercase
/// letter (so "etc. puis" holds), or at a line break (the line gets a full stop if it has
/// none), or at the end of the text. A dot inside
/// a number ("0.2") or directly followed by a letter is no end, nor is a single capital's dot
/// ("M. Dupont"), and "..." ends only after its last dot.
/// </para>
/// <para>
/// The first chunk is exactly the first sentence, so audio starts as early as it can. Later
/// sentences are grouped up to <see cref="Max"/> characters; a longer sentence stays whole
/// (synthesis is ~0.2 s per sentence, so a long chunk costs nothing). Streaming emits a chunk
/// only once its end has arrived, and gives exactly the result of splitting the whole text.
/// </para>
/// </remarks>
public sealed partial class SpeechSplitter
{
    /// <summary>Group size for the sentences after the first.</summary>
    public const int Max = 140;

    private readonly StringBuilder _rest = new();
    private string _pending = string.Empty;
    private bool _started;

    /// <summary>Splits a whole text at once.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        var splitter = new SpeechSplitter();
        return [.. splitter.Push(text), .. splitter.Flush()];
    }

    /// <summary>Appends streamed text and returns the chunks it completed.</summary>
    public IEnumerable<string> Push(string delta)
    {
        _rest.Append(delta);
        var done = new List<string>();
        while (SentenceEnd(final: false) is { } end) Take(end, done);

        // No later sentence can join the group once what has already arrived of it overflows.
        if (_pending.Length > 0 && _pending.Length + 1 + Normalize(_rest.ToString()).Length > Max)
        {
            done.Add(_pending);
            _pending = string.Empty;
        }

        return done;
    }

    /// <summary>Ends the stream: whatever remains, as whole sentences.</summary>
    public IEnumerable<string> Flush()
    {
        var done = new List<string>();
        while (SentenceEnd(final: true) is { } end) Take(end, done);
        Take(_rest.Length, done);
        if (_pending.Length > 0) done.Add(_pending);
        _pending = string.Empty;
        return done;
    }

    private void Take(int end, List<string> done)
    {
        var sentence = Normalize(_rest.ToString(0, end));
        _rest.Remove(0, end);
        if (sentence.Length == 0) return;

        // A line without a stop (a table row, a bullet) gets one, or grouping runs it into
        // the next and the voice never pauses between them.
        if (!IsStop(sentence[^1])) sentence += ".";

        if (!_started)
        {
            _started = true;
            done.Add(sentence);
        }
        else if (_pending.Length == 0)
        {
            _pending = sentence;
        }
        else if (_pending.Length + 1 + sentence.Length <= Max)
        {
            _pending += " " + sentence;
        }
        else
        {
            done.Add(_pending);
            _pending = sentence;
        }
    }

    /// <summary>
    /// Index just past the first complete sentence in the buffer, or null when none is
    /// complete yet — <paramref name="final"/> says no more text is coming.
    /// </summary>
    private int? SentenceEnd(bool final)
    {
        var s = _rest;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\n') return i + 1;
            if (!IsStop(s[i])) continue;

            // Only the last mark of a run ("...", "?!") can be followed by a space.
            var after = i + 1;
            if (after == s.Length) return final ? after : null;
            if (!char.IsWhiteSpace(s[after])) continue;

            // An abbreviated initial ("M. Dupont"): one capital standing alone before a dot.
            if (s[i] == '.' && i >= 1 && char.IsUpper(s[i - 1]) && (i == 1 || char.IsWhiteSpace(s[i - 2])))
                continue;

            var next = after;
            while (next < s.Length && char.IsWhiteSpace(s[next]) && s[next] != '\n') next++;
            if (next == s.Length) return final ? after : null;
            if (s[next] != '\n' && char.IsLower(s[next])) continue;

            return after;
        }

        return null;
    }

    private static bool IsStop(char c) => c is '.' or '!' or '?' or '…';

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
