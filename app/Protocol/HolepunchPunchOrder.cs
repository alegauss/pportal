using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP560, under PP33: PP547's eleven steps, checked against the C rather than declared.
///
/// PP547 wrote the punch's order down and PP550 put running pieces behind it. Nothing read
/// holepunch.c to see whether that order is the one the C runs - so every task built on the
/// sequence rested on a list that had been believed rather than verified. This tree does not
/// usually let a claim about the C stand that way: PP460 checks session.c's nine, PP340's seam
/// checks their call sites, and the punch had nothing.
///
/// IT IS RIGHT, WHICH IS WHY THIS IS AN ASSERTION AND NOT A FIX. Reading the C's
/// <c>chiaki_holepunch_session_punch_hole</c> against PP547's list found them the same, step for
/// step. What was missing was anything that would notice if they stopped being.
///
/// ONE STEP HAS NO ANCHOR AND SAYS SO. Preconditions is a guard on session state rather than a
/// call. The other ten are calls in the C's text and are held by name and by position, never by a
/// line number - this file moves.
/// </summary>
public static class HolepunchPunchOrder
{
    /// <summary>Where the punch is.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>holepunch.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The definition, which bounds the search - the anchors are not unique file-wide.</summary>
    public const string Definition =
        "CHIAKI_EXPORT ChiakiErrorCode chiaki_holepunch_session_punch_hole(";

    /// <summary>
    /// The punch's body: from its definition to the next exported function.
    ///
    /// Bounded on purpose. <c>wait_for_session_message</c> is called from more than one place, so a
    /// file-wide search would find an order that no single function runs.
    /// </summary>
    public static string BodyIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);
        int at = code.IndexOf(Definition, StringComparison.Ordinal);
        if (at < 0)
            return "";

        int next = code.IndexOf("\nCHIAKI_EXPORT", at + Definition.Length, StringComparison.Ordinal);
        return next < 0 ? code[at..] : code[at..next];
    }

    /// <summary>
    /// Each step and what it is in the C, in PP547's order - null where the step is not a call.
    /// </summary>
    public static IReadOnlyList<(HolepunchPunchStep Step, string? Anchor)> Anchors { get; } =
    [
        (HolepunchPunchStep.Preconditions, null),

        (HolepunchPunchStep.WaitForOffer,
            "wait_for_session_message(session, &console_offer_msg, SESSION_MESSAGE_ACTION_OFFER"),

        (HolepunchPunchStep.AckOffer, "http_send_session_message(session, &ack_msg, true)"),

        (HolepunchPunchStep.SendOffer, "send_offer(session)"),

        (HolepunchPunchStep.WaitForOfferAck, "wait_for_session_message_ack("),

        (HolepunchPunchStep.ChooseCandidate, "check_candidates(session, session->local_candidates"),

        (HolepunchPunchStep.SendAccept,
            "send_accept(session, session->local_req_id, &selected_candidate)"),

        (HolepunchPunchStep.WaitForAccept,
            "wait_for_session_message(session, &msg, SESSION_MESSAGE_ACTION_ACCEPT"),

        (HolepunchPunchStep.AckAccept, "http_send_session_message(session, &accept_ack_msg, true)"),

        (HolepunchPunchStep.MarkEstablished, "SESSION_STATE_DATA_ESTABLISHED"),

        (HolepunchPunchStep.ReceiveRequestSendResponse, "receive_request_send_response_ps("),
    ];

    /// <summary>The ten that are calls in the C's text, in order.</summary>
    public static IReadOnlyList<string> Calls { get; } =
        [.. Anchors.Select(one => one.Anchor).OfType<string>()];

    /// <summary>Whether the C still makes those calls, in that order, inside the punch.</summary>
    public static bool TheOrderIsStillTheCs(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string body = BodyIn(source);
        return body.Length > 0 && CCall.InOrder(body, [.. Calls]);
    }

    /// <summary>
    /// And PP547's list is the same list, by position.
    ///
    /// Two statements compared rather than one repeated: the sequence declares an order and this
    /// reads one, and a step added to either without the other fails here.
    /// </summary>
    public static bool TheSequenceRunsTheSameSteps()
        => HolepunchPunch.ExecutionOrder.SequenceEqual(Anchors.Select(one => one.Step));
}
