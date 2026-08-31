using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP605, under PP27: the responder reads the INIT that tells it which tag to answer with.
///
/// PP603 and PP604 wrote the two answers, and neither is sendable without this: the INIT_ACK echoes
/// a tag the responder has not been told until the INIT's payload arrives.
///
/// The datagrams here are built the way takion_send_message_init and takion_send_message_cookie
/// build them, so what is read is what the C actually sends rather than what this file would like.
/// </summary>
public class TakionHandshakeIntakeTests
{
    private static readonly byte[] Cookie =
        [.. Enumerable.Range(0, TakionHandshake.CookieSize).Select(i => (byte)(0xA0 + i))];

    /// <summary>An INIT as takion_send_message_init writes it.</summary>
    private static byte[] Init(uint tagLocal = 0x11223344, uint headerTag = 0)
    {
        byte[] datagram = new byte[TakionHandshakeIntake.InitDatagramSize];
        datagram[0] = TakionMessageHeader.ControlPacketType;

        TakionMessageHeader.Write(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize),
            headerTag, keyPos: 0, TakionMessageHeader.InitChunkType, TakionMessageHeader.NoChunkFlags,
            TakionHandshakeIntake.InitPayloadSize);

        Span<byte> body = datagram.AsSpan(1 + TakionHandshake.MessageHeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(body, tagLocal);
        BinaryPrimitives.WriteUInt32BigEndian(body[4..], TakionHandshake.ARwnd);
        BinaryPrimitives.WriteUInt16BigEndian(body[8..], TakionHandshake.OutboundStreams);
        BinaryPrimitives.WriteUInt16BigEndian(body[0xa..], TakionHandshake.InboundStreams);
        BinaryPrimitives.WriteUInt32BigEndian(body[0xc..], tagLocal);

        return datagram;
    }

    /// <summary>A COOKIE as takion_send_message_cookie writes it.</summary>
    private static byte[] CookieMessage(uint headerTag, byte[]? cookie = null)
    {
        byte[] datagram = new byte[TakionHandshakeIntake.CookieDatagramSize];
        datagram[0] = TakionMessageHeader.ControlPacketType;

        TakionMessageHeader.Write(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize),
            headerTag, keyPos: 0, TakionMessageHeader.CookieChunkType,
            TakionMessageHeader.NoChunkFlags, TakionHandshake.CookieSize);

        (cookie ?? Cookie).CopyTo(datagram, 1 + TakionHandshake.MessageHeaderSize);
        return datagram;
    }

    /// <summary>
    /// THE ONE THAT MAKES THE ANSWER POSSIBLE: the INIT's payload carries the client's tag.
    ///
    /// takion.c takes seq_num_local from tag_local, so both fields of a real INIT hold the same
    /// value - and the responder needs that number for the INIT_ACK's header.
    /// </summary>
    [Fact]
    public void TheInitCarriesTheTagTheAnswerHasToEcho()
    {
        TakionInbound read = TakionHandshakeIntake.Read(Init(0x11223344));

        Assert.Equal(TakionInboundKind.Init, read.Kind);
        Assert.NotNull(read.Init);
        Assert.Equal(0x11223344u, read.Init!.Value.Tag);
        Assert.Equal(0x11223344u, read.Init!.Value.InitialSeqNum);
        Assert.Equal(TakionHandshake.ARwnd, read.Init!.Value.ARwnd);
    }

    /// <summary>
    /// And its header tag is zero, because the client does not know the responder's yet.
    ///
    /// The client writes tag_remote in what it sends, and tag_remote is nothing until the INIT_ACK
    /// names it. A responder expecting its own tag in an INIT would never see one.
    /// </summary>
    [Fact]
    public void TheInitsHeaderTagIsZero()
    {
        Assert.Equal(
            TakionHandshakeIntake.TagBeforeTheInitAck,
            TakionHandshakeIntake.Read(Init()).HeaderTag);
    }

    /// <summary>
    /// The COOKIE comes back under the responder's own tag, which is what ties it to this handshake.
    /// </summary>
    [Fact]
    public void TheCookieComesBackUnderTheRespondersTag()
    {
        TakionInbound read = TakionHandshakeIntake.Read(CookieMessage(0x55667788));

        Assert.Equal(TakionInboundKind.Cookie, read.Kind);
        Assert.Equal(0x55667788u, read.HeaderTag);
        Assert.Equal(Cookie, read.Cookie);
    }

    /// <summary>And the cookie is compared, not assumed.</summary>
    [Fact]
    public void TheCookieIsCheckedAgainstTheOneSent()
    {
        Assert.True(TakionHandshakeIntake.CookieEchoesTheOneSent(Cookie, Cookie));

        byte[] other = (byte[])Cookie.Clone();
        other[^1] ^= 0xFF;
        Assert.False(TakionHandshakeIntake.CookieEchoesTheOneSent(other, Cookie));

        // A short "sent" is a caller bug rather than a match.
        Assert.False(TakionHandshakeIntake.CookieEchoesTheOneSent(Cookie, Cookie.AsSpan(1)));
    }

    /// <summary>
    /// A header whose length field disagrees with the datagram is refused, which is the check
    /// PP451's defect was about on the other side.
    /// </summary>
    [Fact]
    public void AHeaderThatLiesAboutItsLengthIsRefused()
    {
        byte[] datagram = Init();
        BinaryPrimitives.WriteUInt16BigEndian(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram + TakionMessageHeader.SizeFieldOffset),
            0x99);

        Assert.Equal(TakionInboundKind.Unknown, TakionHandshakeIntake.Read(datagram).Kind);
    }

    /// <summary>And so is the wrong size, the wrong packet type, a flag, or an unknown chunk.</summary>
    [Fact]
    public void WhatIsNotOneOfTheTwoIsSaidRatherThanGuessed()
    {
        Assert.Equal(TakionInboundKind.Unknown, TakionHandshakeIntake.Read([]).Kind);
        Assert.Equal(TakionInboundKind.Unknown, TakionHandshakeIntake.Read(new byte[9]).Kind);

        byte[] wrongType = Init();
        wrongType[0] = 2;
        Assert.Equal(TakionInboundKind.Unknown, TakionHandshakeIntake.Read(wrongType).Kind);

        byte[] flagged = Init();
        flagged[TakionMessageHeader.OffsetInDatagram + TakionMessageHeader.ChunkFlagsOffset] = 1;
        Assert.Equal(TakionInboundKind.Unknown, TakionHandshakeIntake.Read(flagged).Kind);

        byte[] unknownChunk = Init();
        unknownChunk[TakionHandshake.ChunkTypeOffsetInDatagram] = 0x7f;
        Assert.Equal(TakionInboundKind.Unknown, TakionHandshakeIntake.Read(unknownChunk).Kind);
    }

    /// <summary>
    /// The whole exchange, in the order it happens: read the INIT, answer it, read the COOKIE that
    /// comes back, answer that.
    ///
    /// The two answers are PP603's and PP604's, so this is the join between all three - and it is
    /// where a tag echoed from the wrong side would show up as the client refusing the ack.
    /// </summary>
    [Fact]
    public void TheThreeStepsJoinUp()
    {
        TakionInbound init = TakionHandshakeIntake.Read(Init(0x11223344));
        Assert.Equal(TakionInboundKind.Init, init.Kind);

        uint clientTag = init.Init!.Value.Tag;
        const uint OurTag = 0x55667788;

        byte[] ack = TakionInitAckDatagram.Write(
            clientTag,
            new TakionInitAck(OurTag, TakionHandshake.ARwnd, TakionHandshake.OutboundStreams,
                TakionHandshake.InboundStreams, OurTag),
            Cookie);

        // The client accepts an ack whose header carries ITS tag.
        Assert.True(TakionHandshake.InboundHeaderTagAccepted(
            BinaryPrimitives.ReadUInt32BigEndian(ack.AsSpan(TakionMessageHeader.OffsetInDatagram)),
            clientTag));

        TakionInbound cookie = TakionHandshakeIntake.Read(CookieMessage(OurTag));
        Assert.Equal(TakionInboundKind.Cookie, cookie.Kind);
        Assert.Equal(TakionHandshake.OutboundHeaderTag(OurTag), cookie.HeaderTag);
        Assert.True(TakionHandshakeIntake.CookieEchoesTheOneSent(cookie.Cookie, Cookie));

        Assert.Equal(
            TakionHandshake.CookieAckDatagramSize,
            TakionMessageHeader.WriteCookieAck(clientTag).Length);
    }
}
