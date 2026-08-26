using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which hole is being opened. The two have different preconditions.</summary>
public enum PunchPort
{
    /// <summary>The control port, which is opened first.</summary>
    Control,

    /// <summary>And the data port, which needs the control one already established.</summary>
    Data,
}

/// <summary>Why a hole cannot be opened yet, or that it can.</summary>
public enum PunchReadiness
{
    /// <summary>Nothing is missing.</summary>
    Ready,

    /// <summary>The control port was asked for and customData1 has not arrived.</summary>
    NoCustomData,

    /// <summary>The data port was asked for and the control port is not established.</summary>
    ControlNotOpen,
}

/// <summary>
/// PP240: opening a hole - the entry, and the handshake where the ordering lives.
///
/// TWO DIFFERENT PRECONDITIONS. The control port refuses unless customData1 has arrived; the data
/// port refuses unless the control port is already established. Neither is a check on the other's
/// condition, so one shared guard would let one of the two start too early.
///
/// AND A FLAG SET BEFORE THE ANSWER. The state marking the offer received is set BEFORE the offer
/// is acknowledged, and that state is exactly what opens PP231's automatic window in the websocket
/// thread. So the window opens while this path is still handling the first offer - which is what
/// makes a SECOND offer arriving in the gap somebody's responsibility. Set after the acknowledgement
/// instead, an offer landing in between would be neither waited for nor answered, and nothing would
/// report it.
///
/// The acknowledgement is the same message the automatic path sends, so <see cref="OfferAck"/>
/// builds both.
/// </summary>
public static class PunchOpening
{
    /// <summary>Whether a hole can be opened, and what is missing when it cannot.</summary>
    public static PunchReadiness Readiness(PunchPort port, HolepunchSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (port == PunchPort.Control)
        {
            return state.Has(SessionStateFlags.CustomData1Received)
                ? PunchReadiness.Ready
                : PunchReadiness.NoCustomData;
        }

        return state.Has(SessionStateFlags.CtrlEstablished)
            ? PunchReadiness.Ready
            : PunchReadiness.ControlNotOpen;
    }

    /// <summary>Which state an offer for this port sets when it arrives.</summary>
    public static SessionStateFlags OfferReceivedFor(PunchPort port)
        => port == PunchPort.Control
            ? SessionStateFlags.CtrlOfferReceived
            : SessionStateFlags.DataOfferReceived;

    /// <summary>
    /// The acknowledgement this path sends by hand, which is the automatic one's message.
    /// </summary>
    public static string Acknowledgement(int offerRequestId) => OfferAck.Message(offerRequestId);

    /// <summary>
    /// The ordering, as a question that can be asked.
    ///
    /// Answers whether a second offer arriving right now would be answered by the websocket thread,
    /// given the state as it stands. The point of the rule is that this becomes true BEFORE the
    /// first offer is acknowledged rather than after.
    /// </summary>
    public static bool ASecondOfferWouldBeAnswered(HolepunchSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.ShouldAckOffers;
    }
}

/// <summary>
/// PP240: the opening where the core writes it.
/// </summary>
public static class PunchOpeningSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the two ports still have two different preconditions.</summary>
    public static bool TheTwoPortsStillDifferInWhatTheyNeed(string core)
    {
        string body = Body(core);

        return body.Contains(
                "port_type == CHIAKI_HOLEPUNCH_PORT_TYPE_CTRL\n        && !(session->state & SESSION_STATE_CUSTOMDATA1_RECEIVED)",
                StringComparison.Ordinal)
            && body.Contains(
                "port_type == CHIAKI_HOLEPUNCH_PORT_TYPE_DATA\n        && !(session->state & SESSION_STATE_CTRL_ESTABLISHED)",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the console's identity still comes out of the offer.</summary>
    public static bool TheConsolesIdentityStillComesFromTheOffer(string core)
    {
        string body = Body(core);

        return body.Contains(
                "memcpy(session->hashed_id_console, console_req->local_hashed_id", StringComparison.Ordinal)
            && body.Contains("session->sid_console = console_req->sid;", StringComparison.Ordinal);
    }

    /// <summary>
    /// THE ORDERING. Whether the offer-received flag is still set before the acknowledgement is
    /// sent - which is what gives a second offer an owner.
    /// </summary>
    public static bool TheFlagIsStillSetBeforeTheAnswer(string core)
    {
        string body = Body(core);

        int flagged = body.IndexOf(
            "session->state |= SESSION_STATE_CTRL_OFFER_RECEIVED;", StringComparison.Ordinal);
        int acknowledged = body.IndexOf(
            "err = http_send_session_message(session, &ack_msg, true);", StringComparison.Ordinal);

        return flagged >= 0 && acknowledged > flagged;
    }

    /// <summary>And whether that acknowledgement is still the automatic one's message.</summary>
    public static bool TheAnswerIsStillTheSameMessage(string core)
    {
        string body = Body(core);

        return body.Contains(".action = SESSION_MESSAGE_ACTION_RESULT,", StringComparison.Ordinal)
            && body.Contains(".req_id = console_offer_msg->req_id,", StringComparison.Ordinal)
            && body.Contains(".error = 0,", StringComparison.Ordinal);
    }

    /// <summary>The opening of punch_hole, as far as the handshake this task covers.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int start = core.IndexOf(
            "CHIAKI_EXPORT ChiakiErrorCode chiaki_holepunch_session_punch_hole(", StringComparison.Ordinal);
        if (start < 0)
            return "";

        // Only as far as the acknowledgement, which is where this slice ends - the candidate
        // exchange after it is a later one and its text would make these searches ambiguous.
        // PP388: without the semicolon. This one slices RAW text, so it cannot move to compacted
        // marks - a compacted position does not address the string being cut. Dropping the
        // terminator is the whole of what is available here, and it is strictly more tolerant: the
        // boundary is where that lock is taken, however the line ends.
        int end = core.IndexOf("chiaki_mutex_lock(&session->stop_mutex)", start, StringComparison.Ordinal);
        return end < 0 ? core[start..] : core[start..end];
    }
}
