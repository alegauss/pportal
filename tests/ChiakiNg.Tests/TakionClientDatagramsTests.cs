using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP672, under PP27: the client's two datagrams, and the C's own bytes as the judge of both.
///
/// The field-by-field cases are the offsets takion_send_message_init and takion_send_message_cookie
/// write to; the join with PP605 is that the responder's intake reads back what these write; and the
/// case that matters most hands a real takion's INIT and COOKIE, received over PP607's loopback, to
/// an equality over the whole array.
/// </summary>
public class TakionClientDatagramsTests(ITestOutputHelper output)
{
    private static readonly byte[] Cookie =
        [.. Enumerable.Range(0, TakionHandshake.CookieSize).Select(i => (byte)(0x3C + i))];

    private const uint OurTag = 0x11223344;
    private const uint PeerTag = 0x55667788;

    /// <summary>The INIT is the size the responder's intake demands of one, and the C's sizeof.</summary>
    [Fact]
    public void TheInitIsTheSizeTheResponderReads()
    {
        byte[] init = TakionClientDatagrams.WriteInit(OurTag);

        Assert.Equal(TakionHandshakeIntake.InitDatagramSize, init.Length);
        Assert.Equal(1 + TakionHandshake.MessageHeaderSize + 0x10, init.Length);
    }

    /// <summary>
    /// ITS HEADER IS ADDRESSED TO NOBODY: tag zero, because tag_remote is nothing until the ack names
    /// it. And the rest of the header is the INIT's: control, chunk INIT, no flags, sixteen plus four.
    /// </summary>
    [Fact]
    public void TheInitsHeaderIsAddressedToNobody()
    {
        byte[] init = TakionClientDatagrams.WriteInit(OurTag);
        ReadOnlySpan<byte> header = init.AsSpan(
            TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize);

        Assert.Equal(TakionMessageHeader.ControlPacketType, init[0]);
        Assert.Equal(
            TakionHandshakeIntake.TagBeforeTheInitAck,
            BinaryPrimitives.ReadUInt32BigEndian(header[TakionMessageHeader.TagOffset..]));
        Assert.Equal(TakionMessageHeader.InitChunkType, header[TakionMessageHeader.ChunkTypeOffset]);
        Assert.Equal(TakionMessageHeader.NoChunkFlags, header[TakionMessageHeader.ChunkFlagsOffset]);
        Assert.Equal(
            TakionHandshakeIntake.InitPayloadSize + TakionMessageHeader.SizeFieldAddend,
            BinaryPrimitives.ReadUInt16BigEndian(header[TakionMessageHeader.SizeFieldOffset..]));
    }

    /// <summary>
    /// THE PAYLOAD CARRIES THE TAG TWICE - as the tag and as the initial sequence number - with the
    /// window and the two stream counts between them, at the offsets the responder reads.
    /// </summary>
    [Fact]
    public void TheInitsPayloadCarriesTheTagTwice()
    {
        ReadOnlySpan<byte> body = TakionClientDatagrams.WriteInit(OurTag)
            .AsSpan(TakionMessageHeader.OffsetInDatagram + TakionHandshake.MessageHeaderSize);

        Assert.Equal(OurTag, BinaryPrimitives.ReadUInt32BigEndian(body));
        Assert.Equal(TakionHandshake.ARwnd, BinaryPrimitives.ReadUInt32BigEndian(body[4..]));
        Assert.Equal(TakionHandshake.OutboundStreams, BinaryPrimitives.ReadUInt16BigEndian(body[8..]));
        Assert.Equal(TakionHandshake.InboundStreams, BinaryPrimitives.ReadUInt16BigEndian(body[0xa..]));
        Assert.Equal(OurTag, BinaryPrimitives.ReadUInt32BigEndian(body[0xc..]));
    }

    /// <summary>The join with PP605: the responder reads back exactly what the client wrote.</summary>
    [Fact]
    public void TheResponderReadsWhatTheClientWrites()
    {
        TakionInbound init = TakionHandshakeIntake.Read(TakionClientDatagrams.WriteInit(OurTag));

        Assert.Equal(TakionInboundKind.Init, init.Kind);
        Assert.Equal(TakionHandshakeIntake.TagBeforeTheInitAck, init.HeaderTag);
        Assert.Equal(TakionHandshake.Init(OurTag), init.Init);

        TakionInbound cookie = TakionHandshakeIntake.Read(TakionClientDatagrams.WriteCookie(PeerTag, Cookie));

        Assert.Equal(TakionInboundKind.Cookie, cookie.Kind);
        Assert.Equal(PeerTag, cookie.HeaderTag);
        Assert.Equal(Cookie, cookie.Cookie);
    }

    /// <summary>
    /// The COOKIE echoes under the PEER's tag - the one the ack named - with the cookie chunk type
    /// and the cookie's own size in the length field.
    /// </summary>
    [Fact]
    public void TheCookieEchoesUnderThePeersTag()
    {
        byte[] message = TakionClientDatagrams.WriteCookie(PeerTag, Cookie);
        ReadOnlySpan<byte> header = message.AsSpan(
            TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize);

        Assert.Equal(TakionHandshakeIntake.CookieDatagramSize, message.Length);
        Assert.Equal(TakionMessageHeader.ControlPacketType, message[0]);
        Assert.Equal(PeerTag, BinaryPrimitives.ReadUInt32BigEndian(header[TakionMessageHeader.TagOffset..]));
        Assert.Equal(TakionMessageHeader.CookieChunkType, header[TakionMessageHeader.ChunkTypeOffset]);
        Assert.Equal(
            TakionHandshake.CookieSize + TakionMessageHeader.SizeFieldAddend,
            BinaryPrimitives.ReadUInt16BigEndian(header[TakionMessageHeader.SizeFieldOffset..]));
        Assert.Equal(
            Cookie,
            message.AsSpan(TakionMessageHeader.OffsetInDatagram + TakionHandshake.MessageHeaderSize).ToArray());
    }

    /// <summary>A cookie of the wrong length is refused rather than padded or cut.</summary>
    [Fact]
    public void AWrongLengthCookieIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            TakionClientDatagrams.WriteCookie(PeerTag, new byte[TakionHandshake.CookieSize - 1]));
    }

    /// <summary>
    /// THE JUDGE: the C's own INIT and COOKIE, over one exchange, equal to these writers' whole arrays.
    ///
    /// PP607's harness, kept as it was: the real takion connects to a UdpClient this test holds and
    /// PP606's responder answers it. What is added is that the two datagrams the C sends are kept, and
    /// the tag the C drew is read out of its INIT - the one value a writer cannot know in advance
    /// (PP602) - so the managed INIT for that tag and the managed COOKIE for the responder's tag and
    /// cookie must be the C's byte for byte. A retried INIT is the same bytes, so the first is kept.
    /// </summary>
    [Fact]
    public void TheBytesAreTheCsOverOneExchange()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        peer.Client.ReceiveTimeout = 2000;

        ushort port = (ushort)((IPEndPoint)peer.Client.LocalEndPoint!).Port;
        var responder = new TakionHandshakeResponder(PeerTag, Cookie);

        NativeTakionLoopback? takion =
            NativeTakionLoopback.TryConnect(port, protocolVersion: 9, out ChiakiError error);

        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(takion);

        byte[]? initFromTheC = null;
        byte[]? cookieFromTheC = null;

        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            var from = new IPEndPoint(IPAddress.Any, 0);

            while (DateTime.UtcNow < deadline && !takion!.Connected)
            {
                byte[] arrived;

                try
                {
                    arrived = peer.Receive(ref from);
                }
                catch (SocketException)
                {
                    continue;
                }

                switch (TakionHandshakeIntake.Read(arrived).Kind)
                {
                    case TakionInboundKind.Init:
                        initFromTheC ??= arrived;
                        break;
                    case TakionInboundKind.Cookie:
                        cookieFromTheC ??= arrived;
                        break;
                    default:
                        break;
                }

                if (responder.Answer(arrived) is { } reply)
                    peer.Send(reply, reply.Length, from);
            }

            Assert.True(
                takion!.Connected,
                $"the C never connected: responder {responder.State}, {responder.InitsSeen} init(s)");
        }
        finally
        {
            takion?.Dispose();
        }

        Assert.NotNull(initFromTheC);
        Assert.NotNull(cookieFromTheC);

        uint tagTheCDrew = TakionHandshakeIntake.ReadInit(
            initFromTheC!.AsSpan(TakionMessageHeader.OffsetInDatagram + TakionHandshake.MessageHeaderSize)).Tag;
        output.WriteLine($"the C drew tag {tagTheCDrew:x8}");

        Assert.Equal(initFromTheC, TakionClientDatagrams.WriteInit(tagTheCDrew));
        Assert.Equal(cookieFromTheC, TakionClientDatagrams.WriteCookie(PeerTag, Cookie));
    }
}
