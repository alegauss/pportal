using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: what the core actually asks curl to do, which is ten options and not four hundred sites.
///
/// `roadkeep remaining PP33` counts 420 call sites across 45 files and that number is what stops
/// "cheapest task in the block" being read as "small". It is also the wrong number for planning: a
/// translation is sized by the BEHAVIOURS it has to reproduce, and there are ten. Seven of them
/// HttpClient does without being asked. Three do not have a straightforward equivalent, and each
/// one is a place a port that reached for HttpClient would be quietly wrong:
///
/// 1. CURLOPT_FAILONERROR, on twelve of the transfers. Curl FAILS the transfer for a response of
///    400 or above and hands back no body. HttpClient returns the response and leaves the status to
///    the caller, so the same 404 is an error in one client and a successful transfer with a body
///    in the other - and every one of those twelve call sites is written expecting the first.
///
/// 2. CURLOPT_CONNECT_ONLY set to 2, once. The 2 is not a boolean: 1 is connect-and-hand-back-the
///    socket and 2 is curl's WEBSOCKET mode. So one of the 420 sites is not an HTTP transfer at
///    all, and its equivalent is ClientWebSocket rather than anything on HttpClient.
///
/// 3. CURLOPT_SHARE, on nine. A share handle pools DNS, connections and cookies ACROSS easy
///    handles. HttpClient pools inside one handler, so the equivalent is one long-lived instance -
///    and the shape a port reaches for first, a client per request, is the one that throws the
///    pooling away and exhausts sockets under the hole-punching retry loop.
///
/// Nothing here performs a transfer. This is the inventory the translation is planned from, and the
/// three findings asserted so that they are decided once rather than rediscovered per call site.
/// </summary>
public static class CurlSemantics
{
    /// <summary>
    /// The status at which CURLOPT_FAILONERROR turns a response into a failed transfer. Curl's own
    /// threshold, and inclusive: 400 fails, 399 does not.
    /// </summary>
    public const int FailOnErrorStatus = 400;

    /// <summary>
    /// Whether curl, with FAILONERROR set, would have reported this response as a failed transfer
    /// rather than handing the body back.
    ///
    /// Written as a predicate rather than left to each call site, because the twelve sites that set
    /// the option all assume it and none of them re-states it.
    /// </summary>
    public static bool WouldFailTransfer(int httpStatus) => httpStatus >= FailOnErrorStatus;

    /// <summary>
    /// What CURLOPT_CONNECT_ONLY means at each of its values. Only 2 appears in this core, and 2 is
    /// the one a reader takes for a boolean.
    /// </summary>
    public static string ConnectOnlyMeaning(long value) => value switch
    {
        0 => "an ordinary transfer",
        1 => "connect and hand back the socket, no protocol",
        2 => "a WebSocket, which is not an HTTP transfer at all",
        _ => "undefined",
    };

    /// <summary>The value the core uses, which is the WebSocket one.</summary>
    public const long ConnectOnlyWebSocket = 2;
}

/// <summary>
/// PP33: the inventory where the Qt core states it, so a new option is a red test.
///
/// The point of counting behaviours rather than sites is that the count is small enough to hold.
/// It stays useful only while it is true: an eleventh option appearing in lib/src is a behaviour
/// nobody has decided about, and it should surface as a failing check rather than as a surprise
/// halfway through the translation.
/// </summary>
public static partial class CurlSemanticsSource
{
    /// <summary>Where the transfers are set up. Every one of them is under lib/src.</summary>
    public const string CoreGlob = @"lib\src";

    /// <summary>The directory, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(CoreGlob);

    /// <summary>
    /// Every CURLOPT_ the core sets, with how many times. Read rather than remembered - the whole
    /// value of an inventory is that it is not a memory of one afternoon.
    /// </summary>
    public static IReadOnlyDictionary<string, int> OptionsUsed(string coreDirectory)
    {
        ArgumentNullException.ThrowIfNull(coreDirectory);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(coreDirectory, "*.c", SearchOption.AllDirectories))
        {
            foreach (Match match in OptionRegex().Matches(File.ReadAllText(file)))
            {
                string option = match.Groups[1].Value;
                counts[option] = counts.TryGetValue(option, out int seen) ? seen + 1 : 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// The three that HttpClient does not do by itself. Named here so the check below is about
    /// this list rather than about whatever the scan happens to find.
    /// </summary>
    public static IReadOnlyList<string> WithoutAPlainEquivalent { get; } =
    [
        "CURLOPT_FAILONERROR",
        "CURLOPT_CONNECT_ONLY",
        "CURLOPT_SHARE",
    ];

    [GeneratedRegex(@"curl_easy_setopt\([^,]+,\s*(CURLOPT_[A-Z0-9_]+)")]
    private static partial Regex OptionRegex();
}
