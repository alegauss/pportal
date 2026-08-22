using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP256: the loop a receive error never leaves.
///
/// <see cref="AFailingReceiveHasNoExitBehindIt"/> carries the task: every other thing that can go
/// wrong ends the loop, and the one that repeats does not.
/// </summary>
public class FollowupExchangeTests
{
    /// <summary>
    /// THE FINDING. Three failures end the loop; the fourth goes round again, and a condition that
    /// persists therefore never ends.
    /// </summary>
    [Fact]
    public void AFailingReceiveHasNoExitBehindIt()
    {
        FollowupStep failed = FollowupExchange.Next(
            readable: true, received: false, receiveFailed: true, length: 0, messageType: 0);

        Assert.Equal(FollowupStep.Retry, failed);
        Assert.False(FollowupExchange.Leaves(failed));
        Assert.False(FollowupExchange.APersistentFailureEnds(failed));

        // While the other things that go wrong all do end it.
        Assert.True(FollowupExchange.Leaves(FollowupStep.Fatal));
        Assert.True(FollowupExchange.Leaves(FollowupStep.TimedOut));
        Assert.True(FollowupExchange.Leaves(FollowupStep.Done));
    }

    /// <summary>Three steps continue, and one of them is a failure - which is the point.</summary>
    [Fact]
    public void ThreeStepsGoRoundAgainAndOneIsAFailure()
    {
        Assert.Equal(
            [FollowupStep.Answer, FollowupStep.Ignore, FollowupStep.Retry],
            [.. FollowupExchange.Continues.OrderBy(s => s.ToString(), StringComparer.Ordinal)]);

        // Two of them are progress. The third is not.
        Assert.False(FollowupExchange.APersistentFailureEnds(FollowupStep.Retry));
    }

    /// <summary>A request is answered and the loop carries on.</summary>
    [Fact]
    public void ARequestIsAnsweredAndTheLoopCarriesOn()
    {
        FollowupStep step = FollowupExchange.Next(
            true, received: false, receiveFailed: false, 88, PunchProbe.RequestType);

        Assert.Equal(FollowupStep.Answer, step);
        Assert.False(FollowupExchange.Leaves(step));
    }

    /// <summary>An extra response is dropped rather than answered.</summary>
    [Fact]
    public void AnExtraResponseIsDropped()
        => Assert.Equal(
            FollowupStep.Ignore,
            FollowupExchange.Next(true, true, false, 88, PunchResponse.ResponseType));

    /// <summary>
    /// THE ORDINARY ENDING. Silence after something is success; silence after nothing is the
    /// timeout PP249 measured the caller forgiving.
    /// </summary>
    [Fact]
    public void SilenceMeansTwoDifferentThings()
    {
        FollowupStep after = FollowupExchange.Next(readable: false, received: true, false, 0, 0);
        FollowupStep before = FollowupExchange.Next(readable: false, received: false, false, 0, 0);

        Assert.Equal(FollowupStep.Done, after);
        Assert.Equal("CHIAKI_ERR_SUCCESS", FollowupExchange.CodeFor(after));

        Assert.Equal(FollowupStep.TimedOut, before);
        Assert.Equal("CHIAKI_ERR_TIMEOUT", FollowupExchange.CodeFor(before));

        // And the caller forgives that one only when it had answered a request of its own.
        Assert.True(FollowupExchange.CallerForgives(before, callerAlreadyAnswered: true));
        Assert.False(FollowupExchange.CallerForgives(before, callerAlreadyAnswered: false));

        // Which is exactly the path PP249 found holding a timeout while returning success.
        Assert.True(PunchCleanup.TheReturnDisagreesWithWhatIsHeld(
            PunchEnding.Chosen, timedOutWaiting: true, alreadyAnswered: true));
    }

    /// <summary>Anything that is not a request or a response ends the punch.</summary>
    [Fact]
    public void AnUnknownMessageEndsThePunch()
    {
        Assert.Equal(FollowupStep.Fatal, FollowupExchange.Next(true, true, false, 88, 0x09000000));

        // As does a packet of the wrong length, unlike the receive failure beside it.
        Assert.Equal(FollowupStep.Fatal, FollowupExchange.Next(true, true, false, 87, PunchProbe.RequestType));
    }

    /// <summary>
    /// Its three named lines belong to the DEFENSIBLE list, not the wrong-call one. PP238 settled
    /// that, and PP256 tried to move it and was wrong to - this is what keeps it where it belongs.
    /// </summary>
    [Fact]
    public void ItsMessagesNameTheOperationNotTheWrongCall()
    {
        Assert.Contains(
            "receive_request_send_response_ps", MisnamedLogs.NamesTheOperationNotTheFunction);

        Assert.DoesNotContain(
            MisnamedLogs.All,
            m => string.Equals(
                m.Function, "receive_request_send_response_ps", StringComparison.Ordinal));

        Assert.Equal(3, MisnamedLogs.All.Count);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheExchangeIsStillTheCores()
    {
        string? file = FollowupExchangeSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            FollowupExchangeSource.TheLoopIsStillUnconditional(core),
            "the loop still has no condition of its own");
        Assert.True(
            FollowupExchangeSource.AFailedReceiveStillContinues(core),
            "a failed receive still continues rather than leaving");
        Assert.True(
            FollowupExchangeSource.TheWrongSizeStillEndsIt(core),
            "while the wrong-size packet beside it still ends the punch");

        Assert.True(
            FollowupExchangeSource.TheTimeoutIsStillSuccessAfterAnything(core),
            "a timeout is still success once something has been heard");
        Assert.True(
            FollowupExchangeSource.ThreeNameTheOperationAndOneNamesNothing(core),
            "three of its four logs still name the operation");
        Assert.True(
            FollowupExchangeSource.TheUnnamedLineIsStillThere(core),
            "and the fourth still names nothing at all");

        Assert.True(
            FollowupExchangeSource.TheRequestIsStillTheProbesSize(core),
            "the request is still the probe's size");
    }
}
