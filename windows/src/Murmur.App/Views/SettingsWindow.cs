using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;
using Murmur.Speech;

namespace Murmur.App.Views;

/// <summary>Settings: the hotkey and the model.</summary>
public sealed class SettingsWindow : Window
{
    /// <summary>Escape cancels a recording rather than becoming the trigger.</summary>
    private const int VkEscape = 0x1B;

    /// <summary>Right Alt is AltGr on many European layouts; warn rather than forbid.</summary>
    private const int VkRightAlt = 0xA5;

    private readonly AppSettings _settings;
    private readonly TransportKey _hotkeyButton;
    private readonly TextBlock _keyWarning;

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

        Title = "Murmur Settings";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = Tokens.Brushes.Chassis;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _hotkeyButton = new TransportKey { EngagedColor = Tokens.Colors.Ink };
        _hotkeyButton.Click += (_, _) => { if (_recorder is null) StartRecording(); else CancelRecording(); };

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
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        CancelRecording();
        base.OnClosed(e);
    }

    private void StartRecording()
    {
        _captured.Clear();
        _held.Clear();

        // Events arrive on the hook thread; every touch of the UI below is posted. Same
        // lesson as the transcriptions view: off-thread Avalonia access fails silently.
        _recorder = PlatformFactory.StartKeyCapture((key, isDown) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnRecordedKey(key, isDown)));

        if (_recorder is null) return; // off Windows, or the hook failed to install

        _hotkeyButton.IsEngaged = true;
        _hotkeyButton.Content = "PRESS YOUR KEY(S)…";
    }

    private void OnRecordedKey(int key, bool isDown)
    {
        if (_recorder is null) return;

        if (isDown)
        {
            if (key == VkEscape && _captured.Count == 0)
            {
                CancelRecording();
                return;
            }

            if (!_captured.Contains(key)) _captured.Add(key);
            _held.Add(key);
            _hotkeyButton.Content = ChordLabel(_captured);
            return;
        }

        _held.Remove(key);

        // The chord is whatever was held together; the last release commits it.
        if (_captured.Count > 0 && _held.Count == 0) CommitRecording();
    }

    private void CommitRecording()
    {
        var chord = _captured.ToArray();
        CancelRecording();

        Save(_settings.Data with { PushToTalkKeys = chord, PushToTalkKey = chord[0] });
        ShowChord(chord);
    }

    private void CancelRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        _hotkeyButton.IsEngaged = false;
        ShowChord(_settings.Data.ResolvedPushToTalkKeys);
    }

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
                       + "combination; releasing them records it. Escape cancels. Hold the "
                       + "recorded key(s) anywhere to dictate; takes effect the next time "
                       + "Murmur starts."),
                },
            }),

            Section("MICROPHONE", BuildMicrophoneSection()),

            Section("MODEL", BuildModelSection()),

            Section("BEHAVIOUR", new StackPanel
            {
                Spacing = Tokens.Space.Snug,
                Children =
                {
                    Toggle("Type transcripts into the focused app", _settings.Data.InjectText,
                        v => Save(_settings.Data with { InjectText = v })),
                    Toggle("Keep a transcript history", _settings.Data.KeepHistory,
                        v => Save(_settings.Data with { KeepHistory = v })),
                },
            }),
        },
    };

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
                Note("Which microphone to record from. Takes effect the next time Murmur starts."),
            },
        };
    }

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
            : Note("Windows has no built-in speech engine equivalent to Apple's, so Murmur "
                 + "cannot transcribe until the Parakeet model is downloaded (~661 MB). "
                 + "See docs/PARAKEET-WINDOWS.md. Expected in:\n"
                 + string.Join("\n", ParakeetTranscriber.DefaultSearchPaths()));

        return new StackPanel { Spacing = Tokens.Space.Snug, Children = { status, detail } };
    }

    private void Save(SettingsData data) => _settings.Update(data);

    private static BrushedPanel Section(string label, Control content) => new BrushedPanel
    {
        Child = new StackPanel
        {
            Margin = new Thickness(Tokens.Space.Roomy),
            Spacing = Tokens.Space.Base,
            Children = { new Silkscreen { Text = label, IsLarge = true }, content },
        },
    };

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
