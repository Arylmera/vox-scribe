using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VoxScribe.App;
using VoxScribe.App.Design;
using VoxScribe.App.Views;
using VoxScribe.Core;
using VoxScribe.Dictionary;

// Renders docs/assets/screenshots/*.png for the README. Everything is seeded into a temp
// folder; the user's real settings, transcripts and dictionary are never opened.
var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("..", "..", "..", "docs", "assets", "screenshots"));
Directory.CreateDirectory(output);

AppBuilder.Configure<ShotApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var demo = Directory.CreateTempSubdirectory("voxscribe-shots-");
var composition = Seed(demo.FullName);

foreach (var dark in new[] { false, true })
{
    var mode = dark ? "dark" : "light";
    foreach (var theme in Themes.All)
    {
        Themes.Apply(theme.Id, null, dark);
        Settle();

        var window = new MainWindow(composition) { ExitAllowed = true };
        window.Show();
        window.Transitions = null;
        window.Opacity = 1;

        // Every theme's home for the gallery; the full tour in the default theme only.
        var pages = theme.Id == Themes.DefaultId
            ? new[] { AppPage.Home, AppPage.History, AppPage.Dictionary, AppPage.Settings }
            : new[] { AppPage.Home };
        foreach (var page in pages)
        {
            window.ShowPage(page);
            if (page == AppPage.Settings)
                window.GetVisualDescendants().OfType<SettingsPage>().First().Select(SettingsTab.Appearance);
            Settle();
            Save(window, Path.Combine(output, $"{theme.Id}-{page.ToString().ToLowerInvariant()}-{mode}.png"));
        }
        window.Close();
    }
}

demo.Delete(recursive: true);
Console.WriteLine($"Screenshots written to {output}");

static void Settle()
{
    for (var i = 0; i < 5; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}

static void Save(Visual visual, string path)
{
    const double scale = 2;
    var size = visual.Bounds.Size;
    using var bitmap = new RenderTargetBitmap(
        new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
        new Vector(96 * scale, 96 * scale));
    bitmap.Render(visual);
    bitmap.Save(path);
    Console.WriteLine(Path.GetFileName(path));
}

static Composition Seed(string dir)
{
    File.WriteAllText(Path.Combine(dir, "dictionary.txt"), string.Join('\n',
        "vox scribe -> VoxScribe",
        "para kit -> Parakeet",
        "light LLM -> LiteLLM",
        "a valonia -> Avalonia",
        "get hub -> GitHub",
        "cloud code -> Claude Code",
        "sherpa onyx -> sherpa-onnx",
        "pull request -> PR"));

    var transcripts = new TranscriptStore(Path.Combine(dir, "transcripts.jsonl"));
    var now = DateTimeOffset.Now;
    (string Text, double Audio, int MinutesAgo)[] samples =
    [
        ("Let's ship the release notes on Thursday, after the design review.", 4.8, 3),
        ("Can you check whether the Parakeet model loads on a clean install?", 4.1, 11),
        ("Merci pour le retour, je regarde ça demain matin et je te dis.", 3.6, 26),
        ("Add a test for the chord overlap, then open a PR against main.", 3.9, 48),
        ("The pill should read CLEAN while the cleanup pass is running.", 3.4, 75),
        ("Reminder: renew the domain before the end of the month.", 2.9, 140),
        ("Draft a short reply to the team: the build is green, we can tag 1.5.", 5.2, 220),
        ("On se retrouve jeudi pour la revue du sprint.", 2.6, 400),
        ("Summarise the last three GitHub issues in one paragraph.", 3.1, 900),
        ("Book a table for four at eight, near the station.", 2.7, 1500),
        ("Rename the settings tab to Speech and move the device picker there.", 4.4, 2600),
        ("Thanks, that fixed it — the hotkey works with a real keyboard now.", 3.8, 4000),
    ];
    foreach (var (text, audio, ago) in samples.Reverse())
    {
        transcripts.Add(new TranscriptRecord
        {
            At = now.AddMinutes(-ago),
            AudioSeconds = audio,
            ProcessingSeconds = 0.18 + audio / 40,
            Text = text,
            Corrections = text.Contains("Parakeet", StringComparison.Ordinal)
                ? [new AppliedCorrection("para kit", "Parakeet", 1)]
                : text.Contains("GitHub", StringComparison.Ordinal) ? [new AppliedCorrection("get hub", "GitHub", 1)] : null,
        });
    }

    // Every chord bound and a (dummy, unreachable) cleanup endpoint, so the pages show the
    // app as it is used rather than freshly installed.
    File.WriteAllText(Path.Combine(dir, "settings.json"), """
        {
          "PushToTalkKeys": [163],
          "CleanupPushToTalkKeys": [160, 161],
          "UndoKeys": [163, 8],
          "CommandKeys": [163, 165],
          "CleanupEndpoint": "http://localhost:4000/v1"
        }
        """);

    return new Composition(
        new AppSettings(Path.Combine(dir, "settings.json")),
        new DictionaryFile(Path.Combine(dir, "dictionary.txt")),
        transcripts,
        engine: null,
        platformAvailable: false,
        injector: null,
        readAloud: null,
        claude: null);
}

internal sealed class ShotApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
