using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP212: the queue the websocket thread fills and the wait that walks it.
///
/// The queue's tests are ordinary. The wait's are not, and that is the point: what the core calls
/// a timeout bounds SILENCE rather than the call, and the assertions below are what say so
/// without a websocket, a console or a clock that moves on its own.
/// </summary>
public class NotificationQueueTests
{
    /// <summary>A wait that asks for one thing, which is what most of the flow does.</summary>
    private const PushNotificationType Wanted = PushNotificationType.SessionMessageCreated;

    /// <summary>Something that arrives and is not it.</summary>
    private const PushNotificationType Other = PushNotificationType.MemberCreated;

    /// <summary>Thirty seconds, in the units the two halves of the check are written in.</summary>
    private const long TimeoutMs = 30_000;
    private const long TimeoutUs = TimeoutMs * NotificationWait.MicrosecondsPerMillisecond;

    private static QueuedNotification N(PushNotificationType type = Other, string payload = "{}")
        => new(type, payload);

    private static NotificationWait Waiting(
        NotificationQueue queue, PushNotificationType mask = Wanted, long startedAtUs = 0)
        => new(queue, mask, TimeoutMs, startedAtUs);

    [Fact]
    public void AnEmptyQueueHasNeitherEnd()
    {
        var queue = new NotificationQueue();

        Assert.Null(queue.Front);
        Assert.Null(queue.Rear);
        Assert.Equal(0, queue.Count);
        Assert.False(queue.Dequeue());
    }

    /// <summary>The websocket thread pushes at the rear and the front stays where it was.</summary>
    [Fact]
    public void EnqueuePutsAtTheRearAndLeavesTheFront()
    {
        var queue = new NotificationQueue();
        QueuedNotification first = N();
        QueuedNotification second = N();

        queue.Enqueue(first);
        queue.Enqueue(second);

        Assert.Same(first, queue.Front);
        Assert.Same(second, queue.Rear);
        Assert.Equal(2, queue.Count);
    }

    /// <summary>Dequeue takes the front, and emptying it empties both ends together.</summary>
    [Fact]
    public void DequeueTakesTheFrontAndEmptyingClearsBothEnds()
    {
        var queue = new NotificationQueue();
        QueuedNotification only = N();
        queue.Enqueue(only);

        Assert.True(queue.Dequeue());
        Assert.Null(queue.Front);
        Assert.Null(queue.Rear);
    }

    /// <summary>
    /// Clear works by identity wherever the notification sits, and removing the rear moves the
    /// rear back rather than leaving it pointing at what is gone.
    /// </summary>
    [Fact]
    public void ClearRemovesByIdentityWhereverItSits()
    {
        var queue = new NotificationQueue();
        QueuedNotification front = N();
        QueuedNotification middle = N();
        QueuedNotification rear = N();

        queue.Enqueue(front);
        queue.Enqueue(middle);
        queue.Enqueue(rear);

        Assert.True(queue.Clear(middle));
        Assert.Same(front, queue.Front);
        Assert.Same(rear, queue.Rear);

        Assert.True(queue.Clear(rear));
        Assert.Same(front, queue.Rear);

        Assert.True(queue.Clear(front));
        Assert.Equal(0, queue.Count);
    }

    /// <summary>Clearing what is not there says so, and so does clearing the same thing twice.</summary>
    [Fact]
    public void ClearingSomethingAbsentSaysSo()
    {
        var queue = new NotificationQueue();
        QueuedNotification held = N();
        queue.Enqueue(held);

        Assert.False(queue.Clear(N()));
        Assert.True(queue.Clear(held));
        Assert.False(queue.Clear(held));
    }

    /// <summary>
    /// Two notifications carrying the same thing are two entries. The core compares pointers
    /// throughout, and a port that made these equal would clear the wrong one.
    /// </summary>
    [Fact]
    public void TwoIdenticalNotificationsAreStillTwoEntries()
    {
        var queue = new NotificationQueue();
        QueuedNotification first = N(Wanted, "{\"a\":1}");
        QueuedNotification second = N(Wanted, "{\"a\":1}");

        queue.Enqueue(first);
        queue.Enqueue(second);

        Assert.True(queue.Clear(first));
        Assert.Equal(1, queue.Count);
        Assert.Same(second, queue.Front);
    }

    /// <summary>Teardown drains the front until there is no front.</summary>
    [Fact]
    public void DrainEmptiesIt()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(N());
        queue.Enqueue(N());

        queue.Drain();

        Assert.Equal(0, queue.Count);
    }

    /// <summary>
    /// The wait sleeps while the rear is what it has already seen - which an empty queue and a
    /// fully scanned one both are, and which is not the same as "the queue is empty".
    /// </summary>
    [Fact]
    public void TheWaitSleepsWhileNothingIsPastTheCursor()
    {
        var queue = new NotificationQueue();
        NotificationWait wait = Waiting(queue);

        Assert.True(wait.MustSleep);

        queue.Enqueue(N());
        Assert.False(wait.MustSleep);

        Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Scan());
        Assert.True(wait.MustSleep);
        Assert.Equal(1, queue.Count);
    }

    /// <summary>The scan stops at the first type meeting the mask and reports it.</summary>
    [Fact]
    public void TheScanStopsAtTheFirstTypeThatMeetsTheMask()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(N());
        QueuedNotification wanted = N(Wanted);
        queue.Enqueue(wanted);
        queue.Enqueue(N(Wanted));

        NotificationWait wait = Waiting(queue);

        Assert.Equal(NotificationWaitOutcome.Matched, wait.Scan());
        Assert.Same(wanted, wait.Match);
    }

    /// <summary>A mask is a set, and any member of it wakes the wait.</summary>
    [Fact]
    public void TheMaskMatchesAnyOfASet()
    {
        var queue = new NotificationQueue();
        QueuedNotification arrived = N(PushNotificationType.SessionDeleted);
        queue.Enqueue(arrived);

        NotificationWait wait = Waiting(
            queue, Wanted | PushNotificationType.SessionDeleted);

        Assert.Equal(NotificationWaitOutcome.Matched, wait.Scan());
        Assert.Same(arrived, wait.Match);
    }

    /// <summary>And a type nobody recognises is in no mask, so it wakes nobody.</summary>
    [Fact]
    public void UnknownMeetsNoMask()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(N(PushNotificationType.Unknown));

        NotificationWait wait = Waiting(queue, (PushNotificationType)0xFFFF);

        Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Scan());
        Assert.Null(wait.Match);
    }

    /// <summary>
    /// A wait removes nothing it finds. That is the rule the whole flow rests on - the caller
    /// clears what it consumed - and a caller that forgets finds the same notification again.
    /// </summary>
    [Fact]
    public void TheWaitRemovesNothingAndAForgetfulCallerFindsItAgain()
    {
        var queue = new NotificationQueue();
        QueuedNotification wanted = N(Wanted);
        queue.Enqueue(wanted);

        Assert.Equal(NotificationWaitOutcome.Matched, Waiting(queue).Scan());
        Assert.Equal(1, queue.Count);

        NotificationWait second = Waiting(queue);
        Assert.Equal(NotificationWaitOutcome.Matched, second.Scan());
        Assert.Same(wanted, second.Match);

        Assert.True(queue.Clear(wanted));
        Assert.Equal(NotificationWaitOutcome.KeepWaiting, Waiting(queue).Scan());
    }

    /// <summary>The cursor is where this call has scanned to, and the next scan resumes there.</summary>
    [Fact]
    public void TheScanResumesAtTheCursorRatherThanTheFront()
    {
        var queue = new NotificationQueue();
        QueuedNotification skipped = N();
        queue.Enqueue(skipped);

        NotificationWait wait = Waiting(queue);
        Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Scan());
        Assert.Same(skipped, wait.Cursor);

        QueuedNotification wanted = N(Wanted);
        queue.Enqueue(wanted);

        Assert.Equal(NotificationWaitOutcome.Matched, wait.Scan());
        Assert.Same(wanted, wait.Match);
        Assert.Same(skipped, wait.Cursor);
    }

    /// <summary>
    /// A cursor cleared out from under the wait. In the core this is a pointer into freed memory;
    /// the port starts again from the front rather than inventing a behaviour to be faithful to.
    /// </summary>
    [Fact]
    public void AClearedCursorRestartsFromTheFront()
    {
        var queue = new NotificationQueue();
        QueuedNotification skipped = N();
        queue.Enqueue(skipped);

        NotificationWait wait = Waiting(queue);
        Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Scan());

        queue.Clear(skipped);
        QueuedNotification wanted = N(Wanted);
        queue.Enqueue(wanted);

        Assert.Equal(NotificationWaitOutcome.Matched, wait.Scan());
        Assert.Same(wanted, wait.Match);
    }

    /// <summary>
    /// The deadline is only ever consulted on the condition variable's OWN timeout. Woken by a
    /// signal, the wait does not look at the clock at all - which is the first half of the defect.
    /// </summary>
    [Fact]
    public void TheDeadlineIsOnlyNoticedWhenTheWaitItselfTimesOut()
    {
        NotificationWait wait = Waiting(new NotificationQueue());
        long wayPast = 10 * TimeoutUs;

        Assert.True(wait.DeadlinePassed(wayPast));
        Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Wake(NotificationWake.Signalled, wayPast));
        Assert.Equal(NotificationWaitOutcome.TimedOut, wait.Wake(NotificationWake.TimedOut, wayPast));
    }

    /// <summary>
    /// And the second half. The condition variable is given the whole timeout, so its first wake
    /// lands where elapsed EQUALS the timeout - and the check is strict, so that wake is refused
    /// and the wait sleeps another full one. A thirty second wait reports a timeout at sixty.
    /// </summary>
    [Fact]
    public void TheFirstSilentWakeLandsOnTheDeadlineAndIsRefused()
    {
        NotificationWait wait = Waiting(new NotificationQueue());

        Assert.False(wait.DeadlinePassed(TimeoutUs));
        Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Wake(NotificationWake.TimedOut, TimeoutUs));

        Assert.Equal(2 * TimeoutUs, wait.EarliestTimeoutUs);
        Assert.Equal(
            NotificationWaitOutcome.TimedOut,
            wait.Wake(NotificationWake.TimedOut, wait.EarliestTimeoutUs));
    }

    /// <summary>
    /// Traffic for other things keeps the wait alive indefinitely. Ten notifications that do not
    /// meet the mask, each just inside the timeout, carry a thirty second wait to five minutes -
    /// and it is still waiting. Reproduced, not fixed.
    /// </summary>
    [Fact]
    public void TrafficForOtherThingsKeepsTheWaitAlivePastItsDeadline()
    {
        var queue = new NotificationQueue();
        NotificationWait wait = Waiting(queue);

        long now = 0;
        for (int i = 0; i < 10; i++)
        {
            now += TimeoutUs - 1;
            queue.Enqueue(N());

            Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Wake(NotificationWake.Signalled, now));
            Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Scan());
        }

        Assert.True(wait.DeadlinePassed(now));
        Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Wake(NotificationWake.Signalled, now));
    }

    /// <summary>
    /// The stop is read on any wake, and reading it puts it back - one stop consumed by one wait,
    /// which is the one-shot shape PP172 named on the mapping screen.
    /// </summary>
    [Fact]
    public void TheStopIsTakenOnAWakeAndTakingItConsumesIt()
    {
        NotificationWait wait = Waiting(new NotificationQueue());
        wait.CancelRequested = true;

        Assert.Equal(NotificationWaitOutcome.Cancelled, wait.Wake(NotificationWake.Signalled, 0));
        Assert.False(wait.CancelRequested);
        Assert.Equal(NotificationWaitOutcome.KeepWaiting, wait.Wake(NotificationWake.Signalled, 0));
    }

    /// <summary>
    /// And is never seen by a wait that keeps finding matches, because the scan does not consult
    /// it: only a wake does.
    /// </summary>
    [Fact]
    public void TheStopIsNotSeenWhileMatchesKeepArriving()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(N(Wanted));

        NotificationWait wait = Waiting(queue);
        wait.CancelRequested = true;

        Assert.Equal(NotificationWaitOutcome.Matched, wait.Scan());
        Assert.True(wait.CancelRequested);
    }

    /// <summary>The deadline is asked first, so a timed out wait reports that and keeps the stop.</summary>
    [Fact]
    public void TheDeadlineIsAskedBeforeTheStop()
    {
        NotificationWait wait = Waiting(new NotificationQueue());
        wait.CancelRequested = true;

        Assert.Equal(
            NotificationWaitOutcome.TimedOut,
            wait.Wake(NotificationWake.TimedOut, 2 * TimeoutUs));
        Assert.True(wait.CancelRequested);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheQueueAndTheWaitAreStillTheCores()
    {
        string? file = NotificationQueueSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(NotificationQueueSource.TheQueueIsStillFrontAndRear(core), "a front and a rear");
        Assert.True(NotificationQueueSource.TheWaitStillWatchesTheRear(core), "the rear, not emptiness");
        Assert.True(NotificationQueueSource.TheScanStillStartsAfterTheCursor(core), "from the cursor");
        Assert.True(NotificationQueueSource.TheWaitStillRemovesNothing(core), "and removes nothing");
        Assert.True(
            NotificationQueueSource.TheDeadlineIsStillInsideTheTimeoutBranch(core),
            "the deadline is reachable one way only");
        Assert.True(NotificationQueueSource.TheComparisonIsStillStrict(core), "and strictly");
        Assert.True(
            NotificationQueueSource.TheStopIsStillAOneShotReadAfterTheWake(core),
            "the stop, after the wake, once");
    }
}
