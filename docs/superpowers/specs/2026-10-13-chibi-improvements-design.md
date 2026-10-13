# Chibi improvements

Date: 2026-10-13 · Status: approved design, not yet implemented

A pass over the nine shipped mascots (`docs/superpowers/specs/2026-10-13-chibi-mascot-design.md`):
a soft 3D look for all of them, and one signature improvement each. Mockup:
`.superpowers/brainstorm/391-1791890241/content/chibi-improve.html` (the servo-skull's wings in
it were rejected; everything else was approved).

## Soft relief (all mascots but Clawd)

- Every character colour becomes a radial gradient: lit from the top-left (centre and origin at
  35 % / 28 % of the shape, radius 80 %), stops at 0 → colour lightened 28 % toward white,
  0.55 → the colour, 1 → darkened 22 % toward black. One helper on `ChibiFace` does it, so the
  characters keep calling `Rgb(...)`.
- Clawd stays flat: pixel art does not take gradients.
- The accent brush stays solid (it glows; a gradient under a glow reads as mud).
- A soft ground shadow under every mascot, drawn by the base (ellipse centred 60,134, radii
  30 × 4.5, black at 28 %). The slime's own shadow goes.
- **Red rule.** The red scan in `ChibiTests` must read every gradient stop, not only solid
  brushes; a darkened stop must never become red. The Clawd exemption stays exactly as it is.

## One improvement per mascot

| Mascot | Pose | Improvement |
|---|---|---|
| Servo-skull | Listen | **The writing quill**: a mechadendrite arm holds a quill that scribbles on the parchment. No wings. |
| Netrunner | Think | **Holo screen**: a translucent cyan panel floats in front; lines of text type in, staggered. |
| Wizard | Listen | **Grimoire**: an open book floats in front; its right page turns. |
| Clawd | Think / Idle | **Claude Code spinner**: the ✻ glyph turns above it with "Thinking…" while thinking; its legs trot in opposition while idle. |
| Owl | Think / Done | **Swivel**: the head swings ±40° while thinking (instead of the ±8° tilt); a wink when done (one happy eye, one open). |
| Vintage mic | Listen | **ON AIR**: an amber "ON AIR" plaque lights above the mic, and rings spread from the grille. Amber, never red. |
| Cat | Think / Done | **Keyboard cat**: it types on a mini keyboard while thinking (paws alternate); a raised maneki-neko paw when done. |
| Slime | Listen | **It swallows your words**: letters fall into the jelly and fade. |
| Dragon | Idle | **Nap**: "z z Z" drift up while it dozes. |

## Motion

New moves, all pure and quantised in `ChibiMotion` like the others:

- `Swivel` — a rotation of ±40° over 2.4 s, whole degrees.
- `Ring` — scale 0.9 → 1.5 and opacity 0.8 → 0 over 1.2 s, staggered by index.
- `Fall` — down 0 → 52 px and fading out over 1.4 s, staggered by index.

Existing moves are reused elsewhere (`Wag` for the quill, `Pulse` for typing lines and the ON AIR
light, `Flap` for the page, `Spin` for the ✻, `Wave` for paws and legs, `Rise` for the z's).

## Testing

- The existing per-mascot suites (every phase, both modes, no red, bubble, dot) cover the new
  drawings once they exist; the red scan learns gradient stops.
- `ChibiMotionTests` gains range and quantisation tests for `Swivel`, `Ring` and `Fall`.
- A test pins that a shaded character colour is a gradient and Clawd's are solid.
- Hand check after install: each mascot in each state looks right at 96 px in light and dark.

## Out of scope

- Sprite-based mascots (separate spec).
- Eyes following the mouse, size setting.
