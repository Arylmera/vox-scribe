using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VoxScribe.App.Views;

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

            // The dictation pill manages its own visibility from the engine state; it only
            // needs to exist. Never becomes MainWindow — it must never own focus.
            if (_composition.Engine is not null) _ = new HudWindow(_composition.Engine);

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
            _ = new SettingsWindow(_composition.Settings).ShowDialog(_main);
        }
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
