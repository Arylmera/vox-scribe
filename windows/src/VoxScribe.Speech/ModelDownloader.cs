using VoxScribe.Abstractions;

namespace VoxScribe.Speech;

/// <summary>
/// Fetches the Parakeet model files into the folder <see cref="ParakeetTranscriber"/> searches.
/// </summary>
/// <remarks>
/// v3 rather than v2: same size and speed, and it covers 25 European languages instead of
/// English only — the French spoken-punctuation path is pointless on an English-only model.
/// </remarks>
public static class ModelDownloader
{
    /// <summary>Where the files are fetched from; each required file sits directly under it.</summary>
    public const string BaseUrl =
        "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/";

    /// <summary>The folder the model is downloaded into — first in the search order.</summary>
    public static string Destination => DataDirectory.File("models", "parakeet-v3");

    /// <summary>Downloads every missing required file into <paramref name="directory"/>.</summary>
    /// <param name="http">Its timeout must cover a 650 MB body; pass one with an infinite timeout.</param>
    /// <param name="directory">Created if absent.</param>
    /// <param name="progress">(file name, 0–1 within that file), when the size is known.</param>
    /// <param name="cancellationToken">Cancels the download; finished files are kept.</param>
    /// <remarks>
    /// Each file is written to <c>.part</c> and renamed only once complete.
    /// <see cref="ParakeetTranscriber.IsComplete"/> checks existence, not size, so a truncated
    /// encoder under its real name would pass and then fail to load with an opaque protobuf
    /// error.
    /// </remarks>
    public static async Task DownloadAsync(
        HttpClient http,
        string directory,
        IProgress<(string File, double Fraction)>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);

        foreach (var name in ParakeetTranscriber.RequiredFiles)
        {
            var target = Path.Combine(directory, name);
            if (File.Exists(target)) continue;

            var part = target + ".part";
            using (var response = await http.GetAsync(
                new Uri(BaseUrl + name), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1;

                var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (source.ConfigureAwait(false))
                {
                    var sink = File.Create(part);
                    await using (sink.ConfigureAwait(false))
                    {
                        var buffer = new byte[81920];
                        long written = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await sink.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                            written += read;
                            if (total > 0) progress?.Report((name, (double)written / total));
                        }
                    }
                }
            }

            File.Move(part, target, overwrite: true);
        }
    }
}
