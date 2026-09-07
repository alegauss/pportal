using System.Net;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>Which of senkusha's callback arms a datagram reached, or that it reached none.</summary>
public enum SenkushaRoute
{
    /// <summary>Nothing acted on it: a base type senkusha has no arm for, or a refused header.</summary>
    Ignored,

    /// <summary>The AV arm, which is where a pong and an MTU response both arrive.</summary>
    ToAv,

    /// <summary>The data arm, which is every protobuf the console sends.</summary>
    ToData,

    /// <summary>The ack arm, which is senkusha's own and the stream connection has no counterpart for.</summary>
    ToDataAck,
}

/// <summary>What one datagram came to.</summary>
/// <param name="Route">Which arm it reached.</param>
/// <param name="Arrival">What that arm answered - the strongest answer where an ack released several.</param>
/// <param name="BaseType">The low nibble the takion routes on, or -1 for an empty datagram.</param>
/// <param name="Verdict">The control message's own verdict, or null where none was read.</param>
/// <param name="Released">How many held sequence numbers an ack released, which is how many arm calls it made.</param>
public readonly record struct SenkushaRouting(
    SenkushaRoute Route,
    SenkushaArrival Arrival,
    int BaseType,
    TakionMessageVerdict? Verdict,
    int Released);

/// <summary>
/// PP805, under PP784: the line between a datagram and one of senkusha's four arms.
///
/// PP791 wrote the arms and PP792 gave the session somewhere to hand the run, and neither wrote
/// this. <see cref="ManagedTakion"/> takes a dispatch delegate and every senkusha caller passed
/// none, so the arms were reached by tests and by nothing else - a run over a real takion would
/// connect, send everything correctly and end every wait in a timeout.
///
/// PP773 IS THE SAME HOLE ONE MODULE OVER and it cost a console trial to find there: a takion that
/// connected and is not dispatching answers nothing, and from this side that reads exactly like a
/// console which never replied.
///
/// FOUR ARMS AND NOT ONE DISPATCH, which is what makes this its own object rather than a method on
/// the arrivals. The stream's arrivals take a whole datagram and route it; senkusha's take a takion
/// EVENT, an ack by sequence number, a protobuf body and an AV packet - four entry points with
/// different arguments, because the C's callback switches on the takion event type before it looks
/// at anything else. So the shape of this walk is decided by that switch.
///
/// THE CONNECT ARM IS NOT HERE. It is not a datagram: the C raises it from the takion's own thread
/// and this port's connect blocks through the handshake, so <see cref="ManagedSenkushaRunHost"/>
/// raises it the moment its connect answers - which is where it already was before this line.
/// </summary>
public sealed class SenkushaRouter
{
    private readonly SenkushaArrivals arrivals;
    private readonly ManagedTakion takion;
    private readonly Func<ulong> clock;

    // PP813's tallies, indexed by the enum's own value so a member added later counts itself.
    private readonly int[] routeTally = new int[Enum.GetValues<SenkushaRoute>().Length];
    private readonly int[] arrivalTally = new int[Enum.GetValues<SenkushaArrival>().Length];
    private readonly Dictionary<int, int> ignoredBaseTypes = [];

    /// <param name="arrivals">The four arms, which decide; nothing here decides for them.</param>
    /// <param name="takion">The transport, for its tag, its ledger and the buffer an ack releases.</param>
    /// <param name="clock">chiaki_time_now_monotonic_us, taken so a round trip can be made to repeat.</param>
    public SenkushaRouter(SenkushaArrivals arrivals, ManagedTakion takion, Func<ulong>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(arrivals);
        ArgumentNullException.ThrowIfNull(takion);

        this.arrivals = arrivals;
        this.takion = takion;
        this.clock = clock ?? ManagedSenkushaRunHost.MonotonicMicroseconds;
    }

    /// <summary>The arms this feeds, for a caller holding the router rather than the host.</summary>
    public SenkushaArrivals Arrivals => arrivals;

    /// <summary>How many datagrams reached this at all.</summary>
    public int Seen { get; private set; }

    /// <summary>And how many an arm acted on, which is the number a silent run has at zero.</summary>
    public int Handled { get; private set; }

    /// <summary>What the last one came to, or null before any arrived.</summary>
    public SenkushaRouting? Last { get; private set; }

    /// <summary>
    /// PP813: the two counters broken out, because together they could not locate a lost pong.
    ///
    /// A live run lost all ten and answered Unknown. <see cref="Handled"/> already proved none was
    /// REFUSED - it counts every arrival that is not Ignored, and it stood at four, which the four
    /// control messages account for on their own. What it cannot say is which of the remaining
    /// three happened: the AV arm was reached and the header refused, the datagram carried a base
    /// type this walk has no arm for, or no pong came back at all.
    ///
    /// The route tally separates the first two and the base types name the second outright. A live
    /// run is the only place the answer exists, so the reading has to survive to the summary line.
    /// </summary>
    /// <returns>The non-zero counts only, so a quiet run's line stays short.</returns>
    public string Census()
    {
        IEnumerable<string> routes = routeTally
            .Select((count, index) => (Name: ((SenkushaRoute)index).ToString(), count))
            .Where(one => one.count > 0)
            .Select(one => $"{one.Name} {one.count}");

        IEnumerable<string> answers = arrivalTally
            .Select((count, index) => (Name: ((SenkushaArrival)index).ToString(), count))
            .Where(one => one.count > 0)
            .Select(one => $"{one.Name} {one.count}");

        IEnumerable<string> bases = ignoredBaseTypes
            .OrderBy(one => one.Key)
            .Select(one => $"{one.Key}x{one.Value}");

        return $"routes [{string.Join(", ", routes)}]"
            + $", arms [{string.Join(", ", answers)}]"
            + (ignoredBaseTypes.Count > 0 ? $", unarmed base types [{string.Join(", ", bases)}]" : string.Empty);
    }

    /// <summary>
    /// One datagram, walked to the arm the C's callback would have reached.
    ///
    /// Handed the receive loop's pooled span, so nothing here keeps it: the protobuf arm parses out
    /// of a copy the push already made, and the AV arm reads a tag and a clock.
    /// </summary>
    public SenkushaRouting Datagram(ReadOnlySpan<byte> datagram)
    {
        Seen++;

        if (datagram.IsEmpty)
            return Record(new SenkushaRouting(SenkushaRoute.Ignored, SenkushaArrival.Ignored, -1, null, 0));

        int baseType = TakionDispatch.BaseTypeOf(datagram);

        // Video AND audio, because the C's AV branch takes both and senkusha's pongs come back on
        // the video one while its MTU responses are asked for as video too. The arm is what tells
        // them apart, by a rule that is opposite in its two states.
        if (baseType is TakionDispatch.Video or TakionDispatch.Audio)
            return Record(Av(datagram, baseType));

        if (baseType != TakionDispatch.Control)
            return Record(new SenkushaRouting(SenkushaRoute.Ignored, SenkushaArrival.Ignored, baseType, null, 0));

        TakionMessageReading message = TakionMessageIntake.Read(datagram, takion.TagLocal, takion.Ledger);

        return Record(message.Verdict switch
        {
            TakionMessageVerdict.Data => Data(datagram, message, baseType),
            TakionMessageVerdict.DataAck => Ack(datagram, message, baseType),
            _ => new SenkushaRouting(
                SenkushaRoute.Ignored, SenkushaArrival.Ignored, baseType, message.Verdict, 0),
        });
    }

    /// <summary>
    /// The ack arm, given what the send buffer released rather than what the ack claimed.
    ///
    /// ONE CALL PER RELEASED NUMBER, which is the C's own loop: a cumulative ack clearing four held
    /// messages raises four DATA_ACK events, and senkusha compares each against the one number it is
    /// waiting for. A port that passed the cumulative number instead would answer correctly only
    /// when nothing else was in flight - which is every gate and not every session.
    ///
    /// Public because the numbers come from <see cref="TakionSendBuffer"/> and a takion has one only
    /// after it has connected, so this is the half a machine with no console can be asked about.
    /// </summary>
    /// <param name="released">The sequence numbers the ack cleared, in the buffer's own order.</param>
    public SenkushaArrival Acked(IReadOnlyList<uint> released)
    {
        ArgumentNullException.ThrowIfNull(released);

        var strongest = SenkushaArrival.Ignored;

        foreach (uint seqNum in released)
        {
            SenkushaArrival one = arrivals.DataAck(seqNum);

            // The one that MATCHED wins, and the rest are acks for other messages - which is the
            // case the arm's own comparison exists for rather than an ordering this may assume.
            if (one != SenkushaArrival.Ignored)
                strongest = one;
        }

        return strongest;
    }

    private SenkushaRouting Av(ReadOnlySpan<byte> datagram, int baseType)
    {
        // Read BEFORE the parse and before the arm's lock, as the C reads it at the top of its own
        // AV handler: a round trip is measured from when the pong arrived, not from when a contended
        // thread got round to noticing it.
        ulong now = clock();

        if (AvPacketV7.Parse(datagram, out _) is not { } header)
            return new SenkushaRouting(SenkushaRoute.Ignored, SenkushaArrival.Ignored, baseType, null, 0);

        SenkushaArrival arrival = arrivals.Av(
            header.IsVideo,
            header.FrameIndex,
            header.UnitIndex,
            datagram.Slice(header.DataOffset, header.DataSize),
            now);

        return new SenkushaRouting(SenkushaRoute.ToAv, arrival, baseType, null, 0);
    }

    private SenkushaRouting Data(ReadOnlySpan<byte> datagram, in TakionMessageReading message, int baseType)
    {
        TakionDataPushReading push = TakionDataPush.Read(
            datagram, message.PayloadOffset, message.PayloadSize, message.Header.ChunkFlags);

        if (push.Verdict != TakionDataPushVerdict.Pushed)
            return new SenkushaRouting(SenkushaRoute.ToData, SenkushaArrival.Ignored, baseType, message.Verdict, 0);

        byte[] payload = push.Entry.Payload;

        // PROTOBUF AND NOT "one of the four", which is the difference from the stream's drain.
        // senkusha_takion_data returns on the first line for any other type - it has no rumble, no
        // pad info and no trigger effects - so a router that accepted the known set would hand the
        // protobuf arm bytes that are not one.
        if (payload.Length < TakionDataDrain.HeaderSize
            || payload[TakionDataDrain.DataTypeOffset] != (byte)TakionDataType.Protobuf)
        {
            return new SenkushaRouting(SenkushaRoute.ToData, SenkushaArrival.Ignored, baseType, message.Verdict, 0);
        }

        SenkushaArrival arrival = arrivals.Protobuf(payload.AsSpan(TakionDataDrain.HeaderSize));

        return new SenkushaRouting(SenkushaRoute.ToData, arrival, baseType, message.Verdict, 0);
    }

    private SenkushaRouting Ack(ReadOnlySpan<byte> datagram, in TakionMessageReading message, int baseType)
    {
        // No buffer is a takion that has not connected, which is a real arrival and not an error:
        // the loop can be reading before the handshake's own answer has been acted on.
        if (takion.SendBuffer is not { } sendBuffer)
            return new SenkushaRouting(SenkushaRoute.ToDataAck, SenkushaArrival.Ignored, baseType, message.Verdict, 0);

        // The header's length check has already compared this against the whole datagram; the bound
        // is repeated because a slice that is out by one throws on the receive thread, and a takion
        // whose loop died is indistinguishable from a console that stopped answering.
        if (message.PayloadOffset < 0 || message.PayloadOffset + message.PayloadSize > datagram.Length)
            return new SenkushaRouting(SenkushaRoute.ToDataAck, SenkushaArrival.Ignored, baseType, message.Verdict, 0);

        TakionAckRead read = TakionDataAck.Read(datagram.Slice(message.PayloadOffset, message.PayloadSize));
        IReadOnlyList<uint> released = TakionDataAck.Apply(sendBuffer, read);

        return new SenkushaRouting(
            SenkushaRoute.ToDataAck, Acked(released), baseType, message.Verdict, released.Count);
    }

    private SenkushaRouting Record(SenkushaRouting routing)
    {
        if (routing.Arrival != SenkushaArrival.Ignored)
            Handled++;

        // PP813: beside the two counters and not instead of them, so nothing reading Seen or
        // Handled changes its answer. An empty datagram's base type is -1 and is not an index.
        routeTally[(int)routing.Route]++;
        arrivalTally[(int)routing.Arrival]++;

        if (routing.Route == SenkushaRoute.Ignored)
        {
            ignoredBaseTypes[routing.BaseType] =
                ignoredBaseTypes.GetValueOrDefault(routing.BaseType) + 1;
        }

        Last = routing;

        return routing;
    }
}

/// <summary>
/// PP805: the knot tied - a takion whose arrivals reach the arms, and a host over the pair.
///
/// THE THREE REFER TO EACH OTHER IN A CIRCLE and the C resolves it the same way: the takion's
/// dispatch reaches the router, the router writes the arrivals, and the host owns the takion, so one
/// of them has to be built before something it needs exists. senkusha.c passes &senkusha to
/// chiaki_takion_connect after the struct is there and before it is filled; here the closure reads a
/// local the line below assigns.
///
/// A FACTORY AND NOT A COMPOSITION ROOT. What this returns still has to be started, stopped and
/// installed on a session, which is a phase and is somebody else's line - this is the smallest thing
/// that makes a run over a real takion reach its own arrivals.
/// </summary>
public static class SenkushaRunWiring
{
    /// <summary>Everything one run needs, wired.</summary>
    /// <param name="tagLocal">chiaki_random_32's answer, which the console echoes.</param>
    /// <param name="peer">Where senkusha's takion goes, which is its own port and not the stream's.</param>
    /// <param name="tags">The ping tags, taken so a run can be made to repeat.</param>
    /// <param name="clock">The monotonic clock, shared by the host's timing and the router's.</param>
    public static (ManagedSenkushaRunHost Host, SenkushaRouter Router) Over(
        uint tagLocal, IPEndPoint peer, Func<uint>? tags = null, Func<ulong>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(peer);

        SenkushaRouter? router = null;

        var takion = new ManagedTakion(tagLocal, datagram => router?.Datagram(datagram));
        var arrivals = new SenkushaArrivals();

        router = new SenkushaRouter(arrivals, takion, clock);

        return (new ManagedSenkushaRunHost(takion, peer, arrivals, tags, clock), router);
    }
}
