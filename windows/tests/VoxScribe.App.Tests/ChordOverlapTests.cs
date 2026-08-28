using VoxScribe.App;
using Shouldly;
using Xunit;

namespace VoxScribe.AppTests;

/// <summary>
/// Two shortcuts that share keys. Right Shift alone and Left Shift + Right Shift both satisfy
/// the plain chord, so the plain hook has to be told to stand aside for the longer one.
/// </summary>
public class ChordOverlapTests
{
    private const int LeftShift = 0xA0;
    private const int RightShift = 0xA1;
    private const int RightControl = 0xA3;

    [Fact]
    public void A_superset_cleanup_chord_blocks_the_plain_one() =>
        Composition.Blockers([RightShift], [LeftShift, RightShift]).ShouldBe([LeftShift]);

    [Fact]
    public void Unrelated_chords_block_nothing() =>
        Composition.Blockers([RightControl], [LeftShift, RightShift]).ShouldBeEmpty();

    /// <summary>
    /// The plain chord being the longer one needs no blocker: holding only part of it never
    /// completes it.
    /// </summary>
    [Fact]
    public void A_shorter_cleanup_chord_blocks_nothing() =>
        Composition.Blockers([LeftShift, RightShift], [RightShift]).ShouldBeEmpty();

    [Fact]
    public void No_cleanup_chord_blocks_nothing()
    {
        Composition.Blockers([RightShift], null).ShouldBeEmpty();
        Composition.Blockers([RightShift], []).ShouldBeEmpty();
    }

    /// <summary>Identical chords are a misconfiguration, not a superset — nothing to suppress.</summary>
    [Fact]
    public void Identical_chords_block_nothing() =>
        Composition.Blockers([RightShift], [RightShift]).ShouldBeEmpty();
}
