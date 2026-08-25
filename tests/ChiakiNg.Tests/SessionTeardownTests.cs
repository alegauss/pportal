using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP336, continuing PP293: the session thread's two exits, and which reason a client is told.
///
/// PP297's capture is of a session that connected and was stopped cleanly, so none of the failure
/// paths are in it. Everything here is asserted against session.c.
/// </summary>
public class SessionTeardownTests
{
    /// <summary>
    /// A RECORDED REASON SURVIVES A LATER GENERIC ONE, which is the assignment that matters.
    ///
    /// The ctrl_failed label writes CTRL_UNKNOWN only where nothing is recorded yet. Assigning it
    /// unconditionally would turn a version mismatch, or a console already in use, into "ctrl
    /// failed" - the ending a user can do least with, replacing the one they could act on.
    /// </summary>
    [Theory]
    [InlineData(ChiakiQuitReason.SessionRequestRpInUse)]
    [InlineData(ChiakiQuitReason.SessionRequestRpVersionMismatch)]
    [InlineData(ChiakiQuitReason.Stopped)]
    public void AReasonAlreadyRecordedIsKept(ChiakiQuitReason already)
    {
        Assert.Equal(
            already, SessionTeardown.Record(already, ChiakiQuitReason.CtrlUnknown));
    }

    /// <summary>And nothing recorded takes what the failure proposes.</summary>
    [Fact]
    public void NothingRecordedTakesTheProposedReason()
    {
        Assert.Equal(
            ChiakiQuitReason.CtrlUnknown,
            SessionTeardown.Record(ChiakiQuitReason.None, ChiakiQuitReason.CtrlUnknown));
    }

    /// <summary>
    /// CANCELLING IS NOT FAILING. The stream connection returning Canceled is what stopping looks
    /// like from inside the run, and lands in the same branch as success.
    /// </summary>
    [Theory]
    [InlineData(ChiakiError.Success)]
    [InlineData(ChiakiError.Canceled)]
    public void AStreamThatEndedCleanlyReportsStopped(ChiakiError error)
    {
        Assert.Equal(ChiakiQuitReason.Stopped, SessionTeardown.FromStreamConnection(error, null));
    }

    /// <summary>Any other error is the unknown stream failure.</summary>
    [Theory]
    [InlineData(ChiakiError.Network)]
    [InlineData(ChiakiError.Unknown)]
    public void AnyOtherErrorIsTheUnknownStreamFailure(ChiakiError error)
    {
        Assert.Equal(
            ChiakiQuitReason.StreamConnectionUnknown,
            SessionTeardown.FromStreamConnection(error, null));
    }

    /// <summary>
    /// A remote disconnect splits on the console's own words, compared WHOLE.
    ///
    /// libchiaki uses strcmp, so a reason that merely contains the shutdown phrase is the other
    /// kind - which matters because the two produce different screens.
    /// </summary>
    [Theory]
    [InlineData("Server shutting down", ChiakiQuitReason.StreamConnectionRemoteShutdown)]
    [InlineData("Server shutting down now", ChiakiQuitReason.StreamConnectionRemoteDisconnected)]
    [InlineData("", ChiakiQuitReason.StreamConnectionRemoteDisconnected)]
    [InlineData(null, ChiakiQuitReason.StreamConnectionRemoteDisconnected)]
    public void ARemoteDisconnectSplitsOnTheWholeReason(string? reason, ChiakiQuitReason quit)
    {
        Assert.Equal(quit, SessionTeardown.FromStreamConnection(ChiakiError.Disconnected, reason));
    }

    /// <summary>
    /// Which exit is taken depends on one thing: whether ctrl was ever started.
    ///
    /// Before it, the thread goes straight to the quit event; after it, through the ctrl teardown -
    /// which falls through to the same event, so neither path ends without one.
    /// </summary>
    [Fact]
    public void CtrlIsStoppedOnlyWhereItWasStarted()
    {
        Assert.Equal(SessionExit.Direct, SessionTeardown.ExitFor(ctrlStarted: false));
        Assert.Equal(SessionExit.ViaCtrl, SessionTeardown.ExitFor(ctrlStarted: true));

        Assert.False(SessionTeardown.StopsCtrl(SessionExit.Direct));
        Assert.True(SessionTeardown.StopsCtrl(SessionExit.ViaCtrl));
    }

    /// <summary>
    /// And BOTH exits send the quit event, which is the whole of what a client learns.
    ///
    /// The fall-through from the ctrl label into the quit label is what makes this true. A path
    /// that ended without an event would leave a client waiting on one forever, which reads as a
    /// hang rather than as a failure.
    /// </summary>
    [Fact]
    public void EveryExitSendsTheQuitEvent()
    {
        Assert.All(
            Enum.GetValues<SessionExit>(),
            exit => Assert.True(SessionTeardown.SendsQuitEvent(exit)));
    }

    /// <summary>And session.c still has the teardown this reproduces.</summary>
    [Fact]
    public void SessionStillDeclaresTheTeardown()
    {
        string? path = SessionTeardownSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(
            SessionTeardownSource.TheCtrlExitStillFallsThroughToQuit(core),
            "the ctrl exit no longer falls through to the quit event");
        Assert.True(
            SessionTeardownSource.AReasonAlreadySetIsStillKept(core),
            "the ctrl-failed label now overwrites a recorded quit reason");
        Assert.True(
            SessionTeardownSource.TheEventIsStillSentUnlocked(core),
            "the quit event is now sent with the state mutex held");
        Assert.True(
            SessionTeardownSource.CancelledIsStillNotAFailure(core),
            "a cancelled stream connection is now reported as a failure");
    }
}
