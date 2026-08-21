using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP249: two cleanups, and a literal that carries weight.
///
/// <see cref="TheFunctionReturnsSuccessWhileHoldingATimeout"/> carries the task: PP244 called the
/// literal return the only thing keeping stale codes in, and this is the path where the staleness
/// is on purpose.
/// </summary>
public class PunchCleanupTests
{
    /// <summary>
    /// THE PATH. Answered a request, then timed out waiting for the next - forgiven, fallen through,
    /// and the variable still holds the timeout while the function hands back success.
    /// </summary>
    [Fact]
    public void TheFunctionReturnsSuccessWhileHoldingATimeout()
    {
        Assert.True(PunchCleanup.TimeoutIsForgiven(alreadyAnswered: true));

        Assert.Equal(
            "CHIAKI_ERR_TIMEOUT",
            PunchCleanup.HeldCode(PunchEnding.Chosen, timedOutWaiting: true, alreadyAnswered: true));

        Assert.Equal("CHIAKI_ERR_SUCCESS", PunchCleanup.ReturnedCode(PunchEnding.Chosen));

        Assert.True(PunchCleanup.TheReturnDisagreesWithWhatIsHeld(
            PunchEnding.Chosen, timedOutWaiting: true, alreadyAnswered: true));
    }

    /// <summary>And that is the only path where the two disagree.</summary>
    [Fact]
    public void ItIsTheOnlyPathWhereTheyDisagree()
    {
        Assert.False(PunchCleanup.TheReturnDisagreesWithWhatIsHeld(
            PunchEnding.Chosen, timedOutWaiting: false, alreadyAnswered: true));

        Assert.False(PunchCleanup.TheReturnDisagreesWithWhatIsHeld(
            PunchEnding.Chosen, timedOutWaiting: true, alreadyAnswered: false));

        Assert.False(PunchCleanup.TheReturnDisagreesWithWhatIsHeld(
            PunchEnding.Failed, timedOutWaiting: true, alreadyAnswered: true));
    }

    /// <summary>An unforgiven timeout is a failure, so it never reaches the literal at all.</summary>
    [Fact]
    public void AnUnforgivenTimeoutIsSimplyAFailure()
    {
        Assert.False(PunchCleanup.TimeoutIsForgiven(alreadyAnswered: false));
        Assert.False(PunchCleanup.ReturnedCodeIsSuccess(PunchEnding.Failed));
    }

    /// <summary>
    /// The chosen socket is the one thing the two endings treat differently on purpose: handed over
    /// on the way out, closed on the way down.
    /// </summary>
    [Fact]
    public void TheChosenSocketIsSparedOnlyOnTheWayOut()
    {
        Assert.Equal(
            SocketFate.HandedOver, PunchCleanup.FateOf(PunchEnding.Chosen, open: true, chosen: true));

        Assert.Equal(
            SocketFate.Closed, PunchCleanup.FateOf(PunchEnding.Failed, open: true, chosen: true));

        // Everything else is closed either way.
        Assert.Equal(
            SocketFate.Closed, PunchCleanup.FateOf(PunchEnding.Chosen, open: true, chosen: false));
        Assert.Equal(
            SocketFate.Closed, PunchCleanup.FateOf(PunchEnding.Failed, open: true, chosen: false));
    }

    /// <summary>A socket that was never open is not closed twice.</summary>
    [Fact]
    public void AClosedSocketIsLeftAlone()
    {
        foreach (PunchEnding ending in Enum.GetValues<PunchEnding>())
        {
            Assert.Equal(
                SocketFate.Untouched, PunchCleanup.FateOf(ending, open: false, chosen: false));
        }
    }

    /// <summary>The three differences are named, so folding the two cleanups into one is visible.</summary>
    [Fact]
    public void TheThreeDifferencesAreNamed()
        => Assert.Equal(3, PunchCleanup.WhereTheTwoCleanupsDiffer.Count);

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheEndingIsStillTheCores()
    {
        string? file = PunchCleanupSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            PunchCleanupSource.TheTimeoutIsStillForgivenByFallingThrough(core),
            "the timeout is still forgiven by falling through");
        Assert.True(
            PunchCleanupSource.NothingClearsTheCodeBeforeTheReturn(core),
            "and nothing clears the code before the literal return");

        Assert.True(
            PunchCleanupSource.TheSuccessPathStillSparesTheChosenSocket(core),
            "the success path still spares the chosen socket");
        Assert.True(
            PunchCleanupSource.TheFailurePathStillClosesEverything(core),
            "and the failure path still closes everything");
        Assert.True(
            PunchCleanupSource.TheHandlesAreStillHandedOver(core),
            "the handles are still handed over by invalidating them");
        Assert.True(
            PunchCleanupSource.OnlyOneCleanupStillGuardsTheEventArray(core),
            "only one cleanup still guards the event array");
        Assert.True(
            PunchCleanupSource.TheOutputIsStillWrittenBeforeTheLastFailure(core),
            "and the output is still written before the last thing that can fail");
    }
}
