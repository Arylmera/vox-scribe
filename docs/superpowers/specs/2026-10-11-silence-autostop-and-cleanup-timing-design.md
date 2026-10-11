# Silence auto-stop and cleanup timing

Date: 2026-10-11 · Status: approved design, not yet implemented

Two small changes to the dictation engine, shipped together because they touch the same
loop. The follow-up feature (rewrite a selection by voice) gets its own spec.

## 1. Auto-stop on silence (toggle mode only)

### Behaviour

- Applies **only when `ToggleMode` is on**. In push-to-talk the key release already ends
  the dictation; nothing changes there.
- Applies to every toggle dictation: raw, cleanup and command chords alike.
- The dictation ends — exactly as if the toggle key had been pressed again — once the user
  **has spoken** and then stayed quiet for `AutoStopSilenceSeconds`.
- Before any speech, the silence counter does not arm: a user who has not started talking
  keeps recording until they press the key again or Esc.
- `AutoStopSilenceSeconds`: default **3**, range **1–10**, **0 = off**.

### Design

- `StreamingSegmenter` (Core) exposes two read-only properties:
  - `HeardSpeech` — true once any chunk's RMS reached `SilenceFloor` (the same absolute
    floor `HasSpeech` uses; the peak-relative part of the threshold is meaningless before
    the first word, since the first chunk *is* the peak).
  - `TrailingQuietSeconds` — continuous quiet at the end of the recording. Kept in its own
    counter that **`Take()` does not reset**; the existing `_quietSamples` is reset on every
    cut, so a 0.6 s segment cut would otherwise restart it and auto-stop would never fire.
- `DictationEngine`:
  - new property `AutoStopSilenceSeconds` (double, 0 = off).
  - in the `BeginAsync` capture loop, after `Accept`: if `ToggleMode`, the setting is > 0,
    `HeardSpeech`, and `TrailingQuietSeconds >= AutoStopSilenceSeconds`, call the same end
    path the second toggle press uses (`EndAsync`), once. The loop's existing
    `State != Recording` guard stops consumption afterwards.
- `AppSettings`: `AutoStopSilenceSeconds` (int, default 3; 0 = off). Wired in
  `Composition.cs` next to `ToggleMode`, both at construction and on settings change.
- Settings UI (`ShortcutsSection`, next to the toggle switch): an on/off switch plus a
  1–10 s slider. Both disabled while toggle mode is off. Values come from `DesignTokens`.

## 2. Cleanup timing

### Behaviour

For every dictation that went through cleanup, record whether cleanup **succeeded** and how
long it **took**. A failure is any fall-back to the raw text: timeout, network/HTTP error,
unparseable body, or a candidate rejected by `TextCleaner.Accept`. A model answer identical
to the input is a **success**.

Shown in three places:

- **Pill**, after a cleaned dictation, in the latency phase:
  - success → `1.2s · clean 0.7s`
  - failure → `1.2s · clean failed`
  - no cleanup → `1.2s` (unchanged)
  - If a face is too narrow for that text, it uses the short form `1.2s · 0.7s` (failure:
    `1.2s · raw`) rather than growing. Checked per theme.
- **History** row meta: `· cleanup 0.7s`, or `· cleanup failed` in the `Caution` colour.
- **Home**: one quiet line, only when at least one cleanup ran this week:
  median cleanup time this week and the number of failures.

### Design

- `TextCleaner`: new `CleanupOutcome(string Text, bool Succeeded)` returned by a new
  `TryCleanAsync`. `CleanAsync` stays as a thin wrapper returning `.Text`, so its contract
  ("return the original text on any failure") is unchanged. `Accept` reports rejection so
  `TryCleanAsync` can tell a rejected candidate from an accepted one.
- `DictationEngine.Cleanup` becomes `Func<string, CancellationToken, Task<CleanupOutcome>>?`.
  The engine times the call with `IClock` (`_clock.Now` before/after).
- `DictationResult` gains `TimeSpan? CleanupTime` and `bool? CleanupFailed` (both null when
  no cleanup ran, including incremental mode where cleanup is skipped).
- `TranscriptRecord` gains `double? CleanupSeconds` and `bool? CleanupFailed`. Nullable, so
  existing `transcripts.jsonl` lines read back unchanged. Filled in `Composition.cs`.
- `HomeStats`/`HomeStatsResult`: `TimeSpan? MedianCleanupThisWeek`, `int CleanupFailuresThisWeek`.
- `PillState` gains the cleanup time and outcome; `HudWindow.OnEngineCompleted` stores them
  beside `_lastLatency`. Formatting lives in `PillFace.Timer()` (shared) and in
  `MonoPill`'s own timer text.

## Testing

- `StreamingSegmenter`: trailing quiet keeps accumulating across a segment cut;
  `HeardSpeech` stays false on room noise below the floor.
- `DictationEngine` (fakes): in toggle mode, speech then N s of silence ends the dictation;
  it does **not** end in push-to-talk, without prior speech, or with the setting at 0.
- Cleanup: a failing cleanup yields `CleanupFailed = true` and the raw text is typed; the
  duration is measured from a fake clock; no cleanup → both fields null.
- `TextCleaner`: `TryCleanAsync` reports failure on timeout, bad body and rejected
  candidate; success on an identical answer.
- `HomeStats`: median and failure count over the week window.
- `ContrastTests`: add the `Caution` text on the history row ground pair (4.5:1, all themes,
  modes, variants).
- By hand: toggle dictation auto-stops on a real microphone; every pill face shows the
  cleanup text without clipping.

## Out of scope

Silero VAD, auto-stop in push-to-talk, per-app silence durations.
