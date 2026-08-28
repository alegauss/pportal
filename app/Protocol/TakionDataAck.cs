using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Why a received data ack was or was not acted on.</summary>
public enum TakionAckVerdict
{
    /// <summary>Parsed, and the send buffer's prefix released.</summary>
    Accepted,

    /// <summary>Not exactly twelve bytes. Logged as a size mismatch and dropped.</summary>
    WrongSize,

    /// <summary>Twelve bytes, but claiming gap-ack blocks that cannot be in them.</summary>
    GapBlocksClaimed,
}

/// <summary>The four fields a data ack carries.</summary>
/// <param name="CumulativeSeqNum">Everything at or before this is acknowledged.</param>
/// <param name="AdvertisedWindow">a_rwnd - the console's receive window. Parsed and dropped.</param>
/// <param name="GapAckBlocks">How many selective blocks follow. Only zero is ever accepted.</param>
/// <param name="DuplicateTsns">Warned about and dropped.</param>
public readonly record struct TakionAckFields(
    uint CumulativeSeqNum,
    uint AdvertisedWindow,
    ushort GapAckBlocks,
    ushort DuplicateTsns);

/// <summary>What reading one data ack decided.</summary>
/// <param name="Verdict">Which of the three.</param>
/// <param name="Fields">The parsed fields, meaningless unless the size was right.</param>
public readonly record struct TakionAckRead(TakionAckVerdict Verdict, TakionAckFields Fields);

/// <summary>
/// PP494, under PP27: the data ack coming BACK, and the selective-acknowledgement path that cannot
/// run in this build.
///
/// PP493 modelled the ack this client sends - one cumulative number for a whole drain. This is its
/// counterpart arriving, and the two are not mirror images. The wire shape has room for gap-ack
/// blocks, four bytes each after a twelve-byte header, and nothing here can accept one.
///
/// THE FIRST CHECK IS WHAT DECIDES THAT, AND THE SECOND ONE ONLY LOOKS LIKE IT DOES. The handler
/// refuses any payload whose size is not exactly twelve. Twelve is the header alone, so a packet
/// carrying even one gap-ack block is 16 bytes and is refused there, logged as a size mismatch. The
/// second check then compares the size against <c>count * 4 + 12</c> - and the size is already
/// known to be twelve, so it is a test that the count is zero. Its warning about an invalid count
/// therefore fires only for a console claiming blocks it did not send.
///
/// So this client is cumulative-only BY CONSTRUCTION rather than by decision, and the branch that
/// reads like selective-ack handling is the branch that proves there is none. A port that read the
/// second check first would carry a path that has never executed.
///
/// TWO FIELDS ARE PARSED AND DROPPED. a_rwnd is the receive window the console advertises, and
/// nothing reads it - this client's own window is a constant it sends and never revises.
/// dup_tsns_count is warned about and discarded.
///
/// AND THE CALLBACK IS INVOKED WITHOUT THE NULL TEST every other call site in that file has.
/// Unreachable: senkusha and streamconnection are the two callers and both set one. Named here
/// because "no guard" and "guard checked elsewhere" read identically at the call site, and the next
/// person to add a third caller is the one who needs to know which it is.
/// </summary>
public static class TakionDataAck
{
    /// <summary>The fixed size of a data ack payload, gap-ack blocks excluded.</summary>
    public const int Size = 0xc;

    /// <summary>How many bytes one gap-ack block would take, if one could ever arrive.</summary>
    public const int GapAckBlockSize = 4;

    /// <summary>
    /// Reads one data ack payload.
    /// </summary>
    /// <param name="payload">The message payload - the C's `buf` and `buf_size` here.</param>
    public static TakionAckRead Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != Size)
            return new TakionAckRead(TakionAckVerdict.WrongSize, default);

        var fields = new TakionAckFields(
            BinaryPrimitives.ReadUInt32BigEndian(payload[..4]),
            BinaryPrimitives.ReadUInt32BigEndian(payload[4..8]),
            BinaryPrimitives.ReadUInt16BigEndian(payload[8..10]),
            BinaryPrimitives.ReadUInt16BigEndian(payload[10..12]));

        // Spelled as the C spells it rather than as `GapAckBlocks != 0`, so the arithmetic that
        // makes the two the same is visible at the one place it matters.
        if (payload.Length != (fields.GapAckBlocks * GapAckBlockSize) + Size)
            return new TakionAckRead(TakionAckVerdict.GapBlocksClaimed, fields);

        return new TakionAckRead(TakionAckVerdict.Accepted, fields);
    }

    /// <summary>
    /// The smallest payload that would carry <paramref name="blocks"/> gap-ack blocks.
    ///
    /// Here to make the point arithmetically: for any count above zero this is not
    /// <see cref="Size"/>, so the first check refuses it before the second is reached.
    /// </summary>
    public static int PayloadSizeFor(int blocks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blocks);
        return Size + (blocks * GapAckBlockSize);
    }

    /// <summary>
    /// Applies an accepted ack to the send buffer, returning what it released.
    /// </summary>
    /// <remarks>
    /// One event per released sequence number, in the order the buffer held them - not one event
    /// for the ack. A cumulative number that clears four held messages is four notifications.
    /// </remarks>
    /// <returns>The released sequence numbers, empty for a verdict that is not Accepted.</returns>
    public static IReadOnlyList<uint> Apply(TakionSendBuffer sendBuffer, TakionAckRead read)
    {
        ArgumentNullException.ThrowIfNull(sendBuffer);

        return read.Verdict == TakionAckVerdict.Accepted
            ? sendBuffer.Ack(read.Fields.CumulativeSeqNum)
            : [];
    }
}

/// <summary>
/// PP494: the C's own spelling of the two checks, so their order is asserted and not recalled.
/// </summary>
public static class TakionDataAckSource
{
    /// <summary>takion.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(TakionPostpone.RelativePath);

    /// <summary>The handler.</summary>
    public static string? AckBody(string source)
        => CFunction.Body(source, "static void takion_handle_packet_message_data_ack");

    /// <summary>
    /// Whether the fixed-size check still comes first, which is what makes the second one a test
    /// that the count is zero.
    ///
    /// The whole claim. If the two ever swap, a gap ack would reach the second check and be
    /// rejected there instead - the same outcome by a different route, and the model's reason for
    /// calling this build cumulative-only would no longer be the code's.
    /// </summary>
    public static bool TheFixedSizeCheckComesFirst(string ackBody)
    {
        ArgumentNullException.ThrowIfNull(ackBody);

        string text = ackBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int fixedSize = text.IndexOf("if(buf_size != 0xc)", StringComparison.Ordinal);
        int gapCheck = text.IndexOf(
            "if(buf_size != gap_ack_blocks_count * 4 + 0xc)", StringComparison.Ordinal);

        return fixedSize >= 0 && gapCheck > fixedSize;
    }

    /// <summary>
    /// Whether a_rwnd and dup_tsns_count are still read and never used for anything but a log.
    ///
    /// Read as: each name appears in the parse, and neither reaches the send buffer call. A field
    /// that started being acted on would want a model, and this is what would notice.
    /// </summary>
    public static bool TheWindowAndDuplicatesAreOnlyLogged(string ackBody)
    {
        ArgumentNullException.ThrowIfNull(ackBody);

        string text = ackBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!text.Contains("uint32_t a_rwnd = ntohl", StringComparison.Ordinal)
            || !text.Contains("uint16_t dup_tsns_count = ntohs", StringComparison.Ordinal))
        {
            return false;
        }

        int release = text.IndexOf("chiaki_takion_send_buffer_ack(", StringComparison.Ordinal);
        if (release < 0)
            return false;

        // Nothing after the parse touches either name except the two log lines above the release.
        return !text[release..].Contains("a_rwnd", StringComparison.Ordinal)
            && !text[release..].Contains("dup_tsns_count", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the release still hands the CUMULATIVE number to the send buffer.
    ///
    /// The one field that is acted on. Handing it the wrong one would clear messages the console
    /// never received, and nothing downstream would report a thing.
    /// </summary>
    public static bool TheCumulativeNumberIsWhatReleases(string ackBody)
    {
        ArgumentNullException.ThrowIfNull(ackBody);
        return ackBody.Contains(
            "chiaki_takion_send_buffer_ack(&takion->send_buffer, cumulative_seq_num,",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the callback is still invoked here without the null test the rest of the file uses.
    ///
    /// Asserted as it IS, not as it should be: both callers set a callback, so changing this would
    /// be a repair to a path nothing can reach. What the assertion buys is that the model's note
    /// stays true - if a guard is added later, the note describing its absence is what goes red.
    /// </summary>
    public static bool TheCallbackIsUnguardedHere(string ackBody)
    {
        ArgumentNullException.ThrowIfNull(ackBody);

        string text = ackBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains("takion->cb(&event, takion->cb_user);", StringComparison.Ordinal)
            && !text.Contains("if(takion->cb)", StringComparison.Ordinal);
    }
}
