using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP449, PP27: takion's AV reorder timeout - what a video packet that never arrives costs.
///
/// PP23 covered the queue and PP27's first part covered the send buffer. This is the decision above
/// the queue: how long the stream waits for a missing head, and what it does when the wait is over.
/// The rule worth the test is that a loss burst costs ONE timeout - the arithmetic below is what
/// separates a 16ms hiccup from eight of them in a row.
/// </summary>
public class AvReorderTimeoutTests
{
    private static string? Takion()
    {
        string? path = AvReorderTimeout.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// A four-slot queue that drops from the begin, which is how takion initialises the video one -
    /// small so an overflow is reachable in three pushes rather than sixty-five.
    /// </summary>
    private static ReorderQueue VideoQueue(ushort begin)
        => new(2, begin) { DropStrategy = ReorderDropStrategy.Begin };

    /// <summary>Nothing waiting: the poll blocks on the socket and nothing else.</summary>
    [Fact]
    public void AnIdleQueueAsksToBlockForever()
    {
        Assert.Equal(ulong.MaxValue, AvReorderTimeout.NextTimeoutMs(new AvHeadWait(0, 0), 500_000));
    }

    /// <summary>
    /// The remaining budget is rounded UP, so a wake is never early enough to find the deadline
    /// still ahead of it.
    /// </summary>
    [Theory]
    [InlineData(0L, 16ul)]
    [InlineData(1L, 16ul)]
    [InlineData(1_000L, 15ul)]
    [InlineData(15_001L, 1ul)]
    [InlineData(15_999L, 1ul)]
    public void TheRemainingBudgetRoundsUpToTheNextMillisecond(long elapsedUs, ulong expectedMs)
    {
        var wait = new AvHeadWait(1_000, 42);

        Assert.Equal(expectedMs, AvReorderTimeout.NextTimeoutMs(wait, 1_000 + elapsedUs));
    }

    /// <summary>
    /// THE TWO DEADLINE TESTS DISAGREE, and this is the pair that says so.
    ///
    /// At elapsed EXACTLY the timeout the poll reports ready - the thread frees its buffer, skips the
    /// receive and flushes - and the flush declines to skip, because its own test is `<=`. So the
    /// loop comes straight back with the same answer and spins until the monotonic clock reads one
    /// microsecond further. Bounded, reproduced, and asserted rather than tidied.
    /// </summary>
    [Fact]
    public void AtExactlyTheDeadlineThePollSaysGoAndTheFlushDoesNothing()
    {
        using ReorderQueue queue = VideoQueue(100);
        queue.Push(103, 0xd0); // a gap at 100, 101 and 102

        var wait = new AvHeadWait(1_000, 100);
        long atTheDeadline = 1_000 + AvReorderTimeout.TimeoutUs;

        Assert.Equal(0ul, AvReorderTimeout.NextTimeoutMs(wait, atTheDeadline));

        AvFlushOutcome outcome = AvReorderTimeout.Flush(queue, atTheDeadline, wait);

        Assert.Equal(0ul, outcome.Skipped);
        Assert.Empty(outcome.Dispatched);
        Assert.Equal(wait, outcome.Wait);

        // One microsecond later it skips, which is the whole width of the spin.
        AvFlushOutcome past = AvReorderTimeout.Flush(queue, atTheDeadline + 1, wait);
        Assert.Equal(3ul, past.Skipped);
    }

    /// <summary>Everything in order comes out, and the wait is cleared rather than kept.</summary>
    [Fact]
    public void AnInOrderRunIsDispatchedAndClearsTheWait()
    {
        using ReorderQueue queue = VideoQueue(100);
        queue.Push(100, 0xa0);
        queue.Push(101, 0xa1);

        AvFlushOutcome outcome = AvReorderTimeout.Flush(queue, 5_000, new AvHeadWait(1_000, 100));

        Assert.Equal(new ulong[] { 100, 101 }, outcome.Dispatched);
        Assert.Equal(0ul, outcome.Skipped);
        Assert.Equal(0, outcome.Wait.StartUs);
        Assert.Equal(0ul, queue.Count);
    }

    /// <summary>A missing head opens the window, dispatches nothing, and names what it waits for.</summary>
    [Fact]
    public void AMissingHeadOpensTheWindow()
    {
        using ReorderQueue queue = VideoQueue(100);
        queue.Push(101, 0xa1);

        AvFlushOutcome outcome = AvReorderTimeout.Flush(queue, 5_000, new AvHeadWait(0, 100));

        Assert.Empty(outcome.Dispatched);
        Assert.Equal(new AvHeadWait(5_000, 100), outcome.Wait);
    }

    /// <summary>
    /// THE RULE: a loss burst costs ONE timeout.
    ///
    /// The window opens waiting for 100. More packets arrive, the queue overflows, and its
    /// drop-from-begin walks the head forward to 101 - which is also missing. The wait re-aims at 101
    /// and KEEPS the budget it opened with, so the skip fires 16001us after the ORIGINAL wait began
    /// rather than 16001us after the head moved.
    ///
    /// Restart the clock on each new gap instead and every lost packet in the burst costs its own
    /// frame time, which is the failure this arithmetic exists to prevent.
    /// </summary>
    [Fact]
    public void AForwardMoveOfTheMissingHeadKeepsTheOriginalBudget()
    {
        using ReorderQueue queue = VideoQueue(100);
        queue.Push(103, 0xd3);
        queue.Push(104, 0xd4); // overflows, walking begin from 100 to 101

        Assert.Equal(101ul, queue.Begin);

        var wait = new AvHeadWait(1_000, 100);
        AvFlushOutcome outcome = AvReorderTimeout.Flush(
            queue, 1_000 + AvReorderTimeout.TimeoutUs + 1, wait);

        // 101 and 102 were skipped in one go, and 103 and 104 came out behind them.
        Assert.Equal(2ul, outcome.Skipped);
        Assert.Equal(new ulong[] { 103, 104 }, outcome.Dispatched);

        // Re-aimed at the new head, and the clock was never restarted.
        Assert.Equal(101ul, outcome.Wait.SeqNum);
        Assert.Equal(0, outcome.Wait.StartUs);
    }

    /// <summary>
    /// And the other direction opens a FRESH window: a head that moved backward is a new wait, so the
    /// budget already spent buys nothing.
    ///
    /// The queue's rebase is what moves it backward, and PP149's antipode reaches here too - at half
    /// the sequence space apart `seq_num_gt` is false in both directions, so that case takes this arm.
    /// </summary>
    [Fact]
    public void ABackwardMoveOfTheMissingHeadStartsAFreshWindow()
    {
        using ReorderQueue queue = VideoQueue(100);
        queue.Push(103, 0xd3);

        long now = 1_000 + AvReorderTimeout.TimeoutUs + 1;
        AvFlushOutcome outcome = AvReorderTimeout.Flush(queue, now, new AvHeadWait(1_000, 105));

        Assert.Equal(0ul, outcome.Skipped);
        Assert.Equal(new AvHeadWait(now, 100), outcome.Wait);
    }

    /// <summary>
    /// The skip JUMPS to the first buffered packet rather than stepping one slot per timeout.
    ///
    /// Three missing packets, one timeout. Stepping would charge a frame time each, and startup -
    /// where the queue opens on a packet index that is not the frame's first unit - would pay it for
    /// every unit it opened short of.
    /// </summary>
    [Fact]
    public void OneTimeoutSkipsEveryMissingPacketBeforeTheFirstBufferedOne()
    {
        using ReorderQueue queue = VideoQueue(100);
        queue.Push(103, 0xd3);

        AvFlushOutcome outcome = AvReorderTimeout.Flush(
            queue, 1_000 + AvReorderTimeout.TimeoutUs + 1, new AvHeadWait(1_000, 100));

        Assert.Equal(3ul, outcome.Skipped);
        Assert.Equal(new ulong[] { 103 }, outcome.Dispatched);
        Assert.Equal(0ul, queue.Count);
    }

    /// <summary>
    /// The skip writes the queue's own begin and count, which no queue function does - so the port
    /// has to expose the same reach-in.
    /// </summary>
    [Fact]
    public void AdvancingTheWindowDropsNothingAndTellsNobody()
    {
        using ReorderQueue queue = VideoQueue(100);
        queue.Push(103, 0xd3);

        queue.AdvanceBegin(3);

        Assert.Equal(103ul, queue.Begin);
        Assert.Equal(1ul, queue.Count);
        Assert.Empty(queue.Drops);
    }

    /// <summary>And it refuses to walk past the window's end rather than wrapping into it.</summary>
    [Fact]
    public void AdvancingPastTheWindowIsRefused()
    {
        using ReorderQueue queue = VideoQueue(100);
        queue.Push(101, 0xa1);

        Assert.Throws<ArgumentOutOfRangeException>(() => queue.AdvanceBegin(3));
    }

    /// <summary>The two constants are the C's, read from the defines rather than trusted here.</summary>
    [Fact]
    public void TheConstantsAreStillTheCs()
    {
        if (Takion() is not { } source)
            return;

        Assert.Equal((long?)AvReorderTimeout.TimeoutUs, AvReorderTimeout.TimeoutUsIn(source));
        Assert.Equal(
            (int?)AvReorderTimeout.VideoQueueSizeExp, AvReorderTimeout.VideoQueueSizeExpIn(source));
    }

    /// <summary>The one-timeout-per-burst rule is still written the way this models it.</summary>
    [Fact]
    public void TheForwardMoveStillKeepsTheBudgetInTheC()
    {
        if (Takion() is not { } source || AvReorderTimeout.FlushBody(source) is not { } body)
            return;

        Assert.True(
            AvReorderTimeout.AForwardMoveKeepsTheBudget(body),
            "the flush restarts the clock when the missing head advances, so a loss burst now costs "
                + "one timeout per packet and this model is behind the C");
        Assert.True(AvReorderTimeout.ProgressClearsTheWait(body));
    }

    /// <summary>The skip is still a jump, and still made by writing the queue's fields.</summary>
    [Fact]
    public void TheSkipIsStillAJumpIntoTheQueue()
    {
        if (Takion() is not { } source || AvReorderTimeout.FlushBody(source) is not { } body)
            return;

        Assert.True(AvReorderTimeout.TheSkipJumpsToTheFirstBufferedPacket(body));
        Assert.True(AvReorderTimeout.TheSkipReachesIntoTheQueue(body));
    }

    /// <summary>
    /// The disagreement at the deadline is still there, and the thread still skips the receive on a
    /// zero - which is what makes it a spin rather than an early wake.
    /// </summary>
    [Fact]
    public void TheDeadlineDisagreementIsStillInTheC()
    {
        if (Takion() is not { } source)
            return;

        if (AvReorderTimeout.FlushBody(source) is not { } flush
            || AvReorderTimeout.NextTimeoutBody(source) is not { } next
            || AvReorderTimeout.ThreadBody(source) is not { } thread)
        {
            return;
        }

        Assert.True(AvReorderTimeout.TheDeadlineTestsStillDisagree(flush, next));
        Assert.True(AvReorderTimeout.AZeroTimeoutSkipsTheReceive(thread));
        Assert.True(AvReorderTimeout.TheRemainingBudgetRoundsUp(next));
        Assert.True(AvReorderTimeout.AnIdleQueueBlocksForever(next));
        Assert.True(AvReorderTimeout.TheClockIsReadOncePerFlush(flush));
    }

    /// <summary>PP272: and every reader says no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(AvReorderTimeout.TimeoutUsIn(""));
        Assert.Null(AvReorderTimeout.VideoQueueSizeExpIn(""));
        Assert.Null(AvReorderTimeout.FlushBody(""));
        Assert.Null(AvReorderTimeout.NextTimeoutBody(""));
        Assert.Null(AvReorderTimeout.ThreadBody(""));
        Assert.False(AvReorderTimeout.AForwardMoveKeepsTheBudget(""));
        Assert.False(AvReorderTimeout.TheSkipReachesIntoTheQueue(""));
        Assert.False(AvReorderTimeout.TheSkipJumpsToTheFirstBufferedPacket(""));
        Assert.False(AvReorderTimeout.ProgressClearsTheWait(""));
        Assert.False(AvReorderTimeout.TheDeadlineTestsStillDisagree("", ""));
        Assert.False(AvReorderTimeout.TheRemainingBudgetRoundsUp(""));
        Assert.False(AvReorderTimeout.AnIdleQueueBlocksForever(""));
        Assert.False(AvReorderTimeout.AZeroTimeoutSkipsTheReceive(""));
        Assert.False(AvReorderTimeout.TheClockIsReadOncePerFlush(""));
    }
}
