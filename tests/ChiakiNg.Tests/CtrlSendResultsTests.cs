using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP383, under PP294: the feature burst reports, and no send in ctrl.c is thrown away.
///
/// PP342 modelled which seven messages the burst sends and in what order. What nothing modelled was
/// what happens when one does not go - and because ctrl_message_send spends the encryption counter
/// before the socket sees anything, the answer was that the channel quietly stopped working a few
/// messages later.
/// </summary>
public class CtrlSendResultsTests(ITestOutputHelper output)
{
    private static string? Ctrl() =>
        CtrlSendResults.Locate() is { } path ? File.ReadAllText(path) : null;

    private static string? Header() =>
        ChiakiNg.Session.SanitizerSource.LocateRelative(@"lib\include\chiaki\ctrl.h") is { } path
            ? File.ReadAllText(path)
            : null;

    private static string? Session() =>
        ChiakiNg.Session.SanitizerSource.LocateRelative(@"lib\src\session.c") is { } path ? File.ReadAllText(path) : null;

    /// <summary>THE SYMPTOM. The burst can report a failure at all.</summary>
    [Fact]
    public void TheBurstReturnsACode()
    {
        if (Header() is not { } header)
            return;

        Assert.True(
            CtrlSendResults.TheBurstCanReportAFailure(header),
            "ctrl_enable_features still returns void, so seven sends answer nobody");
    }

    /// <summary>And every send inside it is read.</summary>
    [Fact]
    public void NoSendInTheBurstIsUnchecked()
    {
        if (Ctrl() is not { } core)
            return;

        int unchecked_ = CtrlSendResults.UncheckedSendsInTheBurst(core);

        Assert.True(unchecked_ >= 0, "ctrl_enable_features could not be found at all");
        Assert.Equal(0, unchecked_);
    }

    /// <summary>
    /// THE HALF A LOG WOULD NOT BUY. The burst stops at the first failure rather than sending on.
    ///
    /// Every further send spends another counter value into a gap the console does not know about,
    /// so continuing widens the break. A guard that only logged would satisfy "the result is read"
    /// and do exactly that.
    /// </summary>
    [Fact]
    public void TheBurstStopsRatherThanCarryingOn()
    {
        if (Ctrl() is not { } core)
            return;

        Assert.True(
            CtrlSendResults.TheBurstStopsAtTheFirstFailure(core),
            "the burst's guard no longer returns, so a failed send is followed by six more");
    }

    /// <summary>Both callers end the channel, each by the mechanism its file has.</summary>
    [Fact]
    public void BothCallersEndTheChannel()
    {
        if (Ctrl() is { } core)
        {
            Assert.True(
                CtrlSendResults.TheHandlerEndsTheChannel(core),
                "the SESSION_ID arm no longer fails the channel on a failed burst");
        }

        if (Session() is { } session)
        {
            Assert.True(
                CtrlSendResults.TheSessionThreadEndsTheChannel(session),
                "the session thread no longer jumps to ctrl_failed on a failed burst");
        }
    }

    /// <summary>
    /// THE RULE, over the whole file - and PP385 brought the ratchet to zero.
    ///
    /// PP383 shipped this as a ceiling of seven because stating the rule over the file found seven
    /// discards outside the burst, each a different decision. All seven are answered, so ctrl.c now
    /// asserts what streamconnection.c and senkusha.c assert: none at all.
    /// </summary>
    [Fact]
    public void NoSendInTheFileIsDiscarded()
    {
        if (Ctrl() is not { } core)
            return;

        IReadOnlyList<string> discarded = CtrlSendResults.DiscardedResults(core);

        foreach (string call in discarded)
            output.WriteLine(call);

        Assert.Equal(0, CtrlSendResults.DiscardCeiling);

        Assert.True(
            discarded.Count <= CtrlSendResults.DiscardCeiling,
            $"{discarded.Count} discarded results, over the ceiling of "
            + $"{CtrlSendResults.DiscardCeiling}: " + string.Join(", ", discarded));
    }

    /// <summary>
    /// PP385: the drain LEAVES on a failed send rather than draining on into the same gap.
    ///
    /// The node is unlinked and freed either way, so there is nothing to retry. What the break buys
    /// is that the messages still queued do not each spend another counter value.
    /// </summary>
    [Fact]
    public void TheDrainLeavesRatherThanDrainingOn()
    {
        if (Ctrl() is not { } core)
            return;

        Assert.True(
            CtrlSendResults.TheDrainLeavesOnAFailedSend(core),
            "the drain no longer reads its send, fails the channel and leaves");
    }

    /// <summary>
    /// PP416: AND LEAVING MEANS THE REST GO TOO, which the break alone did not achieve.
    ///
    /// The break above exits the inner loop only. The outer loop's test on should_stop, msg_queue
    /// and login_pin_entered was then true BECAUSE the queue was not empty, so it took the cancelled
    /// branch and re-entered this drain - sending the next message into the counter gap PP385's rule
    /// forbids, one per outer iteration, until the session thread got round to stopping ctrl. The
    /// count depended on scheduling, which is the part that makes it unreproducible rather than
    /// merely wrong.
    /// </summary>
    [Fact]
    public void LeavingTheDrainDropsWhatIsStillQueued()
    {
        if (Ctrl() is not { } core)
            return;

        Assert.True(
            CtrlSendResults.TheDrainDropsWhatIsStillQueued(core),
            "the drain leaves messages queued, so the outer loop sends them into the same gap");

        // Both halves, because the break is still what ends the inner loop.
        Assert.True(CtrlSendResults.TheDrainLeavesOnAFailedSend(core));
    }

    /// <summary>
    /// PP416: and the reader refuses the drain as it was - a break with nothing dropped.
    ///
    /// The shape that passed PP385's check and still sent everything. Asserted against a synthetic
    /// body rather than by putting the defect back.
    /// </summary>
    [Fact]
    public void TheReaderRefusesABreakThatDropsNothing()
    {
        const string BreakOnly = """
            				if(drain_err != CHIAKI_ERR_SUCCESS)
            				{
            					CHIAKI_LOGE(ctrl->session->log, "failed: %s", chiaki_error_string(drain_err));
            					chiaki_mutex_unlock(&ctrl->notif_mutex);
            					ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
            					chiaki_mutex_lock(&ctrl->notif_mutex);
            					break;
            				}
            """;

        Assert.False(CtrlSendResults.TheDrainDropsWhatIsStillQueued(BreakOnly));

        // And a comment merely NAMING the drop does not satisfy it either - PP400's rule.
        const string CommentOnly = """
            				if(drain_err != CHIAKI_ERR_SUCCESS)
            				{
            					// while(ctrl->msg_queue) ctrl_message_queue_free(rest);
            					break;
            				}
            """;

        Assert.False(CtrlSendResults.TheDrainDropsWhatIsStillQueued(CommentOnly));
    }

    /// <summary>
    /// And it copies the queued type before freeing the node that holds it.
    ///
    /// Asserted because the first version of this fix read msg->type in the log AFTER
    /// ctrl_message_queue_free - a use-after-free introduced by the change that added the log.
    /// </summary>
    [Fact]
    public void TheDrainDoesNotReadAFreedNode()
    {
        if (Ctrl() is not { } core)
            return;

        Assert.True(
            CtrlSendResults.TheDrainCopiesTheTypeBeforeTheFree(core),
            "the drain's log reads the queued node after it has been freed");
    }

    /// <summary>
    /// PP385: the fallback session id is REPORTED and not fatal, which is the one of the seven
    /// that gets a different answer.
    ///
    /// It sends nothing, so nothing desyncs. Its failure is that the session has no id, and the
    /// session thread already ends on that - so a ctrl_failed here would be this port ending
    /// sessions the C carries on with.
    /// </summary>
    [Fact]
    public void TheFallbackSessionIdIsReportedWithoutEndingTheChannel()
    {
        if (Ctrl() is not { } core)
            return;

        Assert.True(
            CtrlSendResults.TheFallbackIsReportedAndNotFatal(core),
            "the fallback session id guard now ends the channel, which the C does not");

        // All four rungs of the ladder, which is what PP342's JudgeSessionId models as four
        // fallbacks - so a fifth rung added without the guard is a rung that says nothing.
        Assert.Equal(4, CtrlSendResults.FallbackCallsThroughTheGuard(core));
    }

    /// <summary>
    /// The burst is still the seven PP342 named, so the rule above is about the whole of it.
    /// </summary>
    [Fact]
    public void TheBurstIsStillSevenMessages()
    {
        Assert.Equal(7, CtrlSendResults.Burst.Count);

        // Two microphone toggles, which PP342 asserts stay two - the capture has them 108
        // microseconds apart, and two sends means two counter values.
        Assert.Equal(2, CtrlSendResults.Burst.Count(m => m == "ctrl_message_toggle_microphone"));

        if (Ctrl() is not { } core)
            return;

        string? body = ChiakiNg.Session.CFunction.Body(core, "ChiakiErrorCode ctrl_enable_features(");
        Assert.NotNull(body);

        foreach (string message in CtrlSendResults.Burst.Distinct())
            Assert.Contains(message, body, StringComparison.Ordinal);
    }

    /// <summary>The readers see the shapes they were written for, and read the file (PP272).</summary>
    [Fact]
    public void TheReadersSeeTheShapesTheyGuardAgainst()
    {
        Assert.False(CtrlSendResults.TheBurstCanReportAFailure("CHIAKI_EXPORT void ctrl_enable_features(ChiakiCtrl *ctrl);"));
        Assert.False(CtrlSendResults.TheBurstCanReportAFailure(""));

        Assert.False(CtrlSendResults.TheBurstStopsAtTheFirstFailure(""));
        Assert.False(CtrlSendResults.TheHandlerEndsTheChannel(""));
        Assert.False(CtrlSendResults.TheSessionThreadEndsTheChannel(""));

        // A guard that logs and carries on, which is the half-fix.
        const string LogsOnly = """
            #define CTRL_FEATURE_SEND(call, what) do { \
            		ChiakiErrorCode feature_err = (call); \
            		if(feature_err != CHIAKI_ERR_SUCCESS) \
            			CHIAKI_LOGE(ctrl->session->log, "failed %s", (what)); \
            	} while(0)
            """;

        Assert.False(CtrlSendResults.TheBurstStopsAtTheFirstFailure(LogsOnly));

        // And a bare send is still found by the file-wide reader.
        const string Bare = "\tctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_GO_HOME, NULL, 0);";

        Assert.Single(CtrlSendResults.DiscardedResults(Bare));

        // PP385's readers, on the shapes they replaced.
        Assert.False(CtrlSendResults.TheDrainLeavesOnAFailedSend(""));
        Assert.False(CtrlSendResults.TheDrainDropsWhatIsStillQueued(""));
        Assert.False(CtrlSendResults.TheDrainCopiesTheTypeBeforeTheFree(""));
        Assert.False(CtrlSendResults.TheFallbackIsReportedAndNotFatal(""));
        Assert.Equal(0, CtrlSendResults.FallbackCallsThroughTheGuard(""));

        const string DrainAsItWas = """
            			while(ctrl->msg_queue)
            			{
            				ChiakiCtrlMessageQueue *msg = ctrl->msg_queue;
            				ctrl->msg_queue = msg->next;
            				chiaki_mutex_unlock(&ctrl->notif_mutex);
            				ctrl_message_send(ctrl, msg->type, msg->payload, msg->payload_size);
            				ctrl_message_queue_free(msg);
            				chiaki_mutex_lock(&ctrl->notif_mutex);
            			}
            """;

        Assert.False(CtrlSendResults.TheDrainLeavesOnAFailedSend(DrainAsItWas));

        // The use-after-free the log introduced, which is the reader's real subject.
        const string ReadAfterFree = """
            				ChiakiErrorCode drain_err = ctrl_message_send(ctrl, msg->type, msg->payload, msg->payload_size);
            				ctrl_message_queue_free(msg);
            				CHIAKI_LOGE(ctrl->session->log, "type %#x", (unsigned int)msg->type);
            """;

        Assert.False(CtrlSendResults.TheDrainCopiesTheTypeBeforeTheFree(ReadAfterFree));

        // And a fallback guard that ends the channel, which would be stricter than the C.
        const string FatalFallback = """
            #define CTRL_FALLBACK_SESSION_ID(ctrl) do { \
            		ChiakiErrorCode fallback_err = ctrl_message_set_fallback_session_id(ctrl); \
            		if(fallback_err != CHIAKI_ERR_SUCCESS) \
            		{ \
            			CHIAKI_LOGE((ctrl)->session->log, "no fallback"); \
            			ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN); \
            		} \
            	} while(0)
            """;

        Assert.False(CtrlSendResults.TheFallbackIsReportedAndNotFatal(FatalFallback));
    }
}
