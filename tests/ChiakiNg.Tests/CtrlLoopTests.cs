using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP349, under PP294: the ctrl thread's loop - what a wake-up means, and how the buffer is framed.
///
/// None of the orderings here are in PP297's capture. A recording shows what crossed, not which
/// branch the thread was in when it crossed.
/// </summary>
public class CtrlLoopTests
{
    /// <summary>
    /// CANCELLED IS THE WORK BRANCH, and it is the first thing to get right.
    ///
    /// A port reading it as a failure sends nothing anybody queued; one reading it as a timeout
    /// spins. Same reading PP336 needed for the stream connection.
    /// </summary>
    [Fact]
    public void ACancelledWaitIsWorkAndNotAFailure()
    {
        Assert.Equal(
            CtrlStep.DrainQueue,
            CtrlLoop.Next(CtrlWake.Cancelled, new CtrlWakeState(QueueHasWork: true)));

        Assert.Equal(CtrlStep.Fail, CtrlLoop.Next(CtrlWake.Failed, new CtrlWakeState()));
    }

    /// <summary>The three conditions are read in the C's order: queue, then PIN, then stop.</summary>
    [Fact]
    public void TheQueueIsDrainedBeforeThePinAndThePinBeforeTheStop()
    {
        var everything = new CtrlWakeState(ShouldStop: true, QueueHasWork: true, PinEntered: true);

        Assert.Equal(CtrlStep.DrainQueue, CtrlLoop.Next(CtrlWake.Cancelled, everything));

        Assert.Equal(
            CtrlStep.SendPin,
            CtrlLoop.Next(CtrlWake.Cancelled, everything with { QueueHasWork = false }));

        Assert.Equal(
            CtrlStep.Stop,
            CtrlLoop.Next(CtrlWake.Cancelled, everything with { QueueHasWork = false, PinEntered = false }));
    }

    /// <summary>A poke with nothing to do waits again, which a spurious wake-up looks like.</summary>
    [Fact]
    public void APokeWithNothingToDoWaitsAgain()
    {
        Assert.Equal(CtrlStep.Wait, CtrlLoop.Next(CtrlWake.Cancelled, new CtrlWakeState()));
    }

    /// <summary>A readable socket is read, whatever else is pending.</summary>
    [Fact]
    public void AReadableSocketIsRead()
    {
        Assert.Equal(
            CtrlStep.Receive,
            CtrlLoop.Next(CtrlWake.Readable, new CtrlWakeState(QueueHasWork: true)));
    }

    /// <summary>
    /// The thread does not wait where it already has work, or a message queued while it was framing
    /// would sit until something else poked the pipe.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void TheThreadOnlyWaitsWhenItHasNothing(bool stop, bool queue, bool pin, bool waits)
    {
        Assert.Equal(waits, CtrlLoop.WaitsFirst(new CtrlWakeState(stop, queue, pin)));
    }

    /// <summary>
    /// A PIN IN THE SAME WAKE-UP DEFERS A STOP BY ONE PASS, because that branch continues.
    ///
    /// A queued message does not defer it: the drain runs to empty and the stop is read after.
    /// One extra pass either way, and the kind of ordering a rewrite changes without noticing.
    /// </summary>
    [Fact]
    public void APinDefersAStopAndAQueuedMessageDoesNot()
    {
        Assert.False(CtrlLoop.StopIsActedOnNow(new CtrlWakeState(ShouldStop: true, PinEntered: true)));
        Assert.True(CtrlLoop.StopIsActedOnNow(new CtrlWakeState(ShouldStop: true, QueueHasWork: true)));
    }

    /// <summary>One whole message is framed out of the buffer, header and payload.</summary>
    [Fact]
    public void AWholeMessageIsFramed()
    {
        var buffer = new CtrlReceiveBuffer();

        Assert.True(buffer.Append([.. CtrlFraming.Header(0x33, 4), 1, 2, 3, 4]));

        (ushort type, byte[] payload) = Assert.NotNull(buffer.TakeMessage());

        Assert.Equal(0x33, type);
        Assert.Equal<byte[]>([1, 2, 3, 4], payload);
        Assert.Equal(0, buffer.Filled);
    }

    /// <summary>
    /// TWO MESSAGES IN ONE READ COME OUT AS TWO, which is what the compaction is for.
    ///
    /// The stream is a sequence of messages and not of packets, so a read carrying one and a half
    /// has to leave the half where the next read can complete it.
    /// </summary>
    [Fact]
    public void TwoMessagesInOneReadComeOutAsTwo()
    {
        var buffer = new CtrlReceiveBuffer();

        buffer.Append(
        [
            .. CtrlFraming.Header(0xfe, 0),
            .. CtrlFraming.Header(0x16, 2), 0x01, 0xff,
        ]);

        Assert.Equal(0xfe, Assert.NotNull(buffer.TakeMessage()).Type);

        (ushort type, byte[] payload) = Assert.NotNull(buffer.TakeMessage());
        Assert.Equal(0x16, type);
        Assert.Equal<byte[]>([0x01, 0xff], payload);

        Assert.Null(buffer.TakeMessage());
    }

    /// <summary>And a message split across two reads is completed by the second.</summary>
    [Fact]
    public void AMessageSplitAcrossTwoReadsIsCompletedByTheSecond()
    {
        var buffer = new CtrlReceiveBuffer();

        buffer.Append([.. CtrlFraming.Header(0x5, 4), 0xAA, 0xBB]);
        Assert.Null(buffer.TakeMessage());

        buffer.Append([0xCC, 0xDD]);

        Assert.Equal<byte[]>([0xAA, 0xBB, 0xCC, 0xDD], Assert.NotNull(buffer.TakeMessage()).Payload);
    }

    /// <summary>
    /// The remainder is moved to the front, so a partial message left behind is at offset zero for
    /// the next read to extend.
    /// </summary>
    [Fact]
    public void ThePartialRemainderIsMovedToTheFront()
    {
        var buffer = new CtrlReceiveBuffer();

        buffer.Append([.. CtrlFraming.Header(0xfe, 0), .. CtrlFraming.Header(0x33, 8), 1, 2]);
        buffer.TakeMessage();

        // Ten bytes left: an eight-byte header and two of eight payload bytes.
        Assert.Equal(10, buffer.Filled);
        Assert.Equal(0x33, CtrlFraming.TypeOf(buffer.Buffered));
    }

    /// <summary>
    /// PP346/PP347: a message the buffer can never hold is recognised, and one that does not fit is
    /// refused rather than written past the end.
    /// </summary>
    [Fact]
    public void AnImpossibleMessageIsRecognisedAndAnOverlongAppendIsRefused()
    {
        var buffer = new CtrlReceiveBuffer();

        buffer.Append(CtrlFraming.Header(0x33, 5000));

        Assert.True(buffer.HoldsAnImpossibleMessage());
        Assert.Null(buffer.TakeMessage());

        Assert.False(buffer.Append(new byte[CtrlFrameBounds.ReceiveBufferSize]));
    }

    /// <summary>And ctrl.c's loop still has the orderings this reproduces.</summary>
    [Fact]
    public void CtrlStillDeclaresTheLoop()
    {
        string? path = CtrlLoopSource.Locate();
        if (path is null)
            return;

        string? thread = CtrlLoopSource.ThreadBody(path);
        Assert.NotNull(thread);

        Assert.True(
            CtrlLoopSource.CancelledIsStillTheWorkBranch(thread),
            "a cancelled wait is no longer where the queue is drained and the PIN is sent");
        Assert.True(
            CtrlLoopSource.TheConditionsAreStillTestedBeforeTheWait(thread),
            "the three conditions are no longer tested before the wait, so queued work can sit");
        Assert.True(
            CtrlLoopSource.ThePinBranchStillContinues(thread),
            "the PIN branch no longer starts the loop over, so a stop is read a pass earlier");
        Assert.True(
            CtrlLoopSource.TheDrainStillReleasesTheLock(thread),
            "a queued send no longer happens with the lock released");
    }
}
