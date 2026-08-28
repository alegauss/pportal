using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP469, PP294: both lock orders exist, so the ctrl channel's two mutexes can deadlock.
///
/// PP468 counted the call sites and left this open deliberately. The sweep was bounded - notif_mutex is
/// acquired in six places, all in ctrl.c, and two of those are on the ctrl thread which arrives holding
/// nothing - so the question reduced to whether any of the four exported ones runs with state_mutex
/// held. One does.
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
    /// THE ANSWER: both orders exist, so the two mutexes form a cycle.
    /// </summary>
    [Fact]
    public void BothOrdersExistSoACycleDoes()
    {
        Assert.Equal(2, CtrlLockOrder.Acquisitions.Count);
        Assert.True(CtrlLockOrder.ACycleExists());

        // Named, so the failure says which way round each is.
        LockAcquisition session = CtrlLockOrder.Acquisitions[0];
        LockAcquisition ctrl = CtrlLockOrder.Acquisitions[1];

        Assert.Equal(CtrlMutex.State, session.Holds);
        Assert.Equal(CtrlMutex.Notif, session.Takes);

        Assert.Equal(CtrlMutex.Notif, ctrl.Holds);
        Assert.Equal(CtrlMutex.State, ctrl.Takes);

        Assert.True(CtrlLockOrder.IsACycle(session, ctrl));

        // And they are different threads, which is what makes it a deadlock rather than reentrancy.
        Assert.NotEqual(session.Thread, ctrl.Thread);
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
    public void ThePinPromptHoldsStateAcrossTheCall()
    {
        if (Session() is not { } source)
            return;

        Assert.True(
            CtrlLockOrder.ThePinPromptStillHoldsStateAcrossTheCall(source),
            "the PIN prompt now releases state_mutex before calling into ctrl, which is PP469's fix "
                + "having landed - the cycle is closed and this assertion should invert");
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
        Assert.False(CtrlLockOrder.ThePinPromptStillHoldsStateAcrossTheCall(""));
        Assert.False(CtrlLockOrder.ThePinSetterStillTakesNotif(""));
    }
}
