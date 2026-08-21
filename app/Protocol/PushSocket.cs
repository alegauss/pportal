using System.Net.WebSockets;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: the one call site that is not an HTTP transfer - PSN's push notification WebSocket.
///
/// PP186 found a single CURLOPT_CONNECT_ONLY set to 2, and 2 is curl's WebSocket mode rather than
/// a boolean. Its equivalent is ClientWebSocket, which is a different type and not an option on
/// <see cref="HttpTransfer"/> - kept apart so that the one site which is not HTTP cannot be
/// translated as though it were.
///
/// Three things about it are not what a port would write:
///
/// 1. THE SUBPROTOCOL IS A HEADER IN CURL AND IS NOT ONE HERE. The core appends
///    "Sec-WebSocket-Protocol: np-pushpacket" to its header list, because curl has nowhere else to
///    put it. ClientWebSocket owns that header: it is set through AddSubProtocol and setting it as
///    a raw header is refused. So the ONE header that decides whether the server talks to this
///    client at all is the one that must not be copied across with the others.
///
/// 2. THE TIMEOUT IS DISABLED. Every other transfer in the core sets CURLOPT_TIMEOUT to a real
///    number; this one sets it to 0, which is curl for "no timeout". A push channel is open for
///    the length of a session, so a port that applied its ordinary transfer timeout here would
///    drop the notifications after however many seconds that was and look like a server that went
///    quiet.
///
/// 3. THE CLIENT LIES ABOUT WHAT IT IS. The user agent says WebSocket++/0.8.2 - a library this
///    client does not use - and the OS says Windows/10.0 whatever it is running on. Both are fixed
///    strings to be reproduced rather than derived: they are what the server has been answering,
///    and a port that sent its own honest values would be the first client to do so.
/// </summary>
public static class PushSocket
{
    /// <summary>The path the notification socket is opened on, under the FQDN discovered first.</summary>
    public const string Path = "/np/pushNotification";

    /// <summary>
    /// The subprotocol. Named apart from <see cref="FixedHeaders"/> because it does NOT travel with
    /// them - see the class note.
    /// </summary>
    public const string SubProtocol = "np-pushpacket";

    /// <summary>
    /// The eight headers the core sends besides the authorisation and the subprotocol, in its
    /// order. Fixed strings, including the two that are not true of this client.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> FixedHeaders { get; } =
    [
        ("User-Agent", "WebSocket++/0.8.2"),
        ("X-PSN-APP-TYPE", "REMOTE_PLAY"),
        ("X-PSN-APP-VER", "RemotePlay/1.0"),
        ("X-PSN-KEEP-ALIVE-STATUS-TYPE", "3"),
        ("X-PSN-OS-VER", "Windows/10.0"),
        ("X-PSN-PROTOCOL-VERSION", "2.1"),
        ("X-PSN-RECONNECTION", "false"),
    ];

    /// <summary>
    /// The literal the core passes to CURLOPT_CONNECT_ONLY, as it is written there - the L suffix
    /// included, because what a drift check reads is the source and not the number.
    /// </summary>
    public const string ConnectOnlyLiteral = "2L";

    /// <summary>The socket's URL for a discovered FQDN.</summary>
    public static Uri UrlFor(string fqdn)
    {
        ArgumentNullException.ThrowIfNull(fqdn);
        return new Uri($"wss://{fqdn}{Path}");
    }

    /// <summary>
    /// Applies everything the core sets, to a socket the caller then connects.
    ///
    /// Separate from the connect so that the options are assertable without a server: what a port
    /// gets wrong here is which door each value goes through, and that is decided before any
    /// connection exists.
    /// </summary>
    public static void Configure(ClientWebSocket socket, string authorizationHeader)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(authorizationHeader);

        // Its own door. Adding it to the header list below throws.
        socket.Options.AddSubProtocol(SubProtocol);

        foreach ((string name, string value) in FixedHeaders)
            socket.Options.SetRequestHeader(name, value);

        socket.Options.SetRequestHeader("Authorization", authorizationHeader);

        // CURLOPT_TIMEOUT 0. ClientWebSocket has no whole-transfer timeout of its own, so what
        // this guards against is a caller adding one: the channel is open for the session.
        socket.Options.KeepAliveInterval = KeepAlive;
    }

    /// <summary>
    /// How often to ping while the channel is idle.
    ///
    /// Not the core's - curl's WebSocket mode does not ping, and the server's own
    /// X-PSN-KEEP-ALIVE-STATUS-TYPE is what keeps the session alive. This is the runtime's default
    /// made explicit, so that a future change to it is a decision rather than an upgrade.
    /// </summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The timeout a caller must NOT apply to this socket, stated as a value so a check can hold
    /// it: curl's 0 means no bound at all.
    /// </summary>
    public static readonly TimeSpan NoTimeout = TimeSpan.Zero;
}

/// <summary>
/// PP33: the push socket's setup where the Qt core states it.
/// </summary>
public static class PushSocketSource
{
    /// <summary>Where the socket is opened.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the subprotocol is still sent as a plain header there.</summary>
    public static bool TheSubProtocolIsStillAHeader(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            $"\"Sec-WebSocket-Protocol: {PushSocket.SubProtocol}\"", StringComparison.Ordinal);
    }

    /// <summary>Whether this transfer still disables the timeout every other one sets.</summary>
    public static bool TheTimeoutIsStillDisabled(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("CURLOPT_TIMEOUT, 0L", StringComparison.Ordinal);
    }

    /// <summary>Whether the connect-only mode is still the WebSocket one.</summary>
    public static bool TheModeIsStillTheWebSocketOne(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            $"CURLOPT_CONNECT_ONLY, {PushSocket.ConnectOnlyLiteral}", StringComparison.Ordinal);
    }

    /// <summary>Whether every fixed header is still sent, and still with that value.</summary>
    public static bool TheFixedHeadersAreStillThese(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach ((string name, string value) in PushSocket.FixedHeaders)
        {
            if (!core.Contains($"\"{name}: {value}\"", StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
