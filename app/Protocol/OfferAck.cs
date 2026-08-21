using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the websocket thread does with a notification inside the auto-ACK window.</summary>
public enum OfferAckOutcome
{
    /// <summary>An OFFER nobody is waiting for: acknowledge it, then queue it as usual.</summary>
    Acknowledge,

    /// <summary>Anything else that parsed: no acknowledgement, and queued as usual.</summary>
    QueueOnly,

    /// <summary>
    /// The payload would not parse. The notification is DROPPED - the enqueue is below this branch
    /// in the loop, and the branch leaves.
    /// </summary>
    Drop,
}

/// <summary>
/// PP231: the acknowledgement the websocket thread sends on its own, and the notification it loses.
///
/// PP207 ported the WINDOW - <see cref="HolepunchSessionState.ShouldAckOffers"/>, which is on
/// exactly when nothing is explicitly waiting for an offer - and PP191 the short message that
/// carries the reply. This is what happens between them.
///
/// The acknowledgement is narrow. Only an OFFER earns one, and the reply is a RESULT carrying the
/// offer's own request id, error zero, no connection request, in the short form. Anything else that
/// parses is left alone and still queued, which is right: the window is about offers nobody is
/// waiting for and not about traffic in general.
///
/// AND ONE BRANCH LOSES THE NOTIFICATION. A payload that will not parse is logged and the loop
/// continues - and the enqueue sits BELOW that, so the notification never reaches the queue, along
/// with everything already allocated for it.
///
/// The asymmetry is what makes it worth stating. Outside the window that same malformed message is
/// never parsed here, so it IS queued - and then PP213's wait finds it, fails on it, and leaves it
/// there for every later wait to fail on again. One message, two opposite fates, decided by which
/// port has been established. Inside the window it is lost; outside it is kept forever. Reproduced,
/// not fixed.
/// </summary>
public static class OfferAck
{
    /// <summary>The error an automatic acknowledgement carries. Always this one.</summary>
    public const int NoError = 0;

    /// <summary>
    /// What to do with one notification, given the window and what its payload parsed to.
    /// </summary>
    /// <param name="inWindow">
    /// <see cref="HolepunchSessionState.ShouldAckOffers"/>. False means this whole branch is
    /// skipped, so the notification is queued whatever its payload turns out to be.
    /// </param>
    /// <param name="isSessionMessage">
    /// Whether the notification is a session message. The branch tests it as well as the window -
    /// a member or a custom-data notification never had a payload to parse.
    /// </param>
    /// <param name="action">What the payload parsed to, or null where it would not parse.</param>
    public static OfferAckOutcome Consider(
        bool inWindow, bool isSessionMessage, SessionMessageAction? action)
    {
        // Outside the branch nothing is parsed, so nothing can be dropped for failing to.
        if (!inWindow || !isSessionMessage)
            return OfferAckOutcome.QueueOnly;

        if (action is not SessionMessageAction parsed)
            return OfferAckOutcome.Drop;

        return parsed == SessionMessageAction.Offer
            ? OfferAckOutcome.Acknowledge
            : OfferAckOutcome.QueueOnly;
    }

    /// <summary>Whether an outcome still puts the notification on the queue.</summary>
    public static bool Queues(OfferAckOutcome outcome) => outcome != OfferAckOutcome.Drop;

    /// <summary>
    /// The acknowledgement itself: a RESULT with the offer's own request id, in the short form.
    ///
    /// The request id is the OFFER's, not one of this side's. An acknowledgement carrying a new id
    /// answers a question nobody asked, and the console is waiting on the one it sent.
    /// </summary>
    public static string Message(int offerRequestId)
        => SessionMessageWriter.ShortMessage(SessionMessageAction.Result, offerRequestId, NoError);
}

/// <summary>
/// PP231: the auto-ACK where the core writes it.
/// </summary>
public static class OfferAckSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the acknowledgement is still only for an OFFER.</summary>
    public static bool OnlyAnOfferIsStillAcknowledged(string core)
    {
        string body = Body(core);
        return body.Contains("if (msg->action == SESSION_MESSAGE_ACTION_OFFER)", StringComparison.Ordinal);
    }

    /// <summary>Whether the reply still carries the offer's own request id, short, with no error.</summary>
    public static bool TheReplyIsStillTheOffersOwnId(string core)
    {
        string body = Body(core);

        return body.Contains(".action = SESSION_MESSAGE_ACTION_RESULT,", StringComparison.Ordinal)
            && body.Contains(".req_id = msg->req_id,", StringComparison.Ordinal)
            && body.Contains(".error = 0,", StringComparison.Ordinal)
            && body.Contains("http_send_session_message(session, &ack_msg, true)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a payload that will not parse still leaves before the enqueue. True means the
    /// notification is still lost, which is what this asserts rather than a fix.
    /// </summary>
    public static bool AParseFailureStillSkipsTheEnqueue(string core)
    {
        string body = Body(core);

        int failed = body.IndexOf(
            "Failed to parse session message for ACKing.", StringComparison.Ordinal);
        if (failed < 0)
            return false;

        int leaves = body.IndexOf("continue;", failed, StringComparison.Ordinal);
        int queued = body.IndexOf(
            "enqueueNq(session->ws_notification_queue, notif)", StringComparison.Ordinal);

        return leaves > failed && queued > leaves;
    }

    /// <summary>And whether the window is still the state mask PP207 read.</summary>
    public static bool TheWindowIsStillTheStateMask(string core)
    {
        string body = Body(core);

        return body.Contains("bool should_ack_offers =", StringComparison.Ordinal)
            && body.Contains("should_ack_offers && notif->type == NOTIFICATION_TYPE_SESSION_MESSAGE_CREATED",
                StringComparison.Ordinal);
    }

    /// <summary>The frame loop's body, which is where all of this lives.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int start = core.IndexOf(
            "uint64_t timeout = WEBSOCKET_PING_INTERVAL_SEC * 1000;", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf("cleanup_json:", start, StringComparison.Ordinal);
        return end < 0 ? core[start..] : core[start..end];
    }
}
