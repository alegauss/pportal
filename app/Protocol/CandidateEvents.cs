using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What an attempt to watch a socket produced.</summary>
public enum WatchResult
{
    /// <summary>It is armed, and the loop will report it.</summary>
    Watching,

    /// <summary>There was nothing to arm - and this is reported as success.</summary>
    NothingToWatch,

    /// <summary>There was no room, or the arming failed.</summary>
    Refused,
}

/// <summary>
/// PP263: how PP245's wait actually watches its sockets.
///
/// TRUE MEANS NO ERROR, NOT WATCHED. Handing the adder an invalid socket returns success without
/// arming anything. That is right for a caller whose two named sockets are optional, and it means
/// the answer cannot be read as "this one is being watched" - <see cref="Watch"/> answers with three
/// outcomes so the two successes stay apart.
///
/// THE CALLBACK KEEPS ONLY THE LAST SOCKET TO FIRE. It records the descriptor and asks the loop to
/// exit, and that exit takes effect only after every callback already active has run. So two sockets
/// ready in the same round run two callbacks, and the second overwrites the first. Nothing is lost -
/// the other stays readable and the next turn finds it - but the field holds the LAST to fire rather
/// than the one that did, and PP245's ladder classifies exactly that one. See
/// <see cref="ReadyAfter"/>.
///
/// THE REFUSAL FOR A FULL ARRAY CANNOT FIRE. The capacity is counted from the same three sources the
/// adds walk, and an invalid socket consumes no slot - so the count is an upper bound and the array
/// cannot fill. <see cref="CapacityFor"/> and <see cref="SlotsUsed"/> compute both sides rather than
/// asserting the relationship, and the branch is kept for the reason PP244 kept its clause.
/// </summary>
public static class CandidateEvents
{
    /// <summary>What watching one socket produces.</summary>
    /// <param name="valid">Whether the socket is a real one.</param>
    /// <param name="room">Whether there is a free slot.</param>
    /// <param name="armed">Whether libevent accepted it.</param>
    public static WatchResult Watch(bool valid, bool room, bool armed)
    {
        // The invalid case is answered before anything else is looked at.
        if (!valid)
            return WatchResult.NothingToWatch;

        return room && armed ? WatchResult.Watching : WatchResult.Refused;
    }

    /// <summary>Whether an outcome is reported to the caller as success.</summary>
    public static bool ReportedAsSuccess(WatchResult result) => result != WatchResult.Refused;

    /// <summary>And whether the socket is actually being watched, which is not the same question.</summary>
    public static bool ActuallyWatching(WatchResult result) => result == WatchResult.Watching;

    /// <summary>Whether an outcome consumes one of the array's slots.</summary>
    public static bool ConsumesASlot(WatchResult result) => result == WatchResult.Watching;

    /// <summary>
    /// How many slots the array is given, counted the way the caller counts them.
    /// </summary>
    public static int CapacityFor(bool ipv4Valid, bool ipv6Valid, bool portGuessing, int guessedSockets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(guessedSockets);

        int slots = 0;
        if (ipv4Valid)
            slots++;
        if (ipv6Valid)
            slots++;
        if (portGuessing)
            slots += guessedSockets;

        return slots;
    }

    /// <summary>
    /// And how many are actually taken, given how many of the guessed ones are still open.
    /// </summary>
    public static int SlotsUsed(bool ipv4Valid, bool ipv6Valid, bool portGuessing, int guessedStillOpen)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(guessedStillOpen);

        int used = 0;
        if (ipv4Valid)
            used++;
        if (ipv6Valid)
            used++;
        if (portGuessing)
            used += guessedStillOpen;

        return used;
    }

    /// <summary>
    /// Which socket the loop reports, given the ones that came ready in a single round.
    ///
    /// The last, because each callback overwrites the field and the exit only takes effect once the
    /// round is done.
    /// </summary>
    public static int? ReadyAfter(IReadOnlyList<int> firedThisRound)
    {
        ArgumentNullException.ThrowIfNull(firedThisRound);
        return firedThisRound.Count == 0 ? null : firedThisRound[^1];
    }

    /// <summary>Whether the loop was woken at all this round.</summary>
    public static bool Triggered(IReadOnlyList<int> firedThisRound)
    {
        ArgumentNullException.ThrowIfNull(firedThisRound);
        return firedThisRound.Count > 0;
    }

    /// <summary>Whether the events stay armed between turns. They do.</summary>
    public const bool EventsPersist = true;
}

/// <summary>
/// PP263: the glue where the core writes it.
/// </summary>
public static class CandidateEventsSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>Whether an invalid socket is still answered before anything else.</summary>
    public static bool AnInvalidSocketIsStillSuccess(string core)
    {
        string body = Adder(core);

        return body.Contains(
            """
            if (CHIAKI_SOCKET_IS_INVALID(sock))
                    return true;
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    /// <summary>And whether it still comes before the capacity check.</summary>
    public static bool ItStillComesBeforeTheCapacityCheck(string core)
    {
        string body = Adder(core);

        string compact = CCall.Compact(body); // PP388

        int invalid = CCall.Mark(compact, "if (CHIAKI_SOCKET_IS_INVALID(sock))");
        int capacity = CCall.Mark(compact, "ctx->events_count >= ctx->events_capacity");

        return invalid >= 0 && capacity > invalid;
    }

    /// <summary>
    /// THE FINDING. Whether the callback still overwrites the field and asks for a deferred exit.
    /// </summary>
    public static bool TheCallbackStillOverwritesAndDefers(string core)
    {
        string body = Callback(core);

        string compact = CCall.Compact(body); // PP388

        int records = CCall.Mark(compact, "ctx->ready_sock = (chiaki_socket_t)fd;");
        int asks = CCall.At(compact, "event_base_loopexit(ctx->base, NULL)");

        // Recorded unconditionally, and the exit requested with no deadline - which is what lets
        // the rest of the round run and overwrite it.
        return records >= 0
            && asks > records
            && CCall.Mark(compact[..records], "if (ctx->event_triggered)") < 0;
    }

    /// <summary>Whether the events are still armed to persist.</summary>
    public static bool TheEventsStillPersist(string core)
        => Adder(core).Contains(
            "event_new(ctx->base, sock, EV_READ | EV_PERSIST, candidate_event_cb, ctx)",
            StringComparison.Ordinal);

    /// <summary>
    /// Whether the capacity is still counted from the same three sources the adds walk.
    /// </summary>
    public static bool TheCapacityIsStillCountedTheSameWay(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains(
                """
                if (!CHIAKI_SOCKET_IS_INVALID(session->ipv4_sock))
                        socket_slots++;
                    if (!CHIAKI_SOCKET_IS_INVALID(session->ipv6_sock))
                        socket_slots++;
                    if (session->stun_random_allocation)
                        socket_slots += socks_count;
                """.Replace("\r\n", "\n", StringComparison.Ordinal),
                StringComparison.Ordinal)
            && text.Contains("poll_ctx.events_capacity = socket_slots;", StringComparison.Ordinal)
            && text.Contains(
                "poll_ctx.events = calloc(socket_slots, sizeof(struct event *));", StringComparison.Ordinal);
    }

    /// <summary>And whether the arming failure still releases what it made.</summary>
    public static bool AFailedArmStillReleasesIt(string core)
        => Adder(core).Contains(
            """
            if (event_add(ev, NULL) < 0) {
                    event_free(ev);
                    return false;
                }
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    /// <summary>candidate_event_add_socket's body.</summary>
    private static string Adder(string core) => Between(core, "static bool candidate_event_add_socket(");

    /// <summary>And the callback's.</summary>
    private static string Callback(string core) => Between(core, "static void candidate_event_cb(");

    private static string Between(string core, string opens)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, and spelled as the definition spells it - PP258's lesson.
        int start = text.LastIndexOf(opens, StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\n}", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
