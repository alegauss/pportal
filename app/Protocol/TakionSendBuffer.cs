using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP27: takion's send buffer, in managed code - what it holds and what an ack removes.
///
/// Every reliable takion message sits here until the console acknowledges it, and the resend thread
/// walks the same array on a timer. That thread is NOT this: it is a timer, a condition variable
/// and a socket, and it belongs with the rest of takion's transport. What is here is the structure
/// underneath it, which is where being wrong is silent - a packet dropped from the buffer early is
/// a message the console never gets and nothing ever resends.
///
/// An ack acknowledges everything at or before it
/// -----------------------------------------------
/// Not just the sequence number named. chiaki_takion_send_buffer_ack removes every packet whose
/// seq_num is the acked one OR is before it, which is what makes a lost ack harmless: the next one
/// clears the backlog. "Before" is 32-bit sequence arithmetic and not less-than, so the buffer
/// keeps working across the turnover.
///
/// The survivors are compacted, and the gaps are the hard part
/// -----------------------------------------------------------
/// Packets are not in sequence order - they are in push order - so the acked ones can be anywhere
/// and the compaction has to close several gaps in one pass. The C tracks a shift window and
/// memmoves when a new gap starts. Reproduced by building the survivors in order, which is the same
/// answer by a construction that cannot get the window arithmetic wrong.
/// </summary>
public sealed class TakionSendBuffer
{
    /// <summary>What a takion sets up by default.</summary>
    public const int DefaultSize = 16;

    private readonly List<Packet> packets = [];

    /// <summary>One held message: its sequence number and how big it is.</summary>
    private readonly record struct Packet(uint SeqNum, int Size);

    /// <summary>How many the buffer holds before it refuses.</summary>
    public int Capacity { get; }

    /// <summary></summary>
    public TakionSendBuffer(int capacity = DefaultSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
    }

    /// <summary>How many are held.</summary>
    public int Count => packets.Count;

    /// <summary>The sequence numbers held, in push order. For assertions.</summary>
    public IReadOnlyList<uint> SeqNums => [.. packets.Select(p => p.SeqNum)];

    /// <summary>
    /// Holds one message until it is acknowledged.
    /// </summary>
    /// <returns>
    /// Overflow where the buffer is full, InvalidData for a sequence number already held, Success
    /// otherwise. Both failures free the buffer in the C, which is why they are errors rather than
    /// exceptions: the caller has handed over ownership either way.
    /// </returns>
    public ChiakiError Push(uint seqNum, int size)
    {
        if (packets.Count >= Capacity)
            return ChiakiError.Overflow;

        // Linear, because the array is sixteen long and in push order - there is nothing to bisect.
        foreach (Packet held in packets)
        {
            if (held.SeqNum == seqNum)
                return ChiakiError.InvalidData;
        }

        packets.Add(new Packet(seqNum, size));
        return ChiakiError.Success;
    }

    /// <summary>
    /// Acknowledges everything at or before <paramref name="seqNum"/>.
    /// </summary>
    /// <returns>The sequence numbers removed, in the order they were held.</returns>
    public IReadOnlyList<uint> Ack(uint seqNum)
    {
        var acked = new List<uint>();
        var survivors = new List<Packet>(packets.Count);

        foreach (Packet held in packets)
        {
            // At or before, in sequence terms. SeqNum.Lt is the 32-bit comparison, so a buffer
            // holding 0xfffffff0 when 0x00000005 is acked clears it rather than keeping it forever.
            if (held.SeqNum == seqNum || SeqNum.Lt(held.SeqNum, seqNum))
                acked.Add(held.SeqNum);
            else
                survivors.Add(held);
        }

        packets.Clear();
        packets.AddRange(survivors);
        return acked;
    }
}
