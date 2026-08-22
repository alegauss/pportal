using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// The session thread's view of itself while it waits. Every field is set by another thread.
/// </summary>
/// <param name="ShouldStop">Somebody asked the session to stop.</param>
/// <param name="CtrlFailed">The control connection died.</param>
/// <param name="CtrlSessionIdReceived">The control channel handed over a session id.</param>
/// <param name="CtrlLoginPinRequested">The console is asking for a PIN.</param>
/// <param name="LoginPinEntered">The user supplied one.</param>
/// <param name="StreamConnectionSwitchReceived">The console agreed to switch to the stream.</param>
/// <param name="PsnRegistSucceeded">Registration over PSN completed.</param>
public readonly record struct SessionState(
    bool ShouldStop = false,
    bool CtrlFailed = false,
    bool CtrlSessionIdReceived = false,
    bool CtrlLoginPinRequested = false,
    bool LoginPinEntered = false,
    bool StreamConnectionSwitchReceived = false,
    bool PsnRegistSucceeded = false);

/// <summary>Which wait a session thread is in.</summary>
public enum SessionWaitKind
{
    /// <summary>Nothing specific: the thread is only watching for stop and failure.</summary>
    State,

    /// <summary>Waiting for the control channel to produce a session id, or ask for a PIN.</summary>
    CtrlStart,

    /// <summary>Waiting for the user to type the PIN the console asked for.</summary>
    Pin,

    /// <summary>Waiting for the console to agree to the stream switch.</summary>
    StreamConnectionSwitch,

    /// <summary>Waiting for PSN registration to complete.</summary>
    Regist,
}

/// <summary>
/// PP293: the five predicates the session thread waits on, and the two that end all of them.
///
/// A condition variable wait does not say WHY it returned. session.c has five predicates and every
/// one of them is the same shape - should_stop, or ctrl_failed, or the one thing this particular
/// wait is actually for - so a wait that ends has told the caller nothing until it re-reads the
/// state and works out which of the three happened.
///
/// That is the bug this shape invites, and it is why the predicates are worth naming rather than
/// inlining. A caller that treats "the wait returned" as "the thing I waited for happened" proceeds
/// into a login with no PIN, or a stream with no session id, on a session that was asked to stop.
/// The C is careful about it; the carefulness lives at five separate call sites and nowhere in the
/// predicates themselves.
///
/// <see cref="Reason"/> is the part the C does not have: it answers which of the three ended the
/// wait, so a managed caller cannot forget to ask.
/// </summary>
public static class SessionWait
{
    /// <summary>Whether this wait is over.</summary>
    public static bool IsSatisfied(SessionWaitKind kind, SessionState state)
        => state.ShouldStop || state.CtrlFailed || Specific(kind, state);

    /// <summary>
    /// The condition this particular wait exists for, without the two that end every wait.
    /// </summary>
    public static bool Specific(SessionWaitKind kind, SessionState state) => kind switch
    {
        SessionWaitKind.CtrlStart => state.CtrlSessionIdReceived || state.CtrlLoginPinRequested,
        SessionWaitKind.Pin => state.LoginPinEntered,
        SessionWaitKind.StreamConnectionSwitch => state.StreamConnectionSwitchReceived,
        SessionWaitKind.Regist => state.PsnRegistSucceeded,

        // State waits for nothing of its own - it is the two below and only them.
        SessionWaitKind.State => false,
        _ => false,
    };

    /// <summary>
    /// Why the wait ended, in the order the C's call sites check it.
    ///
    /// Stop first, then failure, then the thing waited for. The order is not cosmetic: a session
    /// that was asked to stop AND received its session id must stop, and reading the happy
    /// condition first would carry on into a stream nobody wants.
    /// </summary>
    public static SessionWaitReason Reason(SessionWaitKind kind, SessionState state)
    {
        if (state.ShouldStop)
            return SessionWaitReason.Stopped;

        if (state.CtrlFailed)
            return SessionWaitReason.CtrlFailed;

        return Specific(kind, state) ? SessionWaitReason.Satisfied : SessionWaitReason.StillWaiting;
    }
}

/// <summary>Why a wait ended.</summary>
public enum SessionWaitReason
{
    /// <summary>It has not - the predicate is false and the thread should keep waiting.</summary>
    StillWaiting,

    /// <summary>The session was asked to stop. Checked first, whatever else is also true.</summary>
    Stopped,

    /// <summary>The control connection failed.</summary>
    CtrlFailed,

    /// <summary>The thing this wait was for.</summary>
    Satisfied,
}
