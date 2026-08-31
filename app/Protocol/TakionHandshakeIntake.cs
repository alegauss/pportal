using System.Buffers.Binary;

namespace ChiakiNg.Protocol;

/// <summary>What a handshake datagram arriving at the responder turned out to be.</summary>
public enum TakionInboundKind
{
    /// <summary>Not a handshake datagram this reads - wrong size, type, chunk or length field.</summary>
    Unknown,

    /// <summary>TAKION_CHUNK_TYPE_INIT, carrying the client's tag and initial sequence number.</summary>
    Init,

    /// <summary>TAKION_CHUNK_TYPE_COOKIE, echoing the cookie the responder sent.</summary>
    Cookie,
}

/// <summary>One datagram the client sent, read.</summary>
/// <param name="Kind">Which of the two it is, or Unknown.</param>
/// <param name="HeaderTag">The tag its header carried - the receiver's, so the responder's own.</param>
/// <param name="Init">The five fields, where this is an INIT.</param>
/// <param name="Cookie">The thirty-two bytes, where this is a COOKIE.</param>
public readonly record struct TakionInbound(
    TakionInboundKind Kind, uint HeaderTag, TakionInitAck? Init, byte[]? Cookie);

/// <summary>
/// PP605, under PP27: the half of the responder that reads.
///
/// PP603 and PP604 wrote the two answers. Neither is sendable without this one, because the INIT_ACK
/// has to echo a tag the responder has not been told yet - it arrives in the INIT's payload, and
/// until something parses that datagram the responder has nothing to answer with.
///
/// THE INIT'S PAYLOAD IS THE INIT_ACK'S OWN FIRST SIXTEEN BYTES. takion_send_message_init writes
/// tag, a_rwnd, the two stream counts and the initial sequence number at exactly the offsets
/// takion_recv_message_init_ack reads them back from, so <see cref="TakionInitAck"/> is the right
/// record for both and this does not invent a second one.
///
/// THE HEADER TAG IS THE RESPONDER'S, AND IT IS ZERO AT INIT TIME. The client writes tag_remote in
/// what it sends, and tag_remote is nothing until the INIT_ACK names it - so the INIT arrives
/// carrying zero and the COOKIE carries the tag the responder chose. That is the join that says a
/// COOKIE belongs to this handshake rather than an older one, and it is read rather than assumed.
///
/// THE COOKIE IS CHECKED AGAINST THE ONE SENT. takion echoes it verbatim, so a mismatch is the
/// client answering a different responder - and a peer that accepted any thirty-two bytes would
/// pass its own tests and mislead the first real run.
/// </summary>
public static class TakionHandshakeIntake
{
    /// <summary>The payload the INIT carries: the five fields, without a cookie.</summary>
    public const int InitPayloadSize = 0x10;

    /// <summary>1 + the header + the five fields.</summary>
    public const int InitDatagramSize =
        1 + TakionHandshake.MessageHeaderSize + InitPayloadSize;

    /// <summary>1 + the header + the cookie echoed back.</summary>
    public const int CookieDatagramSize =
        1 + TakionHandshake.MessageHeaderSize + TakionHandshake.CookieSize;

    /// <summary>The tag an INIT's header carries, the client not knowing the responder's yet.</summary>
    public const uint TagBeforeTheInitAck = 0;

    /// <summary>Reads one datagram, or says it is not one of the two.</summary>
    public static TakionInbound Read(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length is not (InitDatagramSize or CookieDatagramSize))
            return Unknown();

        if (datagram[0] != TakionMessageHeader.ControlPacketType)
            return Unknown();

        ReadOnlySpan<byte> header =
            datagram.Slice(TakionMessageHeader.OffsetInDatagram, TakionHandshake.MessageHeaderSize);

        if (header[TakionMessageHeader.ChunkFlagsOffset] != TakionMessageHeader.NoChunkFlags)
            return Unknown();

        uint headerTag = BinaryPrimitives.ReadUInt32BigEndian(header[TakionMessageHeader.TagOffset..]);
        int payloadSize = datagram.Length - 1 - TakionHandshake.MessageHeaderSize;

        // The length field, which carries the payload plus four. A datagram whose header disagrees
        // with its own length is one takion_parse_message would refuse, so it is refused here.
        ushort stated = BinaryPrimitives.ReadUInt16BigEndian(header[TakionMessageHeader.SizeFieldOffset..]);
        if (stated != payloadSize + TakionMessageHeader.SizeFieldAddend)
            return Unknown();

        ReadOnlySpan<byte> payload = datagram[(1 + TakionHandshake.MessageHeaderSize)..];

        return header[TakionMessageHeader.ChunkTypeOffset] switch
        {
            TakionMessageHeader.InitChunkType when payloadSize == InitPayloadSize
                => new TakionInbound(TakionInboundKind.Init, headerTag, ReadInit(payload), null),

            TakionMessageHeader.CookieChunkType when payloadSize == TakionHandshake.CookieSize
                => new TakionInbound(TakionInboundKind.Cookie, headerTag, null, payload.ToArray()),

            _ => Unknown(),
        };
    }

    /// <summary>The five fields, at the offsets takion writes them.</summary>
    public static TakionInitAck ReadInit(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < InitPayloadSize)
        {
            throw new ArgumentException(
                $"an INIT payload is {InitPayloadSize} bytes and this span is {payload.Length}",
                nameof(payload));
        }

        return new TakionInitAck(
            Tag: BinaryPrimitives.ReadUInt32BigEndian(payload),
            ARwnd: BinaryPrimitives.ReadUInt32BigEndian(payload[4..]),
            OutboundStreams: BinaryPrimitives.ReadUInt16BigEndian(payload[8..]),
            InboundStreams: BinaryPrimitives.ReadUInt16BigEndian(payload[0xa..]),
            InitialSeqNum: BinaryPrimitives.ReadUInt32BigEndian(payload[0xc..]));
    }

    /// <summary>
    /// Whether the cookie coming back is the one that went out.
    ///
    /// A fixed-time comparison is not the point here - this is a test harness peer, not a
    /// gatekeeper. What matters is that it is compared at all: a responder accepting any thirty-two
    /// bytes passes its own tests and then agrees with a client answering somebody else.
    /// </summary>
    public static bool CookieEchoesTheOneSent(ReadOnlySpan<byte> echoed, ReadOnlySpan<byte> sent)
        => sent.Length == TakionHandshake.CookieSize && echoed.SequenceEqual(sent);

    private static TakionInbound Unknown()
        => new(TakionInboundKind.Unknown, 0, null, null);
}
