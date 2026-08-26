using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP344, under PP294: the queue everything outside the ctrl thread sends through, and the one
/// place the two send paths disagree with each other.
/// </summary>
public class CtrlSendQueueTests
{
    /// <summary>
    /// THE PAYLOAD IS COPIED, which is what lets a caller free its buffer on return.
    ///
    /// Asserted by overwriting the source the way a caller reusing a stack buffer would.
    /// </summary>
    [Fact]
    public void ThePayloadIsCopiedAndNotRetained()
    {
        var queue = new CtrlSendQueue();
        byte[] buffer = [0xDE, 0xAD, 0xBE, 0xEF];

        queue.Enqueue((ushort)CtrlMessage.GoHome, buffer);
        Array.Fill(buffer, (byte)0);

        Assert.Equal<byte[]>([0xDE, 0xAD, 0xBE, 0xEF], Assert.Single(queue.Drain()).Payload);
    }

    /// <summary>Order is preserved, because the C appends at the tail.</summary>
    [Fact]
    public void MessagesComeOutInTheOrderTheyWentIn()
    {
        var queue = new CtrlSendQueue();

        queue.Enqueue((ushort)CtrlMessage.KeyboardEnable, [1]);
        queue.Enqueue((ushort)CtrlMessage.KeyboardTextChangeReq, [2]);
        queue.Enqueue((ushort)CtrlMessage.KeyboardCloseReq, [3]);

        Assert.Equal(
            [(ushort)CtrlMessage.KeyboardEnable, (ushort)CtrlMessage.KeyboardTextChangeReq,
             (ushort)CtrlMessage.KeyboardCloseReq],
            queue.Drain().Select(m => m.Type));
    }

    /// <summary>Draining empties it, so a second drain returns nothing.</summary>
    [Fact]
    public void DrainingEmptiesTheQueue()
    {
        var queue = new CtrlSendQueue();
        queue.Enqueue((ushort)CtrlMessage.GotoBed, []);

        Assert.Single(queue.Drain());
        Assert.Empty(queue.Drain());
        Assert.Equal(0, queue.Count);
    }

    /// <summary>
    /// THE TWO SEND PATHS DISAGREE, and both answers are reproduced rather than reconciled.
    ///
    /// A null payload with a non-zero size is INVALID_DATA on the path the ctrl thread takes and a
    /// silently discarded argument on the path everybody else takes. The branch is on the pointer,
    /// so the size never gets looked at.
    /// </summary>
    [Fact]
    public void ANullPayloadWithASizeIsRefusedOnOnePathAndZeroedOnTheOther()
    {
        Assert.False(CtrlSendQueue.TheDirectSendWouldAccept(payloadIsNull: true, payloadSize: 16));
        Assert.Equal(0, CtrlSendQueue.TheQueuedSendRecordsSize(payloadIsNull: true, payloadSize: 16));
    }

    /// <summary>And both agree where the size is zero, or where there is a buffer.</summary>
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(false, 16)]
    public void TheTwoPathsAgreeEverywhereElse(bool nullPayload, int size)
    {
        Assert.True(CtrlSendQueue.TheDirectSendWouldAccept(nullPayload, size));
        Assert.Equal(nullPayload ? 0 : size, CtrlSendQueue.TheQueuedSendRecordsSize(nullPayload, size));
    }

    /// <summary>A queued message with no buffer carries an empty payload, not a null one.</summary>
    [Fact]
    public void AMessageWithNoBufferCarriesAnEmptyPayload()
    {
        var queue = new CtrlSendQueue();
        queue.Enqueue((ushort)CtrlMessage.GotoBed, [], payloadIsNull: true);

        QueuedCtrlMessage only = Assert.Single(queue.Drain());

        Assert.NotNull(only.Payload);
        Assert.Empty(only.Payload);
    }

    /// <summary>goto-bed is a queued send and nothing more.</summary>
    [Fact]
    public void GotoBedIsAQueuedSendWithNoPayload()
    {
        QueuedCtrlMessage bed = CtrlSendQueue.GotoBed();

        Assert.Equal((ushort)CtrlMessage.GotoBed, bed.Type);
        Assert.Empty(bed.Payload);
    }

    /// <summary>And ctrl.c still has both paths, still disagreeing.</summary>
    [Fact]
    public void CtrlStillDeclaresBothPaths()
    {
        string? path = CtrlSendQueueSource.Locate();
        if (path is null)
            return;

        string? queued = CtrlSendQueueSource.QueuedSendBody(path);
        string? direct = CtrlSendQueueSource.DirectSendBody(path);

        Assert.NotNull(queued);
        Assert.NotNull(direct);

        Assert.True(
            CtrlSendQueueSource.ThePayloadIsStillCopied(queued),
            "the queued send no longer copies the payload");
        Assert.True(
            CtrlSendQueueSource.ItIsStillAppendedAtTheTail(queued),
            "the queue is no longer appended at the tail, so order has changed");
        Assert.True(
            CtrlSendQueueSource.ANullPayloadIsStillNormalised(queued),
            "the queued send no longer branches on the pointer");
        Assert.True(
            CtrlSendQueueSource.TheDirectSendStillRefusesIt(direct),
            "the direct send no longer refuses a null payload with a size");
    }
}
