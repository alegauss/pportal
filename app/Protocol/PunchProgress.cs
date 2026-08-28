using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One CHIAKI_EVENT_HOLEPUNCH, by the only field it carries.</summary>
public enum PunchProgressEvent
{
    /// <summary>finished = false, raised just before the punch.</summary>
    Started,

    /// <summary>finished = true, raised after the data socket has been taken.</summary>
    Finished,
}

/// <summary>The connect state the Qt client puts the screen in for each event.</summary>
public enum PsnConnectState
{
    /// <summary>Whatever it was before the punch began.</summary>
    Unchanged,

    /// <summary>PsnConnectState::DataConnectionStart.</summary>
    DataConnectionStart,

    /// <summary>PsnConnectState::DataConnectionFinished.</summary>
    DataConnectionFinished,
}

/// <summary>What one run of the punch block told the user.</summary>
/// <param name="Events">The events raised, in order. Never more than two, often fewer.</param>
/// <param name="EndState">Where the client's connect state is left.</param>
/// <param name="DataSocketTaken">Whether the data socket was fetched before the finish.</param>
public readonly record struct PunchProgressOutcome(
    IReadOnlyList<PunchProgressEvent> Events, PsnConnectState EndState, bool DataSocketTaken);

/// <summary>
/// PP503, under PP340: the two events the hole punch raises, which are not a pair.
///
/// The punch is the one step of the PSN flow a user can watch happening, and session.c narrates it
/// with two CHIAKI_EVENT_HOLEPUNCHs distinguished only by a bool. Reading them as start/finish and
/// reproducing them as such is the mistake.
///
/// THE START IS UNCONDITIONAL AND THE FINISH IS ON THE SUCCESS PATH ONLY. The start goes out
/// immediately before chiaki_holepunch_session_punch_hole. If that returns an error the block takes
/// QUIT and sends nothing, so the last thing the client heard was "started". A failed OFFER, one
/// step earlier, raises NEITHER - the start sits below it, so that failure shows no punch at all
/// rather than a stalled one. Three outcomes, three different things on screen.
///
/// THE CLIENT TURNS THEM INTO A STATE AND NOTHING RESETS IT. qmlbackend maps the two onto
/// DataConnectionStart and DataConnectionFinished. A failed punch therefore leaves the screen in
/// DataConnectionStart, and what clears that is the session quitting - not a finish event, because
/// there is not one to send.
///
/// SO THE MANAGED SHAPE THAT LOOKS RIGHT IS WRONG. Raising the finish in a finally, or wrapping the
/// punch in a using, balances the pair and reports a data connection that never opened. This
/// reproduces the asymmetry and <see cref="PunchProgress.Run"/> has no finally in it, deliberately.
///
/// TWO SMALLER FACTS. The data socket is fetched BETWEEN the punch and the finish, so "finished"
/// means punched AND socket taken. And a wait on the ctrl state follows the finish, so the event is
/// not the last thing in that block that can fail.
/// </summary>
public static class PunchProgress
{
    /// <summary>What the client does with one event.</summary>
    public static PsnConnectState StateFor(PunchProgressEvent raised) => raised switch
    {
        PunchProgressEvent.Started => PsnConnectState.DataConnectionStart,
        PunchProgressEvent.Finished => PsnConnectState.DataConnectionFinished,
        _ => PsnConnectState.Unchanged,
    };

    /// <summary>
    /// Runs the punch block's narration.
    /// </summary>
    /// <param name="offerSucceeds">Whether holepunch_session_create_offer returned success.</param>
    /// <param name="punchSucceeds">Whether the punch returned success.</param>
    public static PunchProgressOutcome Run(bool offerSucceeds, bool punchSucceeds)
    {
        var events = new List<PunchProgressEvent>(2);

        // Below the offer's guard in the C, which is why a failed offer narrates nothing.
        if (!offerSucceeds)
            return new PunchProgressOutcome(events, PsnConnectState.Unchanged, false);

        events.Add(PunchProgressEvent.Started);

        // No finally, and that is the point: the C's QUIT leaves the start standing alone.
        if (!punchSucceeds)
        {
            return new PunchProgressOutcome(
                events, StateFor(PunchProgressEvent.Started), DataSocketTaken: false);
        }

        // Between the two, so "finished" means the socket is in hand.
        events.Add(PunchProgressEvent.Finished);

        return new PunchProgressOutcome(
            events, StateFor(PunchProgressEvent.Finished), DataSocketTaken: true);
    }

    /// <summary>
    /// The state a run ends in, given how it went. Three inputs, three answers, and only one of
    /// them is the one a balanced pair would give.
    /// </summary>
    public static PsnConnectState EndStateFor(bool offerSucceeds, bool punchSucceeds)
        => Run(offerSucceeds, punchSucceeds).EndState;
}

/// <summary>
/// PP503: the C and the client's own spelling, because the claim is about an event that is absent.
/// </summary>
public static class PunchProgressSource
{
    /// <summary>session.c, which raises the two.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>The client's backend, which turns them into a state.</summary>
    public const string BackendRelativePath = @"gui\src\qmlbackend.cpp";

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? LocateSession() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>qmlbackend.cpp, or null outside a checkout.</summary>
    public static string? LocateBackend() => SanitizerSource.LocateRelative(BackendRelativePath);

    /// <summary>
    /// Whether the start is still raised before the punch and the finish after the data socket.
    ///
    /// The order that makes "finished" mean punched-and-taken. Read as three positions rather than
    /// as two strings, because either event moving is what would change the claim.
    /// </summary>
    public static bool TheEventsStillStraddleThePunch(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string text = sessionSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.IndexOf("event_start.data_holepunch.finished = false;", StringComparison.Ordinal);
        int punch = text.IndexOf(
            "chiaki_holepunch_session_punch_hole(session->holepunch_session, CHIAKI_HOLEPUNCH_PORT_TYPE_DATA)",
            StringComparison.Ordinal);
        int socket = text.IndexOf(
            "data_sock = chiaki_get_holepunch_sock(session->holepunch_session, CHIAKI_HOLEPUNCH_PORT_TYPE_DATA);",
            StringComparison.Ordinal);
        int finish = text.IndexOf("event_finish.data_holepunch.finished = true;", StringComparison.Ordinal);

        return start >= 0 && punch > start && socket > punch && finish > socket;
    }

    /// <summary>
    /// Whether the punch's failure still leaves without sending the finish.
    ///
    /// The whole claim, and it is about an absence - so it is read as the QUIT sitting between the
    /// punch and the finish, with no send of the finish above it.
    /// </summary>
    public static bool AFailedPunchSendsNoFinish(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string text = sessionSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        int punch = text.IndexOf(
            "chiaki_holepunch_session_punch_hole(session->holepunch_session, CHIAKI_HOLEPUNCH_PORT_TYPE_DATA)",
            StringComparison.Ordinal);
        int finish = text.IndexOf("event_finish.data_holepunch.finished = true;", StringComparison.Ordinal);

        if (punch < 0 || finish < punch)
            return false;

        string between = text[punch..finish];

        return between.Contains("!! Failed to punch hole for data connection.", StringComparison.Ordinal)
            && between.Contains("QUIT(quit_ctrl);", StringComparison.Ordinal)
            && !between.Contains("chiaki_session_send_event(session, &event_finish)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the offer's guard is still ABOVE the start event, so a failed offer narrates nothing.
    ///
    /// The third outcome, and the one a reader assumes away: a punch that never began looks on
    /// screen like a connect that skipped the step.
    /// </summary>
    public static bool AFailedOfferSendsNeither(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string text = sessionSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        int offer = text.IndexOf(
            "holepunch_session_create_offer(session->holepunch_session)", StringComparison.Ordinal);
        int start = text.IndexOf("event_start.data_holepunch.finished = false;", StringComparison.Ordinal);

        if (offer < 0 || start < offer)
            return false;

        return text[offer..start].Contains("QUIT(quit_ctrl);", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the client still maps the two onto the two connect states this models.
    ///
    /// The other end of the join. If the backend ever resets the state on a session failure, the
    /// asymmetry stops being visible - and that would be a reason to revisit this, not a reason
    /// for the model to have been wrong.
    /// </summary>
    public static bool TheClientMapsBothEvents(string backendSource)
    {
        ArgumentNullException.ThrowIfNull(backendSource);

        return backendSource.Contains("PsnConnectState::DataConnectionFinished", StringComparison.Ordinal)
            && backendSource.Contains("PsnConnectState::DataConnectionStart", StringComparison.Ordinal)
            && backendSource.Contains("DataHolepunchProgress", StringComparison.Ordinal);
    }
}
