using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP538: the stop the loop PP533 has to write will need, and the shape of it that a casual
/// reading gets wrong.
/// </summary>
public class HolepunchStopTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. A cancel reaches one checkpoint, not all of them.
    ///
    /// The C consumes the flag at every check, so the second caller sees nothing. A port that made
    /// it a plain bool would pass any test that only asked once, and would stop fourteen things
    /// where the C stops one.
    /// </summary>
    [Fact]
    public void OneCancelIsDeliveredToExactlyOneCheckpoint()
    {
        var stop = new HolepunchStop();
        stop.Cancel(stopWebsocketThread: false);

        Assert.True(stop.CheckAndConsume());
        Assert.False(stop.CheckAndConsume());
        Assert.False(stop.CheckAndConsume());
    }

    /// <summary>And a second cancel is a second delivery, so the one-shot re-arms.</summary>
    [Fact]
    public void ASecondCancelIsDeliveredAgain()
    {
        var stop = new HolepunchStop();

        stop.Cancel(stopWebsocketThread: false);
        Assert.True(stop.CheckAndConsume());

        stop.Cancel(stopWebsocketThread: false);
        Assert.True(stop.CheckAndConsume());
    }

    /// <summary>
    /// The websocket flag is the opposite discipline, and that is the point of having both here:
    /// it answers every time, because the thread reading it polls.
    /// </summary>
    [Fact]
    public void TheWebsocketFlagIsReadWithoutBeingConsumed()
    {
        var stop = new HolepunchStop();
        Assert.False(stop.WebsocketShouldStop);

        stop.Cancel(stopWebsocketThread: true);

        Assert.True(stop.WebsocketShouldStop);
        Assert.True(stop.WebsocketShouldStop);
    }

    /// <summary>
    /// Cancelling without stopping the thread still stops the main loop, and leaves the websocket
    /// alone. That is the C's else branch, which only logs.
    /// </summary>
    [Fact]
    public void CancellingWithoutStoppingTheThreadLeavesTheWebsocketRunning()
    {
        var stop = new HolepunchStop();
        var pipeStopped = false;

        stop.Cancel(stopWebsocketThread: false, stopSelectPipe: () => pipeStopped = true);

        Assert.True(stop.CheckAndConsume());
        Assert.False(stop.WebsocketShouldStop);
        Assert.False(pipeStopped);
    }

    /// <summary>
    /// The ordering the C has: the pipe is stopped inside the critical section and the condition is
    /// signalled after it. Asserted by asking, from inside each callback, whether the lock is held -
    /// which is the only way to see an ordering that is otherwise invisible and easy to get wrong.
    /// </summary>
    [Fact]
    public void ThePipeStopsUnderTheLockAndTheSignalComesAfterIt()
    {
        var stop = new HolepunchStop();
        bool? heldDuringPipeStop = null;
        bool? heldDuringSignal = null;

        stop.Cancel(
            stopWebsocketThread: true,
            stopSelectPipe: () => heldDuringPipeStop = stop.LockHeld,
            signal: () => heldDuringSignal = stop.LockHeld);

        Assert.True(heldDuringPipeStop);
        Assert.False(heldDuringSignal);
    }

    /// <summary>Reset clears both, as the C does before a session runs.</summary>
    [Fact]
    public void ResetClearsBoth()
    {
        var stop = new HolepunchStop();
        stop.Cancel(stopWebsocketThread: true);

        stop.Reset();

        Assert.False(stop.CheckAndConsume());
        Assert.False(stop.WebsocketShouldStop);
    }

    /// <summary>
    /// And the C still does it this way. Fourteen checkpoints, all fourteen consuming, one plain
    /// read of the websocket flag - read from holepunch.c so that a change there fails here rather
    /// than leaving the model above describing a file that moved on.
    /// </summary>
    [Fact]
    public void TheCStillConsumesAtEveryCheckpoint()
    {
        if (HolepunchStopSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.Equal(14, HolepunchStopSource.Checkpoints(source));
        Assert.Equal(14, HolepunchStopSource.ConsumingCheckpoints(source));
        Assert.Equal(1, HolepunchStopSource.WebsocketReads(source));
    }

    /// <summary>
    /// PP539: the fourteen sit in six functions, and three of the six are the blocking waits.
    ///
    /// That second half is the finding. A cancel arriving while the loop is blocked does not sit
    /// there until the wait times out - each wait checks the one-shot itself and answers
    /// CHIAKI_ERR_CANCELED, which is how session_create's "canceled" branch is ever reached. A
    /// managed loop whose waits did not check would honour a cancel only between steps and would
    /// look correct until somebody cancelled during a thirty-second wait.
    /// </summary>
    [Fact]
    public void TheCancelIsHonouredInSixPlacesAndThreeAreWaits()
    {
        if (HolepunchStopSource.Locate() is not { } path)
            return;

        var points = HolepunchStopSource.CancelPoints(File.ReadAllText(path));

        Assert.Equal(14, points.Sum(p => p.Checks));
        Assert.Equal(6, points.Count);
        Assert.Equal(3, points.Count(p => p.IsWait));

        Assert.Equal(4, points.Single(p => p.Function == "chiaki_holepunch_session_create").Checks);
        Assert.Equal(2, points.Single(p => p.Function == "chiaki_holepunch_session_start").Checks);
        Assert.Equal(5, points.Single(p => p.Function == "chiaki_holepunch_session_punch_hole").Checks);
        Assert.All(HolepunchStopSource.Waits, w => Assert.Equal(1, points.Single(p => p.Function == w).Checks));
    }

    /// <summary>
    /// And the attribution walks BACKWARDS, which is what two earlier attempts got wrong: a regex
    /// scanning forwards for function boundaries misses a multi-line signature, and every
    /// checkpoint after one gets filed under the previous function. Written out here as the shape
    /// that broke it - a definition whose parameters wrap.
    /// </summary>
    [Fact]
    public void AMultiLineSignatureDoesNotMisattributeTheCheckpoint()
    {
        const string source = """
            static ChiakiErrorCode payload_only(ChiakiLog *log, json_object *msg)
            {
                return CHIAKI_ERR_SUCCESS;
            }

            static ChiakiErrorCode wait_for_notification(
                Session *session, Notification **out, uint16_t types, uint64_t timeout_ms)
            {
                chiaki_mutex_lock(&session->stop_mutex);
                if(session->main_should_stop)
                {
                    session->main_should_stop = false;
                    return CHIAKI_ERR_CANCELED;
                }
            }
            """;

        var points = HolepunchStopSource.CancelPoints(source);

        CancelPointIsOnlyTheWait(points);

        static void CancelPointIsOnlyTheWait(IReadOnlyList<HolepunchStopSource.CancelPoint> points)
        {
            HolepunchStopSource.CancelPoint only = Assert.Single(points);
            Assert.Equal("wait_for_notification", only.Function);
            Assert.True(only.IsWait);
        }
    }

    /// <summary>
    /// And the reader can tell the two apart. A checkpoint that read without clearing is the
    /// difference this whole task is about, so the counter is run against one written out.
    /// </summary>
    [Fact]
    public void ACheckpointThatDoesNotConsumeIsNotCounted()
    {
        const string consuming = """
            if(session->main_should_stop)
            {
                session->main_should_stop = false;
                goto cleanup;
            }
            """;

        const string peeking = """
            if(session->main_should_stop)
            {
                goto cleanup;
            }
            """;

        Assert.Equal(1, HolepunchStopSource.Checkpoints(consuming));
        Assert.Equal(1, HolepunchStopSource.ConsumingCheckpoints(consuming));

        Assert.Equal(1, HolepunchStopSource.Checkpoints(peeking));
        Assert.Equal(0, HolepunchStopSource.ConsumingCheckpoints(peeking));
    }
}
