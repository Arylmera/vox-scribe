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
    private readonly Dictionary<ShortcutSlot, TransportKey> _keys = [];
    private readonly TextBlock _keyWarning;

    /// <summary>Which shortcut the live recorder is binding.</summary>
    private ShortcutSlot _recordingSlot;

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

        foreach (var slot in Enum.GetValues<ShortcutSlot>())
        {
            var key = new TransportKey();
            key.Click += (_, _) =>
            {
                if (_recorder is null) StartRecording(slot); else CancelRecording();
            };
            _keys[slot] = key;
        }

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
                    ShortcutsSection.Build(_settings, Save, _keys, _keyWarning),
                    TypingSection.Build(_settings, Save),
                    CleanupSection.Build(_settings, Save),
                    SpeechSection.Build(_settings, Save),
                    GeneralSection.Build(_settings, Save),
                    AppearanceSection.Build(_settings, Save),
                },
            },
        };

        ShowAllChords();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        CancelRecording();
        base.OnClosed(e);
    }

    /// <summary>The chord currently saved for <paramref name="slot"/>, or null when unbound.</summary>
    private int[]? Chord(ShortcutSlot slot) => slot switch
    {
        ShortcutSlot.Raw => _settings.Data.ResolvedPushToTalkKeys,
        ShortcutSlot.Cleanup => _settings.Data.CleanupPushToTalkKeys,
        ShortcutSlot.Undo => _settings.Data.UndoKeys,
        ShortcutSlot.Command => _settings.Data.CommandKeys,
        _ => null,
    };

    /// <summary>Persists <paramref name="chord"/> into <paramref name="slot"/>; null unbinds.</summary>
    private void SaveChord(ShortcutSlot slot, int[]? chord)
    {
        var data = _settings.Data;
        Save(slot switch
        {
            // The raw slot cannot be unbound; a null here never happens (Escape cancels).
            ShortcutSlot.Raw => data with { PushToTalkKeys = chord, PushToTalkKey = chord![0] },
            ShortcutSlot.Cleanup => data with { CleanupPushToTalkKeys = chord },
            ShortcutSlot.Undo => data with { UndoKeys = chord },
            ShortcutSlot.Command => data with { CommandKeys = chord },
            _ => data,
        });
    }

    private void StartRecording(ShortcutSlot slot)
    {
        _recordingSlot = slot;
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
                // On an optional slot Escape means "unbind" rather than "cancel": with no
                // other gesture available, there would otherwise be no way back to fewer
                // shortcuts once one is recorded. The raw slot only cancels.
                var slot = _recordingSlot;
                CancelRecording();
                if (slot != ShortcutSlot.Raw) SaveChord(slot, null);
                ShowAllChords();
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
    private TransportKey Recording => _keys[_recordingSlot];

    private void CommitRecording()
    {
        var chord = _captured.ToArray();
        var slot = _recordingSlot;
        CancelRecording();

        SaveChord(slot, chord);
        ShowAllChords();
    }

    private void CancelRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        foreach (var key in _keys.Values) key.IsEngaged = false;
        ShowAllChords();
    }

    private void ShowAllChords()
    {
        foreach (var (slot, key) in _keys)
        {
            key.Content = Chord(slot) is { Length: > 0 } chord ? ChordLabel(chord) : "NOT BOUND";
        }

        var altGr = _keys.Keys.Any(slot => Chord(slot)?.Contains(VkRightAlt) == true);
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
