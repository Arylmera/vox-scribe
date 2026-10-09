using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using VoxScribe.App.Views;
using VoxScribe.Speech;

namespace VoxScribe.App;

/// <summary>The application.</summary>
public partial class App : Application
{
    private Composition? _composition;
    private MainWindow? _main;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _composition = Composition.Create();
            _main = new MainWindow(_composition);

            // The lifetime shows whatever MainWindow is set to. Started from the login entry
            // (--tray) we leave it unset: the app lives in the tray, the hotkey works, and
            // the window appears the first time it is asked for.
            if (!desktop.Args?.Contains("--tray", StringComparer.OrdinalIgnoreCase) ?? true)
            {
                desktop.MainWindow = _main;
            }

            // Offer the model download only when transcription has nowhere to run: no model
            // found or configured, and no remote gateway (machines using one have no local
            // model on purpose). Show, not ShowDialog — the main window is not open yet, and
            // never opens at all when started from the login entry.
            var data = _composition.Settings.Data;
            if (data.ModelDirectory is null && data.SttEndpoint is not { Length: > 0 }
                && ParakeetTranscriber.Locate() is null)
            {
                new FirstRunWindow().Show();
            }

            // The dictation pill manages its own visibility from the engine state; it only
            // needs to exist. Never becomes MainWindow — it must never own focus.
            if (_composition.Engine is not null) _ = new HudWindow(_composition.Engine);

            // The pill only shows while dictating, so a read-aloud failure also lands on the
            // tray tooltip, where it stays until the next one. Raised off the UI thread.
            if (_composition.ReadAloud is { } readAloud && TrayIcon.GetIcons(this) is [var tray, ..])
            {
                readAloud.Failed += (_, message) =>
                    Dispatcher.UIThread.Post(() => tray.ToolTipText = $"Vox-Scribe — {message}");
            }

            // Closing the window leaves VoxScribe running in the tray — the hotkey still works,
            // which is the whole point of a dictation app. Quit is explicit, from the tray
            // menu or the app menu.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Disposing tears down the keyboard hook and releases the audio device. Leaving
            // a low-level hook installed after exit is the kind of thing that makes a
            // machine feel broken until it is rebooted.
            desktop.ShutdownRequested += (_, _) =>
            {
                _composition?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _composition = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayShow(object? sender, EventArgs e) => ShowMain();

    private void OnTraySettings(object? sender, EventArgs e)
    {
        ShowMain();
        if (_main is not null && _composition is not null)
        {
            _ = new SettingsWindow(_composition.Settings, _composition.Engine).ShowDialog(_main);
        }
    }

    /// <summary>
    /// Quits and relaunches, so a next-start setting (the theme) applies now. The new copy
    /// is started first with <c>--restarted</c>, which lets it wait for the single-instance
    /// mutex this process still holds while tearing down.
    /// </summary>
    public void Restart()
    {
        if (Environment.ProcessPath is { } exe)
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(exe, "--restarted"));
        }

        OnTrayQuit(this, EventArgs.Empty);
    }

    private void OnTrayQuit(object? sender, EventArgs e)
    {
        // Lift the hide-to-tray guard first, or Shutdown's window close gets cancelled
        // and the quit silently does nothing.
        if (_main is not null) _main.ExitAllowed = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    private void ShowMain()
    {
        if (_main is null) return;

        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }
}
