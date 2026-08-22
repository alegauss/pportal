using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP257: the second err, and the second unlock.
///
/// <see cref="TwoFailuresAreReportedAsSuccess"/> carries the task, and the second of them is the
/// check that the console which joined is the one that was asked for.
/// </summary>
public class SessionStartTests
{
    /// <summary>
    /// THE SHADOW. Exactly two failures are lost, and they are the two declared after the second
    /// variable comes into scope.
    /// </summary>
    [Fact]
    public void TwoFailuresAreReportedAsSuccess()
    {
        Assert.Equal(
            [StartFailure.MemberIdNotHex, StartFailure.WrongConsole],
            SessionStart.Lost);

        foreach (StartFailure lost in SessionStart.Lost)
        {
            Assert.True(SessionStart.IsFailure(lost));
            Assert.Equal("CHIAKI_ERR_SUCCESS", SessionStart.Reported(lost));
        }
    }

    /// <summary>
    /// The worst of the two: a session that joined a different console than the one asked for is
    /// reported as started.
    /// </summary>
    [Fact]
    public void TheIdentityCheckIsOneOfThem()
    {
        Assert.True(SessionStart.IsFailure(StartFailure.WrongConsole));
        Assert.True(SessionStart.IsLost(StartFailure.WrongConsole));
    }

    /// <summary>
    /// Every failure declared BEFORE the shadow is reported properly - which is what makes the two
    /// after it a consequence of where the declaration sits rather than of anything else.
    /// </summary>
    [Theory]
    [InlineData(StartFailure.MemberFieldMissing)]
    [InlineData(StartFailure.MemberIdWrongLength)]
    [InlineData(StartFailure.CustomDataFieldMissing)]
    [InlineData(StartFailure.CustomDataWrongLength)]
    [InlineData(StartFailure.CustomDataUndecodable)]
    [InlineData(StartFailure.UnexpectedNotification)]
    public void EveryOtherFailureIsReported(StartFailure failure)
    {
        Assert.True(SessionStart.IsFailure(failure));
        Assert.False(SessionStart.IsLost(failure));
        Assert.Equal("CHIAKI_ERR_UNKNOWN", SessionStart.Reported(failure));
    }

    /// <summary>And a start with nothing wrong reports success for the right reason.</summary>
    [Fact]
    public void NothingWrongIsSuccess()
    {
        Assert.False(SessionStart.IsFailure(StartFailure.None));
        Assert.False(SessionStart.IsLost(StartFailure.None));
        Assert.Equal("CHIAKI_ERR_SUCCESS", SessionStart.Reported(StartFailure.None));
    }

    /// <summary>
    /// THE SECOND UNLOCK. The exit every working session takes releases the mutex twice; the one
    /// that breaks releases it once.
    /// </summary>
    [Fact]
    public void TheOrdinaryExitUnlocksTwice()
    {
        Assert.Equal(2, SessionStart.UnlocksAfterTheLoop(brokeOut: false));
        Assert.Equal(1, SessionStart.UnlocksAfterTheLoop(brokeOut: true));

        // Which is right only for the exit that still holds it.
        Assert.True(SessionStart.StillHeldAfterTheLoop(brokeOut: true));
        Assert.False(SessionStart.StillHeldAfterTheLoop(brokeOut: false));
    }

    /// <summary>The loop ends only when both notifications have arrived.</summary>
    [Fact]
    public void BothStatesAreNeeded()
    {
        var state = new HolepunchSessionState();
        Assert.False(SessionStart.Finished(state.Flags));

        state.Enter(SessionStateFlags.ConsoleJoined);
        Assert.False(SessionStart.Finished(state.Flags));

        state.Enter(SessionStateFlags.CustomData1Received);
        Assert.True(SessionStart.Finished(state.Flags));
    }

    /// <summary>And the two waits do not share the budget, which the core states itself.</summary>
    [Fact]
    public void TheTwoWaitsDoNotShareTheBudget()
    {
        Assert.False(SessionStart.SharesOneTimeout);
        Assert.Equal(30, SessionStart.TimeoutSeconds);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheStartIsStillTheCores()
    {
        string? file = SessionStartSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            SessionStartSource.TheInnerVariableIsStillDeclared(core),
            "a second variable of the same name is still declared inside the branch");
        Assert.True(
            SessionStartSource.TheTwoFailuresStillWriteTheInnerOne(core),
            "and the two failures after it still write it and break");
        Assert.True(
            SessionStartSource.TheEarlierFailuresStillReachTheOuterOne(core),
            "while the two before it still reach the outer one");

        Assert.True(
            SessionStartSource.TheMutexIsStillReleasedTwice(core),
            "the state mutex is still released inside the loop and again after it");
        Assert.True(
            SessionStartSource.TheLoopStillEndsOnBothStates(core),
            "the loop still ends on both states");

        Assert.True(
            SessionStartSource.TheUnsharedTimeoutIsStillStated(core),
            "the core still states the unshared timeout itself");
        Assert.True(SessionStartSource.TheLengthsAreStillThese(core), "and the lengths are still these");
    }
}
