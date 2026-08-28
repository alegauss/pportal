using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Who closes one of the two sockets the punch hands out.</summary>
public enum SocketCloser
{
    /// <summary>chiaki_rudp_fini, on the copy it took of the ctrl socket.</summary>
    Rudp,

    /// <summary>takion, because streamconnection sets close_socket.</summary>
    Takion,
}

/// <summary>One socket, who made it and who ends it.</summary>
/// <param name="Port">Which of the two.</param>
/// <param name="Closer">What closes it.</param>
/// <param name="HandedOverByValue">
/// Whether the receiver took a copy of the handle rather than a reference to the field.
/// </param>
public readonly record struct HolepunchSocketOwner(
    HolepunchPortType Port, SocketCloser Closer, bool HandedOverByValue);

/// <summary>
/// PP502, under PP340: who closes the two sockets the holepunch session hands out.
///
/// PP479 drove the connect sequence and PP480 joined the interface to session.c's call sites.
/// Neither asked this, and it is the question a managed owner of the flow has to answer before it
/// is written rather than after.
///
/// THE CREATOR CLOSES NEITHER. chiaki_holepunch_session_fini deletes the PSN session, joins the
/// websocket and UPnP threads, removes the port mappings, and frees the curl share, the STUN lists,
/// two stop pipes, two mutexes and two conds. It closes no socket at all. Every CHIAKI_SOCKET_CLOSE
/// in holepunch.c is a STUN probe's or a candidate race's - never ctrl_sock, never data_sock.
///
/// EACH IS CLOSED BY WHAT IT WAS HANDED TO. chiaki_rudp_init does `rudp-&gt;sock = *sock` - a COPY of
/// the handle - and chiaki_rudp_fini closes that copy. The data socket goes to takion, and
/// streamconnection sets close_socket, so takion closes it. Two sockets, two closers, and the
/// thing that created them is neither.
///
/// THE COPY IS WHAT MAKES IT SAFE, AND IT IS WHAT A MANAGED PORT CANNOT REPRODUCE BY ACCIDENT. Two
/// managed handles over one OS handle is a double close by construction, whatever the intent. So
/// the type that owns this flow must NOT be IDisposable over these two - which is the opposite of
/// the reflex for a class whose job is creating sockets.
///
/// AND THE ORDER IN chiaki_session_fini FOLLOWS: rudp released before the holepunch session. Not
/// because reversing it would touch freed memory - the handle is a copy, so it would not - but
/// because that is the order the two closers run in, and a managed teardown that reverses it is
/// leaning on the same accident rather than on the rule.
/// </summary>
public static class HolepunchSocketOwnership
{
    /// <summary>Both sockets, with who ends each.</summary>
    public static IReadOnlyList<HolepunchSocketOwner> Owners { get; } =
    [
        new(HolepunchPortType.Ctrl, SocketCloser.Rudp, HandedOverByValue: true),
        new(HolepunchPortType.Data, SocketCloser.Takion, HandedOverByValue: true),
    ];

    /// <summary>What closes a socket, by the channel it belongs to.</summary>
    public static SocketCloser CloserFor(HolepunchPortType port)
        => Owners.First(o => o.Port == port).Closer;

    /// <summary>
    /// Whether an owner of the PSN flow may dispose the sockets it obtained. It may not.
    ///
    /// A method rather than a comment, so the answer has a call site and a test. Anything that
    /// closes one of these has closed a handle another object still holds a copy of and will close
    /// again.
    /// </summary>
    public static bool TheFlowOwnerMayDispose(HolepunchPortType port) => false;

    /// <summary>
    /// The order chiaki_session_fini releases the two in: what holds a copy goes first.
    ///
    /// The rudp closes the ctrl socket; the holepunch session is released after. Reversing it is
    /// safe only by the accident that the handle was copied, which is not a reason.
    /// </summary>
    public static IReadOnlyList<string> TeardownOrder { get; } =
        ["chiaki_rudp_fini", "chiaki_holepunch_session_fini"];
}

/// <summary>
/// PP502: the C's own spelling, because every claim here is about something that is NOT written.
/// </summary>
public static class HolepunchSocketOwnershipSource
{
    /// <summary>holepunch.c, which creates both sockets.</summary>
    public const string HolepunchRelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>rudp.c, which closes one of them.</summary>
    public const string RudpRelativePath = @"lib\src\remote\rudp.c";

    /// <summary>session.c, which orders the two releases.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>streamconnection.c, which is where the data socket's closer is chosen.</summary>
    public const string StreamRelativePath = @"lib\src\streamconnection.c";

    /// <summary>holepunch.c, or null outside a checkout.</summary>
    public static string? LocateHolepunch() => SanitizerSource.LocateRelative(HolepunchRelativePath);

    /// <summary>rudp.c, or null outside a checkout.</summary>
    public static string? LocateRudp() => SanitizerSource.LocateRelative(RudpRelativePath);

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? LocateSession() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? LocateStream() => SanitizerSource.LocateRelative(StreamRelativePath);

    /// <summary>The holepunch session's teardown.</summary>
    public static string? FiniBody(string holepunchSource)
        => CFunction.Body(holepunchSource, "CHIAKI_EXPORT void chiaki_holepunch_session_fini");

    /// <summary>
    /// Whether the holepunch teardown still closes neither of the two sockets it created.
    ///
    /// Read as absence, and narrowly: the body may close nothing named ctrl_sock or data_sock. It
    /// closes plenty of OTHER sockets elsewhere in the file, so a check for "no close at all" would
    /// be about the wrong function.
    ///
    /// AND IT CARRIES ITS OWN FIXTURE, because a check made only of absences says yes about an
    /// empty file - which is the one thing every drift check in this tree is held to answering no
    /// to. The two releases named first are the last statements of that function, so a body that
    /// is not this one fails here rather than passing by having nothing in it.
    /// </summary>
    public static bool TheFiniClosesNeitherSocket(string finiBody)
    {
        ArgumentNullException.ThrowIfNull(finiBody);

        if (!finiBody.Contains("chiaki_stop_pipe_fini(&session->select_pipe);", StringComparison.Ordinal)
            || !finiBody.Contains("chiaki_cond_fini(&session->state_cond);", StringComparison.Ordinal))
        {
            return false;
        }

        return !finiBody.Contains("CHIAKI_SOCKET_CLOSE(session->ctrl_sock)", StringComparison.Ordinal)
            && !finiBody.Contains("CHIAKI_SOCKET_CLOSE(session->data_sock)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the rudp still takes a COPY of the handle and closes that copy.
    ///
    /// Both halves. The copy is why one close is correct; the close is why the holepunch session
    /// does not need one. If the init ever stored the pointer instead, the release order in
    /// session.c would stop being a convention and start being a lifetime.
    /// </summary>
    public static bool TheRudpCopiesTheHandleAndClosesIt(string rudpSource)
    {
        ArgumentNullException.ThrowIfNull(rudpSource);

        if (CFunction.Body(rudpSource, "CHIAKI_EXPORT RudpInstance *chiaki_rudp_init") is not { } init
            || CFunction.Body(rudpSource, "CHIAKI_EXPORT ChiakiErrorCode chiaki_rudp_fini") is not { } fini)
        {
            return false;
        }

        return init.Contains("rudp->sock = *sock;", StringComparison.Ordinal)
            && fini.Contains("CHIAKI_SOCKET_CLOSE(rudp->sock);", StringComparison.Ordinal);
    }

    /// <summary>Whether takion is still told to close the data socket it is handed.</summary>
    public static bool TakionStillClosesTheDataSocket(string streamSource)
    {
        ArgumentNullException.ThrowIfNull(streamSource);
        return streamSource.Contains("takion_info.close_socket = true;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether chiaki_session_fini still releases the rudp before the holepunch session.
    ///
    /// The order this port copies. Asserted rather than assumed, because the two lines sit four
    /// apart and either could move without anything failing to build.
    /// </summary>
    public static bool TheRudpIsReleasedFirst(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        if (CFunction.Body(sessionSource, "CHIAKI_EXPORT void chiaki_session_fini") is not { } body)
            return false;

        int rudp = body.IndexOf("chiaki_rudp_fini(session->rudp);", StringComparison.Ordinal);
        int punch = body.IndexOf(
            "chiaki_holepunch_session_fini(session->holepunch_session);", StringComparison.Ordinal);

        return rudp >= 0 && punch > rudp;
    }
}
