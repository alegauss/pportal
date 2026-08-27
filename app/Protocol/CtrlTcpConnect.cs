using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>How far the control channel's TCP connect got, and why it stopped.</summary>
public enum CtrlSocketOutcome
{
    /// <summary>The socket is connected and stored on the ctrl.</summary>
    Connected,

    /// <summary>The sockaddr copy could not be allocated.</summary>
    NoMemory,

    /// <summary>The resolved address is neither INET nor INET6, so no port could be stamped.</summary>
    UnsupportedFamily,

    /// <summary>socket() failed.</summary>
    SocketCreationFailed,

    /// <summary>The socket could not be put into non-blocking mode.</summary>
    NonBlockingFailed,

    /// <summary>The stop pipe fired during the connect. Not a failure.</summary>
    Cancelled,

    /// <summary>The console refused the connection.</summary>
    ConnectionRefused,

    /// <summary>The connect failed some other way, the timeout included.</summary>
    ConnectFailed,
}

/// <summary>One attempt, as the state it leaves behind.</summary>
/// <param name="Outcome">How far it got.</param>
/// <param name="Reports">
/// The quit reason THIS function records, or null where it leaves that to its caller.
/// </param>
/// <param name="ClosesTheSocket">Whether the socket it opened is closed before returning.</param>
public readonly record struct CtrlSocketAttempt(
    CtrlSocketOutcome Outcome, ChiakiQuitReason? Reports, bool ClosesTheSocket);

/// <summary>
/// PP415, under PP294: the socket under the ctrl request.
///
/// PP356 ported <c>ctrl_connect</c> - the request and the crypt counters it spends before anything
/// goes out. This is <c>ctrl_connect_tcp</c> underneath it: one socket, one connect, and a cleanup
/// ladder with five ways to leave.
///
/// THE PORT IS STAMPED, NOT RESOLVED. The addrinfo the session selected is copied and then 9295 is
/// written over whatever port it carried, into <c>sin_port</c> or <c>sin6_port</c> by family. An
/// address that is neither is refused AFTER the copy rather than before it, which is why the
/// unsupported-family path has something to free.
///
/// THE NOTIFY MUTEX IS DROPPED AROUND THE CONNECT. It is unlocked before a connect that waits up to
/// <see cref="ConnectTimeoutMs"/> and retaken after. A port that held it across the wait would make
/// that timeout the minimum time a stop takes to be noticed - five seconds of an unresponsive
/// cancel, on the one operation most likely to need one.
///
/// CANCELLED IS NOT A FAILURE, and PP349 established the same shape one function along. The stop
/// pipe firing closes the socket and records nothing. It also tells <c>should_stop</c> apart from
/// the notify pipe firing WITHOUT it, and logs the second as an error - because it should not
/// happen, and a port that folded the two together would lose the only sign that it did.
///
/// THREE OF FIVE FAILURES REPORT, AND THAT IS CORRECT. The socket, non-blocking and connect paths
/// record a specific reason; the allocation and bad-family paths return and let
/// <c>ctrl_thread_func</c> answer with CTRL_CONNECT_FAILED. PP348's guard is what makes it work:
/// a specific reason already recorded is not overwritten by the generic one.
///
/// WHICH IS WHAT MADE THE MEMORY PATH WRONG. Falling through to CONNECT_FAILED told a user whose
/// machine was out of memory that the network had failed. PP345 added
/// <see cref="ChiakiQuitReason.CtrlMemory"/> one function over for exactly this, and its string was
/// already in session.c - so PP415 records it here and the guard drops the generic one as before.
/// The bad-family path is left alone: an address the client cannot use IS a connect failure.
/// </summary>
public static class CtrlTcpConnect
{
    /// <summary>
    /// SESSION_CTRL_PORT, stamped over the resolved address's own port.
    ///
    /// <see cref="CtrlConnect.CtrlPort"/> rather than a second 9295: the two would be the same number
    /// written twice, and PP356 already holds it against the same macro.
    /// </summary>
    public const int CtrlPort = CtrlConnect.CtrlPort;

    /// <summary>How long the connect waits, through the stop pipe, before giving up.</summary>
    public const int ConnectTimeoutMs = 5000;

    /// <summary>
    /// Whether the notify mutex is held while the connect waits. It is not, deliberately.
    /// </summary>
    public const bool HoldsTheNotifyMutexWhileConnecting = false;

    /// <summary>The two families a port can be stamped into, and nothing else.</summary>
    public static bool CanStampAPortInto(AddressFamily family)
        => family is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;

    /// <summary>
    /// What one attempt leaves behind.
    /// </summary>
    /// <param name="outcome">How far it got.</param>
    public static CtrlSocketAttempt Attempt(CtrlSocketOutcome outcome) => outcome switch
    {
        CtrlSocketOutcome.Connected =>
            new CtrlSocketAttempt(outcome, null, ClosesTheSocket: false),

        // PP415: this one used to report nothing and be answered with CONNECT_FAILED.
        CtrlSocketOutcome.NoMemory =>
            new CtrlSocketAttempt(outcome, ChiakiQuitReason.CtrlMemory, ClosesTheSocket: false),

        // No socket was opened yet, and nothing specific is recorded - the caller answers.
        CtrlSocketOutcome.UnsupportedFamily =>
            new CtrlSocketAttempt(outcome, null, ClosesTheSocket: false),

        CtrlSocketOutcome.SocketCreationFailed =>
            new CtrlSocketAttempt(outcome, ChiakiQuitReason.CtrlUnknown, ClosesTheSocket: false),

        CtrlSocketOutcome.NonBlockingFailed =>
            new CtrlSocketAttempt(outcome, ChiakiQuitReason.CtrlUnknown, ClosesTheSocket: true),

        // A stop is what was asked for. Closed, and nothing recorded.
        CtrlSocketOutcome.Cancelled =>
            new CtrlSocketAttempt(outcome, null, ClosesTheSocket: true),

        CtrlSocketOutcome.ConnectionRefused =>
            new CtrlSocketAttempt(
                outcome, ChiakiQuitReason.CtrlConnectionRefused, ClosesTheSocket: true),

        CtrlSocketOutcome.ConnectFailed =>
            new CtrlSocketAttempt(outcome, ChiakiQuitReason.CtrlUnknown, ClosesTheSocket: true),

        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    /// <summary>
    /// The reason a user ends up seeing, once ctrl_thread_func has answered too.
    ///
    /// This is the whole point of the fix, and it needs both halves to state: the thread answers ANY
    /// error from ctrl_connect with CTRL_CONNECT_FAILED, and PP348's guard means that lands only
    /// where nothing more specific was recorded first.
    /// </summary>
    /// <param name="outcome">How far the connect got.</param>
    public static ChiakiQuitReason ReasonTheUserSees(CtrlSocketOutcome outcome)
    {
        CtrlSocketAttempt attempt = Attempt(outcome);

        if (outcome == CtrlSocketOutcome.Connected)
            return ChiakiQuitReason.None;

        // A cancel is a stop, and the session records STOPPED on its own path rather than here.
        if (outcome == CtrlSocketOutcome.Cancelled)
            return ChiakiQuitReason.Stopped;

        return attempt.Reports ?? ChiakiQuitReason.CtrlConnectFailed;
    }
}

/// <summary>PP415: the connect's rules, still stated the same way in the core.</summary>
public static class CtrlTcpConnectSource
{
    /// <summary>Where it lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The function's body, or null where the signature has moved.</summary>
    public static string? Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return CFunction.Body(core, "static ChiakiErrorCode ctrl_connect_tcp(ChiakiCtrl *ctrl)");
    }

    /// <summary>
    /// Whether the port is still stamped into both families.
    ///
    /// Against the MACRO, because that is what the C writes - the literal is asserted separately by
    /// <see cref="ThePortMacroIsStillThis"/>, so the two claims stay two.
    /// </summary>
    public static bool ThePortIsStillStampedPerFamily(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Compact(CCall.Code(body));

        return code.Contains("sin_port=htons(SESSION_CTRL_PORT)", StringComparison.Ordinal)
            && code.Contains("sin6_port=htons(SESSION_CTRL_PORT)", StringComparison.Ordinal);
    }

    /// <summary>And whether that macro is still the number this port stamps.</summary>
    public static bool ThePortMacroIsStillThis(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return CCall.Compact(CCall.Code(core)).Contains(
            $"#define SESSION_CTRL_PORT {CtrlTcpConnect.CtrlPort}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the notify mutex is still dropped around the connect, and retaken after.
    ///
    /// The unlock, the connect, then the lock - in that order. A port holding it would make the
    /// timeout the floor on stop latency, and the ordering is the only thing that says so.
    /// </summary>
    public static bool TheNotifyMutexIsStillDroppedAroundTheConnect(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int unlock = CCall.Mark(code, "chiaki_mutex_unlock(&ctrl->notif_mutex)");
        int connect = CCall.Mark(code, "chiaki_stop_pipe_connect(&ctrl->stop_pipe");
        int relock = CCall.Mark(code, "chiaki_mutex_lock(&ctrl->notif_mutex)");

        return unlock >= 0 && connect > unlock && relock > connect;
    }

    /// <summary>Whether the connect still waits the timeout this port names.</summary>
    public static bool TheTimeoutIsStillThis(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        return CCall.Compact(CCall.Code(body)).Contains(
            $",{CtrlTcpConnect.ConnectTimeoutMs})", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP415: whether the allocation failure now records the memory reason itself.
    ///
    /// The reason, then the return, inside the guard - so a path that recorded it after returning
    /// would not satisfy this.
    /// </summary>
    public static bool TheAllocationFailureStillReportsMemory(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int guard = CCall.Mark(code, "if(!sa)");
        int reports = CCall.Mark(
            code, "ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_MEMORY)", Math.Max(guard, 0));
        int returns = CCall.Mark(code, "return CHIAKI_ERR_MEMORY;", Math.Max(guard, 0));

        return guard >= 0 && reports > guard && returns > reports;
    }

    /// <summary>
    /// And whether a cancel still records nothing while closing the socket.
    ///
    /// The property that keeps a stop from being reported as a fault. Read as the cancel branch
    /// containing a close and no ctrl_failed at all.
    /// </summary>
    public static bool ACancelStillReportsNothing(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int cancel = CCall.Mark(code, "if(err == CHIAKI_ERR_CANCELED)");
        if (cancel < 0)
            return false;

        // NOT the next `else`: the first one after the cancel test is the INNER one, telling
        // should_stop from the notify pipe firing without it, and a branch cut there stops before
        // the close it is meant to contain. The other arm's own log is the unambiguous end.
        int otherwise = CCall.Mark(code, "CHIAKI_LOGE(session->log, \"Ctrl connect failed", cancel);
        if (otherwise < 0)
            return false;

        string compact = CCall.Compact(code);
        string branch = compact[cancel..otherwise];

        return branch.Contains("CHIAKI_SOCKET_CLOSE(sock)", StringComparison.Ordinal)
            && !branch.Contains("ctrl_failed(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the connect failure still tells a refusal apart from everything else.
    ///
    /// Two reasons off one comparison, which is the only place in this function where the error code
    /// chooses what the user is told.
    /// </summary>
    public static bool ARefusalIsStillToldApart(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string compact = CCall.Compact(CCall.Code(body));

        return compact.Contains(
                "err==CHIAKI_ERR_CONNECTION_REFUSED?CHIAKI_QUIT_REASON_CTRL_CONNECTION_REFUSED"
                    + ":CHIAKI_QUIT_REASON_CTRL_UNKNOWN",
                StringComparison.Ordinal);
    }
}
