using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// What a stream run needs from the world, named for the C calls it stands in for.
///
/// Every member is one thing chiaki_stream_connection_run does to something outside itself, so a
/// host that records its calls produces the run's TRACE - and the trace is what the six orderings
/// are asserted over. A host that answers false or sets a flag is how each failure path is reached
/// without a console.
/// </summary>
public interface IStreamRunHost
{
    /// <summary>chiaki_audio_receiver_new for audio. False is the failure the C returns on directly.</summary>
    bool CreateAudioReceiver();

    /// <summary>chiaki_audio_receiver_new for haptics.</summary>
    bool CreateHapticsReceiver();

    /// <summary>chiaki_video_receiver_new.</summary>
    bool CreateVideoReceiver();

    /// <summary>chiaki_takion_connect. False is the failure that must NOT close a takion.</summary>
    bool ConnectTakion();

    /// <summary>chiaki_congestion_control_start.</summary>
    bool StartCongestionControl();

    /// <summary>
    /// PP774: state_finished = false and state_failed = false, as every state entry does them.
    ///
    /// On the interface because the RUN is what enters a state, and the run has only this. It was a
    /// member of the host alone, so the walk could not call it and did not - which
    /// <see cref="StreamConnectionStatesSource.EveryStateStillClearsBothFlags"/> already refuses of
    /// the C, in a check that was only ever pointed outward.
    ///
    /// PP773: AND THE STATE ITSELF, because the C writes all three together and this carried two.
    /// Five times in streamconnection.c the state is assigned and the two flags cleared on the next
    /// two lines, and the state is what the handler thread reads to decide which of three handlers a
    /// protobuf reaches. A run that cleared the flags without saying which state it had entered left
    /// that decision with nothing to make it on.
    /// </summary>
    /// <param name="state">The state being entered, which is where arriving messages are routed by.</param>
    void BeginState(StreamState state);

    /// <summary>stream_connection_send_big.</summary>
    bool SendBig();

    /// <summary>chiaki_feedback_sender_init and the controller state it is handed.</summary>
    bool StartFeedbackSender();

    /// <summary>
    /// chiaki_cond_timedwait_pred on state_finished_cond_check, for one state.
    /// </summary>
    /// <returns>The flags as they read when the wait returned, and whether it returned by timeout.</returns>
    (StreamWaitState Flags, bool TimedOut) Wait(StreamState state);

    /// <summary>Whether a streaminfo arrived while the state was still EXPECT_BANG.</summary>
    bool HasEarlyStreaminfo { get; }

    /// <summary>
    /// stream_connection_takion_data_expect_streaminfo on the buffered one, then free it.
    /// </summary>
    /// <returns>The flags after the handler ran - finished, if the buffered message was a whole one.</returns>
    StreamWaitState ReplayEarlyStreaminfo();

    /// <summary>chiaki_mutex_unlock(&amp;state_mutex).</summary>
    void Unlock();

    /// <summary>chiaki_mutex_lock(&amp;state_mutex).</summary>
    void Lock();

    /// <summary>chiaki_session_send_event with CHIAKI_EVENT_CONNECTED.</summary>
    void SendConnected();

    /// <summary>The idle loop's wait. Timeout is the work; anything else leaves the loop.</summary>
    ChiakiError WaitIdle();

    /// <summary>stream_connection_send_heartbeat. Its failure is logged and ignored.</summary>
    bool SendHeartbeat();

    /// <summary>input_to_wire copied out of the feedback sender, BEFORE the fini.</summary>
    void LiftInputToWire();

    /// <summary>chiaki_feedback_sender_fini.</summary>
    void FiniFeedbackSender();

    /// <summary>stream_connection_send_disconnect, from the label.</summary>
    void SendDisconnect();

    /// <summary>chiaki_congestion_control_stop.</summary>
    void StopCongestionControl();

    /// <summary>chiaki_takion_close, which joins the thread that writes the stage counters.</summary>
    void CloseTakion();

    /// <summary>The four stage timings copied out, after the close and before the free.</summary>
    void LiftStages();

    /// <summary>chiaki_video_receiver_free.</summary>
    void FreeVideoReceiver();

    /// <summary>chiaki_audio_receiver_free on the haptics receiver.</summary>
    void FreeHapticsReceiver();

    /// <summary>chiaki_audio_receiver_free on the audio receiver.</summary>
    void FreeAudioReceiver();

    /// <summary>should_stop, as the disconnect label reads it.</summary>
    bool ShouldStop { get; }

    /// <summary>remote_disconnected, as the disconnect label reads it.</summary>
    bool RemoteDisconnected { get; }
}

/// <summary>
/// PP772: how far the run's walk got, which is not what a teardown frees.
///
/// <see cref="StreamBuilt"/> answers what has to be released and stops moving at congestion control,
/// because past that nothing more is built. The walk keeps going for six more steps - the takion
/// connect wait, the BIG, the bang, the streaminfo, the feedback sender and the idle loop - and a
/// live run failing in any of them reported the same word.
///
/// Two ladders because they answer two questions. Conflating them is what produced the gap: a
/// teardown ladder read as a progress one is right until they stop agreeing, and they stop agreeing
/// at exactly the rung this port has reached.
/// </summary>
public enum StreamRung
{
    /// <summary>Nothing yet: not even the audio receiver.</summary>
    Start,

    /// <summary>The three receivers exist.</summary>
    Receivers,

    /// <summary>And takion is connected.</summary>
    TakionConnected,

    /// <summary>And congestion control is running.</summary>
    CongestionStarted,

    /// <summary>And the takion connect state finished.</summary>
    TakionConnectAwaited,

    /// <summary>And the BIG went out - the message that starts a stream.</summary>
    BigSent,

    /// <summary>And the console answered it with a bang.</summary>
    BangAwaited,

    /// <summary>And the stream info arrived.</summary>
    StreaminfoAwaited,

    /// <summary>And the feedback sender is running, so the pad reaches the console.</summary>
    FeedbackStarted,

    /// <summary>And CONNECTED went out: from here the run is streaming.</summary>
    Connected,

    /// <summary>The idle loop ended, which is a session that ran and stopped.</summary>
    Idle,
}

/// <summary>
/// PP295: chiaki_stream_connection_run as a sequence, driving the models PP362 to PP366 wrote apart.
///
/// Those five modelled the pieces - the state walk, the idle loop, the teardown cascade, the three
/// layers of dispatch - and PP640 stated the six orderings between them as checks on the C. None of
/// that is a run. This is: one function that walks the states with <see cref="StreamConnectionStates"/>,
/// loops with <see cref="StreamIdleLoop"/>, and unwinds through <see cref="StreamTeardown"/>'s
/// cascade from wherever a failure entered, in the order the C does each of them.
///
/// THE ORDER IS THE DELIVERABLE, which is why the host records a trace and the tests read it. A run
/// that made every one of these calls in some other order would pass any comparison of messages
/// and fail a session - creating the receivers backwards, reading the stage counters before the
/// close, sending CONNECTED with the lock held. Each of PP640's six is a fact about THIS sequence.
///
/// AND WRITING IT FOUND THE TABLE WRONG. <see cref="StreamTeardown.EntryAfter"/> entered one label
/// earlier than the C's goto at every rung; three of those were hidden by null-safe frees and the
/// fourth would have closed a takion that never connected. That is the failure PP295's design
/// predicts - "a port that reproduces every function and not their sequence" - arriving in the
/// port's own model before it arrived in its code.
///
/// WHAT THIS DOES NOT DO is talk to anything. The host is an interface, and PP297's capture cannot
/// judge a run: the tap sits on ctrl and the session request, and none of this crosses either.
/// What can be judged is the sequence, against the six checks that already hold the C.
/// </summary>

public static class ManagedStreamRun
{
    /// <summary>
    /// The run. Returns what the C returns from the same exit.
    /// </summary>
    public static ChiakiError Run(IStreamRunHost host) => Run(host, out _);

    /// <summary>
    /// PP770: the same run, saying how far it got.
    ///
    /// Every rung's failure leaves by the same cascade with the same code, so a run that failed at
    /// the connect and one that failed at the BIG were the same sentence - and locating either cost
    /// a rebuild, a console and a trial. The C logs what each step was before it goes; the port
    /// reproduced the ordering and the teardown faithfully and dropped the half that says which.
    ///
    /// NOT A NEW CODE PER STEP. The codes are the C's and inventing more would be a departure. What
    /// this adds is the rung the walk reached, which the cascade already computes for its own use -
    /// <see cref="StreamBuilt"/> is what each exit hands the teardown, and this is the same value
    /// carried out instead of only down.
    /// </summary>
    /// <param name="reached">What had been built when it returned, which is where it stopped.</param>
    public static ChiakiError Run(IStreamRunHost host, out StreamBuilt reached)
        => Run(host, out reached, out _);

    /// <summary>
    /// PP772: the same run, saying how far the WALK got as well as what was built.
    ///
    /// StreamBuilt stops at congestion control because past it nothing more is built, and the six
    /// steps after that are where a live run now fails. Two ladders, because they answer two
    /// questions - and the second one is the one a person reading a failure wants.
    /// </summary>
    /// <param name="rung">The furthest step the walk completed.</param>
    public static ChiakiError Run(IStreamRunHost host, out StreamBuilt reached, out StreamRung rung)
    {
        ArgumentNullException.ThrowIfNull(host);

        reached = StreamBuilt.Nothing;
        rung = StreamRung.Start;

        host.Lock();

        // Ordering 1, first half: audio, haptics, video - and each failure enters the cascade at the
        // label that frees exactly what was built. The audio receiver failing has no label at all in
        // the C; it unlocks and returns, and EntryAfter(Nothing) frees a null to the same effect.
        if (!host.CreateAudioReceiver())
        {
            host.Unlock();
            return ChiakiError.Unknown;
        }

        reached = StreamBuilt.AudioReceiver;

        if (!host.CreateHapticsReceiver())
            return Unwind(host, StreamBuilt.AudioReceiver, ChiakiError.Unknown, unlockFirst: true);

        reached = StreamBuilt.HapticsReceiver;

        if (!host.CreateVideoReceiver())
            return Unwind(host, StreamBuilt.HapticsReceiver, ChiakiError.Unknown, unlockFirst: true);

        reached = StreamBuilt.VideoReceiver;
        rung = StreamRung.Receivers;

        // STATE_TAKION_CONNECT. A connect that fails goes to err_video_receiver: takion is not up,
        // so it is not closed. This is the rung the old table got wrong.
        //
        // PP774: THE CLEAR GOES HERE, before the action and after the state is entered - which is
        // where the C puts it. An event raised while the connect is running must count; one raised
        // before it must not. Clearing after the action drops the first, and clearing at the wait
        // drops both.
        host.BeginState(StreamState.TakionConnect);

        if (!host.ConnectTakion())
            return Unwind(host, StreamBuilt.VideoReceiver, ChiakiError.Unknown, unlockFirst: true);

        reached = StreamBuilt.Takion;
        rung = StreamRung.TakionConnected;

        if (!host.StartCongestionControl())
            return Unwind(host, StreamBuilt.Takion, ChiakiError.Unknown, unlockFirst: false);

        reached = StreamBuilt.CongestionControl;
        rung = StreamRung.CongestionStarted;

        (StreamWaitState flags, bool timedOut) = host.Wait(StreamState.TakionConnect);
        switch (StreamConnectionStates.Next(flags, timedOut))
        {
            case StreamStep.Stopped:
                return Unwind(host, StreamBuilt.Takion, ChiakiError.Canceled, unlockFirst: false);
            case StreamStep.Failed:
            case StreamStep.Wait:
                return Unwind(host, StreamBuilt.CongestionControl, ChiakiError.Unknown, unlockFirst: false);
            default:
                break;
        }

        // STATE_EXPECT_BANG. From here every failure goes to disconnect, which is ordering 6: the
        // console is told on the paths that failed as well as the one that did not.
        rung = StreamRung.TakionConnectAwaited;

        // STATE_EXPECT_BANG, cleared before the send for PP774's reason.
        host.BeginState(StreamState.ExpectBang);

        if (!host.SendBig())
            return Disconnect(host, ChiakiError.Unknown);

        rung = StreamRung.BigSent;

        (flags, timedOut) = host.Wait(StreamState.ExpectBang);
        switch (StreamConnectionStates.Next(flags, timedOut))
        {
            case StreamStep.Stopped:
                return Disconnect(host, ChiakiError.Canceled);
            case StreamStep.Failed:
            case StreamStep.Wait:
                return Disconnect(host, ChiakiError.Unknown);
            default:
                break;
        }

        rung = StreamRung.BangAwaited;

        // STATE_EXPECT_STREAMINFO, and ordering 2: the early buffer is replayed BEFORE the wait, and
        // the wait is skipped where the replay already finished the state.
        // STATE_EXPECT_STREAMINFO. Cleared before the replay, which is the action here - a
        // streaminfo buffered during the bang is replayed INTO this state, so a clear after it
        // would throw away the very arrival the replay exists to deliver.
        host.BeginState(StreamState.ExpectStreaminfo);

        StreamWaitState after = default;
        if (host.HasEarlyStreaminfo)
            after = host.ReplayEarlyStreaminfo();

        if (StreamConnectionStates.WaitsAfterReplay(after))
        {
            (flags, timedOut) = host.Wait(StreamState.ExpectStreaminfo);
        }
        else
        {
            (flags, timedOut) = (after, false);
        }

        switch (StreamConnectionStates.Next(flags, timedOut))
        {
            case StreamStep.Stopped:
                return Disconnect(host, ChiakiError.Canceled);
            case StreamStep.Failed:
            case StreamStep.Wait:
                return Disconnect(host, ChiakiError.Unknown);
            default:
                break;
        }

        rung = StreamRung.StreaminfoAwaited;

        if (!host.StartFeedbackSender())
            return Disconnect(host, ChiakiError.Unknown);

        rung = StreamRung.FeedbackStarted;

        // STATE_IDLE, and ordering 3: CONNECTED goes out with the state mutex released and taken
        // again after, because a handler may call back into the session.
        host.Unlock();
        host.SendConnected();
        host.Lock();

        // STATE_IDLE, the fourth the C clears - so a flag left over from the streaminfo does not
        // end the idle loop on its first pass.
        host.BeginState(StreamState.Idle);

        rung = StreamRung.Connected;

        // The idle loop, where a timeout is the work.
        ChiakiError held;
        while (true)
        {
            ChiakiError wait = host.WaitIdle();
            if (StreamIdleLoop.Next(wait) == IdleStep.Leave)
            {
                held = StreamIdleLoop.HeldOnLeaving(wait);
                break;
            }

            // A heartbeat that fails is logged and ignored, which is deliberate rather than inherited.
            host.SendHeartbeat();
        }

        // Ordering 4: the input delay is lifted BEFORE the sender is finished.
        rung = StreamRung.Idle;

        host.LiftInputToWire();
        host.FiniFeedbackSender();

        // The success path assigns SUCCESS before falling into the label, so a stream whose
        // predicate came true with neither flag set answers SUCCESS rather than the wait's code.
        _ = held;
        return Disconnect(host, ChiakiError.Success);
    }

    /// <summary>
    /// The disconnect label and everything below it. Ordering 6 lives here: the message is sent from
    /// the label, so every failure after the bang tells the console.
    /// </summary>
    private static ChiakiError Disconnect(IStreamRunHost host, ChiakiError held)
    {
        host.SendDisconnect();

        ChiakiError outcome = StreamIdleLoop.Outcome(held, host.ShouldStop, host.RemoteDisconnected);

        return Unwind(host, StreamBuilt.CongestionControl, outcome, unlockFirst: false, fromDisconnect: true);
    }

    /// <summary>
    /// The cascade from a given entry point down, in the C's label order.
    ///
    /// Orderings 1 (second half) and 5 live here: the receivers are freed in the reverse of their
    /// creation, and the stage counters are read after the close - which joins the thread that
    /// writes them - and before the video receiver they partly live in is freed.
    /// </summary>
    private static ChiakiError Unwind(
        IStreamRunHost host, StreamBuilt built, ChiakiError code, bool unlockFirst, bool fromDisconnect = false)
    {
        // The rungs above congestion control unlock in their own branch before the goto; the ones
        // at and below it arrive holding the lock and close_takion is what releases it.
        if (unlockFirst)
            host.Unlock();

        StreamExitLabel entry = fromDisconnect
            ? StreamExitLabel.CongestionControl
            : StreamTeardown.EntryAfter(built);

        foreach (StreamExitLabel label in StreamTeardown.From(entry))
        {
            switch (label)
            {
                case StreamExitLabel.Disconnect:
                    // Reached only through Disconnect(), which has already sent it.
                    break;
                case StreamExitLabel.CongestionControl:
                    host.StopCongestionControl();
                    break;
                case StreamExitLabel.CloseTakion:
                    host.Unlock();
                    host.CloseTakion();
                    break;
                case StreamExitLabel.VideoReceiver:
                    host.Lock();
                    host.LiftStages();
                    host.FreeVideoReceiver();
                    host.Unlock();
                    break;
                case StreamExitLabel.HapticsReceiver:
                    host.Lock();
                    host.FreeHapticsReceiver();
                    host.Unlock();
                    break;
                case StreamExitLabel.AudioReceiver:
                    host.Lock();
                    host.FreeAudioReceiver();
                    host.Unlock();
                    break;
                default:
                    break;
            }
        }

        return code;
    }
}
