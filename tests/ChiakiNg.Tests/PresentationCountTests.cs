using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP699: the presenter's counters, which lived in the client that no longer builds.
///
/// PP76 reads frames_dropped less frames_lost, and PP528 repaired the first of those in
/// gui/src/qmlmainwindow.cpp - which PP598 retired and PP632 stopped building. PP700 then found the
/// deeper reason the operand was missing: nothing in this host presented at all.
///
/// THE ARITHMETIC IS THE QT CLIENT'S AND THESE HOLD IT, because it is not what a reader would
/// invent. Two different things add to the dropped total - the receiver's own loss and the
/// presenter's discards - and the subtraction PP76 makes takes only the first back out. A counter
/// that held the discards alone would leave that subtraction reading below zero on every session.
/// </summary>
public class PresentationCountTests
{
    /// <summary>A fresh count is two zeroes, so a session that ran nothing says so.</summary>
    [Fact]
    public void AFreshCountIsEmpty()
    {
        var count = new PresentationCount();

        Assert.Equal(0, count.Presented);
        Assert.Equal(0, count.Dropped);
        Assert.Equal(0, count.DecoderDropsAgainst(0));
    }

    /// <summary>
    /// THE FOLD: the receiver's loss goes into the DROPPED total, which is what the C does.
    ///
    /// <c>if (frames_lost > 0) session_baseline.frames_dropped += frames_lost</c>. A port that kept
    /// the two apart would be keeping a different number under the same name, and PP76's
    /// subtraction would take out a term that was never added.
    /// </summary>
    [Fact]
    public void TheReceiversLossGoesIntoTheDroppedTotal()
    {
        var count = new PresentationCount();

        count.Lost(3);
        count.Lost(5);

        Assert.Equal(8, count.Dropped);
        Assert.Equal(0, count.Presented);
    }

    /// <summary>Zero and negative are no-ops, which is the C's own guard on a quiet interval.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AnEmptyLossAddsNothing(int framesLost)
    {
        var count = new PresentationCount();

        count.Lost(framesLost);

        Assert.Equal(0, count.Dropped);
    }

    /// <summary>A discard is the presenter's own, one per frame it never showed.</summary>
    [Fact]
    public void ADiscardIsOnePerFrameNeverShown()
    {
        var count = new PresentationCount();

        for (int i = 0; i < 7; i++)
            count.Discard();

        Assert.Equal(7, count.Dropped);
    }

    /// <summary>
    /// THE SUBTRACTION PP76 READS: dropped less lost is what the decoder itself evicted.
    ///
    /// Twelve frames lost by the network and four discarded by the presenter gives sixteen dropped,
    /// and the difference against twelve is the four. That four is the only loss in the path a
    /// decoder is responsible for, which is the whole of what PP76 compares.
    /// </summary>
    [Fact]
    public void TheDifferenceIsThePresentersOwnDiscards()
    {
        var count = new PresentationCount();

        count.Lost(12);

        for (int i = 0; i < 4; i++)
            count.Discard();

        Assert.Equal(16, count.Dropped);
        Assert.Equal(4, count.DecoderDropsAgainst(12));
    }

    /// <summary>
    /// AND IT IS CLAMPED, which is the C's decision and not a defensive one.
    ///
    /// The two counters are sampled by different threads at different moments, so a session can end
    /// with the receiver ahead of the presenter. Subtracting unsigned would turn a few frames of
    /// skew into eighteen quintillion, which sessionbaseline.h says in those words.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 12)]
    [InlineData(100, 101)]
    public void AReceiverAheadOfThePresenterReadsZeroAndNotAWrap(int dropped, int lost)
    {
        var count = new PresentationCount();

        for (int i = 0; i < dropped; i++)
            count.Discard();

        Assert.Equal(0, count.DecoderDropsAgainst(lost));
    }

    /// <summary>Presented is its own tally and does not touch the dropped one.</summary>
    [Fact]
    public void PresentedAndDroppedAreSeparate()
    {
        var count = new PresentationCount();

        count.Present();
        count.Present();
        count.Discard();

        Assert.Equal(2, count.Presented);
        Assert.Equal(1, count.Dropped);
    }

    /// <summary>
    /// The counters are touched from two threads, so they are interlocked rather than incremented.
    ///
    /// The pull and the render run on the stream thread and the present runs on the UI thread's
    /// dispatcher, so a plain ++ would lose counts under exactly the load a busy session has - and
    /// a loss counter that undercounts reads as a decoder doing better than it did.
    /// </summary>
    [Fact]
    public void TheCountersSurviveTwoThreads()
    {
        var count = new PresentationCount();
        const int each = 20_000;

        var one = new Thread(() =>
        {
            for (int i = 0; i < each; i++)
            {
                count.Present();
                count.Lost(1);
            }
        });

        var two = new Thread(() =>
        {
            for (int i = 0; i < each; i++)
                count.Discard();
        });

        one.Start();
        two.Start();

        Assert.True(one.Join(TimeSpan.FromSeconds(30)));
        Assert.True(two.Join(TimeSpan.FromSeconds(30)));

        Assert.Equal(each, count.Presented);
        Assert.Equal(each * 2, count.Dropped);
    }

    /// <summary>
    /// PP528's shape, as a session would exercise it: every pull's loss is carried, whatever
    /// happened to the frame.
    ///
    /// The C's defect was two returns between the pull and the present that dropped the count. Here
    /// the fold happens before any branch, so a run of frames that were pulled and never shown
    /// still adds every one of their losses.
    /// </summary>
    [Fact]
    public void ALossIsCarriedEvenWhenTheFrameIsNot()
    {
        var count = new PresentationCount();

        // Ten pulls: each carried a loss, and none of them reached the screen.
        for (int i = 0; i < 10; i++)
        {
            count.Lost(2);
            count.Discard();
        }

        Assert.Equal(0, count.Presented);
        Assert.Equal(30, count.Dropped);
        Assert.Equal(10, count.DecoderDropsAgainst(20));
    }
}
