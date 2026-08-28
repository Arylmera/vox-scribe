using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Core;

/// <summary>User preferences.</summary>
public sealed record SettingsData
{
    /// <summary>
    /// Virtual-key code of the push-to-talk key. Defaults to Right Ctrl (0xA3).
    /// </summary>
    /// <remarks>
    /// <b>Not Right Alt.</b> On German, Polish, UK, Nordic and most Latin-American layouts
    /// Right Alt is AltGr — it is how those users type <c>@</c>, <c>€</c>, <c>\</c> and
    /// <c>|</c>. Right Ctrl produces no character on any layout.
    /// </remarks>
    public int PushToTalkKey { get; init; } = 0xA3;

    /// <summary>
    /// The full push-to-talk chord as virtual-key codes, or null for pre-chord settings
    /// files. Read through <see cref="ResolvedPushToTalkKeys"/>, which falls back to
    /// <see cref="PushToTalkKey"/> so old settings keep working untouched.
    /// </summary>
    public int[]? PushToTalkKeys { get; init; }

    /// <summary>The chord to arm, whichever era this settings file is from.</summary>
    [JsonIgnore]
    public int[] ResolvedPushToTalkKeys =>
        PushToTalkKeys is { Length: > 0 } keys ? keys : [PushToTalkKey];

    /// <summary>
    /// Shortcut behaviour: false = hold to talk (default), true = press once to start and
    /// again to stop.
    /// </summary>
    public bool PushToTalkToggle { get; init; }

    /// <summary>Where the speech model lives, or null to search the default locations.</summary>
    public string? ModelDirectory { get; init; }

    /// <summary>Whether to type the transcript into the focused app.</summary>
    public bool InjectText { get; init; } = true;

    /// <summary>
    /// Whether each phrase is typed as soon as it is transcribed, rather than the whole
    /// utterance at the end.
    /// </summary>
    /// <remarks>
    /// Off by default: incremental typing follows the caret, so moving it mid-sentence sends
    /// the rest of the dictation to the new spot. The HUD's live preview shows the text as it
    /// arrives either way — this only chooses where it lands while you are still speaking.
    /// </remarks>
    public bool IncrementalInjection { get; init; }

    /// <summary>Whether to keep a transcript history.</summary>
    public bool KeepHistory { get; init; } = true;

    /// <summary>
    /// OpenAI-compatible API base for remote transcription (e.g. a LiteLLM gateway,
    /// <c>http://192.168.1.100:4000/v1</c>), or null to transcribe locally.
    /// </summary>
    public string? SttEndpoint { get; init; }

    /// <summary>Model name the remote gateway routes on.</summary>
    public string SttModel { get; init; } = "stt-mac";

    /// <summary>Bearer key for the remote endpoint, or null when unauthenticated.</summary>
    public string? SttApiKey { get; init; }

    /// <summary>
    /// Second push-to-talk chord, whose utterances go through the cleanup pass. Null or empty
    /// leaves a single shortcut, and then nothing is ever cleaned.
    /// </summary>
    /// <remarks>
    /// The two shortcuts are the whole cleanup switch: the main one types what was heard, this
    /// one types what was tidied. That is a choice made per utterance, at the moment of
    /// speaking, which is when you actually know whether you want the round trip.
    /// </remarks>
    public int[]? CleanupPushToTalkKeys { get; init; }

    /// <summary>
    /// OpenAI-compatible chat API base used to tidy the transcript before it is typed, or
    /// null to type it as transcribed.
    /// </summary>
    /// <remarks>
    /// Off by default. The pass costs a LAN round trip between the key release and the text
    /// appearing, and it is skipped entirely in incremental mode — by the time the utterance
    /// ends there, every phrase has already been typed.
    /// </remarks>
    public string? CleanupEndpoint { get; init; }

    /// <summary>Alias the gateway routes the cleanup call on.</summary>
    /// <remarks>
    /// <c>local-light</c> deliberately: non-thinking, and with no <c>free-*</c> fallback
    /// chain, so dictated text never leaves the LAN when the Mac is asleep.
    /// </remarks>
    public string CleanupModel { get; init; } = "local-light";

    /// <summary>Bearer key for the cleanup endpoint, or null when unauthenticated.</summary>
    public string? CleanupApiKey { get; init; }

    /// <summary>
    /// WASAPI capture device ID (<c>MMDevice.ID</c>), or null for the system default
    /// communications device. Applied at startup.
    /// </summary>
    public string? AudioDeviceId { get; init; }

    /// <summary>
    /// Accent colour as <c>#RRGGBB</c> — tints the dictation pill and highlights. Part of
    /// the Void Glass redesign; the default is its cyan.
    /// </summary>
    public string AccentColor { get; init; } = "#4FD8E8";
}

/// <summary>Settings, persisted as JSON.</summary>
public sealed class AppSettings
{
    private readonly string _path;

    /// <summary>Loads settings from <paramref name="path"/>, or defaults if absent.</summary>
    public AppSettings(string path)
    {
        _path = path;
        Data = Load(path);
    }

    /// <summary>The default location.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Murmur", "settings.json");

    /// <summary>Current values.</summary>
    public SettingsData Data { get; private set; }

    /// <summary>Raised after a successful save.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces and persists the settings.</summary>
    public void Update(SettingsData data)
    {
        Data = data;

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(data, SettingsJsonContext.Default.SettingsData));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static SettingsData Load(string path)
    {
        // Corrupt or unreadable settings must never stop the app launching — defaults are
        // always a working configuration.
        try
        {
            if (!File.Exists(path)) return new SettingsData();

            return JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.SettingsData)
                   ?? new SettingsData();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SettingsData();
        }
    }
}

/// <summary>Source-generated JSON for settings.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsData))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
