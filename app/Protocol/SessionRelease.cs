using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One step of chiaki_session_fini, in the order it runs.</summary>
public enum SessionReleaseStep
{
    /// <summary>The two strings the ctrl thread can read, freed under the state mutex.</summary>
    FreeStringsUnderLock,

    /// <summary>chiaki_stream_connection_fini.</summary>
    StreamConnection,

    /// <summary>chiaki_ctrl_fini.</summary>
    Ctrl,

    /// <summary>chiaki_rudp_fini - which closes the ctrl socket. PP502.</summary>
    Rudp,

    /// <summary>chiaki_holepunch_session_fini - which closes nothing. PP502.</summary>
    Holepunch,

    /// <summary>chiaki_stop_pipe_fini.</summary>
    StopPipe,

    /// <summary>chiaki_cond_fini.</summary>
    StateCond,

    /// <summary>chiaki_mutex_fini - after everything that could take it.</summary>
    StateMutex,

    /// <summary>freeaddrinfo, handed NULL on every PSN session.</summary>
    FreeAddrInfo,
}

/// <summary>
/// PP507, under PP340: the order chiaki_session_fini releases a session in, and the two things it
/// relies on that nothing states.
///
/// Distinct from <see cref="SessionTeardown"/>, which is PP336's subject: that is how the session
/// THREAD exits and which quit reason survives. This is what chiaki_session_fini does afterwards,
/// and PP502 read two of its steps - the rudp before the holepunch - for the socket rule.
///
/// THE ORDER IS THE WHOLE CONTENT. Every step is a release, so nothing here can fail and a wrong
/// order is a use-after-free rather than an error.
///
/// THE TWO FREES AT THE TOP ARE THE ONLY THING UNDER THE STATE MUTEX. login_pin and quit_reason_str
/// are freed with it held and it is released immediately after, because the ctrl thread reads those
/// two and nothing else here is shared. A managed teardown that took a lock around the whole method
/// - which is what a `lock` block looks like - would hold it across two thread joins.
///
/// THE MUTEX IS DESTROYED SECOND TO LAST, after every fini that could take it. Moving it up reads
/// as tidier, since it was locked first, and destroys a lock the stream connection and ctrl
/// teardowns are still using.
///
/// AND THE LAST LINE HANDS freeaddrinfo A NULL ON EVERY PSN SESSION. PP504 established why: that
/// arm of session_init never calls getaddrinfo and the struct was memset to zero. It works because
/// ws2_32 loops on the pointer - neither POSIX nor Microsoft's documentation says freeaddrinfo
/// accepts NULL. Relied upon, not guaranteed, and named here because a managed equivalent may not
/// accept null and the reflex repair would be a guard on a path that has always worked.
/// </summary>
public static class SessionRelease
{
    /// <summary>The nine steps, in the order the C runs them.</summary>
    public static IReadOnlyList<SessionReleaseStep> Order { get; } =
        [.. Enum.GetValues<SessionReleaseStep>()];

    /// <summary>The steps that only run when the session has one - the two PSN pieces.</summary>
    public static IReadOnlySet<SessionReleaseStep> Conditional { get; } =
        new HashSet<SessionReleaseStep> { SessionReleaseStep.Rudp, SessionReleaseStep.Holepunch };

    /// <summary>The one step that holds the state mutex.</summary>
    public const SessionReleaseStep HoldsTheMutex = SessionReleaseStep.FreeStringsUnderLock;

    /// <summary>What the C calls at each step that is a call.</summary>
    public static IReadOnlyDictionary<SessionReleaseStep, string> Calls { get; } =
        new Dictionary<SessionReleaseStep, string>
        {
            [SessionReleaseStep.StreamConnection] = "chiaki_stream_connection_fini(&session->stream_connection);",
            [SessionReleaseStep.Ctrl] = "chiaki_ctrl_fini(&session->ctrl);",
            [SessionReleaseStep.Rudp] = "chiaki_rudp_fini(session->rudp);",
            [SessionReleaseStep.Holepunch] = "chiaki_holepunch_session_fini(session->holepunch_session);",
            [SessionReleaseStep.StopPipe] = "chiaki_stop_pipe_fini(&session->stop_pipe);",
            [SessionReleaseStep.StateCond] = "chiaki_cond_fini(&session->state_cond);",
            [SessionReleaseStep.StateMutex] = "chiaki_mutex_fini(&session->state_mutex);",
            [SessionReleaseStep.FreeAddrInfo] = "freeaddrinfo(session->connect_info.host_addrinfos);",
        };

    /// <summary>The steps a release actually runs, given whether the session was a PSN one.</summary>
    public static IReadOnlyList<SessionReleaseStep> RunFor(bool isPsn)
        => [.. Order.Where(step => isPsn || !Conditional.Contains(step))];

    /// <summary>
    /// Whether the address list handed to freeaddrinfo is null for this kind of session.
    ///
    /// True for PSN, and it is the same unguarded call either way.
    /// </summary>
    public static bool AddrInfoIsNull(bool isPsn) => isPsn;

    /// <summary>
    /// Whether the mutex is destroyed after every step that could take it.
    ///
    /// Asked as a position rather than stated as a fact, so a reordered list answers no.
    /// </summary>
    public static bool TheMutexOutlivesEveryUser(IReadOnlyList<SessionReleaseStep> order)
    {
        ArgumentNullException.ThrowIfNull(order);

        int destroyed = order.ToList().IndexOf(SessionReleaseStep.StateMutex);
        if (destroyed < 0)
            return false;

        // freeaddrinfo is the only step after it, and it touches no lock.
        return order.Skip(destroyed + 1).All(step => step == SessionReleaseStep.FreeAddrInfo);
    }
}

/// <summary>
/// PP507: the C's own order, so a reordering is caught here rather than on a teardown.
/// </summary>
public static class SessionReleaseSource
{
    /// <summary>session.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(PunchProgressSource.SessionRelativePath);

    /// <summary>The release.</summary>
    public static string? FiniBody(string source)
        => CFunction.Body(source, "CHIAKI_EXPORT void chiaki_session_fini");

    /// <summary>
    /// Whether every call this models still appears, in this order.
    ///
    /// One pass with a moving cursor, so a step that moved earlier fails as surely as one that
    /// moved later.
    /// </summary>
    public static bool TheStepsRunInThisOrder(string finiBody)
    {
        ArgumentNullException.ThrowIfNull(finiBody);

        string text = finiBody.Replace("\r\n", "\n", StringComparison.Ordinal);
        var cursor = 0;

        foreach (SessionReleaseStep step in SessionRelease.Order)
        {
            if (!SessionRelease.Calls.TryGetValue(step, out string? call))
                continue;

            int at = text.IndexOf(call, cursor, StringComparison.Ordinal);
            if (at < 0)
                return false;

            cursor = at + call.Length;
        }

        return true;
    }

    /// <summary>
    /// Whether the state mutex is still held for the two frees and nothing else.
    ///
    /// The lock, the two frees, the unlock - and the unlock BEFORE the first fini. A managed
    /// teardown holding a lock across the whole method is the shape this refuses.
    /// </summary>
    public static bool OnlyTheTwoFreesAreUnderTheMutex(string finiBody)
    {
        ArgumentNullException.ThrowIfNull(finiBody);

        string text = finiBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int locked = text.IndexOf("chiaki_mutex_lock(&session->state_mutex)", StringComparison.Ordinal);
        int unlocked = text.IndexOf("chiaki_mutex_unlock(&session->state_mutex);", StringComparison.Ordinal);
        int firstFini = text.IndexOf(
            SessionRelease.Calls[SessionReleaseStep.StreamConnection], StringComparison.Ordinal);

        if (locked < 0 || unlocked < locked || firstFini < unlocked)
            return false;

        string held = text[locked..unlocked];

        return held.Contains("free(session->login_pin);", StringComparison.Ordinal)
            && held.Contains("free(session->quit_reason_str);", StringComparison.Ordinal)
            && !held.Contains("_fini(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether freeaddrinfo is still called unguarded, which is the reliance this names.
    ///
    /// A guard appearing here would repair a path that has always worked and would make the note
    /// above wrong, so it is asserted as it is.
    /// </summary>
    public static bool TheAddrInfoCallIsUnguarded(string finiBody)
    {
        ArgumentNullException.ThrowIfNull(finiBody);

        string text = finiBody.Replace("\r\n", "\n", StringComparison.Ordinal);
        string call = SessionRelease.Calls[SessionReleaseStep.FreeAddrInfo];

        int at = text.IndexOf(call, StringComparison.Ordinal);
        int mutex = text.IndexOf(
            SessionRelease.Calls[SessionReleaseStep.StateMutex], StringComparison.Ordinal);

        // Nothing between the mutex's destruction and the call, which is where a guard would go.
        return at >= 0
            && mutex >= 0
            && at > mutex
            && !text[mutex..at].Contains("if(", StringComparison.Ordinal);
    }

    /// <summary>Whether the two PSN steps are still the only guarded ones.</summary>
    public static bool OnlyTheTwoPsnStepsAreGuarded(string finiBody)
    {
        ArgumentNullException.ThrowIfNull(finiBody);

        string text = finiBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!text.Contains("if(session->rudp)", StringComparison.Ordinal)
            || !text.Contains("if(session->holepunch_session)", StringComparison.Ordinal))
        {
            return false;
        }

        // Every unconditional step's call is preceded by a newline and a tab, not by a guard.
        return SessionRelease.Order
            .Where(step => !SessionRelease.Conditional.Contains(step))
            .Where(SessionRelease.Calls.ContainsKey)
            .All(step => text.Contains($"\n\t{SessionRelease.Calls[step]}", StringComparison.Ordinal));
    }
}
