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
