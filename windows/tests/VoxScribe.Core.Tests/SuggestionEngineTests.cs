using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The suggestion engine mines raw-vs-cleaned pairs for recurring single-word substitutions
/// the cleanup model keeps making — candidates for permanent dictionary entries.
/// </summary>
public sealed class SuggestionEngineTests
{
    [Fact]
    public void Recurring_substitution_surfaces_at_the_threshold()
    {
        var pair = ("deploy with kubernets today", "deploy with kubernetes today");
        SuggestionEngine.Analyze([pair, pair]).ShouldBeEmpty();

        var suggestion = SuggestionEngine.Analyze([pair, pair, pair]).ShouldHaveSingleItem();
        suggestion.Hear.ShouldBe("kubernets");
        suggestion.Write.ShouldBe("kubernetes");
        suggestion.Count.ShouldBe(3);
    }

    [Fact]
    public void Casing_only_differences_are_ignored()
    {
        var pair = ("i met claude yesterday", "I met Claude yesterday");
        SuggestionEngine.Analyze([pair, pair, pair]).ShouldBeEmpty();
    }

    [Fact]
    public void Word_count_divergence_skips_the_pair_without_false_suggestions()
    {
        var pair = ("um so the thing works", "the thing works");
        SuggestionEngine.Analyze([pair, pair, pair]).ShouldBeEmpty();
    }

    [Fact]
    public void Trailing_punctuation_does_not_block_matching()
    {
        var pair = ("send it to jhon.", "send it to John.");
        var suggestion = SuggestionEngine.Analyze([pair, pair, pair]).ShouldHaveSingleItem();
        suggestion.Hear.ShouldBe("jhon");
        suggestion.Write.ShouldBe("John");
    }

    [Fact]
    public void Suggestions_are_ordered_by_count_descending()
    {
        var frequent = ("use kubernets now", "use kubernetes now");
        var rare = ("ping grafna please", "ping grafana please");
        var result = SuggestionEngine.Analyze([frequent, frequent, frequent, frequent, rare, rare, rare]);

        result.Select(s => s.Hear).ShouldBe(["kubernets", "grafna"]);
    }

    [Fact]
    public void A_list_laid_out_by_the_cleaner_still_yields_word_fixes()
    {
        // Cleanup may turn a spoken enumeration into bullets; the markers are not words.
        var pair = ("first kubernets second grafana", "- first kubernetes\n- second grafana");
        var suggestion = SuggestionEngine.Analyze([pair, pair, pair]).ShouldHaveSingleItem();
        suggestion.Hear.ShouldBe("kubernets");
    }

    [Fact]
    public void No_pairs_means_no_suggestions() => SuggestionEngine.Analyze([]).ShouldBeEmpty();
}
