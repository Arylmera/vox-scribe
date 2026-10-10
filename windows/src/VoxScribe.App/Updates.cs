using System.Diagnostics;
using Avalonia;
using VoxScribe.Core;

namespace VoxScribe.App;

/// <summary>
/// The app's view of <see cref="UpdateChecker"/>: the update on offer, if any, and installing it.
/// </summary>
/// <remarks>
/// Checking only ever offers. Installing takes a click (Settings → GENERAL): it downloads the
/// installer, verifies it, runs it silently and quits; the installer closes what is left,
/// replaces the files and starts Vox-Scribe again (<c>voxscribe.iss</c>, the silent [Run] entry).
/// </remarks>
internal static class Updates
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    // Infinite: the default 100 s would also bound the installer download. Checks carry their own timeout.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>The installed version, from Directory.Version.props.</summary>
    public static Version Installed { get; } = typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>The newer release found by the last check, or null.</summary>
    public static UpdateInfo? Available { get; private set; }

    /// <summary>Raised on the calling thread after every check that changed <see cref="Available"/>.</summary>
    public static event EventHandler? Changed;

    /// <summary>Asks GitHub for the latest release. Throws when GitHub cannot be reached.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var timeout = new CancellationTokenSource(CheckTimeout);
        var found = await UpdateChecker.CheckAsync(Http, Installed, timeout.Token).ConfigureAwait(true);
        if (found != Available)
        {
            Available = found;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return found;
    }

    /// <summary>Downloads and verifies <paramref name="update"/>, starts its installer and quits.</summary>
    public static async Task InstallAsync(UpdateInfo update, IProgress<double> progress)
    {
        var path = Path.Combine(Path.GetTempPath(), $"VoxScribe-Setup-{update.Version.ToString(3)}.exe");
        await UpdateChecker.DownloadAsync(Http, update, path, progress, CancellationToken.None).ConfigureAwait(true);

        // /SILENT shows only a progress bar; /CLOSEAPPLICATIONS closes this copy if it is
        // still exiting when the installer gets there.
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
        {
            UseShellExecute = true,
        });
        (Application.Current as App)?.Quit();
    }
}
