using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What one attempt of the send-and-wait made of the datagram that came back.</summary>
public enum RudpMatch
{
    /// <summary>The wanted message, possibly after unwrapping.</summary>
    Found,

    /// <summary>Nothing in the chain was it, so the whole thing goes out again.</summary>
    Retry,

    /// <summary>The caller asked to wait for something this exchange cannot wait for.</summary>
    Unsupported,
}

/// <summary>
/// PP33: RUDP's send-and-wait, where the message you asked for is matched by ONE BYTE.
///
/// THE WANTED MESSAGE IS RECOGNISED BY ITS SUBTYPE. Not by its type - by the single byte PP201
/// found being copied out of the type's high half. So the match is a PREFIX: waiting for
/// INIT_RESPONSE (0xD000) accepts any type whose high byte is 0xD0, and waiting for a control
/// message accepts four different types under one name. A port comparing the sixteen-bit type would
/// reject messages the Qt client accepts, and would have no way of knowing which ones it was losing.
///
/// A WRONG MESSAGE IS UNWRAPPED, NOT DISCARDED. When the check fails, the sub-message is promoted
/// over the top of the outer one and the SAME check runs again - so a datagram carrying the wanted
/// message behind two unwanted ones is found on the third look. PP201 established that any eight
/// trailing bytes become a sub-message with no check that they are a frame, so this unwrapping
/// walks whatever happened to be appended, reinterpreting it a frame at a time.
///
/// PROMOTION THROWS THE OUTER PAYLOAD AWAY. The outer message's data is freed before the
/// sub-message is copied over it. Whatever the wrapper was carrying is gone, and nothing has looked
/// at it - so a message can only ever be read at the depth the expectation happens to stop at.
///
/// A MISMATCH COSTS A RETRANSMIT. The retry loop sends again at the top of every attempt, so a
/// datagram that turned out to be the wrong thing does not merely cause another read - the request
/// goes out a second time. The same is true of a message that matched but arrived too short: the
/// size check runs AFTER the type check and its failure re-enters the send, not the receive.
///
/// AND THE FOUR YOU MAY SEND ARE NOT THE FOUR YOU MAY EXPECT. The two switches admit disjoint sets
/// - INIT_REQUEST, COOKIE_REQUEST, ACK and SESSION_MESSAGE going out; INIT_RESPONSE,
/// COOKIE_RESPONSE, CTRL_MESSAGE and FINISH coming back - and no type appears in both. Asking for
/// anything else is refused outright rather than retried, because it is a mistake in the caller and
/// not a bad network.
/// </summary>
public static class RudpExchange
{
    /// <summary>How long one attempt waits for a datagram, in milliseconds.</summary>
    public const int SelectTimeoutMs = 1500;

    /// <summary>The types this exchange knows how to send, and nothing else.</summary>
    public static IReadOnlyList<RudpPacketType> Sendable { get; } =
    [
        RudpPacketType.InitRequest,
        RudpPacketType.CookieRequest,
        RudpPacketType.Ack,
        RudpPacketType.SessionMessage,
    ];

    /// <summary>
    /// The types it knows how to wait for, each with the subtype byte that satisfies it. A null
    /// byte means the match is on the low nibble instead - see <see cref="CtrlNibbles"/>.
    /// </summary>
    public static IReadOnlyDictionary<RudpPacketType, byte?> Expectable { get; } =
        new Dictionary<RudpPacketType, byte?>
        {
            [RudpPacketType.InitResponse] = 0xD0,
            [RudpPacketType.CookieResponse] = 0xA0,
            [RudpPacketType.CtrlMessage] = null,
            [RudpPacketType.Finish] = 0xC0,
        };

    /// <summary>The low nibbles a control message is admitted on - PP201's four types.</summary>
    public static IReadOnlyList<byte> CtrlNibbles => RudpFrame.CtrlSubtypeNibbles;

    /// <summary>Whether one message, on its own, is what was being waited for.</summary>
    public static bool Satisfies(RudpMessage message, RudpPacketType expected)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!Expectable.TryGetValue(expected, out byte? subtype))
            return false;

        return subtype is null
            ? CtrlNibbles.Contains((byte)(message.Subtype & 0x0F))
            : message.Subtype == subtype;
    }

    /// <summary>
    /// One attempt's worth of matching: unwrap until the expectation is met, or run out.
    ///
    /// <paramref name="matched"/> is the message the expectation stopped at, which is not
    /// necessarily the one that arrived.
    /// </summary>
    public static RudpMatch Match(
        RudpMessage received, RudpPacketType expected, int minDataSize, out RudpMessage? matched)
    {
        ArgumentNullException.ThrowIfNull(received);
        matched = null;

        if (!Expectable.ContainsKey(expected))
            return RudpMatch.Unsupported;

        RudpMessage? current = received;
        while (current is not null)
        {
            if (Satisfies(current, expected))
            {
                // The size check comes AFTER the type check, and its failure sends again.
                if (current.Data.Length < minDataSize)
                    return RudpMatch.Retry;

                matched = current;
                return RudpMatch.Found;
            }

            // Promotion: the outer payload is thrown away and the sub-message takes its place.
            current = current.SubMessage;
        }

        return RudpMatch.Retry;
    }

    /// <summary>Whether this exchange can send that type at all.</summary>
    public static bool CanSend(RudpPacketType type) => Sendable.Contains(type);
}

/// <summary>
/// PP33: the exchange's rules where the Qt core states them.
/// </summary>
public static class RudpExchangeSource
{
    /// <summary>Where the exchange lives.</summary>
    public const string RelativePath = @"lib\src\remote\rudp.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether each expectation is still matched on its subtype byte.</summary>
    public static bool TheMatchIsStillOnTheSubtype(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach ((RudpPacketType _, byte? subtype) in RudpExchange.Expectable)
        {
            if (subtype is null)
                continue;

            string check = $"if(message->subtype != 0x{subtype.Value:X2})";
            if (!core.Contains(check, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Whether a wrong message still promotes its sub-message and looks again.</summary>
    public static bool AWrongMessageIsStillUnwrapped(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if(assign_submessage_to_message(message))", StringComparison.Ordinal)
            && core.Contains("continue;", StringComparison.Ordinal);
    }

    /// <summary>Whether promotion still frees the outer payload before overwriting it.</summary>
    public static bool PromotionStillThrowsThePayloadAway(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int at = core.IndexOf("static bool assign_submessage_to_message(RudpMessage *message)", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = core.IndexOf("chiaki_rudp_ack_packet", at, StringComparison.Ordinal);
        if (end < at)
            return false;

        string body = core[at..end];
        return body.Contains("free(message->data);", StringComparison.Ordinal)
            && body.Contains("memcpy(message, message->subMessage, sizeof(RudpMessage));", StringComparison.Ordinal);
    }

    /// <summary>Whether a too-short payload still re-enters the send rather than failing.</summary>
    public static bool ATooShortPayloadStillRetries(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int at = core.IndexOf("if(message->data_size < min_data_size)", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = core.IndexOf("success = true;", at, StringComparison.Ordinal);
        return end > at && core[at..end].Contains("continue;", StringComparison.Ordinal);
    }

    /// <summary>Whether the sendable and expectable sets are still what this port knows.</summary>
    public static bool TheTwoSetsAreStillDisjoint(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int at = core.IndexOf("switch(send_type)", StringComparison.Ordinal);
        int recv = core.IndexOf("switch(recv_type)", StringComparison.Ordinal);
        if (at < 0 || recv < at)
            return false;

        string send = core[at..recv];
        foreach (RudpPacketType type in RudpExchange.Sendable)
        {
            if (!send.Contains($"case {NameOf(type)}:", StringComparison.Ordinal))
                return false;
        }

        // And no expectable type is in the send switch, which is what makes them disjoint.
        foreach (RudpPacketType type in RudpExchange.Expectable.Keys)
        {
            if (send.Contains($"case {NameOf(type)}:", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Whether an unsupported expectation is still refused rather than retried.</summary>
    public static bool AnUnsupportedTypeIsStillRefused(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "Selected RudpPacketType 0x%04x to receive that is not supported by rudp send receive.",
            StringComparison.Ordinal)
            && core.Contains("return CHIAKI_ERR_INVALID_DATA;", StringComparison.Ordinal);
    }

    /// <summary>The core's spelling of a packet type.</summary>
    private static string NameOf(RudpPacketType type) => type switch
    {
        RudpPacketType.InitRequest => "INIT_REQUEST",
        RudpPacketType.InitResponse => "INIT_RESPONSE",
        RudpPacketType.CookieRequest => "COOKIE_REQUEST",
        RudpPacketType.CookieResponse => "COOKIE_RESPONSE",
        RudpPacketType.SessionMessage => "SESSION_MESSAGE",
        RudpPacketType.StreamConnectionSwitchAck => "STREAM_CONNECTION_SWITCH_ACK",
        RudpPacketType.Ack => "ACK",
        RudpPacketType.CtrlMessage => "CTRL_MESSAGE",
        RudpPacketType.Unknown => "UNKNOWN",
        RudpPacketType.Offset8 => "OFFSET8",
        RudpPacketType.Offset10 => "OFFSET10",
        RudpPacketType.Finish => "FINISH",
        _ => "",
    };
}
