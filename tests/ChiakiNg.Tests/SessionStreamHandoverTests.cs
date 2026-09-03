using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP28, the third and last join: the handover into the stream connection's run.
///
/// PP336 models what the run's RESULT means and PP371 that the disconnect reason can be null. What
/// neither owns is the handover - which lock is held across which call, and why the ecdh is built on
/// the line before the run rather than with the rest of the session's setup.
/// </summary>
public class SessionStreamHandoverTests
{
    private static string? Source()
        => SessionStreamHandover.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// The state mutex is held for everything except the run itself.
    ///
    /// Three of the seven steps hold it and the run is deliberately not one of them. Held across the
    /// run, the mutex would be taken for the length of a session - and ctrl's thread, the stop path
    /// and every event handler want it, so the session would be one nothing could stop.
    /// </summary>
    [Theory]
    [InlineData(HandoverStep.HandshakeKey, true)]
    [InlineData(HandoverStep.EcdhInit, true)]
    [InlineData(HandoverStep.Unlock, false)]
    [InlineData(HandoverStep.Run, false)]
    [InlineData(HandoverStep.Relock, true)]
    [InlineData(HandoverStep.UnlockAgain, false)]
    [InlineData(HandoverStep.EcdhFini, false)]
    public void TheLockIsHeldForEverythingButTheRun(HandoverStep step, bool held)
        => Assert.Equal(held, SessionStreamHandover.HoldsTheStateMutex(step));

    /// <summary>
    /// An exit taken before the run frees nothing, which is what the late init buys.
    ///
    /// The session thread has many exits above this point and not one of them frees an ecdh. That is
    /// correct only because there is not one yet: moving the init up beside the rest of the setup
    /// makes every one of those exits leak, and nothing in the C would say so.
    /// </summary>
    [Fact]
    public void AnExitBeforeTheRunHasNothingToFree()
        => Assert.False(SessionStreamHandover.AnEarlierExitMustFreeTheEcdh);

    /// <summary>The seven steps, in the order the C takes them.</summary>
    [Fact]
    public void TheOrderIsTheSevenStepsAndNoOther()
    {
        Assert.Equal(
            [
                HandoverStep.HandshakeKey,
                HandoverStep.EcdhInit,
                HandoverStep.Unlock,
                HandoverStep.Run,
                HandoverStep.Relock,
                HandoverStep.UnlockAgain,
                HandoverStep.EcdhFini,
            ],
            SessionStreamHandover.Order);
    }

    /// <summary>And session.c still takes them in it.</summary>
    [Fact]
    public void TheHandoverInTheCIsTheOneModelledHere()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            SessionStreamHandoverSource.TheHandoverIsInOrder(source),
            "session.c's handover into the stream connection is no longer key, ecdh, unlock, run, "
                + "lock, unlock, ecdh fini in that order");
    }

    /// <summary>
    /// The run has the unlock immediately before it and the lock immediately after.
    ///
    /// Stronger than the ordering, and the difference matters: a sequence check passes on a version
    /// that unlocked, did something else, and then ran. This is what says the mutex is released for
    /// exactly the run's duration.
    /// </summary>
    [Fact]
    public void NothingElseHappensWhileTheLockIsReleased()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            SessionStreamHandoverSource.TheRunIsBracketedByTheLock(source),
            "something now sits between the state mutex being released and the stream connection's "
                + "run, so the lock is released for longer than the run");
    }

    /// <summary>
    /// The ecdh is still built on the line before the run and freed after it, exactly once each.
    ///
    /// The count is half the claim. A second init or fini anywhere in the file would mean the object
    /// has a lifetime outside this span, and the earlier exits would be freeing - or leaking - one
    /// this model says does not exist yet.
    /// </summary>
    [Fact]
    public void TheEcdhExistsOnlyAcrossTheRun()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            SessionStreamHandoverSource.TheEcdhIsCreatedImmediatelyBeforeTheRun(source),
            "the session's ecdh is no longer created once before the run and freed once after it, "
                + "so the exits above it may now have something to release");
    }

    /// <summary>And it is freed with the lock released, not under it.</summary>
    [Fact]
    public void TheEcdhIsFreedWithTheLockReleased()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            SessionStreamHandoverSource.TheEcdhIsFreedOutsideTheLock(source),
            "chiaki_ecdh_fini now runs under the state mutex, which holds it for the length of a "
                + "free on a path where nothing else wants it");
    }
}
