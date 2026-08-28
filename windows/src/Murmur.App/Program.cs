using Avalonia;

namespace Murmur.App;

/// <summary>Entry point.</summary>
public static class Program
{
    /// <summary>Starts the app, or runs a headless self-test.</summary>
    /// <param name="args">Command line. <c>--selftest</c> exits without showing UI.</param>
    /// <returns>0 on success.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => Record(e.Exception);

        PlatformFactory.InstallResolver();

        // The published single-file exe is the only artifact CI can run end to end, and a
        // GitHub runner cannot show a window. This branch exercises startup — assembly
        // loading, native library resolution out of the self-extracted bundle, model
        // discovery — and exits, which is the class of failure that only appears after
        // publishing.
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTest.Run();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Where an unhandled exception is written before the process dies.</summary>
    /// <remarks>
    /// Beside <c>settings.json</c>, so everything the app owns lives in one folder a user
    /// can be asked to open.
    /// </remarks>
    public static string CrashLogPath => Path.Combine(
        Path.GetDirectoryName(Murmur.Core.AppSettings.DefaultPath)!, "crash.log");

    /// <summary>
    /// Appends a crash to <see cref="CrashLogPath"/>. A tray app dies off-screen, and
    /// Windows Error Reporting keeps a method token and an IL offset — enough to know
    /// something broke, not enough to know what. This keeps the stack.
    /// </summary>
    /// <remarks>
    /// Appends rather than overwrites, because the interesting crash is often the one before
    /// the one you noticed. Swallows its own failure: a logger that throws while the process
    /// is already dying replaces a diagnosable crash with a mysterious one.
    /// </remarks>
    private static void Record(object? error)
    {
        try
        {
            var path = CrashLogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"""

                ─── {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ───
                {error}

                """);
        }
        catch (Exception)
        {
            // Nothing left to do, and nowhere left to say it.
        }
    }

    /// <summary>Configures Avalonia. Also used by the headless test host.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
