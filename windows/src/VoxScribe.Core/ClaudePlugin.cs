using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VoxScribe.Core;

/// <summary>
/// Installs and uninstalls the voxscribe plugin through the <c>claude</c> CLI.
/// </summary>
/// <remarks>
/// Only the CLI: Vox-Scribe never edits Claude Code's own files, <c>settings.json</c>
/// included. The terminal flag the band needs is shown for the user to add themselves.
/// </remarks>
public static class ClaudePlugin
{
    /// <summary>The plugin as the CLI names it.</summary>
    public const string Id = "voxscribe@vox-scribe";

    /// <summary>The GitHub marketplace holding it.</summary>
    public const string Marketplace = "Arylmera/vox-scribe";

    /// <summary>The line to add under <c>env</c> in <c>~/.claude/settings.json</c> for the band to draw in a terminal.</summary>
    public const string TerminalFlagLine = "\"CLAUDE_CODE_ENABLE_FUNCTION_HOOKS\": \"1\"";

    /// <summary>The exit code <see cref="RunAsync"/> reports when <c>claude</c> cannot be started.</summary>
    public const int NotFound = -1;

    /// <summary>Runs the CLI with <paramref name="args"/>; behind a delegate so tests never start a process.</summary>
    public delegate Task<(int ExitCode, string Output)> Runner(IReadOnlyList<string> args, CancellationToken cancellationToken);

    /// <summary>Whether the plugin is in <c>claude plugin list --json</c>'s output; null when it is unreadable.</summary>
    public static bool? IsInstalled(string listJson)
    {
        try
        {
            using var json = JsonDocument.Parse(listJson);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return null;
            return json.RootElement.EnumerateArray().Any(p =>
                p.TryGetProperty("id", out var id) && id.GetString() == Id);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Installed or not; null when <c>claude</c> cannot be run or answers nonsense.</summary>
    public static async Task<bool?> CheckAsync(Runner run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var (code, output) = await run(["plugin", "list", "--json"], cancellationToken).ConfigureAwait(false);
        return code == 0 ? IsInstalled(output) : null;
    }

    /// <summary>Adds the marketplace (an existing one makes this fail harmlessly), then installs. Null on success.</summary>
    public static async Task<string?> InstallAsync(Runner run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var (added, addOutput) = await run(["plugin", "marketplace", "add", Marketplace], cancellationToken).ConfigureAwait(false);
        if (added == NotFound) return addOutput;

        var (code, output) = await run(["plugin", "install", Id, "--scope", "user"], cancellationToken).ConfigureAwait(false);
        return code == 0 ? null : output;
    }

    /// <summary>Removes the plugin, leaving the marketplace. Null on success.</summary>
    public static async Task<string?> UninstallAsync(Runner run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var (code, output) = await run(["plugin", "uninstall", Id], cancellationToken).ConfigureAwait(false);
        return code == 0 ? null : output;
    }

    /// <summary>
    /// The real CLI: <c>claude</c> (the native exe), else <c>claude.cmd</c> (an npm install). No window,
    /// two minutes at most.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        // claude.cmd is a batch file: Windows routes its arguments through cmd.exe's own parsing,
        // so callers must only pass fixed literals here (no quotes or cmd metacharacters).
        foreach (var exe in new[] { "claude", "claude.cmd" })
        {
            var info = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in args) info.ArgumentList.Add(a);

            Process? process;
            try
            {
                process = Process.Start(info);
            }
            catch (Win32Exception)
            {
                continue;
            }

            if (process is null) continue;

            using (process)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    try
                    {
                        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                    catch (IOException) { }
                    catch (InvalidOperationException) { }

                    cancellationToken.ThrowIfCancellationRequested();
                    return (1, "claude did not answer within two minutes");
                }

                var text = (await stdout.ConfigureAwait(false)).Trim();
                var error = (await stderr.ConfigureAwait(false)).Trim();
                return (process.ExitCode, process.ExitCode == 0 || error.Length == 0 ? text : error);
            }
        }

        return (NotFound, "claude was not found on PATH");
    }
}
