using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP291: the FEC-failure decision, and PP292's wrap asserted as the defect it is.
/// </summary>
public class VideoFlushDecisionTests
{
    /// <summary>A plain FAILED charges nothing and sends nothing.</summary>
    [Fact]
    public void OnlyAFecFailureIsCharged()
    {
        FlushFailure failure = VideoFlushDecision.Failed(
            FrameFlushResult.Failed, frameIndexCur: 9, frameIndexPrevComplete: 5,
            idrOnFecFailure: true, waitingForIdr: false);

        Assert.False(failure.ReportCorrupt);
        Assert.False(failure.RequestIdr);
        Assert.Equal(0, failure.FramesLost);
    }

    /// <summary>A FEC failure reports the range from the last complete frame to the failed one.</summary>
    [Fact]
    public void AFecFailureReportsTheRangeAndCharges()
    {
        FlushFailure failure = VideoFlushDecision.Failed(
            FrameFlushResult.FecFailed, frameIndexCur: 9, frameIndexPrevComplete: 5,
            idrOnFecFailure: false, waitingForIdr: false);

        Assert.True(failure.ReportCorrupt);
        Assert.Equal(6, failure.CorruptFrom);
        Assert.Equal(9, failure.CorruptTo);
        Assert.Equal(4, failure.FramesLost);

        // With the setting off, no keyframe is asked for however bad it gets.
        Assert.False(failure.RequestIdr);
    }

    /// <summary>
    /// The IDR request goes out once, and the event says "sent" on the failures after it.
    ///
    /// Two different questions sharing a name in the C: idr_request_sent is seeded from
    /// waiting_for_idr, so on a second failure it is true without anything being sent.
    /// </summary>
    [Theory]
    [InlineData(false, true, false)]   // not waiting: request it, event says not-already-sent
    [InlineData(true, false, true)]    // already waiting: request nothing, event says sent
    public void TheIdrRequestGoesOutOnce(bool waiting, bool expectRequest, bool expectSentFlag)
    {
        FlushFailure failure = VideoFlushDecision.Failed(
            FrameFlushResult.FecFailed, 9, 5, idrOnFecFailure: true, waitingForIdr: waiting);

        Assert.Equal(expectRequest, failure.RequestIdr);
        Assert.Equal(expectSentFlag, failure.IdrRequestSent);
    }

    /// <summary>While waiting for a keyframe, P-frames are dropped and an I-frame ends the wait.</summary>
    [Theory]
    [InlineData(false, false, false, false)]  // not waiting: nothing skipped, nothing cleared
    [InlineData(false, true, false, false)]
    [InlineData(true, false, true, false)]    // waiting, P-frame: skipped
    [InlineData(true, true, false, true)]     // waiting, I-frame: delivered and clears the wait
    public void TheIdrWaitSkipsPFramesAndEndsOnAnIFrame(
        bool waiting, bool isIntra, bool expectSkip, bool expectClear)
    {
        bool skip = VideoFlushDecision.SkipWhileWaitingForIdr(waiting, isIntra, out bool clears);

        Assert.Equal(expectSkip, skip);
        Assert.Equal(expectClear, clears);
    }

    /// <summary>
    /// PP292, now fixed on both sides rather than reproduced on one.
    ///
    /// The ordinary case and the turnover case were never in doubt - what the defect cost was the
    /// third row, and it is the reason the count is charged through a cast on both sides now.
    /// </summary>
    [Theory]
    [InlineData(8, 5, 3)]              // ordinary
    [InlineData(3, 65535, 4)]          // exactly at the turnover, which worked even before
    [InlineData(2, 65530, 8)]          // across it, where the flat subtraction answered -65528
    public void TheFramesLostCountIsReducedAgainstTheWrap(int cur, int prevComplete, int expected)
        => Assert.Equal(expected, VideoFlushDecision.FramesLost(cur, prevComplete));

    /// <summary>
    /// The regression, named rather than left to a row in a table.
    ///
    /// -65528 is what both implementations charged before PP292, to frames_lost and to
    /// frames_lost_total, roughly every eighteen minutes of 60fps streaming whenever FEC failed in
    /// the window. A count that may go negative at all is the shape of the defect, so that is what
    /// this asserts against rather than the one value.
    /// </summary>
    [Fact]
    public void ALossAcrossTheTurnoverIsNotNegative()
    {
        int lost = VideoFlushDecision.FramesLost(2, 65530);

        Assert.True(lost > 0, $"a loss across the sequence wrap counted {lost}");
        Assert.NotEqual(-65528, lost);
    }

    /// <summary>
    /// THE DRIFT CHECK, and PP292 turns half of it around.
    ///
    /// The C still charges only a FEC failure and still seeds idr_request_sent from the wait, which
    /// this port reproduces. The frames-lost arithmetic it no longer reproduces: that line was
    /// fixed in lib/src/videoreceiver.c in the same commit as the managed side, so what has to hold
    /// is that the cast is still there - a merge from upstream is what would take it back out.
    /// </summary>
    [Fact]
    public void TheCStillDoesThis()
    {
        string? file = SanitizerSource.LocateRelative(VideoReceiverSource.RelativePath);
        Assert.True(file is not null, "no videoreceiver.c - this whole file is describing nothing");

        string core = File.ReadAllText(file);

        Assert.True(VideoReceiverSource.TheLostCountIsStillReducedAgainstTheWrap(core),
            "the frames-lost cast is gone from videoreceiver.c, so the C counts -65528 across the "
                + "wrap again and only the managed side is right - PP292 was fixed in both");
        Assert.True(VideoReceiverSource.TheIdrSentFlagIsStillSeededFromTheWait(core),
            "idr_request_sent is no longer seeded from waiting_for_idr");
    }
}
