using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

/// <summary>Conversation mode's panel: what it shows, and that it never takes the focus.</summary>
public sealed class TurnPanelWindowTests
{
    private static readonly PanelBlock Code = new("csharp", "var a = 1;");
    private static readonly PanelBlock Shell = new("bash", "ls");

    [AvaloniaFact]
    public void It_shows_each_block_and_never_activates()
    {
        var panel = new TurnPanelWindow();

        panel.Present([Code, Shell]);

        panel.IsVisible.ShouldBeTrue();
        panel.ShowActivated.ShouldBeFalse();
        panel.Focusable.ShouldBeFalse();
        var texts = panel.GetVisualDescendants().OfType<TextBlock>().ToList();
        texts.ShouldContain(t => t.Text == "csharp");
        texts.ShouldContain(t => t.Text == "var a = 1;");
        texts.ShouldContain(t => t.Text == "bash");
        panel.Close();
    }

    [AvaloniaFact]
    public void Nothing_to_show_hides_it_and_the_next_turn_replaces_it()
    {
        var panel = new TurnPanelWindow();
        panel.Present([Code]);

        panel.Present([]);
        panel.IsVisible.ShouldBeFalse();

        panel.Present([Shell]);
        panel.IsVisible.ShouldBeTrue();
        panel.GetVisualDescendants().OfType<TextBlock>().ShouldNotContain(t => t.Text == "csharp");
        panel.Close();
    }
}
