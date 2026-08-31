using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP604, under PP27: the sixteen bytes every takion control message opens with.
///
/// PP603 wrote the INIT_ACK and put the header inside it, which was right for one message and wrong
/// for two. takion.c has one writer - takion_write_message_header - and calls it for the INIT, the
/// COOKIE, the INIT_ACK and the COOKIE_ACK alike. A responder that copied the field placement a
/// second time would be one edit away from two answers disagreeing about where the chunk type sits.
///
/// THE FIELDS, all big-endian: the tag at +0, four MAC bytes at +4 that stay zero until crypt
/// exists, the key position at +8, the chunk type at +0xc, its flags at +0xd, and at +0xe the
/// payload's length PLUS FOUR. That addend is the one a reader invents wrongly - the C writes
/// <c>payload_data_size + 4</c> and the parse checks what comes back against the bare size.
///
/// THE TAG IS THE RECEIVER'S, WHICH IS WHY BOTH DIRECTIONS USE ONE WRITER. The client puts
/// <c>tag_remote</c> in what it sends and refuses anything whose header does not carry
/// <c>tag_local</c>, so a responder writes the tag it was sent. Same function, opposite value, and
/// <see cref="TakionHandshake.OutboundHeaderTag"/> is the model of that rule.
/// </summary>
public static class TakionMessageHeader
{
    /// <summary>TAKION_PACKET_TYPE_CONTROL, which every handshake datagram opens with.</summary>
    public const byte ControlPacketType = 0;

    /// <summary>TAKION_CHUNK_TYPE_INIT.</summary>
    public const byte InitChunkType = 1;

    /// <summary>TAKION_CHUNK_TYPE_INIT_ACK.</summary>
    public const byte InitAckChunkType = 2;

    /// <summary>TAKION_CHUNK_TYPE_COOKIE.</summary>
    public const byte CookieChunkType = 0xa;

    /// <summary>TAKION_CHUNK_TYPE_COOKIE_ACK.</summary>
    public const byte CookieAckChunkType = 0xb;

    /// <summary>The flags a handshake message carries; takion refuses anything else.</summary>
    public const byte NoChunkFlags = 0;

    /// <summary>Where the header starts in a datagram, the packet type being one byte.</summary>
    public const int OffsetInDatagram = 1;

    /// <summary>The tag's place in the header.</summary>
    public const int TagOffset = 0;

    /// <summary>The four bytes a MAC would occupy.</summary>
    public const int MacOffset = 4;

    /// <summary>The key position's place.</summary>
    public const int KeyPosOffset = 8;

    /// <summary>The chunk type's place, which is 0xd in the datagram.</summary>
    public const int ChunkTypeOffset = 0xc;

    /// <summary>The chunk flags, immediately after the type.</summary>
    public const int ChunkFlagsOffset = 0xd;

    /// <summary>Where the length field sits.</summary>
    public const int SizeFieldOffset = 0xe;

    /// <summary>What the length field carries beyond the payload's own length.</summary>
    public const int SizeFieldAddend = 4;

    /// <summary>Writes the header into the sixteen bytes it is given.</summary>
    /// <param name="header">Exactly <see cref="TakionHandshake.MessageHeaderSize"/> bytes.</param>
    /// <param name="tag">The RECEIVER's tag - tag_local when answering the client.</param>
    /// <param name="keyPos">The key position, zero throughout the handshake.</param>
    /// <param name="chunkType">One of the four chunk types.</param>
    /// <param name="chunkFlags">Zero for every handshake message.</param>
    /// <param name="payloadDataSize">The payload's own length; the addend is added here.</param>
    public static void Write(
        Span<byte> header, uint tag, uint keyPos, byte chunkType, byte chunkFlags, int payloadDataSize)
    {
        if (header.Length != TakionHandshake.MessageHeaderSize)
        {
            throw new ArgumentException(
                $"a header is {TakionHandshake.MessageHeaderSize} bytes and this span is {header.Length}",
                nameof(header));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(payloadDataSize);

        BinaryPrimitives.WriteUInt32BigEndian(header[TagOffset..], tag);
        header.Slice(MacOffset, TakionPacketMac.GmacSize).Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header[KeyPosOffset..], keyPos);
        header[ChunkTypeOffset] = chunkType;
        header[ChunkFlagsOffset] = chunkFlags;
        BinaryPrimitives.WriteUInt16BigEndian(
            header[SizeFieldOffset..], (ushort)(payloadDataSize + SizeFieldAddend));
    }

    /// <summary>
    /// The COOKIE_ACK, which is the header and nothing else.
    ///
    /// takion_recv_message_cookie_ack reads exactly 1 + TAKION_MESSAGE_HEADER_SIZE and asserts the
    /// payload is empty, so the length field carries the addend alone.
    /// </summary>
    /// <param name="tagLocal">The client's tag, echoed so parse_message accepts the message.</param>
    public static byte[] WriteCookieAck(uint tagLocal)
    {
        byte[] datagram = new byte[TakionHandshake.CookieAckDatagramSize];

        datagram[0] = ControlPacketType;
        Write(
            datagram.AsSpan(OffsetInDatagram, TakionHandshake.MessageHeaderSize),
            tagLocal, keyPos: 0, CookieAckChunkType, NoChunkFlags, payloadDataSize: 0);

        return datagram;
    }

    /// <summary>The line in takion.c that places the chunk type.</summary>
    public const string ChunkTypeWrite = "*(buf + 0xc) = chunk_type;";

    /// <summary>And the one that places the length, with its addend spelled out.</summary>
    public const string SizeFieldWrite =
        "*((chiaki_unaligned_uint16_t *)(buf + 0xe)) = htons((uint16_t)(payload_data_size + 4));";

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? LocateSource()
        => SanitizerSource.LocateRelative(TakionHandshake.RelativePath);

    /// <summary>
    /// Whether takion.c still writes the header the way this does.
    ///
    /// The join back to the C, and it belongs here now rather than beside one message: both answers
    /// are wrong together if the header moves, and a check kept next to the INIT_ACK would have said
    /// so about only one of them.
    /// </summary>
    public static bool TheCStillWritesItThisWay(string takionSource)
    {
        ArgumentNullException.ThrowIfNull(takionSource);

        return takionSource.Contains(ChunkTypeWrite, StringComparison.Ordinal)
            && takionSource.Contains(SizeFieldWrite, StringComparison.Ordinal);
    }
}
