using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which of the session thread's two exits a failure takes.</summary>
public enum SessionExit
{
    /// <summary>
    /// Stop ctrl, join it, then send the quit event. Taken once ctrl has been started.
    /// </summary>
    ViaCtrl,

    /// <summary>Send the quit event and nothing else. Taken before ctrl exists.</summary>
    Direct,
}

/// <summary>
/// PP336, continuing PP293: the two exits the session thread has, and which reason survives.
///
/// session_thread_func ends through one of two labels. `quit_ctrl` stops ctrl, joins it, and then
/// FALLS THROUGH into `quit`, which sends the quit event; jumping straight to `quit` skips the
/// ctrl teardown, which is right only before ctrl was ever started. Both paths send exactly one
/// quit event, and that event is the only thing a client ever learns about why the session ended.
///
/// THE REASON IS NOT OVERWRITTEN ONCE SET. The ctrl_failed label assigns CTRL_UNKNOWN only where
/// the reason is still NONE, so a specific reason recorded earlier - a version mismatch, a console
/// already in use - survives a later generic failure. A port that assigned unconditionally would
/// turn every diagnosable ending into "ctrl failed", which is the ending a user can do least with.
///
/// THE LOCK IS RELEASED BEFORE THE JUMP AND RETAKEN TO READ. Every QUIT unlocks the state mutex
/// and then goes; `quit` locks again to copy the reason out, and unlocks BEFORE sending the event.
/// The event goes out with no lock held, which is what lets a handler call back into the session
/// without deadlocking - and is why the reason is copied rather than read through a pointer.
///
/// CANCELED IS SUCCESS. The stream connection returning CHIAKI_ERR_CANCELED lands in the same
/// branch as success and reports STOPPED, because cancelling is what stopping looks like from
/// inside the run. Only an error that is neither becomes STREAM_CONNECTION_UNKNOWN.
/// </summary>
public static class SessionTeardown
{
    /// <summary>
    /// The reason a stop request records. It is assigned at the check, not at the exit, which is
    /// why a stop beats whatever the thread was about to conclude.
    /// </summary>
    public const ChiakiQuitReason WhenStopped = ChiakiQuitReason.Stopped;

    /// <summary>
    /// The exact string that separates a console shutting down from any other remote disconnect.
    /// Compared whole - libchiaki uses strcmp, so a reason that merely contains it is the other one.
    /// </summary>
    public const string ShutdownReason = "Server shutting down";

    /// <summary>
    /// What a reason assignment resolves to, given what is already recorded.
    /// </summary>
    /// <param name="current">What the session has recorded so far.</param>
    /// <param name="proposed">What this failure would record.</param>
    public static ChiakiQuitReason Record(ChiakiQuitReason current, ChiakiQuitReason proposed)
        => current == ChiakiQuitReason.None ? proposed : current;

    /// <summary>
    /// How the stream connection's return value ends the session.
    /// </summary>
    /// <param name="error">What chiaki_stream_connection_run returned.</param>
    /// <param name="remoteDisconnectReason">
    /// The console's own words, read only where the error is Disconnected.
    /// </param>
    public static ChiakiQuitReason FromStreamConnection(
        ChiakiError error, string? remoteDisconnectReason) => error switch
        {
            ChiakiError.Disconnected =>
                string.Equals(remoteDisconnectReason, ShutdownReason, StringComparison.Ordinal)
                    ? ChiakiQuitReason.StreamConnectionRemoteShutdown
                    : ChiakiQuitReason.StreamConnectionRemoteDisconnected,

            // Canceled is what stopping looks like from inside the run, so it is not a failure.
            ChiakiError.Success or ChiakiError.Canceled => ChiakiQuitReason.Stopped,

            _ => ChiakiQuitReason.StreamConnectionUnknown,
        };

    /// <summary>
    /// Whether the exit stops and joins ctrl on the way out.
    ///
    /// Only one thing decides it: whether ctrl was ever started. Everything before that goes
    /// straight to the quit event, and everything after it goes through the ctrl teardown - which
    /// falls through to the same event, so there is no path that ends without one.
    /// </summary>
    public static SessionExit ExitFor(bool ctrlStarted)
        => ctrlStarted ? SessionExit.ViaCtrl : SessionExit.Direct;

    /// <summary>Whether this exit stops and joins ctrl before the event.</summary>
    public static bool StopsCtrl(SessionExit exit) => exit == SessionExit.ViaCtrl;

    /// <summary>Both exits send exactly one quit event, which is the whole of what a client sees.</summary>
    public static bool SendsQuitEvent(SessionExit exit) => exit is SessionExit.ViaCtrl or SessionExit.Direct;
}

/// <summary>
/// PP336: the teardown held against session_thread_func. None of it is in PP297's capture, which is
/// of a session that ran and was stopped cleanly.
/// </summary>
public static class SessionTeardownSource
{
    /// <summary>Where the thread lives.</summary>
    public const string RelativePath = @"lib\src\session.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether the ctrl exit still falls THROUGH into the quit label rather than returning.
    ///
    /// The fall-through is what makes "one quit event on every path" true. A return added between
    /// them would end ctrl-side failures silently, with the client waiting on an event that never
    /// arrives - which looks like a hang and not like a failure.
    /// </summary>
    public static bool TheCtrlExitStillFallsThroughToQuit(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int viaCtrl = core.IndexOf("\nquit_ctrl:", StringComparison.Ordinal);
        int quit = core.IndexOf("\nquit:", StringComparison.Ordinal);
        if (viaCtrl < 0 || quit < 0 || quit < viaCtrl)
            return false;

        // Nothing between them may return, and the join must be there.
        string between = core[viaCtrl..quit];
        return between.Contains("chiaki_ctrl_stop(&session->ctrl);", StringComparison.Ordinal)
            && between.Contains("chiaki_ctrl_join(&session->ctrl);", StringComparison.Ordinal)
            && !between.Contains("return", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a recorded reason is still left alone by the ctrl-failed label.
    ///
    /// Matched as two facts in order rather than as one literal spanning a line break: the guard
    /// exists, and the assignment it guards comes after it. A check on the exact text between them
    /// would go red on a reindentation and say the guard had gone, which is the wrong sentence to
    /// put in front of somebody reading a failing build.
    /// </summary>
    public static bool AReasonAlreadySetIsStillKept(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int label = core.IndexOf("\nctrl_failed:", StringComparison.Ordinal);
        if (label < 0)
            return false;

        int guard = core.IndexOf(
            "if(session->quit_reason == CHIAKI_QUIT_REASON_NONE)", label, StringComparison.Ordinal);
        int assign = core.IndexOf(
            "session->quit_reason = CHIAKI_QUIT_REASON_CTRL_UNKNOWN;", label, StringComparison.Ordinal);

        return guard > label && assign > guard;
    }

    /// <summary>Whether the quit event still goes out with the state mutex released.</summary>
    public static bool TheEventIsStillSentUnlocked(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int quit = core.IndexOf("\nquit:", StringComparison.Ordinal);
        if (quit < 0)
            return false;

        int unlock = core.IndexOf("chiaki_mutex_unlock(&session->state_mutex);", quit, StringComparison.Ordinal);
        int send = core.IndexOf("chiaki_session_send_event(session, &quit_event);", quit, StringComparison.Ordinal);

        return unlock > 0 && send > unlock;
    }

    /// <summary>Whether cancelling the stream connection is still not a failure.</summary>
    public static bool CancelledIsStillNotAFailure(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
            "else if(err != CHIAKI_ERR_SUCCESS && err != CHIAKI_ERR_CANCELED)", StringComparison.Ordinal);
    }
}
