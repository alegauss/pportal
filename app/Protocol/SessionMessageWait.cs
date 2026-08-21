namespace ChiakiNg.Protocol;

/// <summary>What one notification meant to a wait for a session message.</summary>
public enum SessionMessageDisposition
{
    /// <summary>Its action met the mask; it is what the wait was for.</summary>
    Matched,

    /// <summary>Its action met no part of the mask, so the wait goes on looking.</summary>
    Ignored,

    /// <summary>It was a TERMINATE, which ends any wait whether or not one asked for it.</summary>
    Terminated,

    /// <summary>Its payload did not parse into a message at all.</summary>
    Unparseable,
}

/// <summary>
/// PP213: what a wait for a session message does with one notification.
///
/// PP212 shipped the queue and the rule underneath this: a wait removes nothing it finds, and the
/// caller clears what it consumed. This is the first of the two callers, and the ONLY place in the
/// holepunch flow where a notification is cleared as a matter of course.
///
/// The order of the questions is the load-bearing part. TERMINATE is tested BEFORE the mask, so a
/// wait asking only for an OFFER is still ended by a terminate it never asked about. A port that
/// tested the mask first would drop the terminate as "not mine", clear nothing, and sit there
/// until the timeout - which is a hang where the core has a clean cancel.
///
/// <see cref="Clearing"/> is the other half, and it is deliberately a set rather than a flag on
/// each answer: exactly one disposition takes the notification off the queue, and the three that
/// do not are three different reasons for the same consequence.
/// </summary>
public static class SessionMessageWait
{
    /// <summary>
    /// The dispositions that clear the notification they were reached through.
    ///
    /// One. A message the wait is not interested in is removed so the scan does not meet it again;
    /// everything else is left where it is. For <see cref="SessionMessageDisposition.Matched"/>
    /// that is the contract - the notification travels with the message and the caller owns it -
    /// and for the other two it is what makes a broken message permanent: nothing else in the
    /// flow removes it, so every later wait parses it and fails again.
    /// </summary>
    public static IReadOnlySet<SessionMessageDisposition> Clearing { get; } =
        new HashSet<SessionMessageDisposition> { SessionMessageDisposition.Ignored };

    /// <summary>Whether this answer takes the notification off the queue.</summary>
    public static bool Clears(SessionMessageDisposition disposition) => Clearing.Contains(disposition);

    /// <summary>
    /// What one notification means, asked in the core's order.
    /// </summary>
    /// <param name="action">
    /// The action its payload parsed to, or null when the payload did not parse at all. Null and
    /// <see cref="SessionMessageAction.Unknown"/> are NOT the same thing here: an unknown action is
    /// a message that parsed and named something this port does not know, and it is ignored and
    /// cleared like any other message the mask does not want.
    /// </param>
    /// <param name="mask">The actions this wait will accept, ORed together.</param>
    public static SessionMessageDisposition Consider(SessionMessageAction? action, SessionMessageAction mask)
    {
        if (action is not SessionMessageAction parsed)
            return SessionMessageDisposition.Unparseable;

        // Before the mask, and that is the whole point of this line.
        if ((parsed & SessionMessageAction.Terminate) != 0)
            return SessionMessageDisposition.Terminated;

        return SessionMessageEnvelope.Matches(parsed, mask)
            ? SessionMessageDisposition.Matched
            : SessionMessageDisposition.Ignored;
    }
}

/// <summary>What one matched RESULT meant to a wait for an acknowledgement.</summary>
public enum AckDisposition
{
    /// <summary>The request it acknowledges is the one being waited for.</summary>
    Acked,

    /// <summary>It acknowledges some other request, so the wait goes round again.</summary>
    WrongRequest,

    /// <summary>A stop was asked for, and taking it here consumes it.</summary>
    Cancelled,
}

/// <summary>
/// PP213: waiting for the acknowledgement of a request, and the loop that does not end.
///
/// This sits on <see cref="SessionMessageWait"/> asking only for RESULT, and then asks one more
/// question of what comes back: is this the acknowledgement of MY request. Three answers, and the
/// interesting fact about all three is <see cref="Clearing"/>.
///
/// IT IS EMPTY. In the core every path through this wait goes through session_message_free, which
/// nulls the message's pointer to its notification and frees the message - it does not touch the
/// queue. On the way out that costs nothing. On the one path that loops it costs everything:
///
/// an acknowledgement for a request id that is not the one being waited for is rejected, and the
/// notification carrying it stays exactly where it was. The next pass scans the queue from the
/// front, finds that same notification, parses it, sees RESULT, matches the mask, and arrives back
/// here with the same wrong id. Nothing sleeps in between, because the wait underneath only sleeps
/// when there is nothing NEW past its cursor - and this is not new, it is the same one. One ack
/// for an unexpected request spins the holepunch thread until something else tears the session
/// down.
///
/// Reproduced, not fixed. <see cref="WouldSpinOn"/> is that sentence as a predicate.
/// </summary>
public static class SessionMessageAckWait
{
    /// <summary>The only action this wait looks for.</summary>
    public const SessionMessageAction Mask = SessionMessageAction.Result;

    /// <summary>
    /// The dispositions that clear the notification they were reached through. There are none,
    /// which is the defect above rather than an omission here.
    /// </summary>
    public static IReadOnlySet<AckDisposition> Clearing { get; } = new HashSet<AckDisposition>();

    /// <summary>Whether this answer takes the notification off the queue. It never does.</summary>
    public static bool Clears(AckDisposition disposition) => Clearing.Contains(disposition);

    /// <summary>
    /// What one acknowledgement means, asked in the core's order: the stop first, then the id.
    /// </summary>
    /// <param name="requestId">The request this acknowledgement names.</param>
    /// <param name="expectedRequestId">The request being waited for.</param>
    /// <param name="cancelRequested">
    /// The stop flag, read here and - in the core - set back to false by the reading. The caller
    /// owns that reset, the same way it does in <see cref="NotificationWait.Wake"/>.
    /// </param>
    public static AckDisposition Consider(int requestId, int expectedRequestId, bool cancelRequested)
    {
        if (cancelRequested)
            return AckDisposition.Cancelled;

        return requestId == expectedRequestId ? AckDisposition.Acked : AckDisposition.WrongRequest;
    }

    /// <summary>
    /// Whether an acknowledgement already on the queue puts this wait into the loop that does not
    /// end: it is rejected, nothing clears it, and it is therefore the next thing found.
    /// </summary>
    public static bool WouldSpinOn(int requestId, int expectedRequestId)
        => Consider(requestId, expectedRequestId, cancelRequested: false) == AckDisposition.WrongRequest
            && !Clears(AckDisposition.WrongRequest);
}

/// <summary>
/// PP213: the two waits where the core writes them.
/// </summary>
public static class SessionMessageWaitSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SessionMessageSource.Locate();

    /// <summary>Whether TERMINATE is still tested before the mask rather than after it.</summary>
    public static bool TerminateIsStillTestedBeforeTheMask(string core)
    {
        string body = MessageWaitBody(core);

        int terminate = body.IndexOf(
            "msg->action & SESSION_MESSAGE_ACTION_TERMINATE", StringComparison.Ordinal);
        int mask = body.IndexOf("!(msg->action & types)", StringComparison.Ordinal);

        return terminate >= 0 && mask > terminate;
    }

    /// <summary>Whether the mask miss is still the one path that clears.</summary>
    public static bool OnlyTheMaskMissStillClears(string core)
    {
        string body = MessageWaitBody(core);
        if (body.Length == 0)
            return false;

        // One call, and it sits after the mask test rather than in the terminate or parse branch.
        int mask = body.IndexOf("!(msg->action & types)", StringComparison.Ordinal);
        int clear = body.IndexOf("clear_notification(session, notif)", StringComparison.Ordinal);

        return mask >= 0
            && clear > mask
            && body.IndexOf("clear_notification(", clear + 1, StringComparison.Ordinal) < 0;
    }

    /// <summary>Whether the ack wait still clears nothing on any path.</summary>
    public static bool TheAckWaitStillClearsNothing(string core)
    {
        string body = AckWaitBody(core);
        return body.Length > 0 && !body.Contains("clear_notification", StringComparison.Ordinal);
    }

    /// <summary>And whether the wrong request id still loops rather than returning.</summary>
    public static bool AWrongRequestIdStillContinues(string core)
    {
        string body = AckWaitBody(core);

        int test = body.IndexOf("msg->req_id != req_id", StringComparison.Ordinal);
        if (test < 0)
            return false;

        int carryOn = body.IndexOf("continue;", test, StringComparison.Ordinal);
        return carryOn > test;
    }

    /// <summary>
    /// Whether freeing a message still leaves the queue alone - which is what makes the loop above
    /// permanent rather than self-correcting.
    /// </summary>
    public static bool FreeingAMessageStillLeavesTheQueueAlone(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // LAST, not first: holepunch.c forward-declares every static function at the top, and the
        // declaration is a prefix of the definition - so searching forward lands on the semicolon
        // three thousand lines above the body this asks about.
        int start = core.LastIndexOf(
            "static ChiakiErrorCode session_message_free(SessionMessage *message)",
            StringComparison.Ordinal);
        if (start < 0)
            return false;

        int end = core.IndexOf("\n}", start, StringComparison.Ordinal);
        string body = end < 0 ? core[start..] : core[start..end];

        return body.Contains("message->notification = NULL;", StringComparison.Ordinal)
            && !body.Contains("clear_notification", StringComparison.Ordinal);
    }

    private static string MessageWaitBody(string core)
        => Slice(
            core,
            "uint32_t notif_query = NOTIFICATION_TYPE_SESSION_MESSAGE_CREATED;",
            "static ChiakiErrorCode wait_for_session_message_ack(");

    private static string AckWaitBody(string core)
        => Slice(
            core,
            "uint32_t msg_query = SESSION_MESSAGE_ACTION_RESULT;",
            "static ChiakiErrorCode session_message_parse(");

    /// <summary>
    /// One function's body, cut at the two lines that bound it. Sliced rather than searched whole
    /// because half of these ask what is NOT in a function, and holepunch.c is six thousand lines.
    /// </summary>
    private static string Slice(string core, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(core);

        int start = core.IndexOf(from, StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf(to, start, StringComparison.Ordinal);
        return end < 0 ? core[start..] : core[start..end];
    }
}
