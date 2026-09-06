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
    private string? reason;

    // STATE_IDLE, which is what chiaki_stream_connection_init leaves it at.
    private StreamState current = StreamState.Idle;

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

    /// <summary>
    /// PP754: how long the takion's handshake is given, which was the C's fifteen seconds and
    /// nothing else.
    ///
    /// The two waits above were knobs from the start and this was not, so a caller could shorten
    /// every wait the run makes except the longest one. Found by a runner whose own timeout was
    /// shorter than a connect nobody was answering.
    /// </summary>
    public int ConnectTimeoutMs { get; init; } = TakionHandshake.ExpectTimeoutMs;

    /// <summary>
    /// PP773: the takion this run drives, for a caller reading why a wait ended in nothing.
    ///
    /// Its Dispatched count and its receive thread are the two facts that separate "nothing arrived"
    /// from "something arrived and no handler claimed it", and a live run that stops at a wait says
    /// neither. The same ladder PP770 and PP771 built one level down.
    /// </summary>
    public ManagedTakion Takion => takion;

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

    /// <summary>
    /// PP755: the reason the console gave for going away, which the session thread writes its quit
    /// reason from.
    ///
    /// The C keeps it on the stream connection, set by the disconnect handler and read after the
    /// run - so this is where it lives here too, and <see cref="ManagedStreamRunner"/> reads it off
    /// the host rather than being handed one.
    /// </summary>
    public string? RemoteDisconnectReason
    {
        get
        {
            lock (gate)
                return reason;
        }
    }

    /// <summary>
    /// PP755: the disconnect handler's own pair, set together as the C sets them.
    ///
    /// stream_connection_takion_data_disconnect writes remote_disconnected AND the reason before it
    /// signals, so a wait that ends on the flag always finds a reason that belongs to it. Two calls
    /// would leave a window where the flag is true and the reason is the last session's.
    ///
    /// A NULL REASON IS A CASE THE C HAS. Its strdup can fail, and PP371 found the session thread
    /// dereferencing the result twice without testing - so absent stays representable here rather
    /// than being smoothed into an empty string.
    /// </summary>
    public void SignalRemoteDisconnected(string? disconnectReason)
    {
        lock (gate)
        {
            reason = disconnectReason;
            flags = flags with { RemoteDisconnected = true };

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

    /// <summary>
    /// PP773: where the walk is, which is what an ARRIVING message is routed by.
    ///
    /// The C keeps it on the stream connection beside the two flags, and its handlers read it under
    /// the same mutex the run's wait takes - so a protobuf reaching the dispatch is a bang, a
    /// streaminfo or an idle message depending on nothing but this. Read under the lock for that
    /// reason: the reader is the takion's thread and the writer is the run's.
    ///
    /// It starts at <see cref="StreamState.Idle"/> because chiaki_stream_connection_init does, which
    /// is not the state the run's first entry sets - so a message arriving before the walk begins
    /// goes to the idle handler rather than to a bang nobody has asked for yet.
    /// </summary>
    public StreamState State
    {
        get
        {
            lock (gate)
                return current;
        }
    }

    /// <summary>Enters a state: the assignment and the two clears, which the C writes together.</summary>
    public void BeginState(StreamState state)
    {
        lock (gate)
        {
            current = state;
            flags = flags with { Finished = false, Failed = false };
        }
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

    /// <summary>
    /// PP769: the socket the session handed over, which the takion runs on instead of connecting.
    ///
    /// Null leaves it opening its own, which is what every test does and what a run driven from
    /// nowhere has to do. A live session hands one: the C's stream connection never opens a socket,
    /// and a managed run that did started a conversation the console was not in the middle of.
    /// </summary>
    public void AdoptSocket(nint? socket) => takion.Adopted = socket;

    /// <summary>
    /// PP771: what the last connect's handshake answered, or null before one ran.
    ///
    /// <see cref="ConnectTakion"/> answers a bool because the C's rung does, and the handshake
    /// behind it knows far more: the error, and how many INIT and COOKIE attempts it spent. A live
    /// run stopping here told nobody which of the four messages went unanswered, so locating it
    /// meant instrumenting the tree and asking a console again.
    ///
    /// The same shape PP770 gave the run one level up: the value is already computed, and what was
    /// missing is that it left.
    /// </summary>
    public TakionHandshakeOutcome? LastHandshake { get; private set; }

    /// <inheritdoc/>
    public bool ConnectTakion()
    {
        TakionHandshakeOutcome outcome = takion.Connect(peer, ConnectTimeoutMs);
        LastHandshake = outcome;

        if (outcome.Error != ChiakiError.Success)
            return false;

        // PP773: AND THE RECEIVE THREAD, which chiaki_takion_connect starts before it returns. Every
        // caller of the loop before this was a test reading its trace, so a live run connected, sent
        // a BIG and read nothing - the arrivals were wired to a dispatch nobody was calling.
        //
        // Here rather than in Connect because a takion that connected is not always one that should
        // start reading: PP607's responder tests drive the loop themselves, and the C's own thread
        // is started by the connect the RUN makes.
        takion.StartReceiving();

        // PP773: THIS IS THE CONNECTED EVENT, and it is raised here because here is where it
        // happens. In the C, chiaki_takion_connect starts a thread and returns, and the takion's
        // CONNECTED event reaches the handler later and sets state_finished - so the run's wait is
        // what turns an asynchronous arrival into a synchronous step.
        //
        // This port's Connect BLOCKS until the handshake is complete, so by the time it answers
        // Success the event has already happened. Signalling on the return is that same fact, and
        // waiting for an arrival that will never come again is how the live run reached
        // CongestionStarted and stopped.
        Signal(finished: true);

        return true;
    }

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
            //
            // PP775: AGAINST A DEADLINE, and it used to be against the whole timeout each time round.
            // Every pulse that did not satisfy the predicate started the clock over, so a thread
            // pulsing faster than ExpectTimeoutMs held this here for as long as it pulsed - which
            // PP746's driver does every 5ms. chiaki_cond_timedwait_pred computes an absolute
            // deadline and honours it across every spurious wake, and this is that.
            //
            // PP457 IS THE SAME DEFECT ONE MODULE OVER: the punch loop re-armed its full timeout on
            // every extra response. Twice now, which is what makes it a shape rather than a slip.
            long deadline = Environment.TickCount64 + ExpectTimeoutMs;

            while (!StreamConnectionStates.WaitEnds(flags))
            {
                long left = deadline - Environment.TickCount64;
                if (left <= 0)
                    break;

                Monitor.Wait(gate, (int)left);
            }

            return (flags, !StreamConnectionStates.WaitEnds(flags));
        }
    }

    /// <summary>
    /// PP773: where a replayed streaminfo goes, which is the same handler the live one reaches.
    ///
    /// Installed rather than constructed, because the handler needs this host and this host holds
    /// the buffer - <see cref="StreamArrivals.Replay"/> is the method the composition root joins
    /// here. Absent leaves the replay doing what it did before this task: freeing the message and
    /// answering with the flags as they stood, which is the C's replay minus the handler.
    /// </summary>
    public Action<byte[]>? ReplayHandler { get; set; }

    /// <inheritdoc/>
    public StreamWaitState ReplayEarlyStreaminfo()
    {
        lock (gate)
        {
            // Freed after the handler runs, as the C frees the buffered message; a second replay
            // would be the same message handled twice.
            byte[]? held = early;
            early = null;

            // Under the lock, which is where the C runs it: the state mutex spans the whole handler,
            // so a wait that returns has always seen the flag the handler left. A managed monitor is
            // re-entrant, so the Signal inside takes the lock this call already holds.
            if (held is not null)
                ReplayHandler?.Invoke(held);

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
