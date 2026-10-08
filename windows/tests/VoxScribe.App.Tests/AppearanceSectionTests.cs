using System.IO;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using VoxScribe.App.Design;
using VoxScribe.App.Views.Settings;
using VoxScribe.Core;
using Shouldly;

namespace VoxScribe.AppTests;

public sealed class AppearanceSectionTests
{
    [AvaloniaFact]
    public void Theme_key_saves_the_theme_when_no_engine_is_running()
    {
        var settings = new AppSettings(Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json"));
        var other = Themes.All.First(t => t.Id != settings.Data.Theme);
        var section = AppearanceSection.Build(settings, settings.Update, engine: null);

        var key = section.GetLogicalDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == $"Theme: {other.Label}");
        key.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        settings.Data.Theme.ShouldBe(other.Id, "no engine means nothing is recording, so the pick must stick");
    }
}
