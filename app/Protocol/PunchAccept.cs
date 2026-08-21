namespace ChiakiNg.Protocol;

/// <summary>How the wait for the console's ACCEPT ended.</summary>
public enum AcceptWaitOutcome
{
    /// <summary>The message arrived.</summary>
    Accepted,

    /// <summary>Nothing arrived inside the window.</summary>
    TimedOut,

    /// <summary>The caller asked to stop while waiting.</summary>
    Canceled,

    /// <summary>Anything else - the catch-all branch.</summary>
    Failed,
}

/// <summary>
/// Where a cancellation was noticed, which decides what has to be released.
/// </summary>
public enum PunchCancelPoint
{
    /// <summary>Before the wait, when no message is held.</summary>
    BeforeTheWait,

    /// <summary>After it, when one is - and freeing it is the caller's job.</summary>
    AfterTheWait,
}

/// <summary>
/// PP242: the accept half of the punch - one wait, three exits, and a name wider than the question.
///
/// THE WAIT ASKS FOR ONE ACTION. Two of its three failure branches say so: the timeout names ACCEPT
/// and the cancellation names nothing at all. The third - the catch-all, the one that fires for the
/// failures nobody enumerated - says "Failed to wait for ACCEPT or OFFER". A reader hitting that
/// line is told the console failed to send either of two things when only one was ever waited on,
/// and it is the branch least likely to have a second clue beside it. Different in kind from the
/// misnamings <see cref="MisnamedLogs"/> collects: those name another function, this names another
/// question.
///
/// AND TWO CANCELLATIONS THAT READ IDENTICALLY. The check before the wait and the check after the
/// acknowledgement print the same sentence, and jump to different cleanup. The difference is a
/// message that exists in one case and not the other, so the log cannot tell you which release path
/// ran - correct code whose only witness is ambiguous.
///
/// The acknowledgement is <see cref="OfferAck.Message"/> for the third time: PP231 built it for the
/// automatic path, PP240 for the offer, this for the accept.
/// </summary>
public static class PunchAccept
{
    /// <summary>The one action the wait asks for.</summary>
    public const SessionMessageAction WaitsFor = SessionMessageAction.Accept;

    /// <summary>What a reader is shown when the wait ends each way.</summary>
    public static string MessageFor(AcceptWaitOutcome outcome) => outcome switch
    {
        AcceptWaitOutcome.Accepted => "",
        AcceptWaitOutcome.TimedOut =>
            "Timed out waiting for ACCEPT holepunch session message.",
        AcceptWaitOutcome.Canceled => CanceledMessage,
        _ => "Failed to wait for ACCEPT or OFFER holepunch session message.",
    };

    /// <summary>
    /// Whether the message for this outcome names a wait wider than the one that was made.
    ///
    /// Only the catch-all does, which is why this is a question about the outcome rather than a
    /// flag on the function.
    /// </summary>
    public static bool NamesAWiderWait(AcceptWaitOutcome outcome)
        => MessageFor(outcome).Contains(" or OFFER", StringComparison.Ordinal);

    /// <summary>
    /// The sentence both cancellations print - the same one, from two places whose cleanup differs.
    /// </summary>
    public const string CanceledMessage = "canceled";

    /// <summary>Whether a cancellation noticed here is holding a message that must be released.</summary>
    public static bool HoldsAMessage(PunchCancelPoint point)
        => point == PunchCancelPoint.AfterTheWait;

    /// <summary>
    /// And what the log says at each - identical, which is the point of asking.
    /// </summary>
    public static string CancelMessageAt(PunchCancelPoint point)
    {
        _ = point;
        return CanceledMessage;
    }

    /// <summary>
    /// The acknowledgement of the accept, which is the offer's message with a different id in it.
    /// </summary>
    public static string Acknowledgement(int acceptRequestId) => OfferAck.Message(acceptRequestId);

    /// <summary>
    /// The size of both the session's address field and a candidate's.
    ///
    /// The copy that fills the first from the second is sized from the DESTINATION. That is safe
    /// only because these are equal, and they are equal by both being INET6_ADDRSTRLEN rather than
    /// by anything checking - so the equality is asserted rather than assumed.
    /// </summary>
    public const int AddressLength = 46;

    /// <summary>
    /// The chosen candidate's address, as the session ends up holding it: a whole-field copy, so
    /// whatever followed the terminator in the candidate comes along.
    /// </summary>
    public static byte[] Adopt(ReadOnlySpan<byte> candidateAddress)
    {
        byte[] held = new byte[AddressLength];

        // Sized from the destination, exactly as the core sizes it. Equal fields, so this reads no
        // further than the source has - and a shorter source is what would make it a defect.
        candidateAddress[..AddressLength].CopyTo(held);
        return held;
    }
}

/// <summary>
/// PP242: the accept half where the core writes it.
/// </summary>
public static class PunchAcceptSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the wait still asks for one action.</summary>
    public static bool TheWaitStillAsksForAccept(string core)
        => Body(core).Contains(
            "wait_for_session_message(session, &msg, SESSION_MESSAGE_ACTION_ACCEPT,",
            StringComparison.Ordinal);

    /// <summary>
    /// And whether the catch-all still names two, while the branch above it names one.
    /// </summary>
    public static bool TheCatchAllStillNamesAWiderWait(string core)
    {
        string body = Body(core);

        int timedOut = body.IndexOf(
            PunchAccept.MessageFor(AcceptWaitOutcome.TimedOut), StringComparison.Ordinal);
        int wider = body.IndexOf(
            PunchAccept.MessageFor(AcceptWaitOutcome.Failed), StringComparison.Ordinal);

        return timedOut >= 0 && wider > timedOut;
    }

    /// <summary>Whether the acknowledgement is still that same short message.</summary>
    public static bool TheAcknowledgementIsStillTheSameShape(string core)
        => Body(core).Contains(
            """
            SessionMessage accept_ack_msg = {
                    .action = SESSION_MESSAGE_ACTION_RESULT,
                    .req_id = msg->req_id,
                    .error = 0,
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    /// <summary>
    /// Whether the two cancellations still print the same sentence and leave by different doors.
    /// </summary>
    public static bool TheTwoCancellationsStillReadAlike(string core)
    {
        string body = Body(core);

        // Two of them, and only one label between them differs.
        int said = body.Split(
            "chiaki_holepunch_session_punch_holes: canceled", StringSplitOptions.None).Length - 1;

        return said >= 2
            && body.Contains("err = CHIAKI_ERR_CANCELED;\n        goto cleanup;", StringComparison.Ordinal)
            && body.Contains("err = CHIAKI_ERR_CANCELED;\n        goto cleanup_msg;", StringComparison.Ordinal);
    }

    /// <summary>Whether the address is still copied by the destination's size.</summary>
    public static bool TheAddressIsStillCopiedByTheDestinationsSize(string core)
        => Body(core).Contains(
            "memcpy(session->ps_ip, selected_candidate.addr, sizeof(session->ps_ip));",
            StringComparison.Ordinal);

    /// <summary>
    /// The stretch from the cancel check before the wait to the state flags after the copy.
    ///
    /// The start is anchored on the increment rather than the lock, because the lock appears at
    /// every one of the five cancel checks and would match the first.
    /// </summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int increment = text.IndexOf(
            "    session->local_req_id++;\n\n    SessionMessage *msg = NULL;", StringComparison.Ordinal);
        if (increment < 0)
            return "";

        // Backwards to the cancel check that precedes it, so both cancellations are inside.
        int start = text.LastIndexOf(
            "    chiaki_mutex_lock(&session->stop_mutex);", increment, StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf(
            "    chiaki_mutex_lock(&session->state_mutex);", increment, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
