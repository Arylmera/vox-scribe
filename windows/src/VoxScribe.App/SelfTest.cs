using System.Runtime.InteropServices;
using VoxScribe.Core;
using VoxScribe.Dictionary;
using VoxScribe.Speech;

namespace VoxScribe.App;

/// <summary>
/// A headless check that the published binary actually works.
/// </summary>
/// <remarks>
/// Run in CI against the real single-file executable. It cannot show a window on a runner, so
/// it verifies the things that only break <i>after</i> publishing: assemblies resolving out
/// of the bundle, native libraries extracting, and paths resolving against
/// <c>AppContext.BaseDirectory</c> rather than the current directory.
/// </remarks>
public static class SelfTest
{
    /// <summary>Runs the checks.</summary>
    /// <returns>0 if everything passed.</returns>
    public static int Run()
    {
        var failures = 0;

        failures += CheckDictionary();
        failures += CheckStorage();
        failures += CheckModelDiscovery();
        failures += CheckPlatformLayer();

        Console.WriteLine(failures == 0 ? "self-test: PASS" : $"self-test: {failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static int CheckDictionary()
    {
        var failures = 0;

        var corrector = new DictionaryCorrector([DictionaryEntry.Correction("cloud code", "Claude Code")]);
        var (text, applied) = corrector.Apply("I use CloudCode daily.");

        failures += Check("dictionary rewrites glued words",
            text == "I use Claude Code daily." && applied.Count == 1);

        failures += Check("dictionary leaves similar words alone",
            corrector.Apply("Cloudflare is fine.").Text == "Cloudflare is fine.");

        return failures;
    }

    private static int CheckStorage()
    {
        // Exercises the source-generated JSON, which is the part most likely to have been
        // silently broken by trimming or single-file publishing.
        var directory = Path.Combine(Path.GetTempPath(), $"voxscribe-selftest-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(directory);

            var store = new TranscriptStore(Path.Combine(directory, "transcripts.jsonl"));
            store.Add(new TranscriptRecord
            {
                At = DateTimeOffset.UtcNow,
                Text = "Claude Code",
                Corrections = [new AppliedCorrection("cloud code", "Claude Code", 1)],
            });

            var reopened = new TranscriptStore(Path.Combine(directory, "transcripts.jsonl"));
            var ok = reopened.Records.Count == 1
                     && reopened.Records[0].Corrections?[0].To == "Claude Code";

            var settings = new AppSettings(Path.Combine(directory, "settings.json"));
            settings.Update(settings.Data with { PushToTalkKey = 0x7C });
            var settingsOk = new AppSettings(Path.Combine(directory, "settings.json")).Data.PushToTalkKey == 0x7C;

            return Check("history round-trips through source-generated JSON", ok)
                 + Check("settings round-trip", settingsOk);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    private static int CheckModelDiscovery()
    {
        // A fresh machine has no model, and that must be a friendly message rather than a
        // crash on launch.
        var located = ParakeetTranscriber.Locate();
        Console.WriteLine($"  model: {located ?? "(not installed — expected on a clean runner)"}");

        return Check("model search paths are absolute",
            ParakeetTranscriber.DefaultSearchPaths().All(Path.IsPathRooted));
    }

    /// <summary>
    /// Verifies the platform layer resolves from inside the published bundle.
    /// </summary>
    /// <remarks>
    /// This is the riskiest thing in the app's structure. <c>VoxScribe.App</c> targets plain
    /// <c>net10.0</c> and loads <c>VoxScribe.Platform.Windows</c> by reflection, which is what
    /// lets the UI be built and headless-tested on macOS. Reflection into a single-file
    /// bundle is exactly where that arrangement would fail — and it would fail at the moment
    /// the user first pressed the hotkey, not at startup. So it is checked here, on Windows,
    /// against the real published executable.
    /// </remarks>
    private static int CheckPlatformLayer()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("  [skip] platform layer (not Windows)");
            return 0;
        }

        var failures = Check("Windows platform assembly loads from the bundle", PlatformFactory.IsAvailable);
        if (!PlatformFactory.IsAvailable) return failures;

        failures += Check("audio capture constructs", PlatformFactory.CreateAudioCapture() is not null);
        failures += Check("text injector constructs", PlatformFactory.CreateTextInjector() is not null);

        failures += CheckHook();

        var recorder = PlatformFactory.StartKeyCapture((_, _) => { });
        failures += Check("key recorder hook installs", recorder is not null);
        recorder?.Dispose();
        Console.WriteLine($"    key name check: 0xA1 -> {PlatformFactory.KeyDisplayName(0xA1)}");

        // Listed by name so a "my microphone is missing" report is diagnosable from the log.
        var devices = PlatformFactory.ListCaptureDevices();
        failures += Check("capture devices enumerate", devices.Length > 0);
        foreach (var (id, name) in devices) Console.WriteLine($"    mic: {name} ({id})");

        return failures;
    }

    /// <summary>
    /// Installs two real hooks, taps Right Ctrl as a finger would, and expects both to fire.
    /// </summary>
    /// <remarks>
    /// This is the check the unit tests cannot make: they drive the engine through a fake
    /// hotkey. Two hooks rather than one on purpose — the bug that shipped green was a second
    /// hook silently starving the first, and one hook alone can never show it. An untagged
    /// <c>SendInput</c> passes through <c>WH_KEYBOARD_LL</c> exactly like hardware, and needs
    /// no foreground window.
    /// </remarks>
    private static int CheckHook()
    {
        const int VkRightCtrl = 0xA3;
        const int VkF13 = 0x7C;

        using var plain = PlatformFactory.CreateHotkeySource(VkRightCtrl);
        using var second = PlatformFactory.CreateHotkeySource([VkRightCtrl, VkF13]);
        if (plain is null || second is null) return Check("hotkey sources construct", false);

        using var plainPressed = new ManualResetEventSlim();
        using var plainReleased = new ManualResetEventSlim();
        using var secondSawKey = new ManualResetEventSlim();
        plain.Pressed += (_, _) => plainPressed.Set();
        plain.Released += (_, _) => plainReleased.Set();
        // The chord needs F13 too, so it never completes — but its listener must still be
        // called, which a Pressed handler cannot show. Tapping F13 alone does it.
        second.Pressed += (_, _) => secondSawKey.Set();

        var failures = Check("hook installs", plain.Start() && second.Start());
        if (failures > 0) return failures;

        PlatformFactory.TapKeyForSelfTest(VkRightCtrl);
        var wait = TimeSpan.FromSeconds(2);
        failures += Check("hook sees an injected Right Ctrl press", plainPressed.Wait(wait));
        failures += Check("hook sees its release", plainReleased.Wait(wait));

        // Second listener alive: press both chord members via two taps in a row is racy, so
        // instead swap its chord to F13 alone and tap that — a key the plain hook ignores.
        PlatformFactory.UpdateHotkeyChord(second, [VkF13]);
        PlatformFactory.TapKeyForSelfTest(VkF13);
        failures += Check("a second hook receives keys too", secondSawKey.Wait(wait));

        return failures;
    }

    private static int Check(string name, bool passed)
    {
        Console.WriteLine($"  [{(passed ? "ok" : "FAIL")}] {name}");
        return passed ? 0 : 1;
    }
}
