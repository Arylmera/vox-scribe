# VoxScribe

Push-to-talk dictation for Windows. Hold a key, talk, release — cleaned-up text lands in
whatever text field has focus. Native, and on-device by default.

**Status:** shipped as 1.0.0 and in daily use.

The app lives in [`windows/`](windows/). Start there — [`windows/README.md`](windows/README.md)
covers building, the model, and what is and isn't verified.

---

## What it does

Hold the push-to-talk key and speak. Phrases are transcribed **while you are still talking**,
so the wait after you release the key is the length of the tail rather than of the whole
dictation. A pill at the bottom of the screen shows the level and the text as it arrives.

**Two shortcuts, two behaviours.** The first types the transcript as it was heard. The
second sends it to a small language model first — punctuation, capitalisation, filler words
— and types the repaired line. The choice is made when you speak, not in a settings toggle,
and the pill's badge says which one is running.

**Speech runs locally or remotely.** Parakeet through sherpa-onnx on the CPU needs nothing
but the model on disk. Point the app at an OpenAI-compatible endpoint instead and
transcription happens there — a LiteLLM gateway in front of a faster machine, for instance.

**A correction dictionary** rewrites the words a speech model reliably gets wrong — names,
jargon, glued-together compounds. Its behaviour is specified by
[`shared/dictionary-test-vectors.json`](shared/dictionary-test-vectors.json), not by the
code.

Every feature — shortcuts, undo, the pill's states, settings — is walked through in the
**[user guide](docs/GUIDE.md)**.

---

## Quick start

```bash
cd windows
dotnet publish src/VoxScribe.App/VoxScribe.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish
iscc installer\voxscribe.iss
```

Then run `installer/Output/VoxScribe-Setup-1.0.0.exe`. Settings, transcripts, the dictionary
and the speech model live in `%LOCALAPPDATA%\VoxScribe`.

Transcription needs either a downloaded Parakeet model — see
[`docs/PARAKEET-WINDOWS.md`](docs/PARAKEET-WINDOWS.md) — or a remote endpoint configured in
Settings.

---

## There was a macOS build

A Swift/SwiftUI version ran on macOS and shared the dictionary contract with this one. It was
removed on 2026-08-28 to leave a single app in the tree while the Windows side is the one
being worked on. Nothing is lost — `git log -- Sources/` has all of it — and it can come back
when it is wanted.

`shared/dictionary-test-vectors.json` stays where it is. It is still the specification for
correction behaviour, and it is still written as a contract between two implementations even
though only one currently exists.

---

## For contributors

[`AGENTS.md`](AGENTS.md) is the file to read first. It is a list of things that look wrong
and aren't, and things that look fine and will bite you — pinned package versions, why the
platform layer is loaded by reflection, why the keyboard hook must not swallow keys, and
which parts no amount of CI can verify.
