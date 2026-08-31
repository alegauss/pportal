using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP601, under PP27: why no entry point exposes takion's receive, and which door is left open.
///
/// §PP27 says the loop is "bound to sockets and threads a capture has neither of" and stops there,
/// which reads as an absence somebody could fill. It is not: filling it the obvious way is refused
/// by a binding non-goal, and knowing that before starting is the difference between an afternoon
/// and a rejected diff.
///
/// EVERY RECEIVE PATH IS STATIC. takion.c declares takion_handle_packet and its three helpers -
/// message, message_data and message_data_ack - as file-local, and takion.h exports sends, the two
/// AV parses, the congestion format and chiaki_takion_packet_mac. Nothing public takes a datagram
/// and runs what the loop runs. PP531 could time the MAC gate precisely because that one IS
/// exported; the loop around it is not.
///
/// SO THE OBVIOUS FIX IS FORBIDDEN. Removing a `static` is a local patch to the vendored C, and
/// "No local patch to the vendored C" is a non-goal - refused at input like a task line. Its
/// paragraph names the lines it does not reach, and PP593 put both of them there: PP33's deletion
/// and PP30's port. PP27 is not among them, and the rule's own reason is why - a patch leaves every
/// drift check agreeing with a libchiaki nobody runs, which is exactly what a shim reaching a
/// de-statified handler would be.
///
/// THE DOOR THAT IS OPEN is chiaki_takion_connect, whose third parameter is the caller's own
/// socket. Takion does not make its socket; it is handed one. So recorded datagrams can be driven
/// through the real receive loop by giving takion one end of a local pair and writing to the other
/// - no patch, the loop unmodified, and the thread and socket §PP27 says a capture lacks supplied
/// rather than bypassed. What that costs is takion's handshake, which a peer on the other end has
/// to satisfy before the loop reaches a data packet.
/// </summary>
public static class TakionReceiveReach
{
    /// <summary>The C the receive loop lives in.</summary>
    public const string SourceRelativePath = @"lib\src\takion.c";

    /// <summary>Its public header.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\takion.h";

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? LocateSource() => SanitizerSource.LocateRelative(SourceRelativePath);

    /// <summary>takion.h, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>
    /// The receive path, handler by handler, as takion.c declares them.
    ///
    /// Named rather than counted: what a shim would have to reach is this chain, and a plan that
    /// exposed only the first would still not run what the loop runs.
    /// </summary>
    public static IReadOnlyList<string> Handlers { get; } =
    [
        "takion_handle_packet",
        "takion_handle_packet_message",
        "takion_handle_packet_message_data",
        "takion_handle_packet_message_data_ack",
    ];

    /// <summary>The export that hands takion a socket, which is the way in that needs no patch.</summary>
    public const string TheOpenDoor = "chiaki_takion_connect";

    /// <summary>Whether every handler in the chain is still file-local.</summary>
    public static IReadOnlyList<string> HandlersThatAreNotStatic(string takionSource)
    {
        ArgumentNullException.ThrowIfNull(takionSource);

        return
        [
            .. Handlers.Where(handler =>
                !takionSource.Contains(
                    "static void " + handler + "(", StringComparison.Ordinal))
        ];
    }

    /// <summary>
    /// Whether the public header still exposes no way to hand takion a received datagram.
    ///
    /// Asked as the absence of the handlers rather than as a list of what IS exported: the header
    /// gains sends and parses as the port needs them, and a rule keyed on its whole contents would
    /// fail on work that has nothing to do with this.
    /// </summary>
    public static bool TheHeaderExposesNoReceive(string takionHeader)
    {
        ArgumentNullException.ThrowIfNull(takionHeader);

        return !Handlers.Any(handler => takionHeader.Contains(handler, StringComparison.Ordinal));
    }

    /// <summary>Whether the door is still a door: connect still takes the caller's socket.</summary>
    public static bool ConnectStillTakesTheCallersSocket(string takionHeader)
    {
        ArgumentNullException.ThrowIfNull(takionHeader);

        int at = takionHeader.IndexOf(TheOpenDoor + "(", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = takionHeader.IndexOf(';', at);
        if (end < 0)
            return false;

        return takionHeader[at..end].Contains("chiaki_socket_t *", StringComparison.Ordinal);
    }
}
