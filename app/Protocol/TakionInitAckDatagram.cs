using System.Buffers.Binary;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP672: an INIT_ACK read by the client - the five fields, and the cookie it has to echo.
///
/// <see cref="TakionInitAck"/> has no cookie field because the responder's answer is BUILT from one
/// and the client's reading YIELDS one, so it rides beside the record rather than inside it.
/// </summary>
public readonly record struct TakionInitAckReading(TakionInitAck Ack, byte[] Cookie);

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
    public const byte ControlPacketType = TakionMessageHeader.ControlPacketType;

    /// <summary>TAKION_CHUNK_TYPE_INIT_ACK.</summary>
    public const byte InitAckChunkType = TakionMessageHeader.InitAckChunkType;

    /// <summary>The flags an INIT_ACK carries, which takion refuses if they are anything else.</summary>
    public const byte NoChunkFlags = TakionMessageHeader.NoChunkFlags;

    /// <summary>Where the message header starts, the packet type being one byte.</summary>
    public const int HeaderOffset = TakionMessageHeader.OffsetInDatagram;

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
    public const int SizeFieldAddend = TakionMessageHeader.SizeFieldAddend;

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

        // PP604: the one writer, not a second copy of the field placement. The four MAC bytes stay
        // zero there for the reason they do here - the handshake runs before crypt exists.
        TakionMessageHeader.Write(
            datagram.AsSpan(HeaderOffset, TakionHandshake.MessageHeaderSize),
            tagLocal, keyPos: 0, InitAckChunkType, NoChunkFlags, PayloadSize);

        Span<byte> body = datagram.AsSpan(PayloadOffset, PayloadSize);
        BinaryPrimitives.WriteUInt32BigEndian(body, payload.Tag);
        BinaryPrimitives.WriteUInt32BigEndian(body[4..], payload.ARwnd);
        BinaryPrimitives.WriteUInt16BigEndian(body[8..], payload.OutboundStreams);
        BinaryPrimitives.WriteUInt16BigEndian(body[0xa..], payload.InboundStreams);
        BinaryPrimitives.WriteUInt32BigEndian(body[0xc..], payload.InitialSeqNum);
        cookie.CopyTo(body[0x10..]);

        return datagram;
    }

    /// <summary>
    /// PP672: the INIT_ACK read, the way takion_recv_message_init_ack reads what its receive of
    /// exactly <see cref="TakionHandshake.InitAckDatagramSize"/> bytes handed it - the inverse of
    /// <see cref="Write"/>.
    ///
    /// Five refusals, each CHIAKI_ERR_INVALID_RESPONSE and each in the C's order: shorter than the
    /// whole datagram; not a control packet; a header the parse refuses - our tag missing, or a length
    /// field that disagrees; a chunk that is not INIT_ACK or carries flags; and a payload that is not
    /// the five fields plus the cookie, which PP369 made a check rather than an assert because a
    /// Release build compiles the assert out and the six reads below it would run past a short payload.
    ///
    /// The receive it follows is the buffer's size, so on winsock a longer datagram never reaches here:
    /// the recv fails and the attempt retries. Handed more anyway, this reads the first sixty-five
    /// bytes, which is what a truncating recv would have delivered.
    /// </summary>
    /// <param name="datagram">What one receive produced.</param>
    /// <param name="tagLocal">The client's tag, which the header has to carry.</param>
    /// <param name="reading">The five fields and the cookie, where nothing was refused.</param>
    public static ChiakiError Read(
        ReadOnlySpan<byte> datagram, uint tagLocal, out TakionInitAckReading reading)
    {
        reading = default;

        if (datagram.Length < TakionHandshake.InitAckDatagramSize)
            return ChiakiError.InvalidResponse;

        if (datagram[0] != ControlPacketType)
            return ChiakiError.InvalidResponse;

        ReadOnlySpan<byte> message = datagram.Slice(HeaderOffset, TakionHandshake.InitAckDatagramSize - 1);
        if (!TakionMessageHeader.TryReadInbound(message, tagLocal, out TakionInboundHeader header))
            return ChiakiError.InvalidResponse;

        if (header.ChunkType != InitAckChunkType || header.ChunkFlags != NoChunkFlags)
            return ChiakiError.InvalidResponse;

        // PP369's check. With the parse holding the message to its exact size this cannot fail on its
        // own, and it stays because the C keeps it and a reader counting refusals should find five.
        if (header.PayloadSize != PayloadSize)
            return ChiakiError.InvalidResponse;

        ReadOnlySpan<byte> body = datagram.Slice(PayloadOffset, PayloadSize);
        reading = new TakionInitAckReading(
            TakionHandshakeIntake.ReadInit(body), body[0x10..].ToArray());

        return ChiakiError.Success;
    }

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? LocateSource() => TakionMessageHeader.LocateSource();

    /// <summary>
    /// Whether takion.c still writes the header the way this depends on.
    ///
    /// PP604 moved the check itself to <see cref="TakionMessageHeader"/>, where it belongs: both
    /// answers are wrong together if the header moves, and a check kept beside one of them would
    /// have said so about only that one. This forwards so the join stays reachable from here.
    /// </summary>
    public static bool TheHeaderIsStillWrittenThisWay(string takionSource)
        => TakionMessageHeader.TheCStillWritesItThisWay(takionSource);
}
