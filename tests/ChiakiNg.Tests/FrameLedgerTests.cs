using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP76: every decoded frame in exactly one column, which is what makes the comparison a decoder's.
///
/// The residue was got wrong three times and each time it read as loss - one to five frames a
/// session, attributed to whichever decoder happened to be running, which is the exact quantity
/// PP76 exists to measure. Both failures are pinned here because neither announced itself: the
/// numbers simply did not add up, and only by a little.
/// </summary>
public class FrameLedgerTests
{
    /// <summary>The ordinary pull: the drain kept one and threw the rest away.</summary>
    [Fact]
    public void ADrainThatKeptOneIsChargedForTheRest()
    {
        Assert.Equal(3, FrameLedger.Swallowed(consumed: 14, consumedBefore: 10, returnedOne: true));
        Assert.Equal(0, FrameLedger.Swallowed(consumed: 11, consumedBefore: 10, returnedOne: true));
    }

    /// <summary>
    /// THE FIRST DEFECT: one subtracted from a pull that returned nothing.
    ///
    /// A codec that has not filled its reorder window advances frame_num without handing anything
    /// back, and charging that pull for a frame it never returned leaves a decoded frame in no
    /// column at all. It was five a session, constant - small enough to read as rounding.
    /// </summary>
    [Fact]
    public void ADrainThatReturnedNothingIsChargedForAllOfThem()
    {
        Assert.Equal(4, FrameLedger.Swallowed(consumed: 14, consumedBefore: 10, returnedOne: false));
        Assert.Equal(1, FrameLedger.Swallowed(consumed: 11, consumedBefore: 10, returnedOne: false));
    }

    /// <summary>A pull the codec did not advance across swallowed nothing, either way.</summary>
    [Fact]
    public void ADrainThatConsumedNothingSwallowedNothing()
    {
        Assert.Equal(0, FrameLedger.Swallowed(consumed: 10, consumedBefore: 10, returnedOne: false));
        Assert.Equal(0, FrameLedger.Swallowed(consumed: 10, consumedBefore: 10, returnedOne: true));
    }

    /// <summary>
    /// The identity: shown plus what the presenter discarded accounts for every decoded frame.
    ///
    /// The network's loss is added back because it is folded into dropped and those frames never
    /// reached a decoder - see <see cref="PresentationCount.Dropped"/>, which carries both on
    /// purpose so PP76's subtraction cannot read below zero.
    /// </summary>
    [Fact]
    public void AWholeSessionLeavesNothingOver()
    {
        // The measured run this shipped on: 505 decoded, 492 shown, 8 lost, 13 swallowed.
        Assert.Equal(0, FrameLedger.Residue(decoded: 505, shown: 492, dropped: 8 + 13, lost: 8));
    }

    /// <summary>And a frame that went missing shows up rather than being absorbed.</summary>
    [Fact]
    public void AFrameInNoColumnIsVisible()
    {
        Assert.Equal(5, FrameLedger.Residue(decoded: 505, shown: 487, dropped: 8 + 13, lost: 8));
        Assert.Equal(-2, FrameLedger.Residue(decoded: 505, shown: 494, dropped: 8 + 13, lost: 8));
    }

    /// <summary>
    /// THE SECOND DEFECT: the difference taken against the producer's counter.
    ///
    /// frames_available is incremented from libchiaki's thread, so sampling it around a drain this
    /// thread runs races the drain both ways - read before, a frame arriving mid-drain is returned
    /// without entering a difference; read after, the same frame is charged as swallowed and then
    /// returned by the next pull, counted twice. It leaked with the length of the session rather
    /// than sitting at a boundary, which is what a decoder losing frames also looks like.
    ///
    /// frame_num advances only inside the drain, which only the reader calls.
    /// </summary>
    [Fact]
    public void ThePullCountsOffTheCodecAndNotTheCallback()
    {
        if (FrameLedger.LocateShim() is not { } shim)
            return;

        Assert.True(
            FrameLedger.PullReadsTheConsumerCounter(File.ReadAllText(shim)),
            $"{FrameLedger.PullFunction} must take its swallowed count off {FrameLedger.ConsumerCounter} "
                + $"and not {FrameLedger.ProducerCounter}, which is the producer's and races the drain");
    }

    /// <summary>
    /// And the check is reading a real function, so a rename cannot make it pass over nothing.
    ///
    /// PP271's rule. <see cref="FrameLedger.PullReadsTheConsumerCounter"/> returns false where the
    /// body is missing, but a reader deserves to be told which of the two it is.
    /// </summary>
    [Fact]
    public void TheCheckFindsThePull()
    {
        if (FrameLedger.LocateShim() is not { } shim)
            return;

        Assert.Contains(FrameLedger.PullFunction, File.ReadAllText(shim), StringComparison.Ordinal);
        Assert.False(FrameLedger.PullReadsTheConsumerCounter("void nothing(void) { }"));
    }
}
