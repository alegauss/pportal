using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP245: the wait, and a message that says the opposite of what happened.
///
/// <see cref="TheMessageIsFalseExactlyWhereItAppears"/> carries the task: the branch that reports no
/// socket having data is reached only after a socket had data.
/// </summary>
public class CandidateWaitTests
{
    /// <summary>
    /// Two windows, one field each - so a port cannot fold them into a single microsecond value
    /// without changing what the core writes.
    /// </summary>
    [Fact]
    public void TheTwoWindowsUseOneFieldEach()
    {
        Assert.Equal((0, 500_000), CandidateWait.Window(connecting: false));
        Assert.Equal((5, 0), CandidateWait.Window(connecting: true));
    }

    /// <summary>
    /// The float arithmetic lands on the window with nothing to truncate - which is true of this
    /// constant and would not be of most others.
    /// </summary>
    [Fact]
    public void TheShortWindowIsExact()
    {
        Assert.True(CandidateWait.ShortWindowIsExact());

        // And the microsecond field stays inside a second, which is what makes the pair legal.
        Assert.True(CandidateWait.ShortWindowUs < CandidateWait.SecondUs);
    }

    /// <summary>
    /// The escalation: short windows until something answers, then one long one, then unreachable.
    /// The first answer stops retrying even with retries left.
    /// </summary>
    [Fact]
    public void TheFirstAnswerStopsRetryingEvenWithTriesLeft()
    {
        Assert.Equal(
            WaitStep.Retry,
            CandidateWait.Next(candidateChosen: false, retries: 0, anyAnswer: false, connecting: false));

        // Nineteen rounds in and still nothing - still retrying.
        Assert.Equal(
            WaitStep.Retry,
            CandidateWait.Next(false, CandidateWait.Tries - 1, anyAnswer: false, connecting: false));

        // One answer, and the retries stop dead whatever is left of them.
        Assert.Equal(
            WaitStep.Connect,
            CandidateWait.Next(false, retries: 0, anyAnswer: true, connecting: false));
    }

    /// <summary>And what the two ways of running out look like.</summary>
    [Fact]
    public void BothWaysOfRunningOutEndTheSame()
    {
        // Retries spent, nothing ever answered.
        Assert.Equal(
            WaitStep.Unreachable,
            CandidateWait.Next(false, CandidateWait.Tries, anyAnswer: false, connecting: false));

        // Something answered, the long window was used, and still no candidate.
        Assert.Equal(
            WaitStep.Unreachable,
            CandidateWait.Next(false, retries: 0, anyAnswer: true, connecting: true));

        // A chosen candidate leaves before any of that.
        Assert.Equal(
            WaitStep.Done,
            CandidateWait.Next(candidateChosen: true, retries: 99, anyAnswer: false, connecting: false));
    }

    /// <summary>The whole budget, as a number rather than as "a while".</summary>
    [Fact]
    public void TheBudgetIsFifteenSeconds()
        => Assert.Equal(15 * CandidateWait.SecondUs, CandidateWait.Budget());

    /// <summary>
    /// THE MESSAGE. It fires only for a socket the ladder does not recognise, and only with port
    /// guessing on - and in that case a socket had data.
    /// </summary>
    [Fact]
    public void TheMessageIsFalseExactlyWhereItAppears()
    {
        Assert.True(CandidateWait.BecomesNoSocketHasData(ReadySocket.Unrecognised, portGuessing: true));
        Assert.False(CandidateWait.MessageIsAccurate(ReadySocket.Unrecognised, portGuessing: true));

        // Every other socket reaches the read, and the message is not printed at all.
        foreach (ReadySocket ready in Enum.GetValues<ReadySocket>())
        {
            if (ready == ReadySocket.Unrecognised)
                continue;

            Assert.False(CandidateWait.BecomesNoSocketHasData(ready, portGuessing: true));
            Assert.True(CandidateWait.MessageIsAccurate(ready, portGuessing: true));
        }
    }

    /// <summary>
    /// And the asymmetry: with port guessing off the ladder never looks for the third kind, so an
    /// unrecognised socket is carried forward rather than caught.
    /// </summary>
    [Fact]
    public void WithoutPortGuessingTheUnrecognisedSocketIsCarriedForward()
    {
        Assert.False(CandidateWait.BecomesNoSocketHasData(ReadySocket.Unrecognised, portGuessing: false));

        // Carried forward, and read with the IPv6 length - the ladder's default.
        Assert.Equal(28, CandidateWait.AddressLengthFor(ReadySocket.Unrecognised));
        Assert.Equal(28, CandidateWait.AddressLengthFor(ReadySocket.Ipv6));
        Assert.Equal(16, CandidateWait.AddressLengthFor(ReadySocket.Ipv4));
        Assert.Equal(16, CandidateWait.AddressLengthFor(ReadySocket.Guessed));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheWaitIsStillTheCores()
    {
        string? file = CandidateWaitSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(CandidateWaitSource.TheConstantsAreStillThese(core), "the constants");
        Assert.True(
            CandidateWaitSource.TheTwoWindowsStillUseOneFieldEach(core), "one field each");
        Assert.True(
            CandidateWaitSource.RetryingStillStopsAtTheFirstAnswer(core),
            "retrying still stops at the first answer");
        Assert.True(
            CandidateWaitSource.RetriesStillDiscardSendFailures(core),
            "and retries still discard send failures");
        Assert.True(
            CandidateWaitSource.TheUnrecognisedSocketStillBecomesThatMessage(core),
            "the unrecognised socket still becomes that message");
        Assert.True(
            CandidateWaitSource.TheThirdKindIsStillOnlyLookedForWhenGuessing(core),
            "which is still only looked for when guessing");
        Assert.True(
            CandidateWaitSource.TheSameFailureIsStillWrittenTwice(core),
            "and the setsockopt failure still written twice, one of them unnamed");
    }
}
