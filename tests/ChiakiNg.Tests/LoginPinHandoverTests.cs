using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP345, under PP294: a login PIN that could not be handed to ctrl is reported as what it was.
///
/// The old shape passed every test there was, because there was nothing to test: a void function
/// returning early leaves no evidence anywhere. So these read the two files rather than run the
/// path - an out-of-memory malloc is not reachable from a test - and the one thing that CAN be run
/// is run: the reason's sentence, which is the only part of this that crosses the shim.
/// </summary>
public class LoginPinHandoverTests
{
    private static string? Ctrl() =>
        LoginPinHandover.LocateCtrl() is { } path ? File.ReadAllText(path) : null;

    private static string? Session() =>
        LoginPinHandover.LocateSession() is { } path ? File.ReadAllText(path) : null;

    /// <summary>THE SYMPTOM. The handover can report a failure at all.</summary>
    [Fact]
    public void TheHandoverReturnsACode()
    {
        if (Ctrl() is not { } core)
            return;

        Assert.True(
            LoginPinHandover.ItCanReportAFailure(core),
            "chiaki_ctrl_set_login_pin still returns void, so a dropped PIN reaches nobody");
    }

    /// <summary>And its one failure both logs and answers, rather than returning in silence.</summary>
    [Fact]
    public void TheAllocationFailureLogsAndAnswers()
    {
        if (Ctrl() is not { } core)
            return;

        Assert.True(
            LoginPinHandover.TheAllocationFailureIsReported(core),
            "the malloc failure in chiaki_ctrl_set_login_pin does not both log and return a code");
    }

    /// <summary>
    /// THE HALF THAT MATTERS TO A PERSON. The caller reads the answer and ends the session on it,
    /// instead of falling into the wait whose timeout produces the prompt that says "wrong".
    /// </summary>
    [Fact]
    public void TheSessionThreadActsOnAFailedHandover()
    {
        if (Session() is not { } core)
            return;

        Assert.True(
            LoginPinHandover.TheCallerActsOnIt(core),
            "session.c does not end the session on a failed login PIN handover");
    }

    /// <summary>
    /// And the premise that ending is the right answer: the PIN is already freed by the time the
    /// failure is read, so there is nothing to retry with.
    /// </summary>
    [Fact]
    public void ThePinIsAlreadySpentWhenTheFailureIsRead()
    {
        if (Session() is not { } core)
            return;

        Assert.True(
            LoginPinHandover.ThePinIsSpentBeforeTheCheck(core),
            "session.c now checks the handover before freeing the PIN, which changes what a "
            + "failure should do");
    }

    /// <summary>
    /// THE ONE MEASUREMENT. The reason has a sentence of its own, asked of libchiaki through the
    /// shim with the ordinal this port assigned.
    ///
    /// Falling to "Unknown" is exactly what an append landing at different indices on the two sides
    /// looks like, and it is the failure a reading of either file alone cannot see.
    /// </summary>
    [Fact]
    public void TheQuitReasonHasItsOwnSentence()
    {
        Assert.True(
            LoginPinHandover.TheReasonAgreesAcrossTheShim(),
            ChiakiSession.QuitReasonString((int)LoginPinHandover.Reason) ?? "<null>");
    }

    /// <summary>
    /// And it is still an error, so the disconnect screen shows it. chiaki_quit_reason_is_error
    /// excludes STOPPED and REMOTE_SHUTDOWN and nothing else, so a reason appended after those two
    /// is one a user is told about - which is the point of naming it.
    /// </summary>
    [Fact]
    public void TheQuitReasonIsAnErrorAndNotAQuietEnding()
    {
        Assert.NotEqual(ChiakiQuitReason.Stopped, LoginPinHandover.Reason);
        Assert.NotEqual(ChiakiQuitReason.StreamConnectionRemoteShutdown, LoginPinHandover.Reason);
        Assert.NotEqual(ChiakiQuitReason.None, LoginPinHandover.Reason);
    }

    /// <summary>
    /// The readers see the shape this replaced, so a green above is not a reader that agrees with
    /// anything it is shown.
    /// </summary>
    [Fact]
    public void TheReadersSeeTheDefectTheyWereWrittenFor()
    {
        const string Before = """
            CHIAKI_EXPORT void chiaki_ctrl_set_login_pin(ChiakiCtrl *ctrl, const uint8_t *pin, size_t pin_size)
            {
                uint8_t *buf = malloc(pin_size);
                if(!buf)
                    return;
                memcpy(buf, pin, pin_size);
            }
            """;

        Assert.False(LoginPinHandover.ItCanReportAFailure(Before));
        Assert.False(LoginPinHandover.TheAllocationFailureIsReported(Before));

        const string BeforeCaller = """
            chiaki_ctrl_set_login_pin(&session->ctrl, session->login_pin, session->login_pin_size);
            session->login_pin_entered = false;
            free(session->login_pin);
            session->login_pin = NULL;
            err = chiaki_cond_timedwait_pred(&session->state_cond, &session->state_mutex,
                    SESSION_EXPECT_CTRL_START_MS, session_check_state_pred_ctrl_start, session);
            """;

        Assert.False(LoginPinHandover.TheCallerActsOnIt(BeforeCaller));
        Assert.False(LoginPinHandover.ThePinIsSpentBeforeTheCheck(BeforeCaller));
    }
}
