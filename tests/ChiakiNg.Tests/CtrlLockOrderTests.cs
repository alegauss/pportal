using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP469, PP470, PP294: the ctrl channel's two mutexes, and the cycle that is now cut.
///
/// PP468 counted the call sites and left the question open deliberately. PP469's sweep was bounded -
/// notif_mutex is acquired in six places, all in ctrl.c, and two of those are on the ctrl thread which
/// arrives holding nothing - so it reduced to whether any of the four exported ones runs with
/// state_mutex held. One did, at the PIN prompt.
///
/// PP470 cut that end. These tests now hold the repair, and the acquisition it removed is kept as a
/// value so the fix is measured against what it removed rather than against nothing.
/// </summary>
public class CtrlLockOrderTests
{
    private static string? Ctrl()
    {
        string? path = CtrlLockOrder.LocateCtrl();
        return path is null ? null : File.ReadAllText(path);
    }

    private static string? Session()
    {
        string? path = CtrlLockOrder.LocateSession();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// The sweep is complete because the set is: six functions take notif_mutex and they are all in
    /// ctrl.c.
    ///
    /// A seventh appearing is what would make PP469's answer stale, so this enumerates rather than
    /// confirming the six it expects.
    /// </summary>
    [Fact]
    public void SixFunctionsTakeNotifAndTheyAreAllInCtrl()
    {
        Assert.Equal(6, CtrlLockOrder.NotifAcquiredIn.Count);

        if (Ctrl() is not { } source)
            return;

        Assert.Equal(
            CtrlLockOrder.NotifAcquiredIn.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            CtrlLockOrder.FunctionsTakingNotifIn(source)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// Two of the six are on the ctrl thread, which arrives holding nothing - so neither can be the
    /// second half of a cycle, and the question is about the other four.
    /// </summary>
    [Fact]
    public void TwoOfTheSixCannotBePartOfACycle()
    {
        foreach (string name in CtrlLockOrder.OnTheCtrlThread)
            Assert.Contains(name, CtrlLockOrder.NotifAcquiredIn);

        Assert.Equal(4, CtrlLockOrder.NotifAcquiredIn.Count - CtrlLockOrder.OnTheCtrlThread.Count);
    }

    /// <summary>
    /// PP470: one order is left, so there is no cycle.
    ///
    /// This test asserted the opposite when PP469 wrote it. The pair it compared is kept - the removed
    /// acquisition is still a value - so the fix is measured against what it removed rather than
    /// against nothing.
    /// </summary>
    [Fact]
    public void OneOrderIsLeftSoNoCycleDoes()
    {
        Assert.Single(CtrlLockOrder.Acquisitions);
        Assert.False(CtrlLockOrder.ACycleExists());

        LockAcquisition ctrl = CtrlLockOrder.Acquisitions[0];
        Assert.Equal(CtrlMutex.Notif, ctrl.Holds);
        Assert.Equal(CtrlMutex.State, ctrl.Takes);

        // And what PP470 removed WOULD have cycled with it, which is what makes the removal the fix
        // rather than a tidy-up.
        LockAcquisition removed = CtrlLockOrder.WhatPP470Removed;
        Assert.True(CtrlLockOrder.IsACycle(removed, ctrl));
        Assert.NotEqual(removed.Thread, ctrl.Thread);
    }

    /// <summary>Two acquisitions in the same order are not a cycle, which is the control.</summary>
    [Fact]
    public void TheSameOrderTwiceIsNotACycle()
    {
        var one = new LockAcquisition("a", CtrlMutex.State, CtrlMutex.Notif, "x");
        var two = new LockAcquisition("b", CtrlMutex.State, CtrlMutex.Notif, "y");

        Assert.False(CtrlLockOrder.IsACycle(one, two));
    }

    /// <summary>
    /// The session half, in the file: the cond wait returns holding state_mutex and the PIN call
    /// follows with no unlock between.
    /// </summary>
    [Fact]
    public void ThePinPromptReleasesStateBeforeCrossingIntoCtrl()
    {
        if (Session() is not { } source)
            return;

        Assert.True(
            CtrlLockOrder.ThePinPromptReleasesStateBeforeTheCall(source),
            "the PIN prompt holds state_mutex across the call again, or stopped taking the PIN out first - "
                + "either way PP470's cycle is back");
    }

    /// <summary>And the ctrl half: the PIN setter takes notif_mutex.</summary>
    [Fact]
    public void ThePinSetterTakesNotif()
    {
        if (Ctrl() is not { } source)
            return;

        Assert.True(CtrlLockOrder.ThePinSetterStillTakesNotif(source));
    }

    /// <summary>
    /// And the other direction is PP468's census, which this depends on: ctrl_failed takes state_mutex
    /// and six of seven calls hold notif across it.
    /// </summary>
    [Fact]
    public void TheCtrlHalfIsPP468sCensus()
    {
        if (Ctrl() is not { } source)
            return;

        Assert.True(CtrlMutexes.CtrlFailedStillTakesStateMutex(source));

        if (CtrlMutexes.ThreadBody(source) is not { } body)
            return;

        int holding = CtrlMutexes.CountCtrlFailedIn(body) - CtrlMutexes.CountReleasingCallsIn(body);
        Assert.Equal(6, holding);
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Empty(CtrlLockOrder.FunctionsTakingNotifIn(""));
        Assert.False(CtrlLockOrder.ThePinPromptReleasesStateBeforeTheCall(""));
        Assert.False(CtrlLockOrder.ThePinSetterStillTakesNotif(""));
    }
}
