using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP468, PP294: the ctrl channel's two mutexes and the one call site that treats them differently.
///
/// PP350 did this for the stop pipes. The locks were in the same state - forty operations across two
/// mutexes, three classes touching one or the other, nothing saying which guards what.
///
/// The last two tests are a census rather than a verdict: six of seven ctrl_failed calls hold
/// notif_mutex while it takes state_mutex, one releases first, and the session thread agrees with the
/// one. Whether that matters needs a sweep this does not do.
/// </summary>
public class CtrlMutexesTests
{
    private static string? Ctrl()
    {
        string? path = CtrlMutexes.LocateCtrl();
        return path is null ? null : File.ReadAllText(path);
    }

    private static string? Session()
    {
        string? path = CtrlOnceOnly.LocateSession();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE TWO ARE NOT ON THE SAME OBJECT: one belongs to the ctrl, the other to the session.
    ///
    /// A port using one lock for both would serialise the ctrl thread against everything that touches
    /// session state, which is most of the session.
    /// </summary>
    [Fact]
    public void TheTwoMutexesAreOnDifferentObjects()
    {
        Assert.Contains("&ctrl->notif_mutex", CtrlMutexes.LockCallFor(CtrlMutex.Notif));
        Assert.Contains("&ctrl->session->state_mutex", CtrlMutexes.LockCallFor(CtrlMutex.State));

        // And each wakes its waiter a different way, which is PP350's distinction one level down.
        Assert.Equal("notif_pipe", CtrlMutexes.WakesWith(CtrlMutex.Notif));
        Assert.Equal("state_cond", CtrlMutexes.WakesWith(CtrlMutex.State));
        Assert.NotEqual(
            CtrlMutexes.WakesWith(CtrlMutex.Notif), CtrlMutexes.WakesWith(CtrlMutex.State));
    }

    /// <summary>Every guarded field belongs to exactly one of the two.</summary>
    [Theory]
    [InlineData("login_pin_entered", CtrlMutex.Notif)]
    [InlineData("msg_queue", CtrlMutex.Notif)]
    [InlineData("should_stop", CtrlMutex.Notif)]
    [InlineData("ctrl_session_id_received", CtrlMutex.State)]
    [InlineData("stream_connection_switch_received", CtrlMutex.State)]
    public void EachFieldHasOneGuard(string field, CtrlMutex expected)
    {
        Assert.Equal(expected, CtrlMutexes.GuardOf(field));
    }

    /// <summary>And a field neither guards answers null rather than defaulting to one.</summary>
    [Fact]
    public void AnUnguardedFieldAnswersNull()
    {
        Assert.Null(CtrlMutexes.GuardOf("recv_buf_size"));
        Assert.Null(CtrlMutexes.GuardOf(""));
    }

    /// <summary>
    /// The PIN flag is the one with writers on two threads, which is where PP467's sole-writer
    /// argument stops - it is locked because it has to be, not by convention.
    /// </summary>
    [Fact]
    public void ThePinFlagIsTheOneWithTwoWriters()
    {
        Assert.Equal(CtrlMutex.Notif, CtrlMutexes.GuardOf(CtrlMutexes.TheFlagWithTwoWriters));

        // The session flags PP467 catalogued are on the other mutex and have one writer each.
        foreach (OnceOnlyFlag flag in CtrlOnceOnly.Flags)
            Assert.Equal(CtrlMutex.State, CtrlMutexes.GuardOf(flag.Field));
    }

    /// <summary>ctrl_failed takes state_mutex, which is what makes the pairing a question at all.</summary>
    [Fact]
    public void CtrlFailedTakesTheSessionsMutex()
    {
        if (Ctrl() is not { } source)
            return;

        Assert.True(CtrlMutexes.CtrlFailedStillTakesStateMutex(source));
    }

    /// <summary>
    /// THE CENSUS: seven calls, one of which releases notif_mutex around it.
    ///
    /// Counted rather than judged. The numbers are what a later question about lock order needs, and
    /// a call added or a release removed changes them and has to be argued for.
    /// </summary>
    [Fact]
    public void SevenCallsAndOneReleasesFirst()
    {
        Assert.Equal(7, CtrlMutexes.CtrlFailedCalls);
        Assert.Equal(1, CtrlMutexes.CallsThatReleaseFirst);

        if (Ctrl() is not { } source || CtrlMutexes.ThreadBody(source) is not { } body)
            return;

        Assert.Equal(CtrlMutexes.CtrlFailedCalls, CtrlMutexes.CountCtrlFailedIn(body));
        Assert.Equal(CtrlMutexes.CallsThatReleaseFirst, CtrlMutexes.CountReleasingCallsIn(body));
    }

    /// <summary>
    /// And the session thread does what the one exception does: releases its own mutex before calling
    /// into ctrl.
    ///
    /// Which is the reason to read the exception as a discipline six sites do not follow, rather than
    /// as a stray unlock.
    /// </summary>
    [Fact]
    public void TheSessionThreadAgreesWithTheException()
    {
        if (Session() is not { } source)
            return;

        Assert.True(CtrlMutexes.TheSessionThreadStillReleasesBeforeCallingCtrl(source));
    }

    /// <summary>
    /// PP472: five of the six holding calls leave the loop at once, and one does not.
    ///
    /// This is the column PP470's choice needs. For the five, releasing notif_mutex around the call
    /// cannot be observed - there is no "after" inside the loop. For the sixth there is, so the
    /// six-edit fix is not the uniform one PP470's section first described.
    /// </summary>
    [Fact]
    public void FiveOfTheSixLeaveAtOnceAndOneCarriesOn()
    {
        Assert.Equal(6, CtrlMutexes.HoldingCalls.Count);
        Assert.Equal(5, CtrlMutexes.LeaveImmediately);

        // And every one of the six is a holding call, not the releasing one.
        Assert.DoesNotContain(CtrlMutexes.HoldingCalls, c => c.ReleasesNotifFirst);

        // The exception is single - Single() throws if the count ever stops being one.
        CtrlFailedCall exception = CtrlMutexes.TheOneThatCarriesOn;
        Assert.False(exception.LeavesImmediately);

        // The census and this column agree on the total.
        Assert.Equal(
            CtrlMutexes.CtrlFailedCalls,
            CtrlMutexes.HoldingCalls.Count + CtrlMutexes.CallsThatReleaseFirst);
    }

    /// <summary>
    /// And the reason it carries on, read from the C: its break is inside a switch nested in a while,
    /// so it exits the switch and the iteration continues.
    /// </summary>
    [Fact]
    public void TheExceptionsBreakOnlyLeavesTheSwitch()
    {
        if (Ctrl() is not { } source || CtrlMutexes.ThreadBody(source) is not { } body)
            return;

        Assert.True(
            CtrlMutexes.TheExceptionIsStillInsideASwitchInAWhile(body),
            "the rudp submessage switch no longer wraps that ctrl_failed, so the exception may have "
                + "become like the other five and PP470's choice is simpler than recorded");
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(CtrlMutexes.ThreadBody(""));
        Assert.Equal(0, CtrlMutexes.CountCtrlFailedIn(""));
        Assert.Equal(0, CtrlMutexes.CountReleasingCallsIn(""));
        Assert.False(CtrlMutexes.CtrlFailedStillTakesStateMutex(""));
        Assert.False(CtrlMutexes.TheSessionThreadStillReleasesBeforeCallingCtrl(""));
        Assert.False(CtrlMutexes.TheExceptionIsStillInsideASwitchInAWhile(""));
    }
}
