# voxscribe — Claude Code plugin

Companion to the Vox-Scribe app (Windows). The app does the work; this plugin only hands it requests.

- `/parle`, `/say` — read the previous reply aloud (French, English).
- A band above the prompt, a pill in your Vox-Scribe theme's accent followed by buttons:
  - **🎯 cibler** arms this session: Vox-Scribe's command shortcut, pressed from any window,
    sends its dictation here. One session is armed at a time; the last one armed wins.
  - **🎙 parler** dictates to this session with a click; **■ envoyer** sends, **✕ annuler** drops it.
  - **🔊 fr / 🔊 en** — the same as typing `/parle` and `/say`.
  - No band means the app is not running (gone at once when it closes, within 6 s if it stops answering); it comes back when the app starts.

## The band in a terminal

The desktop app draws the band as is. A terminal draws it only with this in `~/.claude/settings.json`,
then a restart:

    "env": { "CLAUDE_CODE_ENABLE_FUNCTION_HOOKS": "1" }

This turns on module code for every installed plugin, not just this one. Vox-Scribe never edits the
file; Settings → CLAUDE copies the line for you. Without it, `/parle` and `/say` still work.

## Experimental

The band uses Claude Code's undocumented plugin-module API and may break with a Claude Code update.
If it does, the band disappears; the commands and Vox-Scribe's own shortcuts keep working.
In the desktop app's split view the band draws only in the left pane.

## Developing

`claude plugin validate .` and `claude plugin test .` (the tests are `hooks/*.test.ts`). Claude Code
generates the API types into `.claude-plugin/types/` (git-ignored); `tsconfig.json` points at them.
