using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VoxScribe.Core;

/// <summary>
/// Repairs a raw transcript through an OpenAI-compatible chat endpoint — punctuation,
/// capitalisation, filler words.
/// </summary>
/// <remarks>
/// <para>
/// Built for the LiteLLM gateway's <c>local-light</c> alias (Qwen3-4B-2507 on the Mac,
/// non-thinking, 0.15–0.30 s warm). Two properties of that alias are load-bearing and are
/// the reason it was chosen over <c>fast-llm</c>, which shares the same backend: it is
/// <b>not</b> a thinking model, and it has <b>no fallback chain</b>. Dictation is whatever
/// you happen to be typing, and it must not leave the LAN because the Mac went to sleep.
/// If you re-point this at an alias with <c>free-*</c> fallbacks, you have made that
/// decision — make it knowingly.
/// </para>
/// <para>
/// Every failure path returns the original text. A transcript that was not tidied is
/// usable; a transcript that arrives ten seconds late is not.
/// </para>
/// </remarks>
public sealed class TextCleaner
{
    /// <summary>
    /// The contract, not a plea. "Be tidy" is a style instruction a model applies unevenly;
    /// "return only the repaired text" is something <see cref="Accept"/> can verify.
    /// </summary>
    private const string SystemPrompt =
        "You repair dictated speech. Return ONLY the repaired text — no quotes, no preamble, "
      + "no explanation, no code fences. Fix punctuation, capitalisation, spacing and obvious "
      + "speech-recognition slips. Remove filler words and false starts. Keep the speaker's "
      + "own words and their language: never translate, never rephrase, never summarise, "
      + "never answer what the text says, never add anything that was not spoken. If the text "
      + "is already clean, return it unchanged. "
      + "Shape follows what was said. If the dictation enumerates several items, put each on "
      + "its own line starting with \"- \". If it describes ordered steps, number them "
      + "\"1. \", \"2. \". Otherwise return one paragraph on a single line. Never invent "
      + "structure that was not spoken, and never split a single thought into bullets. "
      + "If the speaker asks for a shape — \"en liste\", \"in bullet points\", "
      + "\"as a numbered list\" — obey it and delete the request itself from the output. It "
      + "is an instruction to you, not part of the text.";

    /// <summary>
    /// Shared, and the bearer travels per request rather than on the client's default headers.
    /// This class is created and dropped with the engine, and one owned <see cref="HttpClient"/>
    /// per instance would be a disposable to thread through the composition for no gain.
    /// </summary>
    /// <remarks>
    /// The timeout is short on purpose: it sits between the key release and the text
    /// appearing, and a slow answer is worse than no answer.
    /// </remarks>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;

    /// <param name="baseUrl">API base, e.g. <c>http://192.168.1.100:4000/v1</c>.</param>
    /// <param name="model">Alias the gateway routes on, e.g. <c>local-light</c>.</param>
    /// <param name="apiKey">Bearer key, or null for unauthenticated endpoints.</param>
    public TextCleaner(string baseUrl, string model, string? apiKey)
    {
        _endpoint = new Uri(baseUrl.TrimEnd('/') + "/chat/completions");
        _model = model;
        _apiKey = apiKey;
    }

    /// <summary>Repairs <paramref name="text"/>, or returns it unchanged on any failure.</summary>
    public async Task<string> CleanAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Built as a node tree rather than serialised from an anonymous type: the app is
        // published single-file and trimmed, and reflection-based serialisation does not
        // survive that (IL2026).
        var body = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0,
            // Enough for the repaired text and nothing more. A model that starts explaining
            // itself gets cut off, and Accept() then throws the fragment away.
            ["max_tokens"] = 64 + (text.Length / 2),
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = text }),
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(_apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await Http
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return text;

            var answer = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            return Accept(text, Content(answer));
        }
        catch (Exception)
        {
            // Unreachable gateway, sleeping Mac, timeout, malformed body — all one outcome.
            return text;
        }
    }

    /// <summary>Pulls <c>choices[0].message.content</c>, or null if the body is not that shape.</summary>
    public static string? Content(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The mechanical guard: takes <paramref name="candidate"/> only when it still looks like
    /// a repair of <paramref name="original"/>, and falls back to the original otherwise.
    /// </summary>
    /// <remarks>
    /// Deterministic and independent of the prompt, because a model forgets. This is what
    /// stops a chatty answer, a leaked reasoning block or a wholesale rewrite from being
    /// typed into whatever window has focus.
    /// </remarks>
    public static string Accept(string original, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return original;

        var trimmed = candidate.Trim();

        if (trimmed.Contains("```", StringComparison.Ordinal)) return original;
        if (trimmed.Contains("<think", StringComparison.OrdinalIgnoreCase)) return original;

        // Line breaks are allowed: a spoken enumeration is meant to come back as a list. What
        // is not allowed is inventing words, and that is what the overlap rules below check.

        // Bullets and numbering add a few characters per line; dropped filler removes some.
        // Beyond this the model is answering or padding, not laying out.
        if (trimmed.Length < original.Length * 0.5 || trimmed.Length > original.Length * 2.2)
            return original;

        // The invariant, now that layout may change: the same words, arranged differently.
        // Checked both ways, because each direction catches a different lie.
        //
        //   forward  — how much of what was said survived. Low means summarised or answered:
        //              "the build passes, good news!" is the same length and shares almost
        //              nothing.
        //   backward — how much of the answer was actually said. Low means invented, padded,
        //              or prefixed with "Here is the corrected text:".
        //
        // Markers are separators, not words, so bullets and numbering cost nothing here.
        if (Overlap(original, trimmed) < 0.6) return original;
        if (Overlap(trimmed, original) < 0.6) return original;

        return trimmed;
    }

    /// <summary>
    /// Share of <paramref name="original"/>'s content words that also appear in
    /// <paramref name="candidate"/>, 0…1.
    /// </summary>
    /// <remarks>
    /// Words of three characters or more only, so dropped filler ("euh", "bah") and the
    /// articles a repair may re-punctuate around do not count against it. Not symmetric —
    /// callers check it in both directions.
    /// </remarks>
    private static double Overlap(string original, string candidate)
    {
        var kept = new HashSet<string>(Words(candidate), StringComparer.OrdinalIgnoreCase);
        var wanted = Words(original).ToArray();
        if (wanted.Length == 0) return 1;

        return (double)wanted.Count(kept.Contains) / wanted.Length;
    }

    private static IEnumerable<string> Words(string text) =>
        text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3);

    /// <remarks>
    /// <c>-</c>, <c>*</c> and <c>.</c> are separators, so a bullet marker and the "1." of a
    /// numbered item are not words. That is what lets a re-laid-out list score as the same
    /// text it came from.
    /// </remarks>
    private static readonly char[] WordSeparators =
        [' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '\'', '"', '(', ')', '-', '*', '…'];
}
