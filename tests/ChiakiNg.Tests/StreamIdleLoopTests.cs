using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP363, under PP295: the idle loop where a timeout is the work.
///
/// The assertion this file exists for is <see cref="TheTwoLoopsDisagreeAboutWhatCarriesOn"/>. Every
/// other test here says what one loop does; that one says the two loops are opposites, which is the
/// thing a port gets wrong and the thing no reading of either file alone would catch.
/// </summary>
public class StreamIdleLoopTests
{
    private static string? Run()
    {
        string? path = StreamIdleLoopSource.Locate();
        return path is null ? null : StreamIdleLoopSource.RunBody(path);
    }

    /// <summary>THE WORK BRANCH. A timeout sends a heartbeat and waits again.</summary>
    [Fact]
    public void ATimeoutIsTheWork()
    {
        Assert.Equal(IdleStep.SendHeartbeat, StreamIdleLoop.Next(ChiakiError.Timeout));
    }

    /// <summary>
    /// And EVERYTHING else leaves, success included - which is the part that reads backwards.
    ///
    /// The predicate the wait sits on means finished, stopped or the console gone, so the wait
    /// succeeding is the stream ending.
    /// </summary>
    [Theory]
    [InlineData(ChiakiError.Success)]
    [InlineData(ChiakiError.Canceled)]
    [InlineData(ChiakiError.Disconnected)]
    [InlineData(ChiakiError.Unknown)]
    [InlineData(ChiakiError.Thread)]
    public void EverythingThatIsNotATimeoutLeaves(ChiakiError wait)
    {
        Assert.Equal(IdleStep.Leave, StreamIdleLoop.Next(wait));
    }

    /// <summary>
    /// THE TRAP, STATED. The two loops in these two files disagree about which return value means
    /// carry on, and a port written from memory of one gets the other exactly wrong.
    ///
    /// Asserted as a disagreement rather than as two separate facts, because that is the shape of
    /// the mistake: each loop is individually plausible and it is the pair that is surprising.
    /// </summary>
    [Fact]
    public void TheTwoLoopsDisagreeAboutWhatCarriesOn()
    {
        // The stream loop: a timeout carries on, cancelled does not.
        Assert.Equal(IdleStep.SendHeartbeat, StreamIdleLoop.Next(ChiakiError.Timeout));
        Assert.Equal(IdleStep.Leave, StreamIdleLoop.Next(ChiakiError.Canceled));

        // PP349's ctrl loop: cancelled IS the work, and it is where the queue is drained.
        Assert.Equal(
            CtrlStep.DrainQueue,
            CtrlLoop.Next(CtrlWake.Cancelled, new CtrlWakeState(QueueHasWork: true)));

        // A wait that broke ends the ctrl channel, which is the branch the stream loop gives to
        // cancelled. Nothing about the two is transferable.
        Assert.Equal(CtrlStep.Fail, CtrlLoop.Next(CtrlWake.Failed, new CtrlWakeState()));
    }

    /// <summary>
    /// A heartbeat that fails to send is logged and ignored, so a stream whose heartbeats are all
    /// failing looks alive from in here.
    /// </summary>
    [Fact]
    public void AFailedHeartbeatDoesNotEndTheStream()
    {
        Assert.False(StreamIdleLoop.AFailedHeartbeatEndsTheLoop);
    }

    /// <summary>The idle wait is a second, where the three states before it are given five.</summary>
    [Fact]
    public void TheIdleWaitIsShorterThanTheStateWaits()
    {
        Assert.Equal(1000, StreamIdleLoop.HeartbeatIntervalMs);
        Assert.True(StreamIdleLoop.HeartbeatIntervalMs < StreamIdleLoop.ExpectTimeoutMs);
    }

    /// <summary>
    /// THE OUTCOME'S ORDER. A stop beats a remote disconnect, and both beat what the loop held.
    /// </summary>
    [Fact]
    public void AStopWinsOverEverythingElse()
    {
        Assert.Equal(
            ChiakiError.Canceled,
            StreamIdleLoop.Outcome(ChiakiError.Success, shouldStop: true, remoteDisconnected: true));

        Assert.Equal(
            ChiakiError.Disconnected,
            StreamIdleLoop.Outcome(ChiakiError.Success, shouldStop: false, remoteDisconnected: true));

        Assert.Equal(
            ChiakiError.Success,
            StreamIdleLoop.Outcome(ChiakiError.Success, shouldStop: false, remoteDisconnected: false));
    }

    /// <summary>And what the loop held stands where neither flag is set.</summary>
    [Fact]
    public void WhatTheLoopHeldStandsWhereNeitherFlagIsSet()
    {
        Assert.Equal(
            ChiakiError.Network,
            StreamIdleLoop.Outcome(
                StreamIdleLoop.HeldOnLeaving(ChiakiError.Network),
                shouldStop: false,
                remoteDisconnected: false));
    }

    /// <summary>
    /// And it joins to PP336: the three codes this produces are the three the session's table reads.
    /// </summary>
    [Fact]
    public void TheOutcomeIsWhatTheSessionsTableExpects()
    {
        ChiakiError stopped = StreamIdleLoop.Outcome(ChiakiError.Success, true, false);
        ChiakiError gone = StreamIdleLoop.Outcome(ChiakiError.Success, false, true);

        Assert.Equal(ChiakiQuitReason.Stopped, SessionTeardown.FromStreamConnection(stopped, null));
        Assert.Equal(
            ChiakiQuitReason.StreamConnectionRemoteDisconnected,
            SessionTeardown.FromStreamConnection(gone, null));
    }

    /// <summary>And streamconnection.c still does all three of those things.</summary>
    [Fact]
    public void TheRunStillWorksThisWay()
    {
        if (Run() is not { } run)
            return;

        Assert.True(
            StreamIdleLoopSource.ATimeoutIsStillTheWorkBranch(run),
            "the idle loop no longer leaves on anything that is not a timeout");
        Assert.True(
            StreamIdleLoopSource.AFailedHeartbeatIsStillIgnored(run),
            "a failed heartbeat now ends the loop, which the C does not");
        Assert.True(
            StreamIdleLoopSource.TheOutcomeIsStillDecidedInThatOrder(run),
            "the disconnect label no longer decides the code stop-then-remote");
    }

    /// <summary>The readers say no to a file with nothing in it (PP272).</summary>
    [Fact]
    public void TheReadersReadTheFile()
    {
        Assert.False(StreamIdleLoopSource.ATimeoutIsStillTheWorkBranch(""));
        Assert.False(StreamIdleLoopSource.AFailedHeartbeatIsStillIgnored(""));
        Assert.False(StreamIdleLoopSource.TheOutcomeIsStillDecidedInThatOrder(""));

        // And the inverted loop, which is the one edit that would matter most.
        const string Inverted = """
            	while(true)
            	{
            		err = chiaki_cond_timedwait_pred(&stream_connection->state_cond, &stream_connection->state_mutex, HEARTBEAT_INTERVAL_MS, state_finished_cond_check, stream_connection);
            		if(err == CHIAKI_ERR_TIMEOUT)
            			break;

            		err = stream_connection_send_heartbeat(stream_connection);
            	}
            """;

        Assert.False(StreamIdleLoopSource.ATimeoutIsStillTheWorkBranch(Inverted));
    }
}
