using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoxScribe.Core;

/// <summary>A newer release: its version, its installer, and the installer's published SHA-256.</summary>
public sealed record UpdateInfo(Version Version, Uri Installer, string Sha256);

/// <summary>
/// Finds the latest GitHub release and fetches its installer.
/// </summary>
/// <remarks>
/// The installer is unsigned, so the only check on its bytes is the SHA-256 every release
/// note carries (<c>SHA-256 (installer): `…`</c>). A release without one is not offered:
/// nothing that cannot be verified gets run.
/// </remarks>
public static partial class UpdateChecker
{
    /// <summary>The latest-release endpoint of this repository.</summary>
    public static readonly Uri LatestRelease = new("https://api.github.com/repos/Arylmera/vox-scribe/releases/latest");

    /// <summary>The latest release when it is newer than <paramref name="installed"/>, else null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, Version installed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(installed);

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
        Identify(request);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = json.RootElement;

            if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'), out var latest)) return null;
            if (Normalize(latest) <= Normalize(installed)) return null;

            var hash = HashPattern().Match(root.GetProperty("body").GetString() ?? string.Empty);
            if (!hash.Success) return null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (!name.StartsWith("VoxScribe-Setup-", StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                return new UpdateInfo(
                    latest,
                    new Uri(asset.GetProperty("browser_download_url").GetString()!),
                    hash.Groups[1].Value.ToLowerInvariant());
            }

            return null;
        }
    }

    /// <summary>
    /// Downloads the installer to <paramref name="path"/> and verifies it. On a hash mismatch
    /// the file is deleted and <see cref="InvalidDataException"/> thrown.
    /// </summary>
    public static async Task DownloadAsync(
        HttpClient http, UpdateInfo update, string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(update);

        using var request = new HttpRequestMessage(HttpMethod.Get, update.Installer);
        Identify(request);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var sink = File.Create(path);
            await using (sink.ConfigureAwait(false))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await sink.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    sha.AppendData(buffer, 0, read);
                    written += read;
                    if (total > 0) progress?.Report((double)written / total);
                }
            }
        }

        var actual = Convert.ToHexStringLower(sha.GetHashAndReset());
        if (actual != update.Sha256)
        {
            File.Delete(path);
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"The downloaded installer does not match its published SHA-256 ({actual} ≠ {update.Sha256})."));
        }
    }

    /// <summary>GitHub's API refuses requests without a User-Agent.</summary>
    private static void Identify(HttpRequestMessage request) =>
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("VoxScribe", "1"));

    /// <summary>1.5.3.0 and 1.5.3 are the same release; missing parts count as zero.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    [GeneratedRegex(@"SHA-256[^`]*`([0-9a-fA-F]{64})`")]
    private static partial Regex HashPattern();
}
