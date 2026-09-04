using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP702, under PP27: every takion symbol senkusha.c calls, and what answers for it.
///
/// PP27's fourth criterion is an end state - takion.c, takionsendbuffer.c and reorderqueue.c leave
/// the build - and senkusha.c is not one of the three. It calls five of takion's exports, so the
/// criterion cannot be met while the file stands, and nothing had counted them.
///
/// PP638's LINKER RUN DID NOT REACH THIS. That one asked what deleting the FRAME path would leave
/// undefined, which is PP295's subject: streamconnection.c, videoreceiver.c, frameprocessor.c and
/// fec.c. Senkusha is a caller of TAKION and was never in that question, so PP669's census of
/// seventeen consumers is right and about something else.
///
/// THE SHAPE IS <see cref="FramePathConsumers"/>'S, deliberately and to the letter - the symbols are
/// READ out of senkusha.c and each is looked up here, so a symbol with no row fails by name and a
/// row with no call fails in the other direction. That is the lesson FecConsumers cost: "one caller"
/// stayed in the prose for two ports after there were three.
///
/// FOUR OF THE FIVE ANSWER TO TAKION'S OWN PORT and one to PP679's. The formatter is the odd one -
/// it lives in takion.c and its only callers are here, which is why PP679 had to decide who owned it
/// before this census could say anything about it.
///
/// WHAT THIS DOES NOT DECIDE is whether senkusha is ported or its four transport calls are answered
/// where they stand. Both leave the criterion satisfiable and they are different amounts of work;
/// the census is what makes that a choice rather than a discovery.
/// </summary>
public static class SenkushaConsumers
{
    /// <summary>senkusha.c, which is the whole subject.</summary>
    public const string RelativePath = @"lib\src\senkusha.c";

    /// <summary>The three files PP27's fourth criterion says leave the build.</summary>
    public static IReadOnlyList<string> Leaving { get; } =
    [
        @"lib\src\takion.c",
        @"lib\src\takionsendbuffer.c",
        @"lib\src\reorderqueue.c",
    ];

    /// <summary>
    /// The five, with what stands where each does.
    ///
    /// A counterpart is a type that resolves and a member that exists, verified by reflection -
    /// never a sentence. Four of them are <see cref="ManagedTakion"/>'s own lifecycle and its send
    /// path; the fifth is the header formatter PP679 gave an owner.
    /// </summary>
    public static IReadOnlyList<ConsumedSymbol> Symbols { get; } =
    [
        new("chiaki_takion_connect", new(CounterpartAssembly.App, "ManagedTakion", "Connect")),
        new("chiaki_takion_close", new(CounterpartAssembly.App, "ManagedTakion", "Dispose")),
        new("chiaki_takion_send_raw", new(CounterpartAssembly.App, "TakionSendPath", "Send")),
        new(
            "chiaki_takion_send_message_data",
            new(CounterpartAssembly.App, "TakionDataDatagrams", "WriteData")),
        new(
            "chiaki_takion_v7_av_packet_format_header",
            new(CounterpartAssembly.App, "AvPacketV7", "FormatHeader")),
    ];

    /// <summary>senkusha.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The takion symbols a C source calls, distinct and in first-call order.
    ///
    /// Comments stripped first, so a paragraph naming a call is not one, and a declaration is not
    /// either - the same distinction <see cref="FramePathConsumers.CallsIn"/> draws one census over.
    /// </summary>
    public static IReadOnlyList<string> CallsIn(string cSource)
    {
        ArgumentNullException.ThrowIfNull(cSource);

        var found = new List<string>();

        foreach (string line in CCall.Code(cSource).Split('\n'))
        {
            if (line.Contains("extern ", StringComparison.Ordinal))
                continue;

            foreach (Match match in TakionCall.Matches(line))
            {
                string symbol = match.Groups["symbol"].Value;
                if (!found.Contains(symbol))
                    found.Add(symbol);
            }
        }

        return found;
    }

    /// <summary>
    /// Anything in takion's namespace, so a call this census does not know about is news.
    ///
    /// Deliberately wider than the five: a pattern listing them could only ever confirm what it was
    /// given, and the failure this exists to catch is a SIXTH call arriving.
    /// </summary>
    private static readonly Regex TakionCall = new(
        @"\b(?<symbol>chiaki_takion_\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
