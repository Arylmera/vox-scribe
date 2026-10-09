using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Shouldly;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.App.Views;

namespace VoxScribe.AppTests;

/// <summary>Theme, variant and light/dark changes repaint without a restart.</summary>
public sealed class LiveThemeTests
{
    [AvaloniaFact]
    public void A_theme_change_rebuilds_the_main_window()
    {
        var window = new MainWindow();
        window.Show();
        var before = window.Content;
        try
        {
            Themes.Apply("mono", null, dark: true);
            Dispatcher.UIThread.RunJobs();

            window.Content.ShouldNotBeSameAs(before);
            window.Background.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Color.FromRgb(0x0B, 0x0B, 0x0B));
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
            Dispatcher.UIThread.RunJobs();
            window.ExitAllowed = true;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Shared_controls_take_the_theme_painted_when_they_are_built()
    {
        try
        {
            Themes.Apply("mono", null, dark: true);
            new Lamp().LampColor.ShouldBe(Tokens.Colors.Silkscreen);
            new TransportKey().EngagedColor.ShouldBe(Tokens.Colors.Ink);
            new BrushedPanel().CornerRadius.ShouldBe(0);
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }
}
