namespace ChiakiNg.Protocol;

/// <summary>One read of the stats: what arrived and what did not, over the window just closed.</summary>
/// <param name="Received">Generations' received plus the sequence arm's count.</param>
/// <param name="Lost">Generations' lost plus the span the sequence arm could not account for.</param>
public readonly record struct PacketWindow(ulong Received, ulong Lost)
{
    /// <summary>The denominator the congestion thread divides by, and its zero is a real case.</summary>
    public ulong Total => Received + Lost;
}

/// <summary>
/// PP714: packetstats.c, whose two arms are fed by two different receivers.
///
/// The video path pushes GENERATIONS - a received count and an expected-minus-received, once per
/// frame, because a frame says how many packets it should have had. The audio path pushes SEQUENCE
/// NUMBERS, one per packet, because an audio packet says only which one it is. Congestion control
/// reads the sum and sends it upstream every 200ms.
///
/// THE RESET IS THE PART A PORT GETS WRONG. A get that resets zeroes the generation counters, as
/// anyone would expect, and then does NOT zero the sequence floor: it moves seq_min up to the
/// current seq_max. So the next window measures the span from where this one ended, and a port that
/// reset the floor to zero would report the whole run's span as the next window's loss - a report
/// that grows without bound while the stream is perfectly healthy.
///
/// THE SPAN IS NOT A 16-BIT WRAP, whatever the C's own comment on that line says. Both sequence
/// numbers promote to int before they are subtracted, so a max below its min is a NEGATIVE int
/// widened into a uint64 - about 1.8e19, not the small positive difference sixteen-bit wraparound
/// would give. <see cref="Read"/> reproduces that rather than the arithmetic the comment describes,
/// because the console is being told the first one today.
/// </summary>
public sealed class ManagedPacketStats
{
    private readonly Lock gate = new();

    private ulong genReceived;
    private ulong genLost;
    private ushort seqMin;
    private ushort seqMax;
    private ulong seqReceived;

    /// <summary>A frame's worth: how many of its packets arrived and how many did not.</summary>
    public void PushGeneration(ulong received, ulong lost)
    {
        lock (gate)
        {
            genReceived += received;
            genLost += lost;
        }
    }

    /// <summary>
    /// One packet, identified by its sequence number.
    ///
    /// The count rises for every push and the ceiling only for one that is GREATER under RFC 1982,
    /// so a reordered arrival raises the count without moving the ceiling backwards - and a
    /// sequence number that has wrapped past 65535 IS greater, which is how the ceiling ends up
    /// numerically below the floor.
    /// </summary>
    public void PushSeq(ushort seqNum)
    {
        lock (gate)
        {
            seqReceived++;
            if (SeqNum.Gt(seqNum, seqMax))
                seqMax = seqNum;
        }
    }

    /// <summary>Both arms cleared, with the sequence floor moved up rather than zeroed.</summary>
    public void Reset()
    {
        lock (gate)
        {
            ResetLocked();
        }
    }

    /// <summary>
    /// The two numbers, summed over both arms.
    ///
    /// THE SUBTRACTION IS DONE IN INT AND WIDENED, which is what C does with two uint16_t operands
    /// and is the whole of the wrap behaviour. Written as an explicit unchecked cast rather than
    /// left to the compiler, because the value it produces where max is below min is the finding.
    ///
    /// AND THE BRANCH IS THE C'S. Where more packets arrived than the span is wide, the span itself
    /// is reported as lost rather than nothing. Copied rather than tidied: a port that quietly
    /// disagreed with the C here would be a second protocol that happens to agree in the ordinary
    /// case, and the ordinary case - a window with a span of zero - reports zero either way.
    /// </summary>
    /// <param name="reset">Whether to close the window, which is what the 200ms thread passes.</param>
    public PacketWindow Read(bool reset)
    {
        lock (gate)
        {
            ulong received = genReceived;
            ulong lost = genLost;

            ulong span = unchecked((ulong)(seqMax - seqMin));
            ulong seqLost = seqReceived > span ? span : span - seqReceived;

            received += seqReceived;
            lost += seqLost;

            if (reset)
                ResetLocked();

            return new PacketWindow(received, lost);
        }
    }

    private void ResetLocked()
    {
        genReceived = 0;
        genLost = 0;
        seqMin = seqMax;
        seqReceived = 0;
    }
}
