using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP603, under PP27: the INIT_ACK a responder puts on the wire, as bytes.
///
/// PP601 found the way into takion's receive loop that needs no patch - connect takes the caller's
/// socket - and PP602 found that the far end has to ANSWER rather than replay, because the tag is
/// drawn fresh inside connect and no caller can supply the recorded run's. This is the first thing
/// that answer consists of: the datagram takion_recv_message_init_ack reads.
///
/// TakionHandshake already models the rules and the sizes - the two gates, the tag identities, the
/// stream counts, InitAckDatagramSize. What it has no way to produce is a datagram, and a responder
/// is nothing but datagrams. So this is the model's missing half rather than a second copy of it:
/// every size here is read from <see cref="TakionHandshake"/>, and nothing is retyped.
///
/// THE LAYOUT, all big-endian, as takion.c writes and reads it:
/// byte 0 is the packet type; the message header runs 1..0x11 with the tag at +0, four zero MAC
/// bytes at +4, the key position at +8, the chunk type at +0xc, its flags at +0xd and the payload
/// size PLUS FOUR at +0xe; the payload runs 0x11..0x41 with the peer's tag, a_rwnd, the two stream
/// counts, the initial sequence number and a 32-byte cookie.
///
/// THE HEADER TAG IS THE CLIENT'S OWN, which is the part that reads backwards. takion_parse_message
/// refuses a message whose header tag is not tag_local, so a responder echoes the tag it was sent
/// and puts the tag it is CHOOSING in the payload. PP369's comment says what that check is worth:
/// it is the 32 bits an off-path sender would have to guess.
/// </summary>
public static class TakionInitAckDatagram
{
    /// <summary>TAKION_PACKET_TYPE_CONTROL, which every handshake datagram opens with.</summary>
    public const byte ControlPacketType = 0;

    /// <summary>TAKION_CHUNK_TYPE_INIT_ACK.</summary>
    public const byte InitAckChunkType = 2;

    /// <summary>The flags an INIT_ACK carries, which takion refuses if they are anything else.</summary>
    public const byte NoChunkFlags = 0;

    /// <summary>Where the message header starts, the packet type being one byte.</summary>
    public const int HeaderOffset = 1;

    /// <summary>Where the payload starts.</summary>
    public const int PayloadOffset = HeaderOffset + TakionHandshake.MessageHeaderSize;

    /// <summary>How long the payload is: the five fields, then the cookie.</summary>
    public const int PayloadSize = 0x10 + TakionHandshake.CookieSize;

    /// <summary>
    /// What the header's size field carries beyond the payload's own length.
    ///
    /// takion_write_message_header writes <c>payload_data_size + 4</c>, and the parse checks the
    /// result against 0x10 + TAKION_COOKIE_SIZE - so a responder that wrote the bare size would be
    /// refused four bytes short.
    /// </summary>
    public const int SizeFieldAddend = 4;

    /// <summary>
    /// The INIT_ACK, written.
    /// </summary>
    /// <param name="tagLocal">The client's tag, echoed in the header so parse_message accepts it.</param>
    /// <param name="payload">The peer's own answer - its tag, window and stream counts.</param>
    /// <param name="cookie">The 32 bytes the client sends back in its COOKIE message.</param>
    public static byte[] Write(uint tagLocal, TakionInitAck payload, ReadOnlySpan<byte> cookie)
    {
        if (cookie.Length != TakionHandshake.CookieSize)
        {
            throw new ArgumentException(
                $"a cookie is {TakionHandshake.CookieSize} bytes and this one is {cookie.Length}",
                nameof(cookie));
        }

        byte[] datagram = new byte[TakionHandshake.InitAckDatagramSize];

        datagram[0] = ControlPacketType;

        Span<byte> header = datagram.AsSpan(HeaderOffset, TakionHandshake.MessageHeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(header, tagLocal);
        // The four MAC bytes stay zero: the handshake runs before crypt exists.
        BinaryPrimitives.WriteUInt32BigEndian(header[8..], 0);
        header[0xc] = InitAckChunkType;
        header[0xd] = NoChunkFlags;
        BinaryPrimitives.WriteUInt16BigEndian(header[0xe..], PayloadSize + SizeFieldAddend);

        Span<byte> body = datagram.AsSpan(PayloadOffset, PayloadSize);
        BinaryPrimitives.WriteUInt32BigEndian(body, payload.Tag);
        BinaryPrimitives.WriteUInt32BigEndian(body[4..], payload.ARwnd);
        BinaryPrimitives.WriteUInt16BigEndian(body[8..], payload.OutboundStreams);
        BinaryPrimitives.WriteUInt16BigEndian(body[0xa..], payload.InboundStreams);
        BinaryPrimitives.WriteUInt32BigEndian(body[0xc..], payload.InitialSeqNum);
        cookie.CopyTo(body[0x10..]);

        return datagram;
    }

    /// <summary>Where takion.c writes the two header fields this depends on being placed.</summary>
    public const string ChunkTypeWrite = "*(buf + 0xc) = chunk_type;";

    /// <summary>And the size field, with the addend spelled out.</summary>
    public const string SizeFieldWrite =
        "*((chiaki_unaligned_uint16_t *)(buf + 0xe)) = htons((uint16_t)(payload_data_size + 4));";

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? LocateSource() => SanitizerSource.LocateRelative(TakionHandshake.RelativePath);

    /// <summary>
    /// Whether takion.c still writes the header the way this writes it.
    ///
    /// The join, and it is worth having: every offset above was read out of that function once, and
    /// a header whose fields moved would leave this producing a datagram the C rejects for a reason
    /// no test here would name.
    /// </summary>
    public static bool TheHeaderIsStillWrittenThisWay(string takionSource)
    {
        ArgumentNullException.ThrowIfNull(takionSource);

        return takionSource.Contains(ChunkTypeWrite, StringComparison.Ordinal)
            && takionSource.Contains(SizeFieldWrite, StringComparison.Ordinal);
    }
}
