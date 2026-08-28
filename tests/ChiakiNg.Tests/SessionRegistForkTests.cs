using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP504, under PP340: the fork that decides where a session's registration keys come from.
///
/// The interesting outcome is the fourth: a wait that simply ran out, setting nothing, and letting
/// the flow request a session with a key it does not have.
/// </summary>
public class SessionRegistForkTests
{
    /// <summary>A local session resolves its address and uses the caller's keys.</summary>
    [Fact]
    public void ALocalSessionResolvesAndUsesTheCallersKeys()
    {
        SessionRegistOutcome outcome = SessionRegistFork.Run(SessionArm.Local);

        Assert.True(outcome.AddressResolved);
        Assert.True(outcome.KeysFromCaller);
        Assert.False(outcome.KeysFromConsole);
        Assert.True(outcome.HasRegistKey);
    }

    /// <summary>
    /// A PSN session resolves nothing and takes its keys from the console.
    ///
    /// The address arrives later, from the punch, at the moment the request is built - which is why
    /// this arm has no getaddrinfo and why copying the caller's keys here would be dead weight the
    /// callback overwrites.
    /// </summary>
    [Fact]
    public void APsnSessionResolvesNothingAndTakesTheConsolesKeys()
    {
        SessionRegistOutcome outcome = SessionRegistFork.Run(SessionArm.Psn);

        Assert.False(outcome.AddressResolved);
        Assert.False(outcome.KeysFromCaller);
        Assert.True(outcome.KeysFromConsole);
        Assert.True(outcome.HasRegistKey);
    }

    /// <summary>Both handled failures stop the session and name the same reason.</summary>
    [Theory]
    [InlineData(RegistWaitOutcome.Canceled)]
    [InlineData(RegistWaitOutcome.Failed)]
    public void TheTwoHandledFailuresStopWithAReason(RegistWaitOutcome wait)
    {
        SessionRegistOutcome outcome = SessionRegistFork.Run(SessionArm.Psn, wait: wait);

        Assert.True(outcome.Stops);
        Assert.Equal(SessionRegistFork.RegistFailedReason, outcome.QuitReason);
        Assert.False(outcome.HasRegistKey);
    }

    /// <summary>
    /// THE THIRD OUTCOME: the wait runs out, nothing is set, and the flow carries on with no key.
    ///
    /// No stop, no reason, and no key - which is the combination the two handled arms exist to
    /// avoid and which this one reaches by doing nothing at all.
    /// </summary>
    [Fact]
    public void ATimedOutWaitCarriesOnWithNoKeyAndNoReason()
    {
        SessionRegistOutcome outcome =
            SessionRegistFork.Run(SessionArm.Psn, wait: RegistWaitOutcome.TimedOut);

        Assert.False(outcome.Stops);
        Assert.Null(outcome.QuitReason);
        Assert.False(outcome.HasRegistKey);

        Assert.True(SessionRegistFork.ReachesTheRequestWithoutAKey(RegistWaitOutcome.TimedOut));
    }

    /// <summary>And it is the only outcome that reaches the request without one.</summary>
    [Fact]
    public void ItIsTheOnlyOutcomeThatReachesTheRequestEmptyHanded()
    {
        RegistWaitOutcome[] empty = [.. Enum.GetValues<RegistWaitOutcome>()
            .Where(SessionRegistFork.ReachesTheRequestWithoutAKey)];

        Assert.Equal([RegistWaitOutcome.TimedOut], empty);
    }

    /// <summary>
    /// An all-zero key measures zero, which is what makes the request field empty rather than
    /// malformed.
    ///
    /// The builder scans to the first NUL. A key of zeros has one at index zero, so the console is
    /// sent a well-formed request with nothing in that field - and refuses it for the wrong reason.
    /// </summary>
    [Fact]
    public void AnAllZeroKeyMeasuresZero()
    {
        Assert.Equal(0, SessionRegistFork.KeyLength(new byte[16]));
        Assert.Equal(3, SessionRegistFork.KeyLength([1, 2, 3, 0, 9, 9]));
        Assert.Equal(4, SessionRegistFork.KeyLength([1, 2, 3, 4]));
    }

    /// <summary>
    /// THE DRIFT CHECK: only the local arm copies the caller's keys, and the callback still writes
    /// both for a PSN session.
    /// </summary>
    [Fact]
    public void TheCStillForksThisWay()
    {
        if (SessionRegistForkSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);

        string init = Assert.IsType<string>(SessionRegistForkSource.InitBody(source));
        Assert.True(SessionRegistForkSource.OnlyTheLocalArmCopiesTheCallersKeys(init));

        string callback = Assert.IsType<string>(SessionRegistForkSource.RegistCallbackBody(source));
        Assert.True(SessionRegistForkSource.TheCallbackWritesBothKeys(callback));
        Assert.True(SessionRegistForkSource.BothFailureArmsStopTheSession(callback));
    }

    /// <summary>
    /// And the wait is still bounded with a CHECK_STOP after it and no test of its own result.
    ///
    /// The absence between the two is the third outcome: nothing between the wait returning and the
    /// check asks whether the registration actually happened.
    /// </summary>
    [Fact]
    public void TheWaitIsStillFollowedByACheckThatCannotSeeATimeout()
    {
        if (SessionRegistForkSource.Locate() is not { } path)
            return;

        Assert.True(SessionRegistForkSource.TheWaitIsBoundedAndFollowedByCheckStop(
            File.ReadAllText(path)));
    }
}
