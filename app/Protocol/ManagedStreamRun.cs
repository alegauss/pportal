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
    public static ChiakiError Run(IStreamRunHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.Lock();

        // Ordering 1, first half: audio, haptics, video - and each failure enters the cascade at the
        // label that frees exactly what was built. The audio receiver failing has no label at all in
        // the C; it unlocks and returns, and EntryAfter(Nothing) frees a null to the same effect.
        if (!host.CreateAudioReceiver())
        {
            host.Unlock();
            return ChiakiError.Unknown;
        }

        if (!host.CreateHapticsReceiver())
            return Unwind(host, StreamBuilt.AudioReceiver, ChiakiError.Unknown, unlockFirst: true);

        if (!host.CreateVideoReceiver())
            return Unwind(host, StreamBuilt.HapticsReceiver, ChiakiError.Unknown, unlockFirst: true);

        // STATE_TAKION_CONNECT. A connect that fails goes to err_video_receiver: takion is not up,
        // so it is not closed. This is the rung the old table got wrong.
        if (!host.ConnectTakion())
            return Unwind(host, StreamBuilt.VideoReceiver, ChiakiError.Unknown, unlockFirst: true);

        if (!host.StartCongestionControl())
            return Unwind(host, StreamBuilt.Takion, ChiakiError.Unknown, unlockFirst: false);

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
        if (!host.SendBig())
            return Disconnect(host, ChiakiError.Unknown);

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

        // STATE_EXPECT_STREAMINFO, and ordering 2: the early buffer is replayed BEFORE the wait, and
        // the wait is skipped where the replay already finished the state.
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

        if (!host.StartFeedbackSender())
            return Disconnect(host, ChiakiError.Unknown);

        // STATE_IDLE, and ordering 3: CONNECTED goes out with the state mutex released and taken
        // again after, because a handler may call back into the session.
        host.Unlock();
        host.SendConnected();
        host.Lock();

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
