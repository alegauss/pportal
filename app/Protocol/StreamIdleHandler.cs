using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the idle handler does with one protobuf.</summary>
public enum IdleAction
{
    /// <summary>Nothing. The default arm, which every unnamed type reaches.</summary>
    Ignore,

    /// <summary>The remote hung up: routed to the disconnect handler.</summary>
    Disconnect,

    /// <summary>A connection quality report, which is the one arm that reads anything.</summary>
    ReadQuality,

    /// <summary>The console reporting a corrupt frame back, which the C logs and drops.</summary>
    LogCorruptFrame,

    /// <summary>The console acknowledging this side's streaminfo, likewise.</summary>
    LogStreamInfoAck,
}

/// <summary>What one connection quality report left behind.</summary>
/// <param name="ReportedRttMicroseconds">
/// The round trip the console reported, in microseconds, or what was already there where this report
/// carried nothing usable.
/// </param>
/// <param name="Accepted">Whether this report's own round trip was the one taken.</param>
public readonly record struct QualityReading(ulong ReportedRttMicroseconds, bool Accepted);

/// <summary>
/// PP688, under PP295: the handler a running stream spends its life in.
///
/// PP366 modelled which handler a protobuf reaches and stopped at the door. Two of the three are
/// ported - the bang by PP424, the streaminfo by PP686 - and this is the third, which everything
/// after setup arrives at. Its switch has four arms and a default that does nothing.
///
/// THE ROUND TRIP IS MILLISECONDS, AND THAT IS A MEASUREMENT. The C's comment records it rather
/// than assuming it: over a session the console reported 36 to 295 while ICMP to the same console
/// measured 3 to 31, so the two are the same order and the reported number sits just above the
/// floor, which is what an application-level round trip does. Read as microseconds it would be forty
/// times faster than ICMP on the same link. Hence <see cref="RttMicrosecondsPerUnit"/>.
///
/// AND A ZERO IS NOT A ROUND TRIP OF NO TIME. It is the console saying it has nothing yet, so it is
/// left out rather than allowed to erase the last real reading. That is the half a port drops,
/// because a guard against zero reads as defensiveness until you know what the zero means - and the
/// same guard covers the values a double can carry that a duration cannot.
///
/// WHAT IS NOT HERE is the logging, and the bitrate's own reading: that one asks the frame
/// processor's statistics and RESETS them, so it belongs to whatever owns those rather than to a
/// decision about a message. <see cref="TheBitrateReadResets"/> records that it does.
/// </summary>
public static class StreamIdleHandler
{
    /// <summary>What the reported round trip is multiplied by to reach microseconds.</summary>
    public const double RttMicrosecondsPerUnit = 1000.0;

    /// <summary>
    /// Which arm one message takes, by its payload type.
    ///
    /// Everything unnamed reaches the default, which does nothing - reproduced rather than
    /// tightened, because a message the console sends and this port drops is a decision the C
    /// already made and a port that logged it would differ in the one place a reader looks.
    /// </summary>
    public static IdleAction Route(ushort payloadType) => payloadType switch
    {
        StreamMessages.DisconnectType => IdleAction.Disconnect,
        ConnectionQualityType => IdleAction.ReadQuality,
        StreamMessages.CorruptFrameType => IdleAction.LogCorruptFrame,
        StreamExchangeParticipant.StreamInfoAckType => IdleAction.LogStreamInfoAck,
        _ => IdleAction.Ignore,
    };

    /// <summary>tkproto_TakionMessage_PayloadType_CONNECTIONQUALITY.</summary>
    public const ushort ConnectionQualityType = 16;

    /// <summary>The four the switch names, which is what a source check counts.</summary>
    public static IReadOnlyList<ushort> Handled { get; } =
    [
        StreamMessages.DisconnectType,
        ConnectionQualityType,
        StreamMessages.CorruptFrameType,
        StreamExchangeParticipant.StreamInfoAckType,
    ];

    /// <summary>
    /// What one report does to the reported round trip.
    /// </summary>
    /// <param name="rtt">The console's own field, in milliseconds.</param>
    /// <param name="lastReported">
    /// What was reported before, in microseconds. A report carrying nothing usable leaves it, which
    /// is the whole of the rule: the last real reading survives a console that has none.
    /// </param>
    public static QualityReading ReadQuality(double rtt, ulong lastReported)
    {
        // isfinite(q.rtt) && q.rtt > 0.0, and the order does not matter because both have to hold.
        // NaN fails the comparison as well as the finiteness test, which is why the C's guard is not
        // redundant on one arm - an infinity is comparable and would otherwise be taken.
        if (!double.IsFinite(rtt) || rtt <= 0.0)
            return new QualityReading(lastReported, Accepted: false);

        return new QualityReading((ulong)(rtt * RttMicrosecondsPerUnit), Accepted: true);
    }
}

/// <summary>
/// PP688: the switch and its one rule, held against the file they came from.
/// </summary>
public static class StreamIdleHandlerSource
{
    /// <summary>Where the handler lives.</summary>
    public const string RelativePath = StreamInfoMessageSource.RelativePath;

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The idle handler's body, or null where it is gone.</summary>
    public static string? HandlerBody(string streamCore)
        => CFunction.Body(streamCore, "static void stream_connection_takion_data_idle(");

    /// <summary>
    /// The payload types the switch names, in the order it names them.
    ///
    /// Read as the tkproto constants the file writes, so a fifth arm appearing is a count that
    /// disagrees rather than a case nothing noticed.
    /// </summary>
    public static IReadOnlyList<string> CasesIn(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        const string prefix = "case tkproto_TakionMessage_PayloadType_";
        var found = new List<string>();

        for (int at = handlerBody.IndexOf(prefix, StringComparison.Ordinal);
             at >= 0;
             at = handlerBody.IndexOf(prefix, at + prefix.Length, StringComparison.Ordinal))
        {
            int from = at + prefix.Length;
            int colon = handlerBody.IndexOf(':', from);
            if (colon < 0)
                break;

            found.Add(handlerBody[from..colon].Trim());
        }

        return found;
    }

    /// <summary>
    /// Whether the round trip is still guarded by BOTH halves - finite and above zero.
    ///
    /// One without the other is a different rule: without the finiteness test an infinity becomes a
    /// reading, and without the comparison a zero erases the last real one.
    /// </summary>
    public static bool TheRoundTripIsStillGuardedBothWays(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        return handlerBody.Contains("isfinite(q.rtt) && q.rtt > 0.0", StringComparison.Ordinal);
    }

    /// <summary>Whether the conversion is still a thousand, which is what makes the field milliseconds.</summary>
    public static bool TheConversionIsStillAThousand(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        return handlerBody.Contains("(uint64_t)(q.rtt * 1000.0)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the bitrate reading still resets the statistics it read.
    ///
    /// The reset is what makes the number per message. Without it the same bytes are counted into
    /// every subsequent report and the bitrate climbs for the length of the session.
    /// </summary>
    public static bool TheBitrateReadResets(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        int read = handlerBody.IndexOf("chiaki_stream_stats_bitrate(", StringComparison.Ordinal);
        int reset = handlerBody.IndexOf("chiaki_stream_stats_reset(", StringComparison.Ordinal);

        return read >= 0 && reset > read;
    }

    /// <summary>
    /// Whether the default arm still does nothing, which is what makes an unnamed type dropped
    /// rather than handled.
    /// </summary>
    public static bool TheDefaultStillDoesNothing(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        int at = handlerBody.IndexOf("default:", StringComparison.Ordinal);
        if (at < 0)
            return false;

        // Everything from the default to the end of the switch, which is a break and a brace.
        string tail = handlerBody[at..].ReplaceLineEndings("\n");
        string[] lines = [.. tail.Split('\n').Select(line => line.Trim())
            .Where(line => line.Length > 0 && line != "default:")];

        return lines.Length > 0 && lines[0] == "break;";
    }
}
