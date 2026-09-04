using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Settings;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>Settings: shortcuts, typing, cleanup, speech, general, appearance.</summary>
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
        Width = Tokens.Size.SettingsWidth;
        Height = Tokens.Size.SettingsHeight;
        MinWidth = Tokens.Size.SettingsMinWidth;
        MinHeight = Tokens.Size.SettingsMinHeight;
        SizeToContent = SizeToContent.Manual;
        CanResize = true;
        Background = Tokens.Brushes.Chassis;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _hotkeyButton = new TransportKey();
        _hotkeyButton.Click += (_, _) =>
        {
            if (_recorder is null) StartRecording(cleanup: false); else CancelRecording();
        };

        _cleanupHotkeyButton = new TransportKey();
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

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(Tokens.Space.Panel),
                Spacing = Tokens.Space.Wide,
                Children =
                {
                    ShortcutsSection.Build(_settings, Save, _hotkeyButton, _cleanupHotkeyButton, _keyWarning),
                    TypingSection.Build(_settings, Save),
                    CleanupSection.Build(_settings, Save),
                    SpeechSection.Build(_settings, Save),
                    GeneralSection.Build(_settings, Save),
                    AppearanceSection.Build(_settings, Save),
                },
            },
        };

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

    private void Save(SettingsData data) => _settings.Update(data);
}
