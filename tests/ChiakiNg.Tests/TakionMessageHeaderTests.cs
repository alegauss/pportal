using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP604, under PP27: one header writer for both of the responder's answers, and the COOKIE_ACK.
///
/// PP603 wrote the INIT_ACK with the field placement inside it, which was right for one message and
/// wrong for two. takion.c has one writer and calls it for the INIT, the COOKIE, the INIT_ACK and
/// the COOKIE_ACK, so a managed side with two copies is one edit away from two answers disagreeing
/// about where the chunk type sits.
/// </summary>
public class TakionMessageHeaderTests
{
    /// <summary>The COOKIE_ACK is the header and nothing else, at the size the C reads.</summary>
    [Fact]
    public void TheCookieAckIsTheHeaderAlone()
    {
        byte[] datagram = TakionMessageHeader.WriteCookieAck(0x11223344);

        Assert.Equal(TakionHandshake.CookieAckDatagramSize, datagram.Length);
        Assert.Equal(1 + TakionHandshake.MessageHeaderSize, datagram.Length);
        Assert.Equal(TakionMessageHeader.ControlPacketType, datagram[0]);
    }

    /// <summary>
    /// Its chunk type is 0xb at datagram offset 0xd, which is the byte PP451 made safe to read.
    /// </summary>
    [Fact]
    public void TheCookieAckNamesItselfWhereTheCLooks()
    {
        byte[] datagram = TakionMessageHeader.WriteCookieAck(1);

        Assert.Equal(
            TakionMessageHeader.CookieAckChunkType,
            datagram[TakionHandshake.ChunkTypeOffsetInDatagram]);

        Assert.Equal(0xb, TakionMessageHeader.CookieAckChunkType);
        Assert.Equal(
            TakionMessageHeader.NoChunkFlags,
            datagram[TakionHandshake.ChunkTypeOffsetInDatagram + 1]);
    }

    /// <summary>
    /// Its length field carries the addend alone, because the payload is empty.
    ///
    /// takion_recv_message_cookie_ack asserts payload_size is zero, so the four is the whole of what
    /// the field says - and a responder writing zero there is refused.
    /// </summary>
    [Fact]
    public void TheCookieAcksLengthIsTheAddendAlone()
    {
        byte[] datagram = TakionMessageHeader.WriteCookieAck(1);

        ushort stated = BinaryPrimitives.ReadUInt16BigEndian(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram + TakionMessageHeader.SizeFieldOffset));

        Assert.Equal(TakionMessageHeader.SizeFieldAddend, stated);
        Assert.Equal(4, stated);
    }

    /// <summary>And it echoes the client's tag, as everything sent to the client must.</summary>
    [Fact]
    public void TheCookieAckEchoesTheClientsTag()
    {
        byte[] datagram = TakionMessageHeader.WriteCookieAck(0xDEADBEEF);

        Assert.Equal(
            0xDEADBEEFu,
            BinaryPrimitives.ReadUInt32BigEndian(
                datagram.AsSpan(TakionMessageHeader.OffsetInDatagram + TakionMessageHeader.TagOffset)));
    }

    /// <summary>
    /// THE JOIN THAT MATTERS: the INIT_ACK is written through this same writer.
    ///
    /// Not "the two agree" as a sentence - the INIT_ACK's header bytes are compared against what
    /// this produces for the same inputs, so a divergence is a failure rather than a review note.
    /// </summary>
    [Fact]
    public void TheInitAckUsesThisWriter()
    {
        byte[] initAck = TakionInitAckDatagram.Write(
            0x11223344,
            new TakionInitAck(1, 2, 3, 4, 5),
            new byte[TakionHandshake.CookieSize]);

        byte[] expected = new byte[TakionHandshake.MessageHeaderSize];
        TakionMessageHeader.Write(
            expected, 0x11223344, keyPos: 0,
            TakionMessageHeader.InitAckChunkType, TakionMessageHeader.NoChunkFlags,
            TakionInitAckDatagram.PayloadSize);

        Assert.Equal(
            expected,
            initAck.AsSpan(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize).ToArray());
    }

    /// <summary>A span that is not a header's length is refused rather than partly written.</summary>
    [Fact]
    public void AWrongLengthHeaderIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            TakionMessageHeader.Write(new byte[4], 1, 0, 1, 0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TakionMessageHeader.Write(
                new byte[TakionHandshake.MessageHeaderSize], 1, 0, 1, 0, -1));
    }

    /// <summary>
    /// The four chunk types are the C's, so a responder answering the wrong one is caught here and
    /// not by a timeout.
    /// </summary>
    [Fact]
    public void TheChunkTypesAreTheCs()
    {
        Assert.Equal(1, TakionMessageHeader.InitChunkType);
        Assert.Equal(2, TakionMessageHeader.InitAckChunkType);
        Assert.Equal(0xa, TakionMessageHeader.CookieChunkType);
        Assert.Equal(0xb, TakionMessageHeader.CookieAckChunkType);
    }

    /// <summary>And takion.c still writes the header this way, checked once for both answers.</summary>
    [Fact]
    public void TheCStillWritesItThisWay()
    {
        if (TakionMessageHeader.LocateSource() is not { } path)
            return;

        Assert.True(
            TakionMessageHeader.TheCStillWritesItThisWay(File.ReadAllText(path)),
            "takion_write_message_header no longer places the chunk type at +0xc or the length at "
                + "+0xe with its addend, so both of the responder's answers are now wrong");
    }
}
