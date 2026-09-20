using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// Spoken punctuation is position-aware — no space before the mark, one after, clean
/// newlines — which is why it is its own pass and not a dictionary rule.
/// </summary>
public sealed class VoiceCommandProcessorTests
{
    [Theory]
    [InlineData("bonjour virgule le monde", "bonjour, le monde")]
    [InlineData("hello comma world", "hello, world")]
    [InlineData("hello world period", "hello world.")]
    [InlineData("c'est fini point", "c'est fini.")]
    [InlineData("first line à la ligne second line", "first line\nsecond line")]
    [InlineData("une nouvelle ligne ici", "une\nici")]
    [InlineData("one new line two", "one\ntwo")]
    [InlineData("one newline two", "one\ntwo")]
    [InlineData("vraiment point d'interrogation", "vraiment?")]
    [InlineData("vraiment point d’interrogation", "vraiment?")]
    [InlineData("why question mark", "why?")]
    [InlineData("super point d'exclamation oui", "super! oui")]
    [InlineData("wow exclamation mark", "wow!")]
    [InlineData("note deux points ceci", "note: ceci")]
    [InlineData("first semicolon second", "first; second")]
    [InlineData("un point virgule deux", "un; deux")]
    [InlineData("un point-virgule deux", "un; deux")]
    [InlineData("oui Virgule non", "oui, non")]
    [InlineData("fini point à la ligne suite", "fini.\nsuite")]
    public void Commands_become_marks(string spoken, string written) =>
        VoiceCommandProcessor.Apply(spoken).ShouldBe(written);

    [Theory]
    [InlineData("pointless appointment")]
    [InlineData("rien de spécial ici")]
    [InlineData("")]
    public void Text_without_a_command_is_untouched(string text) =>
        VoiceCommandProcessor.Apply(text).ShouldBe(text);
}
