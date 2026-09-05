# VoxScribe — user guide

Everything the app can do, in one place. Feature-by-feature, with where to find it.

---

## Dictating

**Hold the push-to-talk key, speak, release.** The text is typed into whatever field has
focus. The default key is **Right Ctrl**; rebind it in Settings → SHORTCUTS (chords work —
hold several keys together, the last release commits the binding).

**Two shortcuts, two behaviours:**

| Shortcut | Default | What it types |
|---|---|---|
| Raw | Right Ctrl | The transcript as it was heard |
| Cleanup | not bound | The transcript after a small language model fixes punctuation, capitalisation and filler words |

The cleanup shortcut only works once a cleanup endpoint is configured (Settings → CLEANUP).
If the gateway is unreachable, the raw text is typed instead — a dictation never disappears.

**Toggle mode** (Settings → SHORTCUTS): press once to start, press again to stop, instead
of holding the key down.

**Focus anchoring** (on by default): the text goes to the field that had focus when you
*pressed* the shortcut, even if you clicked elsewhere while talking.

**Incremental typing** (off by default, raw shortcut only): each phrase is typed as you
speak it instead of everything at the end.

## The pill

While you dictate, a small pill sits at the bottom of the screen. It never takes focus —
your text still lands where the caret is.

- **Red lamp + `REC · RAW` / `REC · CLEAN`** — recording; the badge says which shortcut is
  running. Live waveform, running timer.
- **Amber `RAW` / `CLEAN` + shimmer** — you released the key; the tail is being transcribed.
- **Preview line** — the transcript as it arrives, last 110 characters.
- **`NOTICE`** — something failed (gateway unreachable, transcription error); the message
  lingers a few seconds. Details go to the crash log.
- **Latency readout** — after a clean finish the pill holds for a moment and the timer slot
  shows the wait you just felt, e.g. `1.2s` (from key release to text typed).

## Undoing the last dictation

Wrong window, mangled sentence, accidental press: open the main window and click **UNDO**
in the voice band (top strip, next to the tape counter). It deletes the last dictation's
text from wherever it was typed by sending the right number of backspaces — so do it while
the caret is still where the text landed. One dictation deep; there is no global hotkey
for it yet.

## Main window

Left icon rail: **Transcriptions** (wave), **Dictionary** (book), **Settings** (gear at the
foot). The voice band on top has a record button (click = same as the push-to-talk key), a
VU meter, the tape counter and UNDO.

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

## Settings

| Section | What's there |
|---|---|
| SHORTCUTS | Raw key, cleanup key, toggle mode. Escape while binding cancels — on the cleanup slot it *unbinds*. |
| TYPING | Type into focused app (on), anchor focus (on), incremental typing (off) |
| CLEANUP | OpenAI-compatible endpoint, model (`local-light`), API key, TEST CONNECTION |
| SPEECH | Microphone, local model status, or a remote OpenAI-compatible transcription endpoint + model + API key |
| GENERAL | Keep history, start at login (minimised to tray) |
| APPEARANCE | Theme — Deep Field (dark), Signal House (warm hardware), Manuscript (paper-light, serif transcripts); an APPLY key restarts the app with the new theme. Accent colour — five swatches, applies immediately |

Speech settings (microphone, remote server) and a first-time cleanup binding take effect
at next start; the rest is immediate.

**Local or remote speech.** By default Parakeet runs on your CPU via sherpa-onnx — nothing
leaves the machine, but the model (~661 MB) must be downloaded first (see
[PARAKEET-WINDOWS.md](PARAKEET-WINDOWS.md); the app shows a banner while it is missing).
Point SPEECH → REMOTE SERVER at an OpenAI-compatible endpoint — a LiteLLM gateway in front
of a faster machine, for instance — and transcription happens there instead.

**API keys are encrypted** with Windows DPAPI before they touch `settings.json`; they are
never stored in plain text.

## Where things live

`%LOCALAPPDATA%\VoxScribe\` holds `settings.json`, `dictionary.txt`, `transcripts.jsonl`
and `models\parakeet-v2\`. Delete the folder and the app starts fresh.
