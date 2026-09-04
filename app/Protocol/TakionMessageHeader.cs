using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>PP672: what takion_recv_message_cookie_ack decides about one datagram.</summary>
public enum TakionCookieAckVerdict
{
    /// <summary>A COOKIE_ACK under our tag: the handshake is done.</summary>
    Accepted,

    /// <summary>
    /// A late INIT_ACK where the cookie ack was expected: the C reads one more datagram in its place.
    /// </summary>
    ReadAnother,

    /// <summary>
    /// Anything else - too short, not a control packet, another tag, another chunk.
    /// CHIAKI_ERR_INVALID_RESPONSE.
    /// </summary>
    Refused,
}

/// <summary>PP672: a control message's header, read.</summary>
/// <param name="Tag">Ours, or the message was refused before this existed.</param>
/// <param name="KeyPosLow">The wire's thirty-two bits of key position, not expanded.</param>
/// <param name="ChunkType">The chunk type at +0xc.</param>
/// <param name="ChunkFlags">Its flags at +0xd.</param>
/// <param name="PayloadSize">The payload's own length, the addend taken off.</param>
internal readonly record struct TakionInboundHeader(
    uint Tag, uint KeyPosLow, byte ChunkType, byte ChunkFlags, int PayloadSize);

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

    /// <summary>
    /// PP672: the sixteen bytes READ, the way takion_parse_message reads them - three refusals, in
    /// the C's order.
    ///
    /// Too short to hold a header; a tag that is not OURS, which is
    /// <see cref="TakionHandshake.InboundHeaderTagAccepted"/> applied; and a length field that does
    /// not agree with the message. The field carries the payload plus the addend, so the C's
    /// <c>buf_size != msg->payload_size + 0xc</c> says: the twelve header bytes the field does not
    /// count, then the four it does. The addend comes off after the check, which is why
    /// <see cref="TakionInboundHeader.PayloadSize"/> is the payload's own.
    ///
    /// INTERNAL AND NARROW ON PURPOSE. PP673 owns the message layer - the parse as a public reader with
    /// the DATA and DATA_ACK switch under it, held against PP608's corpus. This is the handshake
    /// borrowing the header's rules for its two acks until that lands, not a second home for them.
    /// The key position is handed back as the wire's low half and not expanded: the C commits it
    /// through the key state on every message, which is PP677's ledger and zero throughout a handshake.
    /// </summary>
    /// <param name="message">The datagram after its type byte, as the C passes <c>buf + 1</c>.</param>
    /// <param name="tagLocal">The client's own tag, the only one an inbound header may carry.</param>
    /// <param name="header">The five fields, where the message was not refused.</param>
    internal static bool TryReadInbound(
        ReadOnlySpan<byte> message, uint tagLocal, out TakionInboundHeader header)
    {
        header = default;

        if (message.Length < TakionHandshake.MessageHeaderSize)
            return false;

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(message[TagOffset..]);
        uint keyPosLow = BinaryPrimitives.ReadUInt32BigEndian(message[KeyPosOffset..]);
        byte chunkType = message[ChunkTypeOffset];
        byte chunkFlags = message[ChunkFlagsOffset];
        int stated = BinaryPrimitives.ReadUInt16BigEndian(message[SizeFieldOffset..]);

        if (!TakionHandshake.InboundHeaderTagAccepted(tag, tagLocal))
            return false;

        if (message.Length != stated + (TakionHandshake.MessageHeaderSize - SizeFieldAddend))
            return false;

        header = new TakionInboundHeader(tag, keyPosLow, chunkType, chunkFlags, stated - SizeFieldAddend);
        return true;
    }

    /// <summary>
    /// PP672: the COOKIE_ACK read, the way takion_recv_message_cookie_ack reads what its receive
    /// handed it - a datagram received into a buffer of exactly the ack's size.
    ///
    /// THE LENGTH GATE COMES FIRST, which is PP451's repair: no byte is read from a datagram shorter
    /// than the whole ack. THEN THE LATE INIT_ACK TEST, before the type byte is looked at: the C reads
    /// the chunk type at 0xd and, finding INIT_ACK, receives one more datagram in this one's place.
    /// That order is reproduced rather than tidied - a datagram of the ack's size that is not a control
    /// packet and carries two at that offset also asks for another read, because the C asks.
    ///
    /// AND ON WINDOWS THAT BRANCH IS ALL BUT UNREACHABLE. A real INIT_ACK is sixty-five bytes, and
    /// winsock's recv into a seventeen-byte buffer does not truncate it - it fails with WSAEMSGSIZE,
    /// which takion_recv reports as a network error and the cookie loop answers by sending the COOKIE
    /// again. So on this port's one platform a late ack is survived by the retry and not by this
    /// tolerance, which fires only for a datagram already the ack's size. Stated so nobody reads the
    /// branch as the path a slow console takes; <see cref="TakionUdpWire"/> keeps the receive the C's
    /// size so the behaviour is the C's.
    /// </summary>
    /// <param name="datagram">What one receive of <see cref="TakionHandshake.CookieAckDatagramSize"/> bytes produced. Longer, only that many are read - the truncating recv the C gets elsewhere.</param>
    /// <param name="tagLocal">The client's tag, which the header has to carry.</param>
    /// <param name="secondRead">Whether this is the datagram read in a late ack's place, which the C judges without the late-ack test.</param>
    public static TakionCookieAckVerdict ReadCookieAck(
        ReadOnlySpan<byte> datagram, uint tagLocal, bool secondRead = false)
    {
        if (!TakionHandshake.DatagramIsLongEnoughToRead(datagram.Length))
            return TakionCookieAckVerdict.Refused;

        if (!secondRead && datagram[TakionHandshake.ChunkTypeOffsetInDatagram] == InitAckChunkType)
            return TakionCookieAckVerdict.ReadAnother;

        if (datagram[0] != ControlPacketType)
            return TakionCookieAckVerdict.Refused;

        // sizeof(message) - 1 and not the received length: the receive was the buffer's size.
        ReadOnlySpan<byte> message = datagram.Slice(OffsetInDatagram, TakionHandshake.MessageHeaderSize);
        if (!TryReadInbound(message, tagLocal, out TakionInboundHeader header))
            return TakionCookieAckVerdict.Refused;

        if (header.ChunkType != CookieAckChunkType || header.ChunkFlags != NoChunkFlags)
            return TakionCookieAckVerdict.Refused;

        // assert(msg.payload_size == 0), which the size check inside the parse already made true.
        return header.PayloadSize == 0
            ? TakionCookieAckVerdict.Accepted
            : TakionCookieAckVerdict.Refused;
    }

    /// <summary>takion_parse_message's body, or null where it is gone.</summary>
    public static string? ParseBody(string takionSource)
        => CFunction.Body(takionSource, "static ChiakiErrorCode takion_parse_message");

    /// <summary>
    /// PP672: whether the C still refuses the three things <see cref="TryReadInbound"/> refuses, in
    /// that order, and takes the addend off only afterwards.
    ///
    /// The order is the behaviour a log reader sees: a short message is "too short", a foreign tag is
    /// "tag mismatch", and a lying length is "payload size mismatch". A parse that tested them in
    /// another order would put a different sentence in the log for the same datagram.
    /// </summary>
    public static bool TheCStillRefusesTheseThree(string parseBody)
    {
        ArgumentNullException.ThrowIfNull(parseBody);

        int tooShort = parseBody.IndexOf("buf_size < TAKION_MESSAGE_HEADER_SIZE", StringComparison.Ordinal);
        int tag = parseBody.IndexOf("msg->tag != takion->tag_local", StringComparison.Ordinal);
        int size = parseBody.IndexOf("buf_size != msg->payload_size + 0xc", StringComparison.Ordinal);
        int addend = parseBody.IndexOf("msg->payload_size -= 0x4;", StringComparison.Ordinal);

        return tooShort >= 0 && tag > tooShort && size > tag && addend > size;
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
