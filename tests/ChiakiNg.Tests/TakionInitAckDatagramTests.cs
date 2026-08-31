using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP603, under PP27: the INIT_ACK a responder sends, field by field.
///
/// PP601 opened the socket and PP602 said the far end must answer rather than replay. This is the
/// first datagram of that answer, and it is checked against the offsets takion_recv_message_init_ack
/// reads - because a responder that is one byte out does not fail loudly, it times out three times
/// and reports that the console never acked.
/// </summary>
public class TakionInitAckDatagramTests
{
    private static readonly byte[] Cookie =
        [.. Enumerable.Range(0, TakionHandshake.CookieSize).Select(i => (byte)(0xC0 + i))];

    private static byte[] Written(uint tagLocal = 0x11223344)
        => TakionInitAckDatagram.Write(
            tagLocal,
            new TakionInitAck(
                Tag: 0x55667788,
                ARwnd: TakionHandshake.ARwnd,
                OutboundStreams: TakionHandshake.OutboundStreams,
                InboundStreams: TakionHandshake.InboundStreams,
                InitialSeqNum: 0x99AABBCC),
            Cookie);

    /// <summary>It is exactly the size takion refuses anything else for.</summary>
    [Fact]
    public void ItIsExactlyTheSizeTakionExpects()
    {
        Assert.Equal(TakionHandshake.InitAckDatagramSize, Written().Length);

        // And the sizes agree with each other rather than being typed twice.
        Assert.Equal(
            1 + TakionHandshake.MessageHeaderSize + TakionInitAckDatagram.PayloadSize,
            TakionHandshake.InitAckDatagramSize);
    }

    /// <summary>
    /// The header echoes the CLIENT's tag, which is the part that reads backwards.
    ///
    /// takion_parse_message refuses a message whose header tag is not tag_local, so the responder
    /// answers with the tag it was sent and puts its own in the payload.
    /// </summary>
    [Fact]
    public void TheHeaderEchoesTheClientsTagAndThePayloadCarriesThePeers()
    {
        byte[] datagram = Written(0x11223344);

        Assert.Equal(
            0x11223344u,
            BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(TakionInitAckDatagram.HeaderOffset)));

        Assert.Equal(
            0x55667788u,
            BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(TakionInitAckDatagram.PayloadOffset)));
    }

    /// <summary>The packet type, the chunk type and its flags are where takion looks for them.</summary>
    [Fact]
    public void TheTypeBytesAreWhereTakionReadsThem()
    {
        byte[] datagram = Written();

        Assert.Equal(TakionInitAckDatagram.ControlPacketType, datagram[0]);
        Assert.Equal(
            TakionInitAckDatagram.InitAckChunkType,
            datagram[TakionHandshake.ChunkTypeOffsetInDatagram]);
        Assert.Equal(
            TakionInitAckDatagram.NoChunkFlags,
            datagram[TakionHandshake.ChunkTypeOffsetInDatagram + 1]);
    }

    /// <summary>
    /// The size field carries the payload PLUS FOUR, which is the mistake that costs three timeouts.
    ///
    /// takion_write_message_header writes payload_data_size + 4 and the parse checks the result
    /// against 0x10 + TAKION_COOKIE_SIZE. A responder writing the bare size is refused, and the
    /// client's only report is that no ack arrived.
    /// </summary>
    [Fact]
    public void TheSizeFieldCarriesThePayloadPlusFour()
    {
        byte[] datagram = Written();

        ushort stated = BinaryPrimitives.ReadUInt16BigEndian(
            datagram.AsSpan(TakionInitAckDatagram.HeaderOffset + 0xe));

        Assert.Equal(TakionInitAckDatagram.PayloadSize + TakionInitAckDatagram.SizeFieldAddend, stated);
        Assert.Equal(0x34, stated);
    }

    /// <summary>The four MAC bytes are zero: the handshake runs before crypt exists.</summary>
    [Fact]
    public void TheMacBytesAreZeroBecauseCryptIsNotThereYet()
    {
        byte[] datagram = Written();

        Assert.All(
            datagram.AsSpan(TakionInitAckDatagram.HeaderOffset + 4, TakionPacketMac.GmacSize).ToArray(),
            b => Assert.Equal(0, b));
    }

    /// <summary>The five payload fields, at the offsets the C reads them from.</summary>
    [Fact]
    public void ThePayloadFieldsAreAtTheOffsetsTheCReads()
    {
        ReadOnlySpan<byte> body = Written().AsSpan(TakionInitAckDatagram.PayloadOffset);

        Assert.Equal(TakionHandshake.ARwnd, BinaryPrimitives.ReadUInt32BigEndian(body[4..]));
        Assert.Equal(TakionHandshake.OutboundStreams, BinaryPrimitives.ReadUInt16BigEndian(body[8..]));
        Assert.Equal(TakionHandshake.InboundStreams, BinaryPrimitives.ReadUInt16BigEndian(body[0xa..]));
        Assert.Equal(0x99AABBCCu, BinaryPrimitives.ReadUInt32BigEndian(body[0xc..]));
        Assert.Equal(Cookie, body[0x10..].ToArray());
    }

    /// <summary>
    /// And what it writes passes the model's own gate, so the two halves agree.
    ///
    /// TakionHandshake.Check is what the client applies to the ack it parses; a responder whose
    /// stream counts or tag would be refused there is one that produces three timeouts.
    /// </summary>
    [Fact]
    public void WhatItWritesPassesTheModelsGate()
    {
        var answer = new TakionInitAck(
            Tag: 0x55667788,
            ARwnd: TakionHandshake.ARwnd,
            OutboundStreams: TakionHandshake.OutboundStreams,
            InboundStreams: TakionHandshake.InboundStreams,
            InitialSeqNum: 0x99AABBCC);

        Assert.Equal(TakionInitAckVerdict.Accepted, TakionHandshake.Check(answer));
    }

    /// <summary>A cookie of the wrong length is refused rather than truncated.</summary>
    [Fact]
    public void AWrongLengthCookieIsRefused()
    {
        Assert.Throws<ArgumentException>(() => TakionInitAckDatagram.Write(
            1, new TakionInitAck(2, 3, 4, 5, 6), new byte[TakionHandshake.CookieSize - 1]));
    }

    /// <summary>
    /// And takion.c still writes the header the way this reads it.
    ///
    /// The join back to the C. Every offset above came out of takion_write_message_header once, and
    /// a header whose fields moved would leave this producing a datagram the C rejects for a reason
    /// nothing here would name.
    /// </summary>
    [Fact]
    public void TheCStillWritesTheHeaderThisWay()
    {
        if (TakionInitAckDatagram.LocateSource() is not { } path)
            return;

        Assert.True(
            TakionInitAckDatagram.TheHeaderIsStillWrittenThisWay(File.ReadAllText(path)),
            "takion_write_message_header no longer places the chunk type at +0xc or the size at "
                + "+0xe with its addend, so this writer is producing a header the C will refuse");
    }
}
