using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Shouldly;
using VoxScribe.App.Controls;
using VoxScribe.App.Views;
using VoxScribe.Core;
using VoxScribe.Dictionary;

namespace VoxScribe.AppTests;

/// <summary>
/// The dictionary view surfaces recurring cleanup rewrites as suggestions, so a fix the model
/// keeps making can graduate into a rule that costs no round trip.
/// </summary>
public sealed class SuggestionUiTests
{
    private static (DictionaryFile Dictionary, TranscriptStore Transcripts) Stores()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        return (new DictionaryFile(Path.Combine(dir, "dictionary.txt")),
                new TranscriptStore(Path.Combine(dir, "transcripts.jsonl")));
    }

    private static void Rewrites(TranscriptStore transcripts, int times)
    {
        for (var i = 0; i < times; i++)
            transcripts.Add(new TranscriptRecord { Text = "deploy kubernetes now", RawText = "deploy kubernets now" });
    }

    private static bool ShowsSuggestions(Window window) =>
        window.GetVisualDescendants().OfType<Silkscreen>().Any(label => label.Text == "SUGGESTIONS");

    [AvaloniaFact]
    public void A_recurring_rewrite_shows_a_suggestion_row()
    {
        var (dictionary, transcripts) = Stores();
        Rewrites(transcripts, SuggestionEngine.Threshold);

        var window = new Window { Content = new DictionaryView(dictionary, transcripts) };
        window.Show();

        ShowsSuggestions(window).ShouldBeTrue();
        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text?.Contains("kubernets", StringComparison.Ordinal) == true)
            .ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_rewrite_already_covered_by_a_rule_is_not_suggested()
    {
        var (dictionary, transcripts) = Stores();
        dictionary.Add(DictionaryEntry.Correction("kubernets", "kubernetes"));
        Rewrites(transcripts, SuggestionEngine.Threshold);

        var window = new Window { Content = new DictionaryView(dictionary, transcripts) };
        window.Show();

        ShowsSuggestions(window).ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Adding_a_suggestion_makes_it_a_rule_and_removes_the_row()
    {
        var (dictionary, transcripts) = Stores();
        Rewrites(transcripts, SuggestionEngine.Threshold);

        var window = new Window { Content = new DictionaryView(dictionary, transcripts) };
        window.Show();

        // The search row has an ADD too; the suggestion's is the one on the card naming it.
        var card = window.GetVisualDescendants().OfType<TextBlock>()
            .First(block => block.Text?.Contains("kubernets", StringComparison.Ordinal) == true)
            .GetVisualAncestors().OfType<Border>().First();
        var add = card.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "ADD");
        add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        dictionary.Entries.ShouldHaveSingleItem().Hear.ShouldBe("kubernets");
        ShowsSuggestions(window).ShouldBeFalse();
    }
}
