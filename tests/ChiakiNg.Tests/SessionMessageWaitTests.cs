using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP213: the two waits that sit on PP212's queue.
///
/// The last test is the one that earns the task. It composes the queue, the notification wait and
/// both message waits into the loop the core runs, and shows that a single acknowledgement for the
/// wrong request id never sleeps and never shrinks the queue - a hot loop, reproduced without a
/// console, a websocket or a thread.
/// </summary>
public class SessionMessageWaitTests
{
    private const SessionMessageAction Offer = SessionMessageAction.Offer;
    private const SessionMessageAction Result = SessionMessageAction.Result;
    private const SessionMessageAction Terminate = SessionMessageAction.Terminate;

    /// <summary>A message the wait was for is reported, and left for the caller to clear.</summary>
    [Fact]
    public void AMessageMeetingTheMaskIsMatchedAndLeftAlone()
    {
        SessionMessageDisposition disposition = SessionMessageWait.Consider(Offer, Offer);

        Assert.Equal(SessionMessageDisposition.Matched, disposition);
        Assert.False(SessionMessageWait.Clears(disposition));
    }

    /// <summary>A mask is a set, and any member of it is a match.</summary>
    [Fact]
    public void TheMaskMatchesAnyOfASet()
        => Assert.Equal(
            SessionMessageDisposition.Matched,
            SessionMessageWait.Consider(SessionMessageAction.Accept, Offer | SessionMessageAction.Accept));

    /// <summary>
    /// A message the wait is not interested in is the one thing that IS cleared, which is why the
    /// queue does not grow over a session.
    /// </summary>
    [Fact]
    public void AMessageMissingTheMaskIsIgnoredAndCleared()
    {
        SessionMessageDisposition disposition = SessionMessageWait.Consider(Result, Offer);

        Assert.Equal(SessionMessageDisposition.Ignored, disposition);
        Assert.True(SessionMessageWait.Clears(disposition));
    }

    /// <summary>
    /// TERMINATE is asked before the mask, so a wait that asked only for an OFFER is still ended by
    /// one. A port that tested the mask first would call this "not mine" and sit until its timeout.
    /// </summary>
    [Fact]
    public void TerminateEndsAWaitThatNeverAskedForIt()
    {
        SessionMessageDisposition disposition = SessionMessageWait.Consider(Terminate, Offer);

        Assert.Equal(SessionMessageDisposition.Terminated, disposition);
        Assert.False(SessionMessageWait.Clears(disposition));
    }

    /// <summary>And asking for it explicitly changes nothing, because the test is the same one.</summary>
    [Fact]
    public void AskingForTerminateChangesNothing()
        => Assert.Equal(
            SessionMessageDisposition.Terminated,
            SessionMessageWait.Consider(Terminate, Terminate));

    /// <summary>
    /// A payload that did not parse is not the same as one naming an action nobody knows. The first
    /// stays on the queue and is met again by every later wait; the second is ignored and cleared
    /// like any other message that is not wanted.
    /// </summary>
    [Fact]
    public void NotParsingAndNotBeingKnownAreDifferentAnswers()
    {
        SessionMessageDisposition broken = SessionMessageWait.Consider(null, Offer);
        SessionMessageDisposition unknown = SessionMessageWait.Consider(SessionMessageAction.Unknown, Offer);

        Assert.Equal(SessionMessageDisposition.Unparseable, broken);
        Assert.False(SessionMessageWait.Clears(broken));

        Assert.Equal(SessionMessageDisposition.Ignored, unknown);
        Assert.True(SessionMessageWait.Clears(unknown));
    }

    /// <summary>Exactly one of the four answers clears, and it is the uninteresting one.</summary>
    [Fact]
    public void OnlyTheIgnoredAnswerClears()
    {
        Assert.Equal([SessionMessageDisposition.Ignored], SessionMessageWait.Clearing);

        foreach (SessionMessageDisposition disposition in Enum.GetValues<SessionMessageDisposition>())
        {
            Assert.Equal(
                disposition == SessionMessageDisposition.Ignored,
                SessionMessageWait.Clears(disposition));
        }
    }

    /// <summary>The ack wait looks for one action and one only.</summary>
    [Fact]
    public void TheAckWaitLooksOnlyForResult()
        => Assert.Equal(Result, SessionMessageAckWait.Mask);

    /// <summary>An acknowledgement for the request being waited for is the end of it.</summary>
    [Fact]
    public void TheRightRequestIdIsAcked()
        => Assert.Equal(
            AckDisposition.Acked,
            SessionMessageAckWait.Consider(requestId: 4, expectedRequestId: 4, cancelRequested: false));

    /// <summary>One for some other request sends the wait round again.</summary>
    [Fact]
    public void TheWrongRequestIdGoesRoundAgain()
        => Assert.Equal(
            AckDisposition.WrongRequest,
            SessionMessageAckWait.Consider(requestId: 7, expectedRequestId: 4, cancelRequested: false));

    /// <summary>The stop is asked first, so it wins even over the acknowledgement being right.</summary>
    [Fact]
    public void TheStopIsAskedBeforeTheRequestId()
        => Assert.Equal(
            AckDisposition.Cancelled,
            SessionMessageAckWait.Consider(requestId: 4, expectedRequestId: 4, cancelRequested: true));

    /// <summary>
    /// And no answer at all clears the notification. This is a statement about the core, not a gap
    /// in the port: every path through that wait frees the MESSAGE, which does not touch the queue.
    /// </summary>
    [Fact]
    public void TheAckWaitClearsOnNoPath()
    {
        Assert.Empty(SessionMessageAckWait.Clearing);

        foreach (AckDisposition disposition in Enum.GetValues<AckDisposition>())
            Assert.False(SessionMessageAckWait.Clears(disposition));
    }

    /// <summary>
    /// The defect, driven the way the core drives it.
    ///
    /// One RESULT on the queue for a request nobody is waiting for. Each pass scans from the front,
    /// finds it, parses it, matches the mask, rejects the request id, and clears nothing - so the
    /// next pass finds the same one. MustSleep is false on every pass, which is what says this
    /// never reaches a wait: it is a hot loop, not a slow one. Reproduced, not fixed.
    /// </summary>
    [Fact]
    public void AnAckForAnotherRequestNeverSleepsAndNeverEnds()
    {
        const int expected = 4;
        const int arrived = 7;
        const int passes = 100;

        var queue = new NotificationQueue();
        var stray = new QueuedNotification(PushNotificationType.SessionMessageCreated, "{}");
        queue.Enqueue(stray);

        for (int pass = 0; pass < passes; pass++)
        {
            // wait_for_notification, called fresh each time round, so its cursor starts at null.
            var notifications = new NotificationWait(
                queue, PushNotificationType.SessionMessageCreated, timeoutMs: 30_000, startedAtUs: 0);

            Assert.False(notifications.MustSleep);
            Assert.Equal(NotificationWaitOutcome.Matched, notifications.Scan());
            Assert.Same(stray, notifications.Match);

            // wait_for_session_message: RESULT meets the mask, so this is not the path that clears.
            SessionMessageDisposition disposition =
                SessionMessageWait.Consider(Result, SessionMessageAckWait.Mask);

            Assert.Equal(SessionMessageDisposition.Matched, disposition);
            Assert.False(SessionMessageWait.Clears(disposition));

            // and the ack wait rejects it without clearing it either.
            AckDisposition ack = SessionMessageAckWait.Consider(arrived, expected, cancelRequested: false);

            Assert.Equal(AckDisposition.WrongRequest, ack);
            Assert.False(SessionMessageAckWait.Clears(ack));
        }

        Assert.Equal(1, queue.Count);
        Assert.True(SessionMessageAckWait.WouldSpinOn(arrived, expected));
        Assert.False(SessionMessageAckWait.WouldSpinOn(expected, expected));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheTwoWaitsAreStillTheCores()
    {
        string? file = SessionMessageWaitSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            SessionMessageWaitSource.TerminateIsStillTestedBeforeTheMask(core),
            "terminate before the mask");
        Assert.True(
            SessionMessageWaitSource.OnlyTheMaskMissStillClears(core),
            "and the mask miss is the one that clears");
        Assert.True(
            SessionMessageWaitSource.TheAckWaitStillClearsNothing(core),
            "the ack wait clears nothing");
        Assert.True(
            SessionMessageWaitSource.AWrongRequestIdStillContinues(core),
            "and goes round again on a wrong id");
        Assert.True(
            SessionMessageWaitSource.FreeingAMessageStillLeavesTheQueueAlone(core),
            "freeing the message leaves the queue");
    }
}
