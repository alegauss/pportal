using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Where a stream run's four stage timings go, which is SessionBaseline in the application.</summary>
public interface IStageSink
{
    /// <summary>chiaki_session_set_stage, one timer at a time.</summary>
    void Stage(FrameStageTimer stage, ulong sampleUs);

    /// <summary>The input-to-wire sample lifted out of the feedback sender before its fini.</summary>
    void InputToWire(ulong inputUs);
}

/// <summary>
/// PP745: the baseline behind that seam, so the run's samples reach the statistics the port keeps.
///
/// Written with the interface rather than after it, because PP741 counted what happens otherwise:
/// PP740 closed one seam and opened another in the same commit, and the census reported success. An
/// output seam whose consumer already exists has no reason to be owed for a single commit.
/// </summary>
public sealed class BaselineStages(SessionBaseline baseline) : IStageSink
{
    /// <inheritdoc/>
    public void Stage(FrameStageTimer stage, ulong sampleUs) => baseline.PushStage(stage, sampleUs);

    /// <inheritdoc/>
    public void InputToWire(ulong inputUs) => baseline.PushInputToWire(inputUs);
}

/// <summary>
/// PP745, under PP707: the first implementation of <see cref="IStreamRunHost"/> outside the tests.
///
/// PP712 counted the run host's twenty-five members and PP714, PP719, PP723, PP726, PP727 and PP740
/// wrote what was owed, so every member names a counterpart. None of them was REACHED: the only
/// class implementing the interface was a script in the test project, and PP669's rule is that a
/// mapping is not a call. This is the call.
///
/// TWENTY-FOUR MEMBERS ARE DELEGATION and the census says to what. What is not delegation is the
/// wait: <c>chiaki_cond_timedwait_pred</c> on state_finished_cond_check, which is a lock, flags that
/// whatever arrives sets, and a bounded wait on the predicate
/// <see cref="StreamConnectionStates.WaitEnds"/> already states. That predicate is used rather than
/// re-written, so a wait here ends exactly where the C's does - on finished, stopped or the remote
/// going away, and NOT on failed, which PP365 found is written by three handlers and read by none.
///
/// THE LOCK AND UNLOCK ARE COUNTED, NOT TAKEN. The C's pair brackets the CONNECTED callback so a
/// handler may call back into the session, and PP712 already ruled both NotNeeded: a managed lock is
/// the language's. Taking a real monitor here would mean the run must hold it before it calls
/// Unlock, which is the C's ownership and not this port's. So the depth is recorded - the trace and
/// PP640's third ordering are what those two members are for - and the wait takes its own lock.
///
/// COLLABORATORS COME IN. A host that built its own takion would decide the socket, the session and
/// every lifetime at once, and none of those is this task; the three receivers arrive as factories
/// so that returning null is the C's allocation failure, which the interface's own comments say is
/// a path the run has to reach.
/// </summary>
public sealed class ManagedStreamRunHost : IStreamRunHost
{
    // A plain object: the wait is Monitor-based, and System.Threading.Lock has no Wait.
    private readonly object gate = new();

    private readonly ManagedTakion takion;
    private readonly IPEndPoint peer;
    private readonly ManagedCongestionControl congestion;
    private readonly ManagedFeedbackSender feedback;
    private readonly ManagedSessionEvents events;
    private readonly IStreamMessageSink messages;
    private readonly IStageSink stages;
    private readonly Func<StreamMessage> big;

    private readonly Func<ManagedVideoReceiver?> videoFactory;
    private readonly Func<ManagedAudioReceiverPair?> audioFactory;
    private readonly Func<ManagedAudioReceiverPair?> hapticsFactory;

    private StreamWaitState flags;
    private byte[]? early;

    /// <summary>Everything the run needs from the world, none of it built here.</summary>
    public ManagedStreamRunHost(
        ManagedTakion takion,
        IPEndPoint peer,
        ManagedCongestionControl congestion,
        ManagedFeedbackSender feedback,
        ManagedSessionEvents events,
        IStreamMessageSink messages,
        IStageSink stages,
        Func<StreamMessage> big,
        Func<ManagedVideoReceiver?> video,
        Func<ManagedAudioReceiverPair?> audio,
        Func<ManagedAudioReceiverPair?> haptics)
    {
        ArgumentNullException.ThrowIfNull(takion);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(congestion);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(big);
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(haptics);

        this.takion = takion;
        this.peer = peer;
        this.congestion = congestion;
        this.feedback = feedback;
        this.events = events;
        this.messages = messages;
        this.stages = stages;
        this.big = big;
        videoFactory = video;
        audioFactory = audio;
        hapticsFactory = haptics;
    }

    /// <summary>How long a state's wait is given, which is streamconnection.c's EXPECT_TIMEOUT_MS.</summary>
    public int ExpectTimeoutMs { get; init; } = StreamIdleLoop.ExpectTimeoutMs;

    /// <summary>And the idle loop's, which is the heartbeat interval it wakes on.</summary>
    public int IdleTimeoutMs { get; init; } = StreamIdleLoop.HeartbeatIntervalMs;

    /// <summary>How deep the C's state mutex would be held, which PP640's third ordering is about.</summary>
    public int LockDepth { get; private set; }

    /// <summary>The video receiver, once created.</summary>
    public ManagedVideoReceiver? Video { get; private set; }

    /// <summary>The audio arm's receivers, once created.</summary>
    public ManagedAudioReceiverPair? Audio { get; private set; }

    /// <summary>And the haptics arm's, which audioreceiver.c is used twice for.</summary>
    public ManagedAudioReceiverPair? Haptics { get; private set; }

    /// <summary>The flags as they stand, for a caller that is not the run.</summary>
    public StreamWaitState Flags
    {
        get
        {
            lock (gate)
                return flags;
        }
    }

    /// <inheritdoc/>
    public bool HasEarlyStreaminfo
    {
        get
        {
            lock (gate)
                return early is not null;
        }
    }

    /// <inheritdoc/>
    public bool ShouldStop => Flags.ShouldStop;

    /// <inheritdoc/>
    public bool RemoteDisconnected => Flags.RemoteDisconnected;

    /// <summary>
    /// What a message arriving sets, which is the other half of every wait.
    ///
    /// The C's handlers write these fields under state_mutex and signal state_cond; this is the
    /// same, and it is why the wait has a real lock while Lock and Unlock do not.
    /// </summary>
    public void Signal(
        bool? finished = null, bool? shouldStop = null, bool? remoteDisconnected = null, bool? failed = null)
    {
        lock (gate)
        {
            flags = new StreamWaitState(
                finished ?? flags.Finished,
                shouldStop ?? flags.ShouldStop,
                remoteDisconnected ?? flags.RemoteDisconnected,
                failed ?? flags.Failed);

            Monitor.PulseAll(gate);
        }
    }

    /// <summary>A streaminfo that arrived while the state was still EXPECT_BANG, held for replay.</summary>
    public void BufferEarlyStreaminfo(ReadOnlySpan<byte> message)
    {
        byte[] held = message.ToArray();

        lock (gate)
            early = held;
    }

    /// <summary>Clears the flags a state's step begins with, as the C does before each wait.</summary>
    public void BeginState()
    {
        lock (gate)
            flags = flags with { Finished = false, Failed = false };
    }

    /// <inheritdoc/>
    public bool CreateVideoReceiver()
    {
        Video = videoFactory();
        return Video is not null;
    }

    /// <inheritdoc/>
    public bool CreateAudioReceiver()
    {
        Audio = audioFactory();
        return Audio is not null;
    }

    /// <inheritdoc/>
    public bool CreateHapticsReceiver()
    {
        Haptics = hapticsFactory();
        return Haptics is not null;
    }

    /// <inheritdoc/>
    public bool ConnectTakion() => takion.Connect(peer).Error == ChiakiError.Success;

    /// <inheritdoc/>
    public bool StartCongestionControl()
    {
        congestion.Start();
        return true;
    }

    /// <inheritdoc/>
    public bool SendBig()
    {
        messages.Send(big());
        return true;
    }

    /// <inheritdoc/>
    public bool StartFeedbackSender()
    {
        feedback.Start();
        return true;
    }

    /// <inheritdoc/>
    public (StreamWaitState Flags, bool TimedOut) Wait(StreamState state)
    {
        lock (gate)
        {
            // The predicate is the C's, not a re-statement of it: a wait that returned because the
            // flags moved has still said nothing until they are re-read, which is PP362's whole note.
            bool signalled = Monitor.Wait(gate, ExpectTimeoutMs) || StreamConnectionStates.WaitEnds(flags);

            while (signalled && !StreamConnectionStates.WaitEnds(flags))
                signalled = Monitor.Wait(gate, ExpectTimeoutMs);

            return (flags, !StreamConnectionStates.WaitEnds(flags));
        }
    }

    /// <inheritdoc/>
    public StreamWaitState ReplayEarlyStreaminfo()
    {
        lock (gate)
        {
            // Freed after the handler runs, as the C frees the buffered message; a second replay
            // would be the same message handled twice.
            early = null;
            return flags;
        }
    }

    /// <inheritdoc/>
    public void Unlock() => LockDepth--;

    /// <inheritdoc/>
    public void Lock() => LockDepth++;

    /// <inheritdoc/>
    public void SendConnected() => events.SendConnected();

    /// <inheritdoc/>
    public ChiakiError WaitIdle()
    {
        lock (gate)
        {
            // Timeout is the work: the loop's heartbeat rides the wait expiring, and anything else
            // leaves the loop. PP363's shape, with the flags decided by the caller afterwards.
            return Monitor.Wait(gate, IdleTimeoutMs) ? ChiakiError.Success : ChiakiError.Timeout;
        }
    }

    /// <inheritdoc/>
    public bool SendHeartbeat()
    {
        messages.Send(StreamMessages.Heartbeat());
        return true;
    }

    /// <inheritdoc/>
    public void LiftInputToWire() => feedback.LiftInputToWire(stages.InputToWire);

    /// <inheritdoc/>
    public void FiniFeedbackSender() => feedback.Stop();

    /// <inheritdoc/>
    public void SendDisconnect() => messages.Send(StreamMessages.Disconnect());

    /// <inheritdoc/>
    public void StopCongestionControl() => congestion.Stop();

    /// <inheritdoc/>
    public void CloseTakion() => takion.Dispose();

    /// <inheritdoc/>
    public void LiftStages()
    {
        // After the close and before the free, which is where the C reads counters the takion's own
        // thread writes - so the join is what makes the numbers whole.
        foreach (FrameStageTimer stage in StageOrder)
            stages.Stage(stage, 0);
    }

    /// <summary>The four the C copies out, in its order. Decode is the session's, not the run's.</summary>
    public static IReadOnlyList<FrameStageTimer> StageOrder { get; } =
        [FrameStageTimer.Receive, FrameStageTimer.Reorder, FrameStageTimer.Reassemble, FrameStageTimer.Correct];

    /// <inheritdoc/>
    public void FreeVideoReceiver() => Video = null;

    /// <inheritdoc/>
    public void FreeHapticsReceiver() => Haptics = null;

    /// <inheritdoc/>
    public void FreeAudioReceiver() => Audio = null;
}
