using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP338, continuing PP293: the order a session must be taken down in, and the two ways of getting
/// it wrong.
///
/// Neither failure names itself. Finishing a running session is a use-after-free with a stack
/// entirely inside libchiaki; joining one nobody stopped is a hang against a console that never
/// answers. The contract that prevents both is written in no header and no comment, which is why
/// it is written here.
/// </summary>
public class SessionLifecycleTests
{
    /// <summary>
    /// THE ORDER, which is the whole of what a caller needs.
    /// </summary>
    [Fact]
    public void TheOnlySafeTeardownIsStopThenJoinThenFinish()
    {
        Assert.Equal(["stop", "join", "fini"], SessionLifecycle.TeardownOrder);
    }

    /// <summary>
    /// FINISHING A RUNNING SESSION FREES WHAT THE THREAD IS STANDING ON.
    ///
    /// fini destroys the stop pipe, the condition variable and the state mutex - every primitive
    /// the session thread is inside. It neither stops nor joins first, so nothing prevents this.
    /// </summary>
    [Theory]
    [InlineData(SessionPhase.Running)]
    [InlineData(SessionPhase.Stopping)]
    public void FinishingBeforeTheThreadIsJoinedIsAUseAfterFree(SessionPhase phase)
    {
        Assert.Equal(LifecycleVerdict.UseAfterFree, SessionLifecycle.Finishing(phase));
    }

    /// <summary>And after a join, or with no thread at all, it is exactly right.</summary>
    [Theory]
    [InlineData(SessionPhase.Built)]
    [InlineData(SessionPhase.Joined)]
    public void FinishingIsRightWithNoThreadAndAfterAJoin(SessionPhase phase)
    {
        Assert.Equal(LifecycleVerdict.Allowed, SessionLifecycle.Finishing(phase));
    }

    /// <summary>
    /// JOINING WITHOUT STOPPING HANGS, and that is the second failure.
    ///
    /// chiaki_session_join is a thread join and nothing else - it sets no flag and signals nothing -
    /// so a caller who joins a running session waits for a thread that is waiting for a console.
    /// </summary>
    [Fact]
    public void JoiningARunningSessionNobodyStoppedHangs()
    {
        Assert.Equal(LifecycleVerdict.Hangs, SessionLifecycle.Joining(SessionPhase.Running));
    }

    /// <summary>Once a stop has been asked for, the join is what the caller wants.</summary>
    [Fact]
    public void JoiningAfterAStopIsWhatTheCallerWants()
    {
        Assert.Equal(LifecycleVerdict.Allowed, SessionLifecycle.Joining(SessionPhase.Stopping));
    }

    /// <summary>Joining twice, or joining a session that never started, does nothing.</summary>
    [Theory]
    [InlineData(SessionPhase.Built)]
    [InlineData(SessionPhase.Joined)]
    public void JoiningWithNoThreadDoesNothing(SessionPhase phase)
    {
        Assert.Equal(LifecycleVerdict.NoOp, SessionLifecycle.Joining(phase));
    }

    /// <summary>Stopping twice is harmless, which is what lets a dispose stop unconditionally.</summary>
    [Fact]
    public void StoppingTwiceIsHarmless()
    {
        Assert.Equal(LifecycleVerdict.Allowed, SessionLifecycle.Stopping(SessionPhase.Running));
        Assert.Equal(LifecycleVerdict.NoOp, SessionLifecycle.Stopping(SessionPhase.Stopping));
    }

    /// <summary>And nothing at all is safe once everything is freed.</summary>
    [Fact]
    public void NothingIsSafeAfterTheFree()
    {
        Assert.Equal(LifecycleVerdict.UseAfterFree, SessionLifecycle.Stopping(SessionPhase.Finished));
        Assert.Equal(LifecycleVerdict.UseAfterFree, SessionLifecycle.Joining(SessionPhase.Finished));
        Assert.Equal(LifecycleVerdict.UseAfterFree, SessionLifecycle.Finishing(SessionPhase.Finished));
    }

    /// <summary>
    /// STOPPING IS FOUR POKES, because the thread can be blocked four ways.
    ///
    /// A condition wait, a socket select behind the stop pipe, and the stream connection are
    /// different blockers, and setting the flag alone reaches none of them. Which sessions hang
    /// would then depend on where the console stopped answering, so it reproduces differently
    /// every time - which is the worst shape a hang can have.
    /// </summary>
    [Fact]
    public void StoppingIsFourWakeUpsAndNotAFlag()
    {
        Assert.Equal(4, SessionLifecycle.StopWakesUp.Count);
        Assert.Contains("should_stop", SessionLifecycle.StopWakesUp[0], StringComparison.Ordinal);
        Assert.Contains("stop_pipe_stop", SessionLifecycle.StopWakesUp[1], StringComparison.Ordinal);
        Assert.Contains("cond_signal", SessionLifecycle.StopWakesUp[2], StringComparison.Ordinal);
        Assert.Contains("stream_connection_stop", SessionLifecycle.StopWakesUp[3], StringComparison.Ordinal);
    }

    /// <summary>And session.c still behaves the way this describes.</summary>
    [Fact]
    public void SessionStillDeclaresTheContract()
    {
        string? path = SessionLifecycleSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(
            SessionLifecycleSource.StopStillWakesEverything(core, SessionLifecycle.StopWakesUp),
            "chiaki_session_stop no longer performs every wake-up, in order");
        Assert.True(
            SessionLifecycleSource.JoinStillOnlyJoins(core),
            "chiaki_session_join now does more than join, so the ordering rule has changed");
        Assert.True(
            SessionLifecycleSource.FiniStillFreesWhatTheThreadUses(core),
            "chiaki_session_fini no longer matches what this contract is written against");
    }
}
