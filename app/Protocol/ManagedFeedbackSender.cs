using System.Diagnostics;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// A monotonic clock, in the two units feedbacksender.c reads one in.
///
/// An interface because the C reads the clock at four points in one loop and the intervals between
/// them are the behaviour - a test that could only wait would be asserting about a scheduler.
/// </summary>
public interface IMonotonicClock
{
    /// <summary>chiaki_time_now_monotonic_ms.</summary>
    ulong NowMs { get; }

    /// <summary>chiaki_time_now_monotonic_us.</summary>
    ulong NowUs { get; }
}

/// <summary>
/// Stopwatch, which is what a monotonic clock is here.
///
/// Held as an instance and read as ELAPSED rather than as a raw timestamp: multiplying
/// Stopwatch.GetTimestamp() out to microseconds overflows a long after about ten days of uptime,
/// and the failure is a negative interval on a machine nobody rebooted.
/// </summary>
public sealed class MonotonicClock : IMonotonicClock
{
    private readonly Stopwatch since = Stopwatch.StartNew();

    /// <summary>One is enough; the zero it counts from is arbitrary in the C too.</summary>
    public static MonotonicClock Shared { get; } = new();

    /// <inheritdoc/>
    public ulong NowMs => (ulong)since.ElapsedMilliseconds;

    /// <inheritdoc/>
    public ulong NowUs => (ulong)(since.Elapsed.Ticks / 10);
}

/// <summary>
/// A controller as the sender holds one: the half the history diffs and the half the state sends.
/// </summary>
/// <param name="Pad">Buttons, triggers and the two touch slots - what becomes a history event.</param>
/// <param name="Motion">Sticks and the ten motion floats - what becomes a feedback state.</param>
public readonly record struct FeedbackSnapshot(PadSnapshot Pad, FeedbackMotion Motion)
{
    /// <summary>
    /// chiaki_controller_state_set_idle, which is where all three of the C's start.
    ///
    /// PP757: AND IT IS NOW ACTUALLY THAT. The motion half was default(FeedbackMotion) - ten zeroes
    /// - while the C's idle rests under gravity at accel_y = 1 with the identity quaternion. The
    /// comment named the function and the value did not reproduce it, on the three fields
    /// feedbacksender.c initialises with it.
    /// </summary>
    public static FeedbackSnapshot Idle => new(PadSnapshot.Idle, FeedbackMotion.Idle);

    /// <summary>
    /// PP756: both halves off one live state, which is what a pad's push carries.
    ///
    /// The two were readable separately and never together: PadSnapshot.From has existed since the
    /// recorder, and the motion half could not be read at all. So a state arriving from a pad could
    /// only reach this sender as buttons, and its sticks and motion were dropped on the way.
    /// </summary>
    public static FeedbackSnapshot From(ChiakiControllerState state)
        => new(PadSnapshot.From(state), FeedbackMotion.From(state));
}

/// <summary>
/// Where the sender's two packets go: chiaki_takion_send_feedback_state and _history.
///
/// The state goes as a VALUE and the history as BYTES, which is the split the C has rather than a
/// preference: the takion formats a feedback state itself, and the history packet was already
/// formatted by the flush that queued it.
/// </summary>
public interface IFeedbackSink
{
    /// <summary>One feedback state, at its own sequence number.</summary>
    void SendState(ushort seqNum, FeedbackMotion state);

    /// <summary>One history packet, at its own - the two counters are separate.</summary>
    void SendHistory(ushort seqNum, ReadOnlySpan<byte> payload);
}

/// <summary>What one turn of the sender's loop did.</summary>
/// <param name="SentState">Whether a feedback state went out.</param>
/// <param name="SentHistory">Whether a history packet did.</param>
/// <param name="InputToWireUs">The delay sample this tick took, or null where it took none.</param>
public readonly record struct FeedbackTick(bool SentState, bool SentHistory, ulong? InputToWireUs);

/// <summary>
/// PP723, under PP707: feedbacksender.c - the object PP676 and PP717 had no owner for.
///
/// PP712's census owes the run's host three members and they are one piece of work: the init, the
/// fini, and the input delay lifted out just before it. PP676 formats a feedback state and a history
/// event, PP717 turns a controller change into those events, and nothing decided WHEN any of it goes.
///
/// A BUTTON PRESS SENDS NO FEEDBACK STATE. <see cref="EqualsForFeedbackState"/> compares the sticks
/// exactly and the ten motion floats within a hair, and a change that passes it cancels the send the
/// change itself asked for. So a pad whose only moving part is its buttons rides the history alone -
/// and a port that sent a state per change would be sending one per press, at whatever rate the pad
/// samples.
///
/// THE KEEPALIVE IS THE OTHER HALF: with nothing changed at all, one state still goes every 200ms.
/// Between them, the state stream is neither event-driven nor periodic - it is both, and the C's
/// FEEDBACK_STATE_TIMEOUT_MIN_MS is defined, commented TODO at the one place it would apply, and
/// never read.
///
/// AND THE HISTORY IS RESENT ON PURPOSE. A flush truncates the buffer to
/// <see cref="HistoryResendEventCount"/> events rather than emptying it, so the next packet carries
/// them again: redundancy over UDP, where a port that cleared the ring loses a press per lost
/// datagram and looks like a pad with a bad contact.
///
/// The takion is a sink, as PP714 made the congestion report one. What this owns is the decisions.
/// </summary>
public sealed class ManagedFeedbackSender : IDisposable
{
    /// <summary>FEEDBACK_STATE_TIMEOUT_MIN_MS, which the C defines and does not read.</summary>
    public const int StateTimeoutMinMs = 8;

    /// <summary>FEEDBACK_STATE_TIMEOUT_MAX_MS: the keepalive, and the wait's ceiling.</summary>
    public const int StateTimeoutMaxMs = 200;

    /// <summary>FEEDBACK_HISTORY_BUFFER_SIZE: how many events one packet can carry.</summary>
    public const int HistoryBufferSize = 0x10;

    /// <summary>FEEDBACK_HISTORY_RESEND_EVENT_COUNT: what a flush leaves behind to send again.</summary>
    public const int HistoryResendEventCount = 0x4;

    /// <summary>CHIAKI_FEEDBACK_HISTORY_PACKET_QUEUE_SIZE.</summary>
    public const int PacketQueueSize = 0x40;

    /// <summary>CHIAKI_FEEDBACK_HISTORY_PACKET_BUF_SIZE.</summary>
    public const int PacketBufSize = 0x300;

    /// <summary>The window two motion floats are the same within, as the C's CHECKF spells it.</summary>
    public const float MotionEpsilon = 0.0000001f;

    private readonly IFeedbackSink sink;
    private readonly IMonotonicClock clock;
    private readonly Lock gate = new();
    private readonly AutoResetEvent wake = new(false);

    // The history buffer, oldest first. FormatHistory rebuilds the ring from it, so the list's
    // ORDER is the push order and its length is the ring's len.
    private readonly List<byte[]> history = [];

    private readonly byte[]?[] packets = new byte[PacketQueueSize][];
    private readonly int[] packetSizes = new int[PacketQueueSize];
    private readonly List<ulong> inputToWire = [];

    private FeedbackSnapshot controllerState = FeedbackSnapshot.Idle;
    private FeedbackSnapshot controllerStatePrev = FeedbackSnapshot.Idle;
    private PadSnapshot historyPrev = PadSnapshot.Idle;
    private bool controllerStateChanged;
    private bool historyDirty;
    private bool shouldStop;
    private int packetBegin;
    private int packetLen;
    private ushort stateSeqNum;
    private ushort historySeqNum;
    private ulong stateHandedOverUs;
    private ulong lastStateMs;
    private Thread? thread;

    /// <param name="sink">Where the two packets go.</param>
    /// <param name="clock">The clock, which the loop reads four times a turn.</param>
    public ManagedFeedbackSender(IFeedbackSink sink, IMonotonicClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        this.sink = sink;
        this.clock = clock ?? MonotonicClock.Shared;
        lastStateMs = this.clock.NowMs;
    }

    /// <summary>Whether the thread is up. StartFeedbackSender, one of PP712's owed members.</summary>
    public bool Running => thread is not null;

    /// <summary>How many history packets are queued and unsent.</summary>
    public int QueuedPackets => packetLen;

    /// <summary>How many events the next packet would carry.</summary>
    public int PendingEvents => history.Count;

    /// <summary>How many times the packet queue was full and dropped its oldest.</summary>
    public int Overflows { get; private set; }

    /// <summary>How many flushes could not format, which leaves the buffer dirty.</summary>
    public int FormatFailures { get; private set; }

    /// <summary>Every input-to-wire sample taken, oldest first.</summary>
    public IReadOnlyList<ulong> InputToWireUs => inputToWire;

    /// <summary>
    /// PP725: the slot a flush formats into, which is the arithmetic the overflow arm turns on.
    ///
    /// A named function rather than an expression buried in the flush, because the C's full arm
    /// then copies that slot into the one at <paramref name="begin"/> - and when the queue is full
    /// those are the SAME slot. That is provable from this line and nothing else: with a length
    /// equal to the queue's size the modulo returns begin, so the C's memcpy is handed one address
    /// twice. <see cref="FeedbackHistoryOverflow"/> is where the port's answer to that is recorded.
    /// </summary>
    /// <param name="begin">Where the queue's oldest unsent packet is.</param>
    /// <param name="length">How many are queued.</param>
    public static int PacketSlot(int begin, int length) => (begin + length) % PacketQueueSize;

    /// <summary>
    /// controller_state_equals_for_feedback_state: whether a change is worth a state packet.
    ///
    /// The sticks compare exactly and the ten floats within <see cref="MotionEpsilon"/>. The buttons,
    /// the triggers and the touches are NOT here - they are what the history carries, and a change
    /// confined to them cancels the state send it asked for.
    /// </summary>
    public static bool EqualsForFeedbackState(FeedbackMotion a, FeedbackMotion b)
    {
        if (a.LeftX != b.LeftX || a.LeftY != b.LeftY || a.RightX != b.RightX || a.RightY != b.RightY)
            return false;

        return Near(a.GyroX, b.GyroX)
            && Near(a.GyroY, b.GyroY)
            && Near(a.GyroZ, b.GyroZ)
            && Near(a.AccelX, b.AccelX)
            && Near(a.AccelY, b.AccelY)
            && Near(a.AccelZ, b.AccelZ)
            && Near(a.OrientX, b.OrientX)
            && Near(a.OrientY, b.OrientY)
            && Near(a.OrientZ, b.OrientZ)
            && Near(a.OrientW, b.OrientW);
    }

    /// <summary>
    /// chiaki_feedback_sender_set_controller_state: take a change, or notice there is none.
    /// </summary>
    /// <returns>Whether anything moved, which is the C's early return inverted.</returns>
    public bool SetControllerState(FeedbackSnapshot state)
    {
        lock (gate)
        {
            if (state == controllerState)
                return false;

            controllerState = state;

            foreach (HistoryEvent one in FeedbackHistoryRecorder.Record(historyPrev, state.Pad))
                Push(one);

            FlushHistoryLocked();
            historyPrev = state.Pad;

            // Timed from the FIRST unsent change and not from the last. A second press arriving
            // while the first still waits has been waiting since the first, and restamping here
            // would report the queue as empty exactly when it was busiest.
            if (!controllerStateChanged)
                stateHandedOverUs = clock.NowUs;

            controllerStateChanged = true;
        }

        wake.Set();

        return true;
    }

    /// <summary>
    /// One turn of the loop, without a thread - the body made askable, as PP714's Tick is.
    /// </summary>
    public FeedbackTick Tick()
    {
        bool sendState;
        var sendHistory = false;
        FeedbackSnapshot stateNow;
        ulong handedOverUs = 0;
        byte[] packet = [];
        var packetSize = 0;

        lock (gate)
        {
            ulong nowMs = clock.NowMs;
            sendState = nowMs - lastStateMs >= StateTimeoutMaxMs;
            stateNow = controllerState;

            if (controllerStateChanged)
            {
                controllerStateChanged = false;
                handedOverUs = stateHandedOverUs;
                stateHandedOverUs = 0;
                sendState = true;

                // The change cancelling its own send: nothing the state carries moved.
                if (EqualsForFeedbackState(stateNow.Motion, controllerStatePrev.Motion))
                    sendState = false;
            }

            if (packetLen > 0)
            {
                packet = packets[packetBegin] ?? [];
                packetSize = packetSizes[packetBegin];
                packetBegin = (packetBegin + 1) % PacketQueueSize;
                packetLen--;
                sendHistory = true;
            }
        }

        // Both sends happen with the lock RELEASED, which is why the tick reads its own copies above.
        ulong? sample = null;

        if (sendState)
        {
            sink.SendState(stateSeqNum++, stateNow.Motion);

            // Only a handover that reached the socket is a sample: a send driven by the keepalive
            // carries no waiting input, and a change the console does not care about was dropped
            // above without ever being sent.
            if (handedOverUs != 0)
            {
                ulong nowUs = clock.NowUs;
                if (nowUs >= handedOverUs)
                {
                    sample = nowUs - handedOverUs;
                    inputToWire.Add(sample.Value);
                }
            }
        }

        if (sendHistory)
            sink.SendHistory(historySeqNum++, packet.AsSpan(0, packetSize));

        lock (gate)
        {
            if (sendState)
            {
                controllerStatePrev = stateNow;

                // Stamped AFTER the send, so the keepalive's 200ms is measured from the wire.
                lastStateMs = clock.NowMs;
            }
        }

        return new FeedbackTick(sendState, sendHistory, sample);
    }

    /// <summary>
    /// chiaki_feedback_sender_init's thread. StartFeedbackSender, in the run's host.
    /// </summary>
    public void Start()
    {
        if (thread is not null)
            throw new InvalidOperationException("the feedback sender is already running.");

        lock (gate)
        {
            shouldStop = false;
            lastStateMs = clock.NowMs;
        }

        thread = new Thread(Loop) { IsBackground = true, Name = "Chiaki Feedback Sender" };
        thread.Start();
    }

    /// <summary>
    /// chiaki_feedback_sender_fini: flag, signal, join, in that order. FiniFeedbackSender.
    /// </summary>
    public void Stop()
    {
        if (thread is null)
            return;

        lock (gate)
            shouldStop = true;

        wake.Set();
        thread.Join();
        thread = null;
    }

    /// <summary>
    /// LiftInputToWire: hand every sample to the baseline, BEFORE the fini takes the thread down.
    ///
    /// The destination is SessionBaseline.PushInputToWire and it is passed in rather than held,
    /// because the baseline is a native handle and this side of the seam has no use for one.
    /// </summary>
    /// <param name="push">Takes one sample, in microseconds.</param>
    /// <returns>How many were handed over.</returns>
    public int LiftInputToWire(Action<ulong> push)
    {
        ArgumentNullException.ThrowIfNull(push);

        ulong[] taken;
        lock (gate)
            taken = [.. inputToWire];

        foreach (ulong one in taken)
            push(one);

        return taken.Length;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        wake.Dispose();
    }

    private static bool Near(float a, float b)
        => a >= b - MotionEpsilon && a <= b + MotionEpsilon;

    private void Push(HistoryEvent one)
    {
        history.Add(one.Serialise());

        // The ring holds sixteen; a push past that overwrites the oldest.
        if (history.Count > HistoryBufferSize)
            history.RemoveRange(0, history.Count - HistoryBufferSize);

        historyDirty = true;
    }

    /// <summary>
    /// feedback_sender_flush_history_locked: format the buffer into the packet queue.
    /// </summary>
    private void FlushHistoryLocked()
    {
        if (!historyDirty)
            return;

        int index = PacketSlot(packetBegin, packetLen);
        var buf = new byte[PacketBufSize];

        if (FeedbackPayload.FormatHistory(HistoryBufferSize, history, buf, out int written)
            != ChiakiError.Success)
        {
            // The C logs and returns WITHOUT clearing dirty, so the next change tries again.
            FormatFailures++;
            return;
        }

        packets[index] = buf;

        if (packetLen < PacketQueueSize)
        {
            packetSizes[index] = written;
            packetLen++;
        }
        else
        {
            // A full queue drops its OLDEST rather than refusing the newest - and index is begin
            // here, which is why the C's memcpy at this rung copies a buffer onto itself.
            packetSizes[packetBegin] = written;
            packetBegin = (packetBegin + 1) % PacketQueueSize;
            Overflows++;
        }

        // Truncated rather than emptied: the next packet carries these again.
        if (history.Count > HistoryResendEventCount)
            history.RemoveRange(0, history.Count - HistoryResendEventCount);

        historyDirty = false;
    }

    private void Loop()
    {
        while (true)
        {
            int waitMs;
            bool queued;

            lock (gate)
            {
                if (shouldStop)
                    return;

                queued = packetLen > 0;
                ulong since = clock.NowMs - lastStateMs;
                waitMs = since < StateTimeoutMaxMs ? (int)(StateTimeoutMaxMs - since) : StateTimeoutMaxMs;
            }

            // The C waits only where the queue is empty: a queued packet is work already waiting,
            // so the loop goes round again without sleeping on it.
            if (!queued)
                wake.WaitOne(waitMs);

            lock (gate)
            {
                if (shouldStop)
                    return;
            }

            Tick();
        }
    }
}

/// <summary>
/// PP725: the copy on the overflow rung, which this port does not make - as a value, not a comment.
///
/// feedback_sender_flush_history_locked formats into <see cref="ManagedFeedbackSender.PacketSlot"/>
/// and then branches on whether the queue is full. The full arm copies that slot into the one at
/// begin - and on that arm the length IS the queue's size, so the slot it just formatted into
/// reduces to begin. The copy has one address in both arguments.
///
/// IT IS NOT WRONG, IT IS NOTHING. The bytes are already where the copy would put them. memcpy over
/// an identical source and destination is undefined by the standard rather than merely wasted -
/// the case memmove exists for - and nothing has gone wrong here because sixty-four unsent history
/// packets means the stream is in trouble already.
///
/// SO THE PORT LEAVES IT OUT, and this is the record. PP716's rule decided it: a departure is
/// reproduced where the C's flaw is visible to a user or a console and corrected where it is not,
/// and a copy whose bytes are already in place is visible to nobody. PP545's bounded websocket wait
/// is the shape - NativeWaits carries that one as a row rather than as a sentence.
///
/// AND THE ARM IS HELD BY ARITHMETIC, not by a text search. What this port must go on doing is
/// dropping the OLDEST packet, and the identity that makes the C's copy a no-op is the same
/// identity that says which slot is overwritten. Both are the modulo, and both are asserted.
/// </summary>
public static class FeedbackHistoryOverflow
{
    /// <summary>Whether the port makes the copy. It does not, and that is the departure.</summary>
    public const bool ThePortCopies = false;

    /// <summary>What the C's full arm does that this port does not, in one sentence.</summary>
    public const string Departure =
        "the C copies the formatted slot onto itself before advancing begin; this port advances it";

    /// <summary>
    /// Whether the slot a full queue formats into is the slot it overwrites.
    ///
    /// The whole of the finding, as arithmetic. True for every begin, which is what makes the C's
    /// copy a no-op rather than a rare one.
    /// </summary>
    public static bool TheFormattedSlotIsTheOldest(int begin)
        => ManagedFeedbackSender.PacketSlot(begin, ManagedFeedbackSender.PacketQueueSize) == begin;

    /// <summary>And which slot a queue that is NOT full formats into, which is a different one.</summary>
    public static bool TheFormattedSlotIsBeyondTheQueue(int begin, int length)
        => length is > 0 and < ManagedFeedbackSender.PacketQueueSize
            && ManagedFeedbackSender.PacketSlot(begin, length) != begin;
}

/// <summary>
/// PP723: the four numbers and the three decisions, read out of feedbacksender.c.
///
/// Nothing in the C's sender is reachable from a test - the thread owns a takion and every helper
/// is file-local - so what holds this port to it is the file's own text, the way PP717's recorder is
/// held.
/// </summary>
public static class ManagedFeedbackSenderSource
{
    /// <summary>Where the sender is.</summary>
    public const string RelativePath = @"lib\src\feedbacksender.c";

    /// <summary>And the header carrying the packet queue's two sizes.</summary>
    public const string HeaderRelativePath = @"lib\include\chiaki\feedbacksender.h";

    /// <summary>The minimum the C defines and never reads, named so the check can look for it.</summary>
    public const string UnusedMinimum = "FEEDBACK_STATE_TIMEOUT_MIN_MS";

    /// <summary>feedbacksender.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Its header, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>The thread function's body, or null where it is gone.</summary>
    public static string? LoopBody(string source)
        => CFunction.Body(source, "static void *feedback_sender_thread_func(");

    /// <summary>The flush's body, or null.</summary>
    public static string? FlushBody(string source)
        => CFunction.Body(source, "static void feedback_sender_flush_history_locked(");

    /// <summary>A `#define NAME value` in either file, or null where it is not defined.</summary>
    public static string? Defined(string source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(name);

        foreach (string line in source.Split('\n'))
        {
            string text = line.Trim();
            if (!text.StartsWith($"#define {name} ", StringComparison.Ordinal))
                continue;

            string rest = text[$"#define {name} ".Length..].Trim();
            int comment = rest.IndexOf("//", StringComparison.Ordinal);

            return (comment < 0 ? rest : rest[..comment]).Trim();
        }

        return null;
    }

    /// <summary>
    /// Whether the minimum is still defined and read by nothing.
    ///
    /// One `#define` and one mention, and that mention is a TODO inside the branch where it would
    /// apply. Held because it is the difference between a constant this port declined to use and a
    /// constant the C does not use - and only the second is a faithful port.
    /// </summary>
    public static bool TheMinimumIsStillDefinedAndUnused(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (Defined(source, UnusedMinimum) is null)
            return false;

        var mentions = 0;
        for (int at = source.IndexOf(UnusedMinimum, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(UnusedMinimum, at + UnusedMinimum.Length, StringComparison.Ordinal))
        {
            mentions++;
        }

        // The define, and the TODO that says where it would go.
        return mentions == 2 && source.Contains($"TODO: {UnusedMinimum}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the flush still truncates the history buffer instead of emptying it.
    ///
    /// The assignment is the whole of the resend: `len = FEEDBACK_HISTORY_RESEND_EVENT_COUNT` under
    /// a guard that it is above it. A port that reset the buffer would send each event once, and
    /// one lost datagram would then lose a press.
    /// </summary>
    public static bool TheFlushStillTruncatesRatherThanEmpties(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        return flushBody.Contains(
            "history_buf.len = FEEDBACK_HISTORY_RESEND_EVENT_COUNT", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP725: whether the C's full arm still copies the slot it formatted into onto itself.
    ///
    /// Read so the departure <see cref="FeedbackHistoryOverflow"/> records stays honest: the day
    /// upstream removes the copy, the port and the C agree again and that row can go. Both indices
    /// are named, because a copy between two DIFFERENT slots would be a real move and not a no-op.
    /// </summary>
    public static bool TheFullArmStillCopiesTheSlotOntoItself(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        int overflow = flushBody.IndexOf("history packet queue overflow", StringComparison.Ordinal);
        if (overflow < 0)
            return false;

        string arm = flushBody[..overflow];

        return arm.Contains("memcpy(", StringComparison.Ordinal)
            && arm.Contains("history_packets[feedback_sender->history_packet_begin]", StringComparison.Ordinal)
            && arm.Contains("history_packets[packet_index]", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a full packet queue still drops its oldest, rather than refusing the newest.
    ///
    /// The else branch advances begin and logs an overflow. A port that dropped the NEW packet
    /// instead would keep the console's history stale under exactly the load that produced it.
    /// </summary>
    public static bool AFullQueueStillDropsTheOldest(string flushBody)
    {
        ArgumentNullException.ThrowIfNull(flushBody);

        int overflow = flushBody.IndexOf("history packet queue overflow", StringComparison.Ordinal);
        if (overflow < 0)
            return false;

        return flushBody.LastIndexOf(
            "feedback_sender->history_packet_begin + 1", overflow, StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// Whether a change whose feedback state is unchanged still cancels its own send.
    ///
    /// send_feedback_state is set true by the change and then set false again by the comparison.
    /// Two writes to one variable, and the second is the behaviour: a port that only had the first
    /// sends a state per button press.
    /// </summary>
    public static bool AnIrrelevantChangeStillCancelsTheStateSend(string loopBody)
    {
        ArgumentNullException.ThrowIfNull(loopBody);

        int set = loopBody.IndexOf("send_feedback_state = true;", StringComparison.Ordinal);
        if (set < 0)
            return false;

        int compared = loopBody.IndexOf(
            "controller_state_equals_for_feedback_state(", set, StringComparison.Ordinal);
        int cleared = loopBody.IndexOf("send_feedback_state = false;", compared < 0 ? set : compared,
            StringComparison.Ordinal);

        return compared > set && cleared > compared;
    }

    /// <summary>
    /// Whether the loop still skips its wait where a history packet is already queued.
    ///
    /// `if(history_packet_len == 0)` around the timed wait. Without it the sender sleeps on a queue
    /// it could be draining, and a burst of touch events leaves at 200ms intervals.
    /// </summary>
    public static bool AQueuedPacketStillSkipsTheWait(string loopBody)
    {
        ArgumentNullException.ThrowIfNull(loopBody);

        int guard = loopBody.IndexOf(
            "if(feedback_sender->history_packet_len == 0)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        return loopBody.IndexOf("chiaki_cond_timedwait_pred(", guard, StringComparison.Ordinal) > guard;
    }
}
