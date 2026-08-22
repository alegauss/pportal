using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP258: the one wait with no bound.
///
/// <see cref="TheOnlyUnendableWaitIsTheWebSocketsIsAlsoTheOneThatFails"/> carries the task: the wait
/// nothing can end is the wait whose failure is never announced.
/// </summary>
public class SessionCreateTests
{
    /// <summary>
    /// THE FINDING. One wait is neither bounded nor cancellable, and it is the one the console can
    /// simply refuse.
    /// </summary>
    [Fact]
    public void TheOnlyUnendableWaitIsTheWebSocketsIsAlsoTheOneThatFails()
    {
        Assert.Equal([HolepunchWait.WebSocketOpen], SessionCreate.Unendable);

        Assert.False(SessionCreate.IsBounded(HolepunchWait.WebSocketOpen));
        Assert.False(SessionCreate.CanBeCancelled(HolepunchWait.WebSocketOpen));

        // And the failure it would have to observe is never signalled.
        Assert.False(SessionCreate.SignalsTheWaiter(WebSocketWaitOutcome.NeverSignalled));
        Assert.Equal("", SessionCreate.WhatTheWaiterSees(WebSocketWaitOutcome.NeverSignalled));
    }

    /// <summary>Every other wait has both a deadline and a way to be stopped.</summary>
    [Theory]
    [InlineData(HolepunchWait.SessionCreated)]
    [InlineData(HolepunchWait.SessionStarted)]
    [InlineData(HolepunchWait.GatewayDiscovery)]
    public void EveryOtherWaitHasBoth(HolepunchWait wait)
    {
        Assert.True(SessionCreate.IsBounded(wait));
        Assert.True(SessionCreate.CanBeCancelled(wait));
        Assert.False(SessionCreate.NothingCanEndIt(wait));
    }

    /// <summary>A connection that opened is signalled, and that is the only outcome that is.</summary>
    [Fact]
    public void OnlyOpeningIsAnnounced()
    {
        Assert.True(SessionCreate.SignalsTheWaiter(WebSocketWaitOutcome.Opened));
        Assert.Equal("SESSION_STATE_WS_OPEN", SessionCreate.WhatTheWaiterSees(WebSocketWaitOutcome.Opened));

        // The failure clears a different flag entirely - one the waiter does not watch.
        Assert.NotEqual(SessionCreate.WatchedFor, SessionCreate.ClearedInstead);
    }

    /// <summary>Four cancellation checks, none of them where one would matter.</summary>
    [Fact]
    public void FourChecksAndNoneWhereItWouldMatter()
    {
        Assert.Equal(4, SessionCreate.CancelChecks);
        Assert.False(SessionCreate.CanBeCancelled(HolepunchWait.WebSocketOpen));
    }

    /// <summary>The shipped build has no asserts, so the check beside the wait is not one.</summary>
    [Fact]
    public void TheShippedBuildHasNoAsserts()
        => Assert.True(SessionCreate.AssertsAreCompiledOut);

    /// <summary>And the two notification waits still do not share their budget.</summary>
    [Fact]
    public void TheTwoWaitsDoNotShareTheBudget()
    {
        Assert.False(SessionCreate.SharesOneTimeout);
        Assert.Equal(SessionStart.TimeoutSeconds, SessionCreate.TimeoutSeconds);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheCreateIsStillTheCores()
    {
        string? file = SessionCreateSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        // Two bounded waits in the whole file, and one that is not.
        Assert.Equal((2, 1), SessionCreateSource.HowManyWaits(core));

        Assert.True(
            SessionCreateSource.TheUnboundedOneIsStillTheWebSocket(core),
            "the unbounded one is still the websocket's");
        Assert.True(
            SessionCreateSource.NoCancellationIsStillInsideTheWait(core),
            "and no cancellation is still inside it");

        Assert.Equal(
            SessionCreate.CancelChecks, SessionCreateSource.HowManyCancelChecks(core));

        Assert.True(
            SessionCreateSource.TheResultIsStillOnlyAsserted(core),
            "the wait's result is still inspected only by an assert");

        Assert.True(
            SessionCreateSource.TheThreadStillSignalsOnlyOnSuccess(core),
            "the thread still signals only after connecting");
        Assert.True(
            SessionCreateSource.TheCleanupStillClearsTheOtherFlag(core),
            "and its cleanup still clears the flag the waiter does not read");

        Assert.True(
            SessionCreateSource.TheCommentIsStillThereTwice(core),
            "the unshared-timeout comment is still there twice, one of them mistyped");
    }
}
