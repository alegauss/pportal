using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP291: the frame ordering decision, and the C it was read out of.
///
/// This is the half of chiaki_video_receiver_av_packet with no buffers in it, so there is nothing
/// to compare byte for byte - the decision is not observable through the shim, only its
/// consequences several layers down. What can be done instead is both halves of what this port
/// usually does separately: a table of cases asserting the behaviour, and a drift check asserting
/// the C still spells the conditions the table was written from.
///
/// The drift check is not the weaker one here. Every case below was derived by reading four
/// comparisons in videoreceiver.c, and if one of them changes the table goes on passing while
/// describing a receiver that no longer exists.
/// </summary>
public class VideoFrameSequencerTests
{
    /// <summary>The first packet of a session: nothing seen, so nothing is stale and a frame opens.</summary>
    [Fact]
    public void TheFirstPacketOpensAFrame()
    {
        FrameArrival arrival = VideoFrameSequencer.Arrive(
            frameIndex: 1, VideoFrameSequencer.NoFrame, VideoFrameSequencer.NoFrame, VideoFrameSequencer.NoFrame);

        Assert.False(arrival.Stale);
        Assert.True(arrival.StartsFrame);
        Assert.False(arrival.FlushPrevious);

        // The exception. next_expected is 0, frame 1 is beyond it, and it is NOT reported - there
        // was never a frame 0 to ask the console for.
        Assert.False(arrival.ReportCorrupt);
    }

    /// <summary>
    /// ...and the exception really is only frame 1 with nothing seen. Frame 2 first is a gap.
    /// </summary>
    [Fact]
    public void TheFrameOneExceptionIsNarrow()
    {
        FrameArrival two = VideoFrameSequencer.Arrive(
            2, VideoFrameSequencer.NoFrame, VideoFrameSequencer.NoFrame, VideoFrameSequencer.NoFrame);

        Assert.True(two.ReportCorrupt);
        Assert.Equal(0, two.CorruptFrom);
        Assert.Equal(1, two.CorruptTo);

        // And frame 1 arriving once something HAS been seen is an ordinary case again.
        FrameArrival later = VideoFrameSequencer.Arrive(1, frameIndexCur: 0, frameIndexPrev: 0, frameIndexPrevComplete: 0);
        Assert.False(later.ReportCorrupt);
    }

    /// <summary>A packet for the frame already in progress is neither stale nor a new frame.</summary>
    [Fact]
    public void APacketForTheCurrentFrameDoesNothing()
    {
        FrameArrival arrival = VideoFrameSequencer.Arrive(7, 7, 6, 6);

        Assert.False(arrival.Stale);
        Assert.False(arrival.StartsFrame);
        Assert.False(arrival.ReportCorrupt);
    }

    /// <summary>A packet for a frame already past is dropped.</summary>
    [Fact]
    public void AnOldPacketIsStale()
    {
        Assert.True(VideoFrameSequencer.Arrive(5, 7, 6, 6).Stale);
    }

    /// <summary>
    /// The gap: the range reported runs from the last COMPLETE frame plus one to the frame before
    /// this one, which is not the same as from the last frame seen.
    /// </summary>
    [Fact]
    public void AGapIsMeasuredFromTheLastCompleteFrame()
    {
        // 8 arrives; 5 was the last complete one even though 6 was started.
        FrameArrival arrival = VideoFrameSequencer.Arrive(8, frameIndexCur: 6, frameIndexPrev: 6, frameIndexPrevComplete: 5);

        Assert.True(arrival.StartsFrame);
        Assert.True(arrival.ReportCorrupt);
        Assert.Equal(6, arrival.CorruptFrom);
        Assert.Equal(7, arrival.CorruptTo);
    }

    /// <summary>The previous frame is flushed only when it was started and never finished.</summary>
    [Theory]
    [InlineData(6, 6, false)]  // cur was already flushed - prev caught up with it
    [InlineData(6, 5, true)]   // cur was started and never flushed
    public void ThePreviousFrameIsFlushedOnlyWhenUnfinished(int cur, int prev, bool expected)
        => Assert.Equal(expected, VideoFrameSequencer.Arrive(9, cur, prev, prev).FlushPrevious);

    /// <summary>
    /// And it all still holds across the wrap, which is the case a port written on small numbers
    /// gets wrong and never finds out about.
    ///
    /// 0 following 65535 is the NEXT frame, not sixty-five thousand frames of loss - so it opens a
    /// frame, reports nothing, and the packet for 65535 arriving after it is the stale one.
    /// </summary>
    [Fact]
    public void TheDecisionSurvivesTheSequenceWrap()
    {
        FrameArrival wrapped = VideoFrameSequencer.Arrive(0, frameIndexCur: 65535, frameIndexPrev: 65535, frameIndexPrevComplete: 65535);

        Assert.False(wrapped.Stale);
        Assert.True(wrapped.StartsFrame);
        Assert.False(wrapped.ReportCorrupt);

        Assert.True(VideoFrameSequencer.Arrive(65535, frameIndexCur: 0, frameIndexPrev: 0, frameIndexPrevComplete: 0).Stale);

        // A gap across the wrap reports a wrapped range rather than a huge one.
        FrameArrival gap = VideoFrameSequencer.Arrive(2, frameIndexCur: 65534, frameIndexPrev: 65534, frameIndexPrevComplete: 65534);
        Assert.True(gap.ReportCorrupt);
        Assert.Equal(65535, gap.CorruptFrom);
        Assert.Equal(1, gap.CorruptTo);
    }

    /// <summary>
    /// THE DRIFT CHECK. The four conditions the table above was read from are still the C's.
    /// </summary>
    [Fact]
    public void TheCStillMakesTheseDecisions()
    {
        string? file = SanitizerSource.LocateRelative(VideoReceiverSource.RelativePath);
        Assert.True(file is not null, "no videoreceiver.c - this whole file is describing nothing");

        string core = File.ReadAllText(file);

        Assert.True(VideoReceiverSource.OldFrameIsStillGuardedOnCurrent(core),
            "the stale test no longer checks frame_index_cur before comparing against it");
        Assert.True(VideoReceiverSource.ANewFrameIsStillGreaterThanCurrent(core),
            "a new frame is no longer decided by seq_num_16_gt against frame_index_cur");
        Assert.True(VideoReceiverSource.TheGapIsStillFromPrevComplete(core),
            "the corrupt range no longer starts at frame_index_prev_complete + 1");
        Assert.True(VideoReceiverSource.FrameOneIsStillExcepted(core),
            "the 'ok for frame 1' exception is gone, so the first frame of every session now reports a gap");
    }
}
