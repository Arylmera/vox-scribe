using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;

namespace VoxScribe.App.Views;

/// <summary>
/// First-run window: offers to download the Parakeet speech model if it's not already present.
/// </summary>
public sealed class FirstRunWindow : Window, IAsyncDisposable
{
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly Button _downloadBtn;
    private readonly Button _skipBtn;
    private volatile CancellationTokenSource? _downloadCts;

    /// <summary>Creates the first-run setup window.</summary>
    public FirstRunWindow()
    {
        Title = "Vox-Scribe — Setup";
        Width = 480;
        Height = 240;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Tokens.Brushes.Chassis;

        _status = new TextBlock
        {
            Foreground = Tokens.Brushes.Ink,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Tokens.Space.Panel),
        };

        _progress = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 4,
            Margin = new Thickness(Tokens.Space.Panel, 0),
            IsVisible = false,
        };

        _downloadBtn = new Button { Content = "DOWNLOAD MODEL" };
        _skipBtn = new Button { Content = "SKIP FOR NOW" };

        _downloadBtn.Click += OnDownload;
        _skipBtn.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Margin = new Thickness(Tokens.Space.Panel),
            Children =
            {
                new Silkscreen { Text = "Speech Model Setup", IsLarge = true },
                new TextBlock
                {
                    Text = "Vox-Scribe uses a local speech model for dictation. Download it now (661 MB) or you can do this later from the command line.",
                    Foreground = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Muted),
                    TextWrapping = TextWrapping.Wrap,
                },
                _status,
                _progress,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = Tokens.Space.Base,
                    Margin = new Thickness(0, Tokens.Space.Base, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { _skipBtn, _downloadBtn },
                },
            },
        };

        ShowStatus("Download the Parakeet speech model to enable transcription.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
    }

    private async void OnDownload(object? sender, RoutedEventArgs e)
    {
        _downloadBtn.IsEnabled = false;
        _skipBtn.IsEnabled = false;
        _progress.IsVisible = true;
        _progress.IsIndeterminate = true;
        ShowStatus("Downloading model...");

        _downloadCts = new CancellationTokenSource();

        try
        {
            await DownloadModel(_downloadCts.Token);
            ShowStatus("✓ Model downloaded successfully. You can now use Vox-Scribe for dictation.");
            _progress.IsVisible = false;
            await Task.Delay(2000);
            Close();
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Download cancelled.");
            _downloadBtn.IsEnabled = true;
            _skipBtn.IsEnabled = true;
            _progress.IsVisible = false;
        }
        catch (Exception ex)
        {
            ShowStatus($"Download failed: {ex.Message}. You can download manually from the docs or try again later.");
            _downloadBtn.IsEnabled = true;
            _skipBtn.IsEnabled = true;
            _progress.IsVisible = false;
        }
    }

    private async Task DownloadModel(CancellationToken ct)
    {
        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxScribe", "model");
        var modelPath = Path.Combine(modelDir, "parakeet.onnx");

        // ponytail: hardcoded URL for now. In production, use a config/updates API.
        const string modelUrl = "https://huggingface.co/espnet/espnet-model/resolve/main/models/eng/parakeet/parakeet.onnx";

        if (File.Exists(modelPath))
        {
            ShowStatus("✓ Model already present.");
            return;
        }

        Directory.CreateDirectory(modelDir);

        using var http = new HttpClient();
        using var response = await http.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = File.Create(modelPath);

        var buffer = new byte[8192];
        var downloadedBytes = 0L;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(new Memory<byte>(buffer), ct).ConfigureAwait(false)) != 0)
        {
            await fileStream.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead), ct).ConfigureAwait(false);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                var percent = (double)downloadedBytes / totalBytes;
                _progress.IsIndeterminate = false;
                _progress.Value = percent * 100;
                ShowStatus($"Downloading... {(percent * 100):F0}%");
            }
        }
    }
}
