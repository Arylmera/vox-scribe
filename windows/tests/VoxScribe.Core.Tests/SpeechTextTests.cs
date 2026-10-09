using VoxScribe.Core;
using Shouldly;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// The read-aloud pre-clean: what the oral model must never see, and what is spoken as is
/// when the model fails.
/// </summary>
public class PreCleanTests
{
    [Fact]
    public void Fenced_code_is_dropped()
    {
        SpeechText.PreClean("Avant.\n```bash\nrm -f x\n```\nAprès.").ShouldBe("Avant.\nAprès.");
    }

    [Fact]
    public void Urls_are_dropped()
    {
        SpeechText.PreClean("Voir https://example.com/a?b=1 pour le détail.").ShouldBe("Voir  pour le détail.");
    }

    [Fact]
    public void Inline_code_is_unwrapped_and_a_path_becomes_its_last_segment()
    {
        SpeechText.PreClean("Lance `dotnet test` dans `windows/src/ReadAloud.cs` ou `C:\\x\\y.md`.")
            .ShouldBe("Lance dotnet test dans ReadAloud.cs ou y.md.");
    }

    [Fact]
    public void A_table_reads_as_its_cells_without_the_separator_row()
    {
        SpeechText.PreClean("| Modèle | Temps |\n|---|:---:|\n| gemma | 1,0 s |")
            .ShouldBe("Modèle, Temps\ngemma, 1,0 s");
    }

    [Fact]
    public void Heading_bullet_numbering_and_bold_markers_are_stripped()
    {
        SpeechText.PreClean("## Titre\n- un **gras**\n* deux __souligné__\n1. trois")
            .ShouldBe("Titre\nun gras\ndeux souligné\ntrois");
    }

    [Fact]
    public void Prose_passes_through_untouched()
    {
        const string prose = "Le hook est en place. Tu veux que je pousse ?";
        SpeechText.PreClean(prose).ShouldBe(prose);
    }
}

/// <summary>
/// The splitter feeds the TTS pipeline. A chunk never ends mid-sentence — that sounds broken
/// aloud — no word is lost, and the streamed cut equals the whole-text cut.
/// </summary>
public class SpeechSplitterTests
{
    private const string French = "J'ai branché le lecteur sur la passerelle. "
        + "La réécriture orale prend entre 5.8 et 10 secondes avant le premier mot, puis elle va plus vite "
        + "que la parole, donc la synthèse ne manque jamais de texte et chaque phrase arrive bien avant que la précédente se termine. "
        + "Le tableau devient une phrase. M. Dupont a validé les tests, etc. puis j'ai poussé. "
        + "Attends... Voilà! Il reste une question : est-ce que la touche de dictée coupe aussi la lecture ?\n"
        + "Ligne sans point\nSinon je pousse ce soir.";

    private static readonly string[] Expected =
    [
        "J'ai branché le lecteur sur la passerelle.",
        "La réécriture orale prend entre 5.8 et 10 secondes avant le premier mot, puis elle va plus vite que la parole, donc la synthèse ne manque jamais de texte et chaque phrase arrive bien avant que la précédente se termine.",
        "Le tableau devient une phrase. M. Dupont a validé les tests, etc. puis j'ai poussé. Attends... Voilà!",
        "Il reste une question : est-ce que la touche de dictée coupe aussi la lecture ? Ligne sans point. Sinon je pousse ce soir.",
    ];

    [Fact]
    public void Cuts_only_at_sentence_ends_and_groups_short_sentences()
    {
        SpeechSplitter.Split(French).ShouldBe(Expected);
    }

    [Fact]
    public void The_first_chunk_is_exactly_the_first_sentence()
    {
        SpeechSplitter.Split("Fait. Les tests passent. Je pousse.")[0].ShouldBe("Fait.");
    }

    [Fact]
    public void A_sentence_longer_than_the_group_stays_whole()
    {
        var chunks = SpeechSplitter.Split(French);
        chunks[1].Length.ShouldBeGreaterThan(SpeechSplitter.Max);
        chunks[1].ShouldEndWith("se termine.");
    }

    [Fact]
    public void Groups_never_exceed_the_budget()
    {
        var text = string.Join(' ', Enumerable.Range(1, 40).Select(n => $"Phrase numéro {n} du test."));
        foreach (var c in SpeechSplitter.Split(text)) c.Length.ShouldBeLessThanOrEqualTo(SpeechSplitter.Max);
    }

    [Fact]
    public void Loses_no_words()
    {
        string.Join(' ', SpeechSplitter.Split(French)).ShouldBe(French.Replace("point\n", "point. ", StringComparison.Ordinal).Replace('\n', ' '));
    }

    [Theory]
    [InlineData("Elle prend 0.2 s par phrase et reste rapide.")]
    [InlineData("M. Dupont a validé.")]
    [InlineData("Les tests, la spec, etc. sont prêts.")]
    [InlineData("Voir le fichier config.json pour le détail.")]
    public void False_terminators_do_not_end_a_sentence(string sentence)
    {
        // First, because the first chunk is exactly one sentence: later ones are grouped,
        // which would hide a wrong cut.
        SpeechSplitter.Split(sentence + " Dernier.").ShouldBe([sentence, "Dernier."]);
    }

    [Fact]
    public void An_ellipsis_ends_after_its_last_dot()
    {
        SpeechSplitter.Split("Attends... Voilà.").ShouldBe(["Attends...", "Voilà."]);
    }

    [Fact]
    public void A_line_break_ends_a_sentence()
    {
        SpeechSplitter.Split("Ligne sans point\nAutre ligne.").ShouldBe(["Ligne sans point.", "Autre ligne."]);
    }

    [Fact]
    public void Speaks_a_short_reply_in_one_chunk()
    {
        SpeechSplitter.Split("Pool App is healthy, Magos.").ShouldBe(["Pool App is healthy, Magos."]);
    }

    [Fact]
    public void A_sentence_leaves_the_stream_only_once_its_end_has_arrived()
    {
        var splitter = new SpeechSplitter();
        splitter.Push("Une phrase courte").ShouldBeEmpty();
        splitter.Push(".").ShouldBeEmpty();
        splitter.Push(" ").ShouldBeEmpty();
        splitter.Push("Et").ShouldBe(["Une phrase courte."]);
        splitter.Flush().ShouldBe(["Et."]);
    }

    [Fact]
    public void A_group_leaves_as_soon_as_the_next_sentence_cannot_fit_it()
    {
        var splitter = new SpeechSplitter();
        splitter.Push("Premier. Deux. ").ShouldBe(["Premier."]);
        splitter.Push(new string('X', SpeechSplitter.Max)).ShouldBe(["Deux."]);
    }

    [Fact]
    public void Streamed_in_random_deltas_it_cuts_exactly_like_the_whole_text()
    {
        var random = new Random(7);
        foreach (var text in new[] { French, French + "   ", "Fait. " + French })
        {
            for (var round = 0; round < 50; round++)
            {
                var splitter = new SpeechSplitter();
                var streamed = new List<string>();
                for (var i = 0; i < text.Length;)
                {
                    var n = Math.Min(random.Next(1, 12), text.Length - i);
                    streamed.AddRange(splitter.Push(text.Substring(i, n)));
                    i += n;
                }

                streamed.AddRange(splitter.Flush());
                streamed.ShouldBe(SpeechSplitter.Split(text));
            }
        }
    }

    [Fact]
    public void Reads_a_content_delta_and_ignores_the_rest_of_the_stream()
    {
        ReadAloud.Delta("""{"choices":[{"index":0,"delta":{"content":"En","role":"assistant"}}]}""").ShouldBe("En");
        ReadAloud.Delta("""{"choices":[{"index":0,"delta":{}}]}""").ShouldBeNull();
        ReadAloud.Delta("""{"choices":[]}""").ShouldBeNull();
        ReadAloud.Delta("""{"choices":[{"delta":{"content":null}}]}""").ShouldBeNull();
        ReadAloud.Delta("not json").ShouldBeNull();
    }
}

public sealed class ReadAloudSettingsTests
{
    [Fact]
    public void A_settings_file_from_before_read_aloud_gets_its_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "KeepHistory": true, "CleanupModel": "local-light", "OralPrompt": "" }""");

            var data = new AppSettings(path).Data;
            data.ReadAloudEnabled.ShouldBeTrue();
            data.OralModel.ShouldBe("oral");
            data.TtsModel.ShouldBe("tts");
            data.TtsVoice.ShouldBe("ff_siwis");
            data.OralPrompt.ShouldBe(SettingsData.DefaultOralPrompt);
            data.OralPrompt.ShouldStartWith("Tu reformules la réponse écrite");

            var settings = new AppSettings(path);
            settings.Update(data with { ReadAloud = false });
            new AppSettings(path).Data.ReadAloudEnabled.ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
