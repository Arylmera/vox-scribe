# Claude Code panel for the voxscribe plugin — design

Date: 2026-10-10. Status: approved by the owner in conversation; this is the written record.

A band above Claude Code's prompt, drawn by the `voxscribe` plugin, that:

1. offers **/parle** and **/say** as buttons;
2. lets one session be **armed** as the target of the command chord, so a dictation made
   from any window lands in *that* session — no window-title search, no focus needed;
3. offers a **click-to-talk** button (Parler → Envoyer / Annuler) that does the same from
   the band itself.

Plus an **Install / Uninstall** control for the plugin in Vox-Scribe's Settings.

Everything is additive. Existing features — dictation, raw, cleanup, command mode by window
title, `/parle` and `/say` typed by hand — behave exactly as today.

---

## 0. Principles

**Failure isolation.** If Vox-Scribe fails, only Vox-Scribe fails. Neither Claude Code nor
the application being dictated into may be harmed by this feature: a broken module loses
its band, never the prompt; a missing target falls back to today's behaviour.

**The module stays dumb.** AGENTS.md: the plugin is almost empty on purpose, because the
module gets no CI. It draws the band, writes intent files, polls its outbox and calls
`$.prompt.submit`. Arming rules, staleness, fallback and acknowledgement live in C# in
`VoxScribe.Core` (plain `net10.0`), tested against fakes.

**Experimental API.** Plugin modules (`hooks.json` `"modules"`, `ui.render` `AbovePrompt`,
`$.prompt.*`) are undocumented. Reference: HeyCubit/effortless `hooks/register.tsx` and
`types/index.d.ts`. In the terminal they load only with
`CLAUDE_CODE_ENABLE_FUNCTION_HOOKS=1` in `~/.claude/settings.json` `env`; that flag enables
module code for *every* plugin. Vox-Scribe never sets it (§5).

---

## 1. Step one: throwaway feasibility spike

Before any real code, a throwaway module (one button, one timer) tried by hand. It must
answer, on this machine:

| # | Question | If no |
|---|---|---|
| 1 | Can `"modules"` sit next to the existing `"hooks"` key in `hooks/hooks.json` without dropping the `/parle` `/say` hook? (effortless's file holds only `modules`.) | Find the supported layout (second file, plugin.json key) before going on. |
| 2 | Does `$.prompt.submit` called from a `$.clock.every` callback land in an **unfocused** session — terminal with the flag, inside Orca, and the desktop app? | Arming and click-to-talk collapse; only the /parle /say buttons remain. Redesign. |
| 3 | What does `$.prompt.submit` do while a turn is running (`e.props.isWorking`): queue, fail, interrupt? | Record it; if it fails, the module holds the text until idle. |
| 4 | Does `$.command.run({ command: 'voxscribe:parle' })` fire the existing `UserPromptExpansion` hook? | Buttons write `speak/request.json` themselves, if the session exposes its transcript path. |
| 5 | Does `$.fs` offer delete/rename, or only read/write? | Use the ack-only variant of §3 step 3. |

The spike's findings are written into this spec before the plan continues.

---

## 2. Architecture

```
 Claude Code (each session)          %LOCALAPPDATA%\VoxScribe\claude\        VoxScribe.App
 module register.tsx  ── writes ──▶  target.json   mic.json          ◀── reads ── ClaudeBridge
  draws the band                     outbox/<session>.json, <id>.ack ◀── writes ─ (VoxScribe.Core)
  polls outbox, submits ◀─ reads ──  status.json
 hook speak.ps1 (/parle, /say) ───▶  speak\request.json  (unchanged)
```

### Module guarantees (towards Claude Code)

- Every handler is wrapped: on any exception `ui.render` returns `next(e)`. Worst case the
  band disappears; the prompt is untouched.
- Never blocks: bounded file reads only, no `$.process.run`, no waits.
- Missing, unreadable or corrupt file means "nothing to do".
- Touches the prompt only for a valid, fresh `outbox/<own session_id>.json` under a size
  cap. Stale or oversized outbox files are discarded, never sent.
- Circuit breaker: after N consecutive errors it stops polling and the band reads
  "Vox-Scribe désactivé (erreur)". No toast loops, no CPU.
- Intercepts no command (`command.run` untouched): typed `/parle` and `/say` stay with
  `speak.ps1`.

### App guarantees (towards the target application)

- With no live target, the command chord runs today's code path, unchanged.
- Arming affects **only the command chord**. Raw and cleanup dictation are untouched.

### Band (terminal and desktop, words and buttons, one line, always visible)

```
idle, not armed:  ◎ Vox-Scribe   [🎯 Cibler]   [🎙 Parler] [🔊 Parle] [🔊 Say]
armed:            ● Cible active [🎯 Désarmer] [🎙 Parler] [🔊 Parle] [🔊 Say]
dictating:        ● Écoute…  0:12              [■ Envoyer] [✕ Annuler]
app not running:  ○ Vox-Scribe n'est pas lancé
```

Desktop split view: plugin bands draw only in the left pane (known limitation).

---

## 3. Protocol

Directory `%LOCALAPPDATA%\VoxScribe\claude\`, JSON, UTF-8.

| File | Writer | Content | Role |
|---|---|---|---|
| `target.json` | module | `session_id`, `beat` (ms) | Armed session. One file: last writer wins, so arming is exclusive. |
| `mic.json` | module | `session_id`, `action` (`start`/`stop`/`cancel`), `ts` | Click-to-talk requests. |
| `status.json` | app | `state` (`idle`/`listening`/`transcribing`), `session_id`, `since`, `beat` | What the band shows; proof the app is running. |
| `outbox/<session_id>.json` | app | `id`, `text`, `ts` | Text to submit in that session. |
| `outbox/<id>.ack` | module | empty | Delivery acknowledgement. |

Timing: module polls `status.json` and its outbox every ~300 ms; the armed session
refreshes `beat` every 2 s; a target or status older than 6 s is dead. Every band re-reads
`target.json`, so a session shows "inactive" as soon as another takes the target. Disarming
clears `target.json`.

### Chord flow

1. Command chord pressed; recording and transcription exactly as today.
2. At the final step `ClaudeBridge.TryDeliverAsync(text)`:
   - live target → write `outbox/<id>.json`;
   - no live target → **NoTarget**, today's window-title path.
3. Target module sees its outbox: deletes it **first**, then `$.prompt.submit`, then writes
   the ack (at-most-once). If `$.fs` cannot delete (spike Q5): module writes the ack, the
   app deletes the outbox, and the module ignores any already-acked `id`.
4. No ack within 3 s → **NotAcknowledged** → notice "Dictée non livrée à Claude Code". No
   fallback (a late submit would double-send). The text is in History like every transcript.

### Click-to-talk flow

1. Parler → module arms its session (`target.json`), then `mic.json` start.
2. The app treats it as the command chord pressed — same path. A start while a dictation is
   running is ignored, as the engine already does.
3. Envoyer → `stop` (= chord released) → chord flow from step 2. Annuler → `cancel` (the
   existing Cancel chord); nothing sent.

### /parle and /say buttons

`$.command.run({ command: 'voxscribe:parle' | 'voxscribe:say' })`, reusing the existing hook
— no duplicated logic (spike Q4 has the fallback).

---

## 4. App side

**`ClaudeBridge`** in `VoxScribe.Core`, modelled on `ReadAloud`'s `FileSystemWatcher`:

- `TryDeliverAsync(text)` → `Delivered | NoTarget | NotAcknowledged`;
- writes `status.json` on every state change plus a 2 s beat;
- watches `mic.json` and is a **virtual hotkey source** for the command chord (start =
  pressed, stop = released, cancel = the Cancel chord), like `FakeHotkeySource`. The engine
  never knows a button exists.

**One change in `DictationEngine.SendCommandAsync`** (`VoxScribe.Core/DictationEngine.cs`),
at its top:

```
Delivered       → Journal.Record, return
NotAcknowledged → notice, return (no fallback)
NoTarget        → existing code, line for line
```

No new setting: without the module `target.json` never exists, the result is always
NoTarget, and the app behaves as today.

Untouched: `ReadAloud`, `speak.ps1`, `KeyboardHook` (so no new by-hand keyboard check from
this work), every other chord.

---

## 5. Settings: "Claude Code" section

- State: installed / not installed / `claude` not on PATH.
- **Install**: `claude plugin marketplace add Arylmera/vox-scribe`, then
  `claude plugin install voxscribe@vox-scribe`.
- **Uninstall**: `claude plugin uninstall voxscribe@vox-scribe`.
- The existing `CommandWindowTitle` field moves here.
- A note that the plugin applies to new sessions or after `/reload-plugins`.
- **Terminal flag: Vox-Scribe never touches `~/.claude/settings.json`.** The section
  explains that the band needs `CLAUDE_CODE_ENABLE_FUNCTION_HOOKS=1` in the terminal, that
  it enables module code for all plugins, and offers a **Copy the line** button.

Command building and output parsing live in `VoxScribe.Core` behind a process-runner
interface; the view only displays. CLI errors are shown verbatim and the state re-read.

---

## 6. Error handling

| Situation | Behaviour |
|---|---|
| App not running | Band shows "○ Vox-Scribe n'est pas lancé", buttons inert, prompt intact. |
| Module absent, broken or tripped | No `target.json` beat → classic command mode. |
| Armed session died | Beat older than 6 s → classic behaviour. |
| Target alive, no ack | Notice "non livrée", no fallback; text in History. |
| Outbox stale, corrupt or oversized | Discarded by the module, never sent. |
| Start during a dictation | Ignored, as today. |
| `claude` not on PATH | Section says so, buttons disabled. |
| Install / uninstall fails | CLI message shown verbatim; state re-read. |

---

## 7. Testing

CI (C#, load `test-gates` when writing them):

- `ClaudeBridge` with fake filesystem and clock: live/dead target at the 6 s edge,
  exclusivity, acknowledged delivery, ack timeout, outbox written once, `mic.json` →
  start/stop/cancel on the virtual hotkey source.
- `DictationEngine`: existing command-mode tests stay green **unmodified** (NoTarget path);
  NotAcknowledged performs **no** keyboard injection.
- Install/uninstall command building with a fake process runner.

By hand (no CI can see the module):

- the §1 spike;
- end to end: arm session A, focus a browser, dictate with the chord → text in A; close A,
  dictate again → window-title fallback; click-to-talk Parler/Envoyer/Annuler; /parle and
  /say buttons; app closed → band says so.

---

## Out of scope

- Auto-stop on silence for click-to-talk (re-click only; no endpointing in the engine).
- Automatic targeting of the last-used session (arming is explicit).
- SVG/desktop-specific band design.
- Editing `~/.claude/settings.json` from the app.
