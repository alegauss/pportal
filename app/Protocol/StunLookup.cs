namespace ChiakiNg.Protocol;

/// <summary>Which of the three calls inside the lookup ran.</summary>
public enum StunCall
{
    /// <summary>The port allocation test, which also fills in the address.</summary>
    AllocationTest,

    /// <summary>The IPv4 address lookup.</summary>
    Ipv4Lookup,

    /// <summary>And the IPv6 one.</summary>
    Ipv6Lookup,
}

/// <summary>
/// PP259: asking a STUN server what the world sees.
///
/// THE FIRST CALL IGNORES THE FAMILY IT WAS ASKED FOR. One field carries how far a NAT moves its
/// ports, and one sentinel value in it means "not measured yet". While it holds that, this function
/// runs the port allocation test and returns - and the test is handed the IPv4 server list whatever
/// the caller asked for. Only later calls read the family argument and choose between the two lists.
/// So the first lookup of a session is always IPv4, and the parameter that says otherwise is read on
/// every call except that one. <see cref="CallFor"/> takes both and shows which is consulted.
///
/// THE SERVER LIST'S FAILURE IS LOGGED AND STEPPED OVER. Fetching the list can fail and the next
/// statement runs the test anyway, against whatever list is there.
///
/// THREE FAILURES, ONE SENTENCE. All three calls report the same words, so the log names the
/// function and not which of its three attempts failed - see <see cref="FailureMessage"/>, which is
/// one constant on purpose.
///
/// AND THE MAC ADDRESS IS COMMENTED OUT. The function that would fetch it sits below this one as
/// thirty-nine lines of POSIX behind slashes, its forward declaration commented too. Both places
/// that build a connection request zero the field instead; the parser reads one when the console
/// sends it, and the printer skips it when it is zero. PP33 already recorded that this client sends
/// an empty MAC - <see cref="SessionMessageWriter.RouteMacSent"/> - and this is the reason.
/// </summary>
public static class StunLookup
{
    /// <summary>The value meaning the allocation has not been measured yet.</summary>
    public const int NotMeasured = -1;

    /// <summary>
    /// Which call runs, from the allocation field and the family asked for.
    /// </summary>
    public static StunCall CallFor(int allocationIncrement, bool ipv4)
    {
        // The sentinel decides before the family is looked at, which is the whole finding.
        if (allocationIncrement == NotMeasured)
            return StunCall.AllocationTest;

        return ipv4 ? StunCall.Ipv4Lookup : StunCall.Ipv6Lookup;
    }

    /// <summary>Whether the call chosen actually read the family argument.</summary>
    public static bool ReadsTheFamily(StunCall call) => call != StunCall.AllocationTest;

    /// <summary>Which server list the chosen call uses.</summary>
    public static string ListFor(StunCall call)
        => call == StunCall.Ipv6Lookup ? "stun_server_list_ipv6" : "stun_server_list";

    /// <summary>
    /// Whether asking for IPv6 gets IPv6. Not on the first call of a session.
    /// </summary>
    public static bool AnIpv6RequestGetsIpv6(int allocationIncrement)
        => ReadsTheFamily(CallFor(allocationIncrement, ipv4: false));

    /// <summary>The words every failure here reports, whichever call it was.</summary>
    public const string FailureMessage = "get_client_addr_remote_stun: Failed to get external address";

    /// <summary>
    /// Whether a failed server-list fetch stops the lookup. It does not - it is logged and passed.
    /// </summary>
    public const bool AListFailureStops = false;

    /// <summary>The MAC this client sends, which is nothing - PP33's constant, and the reason.</summary>
    public static string MacSent => SessionMessageWriter.RouteMacSent;

    /// <summary>How many lines of the fetcher are commented out.</summary>
    public const int CommentedOutLines = 39;

    /// <summary>How many places build a connection request and zero the field instead. Two.</summary>
    public const int PlacesThatZeroIt = 2;
}

/// <summary>
/// PP259: the lookup where the core writes it.
/// </summary>
public static class StunLookupSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>Whether the sentinel is still what sends the first call to the test.</summary>
    public static bool TheSentinelIsStillWhatBranches(string core)
        => Body(core).Contains(
            $"if(session->stun_allocation_increment == {StunLookup.NotMeasured})", StringComparison.Ordinal);

    /// <summary>
    /// THE FINDING. Whether the test is still handed the IPv4 list with no reference to the family.
    /// </summary>
    public static bool TheTestStillIgnoresTheFamily(string core)
    {
        string body = Body(core);

        int branches = body.IndexOf(
            $"if(session->stun_allocation_increment == {StunLookup.NotMeasured})", StringComparison.Ordinal);
        if (branches < 0)
            return false;

        int decides = body.IndexOf("if(ipv4)", branches, StringComparison.Ordinal);
        if (decides < 0)
            return false;

        string first = body[branches..decides];

        return first.Contains("stun_port_allocation_test(", StringComparison.Ordinal)
            && first.Contains("session->stun_server_list, session->num_stun_servers", StringComparison.Ordinal)
            && !first.Contains("ipv4", StringComparison.Ordinal);
    }

    /// <summary>And whether the later calls still read it.</summary>
    public static bool TheLaterCallsStillReadTheFamily(string core)
    {
        string body = Body(core);

        return body.Contains("session->stun_server_list, session->num_stun_servers, sock, ipv4)", StringComparison.Ordinal)
            && body.Contains(
                "session->stun_server_list_ipv6, session->num_stun_servers_ipv6, sock, ipv4)",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the list's failure is still logged without stopping.</summary>
    public static bool TheListFailureStillDoesNotStop(string core)
    {
        string body = Body(core);

        int fetches = body.IndexOf("err = get_stun_servers(session);", StringComparison.Ordinal);
        if (fetches < 0)
            return false;

        int tests = body.IndexOf("stun_port_allocation_test(", fetches, StringComparison.Ordinal);

        // "return false;", not "return" - the log line between them says "returned error", and a
        // bare substring finds that instead.
        return tests > fetches
            && !body[fetches..tests].Contains("return false;", StringComparison.Ordinal);
    }

    /// <summary>How many failures report the same sentence. Three.</summary>
    public static int HowManySayTheSameThing(string core)
        => Body(core).Split(StunLookup.FailureMessage, StringSplitOptions.None).Length - 1;

    /// <summary>
    /// Whether the MAC fetcher is still commented out in both places.
    /// </summary>
    public static bool TheMacFetcherIsStillCommentedOut(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string[] lines = core.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        int mentions = 0;
        int commented = 0;
        foreach (string line in lines)
        {
            if (!line.Contains("get_mac_addr", StringComparison.Ordinal))
                continue;

            mentions++;
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                commented++;
        }

        // Both the declaration and the definition, and nothing else names it.
        return mentions == 2 && commented == 2;
    }

    /// <summary>And how many lines of it are behind slashes.</summary>
    public static int HowManyLinesAreCommentedOut(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.LastIndexOf(
            "// static bool get_mac_addr(ChiakiLog *log, uint8_t *mac_addr)", StringComparison.Ordinal);
        if (start < 0)
            return 0;

        // To the closing brace, not to the first line that is not a comment: the block has three
        // blank lines in it, and stopping at one of those counts five instead of thirty-nine.
        int count = 0;
        foreach (string line in text[start..].Split('\n'))
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                count++;

            if (string.Equals(trimmed, "// }", StringComparison.Ordinal))
                break;
        }

        return count;
    }

    /// <summary>
    /// Whether both request builders still zero the field the fetcher would have filled.
    /// </summary>
    public static int HowManyPlacesZeroTheMac(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Replace("\r\n", "\n", StringComparison.Ordinal).Split(
            "memset(msg.conn_request->default_route_mac_addr, 0,", StringSplitOptions.None).Length - 1;
    }

    /// <summary>And whether the printer still skips a MAC of all zeros.</summary>
    public static bool ThePrinterStillSkipsAZeroMac(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
            "if(memcmp(zero_bytes0, req->default_route_mac_addr, sizeof(req->default_route_mac_addr)) != 0)",
            StringComparison.Ordinal);
    }

    /// <summary>get_client_addr_remote_stun's body, to its closing brace.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, and spelled as the definition spells it - the lesson PP258 learned the hard way.
        int start = text.LastIndexOf(
            "static bool get_client_addr_remote_stun(Session *session", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\n/**", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
