using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP773: the wire between an arrival and the flag the run's wait ends on.
///
/// PP729 ported the bang's decisions, PP686 the streaminfo's, PP684 the idle arm's and PP366 all
/// three layers of the dispatch that chooses between them. Each of those is asserted on its own
/// here, and none of them said anything about the HOST - so a live run passed the takion connect
/// state, sent a real BIG, and then sat out two whole timeouts while the console's answers reached
/// a dispatch that told nobody.
///
/// WHAT THESE ASSERT IS THE JOIN AND NOT THE HANDLERS. The bang's six outcomes are BangHandlerTests'
/// subject and the audio header's size is StreamInfoMessageTests'. What is new is which flag each
/// outcome leaves on <see cref="ManagedStreamRunHost"/>, and that the state the run entered is what
/// decides which handler a message reaches at all - the same bytes twice, answered differently.
///
/// AND A BANG IS STILL REFUSED WITHOUT KEYING. IBangKeying stands in front of OpenSSL by design, so
/// the composition root supplies none and a console's bang fails at the derive. That is asserted as
/// the state it is rather than left to be discovered by a console: the arrival REACHES the handler,
/// which is this task, and what it needs next is a derivation.
/// </summary>
public class StreamArrivalsTests(ITestOutputHelper output)
{
    /// <summary>A keying that answers what a test tells it to, as PP729's own double does.</summary>
    private sealed class Keying(bool derives = true, bool keys = true) : IBangKeying
    {
        public bool DeriveSecret(ReadOnlySpan<byte> remotePubKey, ReadOnlySpan<byte> remoteSig) => derives;

        public bool InitCrypt() => keys;
    }

    /// <summary>Shared with SessionBangKeyingTests, which drives the same join over a real session.</summary>
    internal sealed class Sent : IStreamMessageSink
    {
        private readonly bool answer;

        public Sent(bool answer = true) => this.answer = answer;

        public List<ushort> PayloadTypes { get; } = [];

        public bool Send(in StreamMessage message)
        {
            PayloadTypes.Add(message.PayloadType);
            return answer;
        }
    }

    private sealed class Quiet : IFeedbackSink
    {
        public void SendState(ushort seqNum, FeedbackMotion state)
        {
        }

        public void SendHistory(ushort seqNum, ReadOnlySpan<byte> payload)
        {
        }
    }

    private sealed class Quieter : ICongestionSink
    {
        public void Send(CongestionReport report)
        {
        }
    }

    private sealed class NoFrames : IAudioFrameSink
    {
        public void Header(in ManagedAudioHeader header)
        {
        }

        public void Frame(ReadOnlySpan<byte> frame)
        {
        }
    }

    private sealed class NoOutbound : IVideoReceiverOutbound
    {
        public void SendCorruptFrame(ushort from, ushort to)
        {
        }

        public bool SendIdrRequest() => true;

        public void FecFailure(int frameIndex, bool idrRequestSent)
        {
        }
    }

    private static ManagedStreamRunHost Host(Sent sent) => HostOn(new ManagedTakion(0x0000_7773), sent);

    /// <summary>The same host over a takion the caller owns, which a keying needs to install on.</summary>
    internal static ManagedStreamRunHost HostOn(ManagedTakion takion, Sent sent) => new(
        takion,
        new IPEndPoint(IPAddress.Loopback, 9296),
        new ManagedCongestionControl(new ManagedPacketStats(), new Quieter(), 0.05),
        new ManagedFeedbackSender(new Quiet()),
        new ManagedSessionEvents(),
        sent,
        new Stages(),
        StreamMessages.Heartbeat,
        () => new ManagedVideoReceiver((_, _, _) => true, new NoOutbound()),
        () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()),
        () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()));

    private sealed class Stages : IStageSink
    {
        public void Stage(FrameStageTimer stage, ulong sampleUs)
        {
        }

        public void InputToWire(ulong inputUs)
        {
        }
    }

    /// <summary>A well-formed bang, shared with the tests that drive the same path over a session.</summary>
    internal static byte[] BangBytes() => Bang();

    private static byte[] Bang() => new Tkproto.TakionMessage
    {
        Type = Tkproto.TakionMessage.Types.PayloadType.Bang,
        BangPayload = new Tkproto.BangPayload
        {
            ServerVersion = 12,
            Token = 7,
            VersionAccepted = true,
            EncryptedKeyAccepted = true,
            SessionKey = "sessionId4321",
            EcdhPubKey = ByteString.CopyFrom([.. Enumerable.Range(0, 65).Select(one => (byte)one)]),
            EcdhSig = ByteString.CopyFrom([.. Enumerable.Range(0, 32).Select(one => (byte)(0x80 + one))]),
        },
    }.ToByteArray();

    private static byte[] StreamInfo(int audioHeaderSize = StreamInfoMessage.AudioHeaderSize)
    {
        var message = new Tkproto.TakionMessage
        {
            Type = Tkproto.TakionMessage.Types.PayloadType.Streaminfo,
            StreamInfoPayload = new Tkproto.StreamInfoPayload
            {
                AudioHeader = ByteString.CopyFrom(new byte[audioHeaderSize]),
            },
        };

        message.StreamInfoPayload.Resolution.Add(new Tkproto.ResolutionPayload
        {
            Width = 1920,
            Height = 1080,
            VideoHeader = ByteString.CopyFrom(new byte[8]),
        });

        return message.ToByteArray();
    }

    private static byte[] Disconnect(string reason) => new Tkproto.TakionMessage
    {
        Type = Tkproto.TakionMessage.Types.PayloadType.Disconnect,
        DisconnectPayload = new Tkproto.DisconnectPayload { Reason = reason },
    }.ToByteArray();

    /// <summary>
    /// A KEYED BANG RAISES THE FLAG ITS STATE WAITS ON, which is the whole of PP773's remainder.
    ///
    /// The wait ends on finished and on nothing else - PP365 established state_failed is read by
    /// nobody - so this is the one arrival that lets the walk leave EXPECT_BANG.
    /// </summary>
    [Fact]
    public void AKeyedBangFinishesTheBangState()
    {
        var sent = new Sent();
        ManagedStreamRunHost host = Host(sent);
        var arrivals = new StreamArrivals(host, sent, new Keying());

        host.BeginState(StreamState.ExpectBang);

        ArrivalReading reading = arrivals.Data(TakionDataType.Protobuf, Bang());

        output.WriteLine($"{reading}");

        Assert.Equal(ProtobufHandler.ExpectBang, reading.Handler);
        Assert.Equal(BangOutcome.Keyed, reading.Bang);
        Assert.Equal(StreamFlagRaised.Finished, reading.Raised);
        Assert.True(host.Flags.Finished);
    }

    /// <summary>
    /// AND WITHOUT KEYING IT IS REFUSED, which is where the composition root stands today.
    ///
    /// IBangKeying is a seam on purpose - SeamReach says so - so the root supplies none and the
    /// derive says no. The message still reached the handler, which is what changed; what it leaves
    /// is state_failed, and a wait does not end on that.
    /// </summary>
    [Fact]
    public void ABangWithNoKeyingIsRefusedAtTheDerive()
    {
        var sent = new Sent();
        ManagedStreamRunHost host = Host(sent);
        var arrivals = new StreamArrivals(host, sent);

        host.BeginState(StreamState.ExpectBang);

        ArrivalReading reading = arrivals.Data(TakionDataType.Protobuf, Bang());

        Assert.Equal(BangOutcome.Refused, reading.Bang);
        Assert.Equal(StreamFlagRaised.Failed, reading.Raised);
        Assert.False(host.Flags.Finished);
        Assert.True(host.Flags.Failed);
    }

    /// <summary>
    /// A STREAMINFO FINISHES ITS OWN STATE, and sends the three messages the C sends first.
    ///
    /// The ack is the one PP370 found unchecked in the C: a console still waiting to be told is a
    /// session that dies later at the far end, so all three are sent and all three are read.
    /// </summary>
    [Fact]
    public void AStreamInfoFinishesTheStreamInfoState()
    {
        var sent = new Sent();
        ManagedStreamRunHost host = Host(sent);
        var arrivals = new StreamArrivals(host, sent);

        // The receivers exist by this point in a run, and the profiles are handed to them.
        host.CreateVideoReceiver();
        host.CreateAudioReceiver();
        host.BeginState(StreamState.ExpectStreaminfo);

        ArrivalReading reading = arrivals.Data(TakionDataType.Protobuf, StreamInfo());

        output.WriteLine($"{reading}, sent {string.Join(", ", sent.PayloadTypes)}");

        Assert.Equal(StreamInfoVerdict.Accepted, reading.StreamInfo);
        Assert.Equal(StreamFlagRaised.Finished, reading.Raised);
        Assert.True(host.Flags.Finished);

        // The C's own three, in its order: the ack, the controller connection, the microphone.
        Assert.Equal(
            [
                StreamExchangeParticipant.StreamInfoAckType,
                StreamMessages.ControllerConnection(dualSense: false).PayloadType,
                StreamMessages.MicrophoneStreamInfo().PayloadType,
            ],
            sent.PayloadTypes);
    }

    /// <summary>
    /// AND A SEND THAT FAILS FAILS THE STATE rather than reporting a stream nobody was told about.
    /// </summary>
    [Fact]
    public void AStreamInfoWhoseAckWillNotGoFailsTheState()
    {
        var sent = new Sent(answer: false);
        ManagedStreamRunHost host = Host(sent);
        var arrivals = new StreamArrivals(host, sent);

        host.CreateVideoReceiver();
        host.CreateAudioReceiver();
        host.BeginState(StreamState.ExpectStreaminfo);

        ArrivalReading reading = arrivals.Data(TakionDataType.Protobuf, StreamInfo());

        Assert.Equal(StreamFlagRaised.Failed, reading.Raised);
        Assert.False(host.Flags.Finished);
    }

    /// <summary>
    /// THE SAME BYTES, ANSWERED BY THE STATE AND NOTHING ELSE - which is PP366's third layer.
    ///
    /// One streaminfo, read three times. In EXPECT_BANG it is saved for the replay; in
    /// EXPECT_STREAMINFO it finishes the state; in IDLE it is an unnamed type the default arm drops.
    /// A port that routed by the message rather than by the state would answer all three the same
    /// way and lose the replay entirely.
    /// </summary>
    [Fact]
    public void OneMessageIsThreeMessagesDependingOnTheState()
    {
        var sent = new Sent();
        ManagedStreamRunHost host = Host(sent);
        var arrivals = new StreamArrivals(host, sent);

        host.CreateVideoReceiver();
        host.CreateAudioReceiver();

        host.BeginState(StreamState.ExpectBang);
        ArrivalReading early = arrivals.Data(TakionDataType.Protobuf, StreamInfo());

        Assert.Equal(BangOutcome.SavedEarly, early.Bang);
        Assert.Null(early.Raised);
        Assert.True(host.HasEarlyStreaminfo);

        host.BeginState(StreamState.ExpectStreaminfo);
        ArrivalReading live = arrivals.Data(TakionDataType.Protobuf, StreamInfo());

        Assert.Equal(StreamInfoVerdict.Accepted, live.StreamInfo);
        Assert.Equal(StreamFlagRaised.Finished, live.Raised);

        host.BeginState(StreamState.Idle);
        ArrivalReading late = arrivals.Data(TakionDataType.Protobuf, StreamInfo());

        Assert.Equal(ProtobufHandler.Idle, late.Handler);
        Assert.Equal(IdleAction.Ignore, late.Idle);
        Assert.Null(late.Raised);
    }

    /// <summary>
    /// AND THE REPLAY IS THE SAME HANDLER OVER THE MESSAGE THE BANG STATE KEPT.
    ///
    /// The host holds the buffer and this holds the handler, so the C's one line - the handler,
    /// then the free - is a delegate the composition root installs. Without it the replay freed the
    /// message and answered with flags nothing had written.
    /// </summary>
    [Fact]
    public void TheReplayRunsTheStreamInfoHandlerOverTheBufferedMessage()
    {
        var sent = new Sent();
        ManagedStreamRunHost host = Host(sent);
        var arrivals = new StreamArrivals(host, sent);

        host.ReplayHandler = arrivals.Replay;
        host.CreateVideoReceiver();
        host.CreateAudioReceiver();

        host.BeginState(StreamState.ExpectBang);
        arrivals.Data(TakionDataType.Protobuf, StreamInfo());

        host.BeginState(StreamState.ExpectStreaminfo);
        StreamWaitState after = host.ReplayEarlyStreaminfo();

        output.WriteLine($"after replay {after}, sent {string.Join(", ", sent.PayloadTypes)}");

        // The state finished on the replay alone, which is ordering 2: the run then skips the wait.
        Assert.True(after.Finished);
        Assert.False(StreamConnectionStates.WaitsAfterReplay(after));
        Assert.False(host.HasEarlyStreaminfo);

        // And a second replay is not the same message twice: the buffer is gone.
        Assert.Equal(3, sent.PayloadTypes.Count);
        host.ReplayEarlyStreaminfo();
        Assert.Equal(3, sent.PayloadTypes.Count);
    }

    /// <summary>
    /// A DISCONNECT ARRIVING IN EITHER SETUP STATE IS ROUTED, with the reason the console gave.
    ///
    /// Tested before the something-else branch in both handlers, which is why a console hanging up
    /// during setup is a disconnect and not an unknown message. PP755 keeps the reason on the host
    /// because the session thread writes its quit reason from it.
    /// </summary>
    [Theory]
    [InlineData(StreamState.ExpectBang)]
    [InlineData(StreamState.ExpectStreaminfo)]
    [InlineData(StreamState.Idle)]
    public void ADisconnectIsRoutedFromEveryStateWithItsReason(StreamState state)
    {
        var sent = new Sent();
        ManagedStreamRunHost host = Host(sent);
        var arrivals = new StreamArrivals(host, sent);

        host.BeginState(state);

        ArrivalReading reading = arrivals.Data(TakionDataType.Protobuf, Disconnect("console said so"));

        Assert.Equal(StreamFlagRaised.RemoteDisconnected, reading.Raised);
        Assert.True(host.Flags.RemoteDisconnected);
        Assert.Equal("console said so", host.RemoteDisconnectReason);
    }

    /// <summary>
    /// LAYER ONE: the connect answers are heard only in the state that waits for one.
    ///
    /// A takion dying during EXPECT_BANG signals nothing, which is the C's own silence and the
    /// reason that wait sits out its whole timeout rather than failing early.
    /// </summary>
    [Fact]
    public void TheConnectAnswersAreHeardOnlyInTheConnectState()
    {
        var sent = new Sent();
        ManagedStreamRunHost host = Host(sent);
        var arrivals = new StreamArrivals(host, sent);

        host.BeginState(StreamState.TakionConnect);
        Assert.Equal(StreamFlagRaised.Finished, arrivals.Connected().Raised);
        Assert.True(host.Flags.Finished);

        host.BeginState(StreamState.ExpectBang);
        Assert.Null(arrivals.Disconnected().Raised);
        Assert.Null(arrivals.Connected().Raised);
        Assert.False(host.Flags.Finished);
        Assert.False(host.Flags.Failed);
    }

    /// <summary>
    /// AND THE WHOLE PATH FROM A DATAGRAM, which is what the takion's callback hands over.
    ///
    /// The message header, the nine-byte data header under it, the type byte, and the body - built
    /// here the way a console builds one, and read back through the same call the receive loop
    /// makes. A datagram carrying somebody else's tag is refused at the first gate.
    /// </summary>
    [Fact]
    public void AWholeDatagramReachesTheHandlerItsStateNames()
    {
        var sent = new Sent();
        ManagedStreamRunHost host = Host(sent);
        var ledger = new ManagedKeyState();

        var arrivals = new StreamArrivals(host, sent, new Keying())
        {
            TagLocal = 0x0000_7773,
            Ledger = ledger,
        };

        host.BeginState(StreamState.ExpectBang);

        byte[] datagram = ControlDatagram(0x0000_7773, Bang());
        ArrivalReading reading = arrivals.Datagram(datagram);

        output.WriteLine($"{reading}");

        Assert.Equal(BangOutcome.Keyed, reading.Bang);
        Assert.True(host.Flags.Finished);

        // And the tag gate, which is the C's first refusal of a message.
        host.BeginState(StreamState.ExpectBang);
        Assert.Equal(TakionRoute.Ignored, arrivals.Datagram(ControlDatagram(0x0000_7774, Bang())).Route);
        Assert.False(host.Flags.Finished);
    }

    /// <summary>
    /// A control datagram carrying one protobuf, built the way takion.c builds one.
    /// </summary>
    private static byte[] ControlDatagram(uint tag, byte[] body)
    {
        int payloadSize = TakionDataPush.DataHeaderSize + body.Length;
        byte[] datagram = new byte[1 + TakionHandshake.MessageHeaderSize + payloadSize];

        datagram[0] = TakionMessageHeader.ControlPacketType;

        TakionMessageHeader.Write(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize),
            tag,
            keyPos: 0,
            TakionMessageIntake.DataChunkType,
            TakionDataPush.ExpectedTypeB,
            payloadSize);

        // The nine-byte data header: four of sequence, two of channel, two reserved, then the type.
        Span<byte> payload = datagram.AsSpan(1 + TakionHandshake.MessageHeaderSize);
        payload[TakionDataDrain.DataTypeOffset] = (byte)TakionDataType.Protobuf;
        body.CopyTo(payload[TakionDataDrain.HeaderSize..]);

        return datagram;
    }
}
