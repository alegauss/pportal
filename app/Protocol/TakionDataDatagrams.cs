using System.Buffers.Binary;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP675: the three data datagrams takion sends, as bytes.
///
/// Every send in takion.c ends in chiaki_takion_send_raw and nothing managed emits a takion byte.
/// <see cref="TakionDataSend"/> scripts the failure order and sends nothing; this is the layer
/// under it - the three builders, written into a caller's span.
///
/// THREE LAYOUTS THAT LOOK LIKE ONE. All three open with the control type byte and the sixteen-byte
/// header, and then diverge:
///
///   the DATA message adds nine bytes - a thirty-two-bit sequence number, the channel, a zero word,
///   and a zero byte - before the payload;
///
///   the CONTINUATION is the same without that last zero byte, so eight, which is the whole of the
///   difference between the two and the easiest thing in this file to get wrong;
///
///   the DATA_ACK is twelve bytes and no payload: the cumulative sequence number, the advertised
///   window, and two zero words.
///
/// THE LENGTH FIELD COUNTS THE OVERHEAD, NOT THE PAYLOAD ALONE. The C passes <c>9 + buf_size</c> to
/// the header writer, which then adds its own four. So a reader taking the field for the payload's
/// length is out by thirteen, and <see cref="TakionMessageHeader.Write"/> already owns the addend.
///
/// AND THE KEY POSITION IS THE LEDGER'S, ADVANCED BY THE WHOLE PACKET FOR THE ACK. The two payload
/// builders advance by the payload's size; the ack advances by <c>sizeof(buf)</c>, the packet whole.
/// That asymmetry is in the C and is reproduced rather than tidied - <see cref="TakionKeyPosition"/>
/// is where the advance lives, and this takes the position it was given.
///
/// NOTHING IS ALLOCATED. The C mallocs a packet per send and frees it on the failure path; a span
/// from the caller is what lets PP44's budget stay at zero over a stream's worth of sends.
/// </summary>
public static class TakionDataDatagrams
{
    /// <summary>The bytes a DATA message adds between the header and the payload.</summary>
    public const int DataOverhead = 9;

    /// <summary>The same for a continuation, which drops the trailing zero byte.</summary>
    public const int ContinuationOverhead = 8;

    /// <summary>A DATA_ACK's body, which is all of its payload.</summary>
    public const int AckBodySize = 0xc;

    /// <summary>Where a datagram's message header begins.</summary>
    public const int HeaderOffset = 1;

    /// <summary>And where the message's own payload begins.</summary>
    public const int BodyOffset = HeaderOffset + TakionHandshake.MessageHeaderSize;

    /// <summary>How long a DATA datagram carrying a payload of a size is.</summary>
    public static int DataSize(int payloadSize) => BodyOffset + DataOverhead + payloadSize;

    /// <summary>The same for a continuation.</summary>
    public static int ContinuationSize(int payloadSize) => BodyOffset + ContinuationOverhead + payloadSize;

    /// <summary>A DATA_ACK is one size, always.</summary>
    public static int AckSize => BodyOffset + AckBodySize;

    /// <summary>
    /// chiaki_takion_send_message_data, written into <paramref name="datagram"/>.
    /// </summary>
    /// <param name="datagram">Exactly <see cref="DataSize"/> bytes.</param>
    /// <param name="tagRemote">The RECEIVER's tag, which for a send is the console's.</param>
    /// <param name="keyPos">The position the ledger advanced to for this packet.</param>
    /// <param name="chunkFlags">The C's type_b, passed through to the header.</param>
    /// <param name="seqNum">The local counter's value for this message.</param>
    /// <param name="channel">Which channel the payload belongs to.</param>
    /// <param name="payload">The message's own bytes.</param>
    public static void WriteData(
        Span<byte> datagram, uint tagRemote, uint keyPos, byte chunkFlags,
        uint seqNum, ushort channel, ReadOnlySpan<byte> payload)
    {
        Require(datagram, DataSize(payload.Length));

        WriteFrame(datagram, tagRemote, keyPos, TakionMessageIntake.DataChunkType, chunkFlags,
            DataOverhead + payload.Length);

        Span<byte> body = datagram[BodyOffset..];

        BinaryPrimitives.WriteUInt32BigEndian(body, seqNum);
        BinaryPrimitives.WriteUInt16BigEndian(body[4..], channel);
        BinaryPrimitives.WriteUInt16BigEndian(body[6..], 0);
        body[8] = 0;

        payload.CopyTo(body[DataOverhead..]);
    }

    /// <summary>
    /// chiaki_takion_send_message_data_cont: the same, one byte shorter.
    ///
    /// The zero byte at +8 is what the DATA message has and this does not, and the header's length
    /// field follows it down. Nothing else differs, which is why they sit in one file.
    /// </summary>
    public static void WriteContinuation(
        Span<byte> datagram, uint tagRemote, uint keyPos, byte chunkFlags,
        uint seqNum, ushort channel, ReadOnlySpan<byte> payload)
    {
        Require(datagram, ContinuationSize(payload.Length));

        WriteFrame(datagram, tagRemote, keyPos, TakionMessageIntake.DataChunkType, chunkFlags,
            ContinuationOverhead + payload.Length);

        Span<byte> body = datagram[BodyOffset..];

        BinaryPrimitives.WriteUInt32BigEndian(body, seqNum);
        BinaryPrimitives.WriteUInt16BigEndian(body[4..], channel);
        BinaryPrimitives.WriteUInt16BigEndian(body[6..], 0);

        payload.CopyTo(body[ContinuationOverhead..]);
    }

    /// <summary>
    /// chiaki_takion_send_message_data_ack: the cumulative sequence and the advertised window.
    /// </summary>
    /// <param name="datagram">Exactly <see cref="AckSize"/> bytes.</param>
    /// <param name="tagRemote">The receiver's tag.</param>
    /// <param name="keyPos">The position, advanced by the WHOLE packet for this one.</param>
    /// <param name="seqNum">The sequence number being acknowledged.</param>
    /// <param name="advertisedWindow">takion's a_rwnd.</param>
    public static void WriteAck(
        Span<byte> datagram, uint tagRemote, uint keyPos, uint seqNum, uint advertisedWindow)
    {
        Require(datagram, AckSize);

        WriteFrame(datagram, tagRemote, keyPos, TakionMessageIntake.DataAckChunkType,
            TakionMessageHeader.NoChunkFlags, AckBodySize);

        Span<byte> body = datagram[BodyOffset..];

        BinaryPrimitives.WriteUInt32BigEndian(body, seqNum);
        BinaryPrimitives.WriteUInt32BigEndian(body[4..], advertisedWindow);
        BinaryPrimitives.WriteUInt16BigEndian(body[8..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(body[0xa..], 0);
    }

    /// <summary>The type byte and the header, which all three open with.</summary>
    private static void WriteFrame(
        Span<byte> datagram, uint tagRemote, uint keyPos, byte chunkType, byte chunkFlags, int bodySize)
    {
        datagram[0] = TakionMessageHeader.ControlPacketType;

        TakionMessageHeader.Write(
            datagram.Slice(HeaderOffset, TakionHandshake.MessageHeaderSize),
            tagRemote, keyPos, chunkType, chunkFlags, bodySize);
    }

    private static void Require(Span<byte> datagram, int wanted)
    {
        if (datagram.Length != wanted)
        {
            throw new ArgumentException(
                $"this datagram is {wanted} bytes and the span is {datagram.Length}", nameof(datagram));
        }
    }
}
