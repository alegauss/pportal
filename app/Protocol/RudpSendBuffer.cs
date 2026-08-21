using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: libchiaki's wrapping sequence comparison, which is not a total order.
///
/// <c>a</c> is less than <c>b</c> when the gap forward is under half the space. At EXACTLY half the
/// space the two guards disagree by one comparison - one is strictly-less, the other is
/// strictly-greater - so 0 and 32768 are neither equal nor either-way-round. Every other pair is
/// ordered. A port using a plain <c>&lt;</c> would order them, and a port using subtraction with a
/// <c>&lt;=</c> would order them the other way; both are wrong in the same one place.
/// </summary>
public static class SeqNum16
{
    /// <summary>Half the sequence space, which is where the comparison stops answering.</summary>
    public const int Half = 1 << 15;

    /// <summary>Whether <paramref name="a"/> comes before <paramref name="b"/>.</summary>
    public static bool LessThan(ushort a, ushort b)
    {
        if (a == b)
            return false;

        int d = b - a;
        return (a < b && d < Half) || (a > b && -d > Half);
    }
}

/// <summary>What a push did.</summary>
public enum RudpPushResult
{
    /// <summary>Queued for acknowledgement.</summary>
    Ok,

    /// <summary>The buffer is full.</summary>
    Overflow,

    /// <summary>That sequence number is already in it.</summary>
    Duplicate,
}

/// <summary>One packet waiting to be acknowledged.</summary>
public sealed class RudpSentPacket
{
    /// <summary>Its sequence number.</summary>
    public ushort SeqNum { get; init; }

    /// <summary>The bytes, kept so they can go again.</summary>
    public byte[] Payload { get; init; } = [];

    /// <summary>How many times it has been sent again.</summary>
    public int Tries { get; set; }

    /// <summary>When it last went out.</summary>
    public long LastSendMs { get; set; }
}

/// <summary>
/// PP33: the RUDP send buffer - sixteen packets waiting to be acknowledged, and what happens when
/// one of them never is.
///
/// GIVING UP ON A PACKET ACKNOWLEDGES IT. When a packet exhausts its retries the resend loop does
/// not drop that packet - it calls the ACK path with that sequence number, and the ack path is
/// CUMULATIVE. So every older unacknowledged packet is discarded along with it, silently, as though
/// the console had confirmed them all. One packet timing out takes the queue behind it with it.
///
/// THE ACK IS CUMULATIVE IN WRAPPING ORDER. It removes the sequence number given and everything
/// "less than" it by <see cref="SeqNum16"/>, so an ack for 3 clears 65534 as well - which is right,
/// and is also why the previous paragraph reaches as far as it does.
///
/// THE REWIND AFTER A REMOVAL STEPS BACK ONE, AND THE ACK REMOVED SEVERAL. Having given up on a
/// packet - which just cleared every older one with it - the core steps the index back with
/// <c>if(i &gt; 0) i -= 1;</c>, as though a single element had gone. It skips one packet for every
/// extra removal, and at index zero the guard means it does not step back at all, so it skips one
/// there even when only one was removed.
///
/// Every skipped packet gets its turn on the next wake-up, so this is a delay rather than a loss -
/// but it is a delay nobody asked for, and a port that wrote the loop correctly would resend sooner
/// than the Qt client does.
///
/// A FAILED PUSH FREES THE CALLER'S BUFFER. Overflow and duplicate both fall through to a cleanup
/// that frees the bytes it was handed - so the buffer takes ownership even when it refuses the
/// packet, and a caller that retried a failed push would be sending freed memory. There is nothing
/// to reproduce in managed code; it is pinned by source so the ownership rule stays written down.
///
/// The compaction itself - the core's hand-rolled shift-the-gaps memmove - is NOT transcribed
/// instruction for instruction. What it computes is "remove the matching ones, keep the order", and
/// that is what is here; the alternating-gap cases that would catch a compaction getting it wrong
/// are tested rather than assumed.
/// </summary>
public sealed class RudpSendBuffer
{
    /// <summary>How many packets fit.</summary>
    public const int Size = 16;

    /// <summary>How long before a packet is sent again, in milliseconds.</summary>
    public const int ResendTimeoutMs = 400;

    /// <summary>How often the thread wakes to look, which is half of that.</summary>
    public const int ResendWakeupTimeoutMs = ResendTimeoutMs / 2;

    /// <summary>How many times a packet goes again before it is given up on.</summary>
    public const int ResendTriesMax = 25;

    private readonly List<RudpSentPacket> packets = [];

    /// <summary>What is still waiting, oldest first.</summary>
    public IReadOnlyList<RudpSentPacket> Packets => packets;

    /// <summary>
    /// Queue a packet. A refusal still takes the bytes - see the class note.
    /// </summary>
    public RudpPushResult Push(ushort seqNum, byte[] payload, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (packets.Count >= Size)
            return RudpPushResult.Overflow;

        if (packets.Any(p => p.SeqNum == seqNum))
            return RudpPushResult.Duplicate;

        packets.Add(new RudpSentPacket { SeqNum = seqNum, Payload = payload, LastSendMs = nowMs });
        return RudpPushResult.Ok;
    }

    /// <summary>
    /// Acknowledge up to and including a sequence number, and say which ones that cleared.
    ///
    /// Cumulative, and in wrapping order - so this reaches backwards across the wrap.
    /// </summary>
    public IReadOnlyList<ushort> Ack(ushort seqNum)
    {
        var acked = new List<ushort>();

        for (int i = packets.Count - 1; i >= 0; i--)
        {
            ushort candidate = packets[i].SeqNum;
            if (candidate == seqNum || SeqNum16.LessThan(candidate, seqNum))
            {
                acked.Add(candidate);
                packets.RemoveAt(i);
            }
        }

        acked.Reverse();
        return acked;
    }

    /// <summary>
    /// One pass of the resend loop, returning what went out again.
    ///
    /// The index handling is the core's, skip and all - see the class note.
    /// </summary>
    public IReadOnlyList<ushort> Resend(long nowMs)
    {
        var sent = new List<ushort>();

        for (int i = 0; i < packets.Count; i++)
        {
            RudpSentPacket packet = packets[i];
            if (nowMs - packet.LastSendMs <= ResendTimeoutMs)
                continue;

            if (packet.Tries >= ResendTriesMax)
            {
                // Giving up acknowledges it - and everything older with it.
                Ack(packet.SeqNum);

                // The guard that keeps an unsigned index from wrapping, and its cost.
                if (i > 0)
                    i--;

                continue;
            }

            packet.LastSendMs = nowMs;
            packet.Tries++;
            sent.Add(packet.SeqNum);
        }

        return sent;
    }
}

/// <summary>
/// PP33: the send buffer's rules where the Qt core states them.
/// </summary>
public static class RudpSendBufferSource
{
    /// <summary>Where the buffer lives.</summary>
    public const string RelativePath = @"lib\src\remote\rudpsendbuffer.c";

    /// <summary>And where the wrapping comparison does.</summary>
    public const string SeqNumPath = @"lib\include\chiaki\seqnum.h";

    /// <summary>The source file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The sequence number header, or null outside a checkout.</summary>
    public static string? LocateSeqNum() => SanitizerSource.LocateRelative(SeqNumPath);

    /// <summary>The constants this port copied, and what the core spells them.</summary>
    public static IReadOnlyList<(string Name, string Value)> Constants { get; } =
    [
        ("RUDP_DATA_RESEND_TIMEOUT_MS", "400"),
        ("RUDP_DATA_RESEND_WAKEUP_TIMEOUT_MS", "(RUDP_DATA_RESEND_TIMEOUT_MS/2)"),
        ("RUDP_DATA_RESEND_TRIES_MAX", "25"),
        ("RUDP_SEND_BUFFER_SIZE", "16"),
    ];

    /// <summary>Whether every one of them still holds the value this port was built against.</summary>
    public static bool TheConstantsAreStillTheseValues(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach ((string name, string value) in Constants)
        {
            if (!core.Contains($"#define {name} {value}", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Whether giving up on a packet still goes through the cumulative ack path.</summary>
    public static bool GivingUpStillAcknowledges(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if(packet->tries >= RUDP_DATA_RESEND_TRIES_MAX)", StringComparison.Ordinal)
            && core.Contains(
                "chiaki_rudp_send_buffer_ack(send_buffer, packet->seq_num, ack_seq_nums, &ack_seq_nums_count);",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the ack is still cumulative in wrapping order.</summary>
    public static bool TheAckIsStillCumulative(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "if(send_buffer->packets[i].seq_num == seq_num || chiaki_seq_num_16_lt(send_buffer->packets[i].seq_num, seq_num))",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the rewind still declines to happen at index zero.</summary>
    public static bool TheRewindStillSkipsIndexZero(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if(i > 0)", StringComparison.Ordinal)
            && core.Contains("i-= 1;", StringComparison.Ordinal);
    }

    /// <summary>Whether a refused push still frees the bytes it was handed.</summary>
    public static bool ARefusedPushStillFreesTheBuffer(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int at = core.IndexOf("chiaki_rudp_send_buffer_push", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = core.IndexOf("chiaki_rudp_send_buffer_ack", at, StringComparison.Ordinal);
        if (end < at)
            return false;

        string push = core[at..end];
        // Both refusals reach the same cleanup, and that cleanup frees what it was handed.
        return push.Contains("err = CHIAKI_ERR_OVERFLOW;", StringComparison.Ordinal)
            && push.Contains("err = CHIAKI_ERR_INVALID_DATA;", StringComparison.Ordinal)
            && push.Contains("goto beach;", StringComparison.Ordinal)
            && push.Contains("free(buf);", StringComparison.Ordinal);
    }

    /// <summary>Whether the comparison's two halves still disagree at exactly half the space.</summary>
    public static bool TheComparisonIsStillAsymmetric(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.Contains("(a < b && d < ((ChiakiSeqNum##bits)1 << (bits - 1)))", StringComparison.Ordinal)
            && header.Contains("((a > b) && -d > ((ChiakiSeqNum##bits)1 << (bits - 1)))", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the packet-type names still omit the two offset types - the same two that PP201
    /// found being admitted under a name they do not carry.
    /// </summary>
    public static bool TheNamesStillOmitTheOffsetTypes(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("case CTRL_MESSAGE:", StringComparison.Ordinal)
            && !core.Contains("case OFFSET8:", StringComparison.Ordinal)
            && !core.Contains("case OFFSET10:", StringComparison.Ordinal);
    }
}
