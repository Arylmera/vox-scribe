namespace VoxScribe.Core;

/// <summary>What an edit produced: the rewrite, or why there is none.</summary>
/// <param name="Text">The text to put in place of the selection; null on failure.</param>
/// <param name="Failure">A short reason, for the pill's notice; null on success.</param>
public sealed record EditOutcome(string? Text, string? Failure);

/// <summary>
/// Applies a spoken instruction to selected text through the cleanup gateway.
/// </summary>
/// <remarks>
/// Unlike <see cref="TextCleaner"/> there is no word-overlap guard — "translate to English"
/// legitimately replaces every word — and no fallback text: on any failure nothing is typed
/// and the selection stays as it was.
/// </remarks>
public sealed class TextEditor(string baseUrl, string model, string? apiKey)
{
    private const string SystemPrompt =
        "You edit text. The user gives an instruction and a text between <<< and >>>. Apply the "
        + "instruction to the text and return only the resulting text: no markers, no quotes, no "
        + "explanation. Keep the text's language unless the instruction asks for another one, and "
        + "keep its line breaks unless the instruction changes them.";

    private readonly Uri _endpoint = new(baseUrl.TrimEnd('/') + "/chat/completions");

    /// <summary>The user message: the instruction, then the text between markers.</summary>
    public static string Request(string selection, string instruction) =>
        $"Instruction: {instruction}\n\nText:\n<<<\n{selection}\n>>>";

    /// <summary>Rewrites <paramref name="selection"/> as <paramref name="instruction"/> says; never throws.</summary>
    public async Task<EditOutcome> EditAsync(string selection, string instruction, CancellationToken cancellationToken)
    {
        var reply = await ChatCompletion.SendAsync(
            _endpoint, model, apiKey, SystemPrompt, Request(selection, instruction),
            // A translation can come out longer than it went in; a token is several characters.
            maxTokens: 256 + selection.Length,
            TextCleaner.TimeoutFor(selection), cancellationToken).ConfigureAwait(false);

        if (reply.Failure is { } failure) return new EditOutcome(null, failure);
        return Vet(reply.Content) is { } edited
            ? new EditOutcome(edited, null)
            : new EditOutcome(null, "the model's answer was unusable");
    }

    /// <summary>
    /// The mechanical guard: null for an empty answer or a leaked reasoning block; strips one
    /// surrounding code fence and echoed markers.
    /// </summary>
    public static string? Vet(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        if (candidate.Contains("<think", StringComparison.OrdinalIgnoreCase)) return null;

        var text = candidate.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal) && text.EndsWith("```", StringComparison.Ordinal) && text.Length > 6)
        {
            var firstBreak = text.IndexOf('\n', StringComparison.Ordinal);
            text = firstBreak < 0 ? string.Empty : text[(firstBreak + 1)..^3].Trim();
        }

        if (text.StartsWith("<<<", StringComparison.Ordinal) && text.EndsWith(">>>", StringComparison.Ordinal))
            text = text[3..^3].Trim();

        return text.Length == 0 ? null : text;
    }
}
