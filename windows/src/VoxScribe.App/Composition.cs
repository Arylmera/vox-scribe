using Avalonia.Media;
using VoxScribe.Abstractions;
using VoxScribe.App.Design;
using VoxScribe.Core;
using VoxScribe.Dictionary;
using VoxScribe.Speech;

namespace VoxScribe.App;

/// <summary>
/// Wires the app together: settings, storage, the platform layer, and the engine.
/// </summary>
/// <remarks>
/// <para>
/// The only place that knows about every layer at once. Views take what they need; nothing
/// else reaches across.
/// </para>
/// <para>
/// The platform implementations are resolved by reflection rather than referenced directly,
/// so <c>VoxScribe.App</c> can target plain <c>net10.0</c> and therefore be built, run and
/// headless-tested on macOS — which is the entire reason Avalonia was chosen over WPF. On any
/// non-Windows machine the lookup simply finds nothing and the app runs with inert stand-ins,
/// which is exactly what a UI test wants.
/// </para>
/// </remarks>
public sealed class Composition : IAsyncDisposable
{
    private Composition(
        AppSettings settings,
        DictionaryFile dictionary,
        TranscriptStore transcripts,
        DictationEngine? engine,
        bool platformAvailable)
    {
        Settings = settings;
        Dictionary = dictionary;
        Transcripts = transcripts;
        Engine = engine;
        IsPlatformAvailable = platformAvailable;
    }

    /// <summary>User preferences.</summary>
    public AppSettings Settings { get; }

    /// <summary>The correction dictionary.</summary>
    public DictionaryFile Dictionary { get; }

    /// <summary>Transcript history.</summary>
    public TranscriptStore Transcripts { get; }

    /// <summary>The dictation engine, or null when no platform layer is available.</summary>
    public DictationEngine? Engine { get; }

    /// <summary>Whether real audio and hotkey support were found.</summary>
    public bool IsPlatformAvailable { get; }

    /// <summary>
    /// Whether transcription is possible: a local model on disk, or a remote endpoint
    /// configured — the missing-model banner is wrong on a machine that dictates remotely.
    /// </summary>
    public static bool IsModelInstalled =>
        ParakeetTranscriber.Locate() is not null
        || new AppSettings(AppSettings.DefaultPath).Data.SttEndpoint is { Length: > 0 };

    /// <summary>
    /// Keys held by any of <paramref name="others"/> that strictly contains
    /// <paramref name="chord"/>, minus the chord itself — the keys whose being down means the
    /// user is reaching for the longer shortcut, not this one.
    /// </summary>
    public static int[] Blockers(int[] chord, params int[]?[] others)
    {
        if (chord.Length == 0) return [];

        return [.. others
            .Where(o => o is { Length: > 0 } && o.Length > chord.Length && chord.All(o.Contains))
            .SelectMany(o => o!.Where(k => !chord.Contains(k)))
            .Distinct()];
    }

    /// <summary>An unbound chord is an empty one: the hook exists but can never complete.</summary>
    private static int[] Chord(int[]? keys) => keys is { Length: > 0 } ? keys : [];

    /// <summary>Parses and installs the accent, keeping the default on a bad value.</summary>
    private static void ApplyAccent(string hex)
    {
        if (Color.TryParse(hex, out var color)) Tokens.Colors.Accent = color;
    }

    /// <summary>Builds the object graph.</summary>
    public static Composition Create()
    {
        var settings = new AppSettings(AppSettings.DefaultPath);

        // Theme first, before any window exists — views cache brushes at build time, so the
        // theme is a next-start setting, unlike the live accent below.
        Themes.Apply(settings.Data.Theme);

        // The accent is live everywhere the moment it changes — same promise as the hotkey.
        ApplyAccent(settings.Data.AccentColor);
        settings.Changed += (_, _) => ApplyAccent(settings.Data.AccentColor);

        var dictionary = new DictionaryFile(DictionaryFile.DefaultPath);
        var transcripts = new TranscriptStore(TranscriptStore.DefaultPath);

        var capture = PlatformFactory.CreateAudioCapture(settings.Data.AudioDeviceId);
        var hotkey = PlatformFactory.CreateHotkeySource(settings.Data.ResolvedPushToTalkKeys);
        var injector = PlatformFactory.CreateTextInjector();
        var focusAnchor = PlatformFactory.CreateFocusAnchor();

        DictationEngine? engine = null;
        var available = capture is not null && hotkey is not null && injector is not null;

        if (available)
        {
            var modelDirectory = settings.Data.ModelDirectory ?? ParakeetTranscriber.Locate();

            // The network layers are built before the engine, so the failure report closes
            // over a slot that is filled once the engine exists. Every failure goes to the
            // crash log for diagnosis and to the engine's Notice for the pill.
            DictationEngine? watching = null;
            void ReportFailure(string message)
            {
                Program.LogNotice(message);
                watching?.ReportNotice(message);
            }

            // A configured remote endpoint wins over the local model: it is an explicit
            // user choice, and the machines that set it deliberately have no local model.
            ITranscriber transcriber = settings.Data.SttEndpoint is { Length: > 0 } endpoint
                ? new RemoteTranscriber(endpoint, settings.Data.SttModel, settings.Data.SttApiKey, ReportFailure)
                : modelDirectory is not null
                    ? new ParakeetTranscriber(modelDirectory)
                    : new UnavailableTranscriber();

            // Every optional shortcut gets a listener whether or not it is bound yet — an
            // empty chord never fires, and all of them share the one keyboard hook, so an
            // idle listener costs nothing. What it buys is that binding one for the first
            // time works immediately instead of after a restart.
            var cleanupHotkey = PlatformFactory.CreateHotkeySource(Chord(settings.Data.CleanupPushToTalkKeys));
            var undoHotkey = PlatformFactory.CreateHotkeySource(Chord(settings.Data.UndoKeys));
            var commandHotkey = PlatformFactory.CreateHotkeySource(Chord(settings.Data.CommandKeys));

            // Escape throws a dictation away. The hook never swallows keys, so outside a
            // recording Escape still reaches whatever app has it — the engine ignores it.
            const int VkEscape = 0x1B;
            var cancelHotkey = PlatformFactory.CreateHotkeySource(VkEscape);

            engine = new DictationEngine(
                capture!, hotkey!, transcriber, injector!,
                () => dictionary.Entries,
                cleanupHotkey: cleanupHotkey,
                focusAnchor: focusAnchor,
                cancelHotkey: cancelHotkey,
                undoHotkey: undoHotkey,
                commandHotkey: commandHotkey);

            // Right Shift and Left Shift + Right Shift both satisfy the shorter chord. Each
            // hook is told the keys that belong only to a longer chord containing its own,
            // and stands aside while any of them is held.
            void Rearm()
            {
                var raw = settings.Data.ResolvedPushToTalkKeys;
                var clean = Chord(settings.Data.CleanupPushToTalkKeys);
                var undo = Chord(settings.Data.UndoKeys);
                var command = Chord(settings.Data.CommandKeys);

                (IHotkeySource? Hook, int[] Own, int[]?[] Others)[] chords =
                [
                    (hotkey, raw, [clean, undo, command]),
                    (cleanupHotkey, clean, [raw, undo, command]),
                    (undoHotkey, undo, [raw, clean, command]),
                    (commandHotkey, command, [raw, clean, undo]),
                ];

                foreach (var (hook, own, others) in chords)
                {
                    if (hook is null) continue;
                    PlatformFactory.UpdateHotkeyChord(hook, own);
                    PlatformFactory.UpdateHotkeyBlockers(hook, Blockers(own, others));
                }
            }

            Rearm();

            engine.ToggleMode = settings.Data.PushToTalkToggle;
            engine.IncrementalInjection = settings.Data.IncrementalInjection;
            engine.SpokenPunctuation = settings.Data.SpokenPunctuation;
            engine.AnchorFocus = settings.Data.AnchorFocus;
            engine.CommandWindowTitle = settings.Data.CommandWindowTitle;

            watching = engine;

            // Rebuilt on every settings change, like the chords: the cleaner holds nothing
            // but its endpoint, so swapping it live is free — and "restart to apply" on a
            // field you just typed reads as broken.
            Func<string, CancellationToken, Task<string>>? BuildCleanup() =>
                settings.Data.CleanupEndpoint is { Length: > 0 } cleanupEndpoint
                    ? new TextCleaner(
                        cleanupEndpoint, settings.Data.CleanupModel, settings.Data.CleanupApiKey,
                        ReportFailure).CleanAsync
                    : null;

            engine.Cleanup = BuildCleanup();

            // A freshly recorded shortcut must work right away — "restart to apply" reads
            // as "recording is broken". The hook reads Keys per event, so swapping the
            // array reference live is safe; so is flipping the toggle behaviour.
            var live = engine;
            settings.Changed += (_, _) =>
            {
                Rearm();
                live.ToggleMode = settings.Data.PushToTalkToggle;
                live.IncrementalInjection = settings.Data.IncrementalInjection;
                live.SpokenPunctuation = settings.Data.SpokenPunctuation;
                live.AnchorFocus = settings.Data.AnchorFocus;
                live.CommandWindowTitle = settings.Data.CommandWindowTitle;
                live.Cleanup = BuildCleanup();
            };

            engine.Completed += (_, result) =>
            {
                if (!settings.Data.KeepHistory) return;

                transcripts.Add(new TranscriptRecord
                {
                    At = result.At,
                    AudioSeconds = result.AudioDuration.TotalSeconds,
                    ProcessingSeconds = result.ProcessingTime.TotalSeconds,
                    Text = result.Text,
                    Corrections = result.Corrections.Count > 0 ? result.Corrections : null,
                    RawText = result.RawText,
                });
            };
        }

        return new Composition(settings, dictionary, transcripts, engine, available);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Engine is not null) await Engine.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// A transcriber that reports the model is missing rather than throwing.
/// </summary>
/// <remarks>
/// A fresh Windows install has no model, and that has to be a readable message in Settings
/// rather than a crash on first press.
/// </remarks>
internal sealed class UnavailableTranscriber : ITranscriber
{
    public bool IsReady => false;

    public ValueTask<bool> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        IReadOnlyList<string> biasPhrases,
        CancellationToken cancellationToken) => ValueTask.FromResult(string.Empty);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
