using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Text.Json;
using Murmur.Abstractions;

namespace Murmur.Speech;

/// <summary>
/// OpenAI-compatible remote transcription — POSTs a WAV to <c>/v1/audio/transcriptions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Built for a LiteLLM gateway fronting a Mac MLX Parakeet server (~250 ms warm over LAN),
/// but speaks the plain OpenAI audio API so any compatible backend works. Model-name routing
/// and Mac→NAS fallback are the gateway's job, not this class's.
/// </para>
/// <para>
/// Network failure returns an empty transcript rather than throwing: dictation should degrade
/// to "nothing was typed", never to a crashed engine. The bias phrases are
/// accepted and ignored — the dictionary correction pass is the guarantee, as with the local
/// engine.
/// </para>
/// </remarks>
public sealed class RemoteTranscriber : ITranscriber
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _model;

    /// <param name="baseUrl">API base, e.g. <c>http://192.168.1.100:4000/v1</c>.</param>
    /// <param name="model">Model name the gateway routes on, e.g. <c>stt-mac</c>.</param>
    /// <param name="apiKey">Bearer key, or null for unauthenticated endpoints.</param>
    public RemoteTranscriber(string baseUrl, string model, string? apiKey)
    {
        _endpoint = new Uri(baseUrl.TrimEnd('/') + "/audio/transcriptions");
        _model = model;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <inheritdoc />
    /// <remarks>Always ready: there is no model to load, and the network is probed per
    /// utterance — a dead link at startup says nothing about the link at dictation time.</remarks>
    public bool IsReady => true;

    /// <inheritdoc />
    public ValueTask<bool> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

    /// <inheritdoc />
    public async ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        IReadOnlyList<string> biasPhrases,
        CancellationToken cancellationToken)
    {
        if (samples.Length == 0) return string.Empty;

        using var content = new MultipartFormDataContent();
        var wav = new ByteArrayContent(ToWav(samples.Span));
        wav.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(wav, "file", "utterance.wav");
        content.Add(new StringContent(_model), "model");

        try
        {
            using var response = await _http.PostAsync(_endpoint, content, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return string.Empty;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return json.RootElement.TryGetProperty("text", out var text)
                ? text.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }
        catch (HttpRequestException) { return string.Empty; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return string.Empty; // HttpClient timeout, not a user cancel
        }
    }

    /// <summary>Encodes float samples as a 16-bit PCM mono WAV at <see cref="AudioChunk.SampleRate"/>.</summary>
    private static byte[] ToWav(ReadOnlySpan<float> samples)
    {
        const int headerSize = 44;
        var data = new byte[headerSize + samples.Length * 2];
        var span = data.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], data.Length - 8);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);            // fmt chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);             // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);             // mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], AudioChunk.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], AudioChunk.SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);             // block align
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);            // bits per sample
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], samples.Length * 2);

        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(
                span[(headerSize + i * 2)..], (short)(clamped * short.MaxValue));
        }

        return data;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
