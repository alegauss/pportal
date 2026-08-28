using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the resend loop does with one buffered packet on one pass.</summary>
public enum ResendStep
{
    /// <summary>Not due yet - its last send is inside the timeout.</summary>
    NotDue,

    /// <summary>Due, and sent again with its try count raised.</summary>
    Resent,

    /// <summary>Due, and out of tries: acked as if the console had, and dropped.</summary>
    GivenUp,
}

/// <summary>
/// PP475, under PP27: the resend loop, which is the last thing PP27's own sentence names.
///
/// PP27's remainder is "the socket, the receive thread, the handshake and the resend loop". PP125 and
/// PP27's first part drove the send buffer across the seam - push, the at-or-before ack, the drop - and
/// PP449, PP450, PP473 did the thread's timer, the handshake and the postpone. This is the buffer's own
/// thread: the one that decides a packet was lost.
///
/// IT WAITS TWO DIFFERENT WAYS. With packets buffered it waits with a timeout of half the resend
/// interval, so it wakes to re-examine them; with none it waits without a timeout at all and only a
/// push wakes it. So an idle stream costs nothing and a busy one is checked twice per resend window,
/// which is a deliberate pair a port would collapse into one poll.
///
/// GIVING UP ACKS THE PACKET TO ITSELF. At the try limit the loop does not drop the packet quietly - it
/// calls the same ack path the console's acknowledgement would, so everything waiting on that sequence
/// number is released exactly as if it had arrived. That is the one place this code lies to the rest of
/// the transport on purpose, and it is the behaviour a rewrite is most likely to replace with a silent
/// removal.
///
/// AND IT DROPS THE MUTEX TO DO IT. The ack takes the lock itself, so the loop unlocks, acks and
/// relocks mid-iteration. The pointer it was holding is stale afterwards, which the code respects by
/// continuing rather than using it - but the count it is iterating against changed under it.
///
/// WHICH IS WHERE PP464'S IDIOM TURNS UP A SECOND TIME. The step back after a removal is written
/// `if(i > 0) i -= 1;`, exactly as discovery's host drop was before PP464 fixed it, so index 0 is the
/// one place the packet shifted into the slot is not re-examined. See
/// <see cref="TheStepBackIsGuardedLikePP464sWas"/>: filed rather than fixed, because ack removes every
/// packet at or before the sequence number and may remove several, so whether stepping back by ONE is
/// the right repair here is a question PP464's answer does not settle.
/// </summary>
public static class TakionResendLoop
{
    /// <summary>TAKION_DATA_RESEND_TIMEOUT_MS - how long a packet waits before it is resent.</summary>
    public const int ResendTimeoutMs = 200;

    /// <summary>
    /// TAKION_DATA_RESEND_WAKEUP_TIMEOUT_MS - half the above, so a due packet is never a full interval
    /// late.
    /// </summary>
    public const int WakeupTimeoutMs = ResendTimeoutMs / 2;

    /// <summary>TAKION_DATA_RESEND_TRIES_MAX - how many sends before the loop gives up.</summary>
    public const int TriesMax = 25;

    /// <summary>TAKION_SEND_BUFFER_SIZE - how many packets may be in flight.</summary>
    public const int BufferSize = 16;

    /// <summary>
    /// How long the next wait is, in milliseconds, or null for no timeout at all.
    ///
    /// The pair is the behaviour: buffered packets need re-examining, and an empty buffer has nothing
    /// to re-examine, so it waits to be pushed to instead of polling.
    /// </summary>
    public static int? WaitFor(int bufferedPackets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bufferedPackets);

        return bufferedPackets > 0 ? WakeupTimeoutMs : null;
    }

    /// <summary>Whether the loop leaves rather than resending, given how the wait ended.</summary>
    public static bool Leaves(ChiakiError wait, bool shouldStop)
        => (wait != ChiakiError.Success && wait != ChiakiError.Timeout) || shouldStop;

    /// <summary>
    /// What one buffered packet gets on this pass.
    /// </summary>
    /// <param name="sinceLastSendMs">How long since it was last put on the wire.</param>
    /// <param name="tries">How many times it has been sent already.</param>
    public static ResendStep Next(long sinceLastSendMs, int tries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tries);

        // Strictly greater, as the C writes it: a packet exactly at the timeout waits one more pass.
        if (sinceLastSendMs <= ResendTimeoutMs)
            return ResendStep.NotDue;

        return tries >= TriesMax ? ResendStep.GivenUp : ResendStep.Resent;
    }

    /// <summary>
    /// Whether giving up releases the sequence number to everything waiting on it - which it does, by
    /// taking the console's own ack path.
    /// </summary>
    public const bool GivingUpAcksLikeTheConsole = true;

    /// <summary>takionsendbuffer.c.</summary>
    public const string RelativePath = @"lib\src\takionsendbuffer.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>A `#define` from it.</summary>
    public static long? DefineIn(string source, string name) => CDefine.Value(source, name);

    /// <summary>The thread's body.</summary>
    public static string? ThreadBody(string source)
        => CFunction.Body(source, "static void *takion_send_buffer_thread_func");

    /// <summary>The resend pass's body.</summary>
    public static string? ResendBody(string source)
        => CFunction.Body(source, "static void takion_send_buffer_resend");

    /// <summary>
    /// Whether the two waits are still different - a timeout with packets, none without.
    /// </summary>
    public static bool TheTwoWaitsAreStillDifferent(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        return threadBody.Contains(
                "chiaki_cond_timedwait_pred(&send_buffer->cond, &send_buffer->mutex, TAKION_DATA_RESEND_WAKEUP_TIMEOUT_MS",
                StringComparison.Ordinal)
            && threadBody.Contains(
                "chiaki_cond_wait_pred(&send_buffer->cond, &send_buffer->mutex", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether giving up still goes through the ack path rather than removing the packet quietly.
    ///
    /// The lock is dropped around it, which is how you can tell it is the same entry point the console's
    /// acknowledgement uses rather than an internal shortcut.
    /// </summary>
    public static bool GivingUpStillAcks(string resendBody)
    {
        ArgumentNullException.ThrowIfNull(resendBody);

        string text = resendBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int limit = text.IndexOf(
            "if(packet->tries >= TAKION_DATA_RESEND_TRIES_MAX)", StringComparison.Ordinal);
        if (limit < 0)
            return false;

        string arm = text[limit..];

        return arm.Contains("chiaki_mutex_unlock(&send_buffer->mutex);", StringComparison.Ordinal)
            && arm.Contains("chiaki_takion_send_buffer_ack(send_buffer, packet->seq_num", StringComparison.Ordinal)
            && arm.Contains("chiaki_mutex_lock(&send_buffer->mutex);", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP476: whether the scan restarts after a give-up rather than stepping back.
    ///
    /// PP475 filed this as PP464's idiom at a second site and PP476 read the ack, which made the answer
    /// bigger than the guard: the ack removes every packet at or before the sequence number, the buffer
    /// is in send order, so acking index i removes the whole prefix 0..i. Stepping back ONE was wrong at
    /// every index, not only at zero - so the repair is a restart and not PP464's unguarded decrement.
    ///
    /// Both halves: the restart is there and the old step back is gone. A predicate looking only for the
    /// restart would pass with both spellings present.
    /// </summary>
    public static bool TheScanRestartsAfterAGiveUp(string resendBody)
    {
        ArgumentNullException.ThrowIfNull(resendBody);

        string text = resendBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains("i = SIZE_MAX;", StringComparison.Ordinal)
            && !text.Contains("if(i > 0)", StringComparison.Ordinal)
            && !text.Contains("i-= 1;", StringComparison.Ordinal);
    }

    /// <summary>
    /// How many packets an ack at this index removes: the whole prefix, because the buffer is in send
    /// order and the ack takes everything at or before the sequence number.
    ///
    /// PP476's finding as arithmetic. It is what makes a one-step back wrong rather than merely short.
    /// </summary>
    public static int AckedByGivingUpAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return index + 1;
    }
}
