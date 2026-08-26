using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the ctrl thread does when its wait ends.</summary>
public enum CtrlStep
{
    /// <summary>Send everything queued, oldest first, then look again.</summary>
    DrainQueue,

    /// <summary>Forward the PIN the user typed, then start the loop over.</summary>
    SendPin,

    /// <summary>Somebody asked the channel to stop. Leave the loop.</summary>
    Stop,

    /// <summary>Nothing to do: wait again.</summary>
    Wait,

    /// <summary>Read what arrived.</summary>
    Receive,

    /// <summary>The wait itself failed. The channel ends.</summary>
    Fail,
}

/// <summary>Why the ctrl thread's wait returned.</summary>
public enum CtrlWake
{
    /// <summary>The notify pipe was poked - somebody has work for the thread.</summary>
    Cancelled,

    /// <summary>The socket has something to read.</summary>
    Readable,

    /// <summary>The wait broke.</summary>
    Failed,
}

/// <summary>What the ctrl thread can see when it wakes.</summary>
/// <param name="ShouldStop">chiaki_ctrl_stop was called.</param>
/// <param name="QueueHasWork">Something is waiting on the send queue.</param>
/// <param name="PinEntered">A PIN was handed over and not yet forwarded.</param>
public readonly record struct CtrlWakeState(
    bool ShouldStop = false, bool QueueHasWork = false, bool PinEntered = false);

/// <summary>
/// PP349, under PP294: the ctrl thread's loop - what a wake-up means, and how the buffer is
/// consumed.
///
/// CANCELLED IS THE WORK BRANCH, which a reader has to know before anything else makes sense. The
/// select is given UINT64_MAX and returns CHIAKI_ERR_CANCELED when the notify pipe is poked - and
/// that branch is where the send queue is drained and a typed PIN goes out. A port treating
/// CANCELED as a failure would send nothing anybody queued; one treating it as a timeout would
/// spin. It is the same reading PP336 needed for the stream connection: cancelled is what asked-for
/// looks like from inside a wait.
///
/// THE THREE CONDITIONS ARE RE-READ RATHER THAN TRUSTED. One pipe carries a stop, a queued message
/// and a PIN (PP350), so what woke the thread is not what it does - it looks at all three, in
/// order, every time.
///
/// THE PIN BRANCH STARTS THE LOOP OVER. It ends in `continue` rather than falling through to the
/// stop check, so a stop requested in the same wake-up as a PIN is not noticed until the next turn.
/// One extra pass, not a hang - and the kind of ordering a rewrite changes without noticing.
/// </summary>
public static class CtrlLoop
{
    /// <summary>
    /// What the thread does, given why it woke and what it can see.
    /// </summary>
    public static CtrlStep Next(CtrlWake wake, CtrlWakeState state) => wake switch
    {
        CtrlWake.Failed => CtrlStep.Fail,
        CtrlWake.Readable => CtrlStep.Receive,

        // The work branch, in the C's own order: queue, then PIN, then stop.
        _ when state.QueueHasWork => CtrlStep.DrainQueue,
        _ when state.PinEntered => CtrlStep.SendPin,
        _ when state.ShouldStop => CtrlStep.Stop,

        // Poked with nothing to do, which a spurious wake-up looks like.
        _ => CtrlStep.Wait,
    };

    /// <summary>
    /// Whether the thread even waits, or goes straight to work.
    ///
    /// The C tests the same three conditions BEFORE the select and skips it where any holds -
    /// otherwise a message queued while the thread was busy would sit until the next poke.
    /// </summary>
    public static bool WaitsFirst(CtrlWakeState state)
        => !state.ShouldStop && !state.QueueHasWork && !state.PinEntered;

    /// <summary>
    /// Whether a stop is acted on in this wake-up, or deferred to the next.
    ///
    /// A PIN in the same wake-up defers it, because that branch continues rather than falling
    /// through. A queued message does not: the drain runs to empty and then the stop is read.
    /// </summary>
    public static bool StopIsActedOnNow(CtrlWakeState state)
        => state.ShouldStop && !state.PinEntered;
}

/// <summary>
/// PP349: the receive buffer's framing - what one message consumes and what is kept.
///
/// The inner loop frames every whole message the buffer holds, then compacts what is left to the
/// front. Two messages can arrive in one read and a third can be split across two, so the
/// compaction is what makes the stream a sequence of messages rather than of packets.
/// </summary>
public sealed class CtrlReceiveBuffer
{
    private readonly byte[] buffer = new byte[CtrlFrameBounds.ReceiveBufferSize];

    /// <summary>How much is buffered.</summary>
    public int Filled { get; private set; }

    /// <summary>What is buffered, for a caller framing it.</summary>
    public ReadOnlySpan<byte> Buffered => buffer.AsSpan(0, Filled);

    /// <summary>
    /// Appends what a read produced, or returns false where it does not fit.
    ///
    /// PP347 is why this answers rather than throws: the rudp arms silently dropped a message that
    /// did not fit, and the port reproduces that rather than inventing an error path.
    /// </summary>
    public bool Append(ReadOnlySpan<byte> received)
    {
        if (!CtrlFrameBounds.FitsInTheCtrlBuffer(received.Length, Filled))
            return false;

        received.CopyTo(buffer.AsSpan(Filled));
        Filled += received.Length;
        return true;
    }

    /// <summary>
    /// Takes the message at the front, or null where there is not a whole one.
    ///
    /// Consumes header plus payload and moves the remainder to the front, which is the memmove the
    /// C does only when something is left.
    /// </summary>
    public (ushort Type, byte[] Payload)? TakeMessage()
    {
        if (Filled < CtrlFraming.HeaderSize)
            return null;

        uint announced = CtrlFraming.PayloadSizeOf(Buffered);

        if (CtrlFrameBounds.Judge(announced, Filled) != FrameVerdict.Dispatch)
            return null;

        var size = (int)announced;
        ushort type = CtrlFraming.TypeOf(Buffered);
        byte[] payload = buffer.AsSpan(CtrlFraming.HeaderSize, size).ToArray();

        int consumed = CtrlFraming.HeaderSize + size;
        Filled -= consumed;

        if (Filled > 0)
            buffer.AsSpan(consumed, Filled).CopyTo(buffer);

        return (type, payload);
    }

    /// <summary>
    /// Whether the buffer is holding a message it can never complete, which ends the channel.
    ///
    /// PP346: the announced length is bounded on its own, so this is a question about the header at
    /// the front and not about the arithmetic.
    /// </summary>
    public bool HoldsAnImpossibleMessage()
        => Filled >= CtrlFraming.HeaderSize
            && CtrlFrameBounds.Judge(CtrlFraming.PayloadSizeOf(Buffered), Filled) == FrameVerdict.Overflow;
}

/// <summary>
/// PP349: the loop held against ctrl.c, for the orderings no capture can show.
/// </summary>
public static class CtrlLoopSource
{
    /// <summary>Where the loop lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The thread body, or null.</summary>
    public static string? ThreadBody(string filePath)
        => CFunction.BodyIn(filePath, "static void *ctrl_thread_func");

    /// <summary>
    /// Whether a cancelled wait is still where the work happens.
    ///
    /// Both halves: the branch tests CANCELED, and the drain is inside it. A port that moved either
    /// would still compile and would send nothing anybody queued.
    /// </summary>
    public static bool CancelledIsStillTheWorkBranch(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        int branch = threadBody.IndexOf("if(err == CHIAKI_ERR_CANCELED)", StringComparison.Ordinal);
        if (branch < 0)
            return false;

        int drain = threadBody.IndexOf("while(ctrl->msg_queue)", branch, StringComparison.Ordinal);
        int pin = threadBody.IndexOf("if(ctrl->login_pin_entered)", branch, StringComparison.Ordinal);

        return drain > branch && pin > drain;
    }

    /// <summary>
    /// Whether the three conditions are still tested before the wait as well as after it.
    ///
    /// Without the test in front, a message queued while the thread was framing would sit until
    /// something else poked the pipe.
    /// </summary>
    public static bool TheConditionsAreStillTestedBeforeTheWait(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        return threadBody.Contains(
            "if(ctrl->should_stop || ctrl->msg_queue || ctrl->login_pin_entered)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the PIN branch still starts the loop over rather than falling through.</summary>
    public static bool ThePinBranchStillContinues(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        int pin = threadBody.IndexOf("if(ctrl->login_pin_entered)", StringComparison.Ordinal);
        if (pin < 0)
            return false;

        int stop = threadBody.IndexOf("if(ctrl->should_stop)", pin, StringComparison.Ordinal);
        int carryOn = threadBody.IndexOf("continue;", pin, StringComparison.Ordinal);

        return carryOn > pin && stop > carryOn;
    }

    /// <summary>Whether each queued send still happens with the lock released.</summary>
    public static bool TheDrainStillReleasesTheLock(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        int drain = threadBody.IndexOf("while(ctrl->msg_queue)", StringComparison.Ordinal);
        if (drain < 0)
            return false;

        int unlock = threadBody.IndexOf(
            "chiaki_mutex_unlock(&ctrl->notif_mutex);", drain, StringComparison.Ordinal);
        int send = threadBody.IndexOf(
            "ctrl_message_send(ctrl, msg->type", drain, StringComparison.Ordinal);
        int relock = threadBody.IndexOf(
            "chiaki_mutex_lock(&ctrl->notif_mutex);", send, StringComparison.Ordinal);

        return unlock > drain && send > unlock && relock > send;
    }
}
