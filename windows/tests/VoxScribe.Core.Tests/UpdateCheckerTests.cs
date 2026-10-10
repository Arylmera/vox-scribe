using System.Net;
using System.Security.Cryptography;
using System.Text;
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>The latest GitHub release is offered only when newer, and only installed when its bytes match.</summary>
public sealed class UpdateCheckerTests
{
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("pretend this is an installer");
    private static readonly string InstallerHash = Convert.ToHexStringLower(SHA256.HashData(Installer));

    private static string Release(string tag, string? hash = null, string asset = "VoxScribe-Setup-1.6.0.exe") => $$"""
        {
          "tag_name": "{{tag}}",
          "body": "- Something new.\n\nSHA-256 (installer): `{{hash ?? InstallerHash}}`",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.test/notes.txt" },
            { "name": "{{asset}}", "browser_download_url": "https://example.test/{{asset}}" }
          ]
        }
        """;

    private static HttpClient Serving(string json, byte[]? installer = null) =>
        new(new Handler(json, installer ?? Installer));

    [Fact]
    public async Task A_newer_release_is_offered_with_its_installer_and_hash()
    {
        using var http = Serving(Release("v1.6.0"));

        var update = await UpdateChecker.CheckAsync(http, new Version(1, 5, 3), CancellationToken.None);

        update.ShouldNotBeNull();
        update.Version.ShouldBe(new Version(1, 6, 0));
        update.Installer.ShouldBe(new Uri("https://example.test/VoxScribe-Setup-1.6.0.exe"));
        update.Sha256.ShouldBe(InstallerHash);
    }

    [Theory]
    [InlineData("v1.5.3")]
    [InlineData("v1.5.2")]
    public async Task The_same_or_an_older_release_is_not_offered(string tag)
    {
        using var http = Serving(Release(tag));

        (await UpdateChecker.CheckAsync(http, new Version(1, 5, 3), CancellationToken.None)).ShouldBeNull();
    }

    /// <summary>Assembly versions carry a fourth part (1.5.3.0); it must not make 1.5.3 look older.</summary>
    [Fact]
    public async Task A_four_part_installed_version_compares_as_three()
    {
        using var http = Serving(Release("v1.5.3"));

        (await UpdateChecker.CheckAsync(http, new Version(1, 5, 3, 0), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_release_without_an_installer_is_not_offered()
    {
        using var http = Serving(Release("v1.6.0", asset: "source.zip"));

        (await UpdateChecker.CheckAsync(http, new Version(1, 5, 3), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task The_download_lands_when_its_hash_matches()
    {
        using var http = Serving(Release("v1.6.0"));
        var update = await UpdateChecker.CheckAsync(http, new Version(1, 5, 3), CancellationToken.None);
        var path = Path.Combine(Path.GetTempPath(), $"vox-update-{Guid.NewGuid():N}.exe");

        try
        {
            await UpdateChecker.DownloadAsync(http, update!, path, null, CancellationToken.None);
            File.ReadAllBytes(path).ShouldBe(Installer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A truncated or tampered installer must never be run.</summary>
    [Fact]
    public async Task A_download_whose_hash_differs_is_refused_and_removed()
    {
        using var http = Serving(Release("v1.6.0", hash: new string('0', 64)));
        var update = await UpdateChecker.CheckAsync(http, new Version(1, 5, 3), CancellationToken.None);
        var path = Path.Combine(Path.GetTempPath(), $"vox-update-{Guid.NewGuid():N}.exe");

        await Should.ThrowAsync<InvalidDataException>(
            () => UpdateChecker.DownloadAsync(http, update!, path, null, CancellationToken.None));
        File.Exists(path).ShouldBeFalse();
    }

    /// <summary>Without a published hash there is nothing to verify against, so nothing is offered.</summary>
    [Fact]
    public async Task A_release_without_a_hash_is_not_offered()
    {
        using var http = Serving(Release("v1.6.0").Replace("SHA-256", "Checksum", StringComparison.Ordinal));

        (await UpdateChecker.CheckAsync(http, new Version(1, 5, 3), CancellationToken.None)).ShouldBeNull();
    }

    private sealed class Handler(string json, byte[] installer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.UserAgent.ShouldNotBeEmpty("GitHub's API refuses requests without a User-Agent");
            var content = request.RequestUri!.Host == "api.github.com"
                ? new StringContent(json, Encoding.UTF8, "application/json")
                : (HttpContent)new ByteArrayContent(installer);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
