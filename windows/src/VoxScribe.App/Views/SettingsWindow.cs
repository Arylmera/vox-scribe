using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.Core;
using VoxScribe.Speech;

namespace VoxScribe.App.Views;

/// <summary>Settings: the hotkey and the model.</summary>
public sealed class SettingsWindow : Window
{
    /// <summary>Escape cancels a recording rather than becoming the trigger.</summary>
    private const int VkEscape = 0x1B;

    /// <summary>Right Alt is AltGr on many European layouts; warn rather than forbid.</summary>
    private const int VkRightAlt = 0xA5;

    private readonly AppSettings _settings;
    private readonly TransportKey _hotkeyButton;
    private readonly TransportKey _cleanupHotkeyButton;
    private readonly TextBlock _keyWarning;

    /// <summary>Which shortcut the live recorder is binding.</summary>
    private bool _recordingCleanupChord;

    /// <summary>The live recorder hook, non-null only while recording.</summary>
    private IDisposable? _recorder;

    /// <summary>Chord members seen so far this recording, in press order.</summary>
    private readonly List<int> _captured = [];

    /// <summary>Chord members currently held; recording ends when this empties.</summary>
    private readonly HashSet<int> _held = [];

    /// <summary>Builds the settings window.</summary>
    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;

        Title = "Vox-Scribe Settings";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = Tokens.Brushes.Chassis;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _hotkeyButton = new TransportKey { EngagedColor = Tokens.Colors.Ink };
        _hotkeyButton.Click += (_, _) =>
        {
            if (_recorder is null) StartRecording(cleanup: false); else CancelRecording();
        };

        _cleanupHotkeyButton = new TransportKey { EngagedColor = Tokens.Colors.Ink };
        _cleanupHotkeyButton.Click += (_, _) =>
        {
            if (_recorder is null) StartRecording(cleanup: true); else CancelRecording();
        };

        _keyWarning = new TextBlock
        {
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Label,
            Foreground = new SolidColorBrush(Tokens.Colors.MeterAmber),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        Content = BuildContent();
        ShowChord(_settings.Data.ResolvedPushToTalkKeys);
        ShowCleanupChord(_settings.Data.CleanupPushToTalkKeys);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        CancelRecording();
        base.OnClosed(e);
    }

    private void StartRecording(bool cleanup)
    {
        _recordingCleanupChord = cleanup;
        _captured.Clear();
        _held.Clear();

        // Events arrive on the hook thread; every touch of the UI below is posted. Same
        // lesson as the transcriptions view: off-thread Avalonia access fails silently.
        _recorder = PlatformFactory.StartKeyCapture((key, isDown) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnRecordedKey(key, isDown)));

        if (_recorder is null) return; // off Windows, or the hook failed to install

        Recording.IsEngaged = true;
        Recording.Content = "PRESS YOUR KEY(S)…";
    }

    private void OnRecordedKey(int key, bool isDown)
    {
        if (_recorder is null) return;

        if (isDown)
        {
            if (key == VkEscape && _captured.Count == 0)
            {
                // On the cleanup slot Escape means "unbind" rather than "cancel": with no
                // other gesture available, there would otherwise be no way back to a single
                // shortcut once one is recorded.
                if (_recordingCleanupChord)
                {
                    CancelRecording();
                    Save(_settings.Data with { CleanupPushToTalkKeys = null });
                    ShowCleanupChord(null);
                    return;
                }

                CancelRecording();
                return;
            }

            if (!_captured.Contains(key)) _captured.Add(key);
            _held.Add(key);
            Recording.Content = ChordLabel(_captured);
            return;
        }

        _held.Remove(key);

        // The chord is whatever was held together; the last release commits it.
        if (_captured.Count > 0 && _held.Count == 0) CommitRecording();
    }

    /// <summary>The button the live recording is writing into.</summary>
    private TransportKey Recording => _recordingCleanupChord ? _cleanupHotkeyButton : _hotkeyButton;

    private void CommitRecording()
    {
        var chord = _captured.ToArray();
        var cleanup = _recordingCleanupChord;
        CancelRecording();

        if (cleanup)
        {
            Save(_settings.Data with { CleanupPushToTalkKeys = chord });
            ShowCleanupChord(chord);
            return;
        }

        Save(_settings.Data with { PushToTalkKeys = chord, PushToTalkKey = chord[0] });
        ShowChord(chord);
    }

    private void CancelRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        _hotkeyButton.IsEngaged = false;
        _cleanupHotkeyButton.IsEngaged = false;
        ShowChord(_settings.Data.ResolvedPushToTalkKeys);
        ShowCleanupChord(_settings.Data.CleanupPushToTalkKeys);
    }

    private void ShowCleanupChord(int[]? chord) =>
        _cleanupHotkeyButton.Content = chord is { Length: > 0 }
            ? ChordLabel(chord)
            : "NOT BOUND";

    private void ShowChord(int[] chord)
    {
        _hotkeyButton.Content = ChordLabel(chord);

        var altGr = chord.Contains(VkRightAlt);
        _keyWarning.Text = altGr
            ? "Right Alt is AltGr on many European layouts — binding it here will interfere "
            + "with typing @, €, \\ and |."
            : string.Empty;
        _keyWarning.IsVisible = altGr;
    }

    private static string ChordLabel(IReadOnlyList<int> chord) =>
        string.Join(" + ", chord.Select(PlatformFactory.KeyDisplayName));

    private StackPanel BuildContent() => new StackPanel
    {
        Margin = new Thickness(Tokens.Space.Panel),
        Spacing = Tokens.Space.Wide,
        Children =
        {
            Section("PUSH TO TALK", new StackPanel
            {
                Spacing = Tokens.Space.Snug,
                Children =
                {
                    _hotkeyButton,
                    _keyWarning,
                    Note("Click, then press the key — or hold several keys together for a "
                       + "combination; releasing them records it. Escape cancels. The new "
                       + "shortcut works immediately: hold it anywhere to dictate."),
                    _cleanupHotkeyButton,
                    Note("Second shortcut. It records the same way, but sends the "
                       + "transcript through the cleanup model before typing it. The first "
                       + "shortcut stays raw and fast. Escape on this one unbinds it. "
                       + "Binding it for the first time needs a restart."),
                    Toggle("Toggle mode — press once to start, press again to stop",
                        _settings.Data.PushToTalkToggle,
                        v => Save(_settings.Data with { PushToTalkToggle = v })),
                },
            }),

            Section("APPEARANCE", BuildAppearanceSection()),

            Section("MICROPHONE", BuildMicrophoneSection()),

            Section("MODEL", BuildModelSection()),

            Section("REMOTE SERVER", BuildRemoteSection()),

            Section("CLEANUP", BuildCleanupSection()),

            Section("BEHAVIOUR", new StackPanel
            {
                Spacing = Tokens.Space.Snug,
                Children =
                {
                    Toggle("Type transcripts into the focused app", _settings.Data.InjectText,
                        v => Save(_settings.Data with { InjectText = v })),
                    Toggle("Type each phrase as you speak it, not all at the end (raw only)",
                        _settings.Data.IncrementalInjection,
                        v => Save(_settings.Data with { IncrementalInjection = v })),
                    Toggle("Keep a transcript history", _settings.Data.KeepHistory,
                        v => Save(_settings.Data with { KeepHistory = v })),
                    Toggle("Start Vox-Scribe when I log in, minimised to the tray",
                        PlatformFactory.IsLaunchAtLoginEnabled(),
                        PlatformFactory.SetLaunchAtLogin),
                },
            }),
        },
    };

    /// <summary>The curated accent swatches — Void Glass cyan first, its default.</summary>
    private static readonly string[] AccentChoices =
        ["#4FD8E8", "#5A8CF5", "#4FE8A0", "#F06AD8", "#E8B44F"];

    private StackPanel BuildAppearanceSection()
    {
        var dots = new List<(string Hex, Border Dot)>();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Space.Base };
        foreach (var hex in AccentChoices)
        {
            var dot = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderThickness = new Thickness(2),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            dot.PointerPressed += (_, _) =>
            {
                Save(_settings.Data with { AccentColor = hex });
                MarkSelectedAccent(dots);
            };
            dots.Add((hex, dot));
            row.Children.Add(dot);
        }

        MarkSelectedAccent(dots);

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                row,
                Note("Accent colour — tints the dictation pill and highlights. Applies immediately."),
            },
        };
    }

    /// <summary>Rings the swatch matching the saved accent; clears the others.</summary>
    private void MarkSelectedAccent(List<(string Hex, Border Dot)> dots)
    {
        foreach (var (hex, dot) in dots)
            dot.BorderBrush = string.Equals(hex, _settings.Data.AccentColor, StringComparison.OrdinalIgnoreCase)
                ? Tokens.Brushes.Ink
                : Avalonia.Media.Brushes.Transparent;
    }

    private StackPanel BuildMicrophoneSection()
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

        var saved = _settings.Data.AudioDeviceId;
        picker.SelectedItem =
            choices.FirstOrDefault(c => Equals(c.Tag, saved)) ?? choices[0];

        picker.SelectionChanged += (_, _) =>
        {
            var id = (picker.SelectedItem as ComboBoxItem)?.Tag as string;
            if (id != _settings.Data.AudioDeviceId)
                Save(_settings.Data with { AudioDeviceId = id });
        };

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                picker,
                Note("Which microphone to record from. Takes effect the next time Vox-Scribe starts."),
            },
        };
    }

    private StackPanel BuildRemoteSection()
    {
        var endpoint = Field("http://192.168.1.100:4000/v1  (empty = transcribe locally)",
            _settings.Data.SttEndpoint,
            v => Save(_settings.Data with { SttEndpoint = v }));
        var model = Field("Model name the gateway routes on",
            _settings.Data.SttModel,
            v => Save(_settings.Data with { SttModel = v ?? "stt-mac" }));
        var apiKey = Field("API key (empty = unauthenticated)",
            _settings.Data.SttApiKey,
            v => Save(_settings.Data with { SttApiKey = v }));
        apiKey.PasswordChar = '•';

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Note("OpenAI-compatible transcription endpoint (e.g. a LiteLLM gateway). "
                   + "When set, it is used instead of the local model."),
                Labeled("ENDPOINT", endpoint),
                Labeled("MODEL", model),
                Labeled("API KEY", apiKey),
                ConnectionTester(() => (
                    _settings.Data.SttEndpoint, _settings.Data.SttModel, _settings.Data.SttApiKey)),
                Note("Takes effect the next time Vox-Scribe starts."),
            },
        };
    }

    private StackPanel BuildCleanupSection()
    {
        var endpoint = Field("http://192.168.1.100:4000/v1  (empty = type it as transcribed)",
            _settings.Data.CleanupEndpoint,
            v => Save(_settings.Data with { CleanupEndpoint = v }));
        var model = Field("Alias the gateway routes on",
            _settings.Data.CleanupModel,
            v => Save(_settings.Data with { CleanupModel = v ?? "local-light" }));
        var apiKey = Field("API key (empty = unauthenticated)",
            _settings.Data.CleanupApiKey,
            v => Save(_settings.Data with { CleanupApiKey = v }));
        apiKey.PasswordChar = '•';

        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                Note("Sends the finished transcript to a small language model to fix "
                   + "punctuation, capitalisation and filler words before it is typed. "
                   + "Costs one LAN round trip; an unreachable model types the raw text."),
                Labeled("ENDPOINT", endpoint),
                Labeled("MODEL", model),
                Labeled("API KEY", apiKey),
                ConnectionTester(() => (
                    _settings.Data.CleanupEndpoint,
                    _settings.Data.CleanupModel,
                    _settings.Data.CleanupApiKey)),
                Note("Overrides \"type each phrase as you speak it\": a tidied dictation is "
                   + "always typed once, at the end, because text already in the target "
                   + "window cannot be repaired. Takes effect the next time VoxScribe starts."),
            },
        };
    }

    /// <summary>
    /// The TEST CONNECTION row: button, lamp and verdict, reading its endpoint fresh on each
    /// click so both the transcription and cleanup sections can share one implementation.
    /// </summary>
    private static StackPanel ConnectionTester(Func<(string? Endpoint, string Model, string? Key)> read)
    {
        var lamp = new Lamp { VerticalAlignment = VerticalAlignment.Center };
        var status = Note("Not tested yet.");
        status.VerticalAlignment = VerticalAlignment.Center;

        var test = new TransportKey { Content = "TEST CONNECTION", EngagedColor = Tokens.Colors.Ink };
        test.Click += async (_, _) =>
        {
            // Clicking steals focus from the field being edited, so its LostFocus save has
            // already run — the settings are current by the time we read them here.
            test.IsEnabled = false;
            lamp.IsLit = false;
            status.Text = "Testing…";
            var (endpoint, model, key) = read();
            var (ok, message) = await TestConnectionAsync(endpoint, model, key);
            lamp.IsLit = true;
            lamp.LampColor = ok ? Tokens.Colors.MeterGreen : Tokens.Colors.MeterRed;
            status.Text = message;
            test.IsEnabled = true;
        };

        var save = new TransportKey { Content = "SAVE", EngagedColor = Tokens.Colors.Ink };
        save.Click += async (_, _) =>
        {
            // Force LostFocus on any active field to trigger its onCommit handler
            save.Focus();

            // Feedback: brief status, then clear after 2s
            status.Text = "Saved.";
            status.Foreground = new SolidColorBrush(Tokens.Colors.MeterGreen);

            await Task.Delay((int)Tokens.Motion.Feedback.TotalMilliseconds * 4);
            status.Text = "Not tested yet.";
            status.Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary);
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Tokens.Space.Snug,
            Children = { test, save, lamp, status },
        };
    }

    /// <summary>
    /// Probes the gateway's <c>/models</c> listing — reachability and auth in one round
    /// trip, plus a check that the configured model is actually routed there.
    /// </summary>
    private static async Task<(bool Ok, string Message)> TestConnectionAsync(
        string? endpoint, string model, string? apiKey)
    {
        if (string.IsNullOrEmpty(endpoint))
            return (false, "No endpoint configured — transcription is local.");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            if (!string.IsNullOrEmpty(apiKey))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var started = Stopwatch.GetTimestamp();
            using var response = await http.GetAsync(endpoint.TrimEnd('/') + "/models");
            var elapsed = Stopwatch.GetElapsedTime(started);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return (false, "Reached the server, but the API key was rejected.");
            if (!response.IsSuccessStatusCode)
                return (false, $"Server answered {(int)response.StatusCode} {response.ReasonPhrase}.");

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var listed = json.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.EnumerateArray().Any(m =>
                    m.TryGetProperty("id", out var id) && id.GetString() == model);

            return listed
                ? (true, $"Connected — model “{model}” available ({elapsed.TotalMilliseconds:F0} ms).")
                : (true, $"Connected ({elapsed.TotalMilliseconds:F0} ms), but “{model}” is not "
                       + "in the server's model list — check the model name.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException or JsonException)
        {
            return (false, e is TaskCanceledException
                ? "No answer within 8 s — server unreachable?"
                : $"Connection failed: {e.Message}");
        }
    }

    /// <summary>A settings text box that persists on focus loss; empty saves as null.</summary>
    private static TextBox Field(string hint, string? value, Action<string?> onCommit)
    {
        var box = new TextBox
        {
            Text = value ?? string.Empty,
            Watermark = hint,
            FontFamily = Tokens.Fonts.Grotesque,
            FontSize = Tokens.Fonts.Body,
        };

        box.LostFocus += (_, _) =>
        {
            var text = box.Text?.Trim();
            onCommit(string.IsNullOrEmpty(text) ? null : text);
        };

        return box;
    }

    private static StackPanel Labeled(string label, Control field) => new()
    {
        Spacing = Tokens.Space.Hair,
        Children = { new Silkscreen { Text = label }, field },
    };

    private static StackPanel BuildModelSection()
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
                    LampColor = found ? Tokens.Colors.MeterGreen : Tokens.Colors.MeterAmber,
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

        var detail = found
            // Showing the resolved path matters: "model not found" is unactionable without
            // knowing which directory was actually checked.
            ? Note($"Loaded from {located}")
            : Note("Windows has no built-in speech engine equivalent to Apple's, so Vox-Scribe "
                 + "cannot transcribe until the Parakeet model is downloaded (~661 MB). "
                 + "See docs/PARAKEET-WINDOWS.md. Expected in:\n"
                 + string.Join("\n", ParakeetTranscriber.DefaultSearchPaths()));

        return new StackPanel { Spacing = Tokens.Space.Snug, Children = { status, detail } };
    }

    private void Save(SettingsData data) => _settings.Update(data);

    private static BrushedPanel Section(string label, Control content)
    {
        var section = new BrushedPanel
        {
            Opacity = 0.8, // Start slightly faded
            Child = new StackPanel
            {
                Margin = new Thickness(Tokens.Space.Roomy),
                Spacing = Tokens.Space.Base,
                Children = { new Silkscreen { Text = label, IsLarge = true }, content },
            },
        };

        // Add fade-in transition
        var transitions = new Transitions();
        transitions.Add(new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = Tokens.Motion.FadeIn,
        });
        section.Transitions = transitions;

        // Animate in on load
        section.Loaded += (_, _) => section.Opacity = 1;

        return section;
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        FontFamily = Tokens.Fonts.Grotesque,
        FontSize = Tokens.Fonts.Label,
        Foreground = new SolidColorBrush(Tokens.Colors.InkSecondary),
        TextWrapping = TextWrapping.Wrap,
    };

    private static CheckBox Toggle(string label, bool value, Action<bool> onChange)
    {
        var box = new CheckBox
        {
            IsChecked = value,
            Content = new TextBlock
            {
                Text = label,
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Body,
                Foreground = Tokens.Brushes.Ink,
            },
        };

        box.IsCheckedChanged += (_, _) => onChange(box.IsChecked ?? false);
        return box;
    }
}
