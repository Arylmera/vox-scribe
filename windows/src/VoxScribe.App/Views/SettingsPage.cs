using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Settings;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>The settings tabs, in display order.</summary>
internal enum SettingsTab
{
    /// <summary>History and start-up.</summary>
    General,

    /// <summary>Model, device, remote STT.</summary>
    Speech,

    /// <summary>The four chords.</summary>
    Shortcuts,

    /// <summary>Where and when text is typed.</summary>
    Typing,

    /// <summary>The cleanup model.</summary>
    Cleanup,

    /// <summary>Theme and accent.</summary>
    Appearance,
}

/// <summary>Settings as a page of the main window: six tabs over the existing sections.</summary>
internal sealed class SettingsPage : UserControl
{
    /// <summary>Escape cancels a recording rather than becoming the trigger.</summary>
    private const int VkEscape = 0x1B;

    /// <summary>Right Alt is AltGr on many European layouts; warn rather than forbid.</summary>
    private const int VkRightAlt = 0xA5;

    private readonly AppSettings _settings;
    private readonly Dictionary<ShortcutSlot, TransportKey> _keys = [];
    private readonly TextBlock _keyWarning;
    private readonly Dictionary<SettingsTab, Control> _sections = [];
    private readonly Dictionary<SettingsTab, Button> _tabs = [];
    private readonly ContentControl _host = new();
    private readonly List<int> _captured = [];
    private readonly HashSet<int> _held = [];

    private ShortcutSlot _recordingSlot;
    private IDisposable? _recorder;

    /// <summary>Builds the page with <paramref name="tab"/> open.</summary>
    public SettingsPage(AppSettings settings, DictationEngine? engine, SettingsTab tab)
    {
        _settings = settings;

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
            Foreground = new SolidColorBrush(Tokens.Colors.Caution),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        // Built once: the shortcut recorders live inside their section, and a control can only
        // be parented once — swapping tabs re-hosts these instances.
        _sections[SettingsTab.General] = GeneralSection.Build(_settings, Save);
        _sections[SettingsTab.Speech] = SpeechSection.Build(_settings, Save);
        _sections[SettingsTab.Shortcuts] = ShortcutsSection.Build(_settings, Save, _keys, _keyWarning);
        _sections[SettingsTab.Typing] = TypingSection.Build(_settings, Save);
        _sections[SettingsTab.Cleanup] = CleanupSection.Build(_settings, Save);
        _sections[SettingsTab.Appearance] = AppearanceSection.Build(_settings, Save, engine);

        var bar = new WrapPanel
        {
            ItemSpacing = Tokens.Space.Tight,
            LineSpacing = Tokens.Space.Tight,
            Margin = new Thickness(0, 0, 0, Tokens.Space.Roomy),
        };
        foreach (var t in Enum.GetValues<SettingsTab>())
        {
            var button = new Button
            {
                Content = Shell.Caps(t.ToString()),
                Height = Tokens.Material.TabHeight,
                Padding = new Thickness(Tokens.Space.Base, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(Tokens.Radius.Chip),
                FontFamily = Tokens.Fonts.Grotesque,
                FontSize = Tokens.Fonts.Body,
            };
            AutomationProperties.SetName(button, $"Settings tab: {t}");
            button.Click += (_, _) => Select(t);
            _tabs[t] = button;
            bar.Children.Add(button);
        }

        KeyboardNavigation.SetTabNavigation(_host, KeyboardNavigationMode.Cycle);
        Content = new DockPanel
        {
            Children =
            {
                Panels.Docked(Shell.PageTitle(Shell.Caps("Settings")), Dock.Top),
                Panels.Docked(bar, Dock.Top),
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = _host,
                },
            },
        };

        // A page can leave the screen without being closed (page switch, theme rebuild, hide to
        // tray); a live key-capture hook must never outlive it.
        DetachedFromVisualTree += (_, _) => CancelRecording();

        ShowAllChords();
        Select(tab);
    }

    /// <summary>The open tab.</summary>
    public SettingsTab Tab { get; private set; }

    /// <summary>Opens <paramref name="tab"/>.</summary>
    public void Select(SettingsTab tab)
    {
        if (tab != SettingsTab.Shortcuts) CancelRecording();
        Tab = tab;
        foreach (var (t, button) in _tabs) Shell.Paint(button, Themes.Active.TabSelection, t == tab);
        _host.Content = _sections[tab];
    }

    /// <summary>Stops a live chord recording, if any, and restores the key labels.</summary>
    public void CancelRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        foreach (var key in _keys.Values) key.IsEngaged = false;
        ShowAllChords();
    }

    // ---- Chord recorder: moved verbatim from SettingsWindow ----

    private int[]? Chord(ShortcutSlot slot) => slot switch
    {
        ShortcutSlot.Raw => _settings.Data.ResolvedPushToTalkKeys,
        ShortcutSlot.Cleanup => _settings.Data.CleanupPushToTalkKeys,
        ShortcutSlot.Undo => _settings.Data.UndoKeys,
        ShortcutSlot.Command => _settings.Data.CommandKeys,
        _ => null,
    };

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

        // Events arrive on the hook thread; every touch of the UI below is posted.
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
                // On an optional slot Escape means "unbind"; the raw slot only cancels.
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

    private TransportKey Recording => _keys[_recordingSlot];

    private void CommitRecording()
    {
        var chord = _captured.ToArray();
        var slot = _recordingSlot;
        CancelRecording();

        SaveChord(slot, chord);
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
