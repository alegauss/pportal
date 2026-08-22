using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP291: the reference frame ring, and the substitution that keeps a stream decodable through loss.
///
/// Held by a table and by a reader over the C, for the same reason the ordering decision is: none of
/// this is observable through the shim. What it protects is a picture rather than a byte - a wrong
/// substitution decodes into a smear that no assertion further down would call a failure.
/// </summary>
public class ReferenceFrameTests
{
    /// <summary>
    /// The fill order, which is the surprising half of add_ref_frame.
    ///
    /// Before slot 0 is taken, a new frame goes into the highest EMPTY slot - so the first lands at
    /// 15 and they walk forwards. A port that wrote them at 0 and shifted from the start would look
    /// identical for the first sixteen frames and then hold a different set.
    /// </summary>
    [Fact]
    public void TheRingFillsBackwardsUntilSlotZeroIsTaken()
    {
        var refs = new ReferenceFrames();
        Assert.All(refs.Slots, slot => Assert.Equal(ReferenceFrames.Empty, slot));

        refs.Add(100);
        Assert.Equal(100, refs.Slots[15]);
        Assert.Equal(ReferenceFrames.Empty, refs.Slots[0]);

        refs.Add(101);
        Assert.Equal(101, refs.Slots[14]);
        Assert.Equal(100, refs.Slots[15]);

        // Fill the rest, so slot 0 is taken by the sixteenth.
        for (int i = 2; i < ReferenceFrames.Capacity; i++)
            refs.Add(100 + i);

        Assert.Equal(115, refs.Slots[0]);
        Assert.Equal(100, refs.Slots[15]);

        // From here it shifts, most-recent-first, and the oldest falls off the end.
        refs.Add(116);
        Assert.Equal(116, refs.Slots[0]);
        Assert.Equal(115, refs.Slots[1]);
        Assert.Equal(101, refs.Slots[15]);
        Assert.False(refs.Holds(100));
    }

    /// <summary>A reference that is held needs no substitute.</summary>
    [Fact]
    public void APresentReferenceIsLeftAlone()
    {
        var refs = new ReferenceFrames();
        refs.Add(40);
        refs.Add(41);

        // Frame 42 referencing 0 back is frame 41.
        ReferenceChoice choice = refs.Choose(frameIndexCur: 42, referenceFrame: 0);
        Assert.True(choice.Present);
        Assert.False(choice.Lost);
    }

    /// <summary>
    /// A missing one is substituted with the nearest OLDER frame that is held - never a newer one,
    /// which has not been decoded.
    /// </summary>
    [Fact]
    public void AMissingReferenceTakesTheNearestOlderOneHeld()
    {
        var refs = new ReferenceFrames();
        refs.Add(38);
        refs.Add(41);

        // Frame 42 asks for 0 back, which is 41 - held. Ask for one back: 40, which is not.
        ReferenceChoice choice = refs.Choose(42, referenceFrame: 1);
        Assert.False(choice.Present);
        Assert.False(choice.Lost);

        // 42 - 3 - 1 = 38, so index 3 is the substitute; 2 would be 39 and is not held.
        Assert.Equal(3, choice.Substitute);
    }

    /// <summary>And where nothing older is held, the frame is lost.</summary>
    [Fact]
    public void NoSubstituteMeansTheFrameIsLost()
    {
        var refs = new ReferenceFrames();
        refs.Add(41);

        ReferenceChoice choice = refs.Choose(42, referenceFrame: 1);
        Assert.False(choice.Present);
        Assert.True(choice.Lost);
        Assert.Equal(-1, choice.Substitute);
    }

    /// <summary>0xff is the slice naming no reference, and the search is skipped for it.</summary>
    [Fact]
    public void NoReferenceIsNotSearchedFor()
    {
        var refs = new ReferenceFrames();
        ReferenceChoice choice = refs.Choose(42, ReferenceFrames.NoReference);

        Assert.True(choice.Present);
        Assert.False(choice.Lost);
    }

    /// <summary>
    /// The subtraction wraps into 16 bits, which is what makes early frames work at all.
    ///
    /// Frame 2 referencing five back is 65532 and not -4, and 65532 is the form a held index would
    /// have taken. A port doing this in int would search for a negative number, find nothing, and
    /// declare the frame lost - on a stream that had simply just started.
    /// </summary>
    [Fact]
    public void TheReferenceIndexWrapsRatherThanGoingNegative()
    {
        var refs = new ReferenceFrames();
        refs.Add(65532);

        ReferenceChoice choice = refs.Choose(frameIndexCur: 2, referenceFrame: 5);
        Assert.True(choice.Present);
        Assert.False(choice.Lost);
    }

    /// <summary>THE DRIFT CHECK. The C still keeps the ring and searches it this way.</summary>
    [Fact]
    public void TheCStillKeepsTheRingThisWay()
    {
        string? file = SanitizerSource.LocateRelative(VideoReceiverSource.RelativePath);
        Assert.True(file is not null, "no videoreceiver.c - this whole file is describing nothing");

        string core = File.ReadAllText(file);

        Assert.True(VideoReceiverSource.TheRingStillShiftsFromSlotZero(core),
            "add_ref_frame no longer shifts the array down when slot 0 is taken");
        Assert.True(VideoReceiverSource.TheRingStillBackfillsFromTheEnd(core),
            "add_ref_frame no longer fills from index 15 downward before slot 0 is taken");
        Assert.True(VideoReceiverSource.TheSubstituteIsStillSearchedForwards(core),
            "the substitution no longer walks from reference_frame + 1 up to 16");
        Assert.True(VideoReceiverSource.NoReferenceIsStillSkipped(core),
            "0xff is no longer excepted, so a slice naming no reference now triggers a search");
    }
}
