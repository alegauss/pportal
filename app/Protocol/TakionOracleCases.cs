namespace ChiakiNg.Protocol;

/// <summary>What one step does to a reorder queue.</summary>
public enum QueueOpKind
{
    /// <summary>chiaki_reorder_queue_push, at <see cref="QueueOp.Value"/>.</summary>
    Push,

    /// <summary>chiaki_reorder_queue_pull, whose answer is recorded.</summary>
    Pull,

    /// <summary>chiaki_reorder_queue_drop, at the OFFSET in <see cref="QueueOp.Value"/>.</summary>
    Drop,
}

/// <summary>One operation, with the sequence number or the offset the kind decides it is.</summary>
/// <param name="Kind">Which of the three.</param>
/// <param name="Value">A sequence number for a push, an offset for a drop, unused for a pull.</param>
/// <param name="Payload">What a push carries. An index rather than an object, as the seam has it.</param>
public readonly record struct QueueOp(QueueOpKind Kind, ulong Value, long Payload);

/// <summary>A queue driven from one starting state through one list of operations.</summary>
/// <param name="Name">What the script is for, so a failure names a shape rather than a seed.</param>
/// <param name="SizeExp">The window is 2^SizeExp elements.</param>
/// <param name="Start">The sequence number the window opens at.</param>
/// <param name="Width">Which of the C's two instantiations.</param>
/// <param name="Strategy">Which end an overflow drops from.</param>
/// <param name="Ops">The operations, in order and already materialised - see the class remarks.</param>
public sealed record QueueScript(
    string Name,
    int SizeExp,
    uint Start,
    ReorderWidth Width,
    ReorderDropStrategy Strategy,
    IReadOnlyList<QueueOp> Ops);

/// <summary>One thing done to a send buffer.</summary>
/// <param name="IsPush">A push, or an ack.</param>
/// <param name="SeqNum">The sequence number pushed or acknowledged.</param>
public readonly record struct BufferStep(bool IsPush, uint SeqNum);

/// <summary>A send buffer of one capacity, driven through one script.</summary>
/// <param name="Name">What the script demonstrates.</param>
/// <param name="Capacity">How many packets may be outstanding before a push is refused.</param>
/// <param name="Steps">The pushes and acks, in order.</param>
public sealed record BufferScript(string Name, int Capacity, IReadOnlyList<BufferStep> Steps);

/// <summary>
/// PP785: what takion's oracles are asked, in ONE list the recorder and the comparison both walk.
///
/// The cases used to live in the differentials that ask them, which was right while a live library
/// was the only consumer. A recording is a second consumer - see
/// <see cref="TakionOracleRecording"/> - and two lists would drift the way PP33 found the json ones
/// would: the recording would answer for cases the comparison no longer makes, and the comparison
/// would ask cases the recording has no answer for, and neither would fail.
///
/// THE SCRIPTS ARE MATERIALISED, not generated at each call. The queue's operations come out of a
/// tiny generator with fixed seeds, exactly as PP23's differential drives it, and the list is built
/// once - so the recorder and the comparison run identical operations even though one of them ran
/// months earlier, on a build that still had the C.
///
/// THE INPUTS ARE A SUBSET OF WHAT THE LIVE DIFFERENTIALS SWEEP, deliberately. PP517 compares 1040
/// MAC packets and PP668 compares 240 AV headers against a library that is present; this records the
/// bounds and the shapes either side of them, because a recording is a file in the tree and a file
/// that nobody can read is not a second opinion. What the live sweeps cover and this does not is
/// covered while the C is here, which is the whole period in which a wider recording could be taken.
/// </summary>
public static class TakionOracleCases
{
    /// <summary>
    /// The generator PP23's differential uses, written out so a failure reproduces everywhere.
    ///
    /// Copied rather than shared, and that is the point: this list is materialised into the
    /// recording's own inputs, so the day somebody improves the differential's generator the
    /// recorded cases do not silently become different cases.
    /// </summary>
    private sealed class Lcg(uint seed)
    {
        private uint state = seed == 0 ? 1u : seed;

        public uint Next()
        {
            state = (1664525u * state) + 1013904223u;
            return state;
        }

        public int Next(int bound) => (int)(Next() % (uint)bound);
    }

    /// <summary>
    /// The reorder queue's scripts: pushes, pulls and drops in a mix nobody chose by hand.
    ///
    /// Sequence numbers cluster near the window so overflows and duplicates actually happen, and one
    /// script is the thirty-two-bit instantiation - takion's DATA queue - because the width decides
    /// the wrap and the wrap is the behaviour.
    /// </summary>
    public static IReadOnlyList<QueueScript> Queues { get; } =
    [
        Script("a small window dropping from the end", 1u, 3, ReorderWidth.Sixteen, ReorderDropStrategy.End, 0x1000),
        Script("a small window dropping from the start", 2u, 3, ReorderWidth.Sixteen, ReorderDropStrategy.Begin, 0x1000),
        Script("a wider window dropping from the end", 3u, 4, ReorderWidth.Sixteen, ReorderDropStrategy.End, 0x1000),
        Script("a window of one, where every arrival overflows", 7u, 0, ReorderWidth.Sixteen, ReorderDropStrategy.End, 0x1000),
        Script("the data queue, thirty-two bits wide", 11u, 3, ReorderWidth.ThirtyTwo, ReorderDropStrategy.Begin, 0xfffffff0),
    ];

    /// <summary>How many operations each script runs.</summary>
    public const int QueueSteps = 120;

    private static QueueScript Script(
        string name, uint seed, int sizeExp, ReorderWidth width, ReorderDropStrategy strategy, uint start)
    {
        var rng = new Lcg(seed);
        var ops = new List<QueueOp>();
        int window = 1 << sizeExp;
        uint cursor = start;

        for (int step = 0; step < QueueSteps; step++)
        {
            int op = rng.Next(10);

            if (op < 6)
            {
                // Mostly near the cursor, occasionally far - including backwards, which is the
                // "older than the window" occasion.
                int spread = rng.Next(3) switch
                {
                    0 => rng.Next(window + 2),
                    1 => -rng.Next(window + 2),
                    _ => rng.Next((2 * window) + 4) - window - 2,
                };

                ulong seq = unchecked(width == ReorderWidth.Sixteen
                    ? (ushort)(cursor + (uint)spread)
                    : cursor + (uint)spread);

                ops.Add(new QueueOp(QueueOpKind.Push, seq, (step << 8) | 0x5a));
            }
            else if (op < 8)
            {
                ops.Add(new QueueOp(QueueOpKind.Pull, 0, 0));
            }
            else if (op < 9)
            {
                ops.Add(new QueueOp(QueueOpKind.Drop, (ulong)rng.Next(window + 1), 0));
            }
            else
            {
                // Not an operation on the queue - it moves where the next pushes land, which is how
                // the window is left behind and re-approached.
                cursor = unchecked(width == ReorderWidth.Sixteen
                    ? (ushort)(cursor + (uint)rng.Next(window + 1))
                    : cursor + (uint)rng.Next(window + 1));
            }
        }

        return new QueueScript(name, sizeExp, start, width, strategy, ops);
    }

    /// <summary>How many bytes every recorded push claims, which the count does not depend on.</summary>
    public const int SendBufferPacketSize = 32;

    /// <summary>
    /// The send buffer's scripts, which are PP518's own five plus the long one.
    ///
    /// The wrap is the case they exist for: a buffer holding 0xfffffff0 and acked at 5 has to
    /// release it, because "older" near the wrap is not "less than".
    /// </summary>
    public static IReadOnlyList<BufferScript> SendBuffers { get; } =
    [
        new("an ack releases the prefix", 16,
            [Push(10), Push(11), Push(12), Push(13), Ack(12), Push(14)]),

        new("the wrap releases what integer order would keep", 16,
            [
                Push(0xfffffff0), Push(0xfffffffe), Push(0xffffffff),
                Push(0x00000000), Push(0x00000005),
                Ack(0x00000005),
                Push(0x00000006),
            ]),

        new("an ack older than everything releases nothing", 16,
            [Push(100), Push(101), Push(102), Ack(50), Ack(99)]),

        new("a full buffer refuses a push", 4,
            [Push(1), Push(2), Push(3), Push(4), Push(5), Ack(2), Push(6), Push(7), Push(8)]),

        new("a duplicate sequence number is refused", 8,
            [Push(7), Push(8), Push(7), Push(9)]),

        new("a long alternating script across the wrap", 16, LongScript()),
    ];

    private static BufferStep Push(uint seqNum) => new(true, seqNum);

    private static BufferStep Ack(uint seqNum) => new(false, seqNum);

    private static IReadOnlyList<BufferStep> LongScript()
    {
        var rng = new Lcg(518u);
        var steps = new List<BufferStep>();
        uint next = 0xffffff80;

        for (int i = 0; i < 200; i++)
        {
            if (rng.Next(3) == 0 && steps.Count > 0)
                steps.Add(Ack(unchecked(next - (uint)(rng.Next(5) + 1))));
            else
                steps.Add(Push(next++));
        }

        return steps;
    }

    /// <summary>
    /// The lengths the MAC gate is asked about, either side of every bound the layouts have.
    ///
    /// Zero and one are below any header; eleven and twelve straddle the smallest layout's end;
    /// twenty-five, twenty-six, thirty-one and thirty-two straddle the largest; sixty-four is well
    /// past all of them.
    /// </summary>
    public static IReadOnlyList<int> MacSizes { get; } = [0, 1, 11, 12, 13, 16, 17, 25, 26, 31, 32, 33, 64];

    /// <summary>Every base type against every length above, which is what the recording holds.</summary>
    public static IReadOnlyList<(int BaseType, int Size)> Mac { get; } =
        [.. Enumerable.Range(0, TakionDispatch.BaseTypeMask + 1)
            .SelectMany(baseType => MacSizes.Select(size => (baseType, size)))];

    /// <summary>A packet whose every byte is distinct, so a wrong offset shows as wrong bytes.</summary>
    public static byte[] MacPacket(int baseType, int size)
    {
        var packet = new byte[size];
        if (size > 0)
            packet[0] = (byte)baseType;

        for (int i = 1; i < size; i++)
            packet[i] = (byte)((i * 7) + 0x11);

        return packet;
    }

    /// <summary>The four lead bytes: audio and video, each with and without the nalu-info flag.</summary>
    public static IReadOnlyList<byte> AvLeads { get; } = [0x03, 0x13, 0x02, 0x12];

    /// <summary>
    /// The lengths both AV parsers are asked about.
    ///
    /// Dense across the bounds - PP499's twenty and twenty-one used to get through the C and make
    /// av_size go below zero, and twenty-three is the first that parses - and sparse past them,
    /// where the answer only shifts the payload's size.
    /// </summary>
    public static IReadOnlyList<int> AvSizes { get; } =
        [1, 8, 16, 17, 18, 19, 20, 21, 22, 23, 24, 26, 28, 32, 40, 60];

    /// <summary>Every lead against every length, for each of the two parsers.</summary>
    public static IReadOnlyList<(byte Lead, int Size)> Av { get; } =
        [.. AvLeads.SelectMany(lead => AvSizes.Select(size => (lead, size)))];

    /// <summary>A datagram of a size, led by a type byte, with distinguishable bytes after it.</summary>
    public static byte[] AvPacketBytes(byte lead, int size)
    {
        var packet = new byte[size];
        if (size > 0)
            packet[0] = lead;

        for (int i = 1; i < size; i++)
            packet[i] = (byte)(i + 0x10);

        return packet;
    }
}
