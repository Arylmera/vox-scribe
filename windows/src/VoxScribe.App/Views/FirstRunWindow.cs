using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Speech;

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
            // The engine was built with no model at startup; only a restart picks it up.
            ShowStatus("✓ Model downloaded. Restarting Vox-Scribe to load it...");
            _progress.IsVisible = false;
            await Task.Delay(2000);
            (Application.Current as App)?.Restart();
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
        // Infinite timeout: the default 100 s also bounds the body read, and the encoder alone
        // is 650 MB. The Cancel path is the token.
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // Progress<T> captures the UI context here, so Report lands on the UI thread.
        var progress = new Progress<(string File, double Fraction)>(p =>
        {
            _progress.IsIndeterminate = false;
            _progress.Value = p.Fraction * 100;
            ShowStatus($"Downloading {p.File}... {p.Fraction * 100:F0}%");
        });

        await ModelDownloader.DownloadAsync(http, ModelDownloader.Destination, progress, ct);
    }
}
