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
    /// PP292, asserted as the defect rather than fixed.
    ///
    /// This is the C's arithmetic and it goes hugely negative across the sequence wrap. It is
    /// pinned here so the reproduction is deliberate and so that whoever decides PP292 has a test
    /// that turns red when they change it - the wrong direction for a guard, and the right one for
    /// a defect somebody is going to argue about.
    /// </summary>
    [Theory]
    [InlineData(8, 5, 3)]              // ordinary
    [InlineData(3, 65535, 4)]          // exactly at the turnover, which happens to work
    [InlineData(2, 65530, -65528)]     // across it: 8 was meant
    public void TheFramesLostCountReproducesTheWrapDefect(int cur, int prevComplete, int expected)
        => Assert.Equal(expected, VideoFlushDecision.FramesLost(cur, prevComplete));

    /// <summary>And the count PP292 would produce instead, which nothing uses yet.</summary>
    [Theory]
    [InlineData(8, 5, 3)]
    [InlineData(3, 65535, 4)]
    [InlineData(2, 65530, 8)]
    public void TheWrappedCountIsWhatWasMeant(int cur, int prevComplete, int expected)
        => Assert.Equal(expected, VideoFlushDecision.FramesLostWrapped(cur, prevComplete));

    /// <summary>THE DRIFT CHECK. The C still charges only a FEC failure, and still subtracts flat.</summary>
    [Fact]
    public void TheCStillDoesThis()
    {
        string? file = SanitizerSource.LocateRelative(VideoReceiverSource.RelativePath);
        Assert.True(file is not null, "no videoreceiver.c - this whole file is describing nothing");

        string core = File.ReadAllText(file);

        Assert.True(VideoReceiverSource.TheLostCountIsStillAFlatSubtraction(core),
            "the frames-lost arithmetic changed - PP292 may have been fixed, and this file still reproduces it");
        Assert.True(VideoReceiverSource.TheIdrSentFlagIsStillSeededFromTheWait(core),
            "idr_request_sent is no longer seeded from waiting_for_idr");
    }
}
