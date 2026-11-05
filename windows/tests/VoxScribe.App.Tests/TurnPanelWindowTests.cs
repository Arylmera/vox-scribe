using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

/// <summary>Conversation mode's panel: what it shows, and that it never takes the focus.</summary>
public sealed class TurnPanelWindowTests
{
    private static readonly PanelBlock Code = new("csharp", "var a = 1;", false);
    private static readonly PanelBlock Diff = new("app.cs", "- old\n+ new", true);

    [AvaloniaFact]
    public void It_shows_each_block_and_never_activates()
    {
        var panel = new TurnPanelWindow();

        panel.Present([Code, Diff]);

        panel.IsVisible.ShouldBeTrue();
        panel.ShowActivated.ShouldBeFalse();
        panel.Focusable.ShouldBeFalse();
        var texts = panel.GetVisualDescendants().OfType<TextBlock>().ToList();
        texts.ShouldContain(t => t.Text == "csharp");
        texts.ShouldContain(t => t.Text == "var a = 1;");
        texts.ShouldContain(t => t.Text == "app.cs");
        panel.Close();
    }

    [AvaloniaFact]
    public void A_diff_colours_only_its_changed_lines()
    {
        var panel = new TurnPanelWindow();

        panel.Present([new PanelBlock("a.cs", "- gone\n+ here\n  same", true)]);

        var runs = panel.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Inlines is { Count: > 0 }).Inlines!.OfType<Run>().ToList();
        runs.Count.ShouldBe(3);
        var colours = runs.Select(r => r.Foreground!.ToString()).ToList();
        colours.Distinct().Count().ShouldBe(3); // removed, added, and the plain ink of the context line
        panel.Close();
    }

    [AvaloniaFact]
    public void Nothing_to_show_hides_it_and_the_next_turn_replaces_it()
    {
        var panel = new TurnPanelWindow();
        panel.Present([Code]);

        panel.Present([]);
        panel.IsVisible.ShouldBeFalse();

        panel.Present([Diff]);
        panel.IsVisible.ShouldBeTrue();
        panel.GetVisualDescendants().OfType<TextBlock>().ShouldNotContain(t => t.Text == "csharp");
        panel.Close();
    }
}
