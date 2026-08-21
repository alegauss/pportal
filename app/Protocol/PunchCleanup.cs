namespace ChiakiNg.Protocol;

/// <summary>Which of the two endings a punch reaches.</summary>
public enum PunchEnding
{
    /// <summary>A candidate was chosen and is handed to the caller.</summary>
    Chosen,

    /// <summary>Nothing was, and everything opened is closed.</summary>
    Failed,
}

/// <summary>What happens to one socket when the punch ends.</summary>
public enum SocketFate
{
    /// <summary>Closed here.</summary>
    Closed,

    /// <summary>Handed to the caller - the session's own handle is invalidated so it lets go.</summary>
    HandedOver,

    /// <summary>It was never open.</summary>
    Untouched,
}

/// <summary>
/// PP249: how the punch ends, twice.
///
/// THE LITERAL RETURN IS LOAD-BEARING, AND ONE PATH PROVES IT. After a candidate is chosen the code
/// waits for the console's own request. A timeout there is FORGIVEN when this side has already
/// answered one - and forgiveness is a fall-through, with the error variable still holding the
/// timeout. The function then returns a literal success. PP244 measured that variable collecting
/// codes nothing clears and said the literal return was all that kept them in; this is the path
/// where the staleness is deliberate rather than accidental. Rewriting the return to hand back the
/// variable - which reads like tidying - turns a working punch into a reported timeout. See
/// <see cref="ReturnedCode"/> against <see cref="HeldCode"/>.
///
/// TWO CLEANUPS THAT MUST DIFFER IN EXACTLY THREE PLACES. The chosen socket is spared on the way
/// out and closed on the way down. The session's own handles are invalidated on success so the
/// caller owns what it was given, and closed on failure. And one of the two guards its event array
/// against null while the other does not - harmless, since the count is zero whenever the pointer
/// is, but it is the difference that makes the other two look incidental.
/// </summary>
public static class PunchCleanup
{
    /// <summary>
    /// What the caller is told, which is a literal on the way out.
    /// </summary>
    public static bool ReturnedCodeIsSuccess(PunchEnding ending) => ending == PunchEnding.Chosen;

    /// <summary>
    /// The code the function is HOLDING when it returns, which is not always what it returns.
    /// </summary>
    /// <param name="ending">How the punch ended.</param>
    /// <param name="timedOutWaiting">Whether the wait for the console's request timed out.</param>
    /// <param name="alreadyAnswered">Whether this side had already answered one.</param>
    public static string HeldCode(PunchEnding ending, bool timedOutWaiting, bool alreadyAnswered)
    {
        if (ending == PunchEnding.Failed)
            return "CHIAKI_ERR_*";

        // The forgiven timeout: it fell through, and nothing reset the variable.
        return timedOutWaiting && alreadyAnswered ? "CHIAKI_ERR_TIMEOUT" : "CHIAKI_ERR_SUCCESS";
    }

    /// <summary>And what it actually hands back.</summary>
    public static string ReturnedCode(PunchEnding ending)
        => ending == PunchEnding.Chosen ? "CHIAKI_ERR_SUCCESS" : "CHIAKI_ERR_*";

    /// <summary>
    /// Whether the two disagree - which is the whole point, and is true on exactly one path.
    /// </summary>
    public static bool TheReturnDisagreesWithWhatIsHeld(
        PunchEnding ending, bool timedOutWaiting, bool alreadyAnswered)
        => !string.Equals(
            HeldCode(ending, timedOutWaiting, alreadyAnswered),
            ReturnedCode(ending),
            StringComparison.Ordinal);

    /// <summary>
    /// Whether the timeout is forgiven, which needs this side to have answered a request already.
    /// </summary>
    public static bool TimeoutIsForgiven(bool alreadyAnswered) => alreadyAnswered;

    /// <summary>
    /// What becomes of one socket at the end.
    /// </summary>
    /// <param name="ending">How the punch ended.</param>
    /// <param name="open">Whether this socket was open.</param>
    /// <param name="chosen">Whether it is the one the punch settled on.</param>
    public static SocketFate FateOf(PunchEnding ending, bool open, bool chosen)
    {
        if (!open)
            return SocketFate.Untouched;

        if (ending == PunchEnding.Failed)
            return SocketFate.Closed;

        return chosen ? SocketFate.HandedOver : SocketFate.Closed;
    }

    /// <summary>
    /// The three places the two cleanups have to differ, named so a port cannot fold them into one.
    /// </summary>
    public static IReadOnlyList<string> WhereTheTwoCleanupsDiffer { get; } =
    [
        "the chosen socket is spared on success and closed on failure",
        "the session's handles are invalidated on success and closed on failure",
        "one guards the event array against null and the other does not",
    ];
}

/// <summary>
/// PP249: the ending where the core writes it.
/// </summary>
public static class PunchCleanupSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the timeout is still forgiven by falling through.</summary>
    public static bool TheTimeoutIsStillForgivenByFallingThrough(string core)
        => Body(core).Contains(
            """
            if(err == CHIAKI_ERR_TIMEOUT)
                {
                    if(!responded)
                        goto cleanup_sockets;
                }
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    /// <summary>
    /// And whether nothing between that fall-through and the return clears the variable - which is
    /// what makes the literal load-bearing.
    /// </summary>
    public static bool NothingClearsTheCodeBeforeTheReturn(string core)
    {
        string body = Body(core);

        int forgiven = body.IndexOf("if(err == CHIAKI_ERR_TIMEOUT)", StringComparison.Ordinal);
        int returns = body.IndexOf("    return CHIAKI_ERR_SUCCESS;", StringComparison.Ordinal);
        if (forgiven < 0 || returns < forgiven)
            return false;

        return !body[forgiven..returns].Contains("err = CHIAKI_ERR_SUCCESS", StringComparison.Ordinal);
    }

    /// <summary>Whether the success path still spares the chosen socket.</summary>
    public static bool TheSuccessPathStillSparesTheChosenSocket(string core)
    {
        string body = Body(core);

        return body.Contains(
                "if (session->ipv4_sock != *out && (!CHIAKI_SOCKET_IS_INVALID(session->ipv4_sock)))",
                StringComparison.Ordinal)
            && body.Contains(
                "if(!CHIAKI_SOCKET_IS_INVALID(socks[j]) && socks[j] != selected_sock)",
                StringComparison.Ordinal);
    }

    /// <summary>And whether the failure path still closes everything.</summary>
    public static bool TheFailurePathStillClosesEverything(string core)
    {
        string tail = Tail(core);

        return tail.Contains(
                "if(!CHIAKI_SOCKET_IS_INVALID(session->ipv4_sock))\n    {", StringComparison.Ordinal)
            && tail.Contains("if(!CHIAKI_SOCKET_IS_INVALID(socks[j]))\n            {", StringComparison.Ordinal)

            // No sparing clause anywhere in it.
            && !tail.Contains("!= selected_sock", StringComparison.Ordinal)
            && !tail.Contains("!= *out", StringComparison.Ordinal);
    }

    /// <summary>Whether the success path still hands the handles over by invalidating them.</summary>
    public static bool TheHandlesAreStillHandedOver(string core)
        => Body(core).Contains(
            """
            session->ipv4_sock = CHIAKI_INVALID_SOCKET;
                session->ipv6_sock = CHIAKI_INVALID_SOCKET;
                free(socks);
                return CHIAKI_ERR_SUCCESS;
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    /// <summary>
    /// Whether the null guard on the event array is still on one cleanup and not the other.
    /// </summary>
    public static bool OnlyOneCleanupStillGuardsTheEventArray(string core)
    {
        string body = Body(core);
        string tail = Tail(core);

        return body.Contains(
                "for (size_t i = 0; i < poll_ctx.events_count; i++)", StringComparison.Ordinal)
            && tail.Contains(
                "for (size_t i = 0; poll_ctx.events && i < poll_ctx.events_count; i++)",
                StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether the caller's socket is still written before the last thing that can fail.
    /// </summary>
    public static bool TheOutputIsStillWrittenBeforeTheLastFailure(string core)
    {
        string body = Body(core);

        int written = body.IndexOf("    *out = selected_sock;", StringComparison.Ordinal);
        int canFail = body.IndexOf(
            "err = receive_request_send_response_ps(", StringComparison.Ordinal);

        return written >= 0 && canFail > written;
    }

    /// <summary>The success path, from the output assignment to its return.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.IndexOf("    *out = selected_sock;", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("cleanup_sockets:", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    /// <summary>And the failure path, from its label to the closing brace.</summary>
    private static string Tail(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.IndexOf("cleanup_sockets:\n    for (size_t i = 0;", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\n    return err;", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
