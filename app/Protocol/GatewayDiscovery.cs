namespace ChiakiNg.Protocol;

/// <summary>Which of the five places writes the gateway status.</summary>
public enum StatusWriter
{
    /// <summary>The thread, having found a gateway.</summary>
    ThreadFound,

    /// <summary>The thread, having not.</summary>
    ThreadNotFound,

    /// <summary>The synchronous fallback, having found one.</summary>
    FallbackFound,

    /// <summary>And having not.</summary>
    FallbackNotFound,

    /// <summary>The wait, giving up. This one runs while the thread is still alive.</summary>
    Timeout,
}

/// <summary>How the discovery ended for the caller.</summary>
public enum DiscoveryEnding
{
    /// <summary>The thread finished inside the window and was joined.</summary>
    Joined,

    /// <summary>It did not, and the wait returned without joining it.</summary>
    Abandoned,

    /// <summary>The thread could not be created, so the work ran here instead.</summary>
    RanInline,
}

/// <summary>
/// PP255: finding the router on a thread, with seven seconds to do it.
///
/// THE TIMEOUT UNLOCKS ONE LINE BEFORE IT WRITES. Five places set the gateway status. The two in the
/// thread hold the state mutex. The two in the synchronous fallback do not, which is safe because no
/// thread exists on that path. The fifth is the timeout, and it releases the mutex and THEN writes -
/// while the thread it has just given up on is still running and will write the same field, under
/// the lock, whenever it finishes. <see cref="HoldsTheLock"/> and <see cref="AThreadIsAlive"/> are
/// separate answers so the one case that is both unlocked and contended can be named.
///
/// Which write lands last is scheduling. PP252 reads that field to choose between asking the gateway
/// for an external address and going straight to STUN, so a router found a fraction past the window
/// is used or ignored by timing, and no log tells the two apart.
///
/// THE ABANDONED THREAD IS NOT LEAKED. It looks like it is - the timeout path returns without a join
/// - but the session teardown joins any thread still marked running. That was checked rather than
/// assumed, and it is what makes this a race on one field rather than a thread nobody owns.
/// <see cref="IsEventuallyJoined"/> says so.
/// </summary>
public static class GatewayDiscovery
{
    /// <summary>How long the wait gives the thread.</summary>
    public const int TimeoutMs = 7000;

    /// <summary>Whether this writer holds the state mutex while it writes.</summary>
    public static bool HoldsTheLock(StatusWriter writer) => writer switch
    {
        StatusWriter.ThreadFound or StatusWriter.ThreadNotFound => true,

        // The fallback runs with no thread in existence, and the timeout has already released it.
        _ => false,
    };

    /// <summary>Whether another thread can be writing the same field at that moment.</summary>
    public static bool AThreadIsAlive(StatusWriter writer) => writer switch
    {
        // The fallback exists precisely because the thread could not be created.
        StatusWriter.FallbackFound or StatusWriter.FallbackNotFound => false,

        _ => true,
    };

    /// <summary>
    /// Whether a writer is unsynchronised while something else may be writing - the race, as one
    /// question.
    /// </summary>
    public static bool Races(StatusWriter writer)
        => !HoldsTheLock(writer) && AThreadIsAlive(writer);

    /// <summary>Every writer that races. Exactly one.</summary>
    public static IReadOnlyList<StatusWriter> Racing { get; } =
        [.. Enum.GetValues<StatusWriter>().Where(Races)];

    /// <summary>
    /// What the status ends up as, and whether that is settled.
    /// </summary>
    /// <param name="ending">How the discovery ended.</param>
    /// <param name="threadWouldFind">What the thread will conclude, if it is still going.</param>
    /// <returns>The status written by the wait, and whether the thread can still overwrite it.</returns>
    public static (GatewayStatus Written, bool CanBeOverwritten) Outcome(
        DiscoveryEnding ending, bool threadWouldFind)
    {
        return ending switch
        {
            // Abandoned: the thread is still running, so it can still overwrite this. The name is
            // on the return type above and not repeated here - an element name on a switch arm is
            // dropped against a named target type, which the compiler reports and nothing read.
            DiscoveryEnding.Abandoned => (GatewayStatus.NotFound, true),

            // Both settled paths agree with what the work found.
            _ => (threadWouldFind ? GatewayStatus.Found : GatewayStatus.NotFound, false),
        };
    }

    /// <summary>
    /// Whether the thread is joined in the end, whichever way the wait went.
    ///
    /// It is - by the teardown, not by this function. Checked rather than assumed.
    /// </summary>
    public static bool IsEventuallyJoined(DiscoveryEnding ending)
        => ending != DiscoveryEnding.RanInline;

    /// <summary>And whether this function is the one that joins it.</summary>
    public static bool JoinedHere(DiscoveryEnding ending) => ending == DiscoveryEnding.Joined;

    /// <summary>What the caller is told, which is the same thing on every path.</summary>
    public const bool AlwaysReportsSuccess = true;
}

/// <summary>
/// PP255: the discovery where the core writes it.
/// </summary>
public static class GatewayDiscoverySource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>Whether the window is still seven seconds.</summary>
    public static bool TheWindowIsStillSeven(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            $"#define UPNP_DISCOVER_TIMEOUT_MS {GatewayDiscovery.TimeoutMs}", StringComparison.Ordinal);
    }

    /// <summary>Whether the thread still writes the status under the lock.</summary>
    public static bool TheThreadStillWritesUnderTheLock(string core)
    {
        string body = Body(core);

        int locks = body.IndexOf("chiaki_mutex_lock(&session->state_mutex);", StringComparison.Ordinal);
        int writes = body.IndexOf("session->gw_status = GATEWAY_STATUS_FOUND;", StringComparison.Ordinal);
        int unlocks = body.IndexOf("chiaki_mutex_unlock(&session->state_mutex);", StringComparison.Ordinal);

        return locks >= 0 && writes > locks && unlocks > writes;
    }

    /// <summary>
    /// THE FINDING. Whether the timeout still releases the mutex before writing the status.
    ///
    /// True means the race is present, which is what this asserts rather than a fix.
    /// </summary>
    public static bool TheTimeoutStillUnlocksFirst(string core)
        => Body(core).Contains(
            """
            chiaki_mutex_unlock(&session->state_mutex);
                    CHIAKI_LOGW(session->log, "UPnP discovery timed out after %d ms, skipping", UPNP_DISCOVER_TIMEOUT_MS);
                    session->gw_status = GATEWAY_STATUS_NOT_FOUND;
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    /// <summary>And whether it still returns without joining.</summary>
    public static bool TheTimeoutStillReturnsWithoutJoining(string core)
    {
        string body = Body(core);

        int gaveUp = body.IndexOf("UPnP discovery timed out after", StringComparison.Ordinal);
        if (gaveUp < 0)
            return false;

        int returns = body.IndexOf("return CHIAKI_ERR_SUCCESS;", gaveUp, StringComparison.Ordinal);
        int joins = body.IndexOf("chiaki_thread_join(&session->upnp_thread", gaveUp, StringComparison.Ordinal);

        return returns > gaveUp && (joins < 0 || joins > returns);
    }

    /// <summary>
    /// Whether the teardown still joins it - which is what keeps the abandonment from being a leak.
    /// </summary>
    public static bool TheTeardownStillJoinsIt(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int fini = text.IndexOf(
            "CHIAKI_EXPORT void chiaki_holepunch_session_fini(", StringComparison.Ordinal);
        if (fini < 0)
            fini = text.IndexOf("chiaki_holepunch_session_fini(Session", StringComparison.Ordinal);

        if (fini < 0)
            return false;

        return text[fini..].Contains(
            "if(session->upnp_thread_running)", StringComparison.Ordinal)
            && text[fini..].Contains(
                "chiaki_thread_join(&session->upnp_thread, NULL);", StringComparison.Ordinal);
    }

    /// <summary>Whether the fallback still repeats the thread's body without the locking.</summary>
    public static bool TheFallbackStillRepeatsItUnlocked(string core)
    {
        string body = Body(core);

        int fallback = body.IndexOf(
            "Failed to create UPnP discovery thread, falling back to synchronous", StringComparison.Ordinal);
        if (fallback < 0)
            return false;

        string after = body[fallback..];

        return after.Contains("session->gw_status = GATEWAY_STATUS_FOUND;", StringComparison.Ordinal)
            && after.Contains("session->gw_status = GATEWAY_STATUS_NOT_FOUND;", StringComparison.Ordinal)
            && !after[..after.IndexOf("return CHIAKI_ERR_SUCCESS;", StringComparison.Ordinal)]
                .Contains("chiaki_mutex_lock", StringComparison.Ordinal);
    }

    /// <summary>How many places write the status here. Five.</summary>
    public static int HowManyWriteTheStatus(string core)
    {
        string body = Body(core);

        return body.Split("session->gw_status = ", StringSplitOptions.None).Length - 1;
    }

    /// <summary>The thread function and the call that starts it.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.IndexOf("static void *upnp_discover_thread_func(", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf(
            "CHIAKI_EXPORT ChiakiErrorCode chiaki_holepunch_session_create(", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
