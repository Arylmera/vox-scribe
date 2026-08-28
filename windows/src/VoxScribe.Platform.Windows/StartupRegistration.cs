using System.Diagnostics;
using Microsoft.Win32;

namespace VoxScribe.Platform.Windows;

/// <summary>Run Vox-Scribe when the user logs in, via the per-user Run key.</summary>
/// <remarks>
/// HKCU rather than a Startup-folder shortcut or a scheduled task: no elevation, no COM to
/// build a .lnk, and the user can see and remove it from Task Manager's Startup tab.
/// </remarks>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Vox-Scribe";

    /// <summary>The flag that tells the app to start in the tray instead of showing up.</summary>
    public const string TrayArgument = "--tray";

    /// <summary>Whether the Run entry exists and still points at this executable.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string == Command();
    }

    /// <summary>Adds or removes the Run entry.</summary>
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (key is null) return;

        if (enabled) key.SetValue(ValueName, Command());
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Re-points an existing Run entry at the current executable.
    /// </summary>
    /// <remarks>
    /// The rename moved the executable, which left anyone with autostart enabled holding a
    /// login entry for a path that no longer exists — and <see cref="IsEnabled"/> reports
    /// false, so the settings toggle looks off rather than broken. Only ever rewrites an
    /// entry already under this app's own name: it never turns autostart on.
    /// </remarks>
    public static void Repair()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is not string existing) return;
        if (existing == Command()) return;

        key.SetValue(ValueName, Command());
    }

    // MainModule, not Assembly.Location: under PublishSingleFile the assembly has no file
    // path, and the exe is what the Run key has to launch.
    private static string Command() =>
        $"\"{Process.GetCurrentProcess().MainModule?.FileName}\" {TrayArgument}";
}
