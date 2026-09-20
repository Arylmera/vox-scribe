using System.Text;
using System.Text.RegularExpressions;

namespace VoxScribe.Core;

/// <summary>
/// Turns spoken punctuation — "virgule", "point", "new line" — into the marks themselves.
/// French and English, deterministic, applied after dictionary correction.
/// </summary>
/// <remarks>
/// <para>
/// Not a dictionary rule, because punctuation is position-aware in a way a literal
/// replacement cannot be: the mark glues to the previous word, takes one space after it
/// mid-text and none at the end, and a newline is clean on both sides. Hence a sibling pass
/// with the corrector's safety contract — NFC input, letter/digit fences rather than
/// <c>\b</c>, <c>IgnoreCase | CultureInvariant</c>, the ICU-safe subset, a match timeout.
/// </para>
/// <para>
/// Opt-in. "Point" is an ordinary French word, and someone dictating prose that is later
/// tidied by the cleanup model gains nothing from it. The raw shortcut without a gateway
/// is where it earns its place.
/// </para>
/// </remarks>
public static class VoiceCommandProcessor
{
    private static readonly (string Spoken, string Mark)[] Commands =
    [
        ("point d'interrogation", "?"),
        ("point d'exclamation", "!"),
        ("exclamation mark", "!"),
        ("question mark", "?"),
        ("point virgule", ";"),
        ("à la ligne", "\n"),
        ("a la ligne", "\n"),
        ("nouvelle ligne", "\n"),
        ("deux points", ":"),
        ("full stop", "."),
        ("semicolon", ";"),
        ("new line", "\n"),
        ("newline", "\n"),
        ("virgule", ","),
        ("period", "."),
        ("comma", ","),
        ("colon", ":"),
        ("point", "."),
    ];

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex SeparatorRun =
        new(@"[\s\-]+", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Dictionary<string, string> Marks =
        Commands.ToDictionary(c => c.Spoken, c => c.Mark, StringComparer.Ordinal);

    private static readonly Regex Matcher = BuildMatcher();

    private static Regex BuildMatcher()
    {
        // Longest spoken form first so "point d'interrogation" beats "point". Spaces and
        // hyphens inside a command match any run of either — the same glued/hyphenated
        // tolerance as the dictionary — and either apostrophe is accepted, because
        // recognisers are split on which one they emit.
        var alternatives = Commands
            .OrderByDescending(c => c.Spoken.Length)
            .Select(c => string.Join(
                @"[\s\-]+",
                c.Spoken.Split(' ').Select(part => Regex.Escape(part).Replace("'", "['’]", StringComparison.Ordinal))));

        var pattern = @"[ \t]*(?<![\p{L}\p{N}])(?<cmd>" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}])[ \t]*";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    }

    /// <summary>Replaces every spoken command in <paramref name="text"/> with its mark.</summary>
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var normalized = text.Normalize(NormalizationForm.FormC);
        var replaced = Matcher.Replace(normalized, match =>
        {
            var key = SeparatorRun.Replace(match.Groups["cmd"].Value, " ")
                .Replace('’', '\'')
                .ToLowerInvariant();
            var mark = Marks[key];
            if (mark == "\n") return "\n";

            var atEnd = match.Index + match.Length >= normalized.Length;
            return atEnd ? mark : mark + " ";
        });

        // "point à la ligne": the period's trailing space runs into the newline the next
        // command emits.
        return replaced.Replace(" \n", "\n", StringComparison.Ordinal);
    }
}
