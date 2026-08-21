using System.Globalization;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP232: the STUN lists fetched at connect time, and the two rules for reading them.
///
/// <see cref="StunServers"/> is the list compiled in, from stun.h. It is not the only one: before a
/// session starts, two more are fetched from a third-party GitHub repository, and the connection
/// path depends on that repository being up.
///
/// THE TWO PARSERS CUT ON DIFFERENT DELIMITERS, and that is the substance rather than a detail. An
/// IPv4 line splits on a COLON. An IPv6 line cannot - an IPv6 address is made of colons - so it is
/// written bracketed, and the core skips the leading bracket, cuts on the closing one, and skips
/// the colon after that. A port that reused the v4 split would take `[2001` as a host and never say
/// why.
///
/// TEN FROM EACH, which is a fixed stack buffer in the core rather than a policy, and a longer line
/// than the buffer is a strcpy with no length check. The count is reproduced; the overflow is not
/// something managed code can reproduce and is recorded rather than imitated.
/// </summary>
public static class StunServerList
{
    /// <summary>Where the IPv4 list comes from.</summary>
    public const string HostsUrl =
        "https://raw.githubusercontent.com/pradt2/always-online-stun/master/valid_hosts.txt";

    /// <summary>And the IPv6 one, which is a different file with a different format.</summary>
    public const string Ipv6Url =
        "https://raw.githubusercontent.com/pradt2/always-online-stun/master/valid_ipv6s.txt";

    /// <summary>How many of each are kept. A fixed array in the core, so a real bound.</summary>
    public const int Most = 10;

    /// <summary>Seconds the fetch is given, from CURLOPT_TIMEOUT.</summary>
    public const int TimeoutSeconds = 10;

    /// <summary>
    /// The IPv4 list: one `host:port` per line.
    /// </summary>
    /// <remarks>
    /// A line the core cannot split leaves it with what it had so far and an error - it does not
    /// skip the line and continue - so the parse stops at the first bad one, and everything before
    /// it is kept. Reproduced: the count that comes back is what the core would have had.
    /// </remarks>
    public static IReadOnlyList<StunServer> ParseHosts(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var servers = new List<StunServer>();

        foreach (string line in Lines(text))
        {
            // The COLON, which is what an IPv4 line can be cut on and an IPv6 one cannot.
            int at = line.IndexOf(':', StringComparison.Ordinal);
            if (at < 0)
                break;

            string host = line[..at];
            if (host.Length == 0)
                break;

            servers.Add(new StunServer(host, PortOf(line[(at + 1)..])));
        }

        return servers;
    }

    /// <summary>
    /// The IPv6 list: one `[address]:port` per line.
    /// </summary>
    /// <remarks>
    /// The core skips the leading bracket by pointer arithmetic and never checks that it was there,
    /// so a line without one silently loses its first character. That is reproduced as written: a
    /// line not starting with `[` is refused here rather than quietly beheaded, which is the one
    /// place this diverges - the C reads past the end of nothing, and there is no managed value
    /// that means "the byte before the string".
    /// </remarks>
    public static IReadOnlyList<StunServer> ParseIpv6(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var servers = new List<StunServer>();

        foreach (string line in Lines(text))
        {
            if (!line.StartsWith('['))
                break;

            // The CLOSING BRACKET, not a colon: everything inside is the address.
            int close = line.IndexOf(']', StringComparison.Ordinal);
            if (close < 1)
                break;

            string host = line[1..close];

            // And the colon after it is skipped the same way the core skips it.
            string rest = line[(close + 1)..];
            if (!rest.StartsWith(':'))
                break;

            servers.Add(new StunServer(host, PortOf(rest[1..])));
        }

        return servers;
    }

    /// <summary>
    /// The port, as strtol reads it: leading digits, and ZERO where there are none.
    ///
    /// Not refused. strtol answers 0 for text it cannot read and the core stores that without
    /// looking, so a malformed port becomes a server on port zero rather than an error - which is
    /// the kind of entry that fails later, somewhere else, on a connect.
    /// </summary>
    public static ushort PortOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
            digits++;

        if (digits == 0)
            return 0;

        return ushort.TryParse(
            text[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out ushort port)
            ? port
            : (ushort)0;
    }

    /// <summary>
    /// The lines, at most <see cref="Most"/> of them.
    ///
    /// Empty ones are dropped rather than parsed: strtok treats consecutive separators as one, so a
    /// blank line in the middle of the file is not a line at all to the core - and a trailing
    /// newline, which every one of these files has, would otherwise be an eleventh entry.
    /// </summary>
    private static IEnumerable<string> Lines(string text)
        => text.Split('\n')
            .Select(line => line.Trim('\r'))
            .Where(line => line.Length > 0)
            .Take(Most);
}

/// <summary>
/// PP232: the fetched lists where the core reads them.
/// </summary>
public static class StunServerListSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether both lists still come from that repository.</summary>
    public static bool TheListsStillComeFromThere(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains($"\"{StunServerList.HostsUrl}\"", StringComparison.Ordinal)
            && core.Contains($"\"{StunServerList.Ipv6Url}\"", StringComparison.Ordinal);
    }

    /// <summary>Whether ten is still the bound, and still a fixed array rather than a policy.</summary>
    public static bool TenIsStillTheBound(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("char server_strings[10][259];", StringComparison.Ordinal)
            && core.Contains("char server_strings_ipv6[10][47];", StringComparison.Ordinal)
            && core.Contains("session->num_stun_servers <= 9", StringComparison.Ordinal)
            && core.Contains("session->num_stun_servers_ipv6 <= 9", StringComparison.Ordinal);
    }

    /// <summary>Whether the two parsers still cut on different delimiters.</summary>
    public static bool TheDelimitersStillDiffer(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("strtok(server_strings[i], \":\")", StringComparison.Ordinal)
            && core.Contains("strtok(server_strings_ipv6[i], \"]\")", StringComparison.Ordinal);
    }

    /// <summary>And whether the bracket and the colon are still skipped by pointer arithmetic.</summary>
    public static bool TheBracketIsStillSkippedByHand(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("// omit leading [", StringComparison.Ordinal)
            && core.Contains("// omit :", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the IPv6 failures still name the IPv4 URL.
    ///
    /// True means the defect is still there, which is what this asserts. Whoever is debugging a
    /// list that will not load is told the wrong file failed.
    /// </summary>
    public static bool TheIpv6ErrorsStillNameTheWrongUrl(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
                "\"Getting IPV6 stun servers from %s failed with HTTP code %ld\", STUN_HOSTS_URL,",
                StringComparison.Ordinal)
            && core.Contains(
                "\"Getting IPV6 stun servers from %s failed with CURL error %s\", STUN_HOSTS_URL,",
                StringComparison.Ordinal);
    }
}
