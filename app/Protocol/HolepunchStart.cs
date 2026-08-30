namespace ChiakiNg.Protocol;

/// <summary>One step of the start, in the order chiaki_holepunch_session_start runs them.</summary>
public enum HolepunchStartStep
{
    /// <summary>The two state guards: created, and not already started.</summary>
    Preconditions,

    /// <summary>http_ps4_session_wakeup, on a PS4 only.</summary>
    WakeUpPs4,

    /// <summary>http_start_session.</summary>
    StartSession,

    /// <summary>Waiting for MEMBER_CREATED and CUSTOM_DATA1_UPDATED.</summary>
    WaitForMember,
}

/// <summary>How the start ended.</summary>
public enum HolepunchStartOutcome
{
    /// <summary>The console joined and identified itself.</summary>
    Started,

    /// <summary>A checkpoint consumed a cancel.</summary>
    Cancelled,

    /// <summary>The session was never created. CHIAKI_ERR_UNINITIALIZED.</summary>
    Uninitialised,

    /// <summary>It was started already. The C answers CHIAKI_ERR_UNKNOWN.</summary>
    AlreadyStarted,

    /// <summary>
    /// The notification wait ran out. The C answers CHIAKI_ERR_HOST_DOWN here and not TIMEOUT,
    /// which is a mapping worth keeping: a console that never joins is down, not slow.
    /// </summary>
    HostDown,

    /// <summary>A step reported failure, or the console that joined was not the one asked for.</summary>
    Failed,
}

/// <summary>What the start did, and where it stopped.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="StoppedAt">The step it stopped at, or null where every step ran.</param>
/// <param name="Failure">
/// PP257's name for what actually went wrong, which is not always what the C reports.
/// </param>
/// <param name="Ran">Each step that was entered, in order.</param>
public readonly record struct HolepunchStartResult(
    HolepunchStartOutcome Outcome,
    HolepunchStartStep? StoppedAt,
    StartFailure Failure,
    IReadOnlyList<HolepunchStartStep> Ran);

/// <summary>The three things the start does, behind an interface so the sequence runs offline.</summary>
public interface IHolepunchStartSteps
{
    /// <summary>Whether the session is created and not already started.</summary>
    bool PreconditionsHold(out bool created);

    /// <summary>The PS4 wakeup, which a PS5 skips.</summary>
    Task<bool> WakeUpPs4Async(CancellationToken cancellationToken);

    /// <summary>http_start_session.</summary>
    Task<bool> StartSessionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Waits for the console to join and identify itself, answering PP257's name for what went
    /// wrong - <see cref="StartFailure.None"/> where nothing did.
    /// </summary>
    Task<StartFailure> WaitForMemberAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// PP546, under PP533: the start sequence, run over the ported pieces.
///
/// PP545 did the create the same way and for the same reason: PP257 ported what the start DOES -
/// its failures, its shadowed variable, its lock releases - and nothing put the steps in order. So
/// the start was a reading of the C and not a thing that runs.
///
/// IT REPORTS WHAT WENT WRONG, WHICH THE C DOES NOT. PP257 found the defect and made the decision
/// this relies on: the function declares its error variable, then shadows it inside the branch that
/// handles the console's arrival, so two failures write the inner one, break, and return the
/// success the wait had left in the outer. One of the two is the identity check - a session that
/// joined the WRONG console is reported as success. <see cref="SessionStart.Lost"/> names both.
///
/// This carries the real failure on the result and answers Failed for it. That is a departure, and
/// it is the same shape PP545's bounded wait is: the non-goal reproduces behaviour, and reporting
/// success for the wrong console is not behaviour worth reproducing. What the C would have said is
/// still available from <see cref="SessionStart.Reported"/>, so the difference is legible rather
/// than lost in the other direction.
///
/// THE CANCELLATION IS PP538'S ONE-SHOT AT PP539'S TWO POINTS, which is session_start's share of
/// the fourteen - read from the C, not chosen here.
/// </summary>
public sealed class HolepunchStart
{
    /// <summary>PP539: session_start's share of the fourteen checkpoints.</summary>
    public const int CancelChecks = 2;

    /// <summary>SESSION_START_TIMEOUT_SEC, which the C does bound.</summary>
    public static TimeSpan MemberTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>The order, which is the C's.</summary>
    public static IReadOnlyList<HolepunchStartStep> ExecutionOrder { get; } =
    [
        HolepunchStartStep.Preconditions,
        HolepunchStartStep.WakeUpPs4,
        HolepunchStartStep.StartSession,
        HolepunchStartStep.WaitForMember,
    ];

    private readonly IHolepunchStartSteps steps;
    private readonly HolepunchStop stop;
    private readonly bool isPs4;

    /// <param name="steps">The three, ported.</param>
    /// <param name="stop">PP538's one-shot.</param>
    /// <param name="isPs4">Whether the wakeup runs at all.</param>
    public HolepunchStart(IHolepunchStartSteps steps, HolepunchStop stop, bool isPs4 = false)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(stop);

        this.steps = steps;
        this.stop = stop;
        this.isPs4 = isPs4;
    }

    /// <summary>Runs the steps in the C's order, checking for a cancel where the C checks.</summary>
    public async Task<HolepunchStartResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var ran = new List<HolepunchStartStep>();

        if (!steps.PreconditionsHold(out bool created))
        {
            ran.Add(HolepunchStartStep.Preconditions);
            return new HolepunchStartResult(
                created ? HolepunchStartOutcome.AlreadyStarted : HolepunchStartOutcome.Uninitialised,
                HolepunchStartStep.Preconditions, StartFailure.None, ran);
        }

        ran.Add(HolepunchStartStep.Preconditions);

        // The wakeup is a PS4's, and a PS5 does not run it at all - so it is skipped rather than
        // run and ignored, which is what makes Ran a record of what happened.
        if (isPs4)
        {
            ran.Add(HolepunchStartStep.WakeUpPs4);
            if (!await steps.WakeUpPs4Async(cancellationToken).ConfigureAwait(false))
                return Stopped(HolepunchStartOutcome.Failed, HolepunchStartStep.WakeUpPs4, ran);
        }

        ran.Add(HolepunchStartStep.StartSession);
        if (!await steps.StartSessionAsync(cancellationToken).ConfigureAwait(false))
            return Stopped(HolepunchStartOutcome.Failed, HolepunchStartStep.StartSession, ran);

        // First checkpoint: after the start call, before the wait.
        if (stop.CheckAndConsume())
            return Stopped(HolepunchStartOutcome.Cancelled, HolepunchStartStep.WaitForMember, ran);

        ran.Add(HolepunchStartStep.WaitForMember);
        StartFailure failure = await steps
            .WaitForMemberAsync(MemberTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (failure != StartFailure.None)
        {
            // The departure: reported as the failure it is, including the two the C loses.
            return new HolepunchStartResult(
                HolepunchStartOutcome.Failed, HolepunchStartStep.WaitForMember, failure, ran);
        }

        // Second checkpoint: the wait itself honours a cancel, and so does the step after it.
        return stop.CheckAndConsume()
            ? Stopped(HolepunchStartOutcome.Cancelled, HolepunchStartStep.WaitForMember, ran)
            : new HolepunchStartResult(
                HolepunchStartOutcome.Started, null, StartFailure.None, ran);
    }

    private static HolepunchStartResult Stopped(
        HolepunchStartOutcome outcome, HolepunchStartStep at, IReadOnlyList<HolepunchStartStep> ran)
        => new(outcome, at, StartFailure.None, ran);
}
