using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What one arrival at senkusha's callback came to.</summary>
public enum SenkushaArrival
{
    /// <summary>Nothing: the event or the message reached no arm in this state.</summary>
    Ignored,

    /// <summary>The takion came up, in the one state that is waiting for it.</summary>
    Connected,

    /// <summary>It went away, in that same state - which sets the flag nobody reads.</summary>
    Disconnected,

    /// <summary>The protocol version was acknowledged.</summary>
    ProtocolAcked,

    /// <summary>The console's bang.</summary>
    Banged,

    /// <summary>The ack of the data message this state is waiting on, by sequence number.</summary>
    DataAcked,

    /// <summary>The console's own client MTU command, with the id this side asked under.</summary>
    ClientMtuCommanded,

    /// <summary>A pong whose tag matched, which is what a round-trip reading is made of.</summary>
    Ponged,

    /// <summary>An MTU probe came back, which means the size under test was carried.</summary>
    MtuCarried,

    /// <summary>
    /// A message reaching the right arm and refused by it - a bang that is not one, a pong whose
    /// tag is wrong, an MTU response that is not video. Logged by the C and nothing else.
    /// </summary>
    Refused,
}

/// <summary>
/// PP791, under PP784: senkusha's state and flags, and the callback that writes them.
///
/// The C keeps state, state_finished and state_failed on one struct that both the run and the
/// takion's callback touch under one mutex. This is that struct's half: what a wait reads and what
/// an arrival writes. PP790's run drives it and PP792's seam reaches it from a live session.
///
/// PP773 IS WHY THIS IS A THING AND NOT A COMMENT. The stream connection had a host, every
/// handler's decisions ported, and three layers of dispatch modelled - and no wire between them. A
/// live PS5 answered every message and the run timed out at every wait, because the arrivals
/// reached the dispatch and the dispatch told nobody. Senkusha's callback is the same shape with
/// one more arm.
///
/// FOUR ARMS AND THE ACK IS THE ONE THE STREAM DOES NOT HAVE. senkusha waits on the acknowledgement
/// of its own message - by sequence number, against the number the send recorded - which is a state
/// the stream connection never enters.
///
/// AND THE TWO AV ARMS DISAGREE ABOUT is_video. A pong must NOT be video and an MTU response MUST
/// be, in the same switch, three lines apart. A port that read one rule for both would answer every
/// MTU probe as carried and measure a link nobody has.
/// </summary>
public sealed class SenkushaArrivals
{
    // A plain object: the wait is Monitor-based, and System.Threading.Lock has no Wait.
    private readonly object gate = new();

    private SenkushaState current = SenkushaState.Idle;
    private SenkushaWaitState flags;

    /// <summary>Where the walk is, which is what every arm below is decided by.</summary>
    public SenkushaState State
    {
        get
        {
            lock (gate)
                return current;
        }
    }

    /// <summary>The flags as they stand, for a caller that is not the run.</summary>
    public SenkushaWaitState Flags
    {
        get
        {
            lock (gate)
                return flags;
        }
    }

    /// <summary>The sequence number the data-ack arm is waiting for, which a send records.</summary>
    public uint DataAckSeqNumExpected { get; set; }

    /// <summary>The id an MTU probe is asked under, which its response must carry back.</summary>
    public uint MtuId { get; set; }

    /// <summary>The tag a ping carries, which its pong must carry back.</summary>
    public uint PingTag { get; set; }

    /// <summary>The frame index a ping carries, which is the test's own index.</summary>
    public ushort PingTestIndex { get; set; }

    /// <summary>And the unit index, which is the ping's number within the test.</summary>
    public ushort PingIndex { get; set; }

    /// <summary>When the pong that ended the last wait arrived, in monotonic microseconds.</summary>
    public ulong PongTimeMicroseconds { get; private set; }

    /// <summary>How many arrivals reached an arm and wrote a flag.</summary>
    public int Signalled { get; private set; }

    /// <summary>The state assignment and the two clears, which the C writes as one triple.</summary>
    public void BeginState(SenkushaState state)
    {
        lock (gate)
        {
            current = state;
            flags = SenkushaStates.Entering(flags);
        }
    }

    /// <summary>should_stop, which the predicate reads and a state entry does not clear.</summary>
    public void Stop()
    {
        lock (gate)
        {
            flags = flags with { ShouldStop = true };
            Monitor.PulseAll(gate);
        }
    }

    /// <summary>
    /// chiaki_cond_timedwait_pred on state_finished_cond_check, bounded by an absolute deadline.
    ///
    /// PP775's rule, learnt one module over: a wait that re-armed its whole timeout on every pulse
    /// is held for as long as anything pulses, and the C's own primitive computes the deadline once.
    /// </summary>
    /// <returns>The flags as they read when it returned, and whether it returned by timeout.</returns>
    public (SenkushaWaitState Flags, bool TimedOut) Wait(int timeoutMs)
    {
        lock (gate)
        {
            long deadline = Environment.TickCount64 + timeoutMs;

            while (!SenkushaStates.WaitEnds(flags))
            {
                long left = deadline - Environment.TickCount64;
                if (left <= 0)
                    break;

                Monitor.Wait(gate, (int)left);
            }

            return (flags, !SenkushaStates.WaitEnds(flags));
        }
    }

    /// <summary>
    /// Layer one's two connect answers, heard ONLY in the state that waits for one.
    ///
    /// Anywhere else they are dropped, so a takion dying during the bang signals nothing and that
    /// wait runs its whole five seconds. The same silence PP366 found in the stream connection, and
    /// the disconnect writes state_failed, which nothing reads.
    /// </summary>
    public SenkushaArrival Event(bool connected)
    {
        lock (gate)
        {
            if (current != SenkushaState.TakionConnect)
                return SenkushaArrival.Ignored;

            flags = flags with { Finished = connected, Failed = !connected };
            Signalled++;
            Monitor.PulseAll(gate);

            return connected ? SenkushaArrival.Connected : SenkushaArrival.Disconnected;
        }
    }

    /// <summary>
    /// The data-ack arm: the state must be waiting for one AND the number must be the one recorded.
    ///
    /// senkusha's own arm, which the stream connection has no counterpart for. An ack for some other
    /// message arrives here too and is ignored, which is why the comparison is part of the arm
    /// rather than an assumption about ordering.
    /// </summary>
    public SenkushaArrival DataAck(uint seqNum)
    {
        lock (gate)
        {
            if (current != SenkushaState.ExpectDataAck || seqNum != DataAckSeqNumExpected)
                return SenkushaArrival.Ignored;

            return Finish(SenkushaArrival.DataAcked);
        }
    }

    /// <summary>
    /// The protobuf arm, routed by the state and refused by the message's own type.
    /// </summary>
    /// <param name="payload">The protobuf, as the data layer handed it over.</param>
    public SenkushaArrival Protobuf(ReadOnlySpan<byte> payload)
    {
        Tkproto.TakionMessage message;

        try
        {
            message = Tkproto.TakionMessage.Parser.ParseFrom(payload.ToArray());
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            // The C logs and returns, touching neither flag.
            return SenkushaArrival.Ignored;
        }

        // PP730: nanopb refuses a message missing a required field and protoc's parser does not, so
        // the switch below would run on bytes the console's own decoder would have thrown out. The C
        // reaches that switch only past a pb_decode that said yes, and this is that line - ahead of
        // every arm, because in the C this IS the decode failing.
        if (!RequiredFields.AllPresentIn(message))
            return SenkushaArrival.Ignored;

        lock (gate)
        {
            return current switch
            {
                SenkushaState.ExpectBang => Bang(message),
                SenkushaState.ExpectProtocolAck => ProtocolAck(message),
                SenkushaState.ExpectClientMtuCommand => ClientMtu(message),
                _ => SenkushaArrival.Ignored,
            };
        }
    }

    /// <summary>
    /// The AV arm, whose two states disagree about whether a packet may be video.
    /// </summary>
    /// <param name="isVideo">The packet's own flag, which the two states read opposite ways.</param>
    /// <param name="frameIndex">Matched against the ping's test index, or against the MTU id.</param>
    /// <param name="unitIndex">Matched against the ping's own index; the MTU arm ignores it.</param>
    /// <param name="data">The packet's payload, whose bytes four to eight carry a pong's tag.</param>
    /// <param name="nowMicroseconds">When it arrived, which is half of a round-trip reading.</param>
    public SenkushaArrival Av(
        bool isVideo, ushort frameIndex, ushort unitIndex, ReadOnlySpan<byte> data, ulong nowMicroseconds)
    {
        // Read before the lock, as the C reads the clock before it takes the mutex: the time a pong
        // arrived is when it ARRIVED, not when a contended lock let this thread look at it.
        uint tag = data.Length >= TagEnd
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data[TagOffset..TagEnd])
            : 0;

        lock (gate)
        {
            if (current == SenkushaState.ExpectPong)
            {
                // NOT video, and both indices matched, and long enough to hold the tag.
                if (isVideo || frameIndex != PingTestIndex || unitIndex != PingIndex || data.Length < TagEnd)
                    return SenkushaArrival.Refused;

                if (tag != PingTag)
                    return SenkushaArrival.Refused;

                PongTimeMicroseconds = nowMicroseconds;
                return Finish(SenkushaArrival.Ponged);
            }

            if (current != SenkushaState.ExpectMtu)
                return SenkushaArrival.Ignored;

            // IS video, and the frame index is the request's id. The opposite rule, three lines on.
            return isVideo && frameIndex == MtuId
                ? Finish(SenkushaArrival.MtuCarried)
                : SenkushaArrival.Refused;
        }
    }

    /// <summary>Where a pong's tag sits in the packet's payload.</summary>
    public const int TagOffset = 4;

    /// <summary>And where it ends, which is the eight bytes the C's own size check demands.</summary>
    public const int TagEnd = 8;

    private SenkushaArrival Bang(Tkproto.TakionMessage message)
        => message.Type == Tkproto.TakionMessage.Types.PayloadType.Bang && message.BangPayload is not null
            ? Finish(SenkushaArrival.Banged)
            : SenkushaArrival.Refused;

    private SenkushaArrival ProtocolAck(Tkproto.TakionMessage message)
        => message.Type == Tkproto.TakionMessage.Types.PayloadType.Takionprotocolrequestack
            && message.TakionProtocolRequestAck is not null
                ? Finish(SenkushaArrival.ProtocolAcked)
                : SenkushaArrival.Refused;

    /// <summary>
    /// The client MTU arm, and the one message it declines to complain about.
    ///
    /// A plain MTU_COMMAND from the server may arrive here and the C says so in a comment: it is
    /// ignored and is NOT an error. Everything else in this state is logged as unexpected. Both
    /// answer Ignored here, because what differs in the C is only what it writes to a log.
    /// </summary>
    private SenkushaArrival ClientMtu(Tkproto.TakionMessage message)
    {
        if (message.Type != Tkproto.TakionMessage.Types.PayloadType.Senkusha
            || message.SenkushaPayload is not { } payload)
        {
            return SenkushaArrival.Refused;
        }

        if (payload.Command != Tkproto.SenkushaPayload.Types.Command.ClientMtuCommand
            || payload.ClientMtuCommand is null)
        {
            // The server's own MTU command, which is expected here and answered with silence.
            return SenkushaArrival.Ignored;
        }

        return payload.ClientMtuCommand.Id == MtuId
            ? Finish(SenkushaArrival.ClientMtuCommanded)
            : SenkushaArrival.Refused;
    }

    /// <summary>Raises the one flag a wait reads, and wakes whoever is on it.</summary>
    private SenkushaArrival Finish(SenkushaArrival arrival)
    {
        flags = flags with { Finished = true };
        Signalled++;
        Monitor.PulseAll(gate);

        return arrival;
    }
}

/// <summary>
/// PP791: the callback's arms read out of senkusha.c, so the wire cannot drift off them.
/// </summary>
public static class SenkushaArrivalsSource
{
    /// <summary>Where the callback is.</summary>
    public const string RelativePath = SenkushaStatesSource.RelativePath;

    /// <summary>senkusha.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>One of the four handlers' bodies, or null where it is gone.</summary>
    public static string? ArmBody(string source, string arm)
        => CFunction.Body(source, $"static void senkusha_takion_{arm}(");

    /// <summary>
    /// Whether the connect answers are still heard only in the state that waits for one.
    ///
    /// The guard is the assertion. Without it both flags would be written from any state, which is
    /// better behaviour and different behaviour.
    /// </summary>
    public static bool TheConnectAnswersAreStillStateGuarded(string callbackBody)
    {
        ArgumentNullException.ThrowIfNull(callbackBody);

        int guard = callbackBody.IndexOf("if(senkusha->state == STATE_TAKION_CONNECT)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int finished = callbackBody.IndexOf("state_finished =", guard, StringComparison.Ordinal);
        int failed = callbackBody.IndexOf("state_failed =", guard, StringComparison.Ordinal);

        return finished > guard && failed > finished;
    }

    /// <summary>
    /// Whether the data-ack arm still compares the sequence number as well as the state.
    ///
    /// An ack for some other message arrives here too. Dropping the comparison would end the wait
    /// on the first ack of any kind, which on a lossy link is the wrong one.
    /// </summary>
    public static bool TheAckArmStillMatchesTheSequenceNumber(string ackBody)
    {
        ArgumentNullException.ThrowIfNull(ackBody);

        return ackBody.Contains(
            "senkusha->state == STATE_EXPECT_DATA_ACK && senkusha->data_ack_seq_num_expected == seq_num",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the two AV states still disagree about is_video, which is the trap.
    ///
    /// A pong is refused for BEING video and an MTU response for NOT being, three lines apart in
    /// one switch. Reading one rule for both answers every MTU probe as carried.
    /// </summary>
    public static bool TheTwoAvArmsStillDisagreeAboutVideo(string avBody)
    {
        ArgumentNullException.ThrowIfNull(avBody);

        int pong = avBody.IndexOf("senkusha->state == STATE_EXPECT_PONG", StringComparison.Ordinal);
        int mtu = avBody.IndexOf("senkusha->state == STATE_EXPECT_MTU", StringComparison.Ordinal);

        if (pong < 0 || mtu <= pong)
            return false;

        return avBody[pong..mtu].Contains("if(packet->is_video", StringComparison.Ordinal)
            && avBody[mtu..].Contains("if(!packet->is_video", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a pong's tag is still read four bytes into the payload and compared.
    ///
    /// PP378 made that read use the unaligned type; the offset and the comparison are what this
    /// holds, because a port reading the tag from the wrong place accepts every pong.
    /// </summary>
    public static bool ThePongTagIsStillReadAtFour(string avBody)
    {
        ArgumentNullException.ThrowIfNull(avBody);

        return avBody.Contains("(packet->data + 4)", StringComparison.Ordinal)
            && avBody.Contains("tag != senkusha->ping_tag", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the server's own MTU command is still tolerated rather than reported.
    ///
    /// The C's comment says so outright: "There might be another MTU_COMMAND from the server, which
    /// we ignore, but this is not an error." A port that logged it would be noisier than the C on a
    /// path that is working.
    /// </summary>
    public static bool TheServersMtuCommandIsStillTolerated(string dataBody)
    {
        ArgumentNullException.ThrowIfNull(dataBody);

        return dataBody.Contains(
            "msg.senkusha_payload.command != tkproto_SenkushaPayload_Command_MTU_COMMAND",
            StringComparison.Ordinal);
    }
}
