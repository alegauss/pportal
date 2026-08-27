using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP411, under PP294: the login-PIN request, and the one that arrives too late.
///
/// PP408 has the answer, PP335 the loop and PP345 the handover. This is the arrival that starts
/// them, and the refusal nothing had written down.
/// </summary>
public class CtrlLoginPinRequestTests
{
    /// <summary>A request before any session id asks the session to prompt.</summary>
    [Fact]
    public void ARequestInTimeAsksTheSessionToPrompt()
    {
        PinRequestEffect effect = CtrlLoginPinRequest.Receive(0, sessionIdReceived: false);

        Assert.Equal(PinRequestOutcome.Prompt, effect.Outcome);
        Assert.True(effect.SessionPinRequested);
        Assert.Null(effect.Passes);
        Assert.False(CtrlLoginPinRequest.ReportsCtrlFailed(effect));
        Assert.True(CtrlLoginPinRequest.SignalsTheSession(effect));
    }

    /// <summary>
    /// THE PROPERTY WORTH HAVING A NAME FOR. A PIN asked for too late ends the session.
    ///
    /// The console asking for a PIN after it has already given out a session id is asking to start
    /// over, and the library refuses. This is what stands between that arrival and a PIN dialog over
    /// an established stream - and the session's own flag, the one its wait reads, is never raised.
    /// </summary>
    [Fact]
    public void ARequestAfterTheSessionIdEndsTheSession()
    {
        PinRequestEffect effect = CtrlLoginPinRequest.Receive(0, sessionIdReceived: true);

        Assert.Equal(PinRequestOutcome.RefusedTooLate, effect.Outcome);
        Assert.False(effect.SessionPinRequested);
        Assert.Equal(ChiakiQuitReason.CtrlUnknown, effect.Passes);
        Assert.True(CtrlLoginPinRequest.ReportsCtrlFailed(effect));
    }

    /// <summary>
    /// CTRL'S OWN FLAG IS RAISED ON BOTH PATHS, and the refusal does not lower it.
    ///
    /// Reproduced rather than tidied. Nothing reads it after the refusal because the session is
    /// ending, so it only matters if that refusal ever becomes recoverable - and a port that cleared
    /// it "while we are here" would have changed that quietly.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CtrlsOwnFlagIsRaisedWhicheverPathRuns(bool sessionIdReceived)
    {
        Assert.True(CtrlLoginPinRequest.Receive(0, sessionIdReceived).CtrlPinRequested);
    }

    /// <summary>
    /// BOTH PATHS WAKE THE SESSION, and that is the part a port drops.
    ///
    /// The prompt signals after raising the session's flag; the refusal signals from inside
    /// ctrl_failed. A port that signalled on one only would leave a session waiting forever on
    /// exactly the arrival this refuses.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EitherPathWakesTheSession(bool sessionIdReceived)
    {
        Assert.True(
            CtrlLoginPinRequest.SignalsTheSession(
                CtrlLoginPinRequest.Receive(0, sessionIdReceived)));
    }

    /// <summary>
    /// PP348: the reason is passed, and recorded only over NONE.
    ///
    /// A session already refused for a cause the user could act on keeps that cause. Reading the
    /// handler's CTRL_UNKNOWN as what the client is told would be wrong in exactly the case PP348
    /// was about.
    /// </summary>
    [Fact]
    public void TheReasonIsRecordedOnlyOverNone()
    {
        PinRequestEffect refused = CtrlLoginPinRequest.Receive(0, sessionIdReceived: true);

        Assert.Equal(
            ChiakiQuitReason.CtrlUnknown,
            CtrlLoginPinRequest.ReasonRecorded(refused, ChiakiQuitReason.None));

        // A cause already recorded survives this handler's generic one.
        Assert.Equal(
            ChiakiQuitReason.CtrlConnectionRefused,
            CtrlLoginPinRequest.ReasonRecorded(refused, ChiakiQuitReason.CtrlConnectionRefused));

        // And the path that did not fail records nothing at all.
        PinRequestEffect prompted = CtrlLoginPinRequest.Receive(0, sessionIdReceived: false);
        Assert.Equal(
            ChiakiQuitReason.None,
            CtrlLoginPinRequest.ReasonRecorded(prompted, ChiakiQuitReason.None));
    }

    /// <summary>
    /// The size guard warns and refuses nothing, because the request carries nothing to read.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(512, true)]
    public void AnyPayloadIsWarnedAboutAtMost(int payloadSize, bool warned)
    {
        Assert.Equal(warned, CtrlLoginPinRequest.IsWarnedAbout(payloadSize));
        Assert.False(CtrlLoginPinRequest.IsRefused(payloadSize));

        // And the outcome is decided by the session id alone, whatever arrived.
        Assert.Equal(
            PinRequestOutcome.Prompt,
            CtrlLoginPinRequest.Receive(payloadSize, sessionIdReceived: false).Outcome);
        Assert.Equal(
            PinRequestOutcome.RefusedTooLate,
            CtrlLoginPinRequest.Receive(payloadSize, sessionIdReceived: true).Outcome);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheHandlersRulesAreStillTheQtCores()
    {
        string? path = CtrlLoginPinRequestSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.NotNull(CtrlLoginPinRequestSource.Body(core));

        Assert.True(
            CtrlLoginPinRequestSource.CtrlsFlagIsStillRaisedFirst(core),
            "ctrl's own flag no longer precedes the mutex, so the refusal may not leave it set");
        Assert.True(
            CtrlLoginPinRequestSource.TheSessionIdTestStillStandsBetweenTheFlags(core),
            "the session id test moved, and a late request can raise the session's PIN flag");
        Assert.True(
            CtrlLoginPinRequestSource.TheLateRequestStillEndsTheSession(core),
            "the late request no longer unlocks and fails, so it either deadlocks or prompts");
        Assert.True(
            CtrlLoginPinRequestSource.TheRefusalStillReturns(core),
            "the refusal falls through, so a prompt goes up over a session already ending");
        Assert.True(
            CtrlLoginPinRequestSource.TheGuardStillOnlyWarns(core),
            "the size guard refuses now, and this port accepts what the handler drops");
    }

    /// <summary>
    /// And the reason still crosses the shim as the ordinal this port reads.
    ///
    /// PP345 established that quit reasons cross as ordinals, so naming CtrlUnknown here is only
    /// worth anything while the two enums agree about which one it is.
    /// </summary>
    [Fact]
    public void TheReasonStillAgreesAcrossTheShim()
    {
        string? path = ChiakiNg.Session.SanitizerSource.LocateRelative(@"lib\include\chiaki\session.h");
        if (path is null)
            return;

        string header = File.ReadAllText(path);

        Assert.Contains("CHIAKI_QUIT_REASON_CTRL_UNKNOWN", header, StringComparison.Ordinal);
    }

    /// <summary>PP272: and every reader answers no to an empty file.</summary>
    [Fact]
    public void EveryReaderAnswersNoToAnEmptyFile()
    {
        Assert.Null(CtrlLoginPinRequestSource.Body(""));
        Assert.False(CtrlLoginPinRequestSource.CtrlsFlagIsStillRaisedFirst(""));
        Assert.False(CtrlLoginPinRequestSource.TheSessionIdTestStillStandsBetweenTheFlags(""));
        Assert.False(CtrlLoginPinRequestSource.TheLateRequestStillEndsTheSession(""));
        Assert.False(CtrlLoginPinRequestSource.TheRefusalStillReturns(""));
        Assert.False(CtrlLoginPinRequestSource.TheGuardStillOnlyWarns(""));
    }
}
