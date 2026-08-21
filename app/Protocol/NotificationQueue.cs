namespace ChiakiNg.Protocol;

/// <summary>
/// PP212: one notification as it sits on the queue.
///
/// A class and not a record struct, because the whole of the wait below is written in terms of
/// WHICH notification, not which value: the cursor is a pointer comparison in the core and is
/// reference identity here. Two notifications carrying identical json are two entries, and a port
/// that made them equal would collapse a queue the core keeps distinct.
/// </summary>
/// <param name="type">Its type, already read by <see cref="PushNotification.TypeOf"/>.</param>
/// <param name="payload">The raw json it arrived as, which the core keeps beside the parsed tree.</param>
public sealed class QueuedNotification(PushNotificationType type, string payload)
{
    /// <summary>What it is, which is the only thing a mask is tested against.</summary>
    public PushNotificationType Type { get; } = type;

    /// <summary>The text it arrived as. The core keeps this next to the parsed tree, not instead.</summary>
    public string Payload { get; } = payload ?? "";
}

/// <summary>
/// PP212: the queue between the websocket thread and the holepunch session.
///
/// A singly linked list with a front and a rear in the core, and the two ends are not
/// interchangeable: the websocket thread pushes at the rear, and the REAR is what every wait
/// watches to decide whether anything new has arrived. Ordinary FIFO here, but with one departure
/// from an ordinary queue that is the reason it exists at all:
///
/// nothing a wait finds is removed by the wait. <see cref="Clear"/> is a separate call the caller
/// makes when it has consumed a notification, and a caller that forgets finds the same one again
/// on its next wait. That is not a leak to tidy - it is what lets two waits look at one arrival.
/// </summary>
public sealed class NotificationQueue
{
    private readonly List<QueuedNotification> items = [];

    /// <summary>The oldest notification, or null when there is none.</summary>
    public QueuedNotification? Front => items.Count > 0 ? items[0] : null;

    /// <summary>The newest, which is what a wait compares its cursor against.</summary>
    public QueuedNotification? Rear => items.Count > 0 ? items[^1] : null;

    /// <summary>How many are held.</summary>
    public int Count => items.Count;

    /// <summary>Everything held, oldest first - the order the scan walks.</summary>
    public IReadOnlyList<QueuedNotification> Items => items;

    /// <summary>The websocket thread's end.</summary>
    public void Enqueue(QueuedNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        items.Add(notification);
    }

    /// <summary>Drops the front, which is what the teardown does until nothing is left.</summary>
    /// <returns>False when there was nothing to drop, which the core also treats as fine.</returns>
    public bool Dequeue()
    {
        if (items.Count == 0)
            return false;

        items.RemoveAt(0);
        return true;
    }

    /// <summary>
    /// Removes one notification BY IDENTITY, wherever it sits. This is the caller's "I have
    /// consumed this", and it is the only thing that shortens the queue during a session.
    /// </summary>
    /// <returns>
    /// False when the notification is not on the queue. The core returns its unknown error for
    /// exactly that case rather than treating a double-clear as success.
    /// </returns>
    public bool Clear(QueuedNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], notification))
            {
                items.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Empties it, the way the teardown does: front first until there is no front.</summary>
    public void Drain()
    {
        while (Front is not null)
            Dequeue();
    }
}

/// <summary>Why a wait woke: the condition variable was signalled, or it timed out by itself.</summary>
public enum NotificationWake
{
    /// <summary>Something was enqueued, or the wake was spurious.</summary>
    Signalled,

    /// <summary>The condition variable's own timeout elapsed.</summary>
    TimedOut,
}

/// <summary>What one turn of the wait decided.</summary>
public enum NotificationWaitOutcome
{
    /// <summary>A notification meeting the mask was found; it is on <see cref="NotificationWait.Match"/>.</summary>
    Matched,

    /// <summary>Nothing yet - sleep again.</summary>
    KeepWaiting,

    /// <summary>The deadline has passed, by the one comparison that can notice.</summary>
    TimedOut,

    /// <summary>A stop was asked for, and taking it here is what consumes it.</summary>
    Cancelled,
}

/// <summary>
/// PP212: waiting for a notification, which is a cursor walk and a timeout that is not one.
///
/// Split from the condition variable it runs on for the reason
/// <see cref="ChiakiNg.Session.FocusChainBehavior.Decide"/> gives: sleeping needs a thread, and
/// deciding whether to sleep again needs nothing but a clock. Everything below is driven by the
/// caller's loop, in the same order the core's loop drives it.
///
/// TWO RULES AND A DEFECT.
///
/// The cursor. <c>last_known</c> is not "the last one consumed" - it is how far this ONE call has
/// scanned. The wait sleeps while the queue's rear is that cursor, scans forward from the cursor's
/// successor, and advances it past everything that does not meet the mask. Nothing is removed;
/// see <see cref="NotificationQueue.Clear"/> for who does that.
///
/// The stop. It is read only after a wake, so a wait that keeps finding matches never sees one -
/// and reading it sets it back to false. One stop is consumed by one wait, which is the same
/// one-shot shape PP172 found on the mapping screen.
///
/// And the defect. The elapsed check lives INSIDE the branch taken when the condition variable
/// itself timed out, and the comparison is strict. Two things follow, and neither is what the
/// caller passing thirty seconds expects. An arrival that does not match re-enters the wait with
/// the FULL timeout again, so traffic for other things keeps the wait alive past its deadline. And
/// in silence, the first wake lands exactly ON the deadline, which a strict comparison does not
/// accept - so the earliest a timeout can be reported is TWO timeouts in. See
/// <see cref="EarliestTimeoutUs"/>. Reproduced, not fixed.
/// </summary>
public sealed class NotificationWait
{
    /// <summary>The core's own conversion, which is where the two units meet.</summary>
    public const long MicrosecondsPerMillisecond = 1000;

    private readonly NotificationQueue queue;
    private readonly PushNotificationType mask;
    private readonly long timeoutMs;
    private readonly long startedAtUs;

    /// <param name="queue">The queue to walk. Held, not copied: it grows under the wait.</param>
    /// <param name="mask">The types this wait will accept, ORed together.</param>
    /// <param name="timeoutMs">What the caller asked for. See the class note for what it buys.</param>
    /// <param name="startedAtUs">The monotonic clock when the wait began.</param>
    public NotificationWait(
        NotificationQueue queue, PushNotificationType mask, long timeoutMs, long startedAtUs)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutMs);

        this.queue = queue;
        this.mask = mask;
        this.timeoutMs = timeoutMs;
        this.startedAtUs = startedAtUs;
    }

    /// <summary>What was found, once <see cref="Scan"/> has returned Matched.</summary>
    public QueuedNotification? Match { get; private set; }

    /// <summary>How far this call has scanned. Null until the first pass.</summary>
    public QueuedNotification? Cursor { get; private set; }

    /// <summary>A stop asked for from outside. Taking it in <see cref="Wake"/> puts it back.</summary>
    public bool CancelRequested { get; set; }

    /// <summary>
    /// Whether there is nothing past the cursor, which is the core's condition for sleeping. An
    /// empty queue counts, and so does a queue whose newest entry this call has already seen.
    /// </summary>
    public bool MustSleep => queue.Rear is null || ReferenceEquals(queue.Rear, Cursor);

    /// <summary>Whether the deadline has passed, by the strict comparison the core makes.</summary>
    public bool DeadlinePassed(long nowUs)
        => nowUs - startedAtUs > timeoutMs * MicrosecondsPerMillisecond;

    /// <summary>
    /// The earliest moment this wait can report a timeout, which is NOT the deadline.
    ///
    /// The condition variable is given the whole timeout, so the first wake lands where elapsed
    /// EQUALS the timeout - and the check is strict, so that wake does not qualify and the wait
    /// sleeps another full one. This is the number, stated rather than discovered in a log.
    /// </summary>
    public long EarliestTimeoutUs => startedAtUs + (2 * timeoutMs * MicrosecondsPerMillisecond);

    /// <summary>
    /// What a wake means, in the order the core asks it: the deadline first and only on the
    /// condition variable's own timeout, then the stop, which taking consumes.
    /// </summary>
    public NotificationWaitOutcome Wake(NotificationWake wake, long nowUs)
    {
        if (wake == NotificationWake.TimedOut && DeadlinePassed(nowUs))
            return NotificationWaitOutcome.TimedOut;

        if (CancelRequested)
        {
            CancelRequested = false;
            return NotificationWaitOutcome.Cancelled;
        }

        return NotificationWaitOutcome.KeepWaiting;
    }

    /// <summary>
    /// The scan: forward from the cursor to the end, stopping at the first type that meets the
    /// mask and advancing the cursor past every one that does not.
    /// </summary>
    public NotificationWaitOutcome Scan()
    {
        IReadOnlyList<QueuedNotification> items = queue.Items;

        for (int i = StartOfScan(items); i < items.Count; i++)
        {
            if (PushNotification.Matches(items[i].Type, mask))
            {
                Match = items[i];
                return NotificationWaitOutcome.Matched;
            }

            Cursor = items[i];
        }

        return NotificationWaitOutcome.KeepWaiting;
    }

    private int StartOfScan(IReadOnlyList<QueuedNotification> items)
    {
        if (Cursor is null)
            return 0;

        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], Cursor))
                return i + 1;
        }

        // The cursor has been cleared out from under us. In the core this is a pointer into freed
        // memory and there is no behaviour to be faithful to, so the port picks the safe reading -
        // start again from the front - rather than inventing one that looks deliberate.
        return 0;
    }
}

/// <summary>
/// PP212: the queue and the wait where the core writes them.
/// </summary>
public static class NotificationQueueSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the queue is still a front and a rear, both starting empty.</summary>
    public static bool TheQueueIsStillFrontAndRear(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("nq->front = nq->rear = NULL;", StringComparison.Ordinal)
            && core.Contains("nq->front = nq->rear = notif;", StringComparison.Ordinal);
    }

    /// <summary>Whether the wait still sleeps on the REAR rather than on the queue being empty.</summary>
    public static bool TheWaitStillWatchesTheRear(string core)
        => Body(core).Contains("->rear == last_known", StringComparison.Ordinal);

    /// <summary>And still scans forward from the cursor rather than from the front every time.</summary>
    public static bool TheScanStillStartsAfterTheCursor(string core)
        => Body(core).Contains("notif = last_known->next;", StringComparison.Ordinal);

    /// <summary>Whether the wait still removes nothing it finds.</summary>
    public static bool TheWaitStillRemovesNothing(string core)
    {
        string body = Body(core);
        return body.Length > 0
            && !body.Contains("dequeueNq", StringComparison.Ordinal)
            && !body.Contains("clear_notification", StringComparison.Ordinal)
            && !body.Contains("free(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the elapsed check is still reachable only through the condition variable's own
    /// timeout - the defect, asserted as still being there.
    /// </summary>
    public static bool TheDeadlineIsStillInsideTheTimeoutBranch(string core)
    {
        string body = Body(core);

        int branch = body.IndexOf("if (err == CHIAKI_ERR_TIMEOUT)", StringComparison.Ordinal);
        int elapsed = body.IndexOf("(now - waiting_since) >", StringComparison.Ordinal);

        return branch >= 0 && elapsed > branch;
    }

    /// <summary>And whether that comparison is still strict, which is what costs the second wait.</summary>
    public static bool TheComparisonIsStillStrict(string core)
        => Body(core).Contains(
            "(now - waiting_since) > (timeout_ms * MILLISECONDS_US)", StringComparison.Ordinal);

    /// <summary>Whether the stop is still read only after a wake, and still consumed by reading.</summary>
    public static bool TheStopIsStillAOneShotReadAfterTheWake(string core)
    {
        string body = Body(core);

        int wait = body.IndexOf("chiaki_cond_timedwait", StringComparison.Ordinal);
        int stop = body.IndexOf("session->main_should_stop", StringComparison.Ordinal);

        return wait >= 0
            && stop > wait
            && body.Contains("session->main_should_stop = false;", StringComparison.Ordinal);
    }

    /// <summary>
    /// wait_for_notification's body, cut at the two lines that bound it. Sliced rather than
    /// searched whole because half of these ask what is NOT in the function, and the same words
    /// appear all over a file this size.
    /// </summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int start = core.IndexOf(
            "uint64_t waiting_since = chiaki_time_now_monotonic_us();", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf(
            "static ChiakiErrorCode clear_notification(", start, StringComparison.Ordinal);

        return end < 0 ? core[start..] : core[start..end];
    }
}
