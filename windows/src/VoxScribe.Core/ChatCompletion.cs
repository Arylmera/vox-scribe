using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace VoxScribe.Core;

/// <summary>What one chat call produced: the reply's content, or why there is none.</summary>
/// <param name="Content"><c>choices[0].message.content</c>; null when the body was not that shape.</param>
/// <param name="Failure">Why the call failed, without a prefix; null when the gateway answered 2xx.</param>
internal readonly record struct ChatReply(string? Content, string? Failure);

/// <summary>One OpenAI-compatible <c>/chat/completions</c> call, shared by cleanup and edit.</summary>
internal static class ChatCompletion
{
    /// <summary>
    /// Shared, and the bearer travels per request rather than on the client's default headers.
    /// This class is created and dropped with the engine, and one owned <see cref="HttpClient"/>
    /// per instance would be a disposable to thread through the composition for no gain.
    /// </summary>
    /// <remarks>
    /// No client-wide timeout: each request gets its own, from the caller's <c>timeout</c>.
    /// </remarks>
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Sends <paramref name="system"/> + <paramref name="user"/>; never throws.</summary>
    public static async Task<ChatReply> SendAsync(
        Uri endpoint, string model, string? apiKey, string system, string user,
        int maxTokens, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // Built as a node tree rather than serialised from an anonymous type: the app is
        // published single-file and trimmed, and reflection-based serialisation does not
        // survive that (IL2026).
        var body = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = 0,
            ["max_tokens"] = maxTokens,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
        };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await Http
                .SendAsync(request, linked.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new ChatReply(null, $"gateway answered {(int)response.StatusCode}");

            var answer = await response.Content
                .ReadAsStringAsync(linked.Token)
                .ConfigureAwait(false);
            return new ChatReply(TextCleaner.Content(answer), null);
        }
        catch (Exception e)
        {
            // Unreachable gateway, sleeping Mac, timeout, malformed body — all one outcome
            // for the caller, but each worth a line in the log.
            return new ChatReply(null, e is TaskCanceledException
                ? "gateway did not answer in time"
                : e.Message);
        }
    }
}
