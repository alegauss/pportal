using System.Buffers.Binary;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP672, under PP27: the two datagrams the CLIENT sends, as bytes.
///
/// PP603 to PP606 wrote the console's side of the handshake and PP607 ran the real C against it. The
/// client's side had the rules - TakionHandshake models every constant and both gates - and one
/// header writer, and nothing that turned either into what takion_send_message_init and
/// takion_send_message_cookie put on the wire. The bytes existed twice, as private helpers in two
/// test files, which is the shape that drifts: a field moved in one copy still passes the other's
/// tests.
///
/// THE INIT ASKS UNDER A HEADER ADDRESSED TO NOBODY. Its header tag is tag_remote, and tag_remote is
/// zero until the INIT_ACK names it. The payload carries the client's own tag, the window it will
/// take, the two stream counts, and its initial sequence number - which is the tag again, by the
/// convention <see cref="TakionHandshake.LocalInitialSeqNum"/> records.
///
/// THE COOKIE ECHOES. Thirty-two bytes the INIT_ACK handed over go back verbatim, under the tag the
/// ack named, and the responder compares them to the ones it sent (PP605). A client that rewrote a
/// byte would be answering a different peer.
///
/// THE ORACLE IS THE C'S OWN BYTES. PP607's harness hands a real takion's INIT and COOKIE to a
/// UdpClient this process holds, so both writers are compared whole against what the C sent over
/// one exchange - with the tag read out of the C's payload, since the C draws it fresh (PP602).
/// </summary>
public static class TakionClientDatagrams
{
    /// <summary>The INIT: the type byte, the header and the five fields.</summary>
    public const int InitSize = TakionHandshakeIntake.InitDatagramSize;

    /// <summary>The COOKIE: the type byte, the header and the echoed cookie.</summary>
    public const int CookieMessageSize = TakionHandshakeIntake.CookieDatagramSize;

    /// <summary>
    /// The INIT, as takion_send_message_init writes it.
    /// </summary>
    /// <param name="tagLocal">The client's own tag, which is also its initial sequence number.</param>
    public static byte[] WriteInit(uint tagLocal)
    {
        TakionInitAck init = TakionHandshake.Init(tagLocal);
        byte[] datagram = new byte[InitSize];

        datagram[0] = TakionMessageHeader.ControlPacketType;
        TakionMessageHeader.Write(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize),
            TakionHandshake.OutboundHeaderTag(TakionHandshakeIntake.TagBeforeTheInitAck), keyPos: 0,
            TakionMessageHeader.InitChunkType, TakionMessageHeader.NoChunkFlags,
            TakionHandshakeIntake.InitPayloadSize);

        Span<byte> body = datagram.AsSpan(
            TakionMessageHeader.OffsetInDatagram + TakionHandshake.MessageHeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(body, init.Tag);
        BinaryPrimitives.WriteUInt32BigEndian(body[4..], init.ARwnd);
        BinaryPrimitives.WriteUInt16BigEndian(body[8..], init.OutboundStreams);
        BinaryPrimitives.WriteUInt16BigEndian(body[0xa..], init.InboundStreams);
        BinaryPrimitives.WriteUInt32BigEndian(body[0xc..], init.InitialSeqNum);

        return datagram;
    }

    /// <summary>
    /// The COOKIE, as takion_send_message_cookie writes it.
    /// </summary>
    /// <param name="tagRemote">The peer's tag, which the INIT_ACK named.</param>
    /// <param name="cookie">The thirty-two bytes the INIT_ACK carried, echoed unchanged.</param>
    public static byte[] WriteCookie(uint tagRemote, ReadOnlySpan<byte> cookie)
    {
        if (cookie.Length != TakionHandshake.CookieSize)
        {
            throw new ArgumentException(
                $"a cookie is {TakionHandshake.CookieSize} bytes and this one is {cookie.Length}",
                nameof(cookie));
        }

        byte[] datagram = new byte[CookieMessageSize];

        datagram[0] = TakionMessageHeader.ControlPacketType;
        TakionMessageHeader.Write(
            datagram.AsSpan(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize),
            TakionHandshake.OutboundHeaderTag(tagRemote), keyPos: 0,
            TakionMessageHeader.CookieChunkType, TakionMessageHeader.NoChunkFlags,
            TakionHandshake.CookieSize);

        cookie.CopyTo(datagram.AsSpan(
            TakionMessageHeader.OffsetInDatagram + TakionHandshake.MessageHeaderSize));

        return datagram;
    }
}
