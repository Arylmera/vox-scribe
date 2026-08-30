# Wave 4 — Communications Ducking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox ('- [ ]') syntax for tracking.

**Goal:** Open the WASAPI capture stream with the Communications audio category so Windows applies the user's own ducking policy while dictating, behind a "Duck other audio while dictating" toggle (default on).

**Architecture:** `SettingsData` gains a `DuckOtherAudio` bool plumbed through `Composition` → `PlatformFactory.CreateAudioCapture` → a new `WasapiAudioCapture` ctor parameter. The platform layer's only new code is one logic-free call: `device.AudioClient.SetClientProperties(false, AudioStreamCategory.Communications, AudioClientStreamOptions.None)` before NAudio's `WasapiCapture` initializes — NAudio caches `AudioClient` per `MMDevice` instance, so the property applies to the client `WasapiCapture` later reads from the same device object. The actual ducking is OS-driven and covered by a manual test checklist.

**Tech Stack:** .NET 10, Avalonia UI (headless xUnit for UI tests), NAudio 2.3.0 (`NAudio.CoreAudioApi.AudioClient.SetClientProperties`, `AudioStreamCategory`, `AudioClientStreamOptions`), Shouldly.

## Global Constraints

- .NET 10, Avalonia UI; build/test with: cd windows && dotnet test VoxScribe.CrossPlatform.slnf
- NAudio pinned 2.3.0; Avalonia.Headless.XUnit pinned 11.3.20; org.k2fsa.sherpa.onnx pinned 1.13.5 (never reference Microsoft.ML.OnnxRuntime)
- VoxScribe.Platform.Windows stays logic-free; all logic in platform-neutral projects behind interfaces (net10.0, CA1416 guards)
- Views must not contain literal values — every colour/size/radius/duration comes from Design/DesignTokens.cs; add tokens rather than inlining
- Red means recording, nothing else is red; amber/green are instrumentation only (UiTests.cs pins this)
- shared/dictionary-test-vectors.json is the spec for correction behaviour: change vectors first, watch red, then make green
- Dictionary regexes stay in the ICU/.NET safe subset, RegexOptions.CultureInvariant, NFC normalization
- Anything touching PushToTalkHook or real injection must be flagged "manual test required"

---

### Task 1: `DuckOtherAudio` setting in SettingsData

**Files:**
- Create: `windows/tests/VoxScribe.Core.Tests/SettingsDuckingTests.cs`
- Modify: `windows/src/VoxScribe.Core/AppSettings.cs`

**Interfaces:**
- Consumes: `AppSettings(string path)`, `SettingsData Data { get; }`, `void Update(SettingsData data)` (all existing, `VoxScribe.Core/AppSettings.cs`)
- Produces: `public bool DuckOtherAudio { get; init; } = true;` on `SettingsData`

**Steps:**

- [ ] Write the failing test — create `windows/tests/VoxScribe.Core.Tests/SettingsDuckingTests.cs`:

```csharp
using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The ducking toggle: default on, and the persisted "off" survives a JSON round trip —
/// including through the source-generated context (trim/single-file safety).
/// </summary>
public sealed class SettingsDuckingTests
{
    [Fact]
    public void Duck_other_audio_defaults_to_on()
    {
        new SettingsData().DuckOtherAudio.ShouldBeTrue();
    }

    [Fact]
    public void Duck_other_audio_off_survives_a_round_trip()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");

            var settings = new AppSettings(path);
            settings.Update(settings.Data with { DuckOtherAudio = false });

            var reloaded = new AppSettings(path);
            reloaded.Data.DuckOtherAudio.ShouldBeFalse();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
```

- [ ] Run it and watch it fail to compile (`DuckOtherAudio` does not exist):

```
cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~SettingsDucking
```

Expected: build error `CS1061: 'SettingsData' does not contain a definition for 'DuckOtherAudio'`.

- [ ] Minimal implementation — in `windows/src/VoxScribe.Core/AppSettings.cs`, add to `SettingsData` directly after the `AudioDeviceId` property (after line 110):

```csharp
    /// <summary>
    /// Whether the microphone is opened as a communications stream, letting Windows apply
    /// the user's own ducking policy (Settings &gt; Sound &gt; "When Windows detects
    /// communications activity") while dictating. Applied at startup.
    /// </summary>
    public bool DuckOtherAudio { get; init; } = true;
```

- [ ] Run again — expected: both tests pass:

```
cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~SettingsDucking
```

- [ ] Run the full suite to confirm nothing else moved:

```
cd windows && dotnet test VoxScribe.CrossPlatform.slnf
```

- [ ] Commit:

```
git add windows/src/VoxScribe.Core/AppSettings.cs windows/tests/VoxScribe.Core.Tests/SettingsDuckingTests.cs
git commit -m "core: DuckOtherAudio setting, default on"
```

---

### Task 2: Communications category on the WASAPI stream (manual test required)

**Files:**
- Modify: `windows/src/VoxScribe.Platform.Windows/WasapiAudioCapture.cs`

**Interfaces:**
- Consumes: `NAudio.CoreAudioApi.AudioClient.SetClientProperties(bool useHardwareOffload, AudioStreamCategory category, AudioClientStreamOptions options)`, `AudioStreamCategory.Communications`, `AudioClientStreamOptions.None` (NAudio 2.3.0)
- Produces: `public WasapiAudioCapture(string? deviceId = null, bool communicationsCategory = true)`

This is the logic-free plumbing the repo rule allows: one constant category call, no retries, no policy. NAudio's `WasapiCapture` never calls `SetClientProperties` itself, but it reads `captureDevice.AudioClient` — and NAudio caches the `AudioClient` per `MMDevice` instance — so setting the property on `device.AudioClient` before each `WasapiCapture` attempt applies it to the client the capture will use. `SetClientProperties` must run before the client is initialized, which is exactly where the call sits (before `capture.WaveFormat`/`StartRecording`). The `Platform.Windows` project is not in the cross-platform slnf, so CI cannot exercise it — verification here is a clean build of the full solution plus the Task 5 manual checklist. **Manual test required.**

**Steps:**

- [ ] Confirm the full Windows solution builds clean before touching it (baseline):

```
cd windows && dotnet build VoxScribe.sln
```

Expected: `Build succeeded`.

- [ ] Modify `windows/src/VoxScribe.Platform.Windows/WasapiAudioCapture.cs`. Replace the ctor block (lines 41–43):

```csharp
    /// <summary>Captures from a specific device, or the default when null.</summary>
    /// <param name="deviceId">An <c>MMDevice.ID</c>, or null for the system default.</param>
    /// <param name="communicationsCategory">
    /// Tag the stream <c>AudioCategory_Communications</c> so Windows applies the user's own
    /// ducking policy (Settings &gt; Sound &gt; "When Windows detects communications
    /// activity") while capturing.
    /// </param>
    public WasapiAudioCapture(string? deviceId = null, bool communicationsCategory = true)
    {
        _deviceId = deviceId;
        _communicationsCategory = communicationsCategory;
    }

    private readonly bool _communicationsCategory;
```

- [ ] In the same file, inside `StartCapture()`, add the category call at the top of the format loop — replace:

```csharp
        foreach (var attempt in Formats(device))
        {
            var capture = new WasapiCapture(device, useEventSync: true, BufferMilliseconds)
```

with:

```csharp
        foreach (var attempt in Formats(device))
        {
            // NAudio caches AudioClient per MMDevice, and WasapiCapture reads
            // device.AudioClient — so setting the category here, before the capture
            // initializes the client, tags the stream it will open. Re-done per attempt
            // because a failed attempt's Dispose may tear the cached client down.
            if (_communicationsCategory)
            {
                try
                {
                    device.AudioClient.SetClientProperties(
                        useHardwareOffload: false,
                        AudioStreamCategory.Communications,
                        AudioClientStreamOptions.None);
                }
                catch (COMException)
                {
                    // Category is best-effort: capture without ducking beats no capture.
                }
            }

            var capture = new WasapiCapture(device, useEventSync: true, BufferMilliseconds)
```

- [ ] Build the full solution again — expected: `Build succeeded`, no new warnings:

```
cd windows && dotnet build VoxScribe.sln
```

- [ ] Run the cross-platform suite (unaffected, but pins the baseline):

```
cd windows && dotnet test VoxScribe.CrossPlatform.slnf
```

- [ ] Commit:

```
git add windows/src/VoxScribe.Platform.Windows/WasapiAudioCapture.cs
git commit -m "windows: tag WASAPI capture stream as Communications for OS ducking"
```

---

### Task 3: Plumb the toggle through PlatformFactory and Composition

**Files:**
- Modify: `windows/src/VoxScribe.App/PlatformFactory.cs`
- Modify: `windows/src/VoxScribe.App/Composition.cs`

**Interfaces:**
- Consumes: `SettingsData.DuckOtherAudio` (Task 1), `WasapiAudioCapture(string?, bool)` (Task 2), `private static T? Create<T>(string typeName, object?[] arguments)` (PlatformFactory.cs:228)
- Produces: `public static IAudioCapture? CreateAudioCapture(string? deviceId = null, bool duckOtherAudio = true)`

`Activator.CreateInstance` does not fill optional parameters, so the factory always passes both arguments; `SelfTest.CheckPlatformLayer`'s existing `PlatformFactory.CreateAudioCapture()` call keeps compiling via the defaults. The factory is reflection-based, so the only cross-platform-testable behaviour is "still returns null off Windows without throwing" — covered by the existing App.Tests run plus the SelfTest in CI against the published exe.

**Steps:**

- [ ] Modify `windows/src/VoxScribe.App/PlatformFactory.cs` — replace lines 69–72:

```csharp
    /// <summary>Creates the WASAPI capture, or null off Windows.</summary>
    /// <param name="deviceId">An <c>MMDevice.ID</c>, or null for the system default.</param>
    /// <param name="duckOtherAudio">Whether the stream is tagged as communications so
    /// Windows ducks other audio while dictating.</param>
    public static IAudioCapture? CreateAudioCapture(string? deviceId = null, bool duckOtherAudio = true) =>
        Create<IAudioCapture>("WasapiAudioCapture", [deviceId, duckOtherAudio]);
```

- [ ] Modify `windows/src/VoxScribe.App/Composition.cs` line 95 — replace:

```csharp
        var capture = PlatformFactory.CreateAudioCapture(settings.Data.AudioDeviceId);
```

with:

```csharp
        var capture = PlatformFactory.CreateAudioCapture(
            settings.Data.AudioDeviceId, settings.Data.DuckOtherAudio);
```

- [ ] Build the full solution and run the suite — expected: `Build succeeded`, all tests pass:

```
cd windows && dotnet build VoxScribe.sln && dotnet test VoxScribe.CrossPlatform.slnf
```

- [ ] Commit:

```
git add windows/src/VoxScribe.App/PlatformFactory.cs windows/src/VoxScribe.App/Composition.cs
git commit -m "windows: plumb DuckOtherAudio through the capture factory"
```

---

### Task 4: Settings toggle in the Microphone section

**Files:**
- Create: `windows/tests/VoxScribe.App.Tests/DuckingToggleTests.cs`
- Modify: `windows/src/VoxScribe.App/Views/SettingsWindow.cs`

**Interfaces:**
- Consumes: `SettingsWindow(AppSettings settings)`, private helpers `Toggle(string label, bool value, Action<bool> onChange)` (SettingsWindow.cs:602) and `Note(string text)` (SettingsWindow.cs:593), `Save(SettingsData data)` (SettingsWindow.cs:563)
- Produces: a `CheckBox` labelled `"Duck other audio while dictating"` inside `BuildMicrophoneSection()`, saving `DuckOtherAudio`

**Steps:**

- [ ] Write the failing headless UI test — create `windows/tests/VoxScribe.App.Tests/DuckingToggleTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

/// <summary>
/// The ducking toggle lives in settings, reflects the stored value, and writes it back —
/// the only part of Wave 4 CI can see; the OS-side ducking itself is a manual test.
/// </summary>
public sealed class DuckingToggleTests
{
    private const string Label = "Duck other audio while dictating";

    private static CheckBox FindToggle(SettingsWindow window) =>
        window.GetVisualDescendants()
            .OfType<CheckBox>()
            .Single(c => (c.Content as TextBlock)?.Text == Label);

    [AvaloniaFact]
    public void Ducking_toggle_reflects_and_saves_the_setting()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var settings = new AppSettings(Path.Combine(dir.FullName, "settings.json"));
            var window = new SettingsWindow(settings);
            window.Show();

            var toggle = FindToggle(window);
            toggle.IsChecked.ShouldBe(true, "the setting defaults to on");

            toggle.IsChecked = false;
            settings.Data.DuckOtherAudio.ShouldBeFalse("unchecking must persist DuckOtherAudio = false");

            window.Close();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
```

- [ ] Run it — expected failure: `Single` throws `InvalidOperationException: Sequence contains no matching element` (the checkbox does not exist yet):

```
cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~DuckingToggle
```

- [ ] Minimal implementation — in `windows/src/VoxScribe.App/Views/SettingsWindow.cs`, inside `BuildMicrophoneSection()`, replace the returned panel (lines 328–337):

```csharp
        return new StackPanel
        {
            Spacing = Tokens.Space.Snug,
            Children =
            {
                picker,
                Note("Which microphone to record from. Takes effect the next time Vox-Scribe starts."),
                Toggle("Duck other audio while dictating",
                    _settings.Data.DuckOtherAudio,
                    v => Save(_settings.Data with { DuckOtherAudio = v })),
                Note("Opens the microphone as a communications stream, so Windows lowers "
                   + "other apps' volume by your own preference (Settings > Sound > \"When "
                   + "Windows detects communications activity\"). Takes effect the next "
                   + "time Vox-Scribe starts."),
            },
        };
```

- [ ] Run again — expected pass:

```
cd windows && dotnet test VoxScribe.CrossPlatform.slnf --filter FullyQualifiedName~DuckingToggle
```

- [ ] Run the full suite (UiTests pin the red-means-recording rule; the toggle adds no colours, so nothing should move):

```
cd windows && dotnet test VoxScribe.CrossPlatform.slnf
```

- [ ] Commit:

```
git add windows/src/VoxScribe.App/Views/SettingsWindow.cs windows/tests/VoxScribe.App.Tests/DuckingToggleTests.cs
git commit -m "windows: Duck other audio toggle in the Microphone section"
```

---

### Task 5: Manual test checklist (manual test required)

**Files:**
- Modify: none (verification only; run against a locally published build)

**Interfaces:**
- Consumes: the installed app, Windows Settings > Sound > "When Windows detects communications activity" (a.k.a. the Communications tab of the classic Sound control panel)

The ducking behaviour is applied by Windows, not by VoxScribe — CI cannot see it. Every box below must be ticked by a human on real hardware before this wave is called done.

**Preparation:**

- [ ] Publish and run the real single-file build (the platform DLL loads by reflection — do not test from `dotnet run` of a partial layout):

```
cd windows && dotnet publish src/VoxScribe.App/VoxScribe.App.csproj -c Release -r win-x64
```

- [ ] In Windows: Settings > Sound > (More/Advanced sound settings) > Communications tab — set "Reduce the volume of other sounds by 80%". Note the current setting to restore it afterwards.

**Checklist:**

- [ ] **Ducking engages.** Play music in any app at a clearly audible level. Hold push-to-talk. The music volume drops within about a second of the pill appearing.
- [ ] **Ducking restores.** Release push-to-talk. Within a few seconds of the pill disappearing, the music returns to its original volume — no residual attenuation.
- [ ] **Repeatability.** Dictate three short utterances back to back. Ducking engages and releases each time; no stuck-attenuated state.
- [ ] **Toggle off restores old behaviour.** In VoxScribe settings, uncheck "Duck other audio while dictating", quit VoxScribe fully (tray > exit), relaunch. Play music, hold push-to-talk: the music volume does **not** change, and dictation still transcribes and injects normally.
- [ ] **Toggle back on.** Re-check the toggle, restart VoxScribe, confirm ducking works again (same as the first two checks).
- [ ] **Windows policy respected.** Set the Communications option to "Do nothing", restart nothing, dictate: no ducking even with the VoxScribe toggle on (the OS policy, not VoxScribe, decides the amount). Restore it to "Reduce by 80%".
- [ ] **Default device change mid-capture — no regression.** With the microphone setting on "System default", start dictating, then change the default communications capture device in Windows Sound settings while still holding the key. Acceptable outcomes: capture continues on the old device for that utterance, or the utterance ends cleanly. Not acceptable: crash, or a wedged state where the next dictation fails. Verify the *next* utterance captures fine (on whichever device is now default).
- [ ] **Explicit device still works.** Pick a specific microphone (not "System default") in VoxScribe settings, restart, dictate: transcription works and ducking still engages.
- [ ] Restore the user's original Communications ducking setting in Windows.

- [ ] When all boxes are ticked, commit the plan checkboxes:

```
git add docs/superpowers/plans/2026-08-30-wave4-communications-ducking.md
git commit -m "docs: wave 4 manual ducking checklist verified"
```
