using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP503, under PP340: the punch's two events, and the three things they can leave on screen.
///
/// A balanced pair would have two outcomes. This has three, and the one in the middle is the one a
/// try/finally would delete.
/// </summary>
public class PunchProgressTests
{
    /// <summary>A punch that works narrates both, in order, and ends finished.</summary>
    [Fact]
    public void AGoodPunchRaisesBothInOrder()
    {
        PunchProgressOutcome outcome = PunchProgress.Run(offerSucceeds: true, punchSucceeds: true);

        Assert.Equal(
            [PunchProgressEvent.Started, PunchProgressEvent.Finished], outcome.Events);
        Assert.Equal(PsnConnectState.DataConnectionFinished, outcome.EndState);
        Assert.True(outcome.DataSocketTaken);
    }

    /// <summary>
    /// THE CLAIM: a failed punch raises the start and never the finish, so the screen is left in
    /// DataConnectionStart.
    ///
    /// What clears that state is the session quitting. Nothing in this block sends a finish, and a
    /// managed flow that added one in a finally would be reporting a data connection that never
    /// opened.
    /// </summary>
    [Fact]
    public void AFailedPunchLeavesTheScreenMidPunch()
    {
        PunchProgressOutcome outcome = PunchProgress.Run(offerSucceeds: true, punchSucceeds: false);

        Assert.Equal([PunchProgressEvent.Started], outcome.Events);
        Assert.Equal(PsnConnectState.DataConnectionStart, outcome.EndState);
        Assert.False(outcome.DataSocketTaken);
    }

    /// <summary>
    /// And a failed offer narrates NOTHING, because the start event sits below its guard.
    ///
    /// The third outcome, and the one that is easy to assume away: on screen it looks like a
    /// connect that never reached the punch, which is exactly what it is.
    /// </summary>
    [Fact]
    public void AFailedOfferRaisesNeitherEvent()
    {
        PunchProgressOutcome outcome = PunchProgress.Run(offerSucceeds: false, punchSucceeds: true);

        Assert.Empty(outcome.Events);
        Assert.Equal(PsnConnectState.Unchanged, outcome.EndState);
        Assert.False(outcome.DataSocketTaken);
    }

    /// <summary>
    /// Three inputs, three distinct end states - which is what "not a pair" means in one line.
    ///
    /// A balanced pair would give two, with the failure and the success agreeing.
    /// </summary>
    [Fact]
    public void TheThreeRunsEndInThreeDifferentStates()
    {
        PsnConnectState[] states =
        [
            PunchProgress.EndStateFor(offerSucceeds: false, punchSucceeds: false),
            PunchProgress.EndStateFor(offerSucceeds: true, punchSucceeds: false),
            PunchProgress.EndStateFor(offerSucceeds: true, punchSucceeds: true),
        ];

        Assert.Equal(3, states.Distinct().Count());
    }

    /// <summary>The socket is taken before the finish, so "finished" means it is in hand.</summary>
    [Fact]
    public void FinishedMeansTheSocketWasTakenToo()
    {
        foreach ((bool offer, bool punch) in new[] { (false, false), (false, true), (true, false) })
        {
            PunchProgressOutcome outcome = PunchProgress.Run(offer, punch);

            Assert.DoesNotContain(PunchProgressEvent.Finished, outcome.Events);
            Assert.False(outcome.DataSocketTaken);
        }
    }

    /// <summary>
    /// THE DRIFT CHECK: the C still straddles the punch with the two events and still leaves
    /// without the finish when the punch fails.
    /// </summary>
    [Fact]
    public void TheCStillRaisesThemThisWay()
    {
        if (PunchProgressSource.LocateSession() is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.True(PunchProgressSource.TheEventsStillStraddleThePunch(source));
        Assert.True(PunchProgressSource.AFailedPunchSendsNoFinish(source));
        Assert.True(PunchProgressSource.AFailedOfferSendsNeither(source));
    }

    /// <summary>
    /// And the client still maps both onto the two states, which is what makes the asymmetry
    /// visible rather than internal.
    /// </summary>
    [Fact]
    public void TheClientStillMapsBothEvents()
    {
        if (PunchProgressSource.LocateBackend() is not { } path)
            return;

        Assert.True(PunchProgressSource.TheClientMapsBothEvents(File.ReadAllText(path)));
    }
}
