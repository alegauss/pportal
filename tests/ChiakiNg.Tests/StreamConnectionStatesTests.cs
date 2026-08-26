using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP362, under PP295: the three states a stream connection walks before a stream exists.
///
/// PP297's capture cannot judge any of it - the tap sits on ctrl and the session request, and none
/// of this crosses either. So it is asserted against streamconnection.c.
/// </summary>
public class StreamConnectionStatesTests
{
    /// <summary>The walk, in order, and what follows each.</summary>
    [Fact]
    public void TheWalkIsThreeStatesThenIdle()
    {
        Assert.Equal(
            [StreamState.TakionConnect, StreamState.ExpectBang, StreamState.ExpectStreaminfo],
            StreamConnectionStates.Walk);

        Assert.Equal(StreamState.ExpectBang, StreamConnectionStates.After(StreamState.TakionConnect));
        Assert.Equal(StreamState.ExpectStreaminfo, StreamConnectionStates.After(StreamState.ExpectBang));
        Assert.Equal(StreamState.Idle, StreamConnectionStates.After(StreamState.ExpectStreaminfo));
        Assert.Null(StreamConnectionStates.After(StreamState.Idle));
    }

    /// <summary>
    /// STOP WINS, as it does at every wait site in these files - even over a finished state.
    /// </summary>
    [Fact]
    public void StopWinsOverAFinishedState()
    {
        Assert.Equal(
            StreamStep.Stopped,
            StreamConnectionStates.Next(
                new StreamWaitState(Finished: true, ShouldStop: true), waitTimedOut: false));
    }

    /// <summary>
    /// PP365: THE FAILURE FLAG CHANGES NOTHING, which is not what its name suggests.
    ///
    /// Three handlers set it and nothing reads it - not the wait predicate, not the run. So a bang
    /// that failed to parse and a console that never sent one arrive at the same place, one of them
    /// after the whole timeout. The C's own log line admits it: "didn't receive bang OR failed to
    /// handle it".
    /// </summary>
    [Fact]
    public void TheFailureFlagChangesNothing()
    {
        Assert.False(StreamConnectionStates.FailureFlagIsRead);

        // Finished wins whether or not a handler also reported failure.
        Assert.Equal(
            StreamStep.Advance,
            StreamConnectionStates.Next(
                new StreamWaitState(Finished: true, Failed: true), waitTimedOut: false));

        // And a failure alone does not even end the wait.
        Assert.False(StreamConnectionStates.WaitEnds(new StreamWaitState(Failed: true)));
        Assert.Equal(
            StreamStep.Wait,
            StreamConnectionStates.Next(new StreamWaitState(Failed: true), waitTimedOut: false));
    }

    /// <summary>What does end the wait: finished, stopped, or the remote going away.</summary>
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    public void TheWaitEndsOnThreeThings(
        bool finished, bool stopped, bool remote, bool ends)
    {
        Assert.Equal(
            ends,
            StreamConnectionStates.WaitEnds(new StreamWaitState(finished, stopped, remote)));
    }

    /// <summary>
    /// A wait that has not ended keeps waiting; one that ran out with nothing is a failure.
    ///
    /// That is the trap: the wait returning says nothing, so both answers come from re-reading the
    /// flags and not from the wait.
    /// </summary>
    [Fact]
    public void AWaitThatRanOutWithNothingIsAFailure()
    {
        Assert.Equal(
            StreamStep.Wait,
            StreamConnectionStates.Next(new StreamWaitState(), waitTimedOut: false));

        Assert.Equal(
            StreamStep.Failed,
            StreamConnectionStates.Next(new StreamWaitState(), waitTimedOut: true));
    }

    /// <summary>
    /// ONLY STREAMINFO IS BUFFERED WHEN IT ARRIVES EARLY.
    ///
    /// The asymmetry looks like an oversight and is the behaviour: a bang arriving before the state
    /// is not kept.
    /// </summary>
    [Theory]
    [InlineData(StreamState.ExpectStreaminfo, true)]
    [InlineData(StreamState.ExpectBang, false)]
    [InlineData(StreamState.TakionConnect, false)]
    [InlineData(StreamState.Idle, false)]
    public void OnlyStreaminfoIsKeptWhenItArrivesEarly(StreamState state, bool buffered)
    {
        Assert.Equal(buffered, StreamConnectionStates.IsBufferedWhenEarly(state));
    }

    /// <summary>And a replay that finished the state means the wait is skipped entirely.</summary>
    [Fact]
    public void AReplayThatFinishedTheStateSkipsTheWait()
    {
        Assert.False(StreamConnectionStates.WaitsAfterReplay(new StreamWaitState(Finished: true)));
        Assert.True(StreamConnectionStates.WaitsAfterReplay(new StreamWaitState()));
    }

    /// <summary>And streamconnection.c still walks it the way this reproduces.</summary>
    [Fact]
    public void TheStreamConnectionStillDeclaresTheWalk()
    {
        string? path = StreamConnectionStatesSource.Locate();
        if (path is null)
            return;

        string source = File.ReadAllText(path);
        string? run = StreamConnectionStatesSource.RunBody(path);
        Assert.NotNull(run);

        Assert.True(
            StreamConnectionStatesSource.EveryStateStillClearsBothFlags(run),
            "a state is entered without clearing both flags, so its wait inherits the last verdict");
        Assert.True(
            StreamConnectionStatesSource.ThePredicateStillWatchesTheThree(source),
            "the wait predicate watches a different set of flags than this port assumes");
        Assert.True(
            StreamConnectionStatesSource.TheFailureFlagIsStillDead(source),
            "state_failed is now read somewhere, so failures are reported sooner than this models");
        Assert.True(
            StreamConnectionStatesSource.EarlyStreaminfoIsStillReplayed(run),
            "early streaminfo is no longer replayed, or the wait after it is no longer skipped");
    }
}
