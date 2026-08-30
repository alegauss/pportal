namespace ChiakiNg.Protocol;

/// <summary>One step of the punch, in the order chiaki_holepunch_session_punch_hole runs them.</summary>
public enum HolepunchPunchStep
{
    /// <summary>The state guard, which differs by port type.</summary>
    Preconditions,

    /// <summary>wait_for_session_message(OFFER).</summary>
    WaitForOffer,

    /// <summary>Acknowledging the console's offer.</summary>
    AckOffer,

    /// <summary>Sending ours.</summary>
    SendOffer,

    /// <summary>wait_for_session_message_ack, for the offer just sent.</summary>
    WaitForOfferAck,

    /// <summary>check_candidates: the race PP459 runs over real sockets.</summary>
    ChooseCandidate,

    /// <summary>send_accept, naming the candidate that answered.</summary>
    SendAccept,

    /// <summary>wait_for_session_message(ACCEPT).</summary>
    WaitForAccept,

    /// <summary>Acknowledging theirs.</summary>
    AckAccept,

    /// <summary>Setting CTRL_ESTABLISHED or DATA_ESTABLISHED.</summary>
    MarkEstablished,

    /// <summary>receive_request_send_response_ps, the last exchange over the chosen socket.</summary>
    ReceiveRequestSendResponse,
}

/// <summary>How the punch ended.</summary>
public enum HolepunchPunchOutcome
{
    /// <summary>A hole is open on the chosen candidate.</summary>
    Punched,

    /// <summary>A checkpoint consumed a cancel.</summary>
    Cancelled,

    /// <summary>The state the port type requires was not set.</summary>
    Uninitialised,

    /// <summary>A wait ran out.</summary>
    TimedOut,

    /// <summary>A step reported failure - including no candidate answering.</summary>
    Failed,
}

/// <summary>What the punch did, and where it stopped.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="StoppedAt">The step it stopped at, or null where every step ran.</param>
/// <param name="Ran">Each step that was entered, in order.</param>
public readonly record struct HolepunchPunchResult(
    HolepunchPunchOutcome Outcome, HolepunchPunchStep? StoppedAt, IReadOnlyList<HolepunchPunchStep> Ran);

/// <summary>The punch's steps, behind an interface so the sequence runs without a console.</summary>
public interface IHolepunchPunchSteps
{
    /// <summary>Whether the state this port type needs is set. PP240's guard.</summary>
    bool PreconditionsHold(HolepunchPortType type);

    /// <summary>Waiting for a session message of a given action, or timing out.</summary>
    Task<bool> WaitForMessageAsync(string action, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sending one, ACK or offer or accept.</summary>
    Task<bool> SendMessageAsync(string action, CancellationToken cancellationToken);

    /// <summary>PP459's race. False where nothing answered.</summary>
    Task<bool> ChooseCandidateAsync(CancellationToken cancellationToken);

    /// <summary>Marking the hole established for this port type.</summary>
    void MarkEstablished(HolepunchPortType type);

    /// <summary>The last exchange over the socket the race chose.</summary>
    Task<bool> ReceiveRequestSendResponseAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// PP547, under PP533: the punch sequence, run over the pieces fifteen files already model.
///
/// PP545 ran the create and PP546 the start; this is the third and the largest. PP240 has the
/// opening, PP241 the two answers thrown away, PP242 the accept, PP243 the probe, PP245 the wait,
/// PP249 the two ways it ends and PP459 the race over real sockets - and nothing put them in order.
///
/// FIVE CHECKPOINTS, WHICH IS THE CHECK RATHER THAN A RESTATEMENT. PP539 counted the fourteen and
/// found five here, more than the create's four and the start's two together, because this is the
/// step that talks to the console. A sequence that placed four or six would be visibly wrong
/// against a number read from the file, which is why <see cref="CancelChecks"/> is asserted against
/// what the sequence actually does rather than written beside it.
///
/// THE GUARD DIFFERS BY PORT TYPE, and that ordering is the whole reason the data hole cannot be
/// punched first: the control hole needs CUSTOMDATA1 to have arrived, and the data hole needs the
/// control hole established. PP240 calls that the handshake where the ordering lives.
/// </summary>
public sealed class HolepunchPunch
{
    /// <summary>PP539: the punch's share of the fourteen, and the largest.</summary>
    public const int CancelChecks = 5;

    /// <summary>SESSION_START_TIMEOUT_SEC, which the message waits use.</summary>
    public static TimeSpan MessageTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>WAIT_RESPONSE_TIMEOUT_SEC, the last exchange's own.</summary>
    public static TimeSpan ResponseTimeout { get; } = TimeSpan.FromSeconds(1);

    /// <summary>The order, which is the C's.</summary>
    public static IReadOnlyList<HolepunchPunchStep> ExecutionOrder { get; } =
    [
        HolepunchPunchStep.Preconditions,
        HolepunchPunchStep.WaitForOffer,
        HolepunchPunchStep.AckOffer,
        HolepunchPunchStep.SendOffer,
        HolepunchPunchStep.WaitForOfferAck,
        HolepunchPunchStep.ChooseCandidate,
        HolepunchPunchStep.SendAccept,
        HolepunchPunchStep.WaitForAccept,
        HolepunchPunchStep.AckAccept,
        HolepunchPunchStep.MarkEstablished,
        HolepunchPunchStep.ReceiveRequestSendResponse,
    ];

    /// <summary>
    /// The five steps the C checks a cancel before. Read off the file: after acknowledging the
    /// console's offer, after the race, after sending accept, after acknowledging theirs, and after
    /// marking the hole established.
    /// </summary>
    public static IReadOnlyList<HolepunchPunchStep> ChecksBefore { get; } =
    [
        HolepunchPunchStep.SendOffer,
        HolepunchPunchStep.SendAccept,
        HolepunchPunchStep.WaitForAccept,
        HolepunchPunchStep.MarkEstablished,
        HolepunchPunchStep.ReceiveRequestSendResponse,
    ];

    private readonly IHolepunchPunchSteps steps;
    private readonly HolepunchStop stop;
    private readonly HolepunchPortType type;

    /// <param name="steps">The punch's pieces.</param>
    /// <param name="stop">PP538's one-shot.</param>
    /// <param name="type">Which hole, which decides the guard and the state bit.</param>
    public HolepunchPunch(IHolepunchPunchSteps steps, HolepunchStop stop, HolepunchPortType type)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(stop);

        this.steps = steps;
        this.stop = stop;
        this.type = type;
    }

    /// <summary>Runs the eleven in the C's order, checking for a cancel where the C checks.</summary>
    public async Task<HolepunchPunchResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var ran = new List<HolepunchPunchStep>();

        foreach (HolepunchPunchStep step in ExecutionOrder)
        {
            if (ChecksBefore.Contains(step) && stop.CheckAndConsume())
                return new HolepunchPunchResult(HolepunchPunchOutcome.Cancelled, step, ran);

            ran.Add(step);

            (bool ok, HolepunchPunchOutcome how) =
                await RunStepAsync(step, cancellationToken).ConfigureAwait(false);

            if (!ok)
                return new HolepunchPunchResult(how, step, ran);
        }

        return new HolepunchPunchResult(HolepunchPunchOutcome.Punched, null, ran);
    }

    private async Task<(bool Ok, HolepunchPunchOutcome How)> RunStepAsync(
        HolepunchPunchStep step, CancellationToken cancellationToken)
    {
        switch (step)
        {
            case HolepunchPunchStep.Preconditions:
                return (steps.PreconditionsHold(type), HolepunchPunchOutcome.Uninitialised);

            case HolepunchPunchStep.WaitForOffer:
            case HolepunchPunchStep.WaitForOfferAck:
            case HolepunchPunchStep.WaitForAccept:
            {
                bool arrived = await steps
                    .WaitForMessageAsync(step.ToString(), MessageTimeout, cancellationToken)
                    .ConfigureAwait(false);
                return (arrived, HolepunchPunchOutcome.TimedOut);
            }

            case HolepunchPunchStep.ChooseCandidate:
                return (await steps.ChooseCandidateAsync(cancellationToken).ConfigureAwait(false),
                    HolepunchPunchOutcome.Failed);

            case HolepunchPunchStep.MarkEstablished:
                steps.MarkEstablished(type);
                return (true, HolepunchPunchOutcome.Punched);

            case HolepunchPunchStep.ReceiveRequestSendResponse:
                return (await steps
                    .ReceiveRequestSendResponseAsync(ResponseTimeout, cancellationToken)
                    .ConfigureAwait(false), HolepunchPunchOutcome.Failed);

            default:
                return (await steps.SendMessageAsync(step.ToString(), cancellationToken).ConfigureAwait(false),
                    HolepunchPunchOutcome.Failed);
        }
    }
}
