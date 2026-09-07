using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>What the ack for one arrival came to.</summary>
public enum TakionAckSendVerdict
{
    /// <summary>No data queue, which is a takion that has not finished its handshake.</summary>
    NoQueue,

    /// <summary>The queue released nothing, so there is nothing to acknowledge. The C sends none either.</summary>
    HeldBack,

    /// <summary>One ack, carrying the last sequence number the queue released.</summary>
    Sent,

    /// <summary>The socket refused it - and the queue has already moved.</summary>
    SendRefused,
}

/// <summary>What one arrival's ack did.</summary>
/// <param name="Verdict">Which of the four.</param>
/// <param name="SeqNum">The number the ack carried, meaningful only where one was sent.</param>
/// <param name="Released">How many entries the queue let go, which is what the one ack covers.</param>
/// <param name="Error">What the socket said; Success on every verdict that did not reach it.</param>
public readonly record struct TakionAckSendReading(
    TakionAckSendVerdict Verdict, uint SeqNum, int Released, ChiakiError Error);

/// <summary>
/// Where a drained data message's acknowledgement goes, which is a takion's own queue and socket.
///
/// PP807: a seam because the routers do not own either. <see cref="StreamArrivals"/> is handed the
/// tag and the ledger as values for the reason its own note gives - the takion is built with the
/// callback that reaches it - and the ack is the third thing in that shape.
/// </summary>
public interface IDataAckSink
{
    /// <summary>The arrival's sequence number in; the ack for whatever the queue released out.</summary>
    TakionAckSendReading AckData(uint seqNum);
}

/// <summary>
/// PP807: the ack this port only ever received, sent - and the QUEUE is what decides that it is.
///
/// PP672 wrote the bytes and nothing ever sent one. Every ack in the tree went the other way: PP494
/// reads the one the console sends and PP805 hands what it released to senkusha's arm, so a port
/// that had modelled both ends of this message had a caller for neither half of its own.
///
/// THE FLAG IS THE QUEUE'S AND NOT THE ARRIVAL'S, which is the whole of what is easy to get wrong
/// here. takion_flush_data_queue sets `ack = true` the moment a PULL succeeds and sends one ack
/// below the loop carrying the last sequence it pulled - so a message the reorder queue held back
/// for a gap ahead of it is not acknowledged, and a flush that pulled four sends one ack and not
/// four. <see cref="TakionDataDrain"/> is where that rule is asserted against the C; this is where
/// it is spent. Acking each arrival instead would look identical on an ordered link and would tell
/// the console a gap had arrived.
///
/// THE QUEUE CARRIES ORDERING HERE AND NOT THE ENTRY, and that is a departure worth stating rather
/// than discovering. The C's queue holds the packet buffer and its drain is what delivers it; this
/// port's routers read the entry and hand it on inline, for the reason PP779 gives, so the slot's
/// payload is a constant nothing reads and the queue is being asked the one question the ack needs
/// - what came free, and in what order.
///
/// WHICH LEAVES ONE ASYMMETRY IN THE PORT AND NOT IN THIS FILE. A message arriving out of order is
/// delivered by the router and NOT acknowledged, so the console retransmits it and the router
/// delivers it twice; the C's queue calls the second copy a duplicate and drops it. The delivery is
/// PP779's departure and the ack is the C's rule, and pairing them this way is the honest half:
/// acking the arrival would make the port quieter and wrong.
/// </summary>
public static class TakionDataAckSend
{
    /// <summary>
    /// What goes into the queue slot, because nothing reads it back.
    ///
    /// The C stores the entry and its drain frees it; here the entry has already been delivered, so
    /// a slot holds a value chosen to be recognisable in a debugger rather than an offset that would
    /// read as though somebody could follow it.
    /// </summary>
    public const long OrderingOnly = -1;

    /// <summary>
    /// One arrival pushed, the queue drained, and one ack for whatever that released.
    /// </summary>
    /// <param name="queue">The takion's data queue, seeded from the remote tag.</param>
    /// <param name="seqNum">The arriving message's sequence number, off its own nine-byte header.</param>
    /// <param name="send">
    /// The send, which is <see cref="ManagedTakion.SendDataAck"/> over a live socket. Taken as a
    /// delegate so the decision above can be asserted without one.
    /// </param>
    public static TakionAckSendReading Arrival(
        ReorderQueue queue, uint seqNum, Func<uint, ChiakiError> send)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(send);

        queue.Push(seqNum, OrderingOnly);

        var acked = false;
        uint last = 0;
        var released = 0;

        // Set on the pull, as the C sets it - and the number kept is the last SUCCESSFUL one, which
        // is what the C's own loop leaves behind when the pull that ends it writes nothing.
        while (queue.Pull() is { } pulled)
        {
            acked = true;
            last = (uint)pulled.SeqNum;
            released++;
        }

        if (!acked)
            return new TakionAckSendReading(TakionAckSendVerdict.HeldBack, 0, 0, ChiakiError.Success);

        ChiakiError sent = send(last);

        return new TakionAckSendReading(
            sent == ChiakiError.Success ? TakionAckSendVerdict.Sent : TakionAckSendVerdict.SendRefused,
            last,
            released,
            sent);
    }
}
