namespace ChiakiNg.Protocol;

/// <summary>One step of the create, in the order chiaki_holepunch_session_create runs them.</summary>
public enum HolepunchCreateStep
{
    /// <summary>get_websocket_fqdn: asking PSN which host to open the channel against.</summary>
    WebSocketFqdn,

    /// <summary>The C creates a thread here; managed, the channel is opened.</summary>
    OpenWebSocket,

    /// <summary>Waiting for it to report itself open. The one wait the C cannot end.</summary>
    WaitForOpen,

    /// <summary>http_create_session: the POST that asks PSN for a session.</summary>
    CreateSession,

    /// <summary>Waiting for SESSION_CREATED and MEMBER_CREATED to arrive on the queue.</summary>
    WaitForCreated,
}

/// <summary>How the create ended.</summary>
public enum HolepunchCreateOutcome
{
    /// <summary>Every step ran.</summary>
    Created,

    /// <summary>A checkpoint consumed a cancel, which is CHIAKI_ERR_CANCELED in the C.</summary>
    Cancelled,

    /// <summary>A wait ran out. CHIAKI_ERR_TIMEOUT.</summary>
    TimedOut,

    /// <summary>A step reported failure.</summary>
    Failed,
}

/// <summary>What the create did, and where it stopped.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="StoppedAt">The step it stopped at, or null where every step ran.</param>
/// <param name="Ran">Each step that was entered, in order.</param>
public readonly record struct HolepunchCreateResult(
    HolepunchCreateOutcome Outcome, HolepunchCreateStep? StoppedAt, IReadOnlyList<HolepunchCreateStep> Ran);

/// <summary>
/// The five things the create does, behind an interface so the sequence runs without a network.
///
/// Each returns whether it succeeded; the waits also distinguish running out of time, because the
/// C does - a timed-out notification wait is CHIAKI_ERR_TIMEOUT and not a failure.
/// </summary>
public interface IHolepunchCreateSteps
{
    /// <summary>PP254's lookup.</summary>
    Task<bool> LookUpFqdnAsync(CancellationToken cancellationToken);

    /// <summary>PP267's channel, opened.</summary>
    Task<bool> OpenWebSocketAsync(CancellationToken cancellationToken);

    /// <summary>Whether it reported itself open before the deadline.</summary>
    Task<bool> WaitForOpenAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>PP266's create call.</summary>
    Task<bool> CreateSessionAsync(CancellationToken cancellationToken);

    /// <summary>Whether the two notifications arrived before the deadline.</summary>
    Task<bool> WaitForCreatedAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// PP545, under PP533: the create sequence, driven over the ported pieces rather than described.
///
/// PP254, PP267, PP212 and PP266 ported the host lookup, the websocket channel, the notification
/// queue and the HTTP calls. Nothing put them in order - no file in app/ referenced more than two
/// of the four - so the create existed as SessionCreate's reading of the C and as nothing that
/// runs. This runs it, the way PP479 runs the connect flow: a sequence over an interface, so the
/// order is testable and the pieces plug in behind it.
///
/// THE CANCELLATION IS PP538'S ONE-SHOT AT PP539'S FOUR POINTS. session_create holds four of the
/// fourteen checkpoints, and <see cref="CancelChecks"/> is that count rather than a number chosen
/// here. Each consults <see cref="HolepunchStop.CheckAndConsume"/>, so a cancel is delivered once -
/// which is the property a plain bool would quietly lose.
///
/// AND ONE DEPARTURE, DECLARED. The C's wait for the websocket has no timeout and no escape: PP258
/// found it, and it hangs whenever the connect fails, which an expired token is enough to cause.
/// The non-goal reproduces behaviour rather than redesigning it, so an exception has to be argued
/// and not taken - and shipping a known hang is not a reproduction worth having. The wait is
/// bounded here, and <see cref="BoundsTheWaitTheCDoesNot"/> says so out loud rather than leaving
/// the difference for a reader to notice.
/// </summary>
public sealed class HolepunchCreate
{
    /// <summary>PP539: session_create's share of the fourteen checkpoints.</summary>
    public const int CancelChecks = 4;

    /// <summary>
    /// The departure from the C, as a value. True, and the reason is on the type - a difference
    /// this deliberate should be answerable without reading prose.
    /// </summary>
    public const bool BoundsTheWaitTheCDoesNot = true;

    /// <summary>
    /// What the C gives the notification wait: SESSION_CREATION_TIMEOUT_SEC, thirty seconds.
    /// </summary>
    public static TimeSpan CreatedTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// What the C gives the websocket wait: nothing. Ten seconds is this port's, and it is the
    /// whole of the departure - long enough that a slow but working connect is not cut off, short
    /// enough that a refused one is an error rather than a hang.
    /// </summary>
    public static TimeSpan OpenTimeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The order, which is the C's.</summary>
    public static IReadOnlyList<HolepunchCreateStep> ExecutionOrder { get; } =
    [
        HolepunchCreateStep.WebSocketFqdn,
        HolepunchCreateStep.OpenWebSocket,
        HolepunchCreateStep.WaitForOpen,
        HolepunchCreateStep.CreateSession,
        HolepunchCreateStep.WaitForCreated,
    ];

    private readonly IHolepunchCreateSteps steps;
    private readonly HolepunchStop stop;

    /// <param name="steps">The five, ported.</param>
    /// <param name="stop">PP538's one-shot, consulted at the four checkpoints.</param>
    public HolepunchCreate(IHolepunchCreateSteps steps, HolepunchStop stop)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(stop);

        this.steps = steps;
        this.stop = stop;
    }

    /// <summary>
    /// Runs the five in order, checking for a cancel where the C checks.
    ///
    /// The checkpoints sit BEFORE the websocket opens, before and after the create call, and after
    /// the notification wait - which is where session_create has its four, and is why a cancel
    /// during the open is honoured by the bounded wait rather than by a check the C does not have.
    /// </summary>
    public async Task<HolepunchCreateResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var ran = new List<HolepunchCreateStep>();

        foreach (HolepunchCreateStep step in ExecutionOrder)
        {
            if (ChecksBefore(step) && stop.CheckAndConsume())
                return new HolepunchCreateResult(HolepunchCreateOutcome.Cancelled, step, ran);

            ran.Add(step);

            (bool ok, bool timedOut) = await RunStepAsync(step, cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                return new HolepunchCreateResult(
                    timedOut ? HolepunchCreateOutcome.TimedOut : HolepunchCreateOutcome.Failed, step, ran);
            }
        }

        // The fourth: after the last wait, which is where the C checks once more before returning.
        return stop.CheckAndConsume()
            ? new HolepunchCreateResult(HolepunchCreateOutcome.Cancelled, HolepunchCreateStep.WaitForCreated, ran)
            : new HolepunchCreateResult(HolepunchCreateOutcome.Created, null, ran);
    }

    /// <summary>
    /// Which steps the C checks a cancel before. Three of the four are here and the fourth is
    /// after the last step, which together are session_create's count.
    /// </summary>
    public static bool ChecksBefore(HolepunchCreateStep step) => step
        is HolepunchCreateStep.OpenWebSocket
        or HolepunchCreateStep.CreateSession
        or HolepunchCreateStep.WaitForCreated;

    private async Task<(bool Ok, bool TimedOut)> RunStepAsync(
        HolepunchCreateStep step, CancellationToken cancellationToken)
    {
        switch (step)
        {
            case HolepunchCreateStep.WebSocketFqdn:
                return (await steps.LookUpFqdnAsync(cancellationToken).ConfigureAwait(false), false);

            case HolepunchCreateStep.OpenWebSocket:
                return (await steps.OpenWebSocketAsync(cancellationToken).ConfigureAwait(false), false);

            case HolepunchCreateStep.WaitForOpen:
            {
                bool open = await steps.WaitForOpenAsync(OpenTimeout, cancellationToken).ConfigureAwait(false);
                // The departure: not opening in time is a timeout here and is nothing in the C.
                return (open, !open);
            }

            case HolepunchCreateStep.CreateSession:
                return (await steps.CreateSessionAsync(cancellationToken).ConfigureAwait(false), false);

            default:
            {
                bool created = await steps.WaitForCreatedAsync(CreatedTimeout, cancellationToken)
                    .ConfigureAwait(false);
                return (created, !created);
            }
        }
    }
}
