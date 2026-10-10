# Working on this repo

Read this before changing anything. It is written for a coding agent picking the project up
cold, and it is mostly a list of things that look wrong but aren't, plus things that look
fine and will bite you.

---

## What this is

**VoxScribe** — push-to-talk dictation for Windows. Hold a key, talk, release, and
cleaned-up text is typed into whatever had focus. C# on .NET 10, Avalonia for the UI,
Parakeet through sherpa-onnx for speech, all under `windows/`.

It works and is in daily use (version in `windows/Directory.Version.props`): push-to-talk with a recordable chord,
streaming transcription while you speak, a dictation pill, tray, start-at-login, an
installer, and five selectable themes (Paper, Orb, Tide, Mono, Fluent), each with light and
dark palettes and curated accents. Local Parakeet and a remote OpenAI-compatible STT gateway
are both wired and both exercised by hand.

**There was a macOS build in Swift, and it was deleted on 2026-08-28** at the owner's
request, to leave one app in the tree while the Windows side is the one being worked on. It
is not gone — `git log -- Sources/` still has all of it, and it will come back when it is
wanted. Two things it left behind on purpose:

- `shared/dictionary-test-vectors.json`, still the specification for correction behaviour
  and still linked into the Windows test project. It is a contract with a second
  implementation that does not currently exist; keep it honest anyway.
- The regex safe subset below, which exists because the two engines disagreed.

---

## The one rule that matters

**`shared/dictionary-test-vectors.json` is the specification for correction behaviour.**

Change the vectors first, watch the tests go red, then make them green. Never edit the
implementation to satisfy a failing vector — the vector is the spec, the code is not.

```bash
cd windows && dotnet test VoxScribe.CrossPlatform.slnf
```

The file is referenced by link from `windows/tests/VoxScribe.Dictionary.Tests`, not copied,
so there is no second copy to drift.

---

## Things that look like bugs and are not

**`dotnet build VoxScribe.sln` fails on macOS** with `NETSDK1073`. Expected —
`VoxScribe.Platform.Windows` targets `net10.0-windows`. Use `VoxScribe.CrossPlatform.slnf`,
which omits it; everything else, including the whole UI suite, builds and tests on macOS in
about half a second.

**The cleanup pass does nothing while incremental injection is on.** By design: in that mode
every phrase is typed the moment it is transcribed, so by the end of the utterance there is
nothing left to improve. The pill's badge reads RAW rather than CLEAN, because it reports
what will actually happen and not which key was pressed.

**Four chords, and the longer one wins.** Binding Right Shift for raw and Left Shift +
Right Shift for cleanup means both chords are satisfied by the second gesture. Each hook is
given the keys that belong only to a longer chord containing its own (`Composition.Blockers`)
and stands aside while any of them is held. Remove that and the shorter shortcut silently
eats every dictation meant for the longer. Unbound chords are empty arrays on live hooks,
not null hooks, so binding one for the first time needs no restart.

**Command mode types nowhere if it cannot find its window.** The command chord's text goes
to the first visible window whose title contains `CommandWindowTitle`, then Return. If no
window matches, or it will not come forward, the engine posts a notice and types nothing —
a prompt meant for Claude landing in the focused spreadsheet is the worse failure.

**The Claude Code plugin is almost empty on purpose.** `claude-plugin/` (listed by
`.claude-plugin/marketplace.json`, so `/plugin marketplace add Arylmera/vox-scribe` works)
holds `/parle`, `/say` and a PowerShell hook that only drops the transcript path and the
command name into `%LOCALAPPDATA%\VoxScribe\speak\request.json`. Finding the reply
(`ReadAloud.LastReply`), rewriting and speaking it all live in the app, where they are tested.
The hook must stay Windows PowerShell 5.1 compatible and saved with a BOM.

**One `WH_KEYBOARD_LL` hook per process, in `KeyboardHook`.** Every shortcut is a listener on
it, not a hook of its own. `PushToTalkHook` once kept a callback and a "current instance" in
statics, which made it a singleton: a second hook overwrote the first, and the loser reported
a successful install and then never saw a keystroke. The statics in `KeyboardHook` are a
listener registry, which is the opposite arrangement — but keep chord state per instance.

---

## Design system

`windows/src/VoxScribe.App/Design/DesignTokens.cs` defines every colour, size, radius and
duration token. **Views must not contain literal values.** If a component needs a number that
isn't a token, add the token rather than inlining it. A number only one control's own drawing
uses (a pill face's width, a wave's frequency) is a named `private const` in that control.

**Five themes, one skeleton.** A theme is data — `Design/ThemeCatalog.cs`: a light and a dark
`Palette`, curated `AccentVariant`s (each with a light and a dark value), its fonts, and a
handful of layout knobs (`ThemeModel.cs`). `Themes.Apply(theme, variant, dark)` writes them into
`Tokens` and raises `Themes.Changed`; every window rebuilds its C#-built content on that
event. The app follows Windows light/dark through Avalonia's `ActualThemeVariant` — no Win32.
Paper is the default; any unknown or retired theme id falls back to it.

The main window is one sidebar shell (Home · History · Dictionary · Settings) for every theme.
Only the dictation pill differs per theme: `Views/Pill/*Pill.cs`, one `PillFace` each, fed by
`HudWindow`'s unchanged polling state machine. A pill face captures its theme's colours at
construction, so it never recolours mid-dictation; the pill swaps faces between dictations
instead. A theme change rebuilds windows live and never restarts the app; `App.Restart()`
survives only for the Parakeet download on the Speech page. `MainWindow` and `HudWindow` hold their
`Themes.Changed` subscription for the window's lifetime and drop it on close. The page views
(`HomePage`, `TranscriptionsView`, `DictionaryView`) subscribe to their data's `Changed` events
only while attached to the visual tree, so the instances a rebuild discards stop refreshing
instead of leaking. The mockups that specify
all of it live in `.superpowers/mockups/`.

Fonts: Instrument Serif, Geist, Geist Mono, Figtree and JetBrains Mono are bundled under
`Assets/Fonts/` with their OFL licences; Fluent uses system Segoe UI Variable and Cascadia Mono.

Two rules that are not negotiable, pinned by `VoxScribe.App.Tests/Design/`:

- **Red means recording.** `#E5484D` is the recording dot and nothing else, in every theme —
  `DoctrineTests` scans every colour token of every theme, mode and variant.
- **WCAG AA.** `ContrastTests` checks every text/ground pair the views draw, 4.5:1, across all
  themes, modes and variants. Accent text never sits on `Hover`.

Green and amber are ordinary colours now (`Positive`, `Caution`, accent variants) — the old
"instrumentation only" rule is gone.

---

## Windows specifics

The specifics below were expensive to establish and several were found the hard way. Treat
them as load-bearing. Full detail in `windows/README.md` and `docs/PARAKEET-WINDOWS.md`.

**Three pinned versions that break silently at "latest":**

| Package | Pin | Why |
|---|---|---|
| `NAudio` | 2.3.0 | 3.x targets .NET 9+ and will not restore |
| `Avalonia.Headless.XUnit` | 11.3.20 | 12.x requires xUnit **v3**, a different package line |
| `org.k2fsa.sherpa.onnx` | 1.13.5 | Bundles ONNX Runtime — never also reference `Microsoft.ML.OnnxRuntime` |

**Right Alt is AltGr** on German, Polish, UK, Nordic and most Latin-American layouts. Binding
push-to-talk there — and especially suppressing it — breaks typing `@`, `€`, `\`, `|` for
those users. Default is **Right Ctrl**, and the hook **observes without swallowing**: if the
key-down is swallowed and the key-up escapes, the target app believes Ctrl is held forever.

**UI Automation cannot inject text.** `TextPattern` is documented read-only and
`ValuePattern` replaces a whole field rather than inserting at the caret. `SendInput` is the
primary path, not a fallback.

**`VoxScribe.App` loads the platform layer by reflection, not by reference.** A direct
reference would force the UI onto `net10.0-windows` and you would lose the ability to run it
on your own machine. Three consequences that have each bitten once: the assembly is invisible
to `PublishSingleFile`, so it is published as a loose file beside the exe *and* resolved by an
explicit `AssemblyLoadContext` handler; the published self-test checks this, because when it
breaks the app starts perfectly and then does nothing at all when the key is pressed; and the
`PublishWindowsPlatformLayer` target must strip `RuntimeIdentifier` from the inner build, or
it lands in `net10.0-windows/win-x64/` while the copy reads the RID-less path — invisible for
as long as a stale DLL sits there, and a hard failure in a clean tree.

**Keep `VoxScribe.Platform.Windows` logic-free.** Anything living there is code CI cannot
exercise. Retries, debouncing and device-change handling belong in the platform-neutral
projects behind an interface — those target plain `net10.0`, so `CA1416` turns any accidental
Win32 call into a build error.

**CI compiles with warnings as errors** and the analyzers are strict on purpose.
`--no-incremental` is mandatory: Roslyn does not re-emit analyzer warnings on a cached build,
so without it the gate proves nothing.

**Data lives in `%LOCALAPPDATA%\VoxScribe`** — settings, transcripts, dictionary, and the
Parakeet model. `DataDirectory` migrates the old `Murmur` folder into it once, and only into
an absent destination.

---

## Regex, if you touch the dictionary

The dictionary's regexes were written to run identically under ICU and .NET, because they
once had to. Two rules survive from that and should not be removed:

- `RegexOptions.CultureInvariant`, or Turkish `İ` matches `i`.
- **NFC normalization.** Decomposed input otherwise means an accented trigger silently never
  fires.

Two known divergences are simply avoided: ICU folds `ß` to `ss` and .NET doesn't; .NET's `.`
splits surrogate pairs. Stay inside the safe subset — `\b`, `\d`, `\w`, `\s`, character
classes, greedy/lazy quantifiers, alternation, `(?<name>…)`, fixed-length lookbehind,
lookahead, `\p{L}`, and `$1`–`$9` in replacements. Nothing else.

---

## What isn't built

1. **Code signing.** The installer is unsigned, so users meet SmartScreen.

Built since this list was first written: a DOWNLOAD MODEL button on the Speech page that
fetches Parakeet v3 (`ModelDownloader`) — never automatically, since cleanup and read-aloud
always need the user's own endpoint anyway, a GitHub release published by CI on a `v*` tag, and one version source,
`windows/Directory.Version.props` — the installer refuses to build without it.

## What no amount of CI can verify

The cleanup pass runs daily against the owner's LiteLLM gateway; its alias must be a
non-thinking model (`local-light`, about 0.65 s per call in September 2026 — `local-ops`
thinks past the 5 s timeout and returns nothing). Its network path is one `catch` whose
entire contract is "return the original text on any failure". The end-to-end delay it adds
after key release has not been measured separately: transcripts record processing time but
not whether cleanup ran.

**Muting other apps while dictating was tried and removed** (October 2026, `git log -S
MicrophoneMuter`). Per-app WASAPI session mute (`ISimpleAudioVolume`) on a capture endpoint
silenced *every* stream on that microphone, VoxScribe's own included, regardless of format or
`AUDCLNT_STREAMOPTIONS_RAW`. Its tests ran against a fake mixer and the hand check only
watched Discord's meter, never whether VoxScribe still heard anything. Do not bring it back
without a two-process test on real hardware.

Everything the platform layer touches is behind an interface and tested with fakes. The
bindings themselves are not, and two real bugs — the hook singleton and the chord overlap —
lived happily behind green tests because those tests drive the engine through
`FakeHotkeySource` and never install a real hook. The published self-test now installs two
real hooks and taps a key through `SendInput`, which covers the singleton class of bug.
**Anything touching `KeyboardHook` still has to be tried by hand with a real keyboard.**
