using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;
using VoxScribe.Speech;

namespace VoxScribe.App.Views.Settings;

/// <summary>Everything that produces the transcript: microphone, local model, remote gateway.</summary>
internal static class SpeechSection
{
    /// <summary>Builds the section.</summary>
    public static Control Build(AppSettings settings, Action<SettingsData> save) =>
        Panels.Section("SPEECH", new StackPanel
        {
            Spacing = Tokens.Space.Base,
            Children =
            {
                Panels.Labelled("MICROPHONE", Microphone(settings, save)),
                Panels.Labelled("MODEL", Model()),
                Panels.Labelled("REMOTE SERVER", Remote(settings, save)),
            },
        });

    private static StackPanel Microphone(AppSettings settings, Action<SettingsData> save)
    {
        // First entry is the system default; real devices follow. Tag carries the MMDevice ID
        // (null for default) so the display string never has to be parsed back.
        var choices = new List<ComboBoxItem>
        {
            new() { Content = "System default (communications device)", Tag = null },
        };
        choices.AddRange(PlatformFactory.ListCaptureDevices()
            .Select(d => new ComboBoxItem { Content = d.Value, Tag = d.Key }));

        var picker = new ComboBox
        {
            ItemsSource = choices,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
        };

        var saved = settings.Data.AudioDeviceId;
        picker.SelectedItem =
            choices.FirstOrDefault(c => Equals(c.Tag, saved)) ?? choices[0];

        picker.SelectionChanged += (_, _) =>
        {
            var id = (picker.SelectedItem as ComboBoxItem)?.Tag as string;
            if (id != settings.Data.AudioDeviceId)
                save(settings.Data with { AudioDeviceId = id });
        };

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                picker,
                Panels.Note("Which microphone to record from. Takes effect the next time Vox-Scribe starts."),
            },
        };
    }

    private static StackPanel Model()
    {
        var located = ParakeetTranscriber.Locate();
        var found = located is not null;

        var status = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children =
            {
                new Lamp
                {
                    IsLit = found,
                    LampColor = found ? Tokens.Colors.Positive : Tokens.Colors.Caution,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = found ? "Parakeet ready" : "Model not installed",
                    FontFamily = Tokens.Fonts.Grotesque,
                    FontSize = Tokens.Fonts.Body,
                    Foreground = Tokens.Brushes.Ink,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

        // Showing the resolved path matters: "model not found" is unactionable without
        // knowing which directory was actually checked.
        if (found)
        {
            return new StackPanel { Spacing = Tokens.Space.Snug, Children = { status, Panels.Note($"Loaded from {located}") } };
        }

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                status,
                Panels.Note("Optional. Parakeet transcribes on this PC's processor, with no server: "
                    + "download it once (about 661 MB). Not needed when a remote server is set below."),
                Download(),
            },
        };
    }

    /// <summary>DOWNLOAD MODEL, which turns into CANCEL while it runs, and its progress.</summary>
    /// <remarks>The engine is built at startup, so a finished download restarts the app.</remarks>
    private static StackPanel Download()
    {
        var progress = Panels.Note("");
        progress.VerticalAlignment = VerticalAlignment.Center;
        var button = new TransportKey { Content = "DOWNLOAD MODEL" };
        CancellationTokenSource? running = null;

        button.Click += async (_, _) =>
        {
            if (running is not null)
            {
                await running.CancelAsync();
                return;
            }

            running = new CancellationTokenSource();
            button.Content = "CANCEL";
            try
            {
                // Infinite timeout: the default 100 s also bounds the body read, and the
                // encoder alone is 650 MB. Cancel is the token.
                using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

                // Progress<T> captures the UI context here, so Report lands on the UI thread.
                var report = new Progress<(string File, double Fraction)>(p =>
                    progress.Text = $"Downloading {p.File}… {p.Fraction * 100:F0}%");
                await ModelDownloader.DownloadAsync(http, ModelDownloader.Destination, report, running.Token);

                progress.Text = "Downloaded. Restarting Vox-Scribe to load it…";
                await Task.Delay(Tokens.Motion.StatusHold);
                (Application.Current as App)?.Restart();
                return;
            }
            catch (OperationCanceledException)
            {
                progress.Text = "Download cancelled. Finished files are kept.";
            }
            catch (Exception e)
            {
                progress.Text = $"Download failed: {e.Message}";
            }

            running.Dispose();
            running = null;
            button.Content = "DOWNLOAD MODEL";
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children = { button, progress },
        };
    }

    private static StackPanel Remote(AppSettings settings, Action<SettingsData> save)
    {
        var endpoint = Panels.Field("http://192.168.1.100:4000/v1  (empty = transcribe locally)",
            settings.Data.SttEndpoint,
            v => save(settings.Data with { SttEndpoint = v }));
        var model = Panels.Field("Model name the gateway routes on",
            settings.Data.SttModel,
            v => save(settings.Data with { SttModel = v ?? "stt-mac" }));
        var apiKey = Panels.Field("API key (empty = unauthenticated)",
            settings.Data.SttApiKey,
            v => save(settings.Data with { SttApiKey = v }));
        apiKey.PasswordChar = '•';

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Panels.Note("OpenAI-compatible transcription endpoint (e.g. a LiteLLM gateway). "
                   + "When set, it is used instead of the local model."),
                Panels.Labelled("ENDPOINT", endpoint),
                Panels.Labelled("MODEL", model),
                Panels.Labelled("API KEY", apiKey),
                ConnectionTester.Build(() => (
                    settings.Data.SttEndpoint, settings.Data.SttModel, settings.Data.SttApiKey)),
                Panels.Note("Takes effect the next time Vox-Scribe starts."),
            },
        };
    }
}
