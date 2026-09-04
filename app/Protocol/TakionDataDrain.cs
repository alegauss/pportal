using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>The data types the drain will hand on. Anything else is logged and dropped.</summary>
public enum TakionDataType
{
    /// <summary>CHIAKI_TAKION_MESSAGE_DATA_TYPE_PROTOBUF.</summary>
    Protobuf = 0,

    /// <summary>CHIAKI_TAKION_MESSAGE_DATA_TYPE_RUMBLE.</summary>
    Rumble = 7,

    /// <summary>CHIAKI_TAKION_MESSAGE_DATA_TYPE_PAD_INFO.</summary>
    PadInfo = 9,

    /// <summary>CHIAKI_TAKION_MESSAGE_DATA_TYPE_TRIGGER_EFFECTS.</summary>
    TriggerEffects = 11,
}

/// <summary>What the drain did with one entry it pulled.</summary>
public enum TakionDrainOutcome
{
    /// <summary>Payload under the nine-byte header. Freed and skipped.</summary>
    TooShort,

    /// <summary>A data type none of the four. Hexdumped, and NOT handed on.</summary>
    UnknownType,

    /// <summary>A known type with no callback registered. Freed the same way, silently.</summary>
    NoCallback,

    /// <summary>Handed to the callback, payload past its nine-byte header.</summary>
    Delivered,
}

/// <summary>
/// One entry as the queue holds it.
///
/// PP674: THE C'S TakionDataPacketEntry HAS SIX FIELDS AND THIS HELD TWO. PP493 modelled the drain,
/// which reads the sequence number and the payload and needs nothing else, so two was the whole of
/// what that line could justify. <see cref="TakionDataPush"/> is the other end - the entry being
/// BUILT - and it reads two more off the payload's own header.
///
/// Extended rather than duplicated. A second record for the same C struct is the shape a reader has
/// to hold two names for, and the two ends would drift; the added fields default so PP493's callers
/// are untouched.
/// </summary>
/// <param name="SeqNum">Its sequence number, which the ack may end up carrying.</param>
/// <param name="Payload">The message payload, header included.</param>
/// <param name="TypeB">The chunk flags. The C warns when they are not one and pushes anyway.</param>
/// <param name="Channel">The sixteen bits four bytes into the payload.</param>
public readonly record struct TakionDataEntry(
    uint SeqNum, byte[] Payload, byte TypeB = 1, ushort Channel = 0);

/// <summary>What one delivery handed to the callback.</summary>
/// <param name="DataType">The type byte, already known to be one of the four.</param>
/// <param name="Body">The payload past its nine-byte header.</param>
public readonly record struct TakionDelivery(TakionDataType DataType, byte[] Body);

/// <summary>What a whole drain did.</summary>
/// <param name="Outcomes">One per entry pulled, in order.</param>
/// <param name="Deliveries">The subset that reached the callback.</param>
/// <param name="Acked">Whether an ack was sent at all.</param>
/// <param name="AckSeqNum">The sequence number it carried, meaningful only when acked.</param>
/// <param name="NonzeroAtSix">Entries whose reserved halfword at payload+6 was not zero.</param>
public readonly record struct TakionDrainOutcomeSet(
    IReadOnlyList<TakionDrainOutcome> Outcomes,
    IReadOnlyList<TakionDelivery> Deliveries,
    bool Acked,
    uint AckSeqNum,
    int NonzeroAtSix);

/// <summary>
/// PP493, under PP27: takion's data queue drain - four things that can happen to an entry, and the
/// one ack that covers all of them.
///
/// PP491 made the path into this queue right. This is what comes out of it: every push is followed
/// by a drain, and the drain is where a control message becomes a callback or quietly does not.
///
/// THE ACK IS ONE PER DRAIN AND CARRIES THE LAST SEQUENCE PULLED. Not one per entry, and not one
/// per delivery. The flag that decides whether to send it is set the moment a pull succeeds -
/// before the size check, before the type check, before anything asks whether a callback exists -
/// so a drain that delivered nothing still acknowledges everything it pulled. That is right for a
/// transport, whose ack means "arrived" and not "understood", and it is exactly the shape a reader
/// tidies into an ack per delivered message.
///
/// THREE OF THE FOUR OUTCOMES LOOK LIKE THE SAME LINE FROM OUTSIDE. Too short, unknown type and no
/// callback all end with the entry freed and nothing handed on. They are separated here because
/// only one of them is a packet the port should ever see: an unknown type is a console sending
/// something this build does not model, and a missing callback is this build's own wiring.
///
/// AND THE SHORT-PAYLOAD BRANCH IS UNREACHABLE. The one push site refuses under nine already -
/// PP491 is the line that made that refusal free its buffer - so this is a second guard on a fact
/// enforced upstream. It is modelled as dead rather than dropped, because a reorder queue stands
/// between the two and nothing about the drain's own code says the invariant holds.
/// </summary>
public static class TakionDataDrain
{
    /// <summary>The nine-byte header every data payload carries before its body.</summary>
    public const int HeaderSize = 9;

    /// <summary>Where the type byte sits inside the payload.</summary>
    public const int DataTypeOffset = 8;

    /// <summary>The reserved halfword the C warns about and does nothing else with.</summary>
    public const int ReservedOffset = 6;

    /// <summary>The four types the drain hands on.</summary>
    public static IReadOnlySet<TakionDataType> KnownTypes { get; } =
        new HashSet<TakionDataType>(Enum.GetValues<TakionDataType>());

    /// <summary>Whether a type byte is one the drain will deliver.</summary>
    public static bool IsKnown(byte dataType) => KnownTypes.Contains((TakionDataType)dataType);

    /// <summary>
    /// Drains a queue's worth of entries, in the order the queue would hand them over.
    /// </summary>
    /// <param name="entries">What the queue yields, already in sequence order.</param>
    /// <param name="hasCallback">
    /// The C's `takion->cb`. False is not an error path: the delivery is the else-if's BODY, so a
    /// session with no callback drops every known-type message and still acks it.
    /// </param>
    public static TakionDrainOutcomeSet Drain(
        IEnumerable<TakionDataEntry> entries, bool hasCallback = true)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var outcomes = new List<TakionDrainOutcome>();
        var deliveries = new List<TakionDelivery>();
        var acked = false;
        uint ackSeqNum = 0;
        var nonzeroAtSix = 0;

        foreach (TakionDataEntry entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry.Payload);

            // Set on the pull, which is what makes the ack cover entries nothing was done with.
            acked = true;
            ackSeqNum = entry.SeqNum;

            if (entry.Payload.Length < HeaderSize)
            {
                outcomes.Add(TakionDrainOutcome.TooShort);
                continue;
            }

            // Read and warned about, and that is all the C does with it.
            if (entry.Payload[ReservedOffset] != 0 || entry.Payload[ReservedOffset + 1] != 0)
                nonzeroAtSix++;

            byte dataType = entry.Payload[DataTypeOffset];
            if (!IsKnown(dataType))
            {
                outcomes.Add(TakionDrainOutcome.UnknownType);
                continue;
            }

            if (!hasCallback)
            {
                outcomes.Add(TakionDrainOutcome.NoCallback);
                continue;
            }

            deliveries.Add(new TakionDelivery(
                (TakionDataType)dataType, entry.Payload[HeaderSize..]));
            outcomes.Add(TakionDrainOutcome.Delivered);
        }

        return new TakionDrainOutcomeSet(outcomes, deliveries, acked, ackSeqNum, nonzeroAtSix);
    }
}

/// <summary>
/// PP493: the C's own spelling of the drain, so the ack's placement is asserted and not recalled.
/// </summary>
public static class TakionDataDrainSource
{
    /// <summary>takion.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(TakionPostpone.RelativePath);

    /// <summary>The drain.</summary>
    public static string? FlushBody(string source)
        => CFunction.Body(source, "static void takion_flush_data_queue");

    /// <summary>
    /// Whether the ack flag is still set on the pull rather than on a delivery.
    ///
    /// The whole claim. If `ack = true` moves below the size check the drain stops acknowledging
    /// what it dropped, and the console retransmits a packet this client already threw away.
    /// </summary>
    public static bool TheAckFlagIsSetOnThePull(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        string text = flushBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int pull = text.IndexOf("bool pulled = chiaki_reorder_queue_pull", StringComparison.Ordinal);
        int flag = text.IndexOf("ack = true;", StringComparison.Ordinal);
        int sizeCheck = text.IndexOf("if(entry->payload_size < 9)", StringComparison.Ordinal);

        return pull >= 0 && flag > pull && sizeCheck > flag;
    }

    /// <summary>
    /// Whether the ack is still sent once, after the loop, with the loop's own sequence number.
    ///
    /// Inside the loop it would be one ack per packet on a channel that sends thousands.
    /// </summary>
    public static bool TheAckIsSentOnceAfterTheLoop(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        string text = flushBody.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string send = "chiaki_takion_send_message_data_ack(takion, (uint32_t)seq_num);";

        // One call site, and it is the body of `if(ack)`. A second one anywhere would be an ack per
        // packet on a channel that sends thousands, and a brace-counting check would not see it.
        int first = text.IndexOf(send, StringComparison.Ordinal);
        if (first < 0 || text.IndexOf(send, first + send.Length, StringComparison.Ordinal) >= 0)
            return false;

        int guard = text.IndexOf("if(ack)", StringComparison.Ordinal);

        return guard >= 0 && guard < first && text[guard..first].Trim('\n', '\t', ' ') == "if(ack)";
    }

    /// <summary>
    /// Whether the delivery is still the else-if's body, so a missing callback drops the message.
    ///
    /// Written as `else if(takion->cb)` and not as a guard around the callback alone, which is why
    /// NoCallback is an outcome of the drain rather than a state of the session.
    /// </summary>
    public static bool TheCallbackIsTheElseIf(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);
        return flushBody.Contains("else if(takion->cb)", StringComparison.Ordinal);
    }

    /// <summary>The four data types the drain accepts, as the C names them.</summary>
    public static bool TheFourTypesAreStillTheOnesAccepted(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        return flushBody.Contains("CHIAKI_TAKION_MESSAGE_DATA_TYPE_PROTOBUF", StringComparison.Ordinal)
            && flushBody.Contains("CHIAKI_TAKION_MESSAGE_DATA_TYPE_RUMBLE", StringComparison.Ordinal)
            && flushBody.Contains("CHIAKI_TAKION_MESSAGE_DATA_TYPE_TRIGGER_EFFECTS", StringComparison.Ordinal)
            && flushBody.Contains("CHIAKI_TAKION_MESSAGE_DATA_TYPE_PAD_INFO", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the short-payload branch here is still the second guard on a fact the push enforces.
    ///
    /// Both halves, because the branch being dead is the claim: this function refuses under nine,
    /// and so does the one push site. If the push site's guard ever goes, this stops being dead and
    /// the model's note about it becomes wrong in the safe direction.
    /// </summary>
    public static bool TheShortPayloadBranchIsGuardedUpstreamToo(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (FlushBody(source) is not { } flush
            || CFunction.Body(source, "static void takion_handle_packet_message_data") is not { } push)
        {
            return false;
        }

        return flush.Contains("if(entry->payload_size < 9)", StringComparison.Ordinal)
            && push.Contains("if(payload_size < 9)", StringComparison.Ordinal);
    }
}
