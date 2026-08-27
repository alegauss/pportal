using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the ctrl thread does with a rudp message of a given subtype.</summary>
public enum RudpAction
{
    /// <summary>Acknowledge the packet it names, and then do what 0x02 does.</summary>
    AckPacketThenTake,

    /// <summary>Acknowledge the message and take any ctrl bytes it carries.</summary>
    AckThenTake,

    /// <summary>Acknowledge the packet it names and nothing else.</summary>
    AckPacketOnly,

    /// <summary>The console is finishing. The channel ends.</summary>
    Finish,

    /// <summary>Unknown: acknowledge both ways, then take ctrl bytes from a fixed offset.</summary>
    UnknownThenTake,
}

/// <summary>
/// PP361, under PP294: the rudp subtype switch, which says out loud that it is wrong.
///
/// The comment on it is upstream's: <c>switch(message.subtype) // wrong but works ...</c>. So this
/// is the one place in the file where a port cannot claim to reproduce intent - only behaviour, and
/// the behaviour includes fallthroughs that are deliberate.
///
/// THREE SUBTYPES FALL INTO A FOURTH. 0x12, 0x26 and 0x36 acknowledge the packet their payload
/// names and then drop through into 0x02 with no break, so each of them also acknowledges the
/// message and takes whatever ctrl bytes it carried. A port writing four independent arms would
/// stop acknowledging three of them and stop reading their payloads, and would look tidier.
///
/// THE DATA OFFSET IS PER-SUBTYPE for the ones that fall through and FIXED at four for the unknown
/// arm - 8 for 0x12, 6 for 0x26, 2 for everything else. The unknown arm cannot ask, because the
/// subtype is what it does not recognise, so it assumes the smallest header it has already checked
/// for.
/// </summary>
public static class CtrlRudpSubtypes
{
    /// <summary>The offset ctrl bytes start at, by subtype. Two is the default.</summary>
    public static int DataOffsetFor(byte subtype) => subtype switch
    {
        0x12 => 8,
        0x26 => 6,
        _ => 2,
    };

    /// <summary>The offset the unknown arm assumes, which it cannot derive.</summary>
    public const int UnknownDataOffset = 4;

    /// <summary>What a subtype does.</summary>
    public static RudpAction ActionFor(byte subtype) => subtype switch
    {
        // The three that fall through, and what they fall into.
        0x12 or 0x26 or 0x36 => RudpAction.AckPacketThenTake,
        0x02 => RudpAction.AckThenTake,

        0x24 => RudpAction.AckPacketOnly,
        0xC0 => RudpAction.Finish,
        _ => RudpAction.UnknownThenTake,
    };

    /// <summary>Whether this subtype ends up taking ctrl bytes out of the message.</summary>
    public static bool TakesCtrlBytes(byte subtype)
        => ActionFor(subtype) is RudpAction.AckPacketThenTake
            or RudpAction.AckThenTake
            or RudpAction.UnknownThenTake;

    /// <summary>
    /// PP413: whether this arm reads an ack counter off the wire, at <c>message.data + 2</c>.
    ///
    /// Four do. The unknown arm does not, and cannot: the subtype is what it does not recognise, so
    /// a two-byte read at a fixed offset would be inventing a layout for the one case defined by not
    /// having one.
    /// </summary>
    public static bool ReadsAnAckCounter(byte subtype)
        => ActionFor(subtype) is RudpAction.AckPacketThenTake or RudpAction.AckPacketOnly;

    /// <summary>
    /// PP413: whether this arm acknowledges a PACKET, which prunes our own resend buffer.
    ///
    /// THE PROPERTY WORTH HAVING A NAME FOR: this is <see cref="ReadsAnAckCounter"/> exactly. An arm
    /// may only acknowledge a packet number it read. The unknown arm used to acknowledge
    /// <c>ack_counter</c> without reading it - so it carried whatever a sibling submessage of the
    /// same datagram left there, and zero where there was none.
    ///
    /// Zero is not a harmless value. <c>chiaki_rudp_send_buffer_ack</c> frees every buffered packet
    /// at or older than the acknowledged seqnum, and <see cref="SeqNum.Lt"/> against zero is true
    /// for 32769 through 65535 - so one unrecognised submessage past the halfway mark discarded
    /// nearly half the resend buffer, and any packet in there the console never received was never
    /// retransmitted.
    ///
    /// Note this acknowledgement sends NOTHING. <c>chiaki_rudp_send_ack_message</c> is what the
    /// console sees, and the unknown arm still sends it, off <c>message.remote_counter</c> - which it
    /// did read. So removing the packet ack changes nothing on the wire.
    /// </summary>
    public static bool AcksAPacket(byte subtype) => ReadsAnAckCounter(subtype);

    /// <summary>
    /// How many 16-bit seqnums an acknowledgement of <paramref name="acked"/> would prune.
    ///
    /// The acked one plus every older one, by RFC 1982 comparison. This is what makes zero the worst
    /// possible accident rather than a no-op, and it is computed from <see cref="SeqNum"/> rather
    /// than asserted in prose.
    /// </summary>
    public static int SeqNumsPrunedByAcking(ushort acked)
    {
        var pruned = 1; // the acked one itself
        for (var candidate = 0; candidate <= ushort.MaxValue; candidate++)
        {
            if (candidate != acked && SeqNum.Lt((ushort)candidate, acked))
                pruned++;
        }

        return pruned;
    }

    /// <summary>
    /// Whether the message's ctrl payload is consistent with its own length, which is the only
    /// thing checked before the bytes are taken.
    ///
    /// PP347 added the second check - that they also fit - and this is the first one: the announced
    /// ctrl payload size must equal the message length less the offset and less the eight-byte ctrl
    /// header.
    /// </summary>
    public static bool IsWellFormed(int dataSize, int offset, uint announcedCtrlPayload)
        => dataSize - offset >= CtrlFraming.HeaderSize
            && dataSize - offset - CtrlFraming.HeaderSize == announcedCtrlPayload;
}

/// <summary>
/// PP361: the microphone toggle, whose third byte is the mute flag and whose log said the opposite.
/// </summary>
public static class CtrlMicrophone
{
    /// <summary>The payload a toggle carries. The third byte is the flag; 89 is 0x59.</summary>
    public static byte[] TogglePayload(bool muted) => [0, 1, muted ? (byte)0 : (byte)1, 89];

    /// <summary>The payload a connect carries, which says nothing.</summary>
    public static byte[] ConnectPayload() => [0, 0];

    /// <summary>
    /// What a toggle's payload means, read back - so the word and the byte cannot disagree again.
    /// </summary>
    public static bool MutedIn(ReadOnlySpan<byte> payload) => payload.Length >= 3 && payload[2] == 0;

    /// <summary>The word a log should use for a payload, derived from the payload.</summary>
    public static string WordFor(ReadOnlySpan<byte> payload) => MutedIn(payload) ? "mute" : "unmute";
}

/// <summary>
/// PP361: both halves held against ctrl.c.
/// </summary>
public static class CtrlRudpSubtypesSource
{
    /// <summary>Where they live.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether the three subtypes still fall through into 0x02 rather than breaking.
    ///
    /// Written as the absence of a break between them: the arms are adjacent and the fallthrough is
    /// what makes three of them acknowledge and read at all.
    /// </summary>
    public static bool TheThreeStillFallThrough(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        int first = threadBody.IndexOf("case 0x12:", StringComparison.Ordinal);
        int second = threadBody.IndexOf("case 0x02:", StringComparison.Ordinal);
        if (first < 0 || second < first)
            return false;

        return !threadBody[first..second].Contains("break;", StringComparison.Ordinal);
    }

    /// <summary>Whether upstream still admits the switch is the wrong shape.</summary>
    public static bool TheSwitchStillSaysItIsWrong(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        return threadBody.Contains("switch(message.subtype) // wrong but works", StringComparison.Ordinal);
    }

    /// <summary>Whether the unknown arm still assumes a fixed offset of four.</summary>
    public static bool TheUnknownArmStillAssumesFour(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        return threadBody.Contains("int offset2 = 4;", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP413: whether the unknown arm still acknowledges no packet.
    ///
    /// Read as the absence of a <c>chiaki_rudp_ack_packet</c> between the <c>default:</c> label and
    /// the <c>break;</c> that ends the arm. PP400's rule applies - the comment explaining the removal
    /// names the call, so comments are stripped before the absence is claimed, or the explanation
    /// would satisfy the search it exists to describe.
    ///
    /// The arm's own send-ack is asserted present in the same reading: "acknowledges no packet" must
    /// not be satisfiable by an arm that stopped acknowledging altogether.
    /// </summary>
    public static bool TheUnknownArmStillAcksNoPacket(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        string code = CCall.Code(threadBody);

        int arm = code.IndexOf("default:", StringComparison.Ordinal);
        if (arm < 0)
            return false;

        string body = code[arm..];
        int ends = body.IndexOf("break;", StringComparison.Ordinal);
        if (ends < 0)
            return false;

        string arms = body[..ends];

        return !CCall.Happens(arms, "chiaki_rudp_ack_packet(ctrl->session->rudp, ack_counter)")
            && CCall.Happens(arms, "chiaki_rudp_send_ack_message(ctrl->session->rudp, remote_counter)");
    }

    /// <summary>
    /// And whether the arms that DO acknowledge a packet still read the counter first.
    ///
    /// The other half of the rule. Both arms take it from <c>message.data + 2</c> before acking, and
    /// an arm that acked without reading is exactly what PP413 removed.
    /// </summary>
    public static bool EveryPacketAckStillFollowsARead(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        string code = CCall.Compact(CCall.Code(threadBody));

        const string read = "ack_counter=ntohs(";
        const string ack = "chiaki_rudp_ack_packet(ctrl->session->rudp,ack_counter)";

        // Walked in pairs rather than counted: each ack must have a read of its OWN since the
        // previous ack. Counting alone would pass an arm that acked twice off one read, and looking
        // only backwards would pass an ack borrowing the read of the arm above it - which is the
        // shape PP413 removed.
        var at = 0;
        var acks = 0;
        while (true)
        {
            int nextAck = code.IndexOf(ack, at, StringComparison.Ordinal);
            if (nextAck < 0)
                break;

            int nextRead = code.IndexOf(read, at, StringComparison.Ordinal);
            if (nextRead < 0 || nextRead > nextAck)
                return false;

            acks++;
            at = nextAck + ack.Length;
        }

        return acks == 2;
    }

    /// <summary>
    /// Whether the microphone toggle's log word still agrees with the byte it writes.
    ///
    /// The two are three lines apart and disagreed for as long as anybody looked.
    /// </summary>
    public static bool TheMicrophoneLogStillAgreesWithTheByte(string toggleBody)
    {
        ArgumentNullException.ThrowIfNull(toggleBody);

        int word = toggleBody.IndexOf("muted ? \"mute\"", StringComparison.Ordinal);
        int flag = toggleBody.IndexOf("toggle[2] = 0;", StringComparison.Ordinal);

        return word >= 0 && flag > word;
    }
}
