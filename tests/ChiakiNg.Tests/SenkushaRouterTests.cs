using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
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
    /// PP813: HANDLED COUNTS A REFUSAL, and the whole live diagnosis rests on that.
    ///
    /// A live run answered Unknown with all ten pongs lost, and this is what let the cause be
    /// narrowed without a second run: Handled stood at four, the four control messages account for
    /// four on their own, and a pong REFUSED by the arm would have made it five. So nothing reached
    /// the arm - the delivery is the suspect and the five checks in it are not.
    ///
    /// The assertion is here rather than in the prose because the reasoning is only sound while
    /// Record keeps counting a refusal. Narrowing Handled to the arrivals that actually acted would
    /// be better behaviour and would silently invalidate the argument in PP813's own section.
    /// </summary>
    [Fact]
    public void HandledCountsARefusalWhichIsWhatMakesItLocateALostPong()
    {
        (SenkushaRouter router, SenkushaArrivals arrivals, ManagedTakion _) = Router();

        var pong = new byte[SenkushaMeasurements.PingBufferBytes];

        Assert.True(ManagedSenkushaRunHost.WritePing(pong, testIndex: 0, pingIndex: 3, tag: 0xC0FFEE, out _));

        arrivals.BeginState(SenkushaState.ExpectPong);
        arrivals.PingTestIndex = 0;
        arrivals.PingIndex = 3;

        // Everything matches except the tag, which is the arm's last check.
        arrivals.PingTag = 0xBADBAD;

        SenkushaRouting routing = router.Datagram(pong);

        output.WriteLine($"{routing}, handled {router.Handled}");

        Assert.Equal(SenkushaRoute.ToAv, routing.Route);
        Assert.Equal(SenkushaArrival.Refused, routing.Arrival);

        // The load-bearing one: a refusal is counted, so Handled staying low means the arm was
        // never reached.
        Assert.Equal(1, router.Handled);
    }

    /// <summary>
    /// PP813: AND THE CENSUS TELLS THE THREE APART, which Seen and Handled together cannot.
    ///
    /// A lost pong is one of three things: the arm refused the header, the datagram carried a base
    /// type this walk has no arm for, or nothing came back. The first two are one count each and
    /// neither was printed, so the live run could not say which - and a live run is the only place
    /// the answer exists.
    /// </summary>
    [Fact]
    public void TheCensusSeparatesARefusedHeaderFromAnUnarmedBaseType()
    {
        (SenkushaRouter router, SenkushaArrivals arrivals, ManagedTakion _) = Router();

        var pong = new byte[SenkushaMeasurements.PingBufferBytes];
        Assert.True(ManagedSenkushaRunHost.WritePing(pong, testIndex: 0, pingIndex: 0, tag: 1, out _));

        arrivals.BeginState(SenkushaState.ExpectPong);
        arrivals.PingTag = 2;

        router.Datagram(pong);

        // Base type 1: not control, not video, not audio - so this walk has no arm for it at all.
        router.Datagram([1, 0, 0, 0]);

        string census = router.Census();

        output.WriteLine(census);

        Assert.Contains("ToAv 1", census, StringComparison.Ordinal);
        Assert.Contains("Refused 1", census, StringComparison.Ordinal);
        Assert.Contains("Ignored 1", census, StringComparison.Ordinal);
        Assert.Contains("unarmed base types [1x1]", census, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP813: THE DATA ROUTE STOPS FOR THREE REASONS AND Ignored NAMES NONE OF THEM.
    ///
    /// A live run had fifty control datagrams take the data route and do nothing, which the census
    /// reported as ToData 50 with Ignored 50 - one column for three unrelated faults. The reorder
    /// queue releasing nothing, a data type this walk has no arm for, and a protobuf every arm
    /// declined are different bugs with different fixes, and the fifty are the standing lead.
    ///
    /// Two are driven here. NotPushed needs a queue that refuses, which this fixture cannot stage
    /// without reaching past the router - so the enum carries it and the two that a datagram can
    /// express are asserted.
    /// </summary>
    [Fact]
    public void TheDataRouteSaysWhereItGaveUp()
    {
        (SenkushaRouter router, SenkushaArrivals arrivals, ManagedTakion _) = Router();

        // A data type senkusha has no arm for at all: the walk stops before any protobuf is read.
        SenkushaRouting notProtobuf = router.Datagram(ProtobufDatagram(ProtocolAck(), dataType: 9));

        // And a protobuf the arms decline, because the state is not one that wants this message.
        arrivals.BeginState(SenkushaState.ExpectPong);
        SenkushaRouting noArm = router.Datagram(ProtobufDatagram(ProtocolAck()));

        string census = router.Census();

        output.WriteLine($"{notProtobuf}\n{noArm}\n{census}");

        Assert.Equal(SenkushaDataStop.NotProtobuf, notProtobuf.Stop);
        Assert.Equal(SenkushaDataStop.NoArm, noArm.Stop);

        // Both are Ignored, which is exactly why the stop column had to exist.
        Assert.Equal(SenkushaArrival.Ignored, notProtobuf.Arrival);
        Assert.Equal(SenkushaArrival.Ignored, noArm.Arrival);

        Assert.Contains("data stopped at [NotProtobuf 1, NoArm 1]", census, StringComparison.Ordinal);

        // None is every datagram that did not stop, so it must not appear in a failure column.
        Assert.DoesNotContain("None", census, StringComparison.Ordinal);
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

    /// <summary>
    /// PP808: AND THE EVENT THE KNOT DID NOT CARRY, which is the one arrival that is not a datagram.
    ///
    /// The test above puts a datagram where the receive loop puts one. This puts the takion's own
    /// death where takion_thread_func's `beach` puts it and asks the same object whether it heard.
    /// PP805 wired every datagram to an arm, and the arm that takes false was reached by tests and
    /// by nothing else - so a takion dying during the connect was noticed five seconds later, when
    /// the wait ran out.
    /// </summary>
    [Fact]
    public void ATakionFromTheWiringRaisesItsDisconnectIntoTheArrivals()
    {
        (ManagedSenkushaRunHost host, SenkushaRouter _) = SenkushaRunWiring.Over(
            TagLocal, new IPEndPoint(IPAddress.Loopback, 9297));

        host.Arrivals.BeginState(SenkushaState.TakionConnect);

        // Never connected, so the close is the only `beach` this takion has - which is the C's
        // failed handshake, the path that raises DISCONNECT having never raised CONNECTED.
        host.Takion.Dispose();

        Assert.True(host.Takion.RaisedDisconnected);
        Assert.True(
            host.Arrivals.Flags.Failed, "the takion the wiring built tells no arrivals it died");
        Assert.False(host.Arrivals.Flags.Finished);
    }

    /// <summary>
    /// A takion connected to this test's own socket, so its data queue and its wire are real.
    /// </summary>
    /// <remarks>
    /// PP807's criterion is about the QUEUE, and an unconnected takion has none - so the ack tests
    /// below cannot use <see cref="Router"/>. The responder answers on its own thread because the
    /// connect blocks through the handshake on this one.
    /// </remarks>
    private static (SenkushaRouter Router, ManagedTakion Takion, UdpClient Peer) Connected(uint tagRemote)
    {
        var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var responder = new TakionHandshakeResponder(tagRemote, [.. Enumerable.Repeat((byte)0x5c, 32)]);

        var answering = new Thread(() =>
        {
            var from = new IPEndPoint(IPAddress.Loopback, 0);

            while (responder.State != TakionResponderState.Done)
            {
                byte[] datagram = peer.Receive(ref from);

                if (responder.Answer(datagram) is { } answer)
                    peer.Send(answer, answer.Length, from);
            }
        })
        {
            IsBackground = true,
            Name = "senkusha peer",
        };

        answering.Start();

        var takion = new ManagedTakion(TagLocal);

        Assert.Equal(
            ChiakiError.Success,
            takion.Connect((IPEndPoint)peer.Client.LocalEndPoint!, expectTimeoutMs: 2000).Error);

        answering.Join(TimeSpan.FromSeconds(5));
        peer.Client.ReceiveTimeout = 2000;

        return (new SenkushaRouter(new SenkushaArrivals(), takion), takion, peer);
    }

    /// <summary>One control datagram carrying a protobuf, at a chosen sequence number.</summary>
    private static byte[] ProtobufDatagramAt(uint seqNum, byte[] body, byte dataType = 0)
    {
        var datagram = new byte[TakionDataDatagrams.DataSize(body.Length)];

        TakionDataDatagrams.WriteData(
            datagram, TagLocal, keyPos: 0, chunkFlags: 0, seqNum: seqNum, channel: 1, body);

        datagram[datagram.Length - body.Length - 1] = dataType;

        return datagram;
    }

    /// <summary>
    /// PP807: SENKUSHA'S ROUTER ACKNOWLEDGES WHAT ITS OWN QUEUE RELEASES, over a real socket.
    ///
    /// The mechanism landed with no caller and this is the caller. takion.c acks from
    /// takion_flush_data_queue, ABOVE the senkusha callback, so this is senkusha's behaviour without
    /// senkusha.c mentioning it - which is exactly why nothing here had been written.
    ///
    /// The queue is seeded from the REMOTE tag, so the number that releases is the responder's.
    /// </summary>
    [Fact]
    public void ADeliveredProtobufIsAcknowledgedOnTheWire()
    {
        const uint TagRemote = 0x71DC_1006;

        (SenkushaRouter router, ManagedTakion takion, UdpClient peer) = Connected(TagRemote);

        using (takion)
        using (peer)
        {
            router.Arrivals.BeginState(SenkushaState.ExpectProtocolAck);

            SenkushaRouting routing = router.Datagram(ProtobufDatagramAt(TagRemote, ProtocolAck()));

            output.WriteLine($"{routing}, {takion.DataAckSent} ack(s)");

            Assert.Equal(SenkushaArrival.ProtocolAcked, routing.Arrival);
            Assert.Equal(1, takion.DataAckSent);

            // And it is an ack this port's own reader accepts, off the socket rather than off a
            // buffer - which is the join PP675's writer never had on this side.
            var from = new IPEndPoint(IPAddress.Loopback, 0);
            byte[] onTheWire = peer.Receive(ref from);

            using var keyState = new KeyState();
            TakionMessageReading read = TakionMessageIntake.Read(onTheWire, TagRemote, keyState);

            Assert.Equal(TakionMessageVerdict.DataAck, read.Verdict);
        }
    }

    /// <summary>
    /// AND A TYPE SENKUSHA HAS NO ARM FOR IS ACKNOWLEDGED TOO, which is the easy half to get wrong.
    ///
    /// The C's flag is set on the PULL and its send sits below the whole loop, so the ack does not
    /// depend on what the callback made of the message. A router that returned early on the type
    /// check would ack only what it understood - a rule the C does not have, and one an ordered
    /// link carrying only protobufs would never show.
    /// </summary>
    [Fact]
    public void AMessageWithNoArmIsAcknowledgedAsWell()
    {
        const uint TagRemote = 0x71DC_2007;

        (SenkushaRouter router, ManagedTakion takion, UdpClient peer) = Connected(TagRemote);

        using (takion)
        using (peer)
        {
            router.Arrivals.BeginState(SenkushaState.ExpectProtocolAck);

            SenkushaRouting routing = router.Datagram(
                ProtobufDatagramAt(TagRemote, ProtocolAck(), (byte)TakionDataType.Rumble));

            output.WriteLine($"{routing}, {takion.DataAckSent} ack(s)");

            Assert.Equal(SenkushaDataStop.NotProtobuf, routing.Stop);
            Assert.Equal(SenkushaArrival.Ignored, routing.Arrival);
            Assert.Equal(1, takion.DataAckSent);
        }
    }

    /// <summary>
    /// AND A MESSAGE ARRIVING INTO A GAP IS ACKNOWLEDGED BY NOTHING, which is the queue deciding.
    ///
    /// One ahead of what the queue is waiting for is held back, so no pull succeeds and the C sends
    /// no ack. The filler then releases both and ONE ack goes out carrying the LAST number - which
    /// is the rule a router that acked each arrival would have broken while looking identical on an
    /// ordered link.
    /// </summary>
    [Fact]
    public void AMessageIntoAGapIsNotAcknowledgedUntilTheGapFills()
    {
        const uint TagRemote = 0x71DC_3008;

        (SenkushaRouter router, ManagedTakion takion, UdpClient peer) = Connected(TagRemote);

        using (takion)
        using (peer)
        {
            router.Arrivals.BeginState(SenkushaState.ExpectProtocolAck);

            router.Datagram(ProtobufDatagramAt(TagRemote + 1, ProtocolAck()));
            Assert.Equal(0, takion.DataAckSent);

            router.Datagram(ProtobufDatagramAt(TagRemote, ProtocolAck()));

            output.WriteLine($"{takion.DataAckSent} ack(s) after the gap filled");

            Assert.Equal(1, takion.DataAckSent);
        }
    }
}
