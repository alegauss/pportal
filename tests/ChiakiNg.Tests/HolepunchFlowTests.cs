using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP460, PP340: the order the nine holepunch calls run in, and what a failure at each does.
///
/// PP429 wrote the nine down as an interface, in FILE order, and said so. PP340's section now says
/// what is left after the four socket tasks: "not I/O but sequence". The two assertions worth having
/// are that file order is not execution order - the finis appear first and run last - and that only
/// four of the nine can report a failure at all, of which exactly one is tested by nothing.
/// </summary>
public class HolepunchFlowTests
{
    private static string? Session()
    {
        string? path = HolepunchFlow.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// FILE ORDER IS NOT EXECUTION ORDER, and the finis are the whole difference.
    ///
    /// PP429's list is the file's. Taken as a sequence it releases the session before punching
    /// anything, so the two lists are held apart deliberately rather than one deriving from the other.
    /// </summary>
    [Fact]
    public void TheFinisAppearFirstAndRunLast()
    {
        if (Session() is not { } source)
            return;

        Assert.True(
            HolepunchFlow.TheFinisStillComeFirstInTheFile(source),
            "the finis no longer precede every other call site, so the two orders may have converged");

        // And they are not in the execution order at all, because they are not a step of it.
        Assert.DoesNotContain(HolepunchStep.Fini, HolepunchFlow.ExecutionOrder);

        // PP429 counts nine call sites; the sequence has seven, and the two finis are the gap.
        Assert.Equal(HolepunchSeam.Count, HolepunchFlow.ExecutionOrder.Count + 2);
    }

    /// <summary>
    /// The seven in-line steps appear in the file in the order they run - so for everything except the
    /// finis, the two orders agree.
    /// </summary>
    [Fact]
    public void TheSevenInLineStepsAppearInTheOrderTheyRun()
    {
        if (Session() is not { } source)
            return;

        Assert.Equal(HolepunchFlow.ExecutionOrder.ToArray(), HolepunchFlow.InFileOrder(source).ToArray());
    }

    /// <summary>
    /// ONLY FOUR OF THE NINE CAN REPORT A FAILURE. The other five hand back a struct, an address
    /// written into the caller's buffer, or a port, with nothing reserved for going wrong - so asking
    /// whether they are checked is not a question.
    /// </summary>
    [Fact]
    public void OnlyFourStepsCanReportAFailureAtAll()
    {
        HolepunchStep[] canFail =
            [.. Enum.GetValues<HolepunchStep>().Where(HolepunchFlow.CanReportFailure)];

        Assert.Equal(
            new[]
            {
                HolepunchStep.CtrlSocket,
                HolepunchStep.CreateOffer,
                HolepunchStep.PunchHole,
                HolepunchStep.DataSocket,
            },
            canFail);

        foreach (HolepunchStep step in new[]
        {
            HolepunchStep.RegistInfo,
            HolepunchStep.SelectedAddress,
            HolepunchStep.CtrlPort,
            HolepunchStep.Fini,
        })
        {
            Assert.Equal(HolepunchGuard.NoFailureToReport, HolepunchFlow.GuardFor(step));
        }
    }

    /// <summary>
    /// The two error-returning steps quit, which is PP339's fix - and the second of them was found by
    /// the check written for the first.
    /// </summary>
    [Fact]
    public void BothErrorReturningStepsQuit()
    {
        if (Session() is not { } source)
            return;

        Assert.Equal(HolepunchGuard.QuitsToCtrlTeardown, HolepunchFlow.GuardFor(HolepunchStep.CreateOffer));
        Assert.Equal(HolepunchGuard.QuitsToCtrlTeardown, HolepunchFlow.GuardFor(HolepunchStep.PunchHole));

        Assert.True(
            HolepunchFlow.BothErrorStepsStillQuit(source),
            "one of the two error-returning steps stopped quitting, which is the CHECK_STOP PP339 "
                + "replaced coming back");
    }

    /// <summary>
    /// The ctrl socket is not tested either - what it FEEDS is. PP339 made that a QUIT after it had
    /// carried on with rudp NULL and reported the failure as "no address answered".
    /// </summary>
    [Fact]
    public void TheCtrlSocketIsCaughtByTheRudpInitItFeeds()
    {
        if (Session() is not { } source)
            return;

        Assert.Equal(HolepunchGuard.CaughtByWhatItFeeds, HolepunchFlow.GuardFor(HolepunchStep.CtrlSocket));
        Assert.True(HolepunchFlow.TheCtrlSocketIsStillCaughtByRudpInit(source));
    }

    /// <summary>
    /// THE FINDING: the data socket is tested by nothing, and it is the only one.
    ///
    /// Recorded rather than repaired - the fix is a change to lib/ and is filed on its own. The
    /// asymmetry is the argument: the same family already cost two fixes in PP339, and the pointer
    /// beside this one has a guard.
    /// </summary>
    [Fact]
    public void TheDataSocketIsTheOnlyStepNothingChecks()
    {
        Assert.Equal(new[] { HolepunchStep.DataSocket }, HolepunchFlow.Unguarded.ToArray());

        if (Session() is not { } source)
            return;

        Assert.True(
            HolepunchFlow.TheDataSocketIsStillUnchecked(source),
            "the data socket has grown a check, so this model is behind the C and the filed fix has "
                + "already landed");
    }

    /// <summary>
    /// And the holepunch event pair does not survive a failed punch: the started event goes out, the
    /// quit skips the finished one.
    /// </summary>
    [Fact]
    public void AFailedPunchLeavesTheEventPairOpen()
    {
        if (Session() is not { } source)
            return;

        Assert.True(HolepunchFlow.AFailedPunchSendsNoFinishedEvent(source));
    }

    /// <summary>Every step names the C function it calls, and the two sockets name the same one.</summary>
    [Fact]
    public void EveryStepNamesItsCallee()
    {
        Assert.Equal(
            HolepunchFlow.CalleeFor(HolepunchStep.CtrlSocket),
            HolepunchFlow.CalleeFor(HolepunchStep.DataSocket));

        foreach (HolepunchStep step in Enum.GetValues<HolepunchStep>())
        {
            string callee = HolepunchFlow.CalleeFor(step);

            Assert.Contains(callee, HolepunchSeam.Asks.Select(a => a.Callee));
        }
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Empty(HolepunchFlow.InFileOrder(""));
        Assert.False(HolepunchFlow.TheFinisStillComeFirstInTheFile(""));
        Assert.False(HolepunchFlow.BothErrorStepsStillQuit(""));
        Assert.False(HolepunchFlow.TheCtrlSocketIsStillCaughtByRudpInit(""));
        Assert.False(HolepunchFlow.TheDataSocketIsStillUnchecked(""));
        Assert.False(HolepunchFlow.AFailedPunchSendsNoFinishedEvent(""));
    }
}
