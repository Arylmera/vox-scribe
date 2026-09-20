using VoxScribe.App;
using Shouldly;
using Xunit;

namespace VoxScribe.AppTests;

/// <summary>
/// Shortcuts that share keys. Right Shift alone and Left Shift + Right Shift both satisfy
/// the shorter chord, so its hook has to be told to stand aside for the longer one.
/// </summary>
public class ChordOverlapTests
{
    private const int LeftShift = 0xA0;
    private const int RightShift = 0xA1;
    private const int RightControl = 0xA3;
    private const int F13 = 0x7C;

    [Fact]
    public void A_superset_chord_blocks_the_shorter_one() =>
        Composition.Blockers([RightShift], [LeftShift, RightShift]).ShouldBe([LeftShift]);

    [Fact]
    public void Unrelated_chords_block_nothing() =>
        Composition.Blockers([RightControl], [LeftShift, RightShift]).ShouldBeEmpty();

    /// <summary>
    /// The longer chord needs no blocker: holding only part of it never completes it.
    /// </summary>
    [Fact]
    public void A_shorter_other_chord_blocks_nothing() =>
        Composition.Blockers([LeftShift, RightShift], [RightShift]).ShouldBeEmpty();

    [Fact]
    public void Unbound_others_block_nothing()
    {
        Composition.Blockers([RightShift], new int[]?[] { null }).ShouldBeEmpty();
        Composition.Blockers([RightShift], []).ShouldBeEmpty();
    }

    /// <summary>Identical chords are a misconfiguration, not a superset — nothing to suppress.</summary>
    [Fact]
    public void Identical_chords_block_nothing() =>
        Composition.Blockers([RightShift], [RightShift]).ShouldBeEmpty();

    /// <summary>Four shortcuts now; every longer chord containing this one contributes.</summary>
    [Fact]
    public void Several_superset_chords_all_contribute_once() =>
        Composition.Blockers([RightShift], [LeftShift, RightShift], [F13, RightShift], [LeftShift, RightShift, F13])
            .ShouldBe([LeftShift, F13], ignoreOrder: true);

    /// <summary>An unbound chord fires never, so nothing needs to block it.</summary>
    [Fact]
    public void An_empty_chord_has_no_blockers() =>
        Composition.Blockers([], [RightShift]).ShouldBeEmpty();
}
