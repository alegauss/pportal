namespace ChiakiNg.Protocol;

/// <summary>Every wait the holepunch flow makes, and what bounds it.</summary>
public enum HolepunchWait
{
    /// <summary>For the websocket to report itself open.</summary>
    WebSocketOpen,

    /// <summary>For the notifications that say the session was created.</summary>
    SessionCreated,

    /// <summary>For the notifications that say it started.</summary>
    SessionStarted,

    /// <summary>For the gateway lookup on its thread.</summary>
    GatewayDiscovery,
}

/// <summary>How a wait for the websocket ends.</summary>
public enum WebSocketWaitOutcome
{
    /// <summary>It opened.</summary>
    Opened,

    /// <summary>
    /// It failed to connect - and nothing signals that, so this outcome never arrives at the
    /// waiter. It is named so the port can say what does not happen.
    /// </summary>
    NeverSignalled,
}

/// <summary>
/// PP258: creating a session, and the one wait nothing can end.
///
/// THE ONLY UNBOUNDED WAIT IS THE ONE THAT CAN HANG. Every other wait in the file carries a timeout.
/// This one waits for the websocket to report itself open and carries neither a deadline nor a
/// cancellation check. The thread it waits on sets that state and signals only after it has
/// connected; its failure path jumps to a cleanup that clears a different flag and signals nothing
/// at all. So a console that refuses the connection leaves the caller blocked with no timeout to
/// expire and no state to change. <see cref="IsBounded"/> and <see cref="CanBeCancelled"/> are
/// separate answers, and the wait that is neither is the same one.
///
/// FOUR CANCELLATION CHECKS SURROUND IT AND NONE IS INSIDE. They bracket every step of the create
/// except the blocking one - the shape PP242 counted five of in the punch, arranged here so that the
/// one thing cancellation would be for is the one thing it cannot reach.
///
/// AND THE CHECK BESIDE THE WAIT IS NOT COMPILED. Its result is inspected by an assert, and the
/// build configures Release with NDEBUG - read out of the build cache, not assumed. In the shipped
/// binary there is no inspection, so a wait that fails re-tests the condition and waits again. See
/// <see cref="AssertsAreCompiledOut"/>.
/// </summary>
public static class SessionCreate
{
    /// <summary>How long the notification waits are given, in seconds.</summary>
    public const int TimeoutSeconds = 30;

    /// <summary>Whether the two notification waits share that budget. They do not.</summary>
    public const bool SharesOneTimeout = false;

    /// <summary>
    /// Whether the shipped build keeps its asserts. It does not - Release carries NDEBUG.
    /// </summary>
    public const bool AssertsAreCompiledOut = true;

    /// <summary>How many cancellation checks the create makes.</summary>
    public const int CancelChecks = 4;

    /// <summary>Whether a wait has a deadline.</summary>
    public static bool IsBounded(HolepunchWait wait) => wait != HolepunchWait.WebSocketOpen;

    /// <summary>And whether a cancellation can reach it.</summary>
    public static bool CanBeCancelled(HolepunchWait wait) => wait != HolepunchWait.WebSocketOpen;

    /// <summary>Whether nothing at all can end this wait but the thing it waits for.</summary>
    public static bool NothingCanEndIt(HolepunchWait wait)
        => !IsBounded(wait) && !CanBeCancelled(wait);

    /// <summary>Every wait nothing can end. One.</summary>
    public static IReadOnlyList<HolepunchWait> Unendable { get; } =
        [.. Enum.GetValues<HolepunchWait>().Where(NothingCanEndIt)];

    /// <summary>
    /// Whether the websocket thread signals the waiter for this outcome.
    ///
    /// Only for the one that opened. The failure path clears a flag the waiter does not read and
    /// signals nothing.
    /// </summary>
    public static bool SignalsTheWaiter(WebSocketWaitOutcome outcome)
        => outcome == WebSocketWaitOutcome.Opened;

    /// <summary>
    /// What the waiter observes for each outcome - which for a failure is nothing, forever.
    /// </summary>
    public static string WhatTheWaiterSees(WebSocketWaitOutcome outcome)
        => SignalsTheWaiter(outcome) ? "SESSION_STATE_WS_OPEN" : "";

    /// <summary>The flag the failure path does clear, which the waiter never reads.</summary>
    public const string ClearedInstead = "ws_open";

    /// <summary>And the one the waiter is watching.</summary>
    public const string WatchedFor = "SESSION_STATE_WS_OPEN";
}

/// <summary>
/// PP258: the create where the core writes it.
/// </summary>
public static class SessionCreateSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>
    /// How many waits in the whole file carry a deadline, and how many do not.
    /// </summary>
    public static (int Bounded, int Unbounded) HowManyWaits(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int bounded = text.Split("chiaki_cond_timedwait(", StringSplitOptions.None).Length - 1;
        int unbounded = text.Split("chiaki_cond_wait(", StringSplitOptions.None).Length - 1;

        return (bounded, unbounded);
    }

    /// <summary>And whether the unbounded one is still the websocket's.</summary>
    public static bool TheUnboundedOneIsStillTheWebSocket(string core)
        => Body(core).Contains(
            $"while (!(session->state & {SessionCreate.WatchedFor}))", StringComparison.Ordinal)
            && Body(core).Contains(
                "err = chiaki_cond_wait(&session->state_cond, &session->state_mutex);",
                StringComparison.Ordinal);

    /// <summary>
    /// Whether the cancellation checks are still all outside that loop.
    /// </summary>
    public static bool NoCancellationIsStillInsideTheWait(string core)
    {
        string body = Body(core);

        int opens = body.IndexOf(
            $"while (!(session->state & {SessionCreate.WatchedFor}))", StringComparison.Ordinal);
        if (opens < 0)
            return false;

        int closes = body.IndexOf(
            "    chiaki_mutex_unlock(&session->state_mutex);", opens, StringComparison.Ordinal);

        return closes > opens
            && !body[opens..closes].Contains("main_should_stop", StringComparison.Ordinal);
    }

    /// <summary>How many cancellation checks the create makes around it.</summary>
    public static int HowManyCancelChecks(string core)
        => Body(core).Split("if(session->main_should_stop)", StringSplitOptions.None).Length - 1;

    /// <summary>
    /// Whether the wait's result is still inspected only by an assert.
    /// </summary>
    public static bool TheResultIsStillOnlyAsserted(string core)
    {
        string body = Body(core);

        int waits = body.IndexOf(
            "err = chiaki_cond_wait(&session->state_cond,", StringComparison.Ordinal);
        if (waits < 0)
            return false;

        int closes = body.IndexOf("\n    }", waits, StringComparison.Ordinal);
        if (closes < 0)
            return false;

        string after = body[waits..closes];

        return after.Contains("assert(err == CHIAKI_ERR_SUCCESS);", StringComparison.Ordinal)
            && !after.Contains("if (err", StringComparison.Ordinal)
            && !after.Contains("if(err", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the websocket thread still signals only on success - the other half of the hang.
    /// </summary>
    public static bool TheThreadStillSignalsOnlyOnSuccess(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, and spelled as the DEFINITION spells it: the forward declaration at the top writes
        // the star against the name and the definition writes it against the type, so a search for
        // one finds only the other. This is the trap PP213 named, met from the other side.
        int thread = text.LastIndexOf("static void* websocket_thread_func(", StringComparison.Ordinal);
        if (thread < 0)
            return false;

        int connected = text.IndexOf("session->ws_open = true;", thread, StringComparison.Ordinal);
        int signals = text.IndexOf(
            "err = chiaki_cond_signal(&session->state_cond);", thread, StringComparison.Ordinal);

        // The connection failure jumps past both.
        int fails = text.IndexOf(
            "Connecting to push notification WebSocket %s failed with CURL error", thread,
            StringComparison.Ordinal);

        return connected > 0
            && signals > connected
            && fails > 0
            && fails < connected
            && text[fails..connected].Contains("goto cleanup;", StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether its cleanup still clears a different flag than the one being watched.
    /// </summary>
    public static bool TheCleanupStillClearsTheOtherFlag(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // The thread's OWN cleanup: this file has several labels by that name, and the first one
        // belongs to a different function entirely.
        // LAST, and spelled as the DEFINITION spells it: the forward declaration at the top writes
        // the star against the name and the definition writes it against the type, so a search for
        // one finds only the other. This is the trap PP213 named, met from the other side.
        int thread = text.LastIndexOf("static void* websocket_thread_func(", StringComparison.Ordinal);
        if (thread < 0)
            return false;

        int cleanup = text.IndexOf("\ncleanup:\n", thread, StringComparison.Ordinal);
        if (cleanup < 0)
            return false;

        // Take the label's own stretch, to the end of the function.
        int ends = text.IndexOf("\n}", cleanup, StringComparison.Ordinal);
        string body = ends < 0 ? text[cleanup..] : text[cleanup..ends];

        return body.Contains($"session->{SessionCreate.ClearedInstead} = false;", StringComparison.Ordinal)
            && !body.Contains("chiaki_cond_signal", StringComparison.Ordinal)
            && !body.Contains(SessionCreate.WatchedFor, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the unshared-timeout comment still appears twice, one of the two mistyped.
    /// </summary>
    public static bool TheCommentIsStillThereTwice(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Split(
                "FIXME: We're currently not using a shared timeout for both", StringSplitOptions.None)
            .Length - 1 == 2
            && text.Contains("exceeing SESSION_CREATION_TIMEOUT_SEC", StringComparison.Ordinal)
            && text.Contains("exceeding SESSION_START_TIMEOUT_SEC", StringComparison.Ordinal);
    }

    /// <summary>chiaki_holepunch_session_create's body.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.LastIndexOf(
            "CHIAKI_EXPORT ChiakiErrorCode chiaki_holepunch_session_create(", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf(
            "CHIAKI_EXPORT ChiakiErrorCode chiaki_holepunch_session_start(", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
