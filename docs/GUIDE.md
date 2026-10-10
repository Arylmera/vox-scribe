# VoxScribe — user guide

Everything the app can do, in one place. Feature-by-feature, with where to find it.

---

## Dictating

**Hold the push-to-talk key, speak, release.** The text is typed into whatever field has
focus. The default key is **Right Ctrl**; rebind it in Settings → SHORTCUTS (chords work —
hold several keys together, the last release commits the binding).

**Three dictation shortcuts, three destinations:**

| Shortcut | Default | What it does |
|---|---|---|
| Raw | Right Ctrl | Types the transcript as it was heard |
| Cleanup | not bound | Types the transcript after a small language model fixes punctuation, capitalisation and filler words |
| Command | not bound | Sends the transcript to Claude Code — see below |

The cleanup shortcut only works once a cleanup endpoint is configured (Settings → CLEANUP).
If the gateway is unreachable, the raw text is typed instead — a dictation never disappears.

**Command mode.** Hold the command shortcut and speak; on release the transcript (tidied
when a cleanup endpoint is set) is typed into the first visible window whose title contains
the text in Settings → SHORTCUTS → COMMAND WINDOW TITLE CONTAINS (default `Claude`), then
submitted with Return. You do not need to be looking at that window. If no window matches,
nothing is typed anywhere and the pill says so — a prompt meant for Claude must never land
in a spreadsheet. The pill's badge reads `CMD`.

**Toggle mode** (Settings → SHORTCUTS): press once to start, press again to stop, instead
of holding the key down.

**When the text is typed** (Settings → TYPING), one of three:

- **On release, in the field where you started** (default) — switch windows or click
  elsewhere while you talk; on release the field that had focus when you *pressed* the
  shortcut comes back and the whole dictation is typed there.
- **As you speak, phrase by phrase** — each phrase is typed the moment it is transcribed,
  into whatever has focus right then.
- **On release, wherever you are** — the whole dictation is typed into whatever has focus
  when you let go.

The pill shows the words as they arrive in all three. The cleanup and command shortcuts
always type once, on release: the model needs the whole sentence.

**Spoken punctuation** (off by default, Settings → TYPING): say the mark and it is written.
French and English — *virgule*, *point*, *point d'interrogation*, *point d'exclamation*,
*deux points*, *point-virgule*, *à la ligne* / *nouvelle ligne*; *comma*, *period* /
*full stop*, *question mark*, *exclamation mark*, *colon*, *semicolon*, *new line*. The mark
glues to the word before it, and "à la ligne" starts a new line with no stray space. Handy
for the raw shortcut when no cleanup model is reachable.

## The pill

While you dictate, a small pill sits at the bottom of the screen. It never takes focus —
your text still lands where the caret is.

- **Red lamp + `REC · RAW` / `REC · CLEAN` / `REC · CMD`** — recording; the badge says which
  shortcut is running. Live waveform, running timer.
- **Amber `RAW` / `CLEAN` / `CMD` + shimmer** — you released the key; the tail is being
  transcribed.
- **Preview line** — the transcript as it arrives, last 110 characters.
- **`NOTICE`** — something failed (gateway unreachable, transcription error, speech model
  not loaded, microphone blocked by Windows privacy settings); the message lingers a few
  seconds. Details go to the crash log.
- **Latency readout** — after a clean finish the pill holds for a moment and the timer slot
  shows the wait you just felt, e.g. `1.2s` (from key release to text typed).

## Cancelling a dictation

Changed your mind mid-sentence: press **Escape** while the pill is recording (it shows a
small `ESC` next to the timer as a reminder). The microphone stops, nothing is typed, and
anything incremental mode had already typed is backspaced away. The pill lingers a moment
saying *Cancelled*. Outside a recording Escape is not touched, so it keeps its usual
meaning in whatever app you are in — which also means the app behind still receives the
key during a recording.

## Undoing the last dictation

Wrong window, mangled sentence, accidental press: press the **undo shortcut** (Settings →
SHORTCUTS → UNDO, not bound by default). It deletes the last dictation's text
from wherever it was typed by sending the right number of backspaces — so do it while the
caret is still where the text landed. One dictation deep.

## Main window

A sidebar with **Home**, **History**, **Dictionary** and **Settings**. Home shows the status
line (speech engine, cleanup model, push-to-talk key), this week's words, your pace, time
saved and streak, then the latest dictations with **copy** and **retype** buttons, and the
four shortcuts at the foot.

Closing the window hides it to the tray; the app keeps listening for the shortcut. Only
the tray menu's **Quit** really exits.

### Transcriptions

Every dictation is kept (Settings → GENERAL to turn history off). Search box, per-row
**COPY** and **DELETE**, **DELETE ALL**, timestamp and processing time. Amber **CORRECTED**
badges show which dictionary rules fired, as `heard → written`.

### Dictionary

Fixes the words the speech model reliably gets wrong — names, jargon, glued compounds.
Two kinds of entry:

- **FIX** — "when you hear X, write Y" (`hear → write`)
- **TERM** — a word or phrase to bias the recogniser toward

**ADD** opens the editor with live warnings when a rule would misfire on common words.
Each entry can be toggled **ON/OFF** without deleting it. **OPEN DICTIONARY.TXT** opens
the underlying file directly.

**Suggestions.** When the cleanup model has made the same one-word fix in three or more
dictations (`kubernets → kubernetes`), it appears at the top of the list with **ADD** and
**DISMISS**. Adding it makes it a FIX rule, so the correction happens without the round trip
— and on the raw shortcut too. Dismissals last until the app restarts.

## Settings

| Section | What's there |
|---|---|
| SHORTCUTS | Raw, cleanup, undo and command chords, the command window title, toggle mode. Escape while binding cancels — on the optional slots it *unbinds*. Every shortcut works the moment it is recorded. |
| TYPING | When the text is typed (on release in the field where you started, as you speak, or on release wherever you are), spoken punctuation (off) |
| CLEANUP | OpenAI-compatible endpoint, model (`local-light`), API key, TEST CONNECTION; the /parle and /say read-aloud toggle |
| SPEECH | Microphone, local model status and its DOWNLOAD MODEL button, or a remote OpenAI-compatible transcription endpoint + model + API key |
| GENERAL | Keep history, start at login (minimised to tray) |
| APPEARANCE | Theme — Paper (default), Orb, Tide, Mono or Fluent, each with its own dictation pill; accent colour from the theme's curated swatches. Both apply immediately, no restart; light or dark follows Windows |

Speech settings (microphone, remote server) take effect at next start; the rest is
immediate.

**Your endpoint, or Parakeet.** Point SPEECH → REMOTE SERVER at an OpenAI-compatible
endpoint — a LiteLLM gateway in front of a faster machine, for instance — and transcription
happens there. Without one, Parakeet can run on this PC's processor via sherpa-onnx, so nothing
leaves the machine: press SPEECH → MODEL → DOWNLOAD MODEL (about 661 MB, once; Vox-Scribe
restarts to load it), or follow [PARAKEET-WINDOWS.md](PARAKEET-WINDOWS.md) by hand. The app
never downloads it on its own. Cleanup and read-aloud have no local option: they always use
your endpoint.

**API keys are encrypted** with Windows DPAPI before they touch `settings.json`; they are
never stored in plain text.

## Reading Claude Code replies aloud

In Claude Code, type **`/parle`** and Vox-Scribe reads the previous reply aloud in French;
**`/say`** does the same in English, translating if needed. The reply is rewritten for the ear
first — no paths, no tables, four to eight sentences — then spoken while the rest renders.
Push-to-talk or Escape stops it; a new `/parle` replaces it. The command itself never reaches
the model, so it costs nothing.

**Install it** once, in Claude Code (Vox-Scribe must be installed and running):

```
/plugin marketplace add Arylmera/vox-scribe
/plugin install voxscribe@vox-scribe
```

The plugin is a hook and two commands; it only tells Vox-Scribe which conversation to read.
It needs Windows PowerShell, which every Windows machine has.

**What your endpoint must serve.** Read-aloud uses the CLEANUP endpoint and key (the SPEECH
ones when cleanup is unset), and two model names on it:

| Model | Endpoint | Used for |
|---|---|---|
| `oral` | `/chat/completions`, streamed | Rewriting the reply for speech. A non-thinking model; if it fails, the reply is read as written |
| `tts` | `/audio/speech`, `response_format: wav` | The voice: `ff_siwis` for `/parle`, `af_heart` for `/say` (Kokoro voice names) |

The model names, voices and prompts are `OralModel`, `TtsModel`, `TtsVoice`, `EnglishVoice`,
`OralPrompt` and `EnglishOralPrompt` in `settings.json`. Turn the whole thing off with the
read-aloud toggle under CLEANUP. A failure shows on the tray icon's tooltip.

## Where things live

`%LOCALAPPDATA%\VoxScribe\` holds `settings.json`, `dictionary.txt`, `transcripts.jsonl`
and the model under `models\parakeet-v3\` (or `parakeet-v2\`). Delete the folder and the app starts fresh.
