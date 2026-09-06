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
    /// PP371: and both reads of that reason are guarded, because it can genuinely be null.
    ///
    /// remote_disconnected is set on the stream side BEFORE the strdup that fills the reason, so a
    /// failed allocation reaches the session with nothing. The C dereferenced it twice - strcmp to
    /// pick the quit reason, then strdup to carry it to the client - on the one path that runs when
    /// a console hangs up.
    /// </summary>
    [Fact]
    public void BothReadsOfTheDisconnectReasonAreGuarded()
    {
        string? path = SessionTeardownSource.Locate();
        if (path is null)
            return;

        Assert.True(
            SessionTeardownSource.TheDisconnectReasonIsStillGuarded(File.ReadAllText(path)),
            "the disconnect reason is dereferenced without being tested again");
    }

    /// <summary>And the reader finds the unguarded version, so the check means something.</summary>
    [Fact]
    public void TheReaderFindsAnUnguardedDisconnectReason()
    {
        const string asItWas = """
            		if(!strcmp(session->stream_connection.remote_disconnect_reason, "Server shutting down"))
            			session->quit_reason = CHIAKI_QUIT_REASON_STREAM_CONNECTION_REMOTE_SHUTDOWN;
            """;

        Assert.False(SessionTeardownSource.TheDisconnectReasonIsStillGuarded(asItWas));
    }

    /// <summary>
    /// PP761: and it finds the guard whichever way the local is filled.
    ///
    /// PP696 replaces the run with a callback that writes the reason out through a parameter, so the
    /// local is declared and passed instead of being assigned off session->stream_connection. What
    /// PP371 is about does not move: both dereferences still go through one local and both are still
    /// tested. Asserted here because the tree only ever has one of the two spellings, and the one it
    /// will have next is the one nothing would have exercised.
    /// </summary>
    [Fact]
    public void TheGuardIsFoundWhenTheReasonComesFromTheCallback()
    {
        const string AsItWillBe = """
            	const char *disconnect_reason = NULL;

            	chiaki_mutex_unlock(&session->state_mutex);
            	err = session->stream_run_cb(data_sock, &disconnect_reason, session->stream_run_cb_user);
            	chiaki_mutex_lock(&session->state_mutex);
            	if(disconnect_reason && !strcmp(disconnect_reason, "Server shutting down"))
            		session->quit_reason = CHIAKI_QUIT_REASON_STREAM_CONNECTION_REMOTE_SHUTDOWN;
            	session->quit_reason_str = disconnect_reason ? strdup(disconnect_reason) : NULL;
            """;

        Assert.True(SessionTeardownSource.TheDisconnectReasonIsStillGuarded(AsItWillBe));

        // Both spellings at once is two pointers under one name, and is neither shape.
        string both = AsItWillBe
            + "\n\tdisconnect_reason = session->stream_connection.remote_disconnect_reason;\n";
        Assert.False(SessionTeardownSource.TheDisconnectReasonIsStillGuarded(both));

        // And the new spelling with an unguarded read is still caught, which is the whole point.
        string unguarded = AsItWillBe.Replace(
            "disconnect_reason ? strdup(disconnect_reason) : NULL",
            "strdup(disconnect_reason)",
            StringComparison.Ordinal);
        Assert.False(SessionTeardownSource.TheDisconnectReasonIsStillGuarded(unguarded));
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

    /// <summary>
    /// PP348: ctrl.c's GENERIC failure guards too, which is where PP336's rule was defeated.
    ///
    /// PP336 asserted the rule and it held - for the session thread's label, while ctrl.c's
    /// ctrl_failed assigned unconditionally on six paths to the same field. A session refused for
    /// something the user could act on had that replaced by CTRL_UNKNOWN when the ctrl connection
    /// failed afterwards, which it does, since there is no session left to carry it.
    ///
    /// THE RULE IS NOT "EVERY WRITER GUARDS" - the first version of this test said that and failed
    /// on four writes that are all correct. A specific cause recorded first needs no guard, and
    /// STOPPED should override: a stop is what the user asked for. Only the generic failure must
    /// not overwrite.
    /// </summary>
    [Fact]
    public void TheGenericCtrlFailureDoesNotOverwriteARecordedReason()
    {
        string? path = SessionTeardownSource.LocateCtrl();
        if (path is null)
            return;

        string? body = ChiakiNg.Session.CFunction.BodyIn(path, "static void ctrl_failed(");
        Assert.NotNull(body);

        Assert.True(
            SessionTeardownSource.TheGenericCtrlFailureGuards(body),
            "ctrl_failed writes the quit reason without guarding on NONE");
        Assert.True(
            SessionTeardownSource.TheFailureItselfIsStillUnconditional(body),
            "ctrl_failed no longer reports the failure unconditionally, so a session could wait on a dead ctrl");
    }

    /// <summary>And the reader finds the unguarded version, so the check above means something.</summary>
    [Fact]
    public void TheReaderFindsAnUnguardedGenericFailure()
    {
        const string asItWas = """
            static void ctrl_failed(ChiakiCtrl *ctrl, ChiakiQuitReason reason)
            {
            	chiaki_mutex_lock(&ctrl->session->state_mutex);
            	ctrl->session->quit_reason = reason;
            	ctrl->session->ctrl_failed = true;
            }
            """;

        string? body = ChiakiNg.Session.CFunction.Body(asItWas, "static void ctrl_failed(");

        Assert.NotNull(body);
        Assert.False(SessionTeardownSource.TheGenericCtrlFailureGuards(body));
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
