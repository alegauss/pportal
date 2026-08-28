using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP467, PP294: the two arriving ctrl messages that may only arrive once.
///
/// PP294 names the risk as "a type handled in the wrong state", and PP466 settled which types arrive.
/// Two of those ten are once-only, enforced by a session bool rather than by anything in ctrl.c - and
/// the locking around those bools is inconsistent in a way where both obvious fixes are wrong.
/// </summary>
public class CtrlOnceOnlyTests
{
    private static string? Ctrl()
    {
        string? path = CtrlOnceOnly.LocateCtrl();
        return path is null ? null : File.ReadAllText(path);
    }

    private static string? Session()
    {
        string? path = CtrlOnceOnly.LocateSession();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>Two of the ten arriving types are once-only, and the other eight are not.</summary>
    [Fact]
    public void TwoOfTheTenArrivingTypesAreOnceOnly()
    {
        Assert.Equal(2, CtrlOnceOnly.Flags.Count);

        foreach (OnceOnlyFlag flag in CtrlOnceOnly.Flags)
        {
            Assert.True(CtrlOnceOnly.IsOnceOnly(flag.Type));

            // And each is a type PP466 established actually arrives - a once-only guard on a
            // send-only type would be a guard against nothing.
            Assert.True(CtrlDispatchTable.Arrives(flag.Type));
        }

        Assert.Equal(
            8,
            CtrlDispatchTable.Received.Count(t => !CtrlOnceOnly.IsOnceOnly(t)));
    }

    /// <summary>
    /// THE DUPLICATES ARE DROPPED THE SAME WAY AND LOGGED DIFFERENTLY: one warns, one notes.
    ///
    /// Nothing about the channel says why one is more alarming, and it is the only distinction the C
    /// draws between them - so a port that levelled the log levels would lose it.
    /// </summary>
    [Fact]
    public void OneDuplicateWarnsAndTheOtherOnlyNotes()
    {
        Assert.Equal(
            SecondArrival.WarnedAndDropped,
            CtrlOnceOnly.Arriving("SESSION_ID", alreadySeen: true));

        Assert.Equal(
            SecondArrival.NotedAndDropped,
            CtrlOnceOnly.Arriving("SWITCH_TO_STREAM_CONNECTION", alreadySeen: true));

        // The two are different answers, which is the whole assertion.
        Assert.NotEqual(
            CtrlOnceOnly.Arriving("SESSION_ID", true),
            CtrlOnceOnly.Arriving("SWITCH_TO_STREAM_CONNECTION", true));
    }

    /// <summary>A first arrival is accepted, and so is any arrival of a type that counts nothing.</summary>
    [Fact]
    public void AFirstArrivalAndAnUncountedTypeAreBothAccepted()
    {
        Assert.Equal(SecondArrival.Accepted, CtrlOnceOnly.Arriving("SESSION_ID", alreadySeen: false));

        // HEARTBEAT_REQ arrives repeatedly by design, so "already seen" means nothing to it.
        Assert.Equal(SecondArrival.Accepted, CtrlOnceOnly.Arriving("HEARTBEAT_REQ", alreadySeen: true));
        Assert.False(CtrlOnceOnly.IsOnceOnly("HEARTBEAT_REQ"));
    }

    /// <summary>
    /// ONE READ OF FOUR TAKES THE LOCK, and the number is asserted so a lock appearing or going has to
    /// be argued for.
    /// </summary>
    [Fact]
    public void OneOfFourReadsTakesTheLock()
    {
        Assert.Equal(1, CtrlOnceOnly.LockedReadsInCtrl);

        if (Ctrl() is not { } source)
            return;

        Assert.Equal(CtrlOnceOnly.ReadsInCtrl, CtrlOnceOnly.CountReadsIn(source));
    }

    /// <summary>The asymmetry itself: one handler locks to read its flag, its neighbour does not.</summary>
    [Fact]
    public void TheTwoHandlersReadTheirFlagsDifferently()
    {
        Assert.True(CtrlOnceOnly.Flags[0].ReadUnderTheLock);
        Assert.False(CtrlOnceOnly.Flags[1].ReadUnderTheLock);

        if (Ctrl() is not { } source)
            return;

        Assert.True(
            CtrlOnceOnly.TheAsymmetryIsStillThere(source),
            "the two once-only handlers now read their flags the same way, so somebody levelled this "
                + "and the reason it was safe either way should be re-read");
    }

    /// <summary>
    /// AND WHY LEVELLING IT DOWN WOULD BREAK: the write locks and signals state_cond, which is what the
    /// session thread's wait depends on.
    ///
    /// The unlocked reads are safe because the ctrl thread is the only writer. The lock is not for the
    /// readers, and a port concluding it is decorative would leave the session thread waiting out its
    /// timeout.
    /// </summary>
    [Fact]
    public void TheWriteLocksAndSignalsForTheSessionThread()
    {
        if (Session() is not { } source)
            return;

        Assert.True(
            CtrlOnceOnly.TheWriteStillLocksAndSignals(source),
            "the setter stopped locking or stopped signalling, and the session thread waits on that");

        Assert.True(
            CtrlOnceOnly.TheSessionThreadStillReadsBoth(source),
            "session.c no longer reads both flags, which is what made the ctrl thread the only writer "
                + "rather than the only toucher");
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Equal(0, CtrlOnceOnly.CountReadsIn(""));
        Assert.False(CtrlOnceOnly.TheAsymmetryIsStillThere(""));
        Assert.False(CtrlOnceOnly.TheWriteStillLocksAndSignals(""));
        Assert.False(CtrlOnceOnly.TheSessionThreadStillReadsBoth(""));
    }
}
