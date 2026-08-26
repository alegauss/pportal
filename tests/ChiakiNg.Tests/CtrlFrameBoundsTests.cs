using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP346, under PP294: the bound the read loop's arithmetic used to defeat.
/// </summary>
public class CtrlFrameBoundsTests
{
    /// <summary>An ordinary message, fully buffered, is dispatched.</summary>
    [Theory]
    [InlineData(0u, 8)]
    [InlineData(16u, 24)]
    [InlineData(504u, 512)]
    public void AWholeMessageIsDispatched(uint announced, int buffered)
    {
        Assert.Equal(FrameVerdict.Dispatch, CtrlFrameBounds.Judge(announced, buffered));
    }

    /// <summary>One that has not all arrived waits.</summary>
    [Theory]
    [InlineData(16u, 8)]
    [InlineData(16u, 23)]
    [InlineData(504u, 511)]
    public void APartialMessageWaits(uint announced, int buffered)
    {
        Assert.Equal(FrameVerdict.Incomplete, CtrlFrameBounds.Judge(announced, buffered));
    }

    /// <summary>Fewer than eight bytes is not a message yet, whatever they say.</summary>
    [Fact]
    public void FewerThanAHeaderIsNotAMessage()
    {
        Assert.Equal(FrameVerdict.Incomplete, CtrlFrameBounds.Judge(0xFFFFFFFF, 7));
    }

    /// <summary>
    /// A length this buffer can never hold ends the channel rather than waiting for the rest.
    ///
    /// 505 is the first: the buffer is 512 and the header takes eight.
    /// </summary>
    [Theory]
    [InlineData(505u)]
    [InlineData(1024u)]
    [InlineData(0x7FFFFFFFu)]
    public void AnImpossibleLengthIsAnOverflow(uint announced)
    {
        Assert.Equal(FrameVerdict.Overflow, CtrlFrameBounds.Judge(announced, 8));
    }

    /// <summary>
    /// THE DEFECT: every length that wrapped the old sum was DISPATCHED.
    ///
    /// 0xFFFFFFF8 through 0xFFFFFFFF made `8 + payload_size` come out between zero and seven. With
    /// at least eight bytes buffered the completeness test was false, the overflow test below it was
    /// never reached, and the message went to a handler that decrypts in place over the announced
    /// length - four gigabytes, from eight bytes into a 512-byte buffer.
    /// </summary>
    [Theory]
    [InlineData(0xFFFFFFF8u)]
    [InlineData(0xFFFFFFFCu)]
    [InlineData(0xFFFFFFFFu)]
    public void EveryLengthThatWrappedTheOldSumUsedToBeDispatched(uint announced)
    {
        Assert.True(CtrlFrameBounds.WrapsTheOldSum(announced));

        // What it did.
        Assert.Equal(FrameVerdict.Dispatch, CtrlFrameBounds.JudgeAsItWas(announced, buffered: 8));

        // What it does.
        Assert.Equal(FrameVerdict.Overflow, CtrlFrameBounds.Judge(announced, buffered: 8));
    }

    /// <summary>
    /// And the two spellings agree everywhere the sum does not wrap, which is what makes the fix a
    /// fix rather than a change.
    /// </summary>
    [Theory]
    [InlineData(0u, 8)]
    [InlineData(16u, 24)]
    [InlineData(16u, 8)]
    [InlineData(504u, 512)]
    [InlineData(505u, 8)]
    [InlineData(100000u, 8)]
    public void TheTwoSpellingsAgreeWhereverTheSumDoesNotWrap(uint announced, int buffered)
    {
        Assert.False(CtrlFrameBounds.WrapsTheOldSum(announced));
        Assert.Equal(
            CtrlFrameBounds.JudgeAsItWas(announced, buffered),
            CtrlFrameBounds.Judge(announced, buffered));
    }

    /// <summary>
    /// The largest payload is the buffer less its header, and 505 is refused where 504 is not.
    /// </summary>
    [Fact]
    public void TheLargestPayloadIsTheBufferLessItsHeader()
    {
        Assert.Equal(504, CtrlFrameBounds.LargestPayload);
        Assert.Equal(FrameVerdict.Dispatch, CtrlFrameBounds.Judge(504, 512));
        Assert.Equal(FrameVerdict.Overflow, CtrlFrameBounds.Judge(505, 512));
    }

    /// <summary>
    /// PP347: the source buffer is larger than the destination, which is what makes the missing
    /// bound reachable with a single well-formed message.
    /// </summary>
    [Fact]
    public void TheRudpBufferIsLargerThanTheOneItFeeds()
    {
        Assert.True(CtrlFrameBounds.RudpReceiveBufferSize > CtrlFrameBounds.ReceiveBufferSize);

        // A message filling the source does not fit the destination, even empty.
        Assert.False(CtrlFrameBounds.FitsInTheCtrlBuffer(
            CtrlFrameBounds.RudpReceiveBufferSize, buffered: 0));
    }

    /// <summary>
    /// And the fill is what the framing loop left, so a partial message raises the bar.
    ///
    /// 512 bytes fit an empty buffer exactly; one byte already there and they do not.
    /// </summary>
    [Theory]
    [InlineData(512, 0, true)]
    [InlineData(512, 1, false)]
    [InlineData(504, 8, true)]
    [InlineData(505, 8, false)]
    [InlineData(0, 512, true)]
    public void WhatFitsDependsOnWhatIsAlreadyThere(int messageBytes, int buffered, bool fits)
    {
        Assert.Equal(fits, CtrlFrameBounds.FitsInTheCtrlBuffer(messageBytes, buffered));
    }

    /// <summary>And ctrl.c still bounds the length alone, before the test it protects.</summary>
    [Fact]
    public void CtrlStillBoundsTheLengthAlone()
    {
        string? path = CtrlFrameBoundsSource.Locate();
        if (path is null)
            return;

        string? thread = CtrlFrameBoundsSource.ThreadBody(path);
        Assert.NotNull(thread);

        Assert.True(
            CtrlFrameBoundsSource.TheBoundIsStillOnTheLengthAlone(thread),
            "the overflow bound is back on a sum that can wrap");
        Assert.True(
            CtrlFrameBoundsSource.TheBoundStillComesFirst(thread),
            "the bound no longer comes before the completeness test it protects");
    }

    /// <summary>
    /// PP347: and every copy into that buffer is guarded by the room left in it.
    ///
    /// Counted rather than located, because two arms had the same defect and a third written the
    /// same way would be a third.
    /// </summary>
    [Fact]
    public void NoCopyIntoTheCtrlBufferIsUnbounded()
    {
        string? path = CtrlFrameBoundsSource.Locate();
        if (path is null)
            return;

        string? thread = CtrlFrameBoundsSource.ThreadBody(path);
        Assert.NotNull(thread);

        Assert.Equal(0, CtrlFrameBoundsSource.UnboundedCopiesIntoTheCtrlBuffer(thread));
    }

    /// <summary>And the counter finds one where there is one, so the check above means something.</summary>
    [Fact]
    public void TheCounterFindsAnUnboundedCopy()
    {
        const string asItWas = """
            				if((message.data_size - offset - 8) == ctrl_payload_size)
            				{
            					memcpy(ctrl->recv_buf + ctrl->recv_buf_size, message.data + offset, message.data_size - offset);
            					ctrl->recv_buf_size += message.data_size - offset;
            				}
            """;

        Assert.Equal(1, CtrlFrameBoundsSource.UnboundedCopiesIntoTheCtrlBuffer(asItWas));
    }
}
