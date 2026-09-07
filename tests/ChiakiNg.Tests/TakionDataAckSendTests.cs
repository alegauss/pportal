using System.Buffers.Binary;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP807: the ack this port only ever received, sent - and the queue deciding when it is.
///
/// PP672 wrote WriteAck and nothing called it outside a test. Every ack in the tree went the other
/// way, PP494 reading what the console sends and PP805 handing what it released to senkusha's arm,
/// so the port answered nothing and what that cost was the console's retry policy rather than
/// anything here.
///
/// WHAT IS ASSERTED IS THE DECISION AND NOT THE BYTES. PP675 already holds the layout and the round
/// trip through PP673's reader; these hold the two things that were missing. First, that the flag is
/// the QUEUE's - a message arriving into a gap is acknowledged by nothing, a flush that pulls two
/// sends one ack carrying the LAST of them, and a duplicate is acknowledged by nothing. Second, that
/// the live stream path reaches it at all, including on the two arms that hand nothing on: the C's
/// flag is set on the pull, above every check, so a drain that delivered nothing still acknowledges
/// what it pulled.
///
/// AND THE ONE THING PP675 STOPPED SHORT OF: what this port sends, this port's own reader accepts.
/// The window is the constant the INIT advertised, the gap-ack count is zero, and PP494's arithmetic
/// says a count above zero could never have been in twelve bytes anyway.
/// </summary>
public class TakionDataAckSendTests(ITestOutputHelper output)
{
    /// <summary>A tag-shaped seed, which is what takion hands the data queue.</summary>
    private const uint Seed = 0x71DC_1006;

    private static ReorderQueue Queue(uint seed = Seed)
        => ReorderQueue.Wide(ManagedTakion.ReorderQueueSizeExp, seed);

    /// <summary>
    /// THE FLAG IS THE QUEUE'S AND NOT THE ARRIVAL'S, which is the whole of this line.
    ///
    /// Three arrivals in the order that separates the two readings. The first is what the queue was
    /// waiting for and is acknowledged; the third arrives with the second still missing and is
    /// acknowledged by NOTHING, because the C's ack comes out of a pull that did not happen; the
    /// second then fills the gap and one ack goes out carrying the third's number, not the second's.
    ///
    /// A port that acknowledged each arrival would answer three times and would have told the
    /// console a gap had arrived - identical behaviour on an ordered link, which is why this is
    /// asserted over a reordered one.
    /// </summary>
    [Fact]
    public void TheQueueDecidesWhetherAnArrivalIsAcknowledged()
    {
        using ReorderQueue queue = Queue();
        List<uint> sent = [];

        TakionAckSendReading first = TakionDataAckSend.Arrival(queue, Seed, Record(sent));
        TakionAckSendReading ahead = TakionDataAckSend.Arrival(queue, Seed + 2, Record(sent));
        TakionAckSendReading filler = TakionDataAckSend.Arrival(queue, Seed + 1, Record(sent));

        output.WriteLine($"{first}\n{ahead}\n{filler}\nsent {string.Join(", ", sent)}");

        Assert.Equal(TakionAckSendVerdict.Sent, first.Verdict);
        Assert.Equal(Seed, first.SeqNum);
        Assert.Equal(1, first.Released);

        // The one that matters: pushed, held back, and answered by nothing at all.
        Assert.Equal(TakionAckSendVerdict.HeldBack, ahead.Verdict);
        Assert.Equal(0, ahead.Released);

        // And the gap filling releases both, for ONE ack carrying the higher number.
        Assert.Equal(TakionAckSendVerdict.Sent, filler.Verdict);
        Assert.Equal(Seed + 2, filler.SeqNum);
        Assert.Equal(2, filler.Released);

        Assert.Equal([Seed, Seed + 2], sent);
    }

    /// <summary>
    /// A RETRANSMISSION IS ACKNOWLEDGED BY NOTHING, because the queue calls it a duplicate.
    ///
    /// The number is behind the window's start once it has been pulled, so the push drops it and the
    /// pull that follows finds an empty queue. Worth its own assertion: this is the case a console
    /// with no ack produces, so it is the one an ack path is most likely to meet first.
    /// </summary>
    [Fact]
    public void AlreadyReleasedNumbersAreAcknowledgedByNothing()
    {
        using ReorderQueue queue = Queue();
        List<uint> sent = [];

        Assert.Equal(TakionAckSendVerdict.Sent, TakionDataAckSend.Arrival(queue, Seed, Record(sent)).Verdict);

        TakionAckSendReading again = TakionDataAckSend.Arrival(queue, Seed, Record(sent));

        output.WriteLine($"{again}, {queue.Drops.Count} dropped");

        Assert.Equal(TakionAckSendVerdict.HeldBack, again.Verdict);
        Assert.Single(queue.Drops);
        Assert.Equal([Seed], sent);
    }

    /// <summary>
    /// A REFUSED SEND IS REPORTED AND THE QUEUE HAS ALREADY MOVED.
    ///
    /// The order is the C's: the pull happens, then the send. So a socket that refuses the ack does
    /// not put the sequence number back - the next arrival is pulled from where this one left the
    /// window, and the console is left with an acknowledgement that never arrived rather than one
    /// that will be repeated.
    /// </summary>
    [Fact]
    public void ARefusedSendLeavesTheQueueWhereItPulledTo()
    {
        using ReorderQueue queue = Queue();

        TakionAckSendReading refused =
            TakionDataAckSend.Arrival(queue, Seed, _ => ChiakiError.Network);

        output.WriteLine($"{refused}");

        Assert.Equal(TakionAckSendVerdict.SendRefused, refused.Verdict);
        Assert.Equal(ChiakiError.Network, refused.Error);
        Assert.Equal(Seed, refused.SeqNum);

        // Moved: the next number in sequence is what the window is waiting for now.
        List<uint> sent = [];
        Assert.Equal(
            TakionAckSendVerdict.Sent, TakionDataAckSend.Arrival(queue, Seed + 1, Record(sent)).Verdict);
        Assert.Equal([Seed + 1], sent);
    }

    /// <summary>
    /// A takion with no queue and no socket acknowledges nothing, and says which of the two it is.
    ///
    /// Both are states a live run passes through rather than errors: the receive loop can be reading
    /// before the handshake's answer has been acted on, which is the argument PP805's own ack arm
    /// makes for the same check one direction over.
    /// </summary>
    [Fact]
    public void ATakionThatHasNotConnectedAcknowledgesNothing()
    {
        using var takion = new ManagedTakion(0x0000_7773);

        TakionAckSendReading reading = takion.AckData(1);

        output.WriteLine($"{reading}, {takion.DataAckSent} sent");

        Assert.Equal(TakionAckSendVerdict.NoQueue, reading.Verdict);
        Assert.Equal(ChiakiError.Uninitialized, reading.Error);

        // And the send itself, which is the other half of the same absence.
        Assert.Equal(ChiakiError.Uninitialized, takion.SendDataAck(1));
        Assert.Equal(0, takion.DataAckSent);
    }

    /// <summary>
    /// WHAT THIS PORT SENDS, THIS PORT'S OWN READER ACCEPTS - which PP675 stopped one call short of.
    ///
    /// That line read the ack back through PP673's message layer and asserted the arm and the body
    /// size. This carries it into PP494's reader, which is the object the console's own ack goes
    /// through: the verdict is Accepted, the cumulative number is the one the queue released, and the
    /// window is the constant the INIT advertised rather than a number chosen here.
    /// </summary>
    [Fact]
    public void TheAckSentIsTheOneThisPortsReaderAccepts()
    {
        const uint tagRemote = 0x0000_7774;
        var ledger = new ManagedKeyState();

        // Built exactly as ManagedTakion.SendDataAck builds it, constant included.
        byte[] datagram = new byte[TakionDataDatagrams.AckSize];
        TakionDataDatagrams.WriteAck(datagram, tagRemote, keyPos: 0, seqNum: Seed, TakionHandshake.ARwnd);

        TakionMessageReading message = TakionMessageIntake.Read(datagram, tagRemote, ledger);
        TakionAckRead read = TakionDataAck.Read(
            datagram.AsSpan(message.PayloadOffset, message.PayloadSize));

        output.WriteLine($"{message.Verdict}: {read.Verdict}, {read.Fields}");

        Assert.Equal(TakionMessageVerdict.DataAck, message.Verdict);
        Assert.Equal(TakionAckVerdict.Accepted, read.Verdict);
        Assert.Equal(Seed, read.Fields.CumulativeSeqNum);
        Assert.Equal(TakionHandshake.ARwnd, read.Fields.AdvertisedWindow);
        Assert.Equal(0, read.Fields.GapAckBlocks);
    }

    /// <summary>
    /// THE STREAM PATH REACHES IT, AND ON THE ARMS THAT DELIVER NOTHING TOO.
    ///
    /// The C's flag is set the moment a pull succeeds - above the size check, above the type check,
    /// above the test for a callback - so a drain that handed nothing on still acknowledges what it
    /// pulled. That is right for a transport, whose ack means "arrived" and not "understood", and it
    /// is exactly the shape a reader tidies into an ack per delivered message.
    ///
    /// So three datagrams: a rumble, which is one of the four and is delivered; an unknown data type,
    /// which is hexdumped and dropped; and a payload under the nine-byte header, which the C frees
    /// and returns from BEFORE the push. The first two are acknowledged and the third is not.
    /// </summary>
    [Fact]
    public void TheStreamPathAcknowledgesEveryMessageThatWasPushed()
    {
        const uint tagLocal = 0x0000_7773;

        var sink = new Acking(Seed);
        var messages = new StreamArrivalsTests.Sent();
        ManagedStreamRunHost host = StreamArrivalsTests.HostOn(new ManagedTakion(tagLocal), messages);

        var arrivals = new StreamArrivals(host, messages)
        {
            TagLocal = tagLocal,
            Ledger = new ManagedKeyState(),
            Acks = sink,
        };

        ArrivalReading known = arrivals.Datagram(
            DataDatagram(tagLocal, Seed, (byte)TakionDataType.Rumble));

        ArrivalReading unknown = arrivals.Datagram(
            DataDatagram(tagLocal, Seed + 1, dataType: 0x5));

        ArrivalReading tooShort = arrivals.Datagram(ShortDataDatagram(tagLocal));

        output.WriteLine(
            $"{known.Route}/{known.Data}, {unknown.Route}, {tooShort.Route}: "
                + $"acked {string.Join(", ", sink.Sent)}");

        // Both of the first two, and the unknown type is the one a tidier port would have dropped.
        Assert.Equal([Seed, Seed + 1], sink.Sent);

        // The short one never reached the queue: the C frees the datagram and returns above the push,
        // so there is no entry to release and nothing to acknowledge.
        Assert.Equal(TakionRoute.ToData, tooShort.Route);
        Assert.Equal(2, sink.Sent.Count);
    }

    /// <summary>A send that records what it was asked to acknowledge and always succeeds.</summary>
    private static Func<uint, ChiakiError> Record(List<uint> into) => seqNum =>
    {
        into.Add(seqNum);
        return ChiakiError.Success;
    };

    /// <summary>
    /// The sink a router holds, over a queue of its own - which is what a takion's is.
    /// </summary>
    private sealed class Acking(uint seed) : IDataAckSink
    {
        private readonly ReorderQueue queue = ReorderQueue.Wide(ManagedTakion.ReorderQueueSizeExp, seed);

        public List<uint> Sent { get; } = [];

        public TakionAckSendReading AckData(uint seqNum)
            => TakionDataAckSend.Arrival(queue, seqNum, one =>
            {
                Sent.Add(one);
                return ChiakiError.Success;
            });
    }

    /// <summary>One DATA message carrying a chosen sequence number and data type.</summary>
    private static byte[] DataDatagram(uint tag, uint seqNum, byte dataType, int bodySize = 4)
    {
        int payloadSize = TakionDataPush.DataHeaderSize + bodySize;
        byte[] datagram = new byte[1 + TakionHandshake.MessageHeaderSize + payloadSize];

        datagram[0] = TakionMessageHeader.ControlPacketType;

        TakionMessageHeader.Write(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize),
            tag,
            keyPos: 0,
            TakionMessageIntake.DataChunkType,
            TakionDataPush.ExpectedTypeB,
            payloadSize);

        Span<byte> payload = datagram.AsSpan(1 + TakionHandshake.MessageHeaderSize);

        BinaryPrimitives.WriteUInt32BigEndian(payload[TakionDataPush.SeqNumOffset..], seqNum);
        payload[TakionDataDrain.DataTypeOffset] = dataType;

        return datagram;
    }

    /// <summary>
    /// The reachable short one: a tagged control message whose payload is under nine bytes.
    ///
    /// PP491's leak seen from here. The parse forces the payload to agree with the datagram, so this
    /// is 21 bytes whole - inside the 17-to-25 window the C's own comment names.
    /// </summary>
    private static byte[] ShortDataDatagram(uint tag)
    {
        const int payloadSize = 4;
        byte[] datagram = new byte[1 + TakionHandshake.MessageHeaderSize + payloadSize];

        datagram[0] = TakionMessageHeader.ControlPacketType;

        TakionMessageHeader.Write(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize),
            tag,
            keyPos: 0,
            TakionMessageIntake.DataChunkType,
            TakionDataPush.ExpectedTypeB,
            payloadSize);

        return datagram;
    }
}
