using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP238: the loop that answers punch requests, and the two things about it that surprise.
///
/// <see cref="TheOnlySuccessIsAnAbsence"/> is the shape of the whole function: nothing that arrives
/// ever means success, and the caller is told "done" by traffic stopping.
/// </summary>
public class PunchExchangeTests
{
    private const int Whole = PunchExchange.RequestLength;

    private static PunchStep Step(
        bool timedOut = false, bool answeredAny = false, int received = Whole,
        uint type = PunchResponse.RequestType)
        => PunchExchange.Next(timedOut, answeredAny, received, type);

    /// <summary>A request is answered, and the loop goes back to waiting.</summary>
    [Fact]
    public void ARequestIsAnswered()
    {
        PunchStep step = Step();

        Assert.Equal(PunchStep.Answer, step);
        Assert.False(PunchExchange.Leaves(step));
    }

    /// <summary>
    /// THE SHAPE. Nothing that arrives is ever success - the only success is a timeout after at
    /// least one request was answered.
    /// </summary>
    [Fact]
    public void TheOnlySuccessIsAnAbsence()
    {
        Assert.Equal(PunchStep.Done, Step(timedOut: true, answeredAny: true));
        Assert.True(PunchExchange.IsSuccess(PunchStep.Done));

        // Everything a datagram can be, and none of them succeed.
        foreach (PunchStep step in new[]
        {
            Step(),
            Step(type: PunchResponse.ResponseType),
            Step(received: 40),
            Step(received: -1),
        })
        {
            Assert.False(PunchExchange.IsSuccess(step));
        }
    }

    /// <summary>And a timeout with nothing ever answered is the timeout, not success.</summary>
    [Fact]
    public void ATimeoutWithNothingAnsweredIsATimeout()
    {
        PunchStep step = Step(timedOut: true, answeredAny: false);

        Assert.Equal(PunchStep.TimedOut, step);
        Assert.True(PunchExchange.Leaves(step));
        Assert.False(PunchExchange.IsSuccess(step));
    }

    /// <summary>
    /// A receive that failed costs nothing: the loop waits again, and the wait it re-enters is
    /// given the whole timeout - the same shape PP212 measured in the notification wait.
    /// </summary>
    [Fact]
    public void AFailedReceiveCostsNothing()
    {
        PunchStep step = Step(received: -1);

        Assert.Equal(PunchStep.WaitAgain, step);
        Assert.False(PunchExchange.Leaves(step));
    }

    /// <summary>An extra response where a request was expected is ordinary and waited past.</summary>
    [Fact]
    public void AnExtraResponseIsIgnored()
    {
        PunchStep step = Step(type: PunchResponse.ResponseType);

        Assert.Equal(PunchStep.Ignore, step);
        Assert.False(PunchExchange.Leaves(step));
    }

    /// <summary>The wrong size is fatal, where the wrong type of a known kind was not.</summary>
    [Fact]
    public void TheWrongSizeIsFatal()
    {
        Assert.Equal(PunchStep.Fatal, Step(received: Whole - 1));
        Assert.Equal(PunchStep.Fatal, Step(received: 0));
        Assert.True(PunchExchange.Leaves(PunchStep.Fatal));
    }

    /// <summary>And so is a type this does not know, which is the third treatment.</summary>
    [Fact]
    public void AnUnknownTypeIsFatal()
        => Assert.Equal(PunchStep.Fatal, Step(type: 0x11223344));

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheLoopIsStillTheCores()
    {
        string? file = PunchExchangeSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            PunchExchangeSource.SuccessIsStillATimeout(core),
            "the only success is still a timeout with something answered");
        Assert.True(
            PunchExchangeSource.AFailedReceiveStillCostsNothing(core),
            "and a failed receive still continues");
        Assert.True(
            PunchExchangeSource.ThreeTreatmentsForABadDatagram(core),
            "with three treatments for a bad datagram");
    }
}
