using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP791, under PP784: the wire from senkusha's callback to the flags its waits read.
///
/// PP773 is why this is code and not a comment. The stream connection had a host, every handler's
/// decisions ported and three layers of dispatch modelled - and nothing between them. A live PS5
/// answered every message and the run timed out at every wait, because the arrivals reached the
/// dispatch and the dispatch told nobody.
///
/// FOUR ARMS, AND TWO OF THEM ARE SENKUSHA'S OWN. The stream connection never waits on the ack of
/// its own message, and it has no AV arm that answers a measurement. Both are here, and the second
/// carries the trap: a pong must NOT be video and an MTU response MUST be, in one switch three
/// lines apart.
/// </summary>
public class SenkushaArrivalsTests(ITestOutputHelper output)
{
    private static string? Source()
        => SenkushaArrivalsSource.Locate() is { } path ? File.ReadAllText(path) : null;

    private static byte[] Bang() => new Tkproto.TakionMessage
    {
        Type = Tkproto.TakionMessage.Types.PayloadType.Bang,
        BangPayload = new Tkproto.BangPayload
        {
            VersionAccepted = true,
            EncryptedKeyAccepted = true,
            SessionKey = string.Empty,
            ServerVersion = 12,
            Token = 7,
        },
    }.ToByteArray();

    private static byte[] ProtocolAck() => new Tkproto.TakionMessage
    {
        Type = Tkproto.TakionMessage.Types.PayloadType.Takionprotocolrequestack,
        TakionProtocolRequestAck =
            new Tkproto.TakionProtocolRequestAckPayload { TakionProtocolVersion = 7 },
    }.ToByteArray();

    private static byte[] ClientMtu(uint id) => new Tkproto.TakionMessage
    {
        Type = Tkproto.TakionMessage.Types.PayloadType.Senkusha,
        SenkushaPayload = new Tkproto.SenkushaPayload
        {
            Command = Tkproto.SenkushaPayload.Types.Command.ClientMtuCommand,
            ClientMtuCommand = new Tkproto.SenkushaClientMtuCommand { Id = id, State = true, MtuReq = 1454 },
        },
    }.ToByteArray();

    private static byte[] ServerMtu() => new Tkproto.TakionMessage
    {
        Type = Tkproto.TakionMessage.Types.PayloadType.Senkusha,
        SenkushaPayload = new Tkproto.SenkushaPayload
        {
            Command = Tkproto.SenkushaPayload.Types.Command.MtuCommand,
            MtuCommand = new Tkproto.SenkushaMtuCommand { Id = 9, MtuReq = 1454, Num = 1 },
        },
    }.ToByteArray();

    private static byte[] Pong(uint tag)
    {
        byte[] data = new byte[SenkushaArrivals.TagEnd];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(SenkushaArrivals.TagOffset), tag);

        return data;
    }

    /// <summary>
    /// THE CONNECT ANSWERS ARE HEARD ONLY IN THE STATE THAT WAITS FOR ONE.
    ///
    /// A takion dying during the bang signals nothing, so that wait runs its whole five seconds.
    /// And the disconnect writes the flag PP365 established nothing reads - reproduced, because a
    /// port that ended the wait on it would report failures sooner than the C.
    /// </summary>
    [Fact]
    public void TheConnectAnswersAreOnlyHeardWhileConnecting()
    {
        var arrivals = new SenkushaArrivals();
        arrivals.BeginState(SenkushaState.TakionConnect);

        Assert.Equal(SenkushaArrival.Connected, arrivals.Event(connected: true));
        Assert.True(arrivals.Flags.Finished);

        // The failure flag is written and the wait would not have ended on it.
        arrivals.BeginState(SenkushaState.TakionConnect);
        Assert.Equal(SenkushaArrival.Disconnected, arrivals.Event(connected: false));
        Assert.True(arrivals.Flags.Failed);
        Assert.False(SenkushaStates.WaitEnds(arrivals.Flags));

        // And anywhere else both are dropped on the floor.
        arrivals.BeginState(SenkushaState.ExpectBang);
        Assert.Equal(SenkushaArrival.Ignored, arrivals.Event(connected: true));
        Assert.Equal(SenkushaArrival.Ignored, arrivals.Event(connected: false));
        Assert.False(arrivals.Flags.Finished);
    }

    /// <summary>
    /// THE ACK ARM MATCHES THE SEQUENCE NUMBER, which is the arm the stream connection lacks.
    ///
    /// An ack for some other message arrives here too. Ending the wait on the first ack of any kind
    /// is right until a link loses one, and then it is a measurement of the wrong message.
    /// </summary>
    [Fact]
    public void AnAckEndsTheWaitOnlyForTheMessageItAcknowledges()
    {
        var arrivals = new SenkushaArrivals { DataAckSeqNumExpected = 4242 };
        arrivals.BeginState(SenkushaState.ExpectDataAck);

        Assert.Equal(SenkushaArrival.Ignored, arrivals.DataAck(4241));
        Assert.False(arrivals.Flags.Finished);

        Assert.Equal(SenkushaArrival.DataAcked, arrivals.DataAck(4242));
        Assert.True(arrivals.Flags.Finished);

        // And in any other state the right number is still nothing.
        var elsewhere = new SenkushaArrivals { DataAckSeqNumExpected = 4242 };
        elsewhere.BeginState(SenkushaState.ExpectBang);

        Assert.Equal(SenkushaArrival.Ignored, elsewhere.DataAck(4242));
    }

    /// <summary>
    /// THE SAME BYTES ARE THREE MESSAGES DEPENDING ON THE STATE, which is the third dispatch layer.
    ///
    /// One bang: it finishes the bang state, is refused in the protocol state, and reaches no arm at
    /// all while a pong is expected.
    /// </summary>
    [Fact]
    public void OneMessageIsAnsweredByTheStateAndNothingElse()
    {
        var arrivals = new SenkushaArrivals();

        arrivals.BeginState(SenkushaState.ExpectBang);
        Assert.Equal(SenkushaArrival.Banged, arrivals.Protobuf(Bang()));
        Assert.True(arrivals.Flags.Finished);

        arrivals.BeginState(SenkushaState.ExpectProtocolAck);
        Assert.Equal(SenkushaArrival.Refused, arrivals.Protobuf(Bang()));
        Assert.False(arrivals.Flags.Finished);

        Assert.Equal(SenkushaArrival.ProtocolAcked, arrivals.Protobuf(ProtocolAck()));
        Assert.True(arrivals.Flags.Finished);

        arrivals.BeginState(SenkushaState.ExpectPong);
        Assert.Equal(SenkushaArrival.Ignored, arrivals.Protobuf(Bang()));
    }

    /// <summary>
    /// AND THE SERVER'S OWN MTU COMMAND IS TOLERATED RATHER THAN REFUSED.
    ///
    /// The C says so in a comment - it may arrive here, it is ignored, and it is NOT an error. A
    /// port that reported it would be noisier than the C on a path that is working.
    /// </summary>
    [Fact]
    public void TheServersMtuCommandIsToleratedAndTheClientsIsMatchedById()
    {
        var arrivals = new SenkushaArrivals { MtuId = 3 };
        arrivals.BeginState(SenkushaState.ExpectClientMtuCommand);

        // Not an error and not an answer.
        Assert.Equal(SenkushaArrival.Ignored, arrivals.Protobuf(ServerMtu()));
        Assert.False(arrivals.Flags.Finished);

        // The client's own, under another id, is neither.
        Assert.Equal(SenkushaArrival.Refused, arrivals.Protobuf(ClientMtu(2)));
        Assert.False(arrivals.Flags.Finished);

        Assert.Equal(SenkushaArrival.ClientMtuCommanded, arrivals.Protobuf(ClientMtu(3)));
        Assert.True(arrivals.Flags.Finished);
    }

    /// <summary>
    /// THE TWO AV ARMS DISAGREE ABOUT is_video, which is the trap this file exists to pin.
    ///
    /// A pong is refused for BEING video and an MTU response for NOT being, three lines apart in one
    /// switch. Reading one rule for both answers every MTU probe as carried, and the port then
    /// measures a link nobody has and tells the console about it in a launch spec.
    /// </summary>
    [Fact]
    public void APongIsNotVideoAndAnMtuResponseIs()
    {
        var arrivals = new SenkushaArrivals
        {
            PingTag = 0xAABBCCDD,
            PingTestIndex = 0,
            PingIndex = 5,
            MtuId = 7,
        };

        arrivals.BeginState(SenkushaState.ExpectPong);

        // Video in the pong state is refused, whatever else matches.
        Assert.Equal(
            SenkushaArrival.Refused,
            arrivals.Av(isVideo: true, frameIndex: 0, unitIndex: 5, Pong(0xAABBCCDD), 1000));

        Assert.Equal(
            SenkushaArrival.Ponged,
            arrivals.Av(isVideo: false, frameIndex: 0, unitIndex: 5, Pong(0xAABBCCDD), 1000));

        Assert.Equal(1000ul, arrivals.PongTimeMicroseconds);

        // And NOT video in the MTU state is refused, which is the opposite rule.
        arrivals.BeginState(SenkushaState.ExpectMtu);

        Assert.Equal(
            SenkushaArrival.Refused,
            arrivals.Av(isVideo: false, frameIndex: 7, unitIndex: 0, [], 2000));

        Assert.Equal(
            SenkushaArrival.MtuCarried,
            arrivals.Av(isVideo: true, frameIndex: 7, unitIndex: 0, [], 2000));
    }

    /// <summary>
    /// A PONG IS MATCHED BY ITS TAG, ITS TWO INDICES AND ITS SIZE, and each refusal is its own.
    ///
    /// The size check is what keeps the tag read in bounds - eight bytes, which is where the tag
    /// ends. A port reading it from a shorter packet is reading past the payload.
    /// </summary>
    [Fact]
    public void APongIsRefusedByIndexBySizeAndByTag()
    {
        var arrivals = new SenkushaArrivals { PingTag = 0x11223344, PingTestIndex = 2, PingIndex = 9 };
        arrivals.BeginState(SenkushaState.ExpectPong);

        // The wrong test, the wrong ping, too short to hold a tag, and the wrong tag.
        Assert.Equal(SenkushaArrival.Refused, arrivals.Av(false, 3, 9, Pong(0x11223344), 1));
        Assert.Equal(SenkushaArrival.Refused, arrivals.Av(false, 2, 8, Pong(0x11223344), 1));
        Assert.Equal(SenkushaArrival.Refused, arrivals.Av(false, 2, 9, new byte[4], 1));
        Assert.Equal(SenkushaArrival.Refused, arrivals.Av(false, 2, 9, Pong(0x11223345), 1));

        Assert.False(arrivals.Flags.Finished);
        Assert.Equal(0, arrivals.Signalled);

        Assert.Equal(SenkushaArrival.Ponged, arrivals.Av(false, 2, 9, Pong(0x11223344), 77));
        Assert.Equal(1, arrivals.Signalled);
    }

    /// <summary>
    /// A WAIT ENDS WHEN AN ARRIVAL RAISES THE FLAG, from a thread that is not the waiter's.
    ///
    /// The part worth the trouble: in the C these flags are written on the takion's receive thread,
    /// and a wire that only worked when signalled from the waiting thread would be no wire at all.
    /// </summary>
    [Fact]
    public void AnArrivalOnAnotherThreadEndsTheWait()
    {
        var arrivals = new SenkushaArrivals();
        arrivals.BeginState(SenkushaState.ExpectBang);

        var signalling = new Thread(() =>
        {
            Thread.Sleep(20);
            arrivals.Protobuf(Bang());
        })
        {
            IsBackground = true,
            Name = "senkusha arrivals",
        };

        signalling.Start();

        (SenkushaWaitState flags, bool timedOut) = arrivals.Wait(SenkushaStates.ExpectTimeoutMs);

        signalling.Join(TimeSpan.FromSeconds(5));

        Assert.True(flags.Finished);
        Assert.False(timedOut);
    }

    /// <summary>And a wait nothing answers times out rather than hanging on its own deadline.</summary>
    [Fact]
    public void AWaitNobodyAnswersTimesOut()
    {
        var arrivals = new SenkushaArrivals();
        arrivals.BeginState(SenkushaState.ExpectBang);

        (SenkushaWaitState flags, bool timedOut) = arrivals.Wait(50);

        Assert.True(timedOut);
        Assert.False(flags.Finished);

        // And a stop ends it without anything arriving, which is the predicate's second field.
        arrivals.Stop();
        (SenkushaWaitState stopped, bool still) = arrivals.Wait(50);

        Assert.False(still);
        Assert.True(stopped.ShouldStop);
    }

    /// <summary>The four arms are the file's own, read rather than remembered.</summary>
    [Fact]
    public void TheArmsAreHeldAgainstTheFile()
    {
        if (Source() is not { } source)
            return;

        string callback = SenkushaArrivalsSource.ArmBody(source, "cb")!;
        string data = SenkushaArrivalsSource.ArmBody(source, "data")!;
        string ack = SenkushaArrivalsSource.ArmBody(source, "data_ack")!;
        string av = SenkushaArrivalsSource.ArmBody(source, "av")!;

        output.WriteLine($"cb {callback.Length}, data {data.Length}, ack {ack.Length}, av {av.Length}");

        Assert.True(SenkushaArrivalsSource.TheConnectAnswersAreStillStateGuarded(callback));
        Assert.True(SenkushaArrivalsSource.TheAckArmStillMatchesTheSequenceNumber(ack));
        Assert.True(SenkushaArrivalsSource.TheTwoAvArmsStillDisagreeAboutVideo(av));
        Assert.True(SenkushaArrivalsSource.ThePongTagIsStillReadAtFour(av));
        Assert.True(SenkushaArrivalsSource.TheServersMtuCommandIsStillTolerated(data));
    }
}
