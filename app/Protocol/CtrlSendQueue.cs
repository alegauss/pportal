using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One message waiting to go out, as the queue holds it.</summary>
/// <param name="Type">The control message type.</param>
/// <param name="Payload">A copy. The caller's buffer is never retained.</param>
public readonly record struct QueuedCtrlMessage(ushort Type, byte[] Payload);

/// <summary>
/// PP344, under PP294: the queue a caller outside the ctrl thread puts messages on, and the one
/// place the two send paths disagree.
///
/// Nothing outside ctrl.c may write to the socket - the ctrl thread owns it - so every send from
/// elsewhere is enqueued and the notify pipe is poked. goto-bed, the keyboard trio and the
/// managed side all arrive this way.
///
/// THE PAYLOAD IS COPIED, and that is what makes the interface safe: a caller may free its buffer
/// the moment the call returns. The queue owns the copy and frees it with the node.
///
/// THE TWO SEND PATHS DISAGREE ABOUT A NULL PAYLOAD WITH A SIZE. ctrl_message_send, which the ctrl
/// thread calls directly, REFUSES it - `!(payload_size == 0 || payload)` returns INVALID_DATA.
/// chiaki_ctrl_send_message, which everybody else calls, silently normalises it: the branch is on
/// the pointer, so a null payload takes the else and the size becomes zero whatever was passed.
/// The same mistake is an error on one path and a discarded argument on the other, and this
/// reproduces both rather than picking the tidier one.
///
/// FIFO BY WALKING THE LIST. The queue is a singly-linked list appended at the tail, so order is
/// preserved and an enqueue is linear in what is already waiting. That costs nothing at the rate
/// control messages are sent, and is worth stating because a port reaching for a stack would
/// reverse the order of a keyboard sequence.
/// </summary>
public sealed class CtrlSendQueue
{
    private readonly List<QueuedCtrlMessage> waiting = [];
    private readonly Lock guard = new();

    /// <summary>How many are waiting.</summary>
    public int Count
    {
        get
        {
            lock (guard)
                return waiting.Count;
        }
    }

    /// <summary>
    /// Enqueues a message, copying the payload.
    ///
    /// Returns false only where the C returns CHIAKI_ERR_MEMORY, which a managed caller cannot
    /// provoke - the shape is kept so the two implementations answer the same question.
    /// </summary>
    public bool Enqueue(ushort type, ReadOnlySpan<byte> payload, bool payloadIsNull = false)
    {
        // The null branch, reproduced. A caller passing no buffer gets size zero regardless of the
        // size it named - see the note on the class for why this is not tidied.
        byte[] copy = payloadIsNull ? [] : payload.ToArray();

        lock (guard)
            waiting.Add(new QueuedCtrlMessage(type, copy));

        return true;
    }

    /// <summary>Takes everything waiting, oldest first, and empties the queue.</summary>
    public IReadOnlyList<QueuedCtrlMessage> Drain()
    {
        lock (guard)
        {
            QueuedCtrlMessage[] taken = [.. waiting];
            waiting.Clear();
            return taken;
        }
    }

    /// <summary>
    /// Whether the direct send would accept this pair, which is the question the queued one does
    /// not ask.
    /// </summary>
    public static bool TheDirectSendWouldAccept(bool payloadIsNull, int payloadSize)
        => !payloadIsNull || payloadSize == 0;

    /// <summary>What the queued send records for a pair the direct send would refuse.</summary>
    public static int TheQueuedSendRecordsSize(bool payloadIsNull, int payloadSize)
        => payloadIsNull ? 0 : payloadSize;

    /// <summary>goto-bed is a queued send with no payload and nothing else.</summary>
    public static QueuedCtrlMessage GotoBed() => new((ushort)CtrlMessage.GotoBed, []);
}

/// <summary>
/// PP344: the queue held against ctrl.c. None of it is in PP297's capture, which recorded a session
/// nobody sent a queued message on.
/// </summary>
public static class CtrlSendQueueSource
{
    /// <summary>Where the queue lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The queued send's body, or null.</summary>
    public static string? QueuedSendBody(string filePath)
        => CFunction.BodyIn(filePath, "chiaki_ctrl_send_message");

    /// <summary>The direct send's body, or null.</summary>
    public static string? DirectSendBody(string filePath)
        => CFunction.BodyIn(filePath, "static ChiakiErrorCode ctrl_message_send(");

    /// <summary>Whether the payload is still copied rather than retained.</summary>
    public static bool ThePayloadIsStillCopied(string queuedSend)
    {
        ArgumentNullException.ThrowIfNull(queuedSend);

        return queuedSend.Contains("queue->payload = malloc(payload_size);", StringComparison.Ordinal)
            && queuedSend.Contains("memcpy(queue->payload, payload, payload_size);", StringComparison.Ordinal);
    }

    /// <summary>Whether it is still appended at the tail rather than pushed at the head.</summary>
    public static bool ItIsStillAppendedAtTheTail(string queuedSend)
    {
        ArgumentNullException.ThrowIfNull(queuedSend);

        return queuedSend.Contains("while(c->next)", StringComparison.Ordinal)
            && queuedSend.Contains("c->next = queue;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the queued send still branches on the POINTER, which is what makes a null payload
    /// with a size a discarded argument rather than an error.
    /// </summary>
    public static bool ANullPayloadIsStillNormalised(string queuedSend)
    {
        ArgumentNullException.ThrowIfNull(queuedSend);

        return queuedSend.Contains("if(payload)", StringComparison.Ordinal)
            && queuedSend.Contains("queue->payload_size = 0;", StringComparison.Ordinal);
    }

    /// <summary>And whether the direct send still refuses the same pair.</summary>
    public static bool TheDirectSendStillRefusesIt(string directSend)
    {
        ArgumentNullException.ThrowIfNull(directSend);

        return directSend.Contains("if(!(payload_size == 0 || payload))", StringComparison.Ordinal)
            && directSend.Contains("CHIAKI_ERR_INVALID_DATA", StringComparison.Ordinal);
    }
}
