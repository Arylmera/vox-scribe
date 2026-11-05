using System.Text;
using Shouldly;
using VoxScribe.Core;
using VoxScribe.Dictionary;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>
/// Saves replace the file whole: a save that dies leaves the old content, and the bytes on disk
/// are the ones the plain <see cref="File"/> writers produced before saves went through a temp file.
/// </summary>
/// <remarks>
/// A killed process cannot be simulated here, so these tests fail the save instead: a directory
/// takes the temp path. A throwing encoder proves nothing, since <see cref="File.WriteAllText(string, string?, Encoding)"/>
/// encodes before it opens the file.
/// </remarks>
public sealed class AtomicSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"voxscribe-atomic-{Guid.NewGuid():N}");

    public AtomicSaveTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string At(string name) => Path.Combine(_dir, name);

    private static TranscriptRecord Record(string text) => new() { At = DateTimeOffset.UnixEpoch, Text = text };

    private static void ShouldKeepItsBytes(string path, Action save)
    {
        var before = File.ReadAllBytes(path);
        Directory.CreateDirectory(path + ".tmp");

        Should.Throw<UnauthorizedAccessException>(save);

        File.ReadAllBytes(path).ShouldBe(before);
    }

    [Fact]
    public void A_failed_settings_save_leaves_the_old_file()
    {
        var settings = new AppSettings(At("settings.json"));
        settings.Update(settings.Data with { AnchorFocus = false });

        ShouldKeepItsBytes(At("settings.json"), () => settings.Update(settings.Data with { AnchorFocus = true }));
    }

    [Fact]
    public void A_failed_dictionary_save_leaves_the_old_file()
    {
        var file = new DictionaryFile(At("dictionary.txt"));
        file.Add(DictionaryEntry.Term("Anthropic"));

        ShouldKeepItsBytes(At("dictionary.txt"), () => file.Add(DictionaryEntry.Term("Vercel")));
    }

    [Fact]
    public void A_failed_transcript_rewrite_leaves_the_old_file()
    {
        var store = new TranscriptStore(At("transcripts.jsonl"));
        store.Add(Record("one"));

        ShouldKeepItsBytes(At("transcripts.jsonl"), store.Clear);
    }

    [Fact]
    public void Settings_bytes_are_unchanged_and_no_temp_file_survives()
    {
        var path = At("settings.json");
        var settings = new AppSettings(path);
        settings.Update(settings.Data with { AnchorFocus = false });

        File.WriteAllText(At("reference"), File.ReadAllText(path));

        File.ReadAllBytes(path).ShouldBe(File.ReadAllBytes(At("reference")));
        File.Exists(path + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void Dictionary_bytes_keep_their_byte_order_mark_and_no_temp_file_survives()
    {
        var path = At("dictionary.txt");
        new DictionaryFile(path).Add(DictionaryEntry.Term("Évian"));

        File.WriteAllText(At("reference"), File.ReadAllText(path), Encoding.UTF8);

        File.ReadAllBytes(path).ShouldBe(File.ReadAllBytes(At("reference")));
        File.ReadAllBytes(path).Take(3).ShouldBe(Encoding.UTF8.Preamble.ToArray());
        File.Exists(path + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void A_transcript_rewrite_ends_every_line_and_no_temp_file_survives()
    {
        var path = At("transcripts.jsonl");
        var store = new TranscriptStore(path);
        store.Add(Record("one"));
        store.Add(Record("two"));
        var gone = Record("three");
        store.Add(gone);

        store.Remove(gone.Id);

        File.WriteAllLines(At("reference"), File.ReadLines(path));
        File.ReadAllBytes(path).ShouldBe(File.ReadAllBytes(At("reference")));
        File.ReadLines(path).Count().ShouldBe(2);
        File.Exists(path + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public void Clearing_the_transcripts_leaves_an_empty_file()
    {
        var path = At("transcripts.jsonl");
        var store = new TranscriptStore(path);
        store.Add(Record("one"));

        store.Clear();

        new FileInfo(path).Length.ShouldBe(0);
    }
}
