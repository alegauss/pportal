using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP675: the three data datagrams, byte for byte against what takion.c writes.
///
/// Every send in takion.c ends in chiaki_takion_send_raw and nothing managed emitted a takion byte.
/// TakionDataSend scripts the failure order and sends nothing; these are the bytes under it.
///
/// THE THREE LOOK LIKE ONE AND DIFFER BY A BYTE. The DATA message adds nine before the payload, the
/// continuation eight - the same fields without the trailing zero - and the ack twelve with no
/// payload at all. A port that wrote nine for both would send a continuation the console reads one
/// byte out of step for the rest of the message, which is the failure this file exists against.
///
/// AND THE ROUND TRIP IS THE STRONGEST ASSERTION AVAILABLE. PP673's reader parses what these write:
/// a datagram built here goes back through TakionMessageIntake and comes out with the chunk type,
/// the flags and the payload length the builder was given. A wrong length field fails there rather
/// than being asserted about twice.
/// </summary>
public class TakionDataDatagramsTests(ITestOutputHelper output)
{
    private const uint TagRemote = 0x71DC1006;

    private static byte[] Payload(int size)
        => [.. Enumerable.Range(0, size).Select(i => (byte)(i + 0x30))];

    /// <summary>
    /// THE DATA MESSAGE: type byte, header, then sequence, channel, a zero word and a zero byte.
    ///
    /// Every field read back at its own offset rather than against a golden blob, so a failure
    /// names which one moved.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(1400)]
    public void TheDataMessageIsTheCsLayout(int payloadSize)
    {
        byte[] payload = Payload(payloadSize);
        byte[] datagram = new byte[TakionDataDatagrams.DataSize(payloadSize)];

        TakionDataDatagrams.WriteData(datagram, TagRemote, 0x1000, chunkFlags: 1, 0xABCDEF01, 3, payload);

        Assert.Equal(TakionMessageHeader.ControlPacketType, datagram[0]);
        Assert.Equal(1 + 0x10 + 9 + payloadSize, datagram.Length);

        Span<byte> body = datagram.AsSpan(TakionDataDatagrams.BodyOffset);

        Assert.Equal(0xABCDEF01u, BinaryPrimitives.ReadUInt32BigEndian(body));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(body[4..]));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(body[6..]));
        Assert.Equal(0, body[8]);
        Assert.Equal(payload, body[9..].ToArray());
    }

    /// <summary>
    /// THE CONTINUATION IS THE DATA MESSAGE MINUS ONE BYTE, and that byte is the whole difference.
    ///
    /// Asserted as a relation rather than as a second layout: the two share every field up to +8,
    /// and the payload starts one earlier. A builder that copied nine into both would fail here.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(1400)]
    public void TheContinuationIsOneByteShorterAndOtherwiseTheSame(int payloadSize)
    {
        byte[] payload = Payload(payloadSize);

        byte[] data = new byte[TakionDataDatagrams.DataSize(payloadSize)];
        byte[] cont = new byte[TakionDataDatagrams.ContinuationSize(payloadSize)];

        TakionDataDatagrams.WriteData(data, TagRemote, 0x1000, 1, 0x11223344, 5, payload);
        TakionDataDatagrams.WriteContinuation(cont, TagRemote, 0x1000, 1, 0x11223344, 5, payload);

        Assert.Equal(data.Length - 1, cont.Length);

        // Everything up to the DATA message's extra zero byte is identical but for the length field.
        Assert.Equal(
            data.AsSpan(TakionDataDatagrams.BodyOffset, 8).ToArray(),
            cont.AsSpan(TakionDataDatagrams.BodyOffset, 8).ToArray());

        // And the payload starts one earlier.
        Assert.Equal(payload, cont.AsSpan(TakionDataDatagrams.BodyOffset + 8).ToArray());

        // The length field follows it down, which is the half a byte-count check would miss.
        Assert.Equal(
            BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1 + TakionMessageHeader.SizeFieldOffset)) - 1,
            BinaryPrimitives.ReadUInt16BigEndian(cont.AsSpan(1 + TakionMessageHeader.SizeFieldOffset)));
    }

    /// <summary>THE ACK: twelve bytes, the sequence, the window, and two zero words.</summary>
    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(1u, 0x19000u)]
    [InlineData(0xFFFFFFFFu, 0xFFFFFFFFu)]
    public void TheAckIsTheCsLayout(uint seqNum, uint window)
    {
        byte[] datagram = new byte[TakionDataDatagrams.AckSize];

        TakionDataDatagrams.WriteAck(datagram, TagRemote, 0x2000, seqNum, window);

        Assert.Equal(1 + 0x10 + 0xc, datagram.Length);

        Span<byte> body = datagram.AsSpan(TakionDataDatagrams.BodyOffset);

        Assert.Equal(seqNum, BinaryPrimitives.ReadUInt32BigEndian(body));
        Assert.Equal(window, BinaryPrimitives.ReadUInt32BigEndian(body[4..]));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(body[8..]));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(body[0xa..]));
    }

    /// <summary>
    /// THE ROUND TRIP: what these write, PP673's reader reads back.
    ///
    /// The strongest assertion available without a console, and the one that catches a length field
    /// off by the addend - which is the mistake the header's own docstring says a reader invents.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(64)]
    [InlineData(1400)]
    public void WhatIsWrittenParsesBackAsWhatItIs(int payloadSize)
    {
        using var keyState = new KeyState();

        byte[] data = new byte[TakionDataDatagrams.DataSize(payloadSize)];
        TakionDataDatagrams.WriteData(data, TagRemote, 0, chunkFlags: 1, 7, 2, Payload(payloadSize));

        TakionMessageReading read = TakionMessageIntake.Read(data, TagRemote, keyState);

        output.WriteLine($"{payloadSize}-byte payload: {read.Verdict}, body {read.PayloadSize}");

        Assert.Equal(TakionMessageVerdict.Data, read.Verdict);
        Assert.Equal(TakionDataDatagrams.DataOverhead + payloadSize, read.PayloadSize);
        Assert.Equal(1, read.Header.ChunkFlags);

        // And the data push reads the entry back out of it.
        TakionDataPushReading pushed = TakionDataPush.Read(
            data, read.PayloadOffset, read.PayloadSize, read.Header.ChunkFlags);

        Assert.Equal(TakionDataPushVerdict.Pushed, pushed.Verdict);
        Assert.Equal(7u, pushed.Entry.SeqNum);
        Assert.Equal(2, pushed.Entry.Channel);
    }

    /// <summary>And an ack parses back as an ack, on the other arm of the same switch.</summary>
    [Fact]
    public void AnAckParsesBackAsAnAck()
    {
        using var keyState = new KeyState();

        byte[] ack = new byte[TakionDataDatagrams.AckSize];
        TakionDataDatagrams.WriteAck(ack, TagRemote, 0, 42, 0x19000);

        TakionMessageReading read = TakionMessageIntake.Read(ack, TagRemote, keyState);

        Assert.Equal(TakionMessageVerdict.DataAck, read.Verdict);

        // The body's own size comes back, addend and all handled by the two halves of the header:
        // the writer adds four and the reader takes four off. A first draft of this line subtracted
        // it a second time, which is the exact mistake TakionMessageHeader's docstring warns a
        // reader invents - and the round trip is what said so.
        Assert.Equal(TakionDataDatagrams.AckBodySize, read.PayloadSize);
    }

    /// <summary>
    /// The tag written is the RECEIVER's, so what the port sends is refused by the port's own reader.
    ///
    /// This is the C's rule seen from the sending side, and it is easy to write backwards: a builder
    /// that put tag_local in an outbound header would produce datagrams the console drops in
    /// silence. Here the round trip only works because the reader is told the same tag.
    /// </summary>
    [Fact]
    public void TheTagIsTheReceiversAndNotOurs()
    {
        using var keyState = new KeyState();

        byte[] ack = new byte[TakionDataDatagrams.AckSize];
        TakionDataDatagrams.WriteAck(ack, TagRemote, 0, 1, 1);

        Assert.Equal(TagRemote, BinaryPrimitives.ReadUInt32BigEndian(ack.AsSpan(1)));

        // Read as though we were the receiver of our own send: refused, because our tag is not this.
        Assert.Equal(
            TakionMessageVerdict.Refused,
            TakionMessageIntake.Read(ack, TagRemote + 1, keyState).Verdict);
    }

    /// <summary>A span of the wrong size is refused rather than half-written.</summary>
    [Fact]
    public void AWrongSizedSpanIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => TakionDataDatagrams.WriteData(new byte[10], TagRemote, 0, 1, 1, 1, Payload(4)));

        Assert.Throws<ArgumentException>(
            () => TakionDataDatagrams.WriteContinuation(new byte[10], TagRemote, 0, 1, 1, 1, Payload(4)));

        Assert.Throws<ArgumentException>(
            () => TakionDataDatagrams.WriteAck(new byte[10], TagRemote, 0, 1, 1));
    }

    /// <summary>The sizes are the C's arithmetic, stated so a reader can check them by eye.</summary>
    [Fact]
    public void TheSizesAreTheCsArithmetic()
    {
        Assert.Equal(9, TakionDataDatagrams.DataOverhead);
        Assert.Equal(8, TakionDataDatagrams.ContinuationOverhead);
        Assert.Equal(0xc, TakionDataDatagrams.AckBodySize);

        Assert.Equal(1 + 0x10 + 9, TakionDataDatagrams.DataSize(0));
        Assert.Equal(1 + 0x10 + 8, TakionDataDatagrams.ContinuationSize(0));
        Assert.Equal(1 + 0x10 + 0xc, TakionDataDatagrams.AckSize);
    }
}
