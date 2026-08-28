using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What a second arrival of a once-only message does.</summary>
public enum SecondArrival
{
    /// <summary>The first one: it is acted on.</summary>
    Accepted,

    /// <summary>A duplicate, logged at warning and dropped.</summary>
    WarnedAndDropped,

    /// <summary>A duplicate, logged at info and dropped.</summary>
    NotedAndDropped,
}

/// <summary>One flag that makes a message type once-only.</summary>
/// <param name="Field">The session field, as the C names it.</param>
/// <param name="Type">The message type whose second arrival it refuses.</param>
/// <param name="ReadUnderTheLock">Whether the handler takes state_mutex to read it.</param>
/// <param name="SecondTime">What the duplicate gets.</param>
public readonly record struct OnceOnlyFlag(
    string Field, string Type, bool ReadUnderTheLock, SecondArrival SecondTime);

/// <summary>
/// PP467, under PP294: the two arriving ctrl messages that may only arrive once, and the locking
/// around the flags that enforce it.
///
/// PP294's section names the risk this is about: "the risk is a type handled in the wrong state rather
/// than an algorithm translated wrongly". PP466 settled which of the 22 types arrive. Two of those ten
/// are once-only, and both detect a duplicate with a session-level bool - which is state, held outside
/// ctrl.c, and read inconsistently.
///
/// THE TWO DUPLICATES ARE TREATED THE SAME AND LOGGED DIFFERENTLY. A second SESSION_ID is a WARNING
/// and a second SWITCH_TO_STREAM_CONNECTION is INFO, and both are dropped without touching anything.
/// Nothing about the channel says why one is more alarming than the other; it is worth carrying
/// because a port that normalised the levels would lose the only distinction the C draws between them.
///
/// AND THE LOCKING IS INCONSISTENT AND HARMLESS, WHICH IS WORTH STATING PRECISELY BECAUSE BOTH
/// OBVIOUS FIXES ARE WRONG. Of the four reads of these flags inside ctrl.c, exactly one takes
/// state_mutex: the session-id handler's. The other three - two of the same flag, one of the other -
/// do not.
///
/// It is safe, and not by luck. THE CTRL THREAD IS THE ONLY WRITER of both, so no read on that thread
/// can see a value another thread has moved. What the locks are for is the OTHER side: the session
/// thread waits on state_cond and reads both flags in its predicates, and the write locks and signals
/// so that wait wakes. So a managed port that mirrored the locking literally would take a lock in one
/// of four places for no reason, and one that dropped the locks because "the reads are unlocked
/// anyway" would leave the session thread waiting out its timeout.
/// </summary>
public static class CtrlOnceOnly
{
    /// <summary>The two flags, and how each is read.</summary>
    public static IReadOnlyList<OnceOnlyFlag> Flags { get; } =
    [
        new(
            "ctrl_session_id_received",
            "SESSION_ID",
            ReadUnderTheLock: true,
            SecondArrival.WarnedAndDropped),

        new(
            "stream_connection_switch_received",
            "SWITCH_TO_STREAM_CONNECTION",
            ReadUnderTheLock: false,
            SecondArrival.NotedAndDropped),
    ];

    /// <summary>
    /// What arriving <paramref name="type"/> gets, given whether its flag is already up.
    ///
    /// A type that is not once-only is always accepted: nothing else in the ten counts arrivals.
    /// </summary>
    public static SecondArrival Arriving(string type, bool alreadySeen)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!alreadySeen)
            return SecondArrival.Accepted;

        foreach (OnceOnlyFlag flag in Flags)
        {
            if (string.Equals(flag.Type, type, StringComparison.Ordinal))
                return flag.SecondTime;
        }

        return SecondArrival.Accepted;
    }

    /// <summary>Whether a type may only be acted on once.</summary>
    public static bool IsOnceOnly(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Flags.Any(f => string.Equals(f.Type, type, StringComparison.Ordinal));
    }

    /// <summary>
    /// How many reads of these flags inside ctrl.c take the lock. One of four.
    ///
    /// A number rather than a predicate, so a lock added or removed changes it and has to be argued
    /// for rather than absorbed.
    /// </summary>
    public const int LockedReadsInCtrl = 1;

    /// <summary>And how many reads there are in total.</summary>
    public const int ReadsInCtrl = 4;

    /// <summary>ctrl.c, where the handlers are.</summary>
    public static string? LocateCtrl() => CtrlMessageCensus.LocateCtrl();

    /// <summary>session.c, where the flags live and the write locks.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? LocateSession() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>
    /// Every line in ctrl.c that reads one of the two flags, as the C spells it.
    ///
    /// Counted rather than described: <see cref="ReadsInCtrl"/> is the claim and this is the reading.
    /// </summary>
    public static int CountReadsIn(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        var found = 0;

        foreach (string line in ctrlSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.TrimStart();

            // Comments mention both flags, and an assignment is a write rather than a read.
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            foreach (OnceOnlyFlag flag in Flags)
            {
                if (line.Contains($"session->{flag.Field}", StringComparison.Ordinal)
                    && !line.Contains($"{flag.Field} =", StringComparison.Ordinal))
                {
                    found++;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the session-id handler still takes state_mutex to read its flag, and the switch handler
    /// still does not.
    ///
    /// The asymmetry itself, so a port that levelled it in either direction has to say so.
    /// </summary>
    /// <remarks>
    /// Through <see cref="CFunction"/>, and that is not a preference. Both handlers are forward-declared
    /// at the top of ctrl.c, so hand-rolled index arithmetic from the first match of a signature lands
    /// on a prototype and then measures two positions in functions neither test is in - which is the
    /// exact trap that reader's own note describes, walked into once here before this used it.
    /// </remarks>
    public static bool TheAsymmetryIsStillThere(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        string text = ctrlSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (CFunction.Body(text, "static void ctrl_message_received_session_id") is not { } locked
            || CFunction.Body(text, "static void ctrl_message_received_switch_to_stream_connection")
                is not { } unlocked)
        {
            return false;
        }

        int guard = locked.IndexOf("if(ctrl->session->ctrl_session_id_received)", StringComparison.Ordinal);
        int locks = locked.IndexOf("chiaki_mutex_lock(&ctrl->session->state_mutex);", StringComparison.Ordinal);

        bool oneLocks = guard >= 0 && locks >= 0 && locks < guard;

        int otherGuard = unlocked.IndexOf(
            "if(!ctrl->session->stream_connection_switch_received)", StringComparison.Ordinal);

        bool theOtherDoesNot = otherGuard >= 0
            && !unlocked[..otherGuard].Contains("chiaki_mutex_lock", StringComparison.Ordinal);

        return oneLocks && theOtherDoesNot;
    }

    /// <summary>
    /// Whether the WRITE still locks and signals, which is what the session thread's wait depends on.
    ///
    /// This is the half a port would drop if it concluded from the unlocked reads that the lock is
    /// decorative.
    /// </summary>
    public static bool TheWriteStillLocksAndSignals(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string text = sessionSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        int setter = text.IndexOf(
            "chiaki_session_set_stream_connection_switch_received(ChiakiSession *session)",
            StringComparison.Ordinal);
        if (setter < 0)
            return false;

        int body = text.IndexOf('{', setter);
        int end = text.IndexOf("\n}", body, StringComparison.Ordinal);
        if (body < 0 || end < body)
            return false;

        string inside = text[body..end];

        return inside.Contains("chiaki_mutex_lock(&session->state_mutex);", StringComparison.Ordinal)
            && inside.Contains("stream_connection_switch_received = true;", StringComparison.Ordinal)
            && inside.Contains("chiaki_cond_signal(&session->state_cond);", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the session thread still reads both flags in its state predicates - which is what makes
    /// the ctrl thread the only WRITER rather than the only toucher.
    /// </summary>
    public static bool TheSessionThreadStillReadsBoth(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        return Flags.All(f =>
            sessionSource.Contains($"session->{f.Field}", StringComparison.Ordinal));
    }
}
