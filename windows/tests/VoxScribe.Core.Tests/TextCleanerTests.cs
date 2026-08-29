using VoxScribe.Core;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// Covers the mechanical guard, which is the only part of the cleanup pass that must hold
/// whatever the model does. The network path is not tested here — its whole contract is
/// "return the original on any failure", and it is one <c>catch</c>.
/// </summary>
public class TextCleanerTests
{
    private const string Original = "bonjour euh je voulais dire que le build passe";

    [Fact]
    public void A_tidied_line_is_taken()
    {
        TextCleaner.Accept(Original, "Bonjour, je voulais dire que le build passe.")
            .ShouldBe("Bonjour, je voulais dire que le build passe.");
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        TextCleaner.Accept(Original, "  Bonjour, je voulais dire que le build passe.  ")
            .ShouldBe("Bonjour, je voulais dire que le build passe.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_back_keeps_the_original(string? candidate) =>
        TextCleaner.Accept(Original, candidate).ShouldBe(Original);

    /// <summary>
    /// The one the length check cannot see: an answer the same size as the question, sharing
    /// almost none of its words.
    /// </summary>
    [Fact]
    public void A_model_that_answers_instead_of_repairing_is_refused() =>
        TextCleaner.Accept(Original, "Le build passe, c'est une bonne nouvelle !").ShouldBe(Original);

    [Fact]
    public void A_translation_is_refused() =>
        TextCleaner.Accept(Original, "Hello, I wanted to say that the build passes.")
            .ShouldBe(Original);

    [Fact]
    public void A_preamble_on_its_own_line_is_refused() =>
        TextCleaner.Accept(Original, "Voici le texte corrigé :\nBonjour, le build passe.")
            .ShouldBe(Original);

    [Fact]
    public void A_leaked_reasoning_block_is_refused() =>
        TextCleaner.Accept(Original, "<think>the user wants punctuation</think> Bonjour.")
            .ShouldBe(Original);

    [Fact]
    public void A_code_fence_is_refused() =>
        TextCleaner.Accept(Original, "```bonjour je voulais dire que le build passe```")
            .ShouldBe(Original);

    [Fact]
    public void A_summary_is_too_short_to_be_a_repair() =>
        TextCleaner.Accept(Original, "Le build passe.").ShouldBe(Original);

    [Fact]
    public void Padding_is_too_long_to_be_a_repair() =>
        TextCleaner.Accept(Original, Original + " " + Original + " " + Original)
            .ShouldBe(Original);

    /// <summary>
    /// A spoken enumeration comes back as a list. The markers are not words, so this is the
    /// same text with a different layout.
    /// </summary>
    [Fact]
    public void A_spoken_enumeration_may_come_back_as_bullets()
    {
        const string spoken = "il faut relancer le build vérifier les tests et publier la version";
        const string laidOut = "- Relancer le build\n- Vérifier les tests\n- Publier la version";

        TextCleaner.Accept(spoken, laidOut).ShouldBe(laidOut);
    }

    [Fact]
    public void Numbered_steps_are_accepted_too()
    {
        const string spoken = "d'abord relancer le build ensuite vérifier les tests";
        const string laidOut = "1. Relancer le build\n2. Vérifier les tests";

        TextCleaner.Accept(spoken, laidOut).ShouldBe(laidOut);
    }

    /// <summary>
    /// Layout does not license invention: a list whose items were never spoken is still the
    /// model answering.
    /// </summary>
    [Fact]
    public void An_invented_list_is_refused()
    {
        const string spoken = "il faut relancer le build vérifier les tests et publier la version";
        const string invented =
            "- Installer les dépendances\n- Configurer le serveur\n- Ouvrir le port";

        TextCleaner.Accept(spoken, invented).ShouldBe(spoken);
    }

    /// <summary>
    /// A request for format ("as a list" / "in bullet points") followed by an enumeration
    /// must format as a list and drop the request. This tests that the prompt clarifies to
    /// treat the entire input as dictated text, not as a command to the model.
    /// </summary>
    [Fact]
    public void A_request_for_formatting_with_enumeration_is_formatted()
    {
        const string spoken = "in bullet points first install dependencies then configure the server finally open the port";
        const string formatted = "- Install dependencies\n- Configure the server\n- Open the port";

        TextCleaner.Accept(spoken, formatted).ShouldBe(formatted);
    }

    [Fact]
    public void The_content_field_is_read_out_of_an_openai_body() =>
        TextCleaner.Content("""
            {"choices":[{"index":0,"message":{"role":"assistant","content":"Bonjour."}}]}
            """).ShouldBe("Bonjour.");

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"error":{"message":"model not found"}}""")]
    [InlineData("""{"choices":[]}""")]
    public void A_body_of_the_wrong_shape_reads_as_nothing(string body) =>
        TextCleaner.Content(body).ShouldBeNull();
}
