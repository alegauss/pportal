using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What a step of the negotiation would tell the caller, once one is listening.</summary>
public enum NegotiationOutcome
{
    /// <summary>It worked.</summary>
    Ok,

    /// <summary>It did not, and the caller was told.</summary>
    Failed,

    /// <summary>
    /// It did not, and NOBODY IS LISTENING - the call's answer is discarded at the call site, so
    /// this outcome is indistinguishable from <see cref="Ok"/> to everything downstream.
    /// </summary>
    FailedUnheard,
}

/// <summary>
/// PP241: the part of the punch where two answers are thrown away.
///
/// send_offer is invoked bare, and the very next statement waits thirty seconds for an
/// acknowledgement of the message it may not have sent. So a failure to send is reported as "timed
/// out waiting for ACK of our connection offer" - a sentence about the console being slow, for a
/// request that never left.
///
/// http_check_session is invoked bare too, and that is PP233's function: the one whose tokener
/// branch returns success without reading anything. A call that cannot always tell you it failed,
/// called in a way that could not hear it if it did.
///
/// THE REQUEST IDS LOOK WRONG AND ARE NOT. The offer takes the current id and increments; the
/// accept is sent with the value AFTER that increment, and only then is it incremented again. Read
/// quickly it looks like the accept reuses the offer's. It does not - one, then two, then three -
/// and it is written down here because checking took longer than assuming would have.
/// </summary>
public static class PunchNegotiation
{
    /// <summary>The first id a session hands out.</summary>
    public const int FirstRequestId = 1;

    /// <summary>
    /// The ids one round of the negotiation uses, from the id a session is holding.
    /// </summary>
    /// <returns>The offer's, the accept's, and what the session holds afterwards.</returns>
    public static (int Offer, int Accept, int Next) RequestIds(int held)
    {
        // Taken, then incremented - so the offer gets what was held.
        int offer = held;
        int afterOffer = held + 1;

        // And the accept is sent with THAT, before the second increment.
        return (offer, afterOffer, afterOffer + 1);
    }

    /// <summary>
    /// What the caller of the offer learns, which is nothing.
    ///
    /// A send that failed and a send that worked are the same to everything after it; the
    /// difference shows up as a timeout on the wait that follows, which names the wrong thing.
    /// </summary>
    public static NegotiationOutcome OfferOutcome(bool sent)
        => sent ? NegotiationOutcome.Ok : NegotiationOutcome.FailedUnheard;

    /// <summary>
    /// And what the check tells the caller, which is also nothing - twice over.
    ///
    /// Its answer is discarded here, and PP233 measured that the answer itself is unreliable: a
    /// tokener it cannot allocate is reported as success. Both are true at once, which is why this
    /// takes the two separately rather than one "worked" flag.
    /// </summary>
    public static NegotiationOutcome CheckOutcome(SessionCheckOutcome outcome)
        => SessionCheck.IsFailure(outcome) ? NegotiationOutcome.FailedUnheard : NegotiationOutcome.Ok;

    /// <summary>
    /// What the wait after an unsent offer reports.
    ///
    /// The timeout, because nothing is coming - and the message names the acknowledgement rather
    /// than the send, which is the one thing a reader would want it to name.
    /// </summary>
    public const string WhatAnUnsentOfferLooksLike =
        "Timed out waiting for ACK of our connection offer.";
}

/// <summary>
/// PP241: the negotiation where the core writes it.
/// </summary>
public static class PunchNegotiationSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the offer is still sent without its answer being read.</summary>
    public static bool TheOfferIsStillSentUnchecked(string core)
        => Body(core).Contains("\n    send_offer(session);\n", StringComparison.Ordinal);

    /// <summary>And whether the check is still called the same way.</summary>
    public static bool TheCheckIsStillCalledUnchecked(string core)
        => Body(core).Contains("\n    http_check_session(session, true);\n", StringComparison.Ordinal);

    /// <summary>Whether an unsent offer still surfaces as that message.</summary>
    public static bool AnUnsentOfferStillLooksLikeATimeout(string core)
    {
        string body = Body(core);

        // PP388: three marks in one space. The third is a message rather than a call, which is
        // exactly why the anchor reader exists beside the call one.
        string compact = CCall.Compact(body);

        int sent = CCall.At(compact, "send_offer(session)");
        int waits = CCall.Mark(compact, "wait_for_session_message_ack(");
        int says = CCall.Mark(compact, PunchNegotiation.WhatAnUnsentOfferLooksLike);

        return sent >= 0 && waits > sent && says > waits;
    }

    /// <summary>Whether the ids are still taken and incremented in that order.</summary>
    public static bool TheIdsAreStillTakenThenIncremented(string core)
    {
        string body = Body(core);

        string compact = CCall.Compact(body); // PP388

        int taken = CCall.Mark(compact, "const int our_offer_req_id = session->local_req_id;");
        if (taken < 0)
            return false;

        int bumped = CCall.Mark(compact, "session->local_req_id++;", taken + 1);
        int accepted = CCall.Mark(compact, "send_accept(session, session->local_req_id,");

        return bumped > taken && accepted > bumped;
    }

    /// <summary>The stretch between the handshake and the accept.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.IndexOf("    // Send our own OFFER", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("Failed to send ACCEPT message.", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
