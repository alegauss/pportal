using System.Net;
using ChiakiNg.Protocol;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP805, under PP784: the walk from a datagram to one of senkusha's four arms.
///
/// PP791 wrote the arms and nothing fed them, so a run over a real takion would connect, send every
/// message correctly and end every wait in a timeout - the shape PP773 cost a console trial to find
/// one module over. These drive the router with datagrams written by the port's own writers, which
/// is the only way to assert the walk without a console.
/// </summary>
public class SenkushaRouterTests(ITestOutputHelper output)
{
    private const uint TagLocal = 0x5A5A5A5A;

    private static (SenkushaRouter Router, SenkushaArrivals Arrivals, ManagedTakion Takion) Router(
        Func<ulong>? clock = null)
    {
        var takion = new ManagedTakion(TagLocal);
        var arrivals = new SenkushaArrivals();

        return (new SenkushaRouter(arrivals, takion, clock), arrivals, takion);
    }

    /// <summary>One control datagram carrying a protobuf, as the console would send it.</summary>
    private static byte[] ProtobufDatagram(byte[] body, byte dataType = 0)
    {
        var datagram = new byte[TakionDataDatagrams.DataSize(body.Length)];

        // The tag is OURS: an inbound header carries the receiver's tag, and the intake refuses
        // every other one.
        TakionDataDatagrams.WriteData(
            datagram, TagLocal, keyPos: 0, chunkFlags: 0, seqNum: 1, channel: 1, body);

        // The data header's type byte, which WriteData leaves at PROTOBUF. Written through so a
        // test can ask what senkusha does with a type it has no arm for.
        datagram[datagram.Length - body.Length - 1] = dataType;

        return datagram;
    }

    private static byte[] ProtocolAck() => new Tkproto.TakionMessage
    {
        Type = Tkproto.TakionMessage.Types.PayloadType.Takionprotocolrequestack,
        TakionProtocolRequestAck =
            new Tkproto.TakionProtocolRequestAckPayload { TakionProtocolVersion = 7 },
    }.ToByteArray();

    /// <summary>
    /// THE PROTOBUF REACHES THE ARM, which is the whole of what was missing.
    ///
    /// Written by the port's own data writer and read back through the intake, the push and the
    /// drain header - so what is asserted is the walk and not a hand-built buffer that happens to
    /// match the reader.
    /// </summary>
    [Fact]
    public void AProtobufWalksAllTheWayToTheStateItEnds()
    {
        (SenkushaRouter router, SenkushaArrivals arrivals, ManagedTakion _) = Router();

        arrivals.BeginState(SenkushaState.ExpectProtocolAck);

        SenkushaRouting routing = router.Datagram(ProtobufDatagram(ProtocolAck()));

        output.WriteLine($"{routing}");

        Assert.Equal(SenkushaRoute.ToData, routing.Route);
        Assert.Equal(SenkushaArrival.ProtocolAcked, routing.Arrival);
        Assert.True(arrivals.Flags.Finished, "the wait was not ended");
        Assert.Equal(1, router.Handled);
    }

    /// <summary>
    /// A DATA TYPE THAT IS NOT PROTOBUF STOPS AT THE FIRST LINE, as senkusha_takion_data does.
    ///
    /// The stream's drain delivers four types; senkusha has an arm for one. A router that accepted
    /// the known set would hand the protobuf parser a rumble payload, and the parse failing is not
    /// the same as never having been attempted - it writes a log line about a message the console
    /// sent correctly.
    /// </summary>
    [Fact]
    public void ARumblePayloadIsNotOfferedToTheProtobufArm()
    {
        (SenkushaRouter router, SenkushaArrivals arrivals, ManagedTakion _) = Router();

        arrivals.BeginState(SenkushaState.ExpectProtocolAck);

        SenkushaRouting routing = router.Datagram(
            ProtobufDatagram(ProtocolAck(), (byte)TakionDataType.Rumble));

        Assert.Equal(SenkushaRoute.ToData, routing.Route);
        Assert.Equal(SenkushaArrival.Ignored, routing.Arrival);
        Assert.False(arrivals.Flags.Finished);
        Assert.Equal(0, router.Handled);
    }

    /// <summary>
    /// A PONG REACHES THE AV ARM with the clock read where the C reads it.
    ///
    /// The ping is formatted by the same writer the run sends, so the header this parses is the one
    /// the console would echo - and the round trip that comes out of it is the difference between
    /// this reading and the one the send took.
    /// </summary>
    [Fact]
    public void APongReachesTheAvArmAndCarriesTheArrivalTime()
    {
        (SenkushaRouter router, SenkushaArrivals arrivals, ManagedTakion _) = Router(clock: () => 4242);

        var pong = new byte[SenkushaMeasurements.PingBufferBytes];

        Assert.True(ManagedSenkushaRunHost.WritePing(pong, testIndex: 0, pingIndex: 3, tag: 0xC0FFEE, out _));

        arrivals.BeginState(SenkushaState.ExpectPong);
        arrivals.PingTestIndex = 0;
        arrivals.PingIndex = 3;
        arrivals.PingTag = 0xC0FFEE;

        SenkushaRouting routing = router.Datagram(pong);

        output.WriteLine($"{routing}, at {arrivals.PongTimeMicroseconds}us");

        Assert.Equal(SenkushaRoute.ToAv, routing.Route);
        Assert.Equal(SenkushaArrival.Ponged, routing.Arrival);
        Assert.Equal(4242ul, arrivals.PongTimeMicroseconds);
    }

    /// <summary>
    /// ONE ARM CALL PER RELEASED NUMBER, and never one for the cumulative one.
    ///
    /// The C's ack handler asks the send buffer what a cumulative number cleared and raises an event
    /// for each - so an ack clearing four held messages is four comparisons. A router passing the
    /// cumulative number instead is right only while nothing else is in flight, which is every gate
    /// and not every session.
    /// </summary>
    [Fact]
    public void EveryReleasedNumberIsComparedAndTheMatchingOneWins()
    {
        (SenkushaRouter router, SenkushaArrivals arrivals, ManagedTakion _) = Router();

        arrivals.BeginState(SenkushaState.ExpectDataAck);
        arrivals.DataAckSeqNumExpected = 7;

        // Four released, and the one senkusha is waiting for is not the last.
        Assert.Equal(SenkushaArrival.DataAcked, router.Acked([5, 6, 7, 8]));
        Assert.True(arrivals.Flags.Finished, "the ack that matched did not end the wait");
    }

    /// <summary>And an ack that released nothing senkusha wanted ends no wait.</summary>
    [Fact]
    public void ReleasedNumbersForOtherMessagesAreIgnored()
    {
        (SenkushaRouter router, SenkushaArrivals arrivals, ManagedTakion _) = Router();

        arrivals.BeginState(SenkushaState.ExpectDataAck);
        arrivals.DataAckSeqNumExpected = 7;

        Assert.Equal(SenkushaArrival.Ignored, router.Acked([5, 6, 8]));
        Assert.False(arrivals.Flags.Finished);
    }

    /// <summary>
    /// An ack before the takion connected releases nothing rather than throwing.
    ///
    /// A real arrival: the send buffer is built by the handshake, and a loop can be reading while
    /// that is still in flight. An exception on the receive thread would kill the loop, and a takion
    /// whose loop died looks exactly like a console that stopped answering.
    /// </summary>
    [Fact]
    public void AnAckWithNoSendBufferIsIgnoredRatherThanThrown()
    {
        (SenkushaRouter router, SenkushaArrivals _, ManagedTakion _) = Router();

        var ack = new byte[TakionDataDatagrams.AckSize];
        TakionDataDatagrams.WriteAck(ack, TagLocal, keyPos: 0, seqNum: 7, advertisedWindow: 0x19000);

        SenkushaRouting routing = router.Datagram(ack);

        Assert.Equal(SenkushaRoute.ToDataAck, routing.Route);
        Assert.Equal(SenkushaArrival.Ignored, routing.Arrival);
        Assert.Equal(0, routing.Released);
    }

    /// <summary>A datagram with no bytes is an arrival that reached nothing, and not a throw.</summary>
    [Fact]
    public void AnEmptyDatagramIsIgnored()
    {
        (SenkushaRouter router, SenkushaArrivals _, ManagedTakion _) = Router();

        SenkushaRouting routing = router.Datagram([]);

        Assert.Equal(SenkushaRoute.Ignored, routing.Route);
        Assert.Equal(-1, routing.BaseType);
        Assert.Equal(1, router.Seen);
        Assert.Equal(0, router.Handled);
    }

    /// <summary>
    /// THE KNOT IS TIED, which is the fact whose absence made every wait time out.
    ///
    /// Asserted through the takion's own dispatch rather than by reading the wiring back: what was
    /// missing was not an object but a CALL, and PP764's lesson is that a census counting types
    /// cannot see one. So this puts a datagram in where the receive loop puts one and asks the
    /// arrivals whether they heard it.
    /// </summary>
    [Fact]
    public void ATakionFromTheWiringDispatchesIntoTheArrivals()
    {
        (ManagedSenkushaRunHost host, SenkushaRouter router) = SenkushaRunWiring.Over(
            TagLocal, new IPEndPoint(IPAddress.Loopback, 9297));

        Assert.Same(host.Arrivals, router.Arrivals);

        host.Arrivals.BeginState(SenkushaState.ExpectProtocolAck);

        ((ITakionLoopHost)host.Takion).Dispatch(ProtobufDatagram(ProtocolAck()));

        Assert.Equal(1, router.Seen);
        Assert.Equal(SenkushaArrival.ProtocolAcked, router.Last?.Arrival);
        Assert.True(host.Arrivals.Flags.Finished, "the takion the wiring built reaches no arrivals");
    }
}
