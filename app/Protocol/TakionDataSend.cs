using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which of the two data sends is being modelled.</summary>
public enum TakionSendVariant
{
    /// <summary>chiaki_takion_send_message_data - writes a zero type byte, payload at nine.</summary>
    First,

    /// <summary>chiaki_takion_send_message_data_cont - no type byte, payload at eight.</summary>
    Continuation,
}

/// <summary>How far a send got before it stopped.</summary>
public enum TakionSendStage
{
    /// <summary>The key position could not be taken. Nothing has been spent.</summary>
    KeyPositionRefused,

    /// <summary>The packet could not be allocated. The key position is already spent.</summary>
    AllocationFailed,

    /// <summary>The sequence number's mutex would not lock. The packet leaks here.</summary>
    SequenceLockFailed,

    /// <summary>The socket refused it. Both numbers spent, packet freed, nothing will resend.</summary>
    SendFailed,

    /// <summary>Sent, but the send buffer would not hold it. Freed, and never retried.</summary>
    SentButNotHeld,

    /// <summary>Sent and held for resend.</summary>
    SentAndHeld,
}

/// <summary>What one send did and what it cost.</summary>
/// <param name="Stage">Where it stopped.</param>
/// <param name="Error">What the caller sees. Success for both stages that reached the wire.</param>
/// <param name="KeyPositionSpent">Whether the key ledger moved.</param>
/// <param name="SequenceNumberSpent">Whether a sequence number was consumed.</param>
/// <param name="SeqNum">The number consumed, meaningful only when one was.</param>
/// <param name="PacketLeaked">Whether the packet buffer was neither freed nor handed on.</param>
public readonly record struct TakionSendOutcome(
    TakionSendStage Stage,
    ChiakiError Error,
    bool KeyPositionSpent,
    bool SequenceNumberSpent,
    uint SeqNum,
    bool PacketLeaked);

/// <summary>
/// PP496, under PP27: the two data sends, and what each of their failures has already spent.
///
/// PP495 modelled the key position a send takes; this is the send that takes it. Translating the
/// body is easy and beside the point - the content is the ORDER, because every failure here is a
/// failure AFTER something irreversible.
///
/// A FAILED SEND LEAVES A HOLE NOTHING FILLS. The key position is taken before the packet is even
/// allocated, and the sequence number is taken after the header is written but before the payload
/// is. Only then does the packet go out. If the socket refuses it, the buffer is freed and both
/// numbers stay spent - and because the push into the send buffer comes AFTER the send, the packet
/// was never held, so the resend loop has nothing to retry. The console is left waiting on a
/// sequence number that exists nowhere.
///
/// A FULL SEND BUFFER IS THE SAME HOLE, QUIETER. Both callers ignore what the push returns, and the
/// push frees the packet on every failure of its own. So an overflow means the message went out,
/// was immediately forgotten, and will never be retried if it is lost. The only report is a log
/// line in another file, and the caller is told the send succeeded.
///
/// THE TWO VARIANTS DIFFER BY ONE BYTE AND IT IS THE BYTE THAT MEANS TYPE. The first writes zero at
/// offset eight and puts the payload at nine; the continuation writes no type byte and puts the
/// payload at eight. Line for line they are otherwise the same function, which is exactly how the
/// difference gets lost in a port.
///
/// AND ONE LEAK THAT IS NAMED RATHER THAN FIXED. A failure to lock the sequence number's mutex
/// returns without freeing the packet - the shape PP474 and PP491 repaired elsewhere. It is
/// unreachable: the mutex is an ordinary one and locking it does not fail. Modelled so the next
/// reader does not have to re-derive that.
/// </summary>
public static class TakionDataSend
{
    /// <summary>The type byte plus the sixteen-byte message header, before the data header.</summary>
    public const int HeaderBytes = 1 + 0x10;

    /// <summary>Where the payload starts inside the message, for each variant.</summary>
    public static int PayloadOffsetFor(TakionSendVariant variant) => variant switch
    {
        TakionSendVariant.First => 9,
        TakionSendVariant.Continuation => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    /// <summary>Whether this variant writes the data-type byte at all.</summary>
    public static bool WritesTypeByte(TakionSendVariant variant)
        => variant == TakionSendVariant.First;

    /// <summary>The whole packet a send of <paramref name="payloadSize"/> bytes puts on the wire.</summary>
    public static int PacketSize(TakionSendVariant variant, int payloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadSize);
        return HeaderBytes + PayloadOffsetFor(variant) + payloadSize;
    }

    /// <summary>
    /// Runs one send against a scripted world, and reports what it spent.
    /// </summary>
    /// <param name="sendBuffer">Where a sent packet is held for resend.</param>
    /// <param name="nextSeqNum">The sequence counter, advanced when one is taken.</param>
    /// <param name="payloadSize">The caller's payload.</param>
    /// <param name="variant">Which of the two.</param>
    /// <param name="keyPositionAvailable">Whether the ledger granted a position.</param>
    /// <param name="allocationSucceeds">Whether the packet buffer could be allocated.</param>
    /// <param name="sequenceLockSucceeds">
    /// Whether the sequence mutex locked. False is the unreachable leak, kept so a test can ask.
    /// </param>
    /// <param name="sendSucceeds">Whether the socket took it.</param>
    public static TakionSendOutcome Send(
        TakionSendBuffer sendBuffer,
        ref uint nextSeqNum,
        int payloadSize,
        TakionSendVariant variant = TakionSendVariant.First,
        bool keyPositionAvailable = true,
        bool allocationSucceeds = true,
        bool sequenceLockSucceeds = true,
        bool sendSucceeds = true)
    {
        ArgumentNullException.ThrowIfNull(sendBuffer);

        // First, and before anything else exists to undo.
        if (!keyPositionAvailable)
            return new TakionSendOutcome(
                TakionSendStage.KeyPositionRefused, ChiakiError.Overflow, false, false, 0, false);

        if (!allocationSucceeds)
            return new TakionSendOutcome(
                TakionSendStage.AllocationFailed, ChiakiError.Memory, true, false, 0, false);

        if (!sequenceLockSucceeds)
            return new TakionSendOutcome(
                TakionSendStage.SequenceLockFailed, ChiakiError.Unknown, true, false, 0, PacketLeaked: true);

        uint seqNum = nextSeqNum++;

        if (!sendSucceeds)
            return new TakionSendOutcome(
                TakionSendStage.SendFailed, ChiakiError.Network, true, true, seqNum, false);

        ChiakiError held = sendBuffer.Push(seqNum, PacketSize(variant, payloadSize));

        // The caller is told the send succeeded either way: the C ignores what the push returns.
        return held == ChiakiError.Success
            ? new TakionSendOutcome(TakionSendStage.SentAndHeld, ChiakiError.Success, true, true, seqNum, false)
            : new TakionSendOutcome(TakionSendStage.SentButNotHeld, ChiakiError.Success, true, true, seqNum, false);
    }

    /// <summary>
    /// The stages that leave a sequence number the console will wait for and nothing will resend.
    ///
    /// Two of the six, and they are the two that matter: one reports an error and one does not.
    /// </summary>
    public static IReadOnlyList<TakionSendStage> LeaveAnUnresendableGap { get; } =
        [TakionSendStage.SendFailed, TakionSendStage.SentButNotHeld];

    /// <summary>Whether this outcome left such a gap.</summary>
    public static bool LeavesAGap(TakionSendOutcome outcome)
        => LeaveAnUnresendableGap.Contains(outcome.Stage);
}

/// <summary>
/// PP496: the C's own spelling of the order, because the order is the whole claim.
/// </summary>
public static class TakionDataSendSource
{
    /// <summary>takion.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(TakionPostpone.RelativePath);

    /// <summary>takionsendbuffer.c, where the push frees what it refuses.</summary>
    public const string SendBufferRelativePath = @"lib\src\takionsendbuffer.c";

    /// <summary>The send buffer's source, or null outside a checkout.</summary>
    public static string? LocateSendBuffer() => SanitizerSource.LocateRelative(SendBufferRelativePath);

    /// <summary>The function for one variant.</summary>
    public static string? BodyOf(string source, TakionSendVariant variant)
        => CFunction.Body(source, variant == TakionSendVariant.First
            ? "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_send_message_data"
            : "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_send_message_data_cont");

    /// <summary>
    /// Whether the four steps still run in the order that decides what a failure costs.
    ///
    /// Position, allocate, sequence, send, hold. If the push ever moved above the send, a failed
    /// send would leave something to retry and this model would be describing a different bug.
    /// </summary>
    public static bool TheStepsAreStillInThisOrder(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string text = body.Replace("\r\n", "\n", StringComparison.Ordinal);

        int position = text.IndexOf("chiaki_takion_crypt_advance_key_pos(", StringComparison.Ordinal);
        int allocate = text.IndexOf("malloc(packet_size)", StringComparison.Ordinal);
        int sequence = text.IndexOf("takion->seq_num_local++", StringComparison.Ordinal);
        int send = text.IndexOf("chiaki_takion_send(takion, packet_buf", StringComparison.Ordinal);
        int hold = text.IndexOf("chiaki_takion_send_buffer_push(", StringComparison.Ordinal);

        return position >= 0
            && allocate > position
            && sequence > allocate
            && send > sequence
            && hold > send;
    }

    /// <summary>
    /// Whether the push's result is still discarded, so a full buffer reads as a successful send.
    ///
    /// Spelled as the statement standing alone - no assignment, no test. An `err =` in front of it
    /// would be the repair, and would change what the caller is told.
    /// </summary>
    public static bool ThePushResultIsDiscarded(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string text = body.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
            "\n\tchiaki_takion_send_buffer_push(&takion->send_buffer, seq_num_val, packet_buf, packet_size);",
            StringComparison.Ordinal);
    }

    /// <summary>Whether a failed send still frees the packet, so only the numbers are lost.</summary>
    public static bool AFailedSendFreesThePacket(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string text = body.Replace("\r\n", "\n", StringComparison.Ordinal);

        int send = text.IndexOf("chiaki_takion_send(takion, packet_buf", StringComparison.Ordinal);
        if (send < 0)
            return false;

        int hold = text.IndexOf("chiaki_takion_send_buffer_push(", send, StringComparison.Ordinal);

        return hold > send && text[send..hold].Contains("free(packet_buf);", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the sequence lock's failure still returns without freeing.
    ///
    /// Asserted as it IS. Unreachable - the mutex is an ordinary one - so repairing it would be a
    /// change to a path nothing can take; what this buys is that the model's note stays honest.
    /// </summary>
    public static bool TheSequenceLockFailureStillLeaks(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string text = body.Replace("\r\n", "\n", StringComparison.Ordinal);

        int locked = text.IndexOf(
            "chiaki_mutex_lock(&takion->seq_num_local_mutex)", StringComparison.Ordinal);
        if (locked < 0)
            return false;

        int taken = text.IndexOf("takion->seq_num_local++", locked, StringComparison.Ordinal);

        return taken > locked && !text[locked..taken].Contains("free(packet_buf);", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the two variants still differ by the type byte and nothing else structural.
    ///
    /// The first writes a zero at offset eight and copies from nine; the continuation copies from
    /// eight and writes no such byte. Both halves, because "the continuation writes no type" is
    /// only meaningful beside "the first one does".
    /// </summary>
    public static bool TheVariantsDifferByTheTypeByte(string first, string continuation)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(continuation);

        return first.Contains("*(msg_payload + 8) = 0;", StringComparison.Ordinal)
            && first.Contains("memcpy(msg_payload + 9, buf, buf_size);", StringComparison.Ordinal)
            && !continuation.Contains("*(msg_payload + 8) = 0;", StringComparison.Ordinal)
            && continuation.Contains("memcpy(msg_payload + 8, buf, buf_size);", StringComparison.Ordinal);
    }

    /// <summary>Whether the push still frees the packet on every failure of its own.</summary>
    public static bool ThePushFreesWhatItRefuses(string sendBufferSource)
    {
        ArgumentNullException.ThrowIfNull(sendBufferSource);

        if (CFunction.Body(
                sendBufferSource,
                "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_send_buffer_push") is not { } body)
        {
            return false;
        }

        string text = body.Replace("\r\n", "\n", StringComparison.Ordinal);

        int beach = text.IndexOf("beach:", StringComparison.Ordinal);

        return beach >= 0
            && text[beach..].Contains("if(err != CHIAKI_ERR_SUCCESS)", StringComparison.Ordinal)
            && text[beach..].Contains("free(buf);", StringComparison.Ordinal);
    }
}
