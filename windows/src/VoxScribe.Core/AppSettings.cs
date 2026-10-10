using VoxScribe.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxScribe.Core;

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
    public int PushToTalkKey { get; set; } = 0xA3;

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

    /// <summary>
    /// Whether each phrase is typed as soon as it is transcribed, rather than the whole
    /// utterance at the end.
    /// </summary>
    /// <remarks>
    /// Off by default: incremental typing follows the caret, so moving it mid-sentence sends
    /// the rest of the dictation to the new spot. The HUD's live preview shows the text as it
    /// arrives either way — this only chooses where it lands while you are still speaking.
    /// <para>
    /// Raw dictation only. The cleanup shortcut overrides it and always types once, at the
    /// end, because text already in the target window cannot be repaired.
    /// </para>
    /// </remarks>
    public bool IncrementalInjection { get; init; }

    /// <summary>
    /// Whether spoken punctuation ("virgule", "point", "new line"…) becomes the mark itself.
    /// Off by default: "point" is an ordinary French word, and a dictation that goes through
    /// the cleanup model is punctuated there anyway.
    /// </summary>
    public bool SpokenPunctuation { get; init; }

    /// <summary>
    /// Whether text goes to the field that had focus when the shortcut was pressed, rather
    /// than wherever focus is at release.
    /// </summary>
    /// <remarks>
    /// On by default: it lets the user switch windows or click elsewhere while speaking.
    /// While on, it overrides <see cref="IncrementalInjection"/> — phrases are held and typed
    /// together at release, because typing them as they land would send them to whatever the
    /// user is clicking on at that moment.
    /// </remarks>
    public bool AnchorFocus { get; set; } = true;

    /// <summary>Whether to look for a newer release at start-up. It only ever offers.</summary>
    public bool CheckUpdatesAtStartup { get; set; } = true;

    /// <summary>Whether to keep a transcript history.</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>
    /// OpenAI-compatible API base for remote transcription (e.g. a LiteLLM gateway,
    /// <c>http://192.168.1.100:4000/v1</c>), or null to transcribe locally.
    /// </summary>
    public string? SttEndpoint { get; init; }

    /// <summary>Model name the remote gateway routes on.</summary>
    public string SttModel { get; set; } = "stt-mac";

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
    /// Chord that deletes the last dictation's text from wherever it landed, without opening
    /// the window. Null or empty leaves undo on the UNDO key only.
    /// </summary>
    public int[]? UndoKeys { get; init; }

    /// <summary>
    /// Command-mode chord: the dictation (tidied when a cleanup endpoint is set) is typed into
    /// the first window whose title contains <see cref="CommandWindowTitle"/>, then submitted
    /// with Return — a Claude Code session, wherever it is. Null or empty disables it.
    /// </summary>
    public int[]? CommandKeys { get; init; }

    /// <summary>Title fragment that identifies the command-mode target window.</summary>
    public string CommandWindowTitle { get; set; } = "Claude";

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
    public string CleanupModel { get; set; } = "local-light";

    /// <summary>Bearer key for the cleanup endpoint, or null when unauthenticated.</summary>
    public string? CleanupApiKey { get; init; }

    /// <summary>
    /// Whether a <c>/parle</c> or <c>/say</c> request from Claude Code is read aloud, or null for a file
    /// from before read-aloud existed. Read through <see cref="ReadAloudEnabled"/>.
    /// </summary>
    /// <remarks>
    /// Nullable, and the strings below fall back in their getters, because the source-generated
    /// reader sets every init-only property — one missing from an older file arrives as
    /// false or null, not as its initializer.
    /// </remarks>
    public bool? ReadAloud { get; init; }

    /// <summary>On unless explicitly turned off. Uses the cleanup endpoint and key (the STT
    /// ones when cleanup is unset).</summary>
    [JsonIgnore]
    public bool ReadAloudEnabled => ReadAloud ?? true;

    /// <summary>Alias that rewrites a reply for speech, streamed.</summary>
    public string OralModel { get => Or(_oralModel, "oral"); init => _oralModel = value; }

    /// <summary>Alias that synthesises speech (<c>/audio/speech</c>).</summary>
    public string TtsModel { get => Or(_ttsModel, "tts"); init => _ttsModel = value; }

    /// <summary>Voice passed to the TTS alias; its prefix picks the language.</summary>
    public string TtsVoice { get => Or(_ttsVoice, "ff_siwis"); init => _ttsVoice = value; }

    /// <summary>System prompt of the oral rewrite.</summary>
    public string OralPrompt { get => Or(_oralPrompt, DefaultOralPrompt); init => _oralPrompt = value; }

    /// <summary>Voice <c>/say</c> speaks with; its prefix picks the language.</summary>
    public string EnglishVoice { get => Or(_englishVoice, "af_heart"); init => _englishVoice = value; }

    /// <summary>System prompt of the <c>/say</c> rewrite, which also translates.</summary>
    public string EnglishOralPrompt { get => Or(_englishOralPrompt, DefaultEnglishOralPrompt); init => _englishOralPrompt = value; }

    private readonly string? _oralModel;
    private readonly string? _ttsModel;
    private readonly string? _ttsVoice;
    private readonly string? _oralPrompt;
    private readonly string? _englishVoice;
    private readonly string? _englishOralPrompt;

    private static string Or(string? value, string fallback) => value is { Length: > 0 } ? value : fallback;

    /// <summary>The oral rewrite prompt tuned on real replies (read-aloud spec, step 5).</summary>
    public const string DefaultOralPrompt = """
        Tu reformules la réponse écrite d'un assistant de code pour qu'elle soit dite à voix haute, comme un collègue qui t'explique de vive voix ce qu'il vient de faire.

        Règles strictes :
        - Français parlé, naturel, phrases courtes. Tutoiement. Pas de « Alors », pas de « Voici », pas de « Bonne nouvelle ».
        - Aucune liste, aucun tableau, aucun titre, aucun symbole, aucun emoji. Un tableau devient une ou deux phrases qui disent ce qu'il montre.
        - Ne prononce jamais un nom de fichier, un chemin, une URL, une commande, un identifiant de commit ou une version précise. Dis plutôt ce que c'est : « la spec », « le hook », « la config de la passerelle ».
        - Garde les décisions, les résultats chiffrés qui comptent (au plus deux ou trois), les problèmes rencontrés et la question finale s'il y en a une.
        - N'invente rien : pas de jugement, de comparaison ou de conclusion absents du texte. Ne réponds pas à la question, rapporte-la.
        - Entre quatre et huit phrases au total, quelle que soit la longueur d'origine.
        """;

    /// <summary>The <c>/say</c> counterpart of <see cref="DefaultOralPrompt"/>.</summary>
    public const string DefaultEnglishOralPrompt = """
        You rewrite a coding assistant's written reply so it can be spoken aloud, like a colleague telling you in person what they just did. The reply may be in another language: always answer in English.

        Strict rules:
        - Natural spoken English, short sentences. No "So", no "Here is", no "Good news".
        - No lists, tables, headings, symbols or emoji. A table becomes one or two sentences saying what it shows.
        - Never say a file name, path, URL, command, commit id or exact version. Say what it is instead: "the spec", "the hook", "the gateway config".
        - Keep the decisions, the numbers that matter (two or three at most), the problems met and the closing question if there is one.
        - Invent nothing: no judgement, comparison or conclusion that is not in the text. Do not answer the question, report it.
        - Four to eight sentences in total, whatever the original length.
        """;

    /// <summary>
    /// WASAPI capture device ID (<c>MMDevice.ID</c>), or null for the system default
    /// communications device. Applied at startup.
    /// </summary>
    public string? AudioDeviceId { get; init; }

    /// <summary>
    /// Visual theme id: "paper" (the default), "orb", "tide", "mono" or "fluent". Unknown
    /// values — including the retired "deep-field", "signal-house" and "manuscript" — fall back
    /// to Paper, so old or hand-edited files keep working.
    /// </summary>
    public string Theme { get; set; } = "paper";

    /// <summary>
    /// Accent variant id within the theme ("plum", "moss", …), or null for the theme's first.
    /// An id the theme does not offer falls back the same way.
    /// </summary>
    public string? AccentVariant { get; init; }
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

        // A settings file written before keys were protected still carries them in the
        // clear. Rewriting once here is the whole migration — no subscriber exists yet, so
        // the Changed event fires into nothing.
        if (OperatingSystem.IsWindows()
            && File.Exists(path)
            && (Data.SttApiKey ?? Data.CleanupApiKey) is not null
            && !File.ReadAllText(path).Contains(ProtectedPrefix, StringComparison.Ordinal))
        {
            Update(Data);
        }
    }

    /// <summary>The default location.</summary>
    public static string DefaultPath => DataDirectory.File("settings.json");

    /// <summary>Current values.</summary>
    public SettingsData Data { get; private set; }

    /// <summary>Raised after a successful save.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces and persists the settings.</summary>
    /// <remarks><see cref="Data"/> keeps the keys in the clear; only the file is protected.</remarks>
    public void Update(SettingsData data)
    {
        Data = data;

        var stored = data with
        {
            SttApiKey = Protect(data.SttApiKey),
            CleanupApiKey = Protect(data.CleanupApiKey),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(stored, SettingsJsonContext.Default.SettingsData));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static SettingsData Load(string path)
    {
        // Corrupt or unreadable settings must never stop the app launching — defaults are
        // always a working configuration.
        try
        {
            if (!File.Exists(path)) return new SettingsData();

            var data = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.SettingsData)
                       ?? new SettingsData();

            return data with
            {
                SttApiKey = Unprotect(data.SttApiKey),
                CleanupApiKey = Unprotect(data.CleanupApiKey),
            };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SettingsData();
        }
    }

    /// <summary>Marks a DPAPI-protected value in the settings file.</summary>
    private const string ProtectedPrefix = "dpapi:";

    /// <summary>
    /// Encrypts an API key for the file, per Windows user. Elsewhere — macOS dev machines,
    /// the cross-platform tests — the value passes through in the clear, which is what those
    /// environments had all along.
    /// </summary>
    private static string? Protect(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || !OperatingSystem.IsWindows()) return secret;

        return ProtectedPrefix + Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));
    }

    /// <summary>
    /// Decrypts a stored key. A value that cannot be decrypted — another user's profile, a
    /// copied settings file, a non-Windows machine — becomes null: an unauthenticated
    /// endpoint is a readable failure, a garbled bearer header is not.
    /// </summary>
    private static string? Unprotect(string? stored)
    {
        if (stored is null || !stored.StartsWith(ProtectedPrefix, StringComparison.Ordinal)) return stored;
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(stored[ProtectedPrefix.Length..]),
                null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>Source-generated JSON for settings.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsData))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
