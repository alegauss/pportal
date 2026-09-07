using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP791, under PP784: the first implementation of <see cref="ISenkushaRunHost"/> outside the tests.
///
/// PP790 wrote the run and it takes a host. Nothing filled it, so what existed was a sequence
/// nothing performs - PP669's rule, and the state PP745 took the stream connection's own host out
/// of. This is the call: the run's eleven members answered by the port's own takion, the arrivals
/// PP791's first half wrote, and the six protobufs <see cref="SenkushaMessages"/> builds.
///
/// PP773 IS WHY THE WIRE IS HALF THE WORK. The stream connection had a host, every handler's
/// decisions ported and three layers of dispatch modelled - and nothing joining them. A live PS5
/// answered every message and the run timed out at every wait. So the connect here starts the
/// receive thread and raises the event the wait is on, exactly as its counterpart learnt to.
///
/// THE THREE MEASUREMENTS ARE LOOPS AND THE ARITHMETIC IS NOT HERE. <see cref="SenkushaMeasurements"/>
/// holds the average, the window and the probe, and this spends them: what a host adds is the send,
/// the wait and the retry. A search whose midpoint were computed here would be a second copy of the
/// one PP789 already holds against the C.
///
/// AND SENKUSHA'S TAKION IS NOT THE STREAM'S. Its AV packets are v7, nothing it sends is encrypted,
/// and the pings go out through <see cref="ManagedTakion.SendRaw"/> - no header, no sequence number
/// and no send buffer, because what is being measured is the size that reaches the console.
/// </summary>
public sealed class ManagedSenkushaRunHost : ISenkushaRunHost
{
    private readonly ManagedTakion takion;
    private readonly IPEndPoint peer;
    private readonly SenkushaArrivals arrivals;
    private readonly Func<uint> tags;
    private readonly Func<ulong> clock;
    private readonly List<SenkushaSend> sent = [];

    /// <summary>Everything the run needs from the world, none of it built here.</summary>
    /// <param name="takion">The transport, already constructed and not yet connected.</param>
    /// <param name="peer">Where the console is, which senkusha reaches on its own port.</param>
    /// <param name="arrivals">The callback's half of the state, which the receive thread writes.</param>
    /// <param name="tags">chiaki_random_32, taken as a function so a test can make a run repeat.</param>
    /// <param name="clock">chiaki_time_now_monotonic_us, likewise - a round trip is two readings.</param>
    public ManagedSenkushaRunHost(
        ManagedTakion takion,
        IPEndPoint peer,
        SenkushaArrivals arrivals,
        Func<uint>? tags = null,
        Func<ulong>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(takion);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(arrivals);

        this.takion = takion;
        this.peer = peer;
        this.arrivals = arrivals;
        this.tags = tags ?? RandomTag;
        this.clock = clock ?? MonotonicMicroseconds;
    }

    /// <summary>EXPECT_TIMEOUT_MS, which four of the run's waits are given.</summary>
    public int ExpectTimeoutMs { get; init; } = SenkushaTimeouts.ExpectMs;

    /// <summary>CONNECT_TIMEOUT_MS, which the wait on the takion's own event is given.</summary>
    public int ConnectTimeoutMs { get; init; } = SenkushaTimeouts.ConnectMs;

    /// <summary>EXPECT_PONG_TIMEOUT_MS, which one ping of ten is given.</summary>
    public int ExpectPongTimeoutMs { get; init; } = SenkushaTimeouts.ExpectPongMs;

    /// <summary>
    /// How long the handshake itself is given, which is the takion's wait and not senkusha's.
    ///
    /// The C's connect returns as soon as the handshake is done and the CONNECTED event arrives on
    /// the takion's thread, so CONNECT_TIMEOUT_MS bounds the WAIT and not the connect. This port's
    /// connect blocks through the handshake, so both numbers are real and they are different waits.
    /// </summary>
    public int HandshakeTimeoutMs { get; init; } = TakionHandshake.ExpectTimeoutMs;

    /// <summary>The frame index every ping of the round-trip test carries, which is the test's own.</summary>
    public ushort PingTestIndex { get; init; }

    /// <summary>SENKUSHA_PING_COUNT_DEFAULT, which is how many the round-trip test sends.</summary>
    public int PingCount { get; init; } = SenkushaMeasurements.PingCount;

    /// <summary>The takion this run drives, for a caller reading why a wait ended in nothing.</summary>
    public ManagedTakion Takion => takion;

    /// <summary>The arrivals, likewise: what a wait ended on is written there and not here.</summary>
    public SenkushaArrivals Arrivals => arrivals;

    /// <summary>Every protobuf this host put on the wire, in order.</summary>
    public IReadOnlyList<SenkushaSend> Sent => sent;

    /// <summary>What the last connect's handshake answered, or null before one ran.</summary>
    public TakionHandshakeOutcome? LastHandshake { get; private set; }

    /// <summary>Each ping that came back, in microseconds, from the last round-trip test.</summary>
    public IReadOnlyList<ulong> Answered { get; private set; } = [];

    /// <summary>The inbound search's steps, which is how a measurement is read back.</summary>
    public IReadOnlyList<SenkushaMtuStep> InboundSteps { get; private set; } = [];

    /// <summary>And the outbound one's.</summary>
    public IReadOnlyList<SenkushaMtuStep> OutboundSteps { get; private set; } = [];

    /// <inheritdoc/>
    public bool ShouldStop => arrivals.Flags.ShouldStop;

    /// <inheritdoc/>
    public void BeginState(SenkushaState state) => arrivals.BeginState(state);

    /// <inheritdoc/>
    public bool ConnectTakion()
    {
        TakionHandshakeOutcome outcome = takion.Connect(peer, HandshakeTimeoutMs);
        LastHandshake = outcome;

        if (outcome.Error != ChiakiError.Success)
            return false;

        // PP773's lesson, one module over: a takion that connected and is not reading answers
        // nothing, and every wait after this one ends in a timeout with no explanation.
        //
        // WITHOUT CRYPT, which is senkusha's own difference. Its takion is built with enable_crypt
        // false, so an arm decrypting arrivals here would turn a pong into garbage.
        takion.StartReceiving(enableCrypt: false);

        // The CONNECTED event. The C's connect starts a thread and the event reaches the callback
        // later; this port's blocks until the handshake is done, so by the time it answers Success
        // the event has already happened and waiting for another would wait forever.
        arrivals.Event(connected: true);

        return true;
    }

    /// <inheritdoc/>
    public (SenkushaWaitState Flags, bool TimedOut) Wait(SenkushaState state)
        => arrivals.Wait(TimeoutFor(state));

    /// <summary>Which of the three waits a state is given, with this host's own knobs applied.</summary>
    public int TimeoutFor(SenkushaState state) => state switch
    {
        SenkushaState.TakionConnect => ConnectTimeoutMs,
        SenkushaState.ExpectPong => ExpectPongTimeoutMs,
        _ => ExpectTimeoutMs,
    };

    /// <inheritdoc/>
    public bool SetVersion() => Send(SenkushaMessages.SetVersion()) is not null;

    /// <inheritdoc/>
    public bool SendBig() => Send(SenkushaMessages.Big()) is not null;

    /// <inheritdoc/>
    public void SendDisconnect() => Send(SenkushaMessages.Disconnect());

    /// <inheritdoc/>
    public void CloseTakion() => takion.Dispose();

    /// <summary>
    /// senkusha_run_rtt_test: echo on, ten pings, echo off, and the mean over what came back.
    ///
    /// A LOST PONG CONTINUES. Nine of ten missing is still an answer - the C divides by the pings
    /// that were ANSWERED and not by the count it sent, which PP789 holds against the file - and the
    /// only thing that returns early is a stop.
    ///
    /// The echo commands either side are not decoration: the console does not send pongs until it
    /// is told to, so a run that skipped the first one measures ten timeouts.
    /// </summary>
    public ChiakiError RunRttTest(out ulong roundTripMicroseconds)
    {
        roundTripMicroseconds = 0;
        Answered = [];

        ChiakiError enabled = SendWaitingForAck(SenkushaMessages.EchoCommandMessage(true));
        if (enabled != ChiakiError.Success)
            return enabled;

        var answered = new List<ulong>();

        for (int index = 0; index < PingCount; index++)
        {
            uint tag = tags();

            BeginState(SenkushaState.ExpectPong);
            arrivals.PingTestIndex = PingTestIndex;
            arrivals.PingIndex = (ushort)index;
            arrivals.PingTag = tag;

            byte[] ping = new byte[SenkushaMeasurements.PingBufferBytes];
            if (!WritePing(ping, PingTestIndex, (ushort)index, tag, out _))
                return ChiakiError.Unknown;

            // Read before the send, as the C reads it: a round trip is measured from when the ping
            // left, not from when a contended thread got round to noting it.
            ulong started = clock();

            ChiakiError put = takion.SendRaw(ping);
            if (put != ChiakiError.Success)
                return put;

            (SenkushaWaitState flags, bool _) = arrivals.Wait(ExpectPongTimeoutMs);

            if (!flags.Finished)
            {
                if (flags.ShouldStop)
                    return ChiakiError.Canceled;

                // The lost pong, which is a continue and not a failure.
                continue;
            }

            answered.Add(arrivals.PongTimeMicroseconds - started);
        }

        ChiakiError disabled = SendWaitingForAck(SenkushaMessages.EchoCommandMessage(false));
        if (disabled != ChiakiError.Success)
            return disabled;

        Answered = answered;

        if (answered.Count < 1)
            return ChiakiError.Unknown;

        roundTripMicroseconds = SenkushaMeasurements.Average(answered).AverageMicroseconds;

        return ChiakiError.Success;
    }

    /// <summary>
    /// senkusha_run_mtu_in_test: ask the console for a packet of a size, and halve the window.
    ///
    /// The probe that CARRIED raises the floor and the one that did not lowers the ceiling, which is
    /// <see cref="SenkushaMeasurements.Step"/>. Each size is asked up to three times, because a
    /// single lost datagram is not evidence that a link cannot carry one.
    /// </summary>
    public ChiakiError RunMtuInTest(ulong timeoutMs, out uint mtuIn)
    {
        uint min = ManagedSenkushaRun.SearchMin;
        uint max = ManagedSenkushaRun.SearchMax;
        uint cur = SenkushaMeasurements.FirstInboundProbe(max);
        uint requestId = 0;

        var steps = new List<SenkushaMtuStep>();
        mtuIn = min;

        while (SenkushaMeasurements.Searching(min, max))
        {
            var carried = false;

            for (uint attempt = 0; attempt < ManagedSenkushaRun.SearchRetries; attempt++)
            {
                BeginState(SenkushaState.ExpectMtu);
                arrivals.MtuId = ++requestId;

                if (Send(SenkushaMessages.MtuCommandMessage(requestId, cur)) is null)
                    return ChiakiError.Network;

                (SenkushaWaitState flags, bool _) = arrivals.Wait((int)timeoutMs);

                if (!flags.Finished)
                {
                    if (flags.ShouldStop)
                        return ChiakiError.Canceled;

                    // PP380: the retry, on both arms. Falling through here reported the size as
                    // carried for a response that never came.
                    continue;
                }

                carried = true;
                break;
            }

            SenkushaMtuStep step = SenkushaMeasurements.Step(cur, carried, min, max);
            steps.Add(step);

            min = step.Min;
            max = step.Max;
            cur = SenkushaMeasurements.NextProbe(min, max);
        }

        InboundSteps = steps;

        // The FLOOR and never the probe, which is the size known to carry rather than the one under
        // test when the window closed.
        mtuIn = min;

        return ChiakiError.Success;
    }

    /// <summary>
    /// senkusha_run_mtu_out_test: the same search the other way, over pings this side pads.
    ///
    /// It opens with a client MTU command the console answers with one of its own, and closes with a
    /// second carrying the window's CEILING - the size that stopped carrying, which is PP789's
    /// finding and is reported as the C reports it.
    ///
    /// One buffer for the whole search, padded once. The probe is the LENGTH sent out of it, which is
    /// why a smaller size costs no allocation and why the padding never has to be rewritten.
    /// </summary>
    public ChiakiError RunMtuOutTest(uint mtuIn, ulong timeoutMs, out uint mtuOut)
    {
        uint min = ManagedSenkushaRun.SearchMin;
        uint max = ManagedSenkushaRun.SearchMax;

        mtuOut = min;

        if (!SenkushaMeasurements.OutboundArgumentsAccepted(mtuIn, min, max))
            return ChiakiError.InvalidData;

        BeginState(SenkushaState.ExpectClientMtuCommand);
        arrivals.MtuId = OpeningCommandId;

        if (Send(SenkushaMessages.ClientMtuCommandMessage(OpeningCommandId, mtuIn, true, mtuIn)) is null)
            return ChiakiError.Network;

        (SenkushaWaitState opened, bool timedOut) = arrivals.Wait(ExpectTimeoutMs);

        if (!opened.Finished)
        {
            if (timedOut)
                return ChiakiError.Timeout;

            return opened.ShouldStop ? ChiakiError.Canceled : ChiakiError.Unknown;
        }

        byte[] packet = OutboundBuffer(max);

        uint cur = SenkushaMeasurements.FirstOutboundProbe(mtuIn);
        var steps = new List<SenkushaMtuStep>();

        while (SenkushaMeasurements.Searching(min, max))
        {
            var carried = false;

            for (uint attempt = 0; attempt < ManagedSenkushaRun.SearchRetries; attempt++)
            {
                uint tag = tags();

                BeginState(SenkushaState.ExpectPong);
                arrivals.PingTestIndex = 0;
                arrivals.PingIndex = (ushort)attempt;
                arrivals.PingTag = tag;

                if (!WritePing(packet, 0, (ushort)attempt, tag, out int headerSize)
                    || headerSize != SenkushaMeasurements.AvPacketAdd)
                {
                    return ChiakiError.Unknown;
                }

                // The size under test MINUS the two headers the wire adds, which is the whole point
                // of the measurement: what is being asked is how big an IP packet gets through.
                var length = (int)(cur - SenkushaMeasurements.UdpPacketAdd);

                ChiakiError put = takion.SendRaw(packet.AsSpan(0, length));

                (SenkushaWaitState flags, bool _) = put == ChiakiError.Success
                    ? arrivals.Wait((int)timeoutMs)
                    : (arrivals.Flags, true);

                if (!flags.Finished)
                {
                    if (flags.ShouldStop)
                        return ChiakiError.Canceled;

                    continue;
                }

                carried = true;
                break;
            }

            SenkushaMtuStep step = SenkushaMeasurements.Step(cur, carried, min, max);
            steps.Add(step);

            min = step.Min;
            max = step.Max;
            cur = SenkushaMeasurements.NextProbe(min, max);
        }

        OutboundSteps = steps;
        mtuOut = min;

        // The closing command, with the ceiling and an ack waited for. Its failure is the test's.
        return SendWaitingForAck(
            SenkushaMessages.ClientMtuCommandMessage(ClosingCommandId, max, false, mtuIn));
    }

    /// <summary>The id the outbound test's opening client MTU command carries.</summary>
    public const uint OpeningCommandId = 1;

    /// <summary>And its closing one, which is two and not the id the search ended on.</summary>
    public const uint ClosingCommandId = 2;

    /// <summary>
    /// The outbound test's buffer: a header's worth of zeros, eight more, then CHIAKI repeating.
    ///
    /// Sized for the search's CEILING minus the UDP and IP headers, because that is the largest
    /// probe the window can reach - and the smaller ones are prefixes of it.
    /// </summary>
    public static byte[] OutboundBuffer(uint max)
    {
        var packet = new byte[max - SenkushaMeasurements.UdpPacketAdd];
        int from = (int)SenkushaMeasurements.AvPacketAdd + SenkushaArrivals.TagEnd;

        ReadOnlySpan<byte> padding = SenkushaMeasurements.OutboundPadding;
        for (int i = from; i < packet.Length; i++)
            packet[i] = padding[(i - from) % padding.Length];

        return packet;
    }

    /// <summary>
    /// One ping, formatted into a caller's buffer: a v7 AV header and a tag four bytes past it.
    ///
    /// The C zeroes the four bytes between the header and the tag in the outbound test and leaves
    /// them zero in the round-trip one, so both are written here rather than relying on a buffer
    /// that happened to be fresh.
    /// </summary>
    public static bool WritePing(
        byte[] packet, ushort testIndex, ushort pingIndex, uint tag, out int headerSize)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var header = new V7AvHeader(
            IsVideo: false,
            UsesNaluInfoStructs: false,
            PacketIndex: 0,
            FrameIndex: testIndex,
            UnitIndex: pingIndex,
            UnitsInFrameTotal: SenkushaMeasurements.PingUnitsInFrameTotal,
            UnitsInFrameFec: 0,
            Codec: SenkushaMeasurements.PingCodec,
            WordAt0x18: 0,
            AdaptiveStreamIndex: 0,
            KeyPos: 0,
            DataOffset: 0,
            DataSize: 0);

        if (AvPacketV7.FormatHeader(packet, header, out headerSize) != ChiakiError.Success)
            return false;

        int at = headerSize + SenkushaMeasurements.TagOffsetPastHeader;
        if (at + sizeof(uint) > packet.Length)
            return false;

        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(headerSize, sizeof(uint)), 0);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(at, sizeof(uint)), tag);

        return true;
    }

    /// <summary>
    /// senkusha_send_data: the chokepoint every protobuf goes through, and the trace's only writer.
    /// </summary>
    /// <returns>The sequence number the send took, or null where it did not leave.</returns>
    private uint? Send(in SenkushaSend message)
    {
        sent.Add(message);

        TakionSendOutcome outcome = takion.SendData(DataChannel, message.Body, message.DataType);

        return outcome.Error == ChiakiError.Success ? outcome.SeqNum : null;
    }

    /// <summary>The channel senkusha's protobufs go out on, which is one for every one of them.</summary>
    public const ushort DataChannel = 1;

    /// <summary>
    /// senkusha_send_data_wait_for_ack: send, then wait for the ack of THAT message.
    ///
    /// By sequence number, recorded before the wait begins. An ack for some other message arrives
    /// here too, and the arm that reads it compares - so the number has to be in place before
    /// anything can answer.
    ///
    /// PP380: a wait that did not finish answers TIMEOUT or CANCELED or UNKNOWN, and never the
    /// success the wait itself left behind.
    /// </summary>
    private ChiakiError SendWaitingForAck(in SenkushaSend message)
    {
        BeginState(SenkushaState.ExpectDataAck);

        // BEFORE the send, and the C gets this for free: it holds the state mutex across the whole
        // helper, so an ack arriving mid-send blocks until the wait begins. Here the arrivals take
        // their own lock per call, so setting the expectation afterwards leaves a window in which
        // the console's ack is compared against the LAST message's number and dropped.
        arrivals.DataAckSeqNumExpected = takion.SeqNumLocal;

        if (Send(message) is not { } seqNum)
            return ChiakiError.Network;

        // And corrected where something else took a number in between, which is a window this
        // narrows rather than closes - the alternative is a lock the takion would have to share.
        if (seqNum != arrivals.DataAckSeqNumExpected)
            arrivals.DataAckSeqNumExpected = seqNum;

        (SenkushaWaitState flags, bool timedOut) = arrivals.Wait(ExpectTimeoutMs);

        if (flags.Finished)
            return ChiakiError.Success;

        if (flags.ShouldStop)
            return ChiakiError.Canceled;

        return timedOut ? ChiakiError.Timeout : ChiakiError.Unknown;
    }

    /// <summary>chiaki_random_32, which every ping's tag is one of.</summary>
    private static uint RandomTag()
    {
        Span<byte> four = stackalloc byte[4];
        RandomNumberGenerator.Fill(four);

        return BinaryPrimitives.ReadUInt32LittleEndian(four);
    }

    /// <summary>
    /// chiaki_time_now_monotonic_us, which a round trip is the difference of two of.
    ///
    /// PP805: public, because the router that feeds this host's arrivals reads the same clock and
    /// the two readings are subtracted from one another - a pong's arrival is taken there and the
    /// send that preceded it here. Two clocks would make a round trip the difference of two
    /// different origins, which is a number rather than a measurement.
    /// </summary>
    public static ulong MonotonicMicroseconds()
        => (ulong)(System.Diagnostics.Stopwatch.GetTimestamp()
            / (System.Diagnostics.Stopwatch.Frequency / 1_000_000.0));
}
