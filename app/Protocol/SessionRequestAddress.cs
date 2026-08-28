using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Where the Host line's name came from.</summary>
public enum HostnameSource
{
    /// <summary>getnameinfo resolved the address that answered.</summary>
    Resolved,

    /// <summary>getnameinfo failed and the literal was copied in. The request still goes out.</summary>
    Fallback,

    /// <summary>The punch's selected address, fetched just before the request is built.</summary>
    Punched,

    /// <summary>No address answered, so no request is built at all.</summary>
    None,
}

/// <summary>What the request will name, and over what.</summary>
/// <param name="Source">Where the name came from.</param>
/// <param name="Hostname">The name itself, or null where none was reached.</param>
/// <param name="Port">The port the Host line carries.</param>
/// <param name="OverRudp">Whether the request goes over the rudp channel rather than a TCP socket.</param>
/// <param name="Attempts">How many addresses were tried, for the local arm.</param>
/// <param name="QuitReason">Set where the request never happened.</param>
public readonly record struct SessionRequestTarget(
    HostnameSource Source,
    string? Hostname,
    int Port,
    bool OverRudp,
    int Attempts,
    string? QuitReason);

/// <summary>
/// PP505, under PP340: the two ways a session request names its console.
///
/// One HTTP GET, and how its Host line is filled depends on which path the session took. The arms
/// differ in more than the address.
///
/// THE LOCAL ARM RESOLVES BACKWARDS. It walks host_addrinfos and, for each candidate, calls
/// getnameinfo to write that address into connect_info.hostname BEFORE trying to connect. So the
/// hostname left standing after the loop names whichever address ANSWERED, not the one the caller
/// typed. The first connect that works breaks out; a loop that sets no socket ends the request with
/// SESSION_REQUEST_UNKNOWN - the failure PP339 found a PSN session reaching by falling into this
/// arm with an empty list.
///
/// THE PSN ARM HAS NO LOOP AND NO SOCKET. It sends over the rudp channel, and its address arrives
/// later: the block just above the request asks the punch for the selected address and the ctrl
/// port. The port differs too - the local request always names SESSION_PORT, the PSN one names
/// whatever the punch settled on.
///
/// AND THE LOCAL ARM HAS A FALLBACK THAT IS EASY TO MISS. When getnameinfo fails it copies the
/// literal "unknown" into the hostname and CARRIES ON. The connect has already succeeded by then,
/// so the request goes out with `Host: unknown` over a socket pointing at the right console. It
/// works, and the log line explaining it is far from anything that reads the header.
///
/// Naming all four outcomes is what lets a managed request state which host it is naming rather
/// than assemble one and hope.
/// </summary>
public static class SessionRequestAddress
{
    /// <summary>SESSION_PORT - what the local arm always names.</summary>
    public const int SessionPort = 9295;

    /// <summary>What the C copies in when getnameinfo fails.</summary>
    public const string FallbackHostname = "unknown";

    /// <summary>The reason a local arm that reached no address sets.</summary>
    public const string NoAddressReason = "CHIAKI_QUIT_REASON_SESSION_REQUEST_UNKNOWN";

    /// <summary>
    /// The local arm: try each address in turn, naming it before connecting.
    /// </summary>
    /// <param name="addresses">
    /// The candidates. Each entry is what getnameinfo would return, or null where it would fail.
    /// </param>
    /// <param name="connects">Whether the connect to the address at that index succeeds.</param>
    public static SessionRequestTarget Local(
        IReadOnlyList<string?> addresses, Func<int, bool> connects)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(connects);

        for (var i = 0; i < addresses.Count; i++)
        {
            // Written before the connect is attempted, so a failed attempt leaves its name behind.
            string? resolved = addresses[i];
            HostnameSource source = resolved is null ? HostnameSource.Fallback : HostnameSource.Resolved;
            string hostname = resolved ?? FallbackHostname;

            if (!connects(i))
                continue;

            return new SessionRequestTarget(
                source, hostname, SessionPort, OverRudp: false, Attempts: i + 1, QuitReason: null);
        }

        // Zero candidates and every candidate refusing end the same way, which is what made
        // PP339's failure unreadable: a PSN session in this arm has an empty list.
        return new SessionRequestTarget(
            HostnameSource.None, null, SessionPort, false, addresses.Count, NoAddressReason);
    }

    /// <summary>
    /// The PSN arm: no loop, no socket, and the address fetched from the punch just in time.
    /// </summary>
    /// <param name="selectedAddress">What chiaki_get_ps_selected_addr wrote.</param>
    /// <param name="ctrlPort">What chiaki_get_ps_ctrl_port returned.</param>
    public static SessionRequestTarget Psn(string selectedAddress, int ctrlPort)
    {
        ArgumentException.ThrowIfNullOrEmpty(selectedAddress);

        return new SessionRequestTarget(
            HostnameSource.Punched, selectedAddress, ctrlPort, OverRudp: true, Attempts: 0, null);
    }

    /// <summary>Whether a request is actually built for this outcome.</summary>
    public static bool BuildsARequest(SessionRequestTarget target)
        => target.Source != HostnameSource.None;

    /// <summary>
    /// Whether the Host line names something that identifies a console.
    ///
    /// False for the fallback, and that is the point: the request is well-formed and goes to the
    /// right machine over an already-connected socket, while naming nothing.
    /// </summary>
    public static bool NamesTheConsole(SessionRequestTarget target)
        => target.Source is HostnameSource.Resolved or HostnameSource.Punched;
}

/// <summary>
/// PP505: the C's two arms, so the fallback and the ports are read rather than remembered.
/// </summary>
public static class SessionRequestAddressSource
{
    /// <summary>session.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(PunchProgressSource.SessionRelativePath);

    /// <summary>
    /// Whether the local arm still names each address before it tries to connect to it.
    ///
    /// The order is the claim: resolved-then-connect is what leaves the answering address in the
    /// hostname, and connect-then-resolve would leave the last one tried.
    /// </summary>
    public static bool TheLocalArmNamesBeforeItConnects(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string text = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        int loop = text.IndexOf(
            "for(struct addrinfo *ai=session->connect_info.host_addrinfos; ai; ai=ai->ai_next)",
            StringComparison.Ordinal);
        if (loop < 0)
            return false;

        int name = text.IndexOf("getnameinfo(sa,", loop, StringComparison.Ordinal);
        int connect = text.IndexOf("chiaki_stop_pipe_connect(&session->stop_pipe, session_sock", loop,
            StringComparison.Ordinal);

        return name > loop && connect > name;
    }

    /// <summary>
    /// Whether the fallback still writes the literal and carries on rather than skipping.
    ///
    /// The claim is about the fallback's OWN block: the copy is the last thing in it, so control
    /// leaves through the closing brace and the loop body continues to the connect. It is not that
    /// nothing between here and the connect can `continue` - three later failures do, and each is
    /// about the socket rather than about the name.
    ///
    /// Written by reading to the end of the copy's line and requiring a closing brace next, which
    /// is what "there is no skip here" looks like without claiming more than it should.
    /// </summary>
    public static bool TheFallbackCarriesOn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string text = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        const string guard = "if(r != 0)";
        string copy =
            $"memcpy(session->connect_info.hostname, \"{SessionRequestAddress.FallbackHostname}\", 8);";

        int at = text.IndexOf(copy, StringComparison.Ordinal);
        if (at < 0)
            return false;

        // The guard it sits under, close above it.
        int guarded = text.LastIndexOf(guard, at, StringComparison.Ordinal);
        if (guarded < 0 || text[guarded..at].Contains("continue;", StringComparison.Ordinal))
            return false;

        // And nothing after it inside the block: the next non-blank line closes the brace.
        string after = text[(at + copy.Length)..];
        string next = after
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0)
            ?? string.Empty;

        return next == "}";
    }

    /// <summary>
    /// Whether the PSN arm still takes both the address and the port from the punch, just before
    /// the request is formatted.
    ///
    /// Read as three positions, because the port default sits above them and the snprintf below.
    /// </summary>
    public static bool ThePsnArmTakesAddressAndPortFromThePunch(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string text = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        int def = text.IndexOf("int port = SESSION_PORT;", StringComparison.Ordinal);
        int addr = text.IndexOf(
            "chiaki_get_ps_selected_addr(session->holepunch_session, session->connect_info.hostname);",
            StringComparison.Ordinal);
        int port = text.IndexOf(
            "port = chiaki_get_ps_ctrl_port(session->holepunch_session);", StringComparison.Ordinal);
        int format = text.IndexOf("session_request_fmt,", addr < 0 ? 0 : addr, StringComparison.Ordinal);

        return def >= 0 && addr > def && port > addr && format > port;
    }

    /// <summary>Whether the Host line still carries the hostname and the port, in that order.</summary>
    public static bool TheHostLineCarriesBoth(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Contains("\"Host: %s:%d\\r\\n\"", StringComparison.Ordinal);
    }
}
