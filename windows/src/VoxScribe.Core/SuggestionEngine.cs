namespace VoxScribe.Core;

/// <summary>A recurring raw → cleaned word substitution worth making a dictionary rule.</summary>
public sealed record DictionarySuggestion(string Hear, string Write, int Count);

/// <summary>
/// Mines raw-versus-cleaned transcript pairs for word substitutions the cleanup model keeps
/// making. A fix it applies every time is a fix the dictionary could apply for free — and
/// on the raw shortcut too, where the model never runs.
/// </summary>
/// <remarks>Pure: no I/O, no state. The view decides what to do with the result.</remarks>
public static class SuggestionEngine
{
    /// <summary>Occurrences before a substitution is worth suggesting.</summary>
    public const int Threshold = 3;

    private static readonly char[] WordSeparators = [' ', '\t', '\n', '\r'];
    private static readonly char[] EdgePunctuation = ['.', ',', ';', ':', '!', '?', '(', ')', '"', '«', '»', '-', '*'];

    /// <summary>Substitutions seen at least <see cref="Threshold"/> times, most frequent first.</summary>
    public static IReadOnlyList<DictionarySuggestion> Analyze(IEnumerable<(string Raw, string Cleaned)> pairs)
    {
        var counts = new Dictionary<(string Hear, string Write), int>();
        foreach (var (raw, cleaned) in pairs)
        {
            foreach (var substitution in Substitutions(raw, cleaned))
                counts[substitution] = counts.GetValueOrDefault(substitution) + 1;
        }

        return counts
            .Where(pair => pair.Value >= Threshold)
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new DictionarySuggestion(pair.Key.Hear, pair.Key.Write, pair.Value))
            .ToList();
    }

    // ponytail: two-pointer resync finds single-word substitutions only; the first
    // insertion or deletion abandons the pair. Upgrade to a word-level LCS diff when
    // multi-word substitutions ("cloud code" → "Claude Code") start mattering here.
    private static IEnumerable<(string Hear, string Write)> Substitutions(string raw, string cleaned)
    {
        var rawWords = Tokenize(raw);
        var cleanedWords = Tokenize(cleaned);
        var i = 0;
        var j = 0;

        while (i < rawWords.Length && j < cleanedWords.Length)
        {
            if (Same(rawWords[i], cleanedWords[j]))
            {
                i++;
                j++;
                continue;
            }

            var lastPair = i + 1 == rawWords.Length && j + 1 == cleanedWords.Length;
            var resyncs = i + 1 < rawWords.Length && j + 1 < cleanedWords.Length
                && Same(rawWords[i + 1], cleanedWords[j + 1]);

            if (!lastPair && !resyncs) yield break;   // structure diverged — the rest is unreliable

            yield return (rawWords[i].ToLowerInvariant(), cleanedWords[j]);
            i++;
            j++;
        }
    }

    private static string[] Tokenize(string text) =>
        text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(EdgePunctuation))
            .Where(word => word.Length > 0)
            .ToArray();

    // Case-insensitive: cleanup capitalises sentence starts constantly, and the dictionary
    // corrector matches IgnoreCase anyway — casing-only diffs are noise.
    private static bool Same(string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
}
