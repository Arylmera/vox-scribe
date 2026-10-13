# Chibi desktop mascot

Date: 2026-10-13 · Status: approved design, not yet implemented

An optional little character that lives on the desktop permanently and replaces the dictation
pill: it dozes while idle, listens while you speak, thinks while the tail is transcribed and
cleaned, and celebrates when the text lands. Off by default; the pill stays the default
dictation indicator.

Mockups (SVG, animated, all four states of every character):
`.superpowers/brainstorm/1026-1791882673/content/chibi-all.html`.

## Characters

Nine, all vector, all drawn in code. Each has one **accent part** that takes the accent colour
(see *Accent*); everything else is the character's fixed identity.

| Id | Name | Look | Accent part |
|---|---|---|---|
| `servo-skull` | Servo-skull | Floating skull, bionic eye in a brass ring, cables, a parchment hanging below | Eye lens, antenna tip, vox grille glow |
| `netrunner` | Netrunner | Neon hair, hood, visor | Visor and its equaliser |
| `wizard` | Wizard | Starred pointy hat, beard, staff with a crystal | Robe and hat |
| `clawd` | Clawd | Claude Code's pixel crab: terracotta block (Anthropic's official #D97757 — inside the red detector's window; the owner's explicit exemption, Clawd only), two eyes, stubby arms, four legs. Drawn aliased, as pixels | Listening waves and sparkles only — the body stays terracotta |
| `owl` | Owl scribe | Round glasses, ear tufts, quill under the wing | Glasses rims |
| `mic` | Vintage mic | 1950s chrome broadcast mic with a face | Grille glow and rising notes |
| `cat` | Cat | Big sitting cat, tail | A collar (new, not in the mockup) |
| `slime` | Slime | Rimuru-style: an almost spherical light-blue drop, small peak, big shine | Sparkles and glow — the body stays blue |
| `dragon` | Little dragon | Small green dragon, wings, horns | Wing membranes |

Clawd is Anthropic's mascot and the netrunner/servo-skull/slime are game- or anime-inspired.
The repository is public; the owner has accepted that. None of them uses an official logo.

Game characters (mimic chest, 8-bit knight, tamagotchi, …) are a later wave, not this spec.

## States

The mascot reads the same frame the pill does (`PillState`), plus one new phase.

| Phase | Mascot does |
|---|---|
| `Idle` (new) | Sleepy eyes, slow float/bob, occasional blink |
| `Recording` | Eyes open; mouth / equaliser / squash driven by the **real input level** (`PillState.Level`); **red recording dot** on the character |
| `Working` | Looks up, "…" dots, character flourish (eye focusing, runes, smoke, tail wag) |
| `Latency` | Happy `^^` eyes and a hop, then back to `Idle` |
| `Notice` | Worried face, notice text in the bubble |

**Red means recording** still holds: the only red on screen is the recording dot, shown during
`Recording` only. No character colour is red.

## Speech bubble

Next to the mascot, shown only when there is something to say: the live preview text and the
mode badge (RAW / CLEAN / CMD / EDIT) while dictating, the notice while one lingers, the
latency (and cleanup time / fallback) after a finish. Hidden while idle. The bubble uses the
active theme's surface/text tokens, so it is covered by `ContrastTests` like any other ground.

## Window

`HudWindow` keeps its polling state machine and gains a **mascot mode**:

- The face is a `ChibiFace` instead of a `PillFace` (both fed `PillState`).
- In mascot mode the window **does not hide when idle**: `Sync` sends `PillPhase.Idle` where it
  now calls `Hide()`. Notice and latency lingers are unchanged.
- **Draggable**: hit-testing on; dragging the character moves the window
  (`BeginMoveDrag`). Position is saved on drop and restored at startup. A saved position that
  lands on no current screen (monitor unplugged) falls back to bottom-right of the primary
  working area.
- **Still never takes focus.** `Overlay.MakeOverlay` gets a variant that keeps
  `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` but drops `WS_EX_TRANSPARENT`, so clicks land on the
  mascot without activating it. The text must keep going to the caret.
- Right-click opens a small menu: change character, hide mascot, settings. Double-click (the
  press's ClickCount = 2) opens the main window.
- Fixed size, about 100 px tall plus the bubble. A size setting can come later.
- The window sizes to its content (the bubble collapses when idle), anchored at its
  bottom-left corner, so only the character blocks clicks while idle.

Switching pill ⇄ mascot or changing the character happens live, like a theme change (rebuild
between dictations, never mid-dictation).

## Animation and rendering

- All motion (bob, blink, hop, mouth, equaliser, glow pulse, spin, wag, flap, squash, rising
  notes/smoke) is a **pure function of time and input level** (`ChibiMotion`), applied from
  `HudWindow`'s existing 33 ms tick. Values are quantised (half pixels, 6°, 0.05 scale), so an
  idle frame that changes nothing sets unchanged values and repaints nothing.
  Composition/keyframe animations were considered and rejected: continuous motion repaints
  every frame either way, and animating offsets fights layout.
- The `Latency` hop is a decaying bounce.
- Soft glow (`DropShadowEffect`) on the accent part while listening.
- Mockup path data ports directly: Avalonia's `Geometry.Parse` reads SVG path syntax.
- Clawd renders aliased (`RenderOptions.EdgeMode = Aliased`) so its pixels stay crisp.
- Character colours and drawing numbers are `private const` in each character's file; sizes
  shared with the window are tokens.

## Accent

- Default: the mascot's accent part follows the **active theme's accent** (light/dark value
  per Windows mode), and recolours when the theme changes.
- **Override**: the user may pin a mascot accent, chosen from the accent swatches of the theme
  catalog (all themes' `AccentVariant`s). Those are already scanned by `DoctrineTests`, so an
  override can never be red.

## Settings

`AppSettings` gains:

- `Mascot` — character id, or null for the pill (default null). An unknown id falls back to
  the pill.
- `MascotHidden` — bool, the tray/right-click "hide" state. While hidden, dictation shows the
  pill as today, so feedback is never lost.
- `MascotAccent` — accent variant id, or null to follow the theme.
- `MascotX`, `MascotY` — saved position in pixels, or null for the default corner.

Appearance section: "Dictation indicator: Pill / Mascot", the nine characters as a picker
with a live preview, and "Mascot accent: Theme / swatches". The tray menu gets "Show/Hide
mascot".

## Code layout

- `Views/Chibi/ChibiFace.cs` — base: shared bob/hop/blink, bubble, recording dot, accent
  brush, `Update(PillState)`.
- `Views/Chibi/<Name>Chibi.cs` — one per character, drawing only.
- `PillPhase.Idle` added; `PillFace` implementations ignore it (the pill is hidden then).
- `HudWindow` — mascot mode, idle phase, drag + position save, overlay variant.
- `Overlay` — non-transparent variant.
- `AppSettings`, `AppearanceSection`, tray menu in `App.axaml.cs`.

## Testing

- Headless: every character renders every phase in light and dark without throwing, and
  exposes its recording dot only in `Recording` (extend `PillTests`).
- Doctrine: no character constant is red (same distance test `DoctrineTests` uses), and the
  bubble's text/ground pairs pass `ContrastTests`.
- Mode and placement are pure functions (Mascots.Active, MascotPlacement.Anchor), unit-tested;
  HudWindow itself is covered by the hand check (it needs a live DictationEngine).
- Position fallback: a saved point outside every screen resolves to the default corner (pure
  function, unit-tested).
- **By hand, required**: drag the mascot while a text field has the caret, then dictate — the
  text must land in the field. Check after a full-screen app takes the top of the z-order, and
  with the mascot on a second monitor.

## Out of scope

- Eyes following the mouse across the screen (needs cursor polling through the platform
  layer) — later.
- Size setting, user-supplied sprites, Lottie.
- Game character wave.
