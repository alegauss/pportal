using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP750: the feedback sender's two packets, encrypted, signed and put on the takion's socket.
///
/// PP723 wrote the thread, PP676 the serialisers and TakionFeedbackSends the head - and
/// <see cref="IFeedbackSink"/> stayed the seam only doubles filled, because nothing could encrypt
/// or sign a packet. PP750's two gkcrypt operations are what this needed.
///
/// THE STATE IS FORMATTED HERE AND THE HISTORY IS NOT, which is the split the C has rather than a
/// preference: the takion formats a feedback state itself, and a history packet was already
/// formatted by the flush that queued it.
/// </summary>
/// <param name="takion">The takion whose socket and local cipher these go out under.</param>
/// <param name="v12">
/// Which state layout to format. The C decides on its own version, and this port's takion does not
/// carry one yet - so it is the caller's, and named rather than guessed.
/// </param>
public sealed class TakionFeedbackSink(ManagedTakion takion, bool v12 = true) : IFeedbackSink
{
    /// <summary>How many states this sink has handed to the takion.</summary>
    public int StatesOffered { get; private set; }

    /// <summary>And how many histories.</summary>
    public int HistoriesOffered { get; private set; }

    /// <summary>How many of both the socket took.</summary>
    public int Sent { get; private set; }

    /// <summary>The last error, so a caller can see why one did not go.</summary>
    public ChiakiError? Last { get; private set; }

    /// <inheritdoc/>
    public void SendState(ushort seqNum, FeedbackMotion state)
    {
        StatesOffered++;

        Span<byte> payload = stackalloc byte[FeedbackPayload.StateSize(v12)];
        FeedbackPayload.FormatState(payload, v12, state);

        Put(TakionFeedbackSends.FeedbackStateType, seqNum, payload);
    }

    /// <inheritdoc/>
    public void SendHistory(ushort seqNum, ReadOnlySpan<byte> payload)
        => Put(TakionFeedbackSends.FeedbackHistoryType, seqNum, payload);

    private void Put(byte type, ushort seqNum, ReadOnlySpan<byte> payload)
    {
        if (type == TakionFeedbackSends.FeedbackHistoryType)
            HistoriesOffered++;

        ChiakiError sent = takion.SendFeedback(type, seqNum, payload);
        Last = sent;

        if (sent == ChiakiError.Success)
            Sent++;
    }
}
