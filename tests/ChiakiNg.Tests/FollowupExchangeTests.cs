using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP256, PP458: what PP256 found in the punch loop that PP238's model of the same loop did not say.
///
/// PP458 merged the two models, so the six assertions here that PP238 already made from the other
/// vocabulary are gone rather than restated - a failing receive returning WaitAgain, an extra response
/// being ignored, silence meaning two things, an unknown type and a wrong length both being fatal.
/// <see cref="PunchExchangeTests"/> has them, once.
///
/// What is left is PP256's own: the code the caller is told for each ending, the PP249 join that made
/// the timeout forgivable, and the source predicates about this function's logs. Those were never
/// duplicated, which is why they are the file that survives.
/// </summary>
public class FollowupExchangeTests
{
    /// <summary>
    /// PP256's finding, restated once in the merged vocabulary: three failures end the loop and the
    /// fourth goes round again, so a condition that persists never ends by its own step.
    ///
    /// PP457 bounded that above the loop, which is why this says "by its own step" - the bound is not
    /// a step and <see cref="PunchExchange.APersistentFailureEnds"/> still answers false.
    /// </summary>
    [Fact]
    public void AFailingReceiveHasNoExitBehindItsOwnStep()
    {
        PunchStep failed = PunchExchange.Next(
            timedOut: false, answeredAny: false, received: -1, messageType: 0);

        Assert.Equal(PunchStep.WaitAgain, failed);
        Assert.False(PunchExchange.Leaves(failed));
        Assert.False(PunchExchange.APersistentFailureEnds(failed));

        Assert.Equal(
            new[] { PunchStep.Answer, PunchStep.Ignore, PunchStep.WaitAgain },
            PunchExchange.Continues.OrderBy(s => s.ToString(), StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// THE ORDINARY ENDING, and the code the caller is told for it - which is what PP256 carried and
    /// PP238 did not.
    /// </summary>
    [Fact]
    public void EachEndingHasTheCodeTheCallerIsTold()
    {
        PunchStep after = PunchExchange.Next(timedOut: true, answeredAny: true, 0, 0);
        PunchStep before = PunchExchange.Next(timedOut: true, answeredAny: false, 0, 0);

        Assert.Equal("CHIAKI_ERR_SUCCESS", PunchExchange.CodeFor(after));
        Assert.Equal("CHIAKI_ERR_TIMEOUT", PunchExchange.CodeFor(before));
        Assert.Equal("CHIAKI_ERR_NETWORK or CHIAKI_ERR_UNKNOWN", PunchExchange.CodeFor(PunchStep.Fatal));

        // And a step that stays in the loop is told nothing, because the caller never sees it.
        Assert.Equal("", PunchExchange.CodeFor(PunchStep.WaitAgain));
    }

    /// <summary>
    /// The PP249 join: the caller forgives the timeout only when it had already answered a request of
    /// its own.
    /// </summary>
    [Fact]
    public void TheCallerForgivesTheTimeoutOnlyAfterAnsweringOneItself()
    {
        PunchStep before = PunchExchange.Next(timedOut: true, answeredAny: false, 0, 0);

        Assert.True(PunchExchange.CallerForgives(before, callerAlreadyAnswered: true));
        Assert.False(PunchExchange.CallerForgives(before, callerAlreadyAnswered: false));

        // Which is exactly the path PP249 found holding a timeout while returning success.
        Assert.True(PunchCleanup.TheReturnDisagreesWithWhatIsHeld(
            PunchEnding.Chosen, timedOutWaiting: true, alreadyAnswered: true));
    }

    /// <summary>
    /// Its named lines belong to the DEFENSIBLE list, not the wrong-call one. PP238 settled that, and
    /// PP256 tried to move it and was wrong to - this is what keeps it where it belongs.
    /// </summary>
    [Fact]
    public void ItsMessagesNameTheOperationNotTheWrongCall()
    {
        Assert.Contains(
            PunchExchangeSource.FunctionName, MisnamedLogs.NamesTheOperationNotTheFunction);

        Assert.DoesNotContain(
            MisnamedLogs.All,
            m => string.Equals(
                m.Function, PunchExchangeSource.FunctionName, StringComparison.Ordinal));

        Assert.Equal(3, MisnamedLogs.All.Count);
    }

    /// <summary>PP256's source predicates about this function's logs, on the merged reader.</summary>
    [Fact]
    public void ItsLogLinesAreStillSplitTheSameWay()
    {
        if (PunchExchangeSource.Locate() is not { } file)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            PunchExchangeSource.FourNameTheOperationAndOneNamesNothing(core),
            "four of its five logs still name the operation");
        Assert.True(
            PunchExchangeSource.TheUnnamedLineIsStillThere(core),
            "and the fifth still names nothing at all");
        Assert.True(
            PunchExchangeSource.TheRequestIsStillTheProbesSize(core),
            "the request is still the probe's size");
        Assert.True(
            PunchExchangeSource.TheLoopIsStillUnconditional(core),
            "the loop still has no condition of its own");
    }

    /// <summary>
    /// PP458's guard: exactly one file reads this loop out of the C.
    ///
    /// Two did, for as long as neither task knew about the other, and the only thing that surfaced it
    /// was PP457's fix happening to touch both. A second name here is a third model starting up.
    ///
    /// It counts files naming the DEFINITION, not the function - five mention the name in prose or in
    /// the list of decided log prefixes, and a guard on that reported all five.
    /// </summary>
    [Fact]
    public void ExactlyOneFileReadsTheLoop()
    {
        IReadOnlyList<string> files = PunchExchangeSource.FilesReadingTheLoop();
        if (files.Count == 0)
            return;

        Assert.Equal(new[] { "PunchExchange.cs" }, files.ToArray());
    }
}
