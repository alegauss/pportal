using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// The head-wait one AV reorder queue carries between flushes.
/// </summary>
/// <param name="StartUs">
/// When the wait for the missing head began, or 0 for not waiting. The C's own sentinel, kept rather
/// than turned into a nullable: `0` and `no wait` are one state in the source and splitting them
/// would invent a distinction the flush does not test.
/// </param>
/// <param name="SeqNum">Which sequence number is being waited for.</param>
public readonly record struct AvHeadWait(long StartUs, ulong SeqNum);

/// <summary>What one flush pass dispatched, skipped, and left behind.</summary>
/// <param name="Dispatched">The sequence numbers pulled in order, oldest first.</param>
/// <param name="Skipped">How many missing slots the timeout stepped over, across every pass.</param>
/// <param name="Wait">The head-wait to carry into the next flush.</param>
public readonly record struct AvFlushOutcome(
    IReadOnlyList<ulong> Dispatched,
    ulong Skipped,
    AvHeadWait Wait);

/// <summary>
/// PP449: the timer half of PP27's takion - the one thing standing between a lost video packet and a
/// stalled stream.
///
/// PP23 ported the reorder queue and PP27's first part ported the send buffer. Neither is the piece
/// that decides WHEN to give up on a packet that never arrived, and without it a managed transport
/// has a queue that waits forever. That decision is two static functions in takion.c, called from
/// the receive thread and from every AV push, and it is pure arithmetic over a clock - so it is
/// modelled here and tested without a socket.
///
/// ONE TIMEOUT PER LOSS BURST, NOT ONE PER PACKET. This is the rule the whole thing exists for and
/// the one a rewrite would get wrong. When the missing head advances - the gap moved forward inside
/// the same burst - the wait keeps its ORIGINAL budget and only re-aims at the new sequence number.
/// A fresh window is opened only when the head moves BACKWARD, which the queue's rebase can do. Reset
/// the clock on every new gap instead and a burst of eight lost packets costs eight frame times, at
/// which point the timeout is worse than not having one.
///
/// AND THE SKIP JUMPS, IT DOES NOT STEP. Once the budget is spent the window advances straight to
/// the first buffered packet rather than one slot at a time, so startup - where the queue opens on a
/// packet index that is not the frame's first unit - also pays a single timeout. It does that by
/// writing `queue->begin` and `queue->count` itself; see <see cref="ReorderQueue.AdvanceBegin"/> for
/// why the port has to reach in the same way.
///
/// TWO THINGS ARE REPRODUCED RATHER THAN FIXED, both bounded and both stated because a managed
/// version that tidied them would stop agreeing with the C:
///
///   the deadline is tested two ways. <see cref="NextTimeoutMs"/> reports ready at elapsed EQUAL to
///   the timeout, and <see cref="Flush"/> skips only at elapsed strictly GREATER - so at exactly the
///   deadline the thread polls with no timeout, flushes nothing, and asks again. It spins until the
///   monotonic clock reads one microsecond further, which is the whole of it;
///
///   the clock is read once. `now` comes from the top of the call, and a fresh window opened after a
///   skip is stamped with it - so that window is short by however long the flush took. The C reads
///   the clock again for the dwell statistic in the pull loop, with a comment saying why, and then
///   does not do the same here.
///
/// ONE GUARD IS UNREACHABLE and is reproduced anyway. `if(skipped >= queue->count) break;` covers a
/// window in which every slot is a gap, and no sequence of pushes and pulls produces one: a push
/// always sets the slot it grew the window for, and a pull leaves `set` alone. So it is asserted
/// against the source rather than exercised, the same way PP448's counter underflow is.
/// </summary>
public static partial class AvReorderTimeout
{
    /// <summary>
    /// TAKION_AV_REORDER_TIMEOUT_US - one frame at 60fps, which is the budget for a packet that has
    /// not arrived.
    /// </summary>
    /// <remarks>
    /// Not the authority. <see cref="TimeoutUsIn"/> reads the define, and the test asserts the two
    /// agree, so a change in the C fails here rather than drifting.
    /// </remarks>
    public const long TimeoutUs = 16000;

    /// <summary>
    /// TAKION_AV_VIDEO_REORDER_QUEUE_SIZE_EXP - the video queue is 64 entries, four times the data
    /// queue's.
    /// </summary>
    public const int VideoQueueSizeExp = 6;

    /// <summary>
    /// What the receive thread should pass as its poll timeout, in milliseconds.
    ///
    /// Three answers and not one: <see cref="ulong.MaxValue"/> where nothing is waiting, so the poll
    /// blocks on the socket alone; 0 where the budget is spent, which the thread reads as "do not
    /// receive at all, flush now"; and otherwise the remaining budget rounded UP, so a wake is never
    /// early enough to find the deadline still ahead of it.
    /// </summary>
    public static ulong NextTimeoutMs(AvHeadWait wait, long nowUs)
    {
        if (wait.StartUs == 0)
            return ulong.MaxValue;

        long remainingUs = TimeoutUs - (nowUs - wait.StartUs);
        if (remainingUs <= 0)
            return 0;

        return (ulong)((remainingUs + 999) / 1000);
    }

    /// <summary>
    /// One flush pass: pull everything in order, and decide what the missing head costs.
    /// </summary>
    /// <param name="queue">
    /// The video reorder queue. Mutated, as the C mutates it - pulled from, and on a timeout advanced
    /// past the gap.
    /// </param>
    /// <param name="nowUs">
    /// The monotonic clock, read ONCE by the caller. Taken as a parameter rather than read inside
    /// because that is what makes the decision testable, and because the single read is itself part
    /// of the behaviour.
    /// </param>
    /// <param name="wait">The head-wait carried in from the previous pass.</param>
    public static AvFlushOutcome Flush(ReorderQueue queue, long nowUs, AvHeadWait wait)
    {
        ArgumentNullException.ThrowIfNull(queue);

        var dispatched = new List<ulong>();
        ulong skippedTotal = 0;
        long startUs = wait.StartUs;
        ulong waitSeq = wait.SeqNum;

        bool madeProgress = true;
        while (madeProgress)
        {
            madeProgress = false;

            while (queue.Pull() is { } pulled)
            {
                madeProgress = true;
                dispatched.Add(pulled.SeqNum);
            }

            // Anything at all came out, so the head is not missing any more.
            if (madeProgress)
                startUs = 0;

            if (queue.Count == 0)
                break;

            if (startUs != 0 && queue.Begin != waitSeq)
            {
                if (ReorderQueue.SeqNumGt(queue.Begin, waitSeq))
                {
                    // The gap moved forward inside the same burst: re-aim, keep the budget. No
                    // break - a budget already spent has to be able to skip on this pass.
                    waitSeq = queue.Begin;
                }
                else
                {
                    // Backward, which the queue's rebase can do: a genuinely new wait.
                    startUs = nowUs;
                    waitSeq = queue.Begin;
                    break;
                }
            }

            // First pass over this gap.
            if (startUs == 0)
            {
                startUs = nowUs;
                waitSeq = queue.Begin;
                break;
            }

            // `<=`, so the skip needs elapsed STRICTLY past the timeout. See the type's note.
            if (nowUs - startUs <= TimeoutUs)
                break;

            ulong skipped = 0;
            while (skipped < queue.Count)
            {
                if (queue.Peek(skipped) is not null)
                    break;

                skipped++;
            }

            // Every slot in the window is a gap: there is nothing to skip TO.
            if (skipped >= queue.Count)
                break;

            queue.AdvanceBegin(skipped);
            skippedTotal += skipped;
            startUs = 0;
            madeProgress = true;
        }

        return new AvFlushOutcome(dispatched, skippedTotal, new AvHeadWait(startUs, waitSeq));
    }

    /// <summary>takion.c, where both functions live.</summary>
    public const string RelativePath = @"lib\src\takion.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The flush's body, or null where the file declares no definition for it.</summary>
    public static string? FlushBody(string source)
        => CFunction.Body(source, "static void takion_av_queue_flush_with_timeout");

    /// <summary>The poll-timeout function's body.</summary>
    public static string? NextTimeoutBody(string source)
        => CFunction.Body(source, "static uint64_t takion_av_queues_next_timeout_ms");

    /// <summary>The receive thread's body, which is the one caller that polls.</summary>
    public static string? ThreadBody(string source)
        => CFunction.Body(source, "static void *takion_thread_func");

    /// <summary>
    /// TAKION_AV_REORDER_TIMEOUT_US as the C defines it, or null where the define is gone.
    ///
    /// Read rather than typed: <see cref="TimeoutUs"/> is a copy and this is what proves it is still
    /// the right one.
    /// </summary>
    public static long? TimeoutUsIn(string source)
        => CDefine.Value(source, "TAKION_AV_REORDER_TIMEOUT_US");

    /// <summary>TAKION_AV_VIDEO_REORDER_QUEUE_SIZE_EXP as the C defines it.</summary>
    public static int? VideoQueueSizeExpIn(string source)
        => (int?)CDefine.Value(source, "TAKION_AV_VIDEO_REORDER_QUEUE_SIZE_EXP");

    /// <summary>
    /// Whether a forward move of the missing head still keeps its original budget.
    ///
    /// The predicate is the ABSENCE of a clock write in that arm: the branch is there either way,
    /// and what makes it one-timeout-per-burst is that it does not restart the window.
    /// </summary>
    public static bool AForwardMoveKeepsTheBudget(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        int test = flushBody.IndexOf(
            "queue->seq_num_gt(queue->begin, *head_wait_seq_num)", StringComparison.Ordinal);
        if (test < 0)
            return false;

        int otherwise = flushBody.IndexOf("else", test, StringComparison.Ordinal);
        if (otherwise < 0)
            return false;

        string arm = flushBody[test..otherwise];

        return arm.Contains("*head_wait_seq_num = queue->begin;", StringComparison.Ordinal)
            && !arm.Contains("head_wait_start_us =", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the timeout still advances the window by writing the queue's own fields, which is
    /// what obliges the port to expose <see cref="ReorderQueue.AdvanceBegin"/>.
    /// </summary>
    public static bool TheSkipReachesIntoTheQueue(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        return flushBody.Contains(
                "queue->begin = queue->seq_num_add(queue->begin, skipped);", StringComparison.Ordinal)
            && flushBody.Contains("queue->count -= skipped;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the skip still walks to the FIRST buffered packet rather than stepping one slot.
    /// </summary>
    public static bool TheSkipJumpsToTheFirstBufferedPacket(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        return flushBody.Contains("while(skipped < queue->count)", StringComparison.Ordinal)
            && flushBody.Contains("if(skipped >= queue->count)", StringComparison.Ordinal);
    }

    /// <summary>Whether pulling anything still clears the wait.</summary>
    public static bool ProgressClearsTheWait(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        return ProgressClearsRegex().IsMatch(flushBody);
    }

    /// <summary>
    /// Whether the two deadline tests still disagree: `<= 0` remaining reports ready, and
    /// `<= TIMEOUT` elapsed declines to skip.
    /// </summary>
    public static bool TheDeadlineTestsStillDisagree(string flushBody, string nextTimeoutBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);
        ArgumentNullException.ThrowIfNull(nextTimeoutBody);

        return flushBody.Contains(
                "now - *head_wait_start_us <= TAKION_AV_REORDER_TIMEOUT_US", StringComparison.Ordinal)
            && nextTimeoutBody.Contains("if(remaining_us <= 0)", StringComparison.Ordinal);
    }

    /// <summary>Whether the remaining budget is still rounded up to the next millisecond.</summary>
    public static bool TheRemainingBudgetRoundsUp(string nextTimeoutBody)
    {
        ArgumentNullException.ThrowIfNull(nextTimeoutBody);

        return nextTimeoutBody.Contains("(remaining_us + 999) / 1000", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether an idle queue still reports a poll timeout of UINT64_MAX, which is how the thread
    /// blocks on the socket alone.
    /// </summary>
    public static bool AnIdleQueueBlocksForever(string nextTimeoutBody)
    {
        ArgumentNullException.ThrowIfNull(nextTimeoutBody);

        return nextTimeoutBody.Contains("uint64_t timeout_ms = UINT64_MAX;", StringComparison.Ordinal)
            && IdleContinueRegex().IsMatch(nextTimeoutBody);
    }

    /// <summary>
    /// Whether a zero timeout still makes the thread skip the receive entirely and flush.
    ///
    /// This is what turns the two disagreeing deadline tests into a spin rather than a late wake:
    /// the loop does not go near the socket on that pass.
    /// </summary>
    public static bool AZeroTimeoutSkipsTheReceive(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        return ZeroTimeoutRegex().IsMatch(threadBody);
    }

    /// <summary>
    /// Whether the clock is still read once per flush - the single `chiaki_time_now_monotonic_us`
    /// assigned to `now` at the top, which every window this pass opens is stamped with.
    /// </summary>
    public static bool TheClockIsReadOncePerFlush(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        return flushBody.Contains(
                "int64_t now = chiaki_time_now_monotonic_us();", StringComparison.Ordinal)
            && flushBody.Contains("*head_wait_start_us = now;", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"if\(made_progress\)\s*\r?\n\s*\*head_wait_start_us = 0;")]
    private static partial Regex ProgressClearsRegex();

    [GeneratedRegex(@"if\(head_wait_start_us == 0\)\s*\r?\n\s*continue;")]
    private static partial Regex IdleContinueRegex();

    [GeneratedRegex(
        @"if\(recv_timeout_ms == 0\)\s*\r?\n\s*\{[^}]*takion_av_queues_flush_with_timeout\(takion\);"
            + @"\s*\r?\n\s*continue;")]
    private static partial Regex ZeroTimeoutRegex();
}
