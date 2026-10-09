using System.IO;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Controls;
using VoxScribe.App.Design;
using VoxScribe.App.Views;
using VoxScribe.Core;

namespace VoxScribe.AppTests;

public sealed class HomePageTests
{
    private static TranscriptStore Store(int records, bool cleaned = false)
    {
        var store = new TranscriptStore(Path.Combine(Directory.CreateTempSubdirectory().FullName, "t.jsonl"));
        for (var i = 0; i < records; i++)
        {
            store.Add(new TranscriptRecord
            {
                At = DateTimeOffset.Now.AddMinutes(-i),
                Text = "one two three four five",
                AudioSeconds = 2,
                RawText = cleaned ? "one two tree four five" : null,
            });
        }

        return store;
    }

    private static Window Host(HomePage page)
    {
        var window = new Window { Content = page };
        window.Show();
        return window;
    }

    private static IEnumerable<Button> Named(Window w, string name) =>
        w.GetVisualDescendants().OfType<Button>().Where(b => AutomationProperties.GetName(b) == name);

    [AvaloniaFact]
    public void Shows_the_last_five_dictations_with_copy_and_type_again()
    {
        var window = Host(new HomePage(Store(7), null, _ => { }, _ => Task.CompletedTask));

        Named(window, "Copy").Count().ShouldBe(5);
        Named(window, "Type again").Count().ShouldBe(5);
    }

    [AvaloniaFact]
    public void Words_this_week_are_counted_from_history()
    {
        var window = Host(new HomePage(Store(7), null, _ => { }, _ => Task.CompletedTask));

        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Inlines?.OfType<Run>().Any(r => r.Text == "35") == true)
            .ShouldBeTrue("7 dictations × 5 words");
    }

    [AvaloniaFact]
    public void A_cleaned_dictation_is_badged_clean()
    {
        var window = Host(new HomePage(Store(1, cleaned: true), null, _ => { }, _ => Task.CompletedTask));

        window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "CLEAN").ShouldBeTrue();
    }

    [AvaloniaFact]
    public void See_all_opens_history()
    {
        AppPage? went = null;
        var window = Host(new HomePage(Store(1), null, p => went = p, _ => Task.CompletedTask));

        var link = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content is string s && s.StartsWith("See all", StringComparison.Ordinal));
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        went.ShouldBe(AppPage.History);
    }

    [AvaloniaFact]
    public void Empty_history_says_so()
    {
        var window = Host(new HomePage(Store(0), null, _ => { }, _ => Task.CompletedTask));

        window.GetVisualDescendants().OfType<Silkscreen>().Any(s => s.Text == "NO DICTATIONS YET").ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Builds_in_every_theme_light_and_dark()
    {
        try
        {
            foreach (var theme in Themes.All)
                foreach (var dark in new[] { false, true })
                {
                    Themes.Apply(theme.Id, null, dark);
                    Should.NotThrow(() => Host(new HomePage(Store(3, cleaned: true), null, _ => { }, _ => Task.CompletedTask)), theme.Id);
                }
        }
        finally
        {
            Themes.Apply(Themes.DefaultId, null, false);
        }
    }
}
