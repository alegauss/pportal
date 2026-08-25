using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Where a session is in its life.</summary>
public enum SessionPhase
{
    /// <summary>Built, with no thread. chiaki_session_init has run.</summary>
    Built,

    /// <summary>The session thread is running.</summary>
    Running,

    /// <summary>A stop has been asked for; the thread has not been waited on yet.</summary>
    Stopping,

    /// <summary>The thread has ended and been joined.</summary>
    Joined,

    /// <summary>Everything is freed. Nothing may be called.</summary>
    Finished,
}

/// <summary>What calling something in a given phase does.</summary>
public enum LifecycleVerdict
{
    /// <summary>It is what the phase expects.</summary>
    Allowed,

    /// <summary>It does nothing, and does no harm.</summary>
    NoOp,

    /// <summary>It blocks forever, because nothing will end the thread.</summary>
    Hangs,

    /// <summary>It frees what a running thread is standing on.</summary>
    UseAfterFree,
}

/// <summary>
/// PP338, continuing PP293: the order a session must be taken down in, which nothing states.
///
/// chiaki_session_stop, _join and _fini are three exported functions with no relationship written
/// down anywhere, and getting the order wrong fails in two different ways that both look like
/// something else.
///
/// FINI DOES NOT STOP AND DOES NOT JOIN. It frees the login pin and the quit reason, finalises the
/// stream connection and ctrl, and then destroys the stop pipe, the condition variable and the
/// state mutex - every primitive the session thread is standing on. Called on a running session it
/// is a use-after-free whose stack is entirely inside libchiaki, with no managed frame to name.
///
/// JOIN DOES NOT STOP EITHER. It is chiaki_thread_join and nothing else, so joining a session
/// nobody asked to stop waits for a thread that is waiting for a console. That is a hang, and it
/// is the one this ordering exists to prevent: stop, then join, then fini.
///
/// STOPPING IS FOUR POKES AND NOT A FLAG. The thread can be blocked in a condition wait, inside a
/// socket select, or down in the stream connection, and each needs its own wake-up: should_stop is
/// set, the stop pipe is stopped, the condition is signalled, and the stream connection is stopped
/// - all under the state mutex. A port that set the flag and waited would hang against every one of
/// the other three.
/// </summary>
public static class SessionLifecycle
{
    /// <summary>
    /// Everything <c>chiaki_session_stop</c> does, in the order it does it.
    ///
    /// Named rather than counted, because the failure of missing one is a hang against whichever
    /// blocker was left unpoked - and which one that is depends on where the console stopped
    /// answering, so it reproduces differently every time.
    /// </summary>
    public static IReadOnlyList<string> StopWakesUp { get; } =
    [
        "session->should_stop = true;",
        "chiaki_stop_pipe_stop(&session->stop_pipe);",
        "chiaki_cond_signal(&session->state_cond);",
        "chiaki_stream_connection_stop(&session->stream_connection);",
    ];

    /// <summary>What stopping a session in this phase does.</summary>
    public static LifecycleVerdict Stopping(SessionPhase phase) => phase switch
    {
        // Legal and useful only while something is running. Before the thread exists the flag is
        // set on a session that will read it at its first check, which is harmless.
        SessionPhase.Built or SessionPhase.Running => LifecycleVerdict.Allowed,
        SessionPhase.Stopping => LifecycleVerdict.NoOp,
        SessionPhase.Joined => LifecycleVerdict.NoOp,
        _ => LifecycleVerdict.UseAfterFree,
    };

    /// <summary>
    /// What joining does. The interesting answer is the one for a running session nobody stopped.
    /// </summary>
    public static LifecycleVerdict Joining(SessionPhase phase) => phase switch
    {
        // No thread was ever created, so there is nothing to wait for.
        SessionPhase.Built => LifecycleVerdict.NoOp,

        // The thread is waiting for a console that may never answer. Nothing here ends it.
        SessionPhase.Running => LifecycleVerdict.Hangs,

        SessionPhase.Stopping => LifecycleVerdict.Allowed,
        SessionPhase.Joined => LifecycleVerdict.NoOp,
        _ => LifecycleVerdict.UseAfterFree,
    };

    /// <summary>
    /// What finishing does, which is the one that corrupts rather than blocks.
    /// </summary>
    public static LifecycleVerdict Finishing(SessionPhase phase) => phase switch
    {
        // No thread: fini is exactly what a built-and-abandoned session needs.
        SessionPhase.Built or SessionPhase.Joined => LifecycleVerdict.Allowed,

        // The thread is alive and holds the mutex, the cond and the stop pipe this destroys.
        SessionPhase.Running or SessionPhase.Stopping => LifecycleVerdict.UseAfterFree,

        _ => LifecycleVerdict.UseAfterFree,
    };

    /// <summary>The only order that is safe from a running session, which is what a caller needs.</summary>
    public static IReadOnlyList<string> TeardownOrder { get; } = ["stop", "join", "fini"];
}

/// <summary>
/// PP338: the three functions held against session.c, because the contract between them is not
/// written anywhere else - not in the header, not in a comment, and not in PP297's capture.
/// </summary>
public static class SessionLifecycleSource
{
    /// <summary>Where the three live.</summary>
    public const string RelativePath = @"lib\src\session.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether stop still performs every wake-up, in order.
    ///
    /// A wake-up removed upstream would leave the port stopping a thread that stays blocked on
    /// whichever one went - and since the four cover different blockers, which sessions hang would
    /// depend on where the console stopped answering.
    /// </summary>
    public static bool StopStillWakesEverything(string core, IReadOnlyList<string> wakeUps)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(wakeUps);

        int stop = core.IndexOf("chiaki_session_stop(ChiakiSession *session)", StringComparison.Ordinal);
        if (stop < 0)
            return false;

        int at = stop;
        foreach (string wake in wakeUps)
        {
            int found = core.IndexOf(wake, at, StringComparison.Ordinal);
            if (found < 0)
                return false;

            at = found;
        }

        return true;
    }

    /// <summary>Whether join is still nothing but a thread join.</summary>
    public static bool JoinStillOnlyJoins(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int join = core.IndexOf("chiaki_session_join(ChiakiSession *session)", StringComparison.Ordinal);
        if (join < 0)
            return false;

        int end = core.IndexOf("\n}", join, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string body = core[join..end];
        return body.Contains("chiaki_thread_join(&session->session_thread, NULL)", StringComparison.Ordinal)
            && !body.Contains("should_stop", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether fini still destroys the primitives a running thread stands on, and still neither
    /// stops nor joins.
    /// </summary>
    public static bool FiniStillFreesWhatTheThreadUses(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int fini = core.IndexOf("chiaki_session_fini(ChiakiSession *session)", StringComparison.Ordinal);
        if (fini < 0)
            return false;

        int end = core.IndexOf("\n}", fini, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string body = core[fini..end];

        return body.Contains("chiaki_stop_pipe_fini(&session->stop_pipe);", StringComparison.Ordinal)
            && body.Contains("chiaki_cond_fini(&session->state_cond);", StringComparison.Ordinal)
            && body.Contains("chiaki_mutex_fini(&session->state_mutex);", StringComparison.Ordinal)
            && !body.Contains("chiaki_session_stop", StringComparison.Ordinal)
            && !body.Contains("chiaki_session_join", StringComparison.Ordinal);
    }
}
