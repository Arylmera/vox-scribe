using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using VoxScribe.App.Views.Settings;
using VoxScribe.Core;
using Shouldly;

namespace VoxScribe.AppTests;

public sealed class VoiceChatSectionTests
{
    private static AppSettings Fresh() =>
        new(Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json"));

    private static CheckBox Box(Control section, string name) =>
        section.GetLogicalDescendants().OfType<CheckBox>()
            .Single(b => b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == name));

    [AvaloniaFact]
    public void Ticking_a_detected_app_saves_it_and_unticking_removes_it()
    {
        var settings = Fresh();
        var section = VoiceChatSection.Build(settings, settings.Update, () => ["Discord", "steam"]);

        Box(section, "Discord").IsChecked = true;
        settings.Data.MuteAppsWhileDictating.ShouldBe(["Discord"]);

        Box(section, "Discord").IsChecked = false;
        settings.Data.MuteAppsWhileDictating.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void A_saved_app_stays_listed_and_ticked_while_it_is_closed()
    {
        var settings = Fresh();
        settings.Update(settings.Data with { MuteAppsWhileDictating = ["Discord"] });

        var section = VoiceChatSection.Build(settings, settings.Update, () => []);

        Box(section, "Discord").IsChecked.ShouldBe(true);
    }
}
