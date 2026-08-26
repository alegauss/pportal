using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the read loop does with the message its buffer currently starts with.</summary>
public enum FrameVerdict
{
    /// <summary>Enough is buffered: dispatch it and consume header plus payload.</summary>
    Dispatch,

    /// <summary>A well-formed message that has not all arrived. Wait for more.</summary>
    Incomplete,

    /// <summary>An announced length this buffer can never hold. The channel fails.</summary>
    Overflow,
}

/// <summary>
/// PP346, under PP294: whether the read loop may frame the message its buffer starts with.
///
/// THE BOUND IS ON THE ANNOUNCED LENGTH ALONE, and that is the whole of what this fixes. ctrl.c
/// used to write both tests on <c>8 + payload_size</c>, a sum in unsigned 32-bit arithmetic: an
/// announced length of 0xFFFFFFF8 or more wrapped it to between zero and seven. The loop only runs
/// while at least eight bytes are buffered, so the "not all arrived" test was false, the overflow
/// test below it was never reached, and the message was dispatched with the length as announced.
///
/// What that reached is not a read. ctrl_message_received decrypts IN PLACE over payload_size
/// bytes, so a header claiming 0xFFFFFFFF started an AES-CTR pass over four gigabytes beginning
/// eight bytes into a 512-byte buffer. The eight-byte header is plaintext - only the payload is
/// encrypted - so the field that decides this is not authenticated.
///
/// The check that existed was the right check. It was defeated by the arithmetic in front of it,
/// which is why the comparison here is against the buffer LESS its header, on a value nothing has
/// been added to.
/// </summary>
public static class CtrlFrameBounds
{
    /// <summary>The ctrl receive buffer, which is what an announced length is measured against.</summary>
    public const int ReceiveBufferSize = 512;

    /// <summary>The largest payload this buffer can ever hold, header included.</summary>
    public const int LargestPayload = ReceiveBufferSize - CtrlFraming.HeaderSize;

    /// <summary>
    /// What the loop does, given what the header announced and how much is buffered.
    /// </summary>
    /// <param name="announced">The payload length off the wire, unmodified.</param>
    /// <param name="buffered">How many bytes are in the receive buffer.</param>
    public static FrameVerdict Judge(uint announced, int buffered)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(buffered);

        // Fewer than a header's worth is not a message yet, which is the loop's own condition.
        if (buffered < CtrlFraming.HeaderSize)
            return FrameVerdict.Incomplete;

        // Before any addition. A length this buffer can never hold ends the channel however much
        // has arrived, because waiting for the rest would wait forever.
        if (announced > LargestPayload)
            return FrameVerdict.Overflow;

        // Safe now: announced is at most 504, so the sum is at most 512 and fits anywhere.
        return buffered < CtrlFraming.HeaderSize + announced
            ? FrameVerdict.Incomplete
            : FrameVerdict.Dispatch;
    }

    /// <summary>
    /// What the old spelling answered, kept so the regression is named rather than described.
    ///
    /// <c>8 + announced</c> in unsigned 32-bit, exactly as the C computed it.
    /// </summary>
    public static FrameVerdict JudgeAsItWas(uint announced, int buffered)
    {
        if (buffered < CtrlFraming.HeaderSize)
            return FrameVerdict.Incomplete;

        uint sum = unchecked((uint)CtrlFraming.HeaderSize + announced);

        if ((ulong)buffered < sum)
            return sum > ReceiveBufferSize ? FrameVerdict.Overflow : FrameVerdict.Incomplete;

        return FrameVerdict.Dispatch;
    }

    /// <summary>Whether an announced length wraps the sum the old spelling used.</summary>
    public static bool WrapsTheOldSum(uint announced)
        => unchecked((uint)CtrlFraming.HeaderSize + announced) < announced;

    /// <summary>The rudp receive buffer, which is EIGHT BYTES LARGER than the one it feeds.</summary>
    public const int RudpReceiveBufferSize = 520;

    /// <summary>
    /// PP347: whether a rudp message may be copied into the ctrl buffer at its current fill.
    ///
    /// The check the C already had is a consistency test - the announced ctrl payload size equals
    /// the message length less its own eight-byte header - which says the message is well formed and
    /// nothing about whether it fits. Two things make that reachable rather than theoretical: the
    /// source buffer is 520 bytes where the destination is 512, so one well-formed message can be
    /// larger than what it is copied into; and the fill is whatever the framing loop left behind,
    /// which raises the offset it lands at.
    /// </summary>
    /// <param name="messageBytes">How much of the rudp message is copied - its length past the offset.</param>
    /// <param name="buffered">What the ctrl buffer already holds.</param>
    public static bool FitsInTheCtrlBuffer(int messageBytes, int buffered)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(messageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(buffered);

        return messageBytes <= ReceiveBufferSize - buffered;
    }
}

/// <summary>
/// PP346: the bound held against ctrl.c, because the whole defect was arithmetic and a shape check
/// is what notices it coming back.
/// </summary>
public static class CtrlFrameBoundsSource
{
    /// <summary>Where the loop lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The thread body, or null.</summary>
    public static string? ThreadBody(string filePath)
        => CFunction.BodyIn(filePath, "static void *ctrl_thread_func");

    /// <summary>
    /// Whether the overflow bound is still on the announced length alone.
    ///
    /// Matched as the comparison rather than as a line: any test of `8 + payload_size` against the
    /// buffer size is the defect returning, whichever way round it is written.
    /// </summary>
    public static bool TheBoundIsStillOnTheLengthAlone(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        bool bounded = threadBody.Contains(
            "payload_size > sizeof(ctrl->recv_buf) - 8", StringComparison.Ordinal);

        // The sum must not be compared against the buffer anywhere: that comparison is the one the
        // wrap defeated, and it is unreachable-by-construction only while the bound above exists.
        bool sumComparedToBuffer = threadBody.Contains(
            "8 + payload_size > sizeof(ctrl->recv_buf)", StringComparison.Ordinal);

        return bounded && !sumComparedToBuffer;
    }

    /// <summary>Whether the bound still comes BEFORE the completeness test it protects.</summary>
    public static bool TheBoundStillComesFirst(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        int bound = threadBody.IndexOf(
            "payload_size > sizeof(ctrl->recv_buf) - 8", StringComparison.Ordinal);
        int complete = threadBody.IndexOf(
            "ctrl->recv_buf_size < 8 + payload_size", StringComparison.Ordinal);

        return bound >= 0 && complete > bound;
    }

    /// <summary>
    /// PP347: every copy into the ctrl buffer, and whether each is guarded by the room left in it.
    ///
    /// Counted rather than located, because there are two arms with the same defect and a third
    /// written the same way would be a third. What is looked for is the destination expression, and
    /// what is required is that the room appears in the condition governing it.
    /// </summary>
    /// <returns>The number of copies with no bound on the destination.</returns>
    public static int UnboundedCopiesIntoTheCtrlBuffer(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        const string destination = "memcpy(ctrl->recv_buf + ctrl->recv_buf_size,";
        const string room = "sizeof(ctrl->recv_buf) - ctrl->recv_buf_size";

        var unbounded = 0;
        for (int at = threadBody.IndexOf(destination, StringComparison.Ordinal);
             at >= 0;
             at = threadBody.IndexOf(destination, at + 1, StringComparison.Ordinal))
        {
            // The condition that governs a copy is above it. Looking back to the previous `if` is
            // enough here: every one of these sits alone inside its own braces.
            int guard = threadBody.LastIndexOf("if(", at, StringComparison.Ordinal);
            if (guard < 0 || !threadBody[guard..at].Contains(room, StringComparison.Ordinal))
                unbounded++;
        }

        return unbounded;
    }
}
