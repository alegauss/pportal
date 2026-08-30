using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>Where a managed holepunch stopped, and what it produced if it did not.</summary>
/// <param name="FailedAt">The sequence that failed, or null where none did.</param>
/// <param name="Session">The seven asks, answered - null unless every sequence ran.</param>
/// <param name="Start">The start's failure, where the start is what failed.</param>
public readonly record struct ManagedHolepunchResult(
    ManagedHolepunchStage? FailedAt,
    SequencedHolepunchSession? Session,
    StartFailure Start);

/// <summary>The four things a managed holepunch does before session.c is entered.</summary>
public enum ManagedHolepunchStage
{
    /// <summary>chiaki_holepunch_session_create.</summary>
    Create,

    /// <summary>chiaki_holepunch_session_start.</summary>
    Start,

    /// <summary>The CTRL hole, which session.c asks for the socket of.</summary>
    PunchCtrl,

    /// <summary>Recording what the punch produced, without which the session cannot answer.</summary>
    Record,
}

/// <summary>
/// PP561, under PP33: the three sequences and the session, wired to each other.
///
/// PP548, PP549 and PP550 each put running pieces behind one interface, PP553 answered session.c's
/// seven asks from them, and PP556 made a prepared session one that can answer. Nothing joined
/// them: every one of those tasks ends at a delegate or an interface, and the thing that constructs
/// a create, hands its queue to a start, hands the same queue to a punch and gives the result to a
/// session did not exist. This is that thing, and it is what makes the eight tasks add up.
///
/// ONE QUEUE, WHICH IS THE JOIN THAT MATTERS. The create owns the websocket and therefore the
/// queue; the start and the punch read the same one. PP558 is why that works - each wait takes what
/// it handled off the queue as the C's loops do - and PP559 is why the start can trust what it
/// finds, the client's own join having been consumed by the create.
///
/// AND ONE STOP. PP538's one-shot is consumed at fourteen checkpoints across the three sequences,
/// so they share it rather than each holding their own: a cancel is one cancel wherever it lands.
///
/// WHAT THIS IS NOT is a session that has connected. It runs what the C does BEFORE session.c is
/// entered - PP553's division, which is the C's. The data hole is punched later, on demand, when
/// session.c asks.
/// </summary>
public sealed class ManagedHolepunch : IDisposable
{
    private readonly string oauthHeader;
    private readonly LiveHolepunchCreateSteps create;
    private readonly HolepunchStop stop;

    /// <param name="oauthHeader">The bearer PsnEndpoints builds.</param>
    /// <param name="pushContextId">The id the create request carries.</param>
    /// <param name="stop">PP538's one-shot, shared by all three sequences.</param>
    /// <param name="channel">The channel, injected so a test can hand over a closed one.</param>
    public ManagedHolepunch(
        string oauthHeader, string pushContextId, HolepunchStop? stop = null, PushChannel? channel = null)
    {
        ArgumentNullException.ThrowIfNull(oauthHeader);
        ArgumentNullException.ThrowIfNull(pushContextId);

        this.oauthHeader = oauthHeader;
        this.stop = stop ?? new HolepunchStop();
        create = new LiveHolepunchCreateSteps(oauthHeader, pushContextId, channel);
    }

    /// <summary>The queue the websocket fills, which all three sequences read.</summary>
    public NotificationQueue Queue => create.Queue;

    /// <summary>The one-shot the three share.</summary>
    public HolepunchStop Stop => stop;

    /// <summary>The session id the create came back with, once it has.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>The console asked for, as 64 hex characters.</summary>
    public string ExpectedDeviceUid { get; set; } = "";

    /// <summary>Whether the wakeup runs, which only a PS4 does.</summary>
    public bool IsPs4 { get; init; }

    /// <summary>How the punch is run for a port. Injected because the race needs a network.</summary>
    public Func<HolepunchPortType, Task<(HolepunchPunchResult Result, object? Socket)>>? Punch { get; set; }

    /// <summary>
    /// Runs the create, the start and the CTRL punch, and hands back a session that can answer.
    ///
    /// Each sequence is given the same queue and the same stop. The stage that failed is named, so
    /// a caller knows which of the three rather than only that something did.
    /// </summary>
    public async Task<ManagedHolepunchResult> RunAsync(CancellationToken cancellationToken = default)
    {
        HolepunchCreateResult created = await new HolepunchCreate(create, stop)
            .RunAsync(cancellationToken).ConfigureAwait(false);

        if (created.Outcome != HolepunchCreateOutcome.Created)
            return Failed(ManagedHolepunchStage.Create);

        var startSteps = new LiveHolepunchStartSteps(oauthHeader, SessionId, Queue, ExpectedDeviceUid)
        {
            // PP552: so a dead socket ends a wait instead of serving out thirty seconds.
            ChannelEnded = () => create.ChannelEnded,
        };

        HolepunchStartResult started = await new HolepunchStart(startSteps, stop, IsPs4)
            .RunAsync(cancellationToken).ConfigureAwait(false);

        if (started.Outcome != HolepunchStartOutcome.Started)
            return new ManagedHolepunchResult(ManagedHolepunchStage.Start, null, started.Failure);

        if (Punch is not { } punch)
            return Failed(ManagedHolepunchStage.PunchCtrl);

        // The session punches the DATA hole on demand, when session.c asks for it.
        var session = new SequencedHolepunchSession(
            async type => (await punch(type).ConfigureAwait(false)).Result);

        // PP556's guarantee: prepared means able to answer the first ask, which is this socket.
        bool prepared = await session
            .TakeCtrlHoleAsync(() => punch(HolepunchPortType.Ctrl))
            .ConfigureAwait(false);

        if (!prepared)
        {
            session.Dispose();
            return Failed(ManagedHolepunchStage.Record);
        }

        return new ManagedHolepunchResult(null, session, StartFailure.None);
    }

    private static ManagedHolepunchResult Failed(ManagedHolepunchStage at)
        => new(at, null, StartFailure.None);

    /// <summary>Closes the channel, which ends the read loop and the queue with it.</summary>
    public void Dispose() => create.Dispose();
}
