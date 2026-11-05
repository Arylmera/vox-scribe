using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Views;
using VoxScribe.Core;
using Xunit;

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

    [Theory]
    [InlineData("+ added", true)]
    [InlineData("- removed", true)]
    [InlineData("@@ -1,2 +1,2 @@", true)]
    [InlineData("  context", false)]
    [InlineData("+++ b/file.cs", false)]
    [InlineData("--- a/file.cs", false)]
    public void Only_the_changed_lines_of_a_diff_are_coloured(string line, bool coloured)
    {
        (TurnPanelWindow.DiffColour(line, dark: true) is not null).ShouldBe(coloured);
        (TurnPanelWindow.DiffColour(line, dark: false) is not null).ShouldBe(coloured);
    }

    [AvaloniaFact]
    public void A_diff_block_is_coloured_and_any_other_is_plain()
    {
        var panel = new TurnPanelWindow();

        panel.Present([new PanelBlock("diff", "- a\n+ b"), new PanelBlock("csharp", "- a")]);

        var bodies = panel.GetVisualDescendants().OfType<TextBlock>().ToList();
        bodies.Count(t => t.Inlines is { Count: 2 }).ShouldBe(1);
        bodies.ShouldContain(t => t.Text == "- a");
        panel.Close();
    }

    [AvaloniaFact]
    public void The_close_button_hides_the_panel_without_a_click_on_it()
    {
        var panel = new TurnPanelWindow();
        var clicks = 0;
        panel.Clicked += (_, _) => clicks++;
        panel.Present([Code]);

        panel.GetVisualDescendants().OfType<Button>().Single()
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        panel.IsVisible.ShouldBeFalse();
        clicks.ShouldBe(0);
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
