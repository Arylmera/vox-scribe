# Working on this repo

Read this before changing anything. It is written for a coding agent picking the project up
cold, and it is mostly a list of things that look wrong but aren't, plus things that look
fine and will bite you.

---

## What this is

**VoxScribe** — push-to-talk dictation for Windows. Hold a key, talk, release, and
cleaned-up text is typed into whatever had focus. C# on .NET 10, Avalonia for the UI,
Parakeet through sherpa-onnx for speech, all under `windows/`.

It works and is in daily use, shipped as 1.0.0: push-to-talk with a recordable chord,
streaming transcription while you speak, a dictation pill, tray, start-at-login, an
installer, and the Void Glass theme with a user-selectable accent. Local Parakeet and a
remote OpenAI-compatible STT gateway are both wired and both exercised by hand.

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

**Two shortcuts, and the longer one wins.** Binding Right Shift for raw and Left Shift +
Right Shift for cleanup means both chords are satisfied by the second gesture. The plain
hook is given the keys that belong only to the cleanup chord and stands aside while any of
them is held. Remove that and the raw shortcut silently eats every cleanup dictation.

**`PushToTalkHook` must never hold per-instance state in a static.** It did — a callback and
a "current instance" — which made it a singleton: a second hook overwrote the first, and the
loser reported a successful install and then never saw a keystroke. There are two hooks now.

---

## Design system

`windows/src/VoxScribe.App/Design/DesignTokens.cs` defines every colour, size, radius and
duration token. **Views must not contain literal values.** If a component needs a number that
isn't a token, add the token rather than inlining it.

The direction is **Void Glass**: a cool near-black ground (`#0A0D12`), flat glass cards with
hairline borders, generous radii, pill buttons, Segoe UI Variable and Cascadia Mono. The
accent is a user setting — `Tokens.Colors.Accent` is mutable and applied live — so nothing
may hard-code it.

Two rules that are not negotiable, and `VoxScribe.App.Tests/UiTests.cs` pins them:

- **Red means recording.** Nothing else in the app is red.
- **Amber and green are instrumentation only** — level meters, never UI chrome.

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

1. **Command Mode** — dictate at a Claude Code session instead of at a text field.
2. **Onboarding** — a first-run window, and model download from inside the app rather than
   by following `docs/PARAKEET-WINDOWS.md` by hand.
3. **Code signing.** The installer is unsigned, so users meet SmartScreen.

## What no amount of CI can verify

The cleanup pass has never run against a reachable gateway. Its guard is covered by tests;
its network path is one `catch` whose entire contract is "return the original text on any
failure", and the latency it adds between the key release and the text appearing has never
been measured on real hardware.

Everything the platform layer touches is behind an interface and tested with fakes. The
bindings themselves are not, and two real bugs — the hook singleton and the chord overlap —
lived happily behind green tests because those tests drive the engine through
`FakeHotkeySource` and never install a real hook. **Anything touching `PushToTalkHook` has to
be tried by hand.**
