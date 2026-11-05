using System.Text;

namespace VoxScribe.Core;

/// <summary>One code block of the panel.</summary>
/// <param name="Title">The block's language, or "code".</param>
/// <param name="Body">The text shown, already capped.</param>
public sealed record PanelBlock(string Title, string Body);

/// <summary>
/// What conversation mode puts on screen rather than saying: the reply's fenced code.
/// </summary>
/// <remarks>
/// The reply only, never the turn's Edit and Write calls: those showed every throwaway script
/// the turn wrote. A diff the user wants is asked for, and arrives as a fence in the reply.
/// </remarks>
public static class TurnPanel
{
    /// <summary>A longer block is cut: the panel is for a glance, the session keeps the rest.</summary>
    private const int MaxLines = 400;

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
                    yield return new PanelBlock(language.Length > 0 ? language : "code", Cap(body));
                    body = null;
                }

                continue;
            }

            body?.Add(raw);
        }

        // An unclosed fence runs to the end, as it does for the pre-clean.
        if (body is { Count: > 0 }) yield return new PanelBlock(language.Length > 0 ? language : "code", Cap(body));
    }

    private static string Cap(List<string> lines)
    {
        var body = new StringBuilder(string.Join('\n', lines.Take(MaxLines)));
        if (lines.Count > MaxLines) body.Append("\n… ").Append(lines.Count - MaxLines).Append(" more lines");
        return body.ToString();
    }
}
