using System.IO;
using System.Net;
using System.Net.Http;
using VoxScribe.Speech;
using Shouldly;
using Xunit;

namespace VoxScribe.AppTests;

public sealed class ModelDownloaderTests
{
    [Fact]
    public async Task Downloads_every_required_file_into_a_complete_model()
    {
        var dir = TempDir();
        using var http = new HttpClient(new Serve(_ => new MemoryStream(new byte[1000])));

        await ModelDownloader.DownloadAsync(http, dir, progress: null, CancellationToken.None);

        ParakeetTranscriber.IsComplete(dir).ShouldBeTrue();
        Directory.GetFiles(dir, "*.part").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_download_cut_off_midway_never_leaves_a_file_under_its_real_name()
    {
        var dir = TempDir();
        using var http = new HttpClient(new Serve(name =>
            name == "joiner.int8.onnx" ? new Breaks() : new MemoryStream(new byte[10])));

        await Should.ThrowAsync<IOException>(() =>
            ModelDownloader.DownloadAsync(http, dir, progress: null, CancellationToken.None));

        File.Exists(Path.Combine(dir, "joiner.int8.onnx")).ShouldBeFalse(
            "a truncated file under its real name passes IsComplete and then fails to load");
        ParakeetTranscriber.IsComplete(dir).ShouldBeFalse();
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"vox-model-{Guid.NewGuid():N}");

    private sealed class Serve(Func<string, Stream> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(body(request.RequestUri!.Segments[^1])),
            });
    }

    /// <summary>Delivers some bytes, then fails as a dropped connection does.</summary>
    private sealed class Breaks : MemoryStream
    {
        private bool _sent;

        public Breaks() : base(new byte[100]) { }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_sent) throw new IOException("connection reset");
            _sent = true;
            return base.ReadAsync(buffer, ct);
        }
    }
}
