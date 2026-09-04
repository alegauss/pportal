namespace ChiakiNg.Protocol;

/// <summary>What the message layer decided about one control datagram.</summary>
public enum TakionMessageVerdict
{
    /// <summary>Refused by the parse. The C frees the buffer and returns without a switch.</summary>
    Refused,

    /// <summary>TAKION_CHUNK_TYPE_DATA. The handler KEEPS the buffer, queued against a sequence number.</summary>
    Data,

    /// <summary>TAKION_CHUNK_TYPE_DATA_ACK. Handled and the buffer released in the same breath.</summary>
    DataAck,

    /// <summary>Any other chunk type: logged and dropped, which is a branch and not a failure.</summary>
    Unknown,
}

/// <summary>
/// One control message, read.
/// </summary>
/// <param name="Verdict">Which of the switch's three arms, or refused before reaching it.</param>
/// <param name="Header">The sixteen bytes, where the parse got that far.</param>
/// <param name="KeyPos">The position the key state committed to, which the C does on EVERY message.</param>
/// <param name="PayloadOffset">Where the payload starts in the caller's datagram, or zero.</param>
/// <param name="PayloadSize">Its length, which the C has already had the addend taken off.</param>
/// <param name="Lifetime">Whether the datagram outlives the call, which is what the switch decides.</param>
public readonly record struct TakionMessageReading(
    TakionMessageVerdict Verdict,
    TakionInboundHeader Header,
    ulong KeyPos,
    int PayloadOffset,
    int PayloadSize,
    DatagramLifetime Lifetime);

/// <summary>
/// PP673: takion_handle_packet_message, which is the layer between PP500's branch and the models.
///
/// <see cref="TakionReceivePath"/> hands a control datagram to a sink and stops.
/// <see cref="TakionDataDrain"/> models the data queue's flush and <see cref="TakionDataAck"/> reads
/// the inbound ack. Nothing joined them, so a control datagram reached a branch and went no further.
///
/// THE PARSE IS PP672'S, LIFTED RATHER THAN REWRITTEN. That line needed the three refusals for the
/// two handshake acks it reads, and <see cref="TakionMessageHeader.TryReadInbound"/> keeps the C's
/// order: too short to hold a header, a tag that is not ours, a length field disagreeing with the
/// message. Its docstring says PP673 owns the message layer and that it is the handshake borrowing
/// the rules until this lands. This is that landing, so there is one reader and not two.
///
/// WHAT THIS ADDS IS THE COMMIT AND THE SWITCH. The C calls chiaki_key_state_request_pos with
/// commit true on every message it parses, before it knows what kind the message is - so a message
/// it then drops has still moved the ledger. That is reproduced rather than tidied, because the
/// ledger is a running expansion of a 32-bit wire field and skipping one moves every later answer.
///
/// AND THE SWITCH IS ABOUT OWNERSHIP as much as routing. DATA keeps the buffer - the C queues it
/// against a sequence number and frees it when the queue releases it - while DATA_ACK and the
/// unknown arm free it where they stand. Over the port's pooled receive buffer that is the
/// difference between a borrow and a copy, which is the distinction <see cref="DatagramLifetime"/>
/// already draws for the branch above this one.
/// </summary>
public static class TakionMessageIntake
{
    /// <summary>TAKION_CHUNK_TYPE_DATA.</summary>
    public const byte DataChunkType = 0;

    /// <summary>TAKION_CHUNK_TYPE_DATA_ACK.</summary>
    public const byte DataAckChunkType = 3;

    /// <summary>
    /// Read one control datagram, the way takion_handle_packet_message does.
    /// </summary>
    /// <param name="datagram">The WHOLE datagram, type byte included, as the C's <c>buf</c>.</param>
    /// <param name="tagLocal">The client's own tag, the only one an inbound header may carry.</param>
    /// <param name="keyState">
    /// The ledger the position is committed to. The C commits on every parsed message, so this is
    /// called before the switch and its answer is kept whatever the switch then decides.
    /// </param>
    public static TakionMessageReading Read(ReadOnlySpan<byte> datagram, uint tagLocal, KeyState keyState)
    {
        ArgumentNullException.ThrowIfNull(keyState);

        // The C passes buf+1: the type byte is the branch's, not the message's.
        if (datagram.Length < 1)
            return Refused();

        if (!TakionMessageHeader.TryReadInbound(datagram[1..], tagLocal, out TakionInboundHeader header))
            return Refused();

        // Committed before the switch, and on a message the switch may drop. The C does this and
        // the ledger is a running expansion, so an uncommitted message moves every later answer.
        ulong keyPos = keyState.RequestPos(header.KeyPosLow, commit: true);

        int payloadOffset = 1 + TakionHandshake.MessageHeaderSize;
        int payloadSize = header.PayloadSize;

        return header.ChunkType switch
        {
            DataChunkType => new TakionMessageReading(
                TakionMessageVerdict.Data, header, keyPos, payloadOffset, payloadSize, DatagramLifetime.Copied),

            DataAckChunkType => new TakionMessageReading(
                TakionMessageVerdict.DataAck, header, keyPos, payloadOffset, payloadSize, DatagramLifetime.Borrowed),

            _ => new TakionMessageReading(
                TakionMessageVerdict.Unknown, header, keyPos, payloadOffset, payloadSize, DatagramLifetime.Borrowed),
        };
    }

    /// <summary>
    /// The two refusals a TRUNCATED head can answer, committing nothing.
    ///
    /// PP510's tap keeps eighteen bytes of each datagram, so a captured control message is a header
    /// and a stub. The third refusal compares the length field against the whole message and on a
    /// stub it always fires - correctly, and about the capture rather than about the header. So a
    /// corpus reader gets the two that are answerable: too short to hold a header, and a tag that is
    /// not the one asked for.
    ///
    /// Nothing is committed either, which matters over four thousand messages: the ledger is a
    /// running expansion, so reading a capture through <see cref="Read"/> would make every position
    /// after the first an artefact of the order the file happened to be in.
    /// </summary>
    public static bool HeadParses(ReadOnlySpan<byte> datagram, uint tagLocal, out TakionInboundHeader header)
    {
        header = default;

        return datagram.Length >= 1
            && TakionMessageHeader.TryReadInboundFields(datagram[1..], tagLocal, out header, out _);
    }

    /// <summary>Which arm a chunk type takes, which is the switch with no message around it.</summary>
    public static TakionMessageVerdict ArmFor(byte chunkType) => chunkType switch
    {
        DataChunkType => TakionMessageVerdict.Data,
        DataAckChunkType => TakionMessageVerdict.DataAck,
        _ => TakionMessageVerdict.Unknown,
    };

    /// <summary>
    /// Whether an arm keeps the datagram past the call.
    ///
    /// Only DATA does. The C queues that buffer against a sequence number and the queue frees it
    /// later; both other arms free it where they stand.
    /// </summary>
    public static DatagramLifetime LifetimeOf(TakionMessageVerdict verdict)
        => verdict == TakionMessageVerdict.Data ? DatagramLifetime.Copied : DatagramLifetime.Borrowed;

    private static TakionMessageReading Refused()
        => new(TakionMessageVerdict.Refused, default, 0, 0, 0, DatagramLifetime.Borrowed);
}
