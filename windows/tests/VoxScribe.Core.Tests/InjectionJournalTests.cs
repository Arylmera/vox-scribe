using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The injection journal is the record of exactly what DictationEngine sent to the
/// injector for the current/last dictation. Undo counts its characters; a later
/// wave diffs against it, so Retract must model "the last N chars were taken back".
/// </summary>
public sealed class InjectionJournalTests
{
    [Fact]
    public void Records_appended_text_in_order()
    {
        var journal = new InjectionJournal();
        journal.Record("hello");
        journal.Record(" world");
        journal.InjectedText.ShouldBe("hello world");
    }

    [Fact]
    public void Begin_dictation_clears_the_previous_journal()
    {
        var journal = new InjectionJournal();
        journal.Record("old text");
        journal.BeginDictation();
        journal.InjectedText.ShouldBe(string.Empty);
    }

    [Fact]
    public void Retract_removes_the_last_chars_and_clamps_at_zero()
    {
        var journal = new InjectionJournal();
        journal.Record("hello");
        journal.Retract(2);
        journal.InjectedText.ShouldBe("hel");
        journal.Retract(99);
        journal.InjectedText.ShouldBe(string.Empty);
        journal.Retract(-5);
        journal.InjectedText.ShouldBe(string.Empty);
    }

    [Fact]
    public void Empty_journal_reports_empty_text()
    {
        new InjectionJournal().InjectedText.ShouldBe(string.Empty);
    }
}
