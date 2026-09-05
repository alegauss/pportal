using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP723: feedbacksender.c - the decisions, the queue, and the thread that owns both.
///
/// Three of the four members PP712's census still owed PP707's host are this object's init, its
/// fini and the input delay lifted out of it, so these hold PP707's second criterion as well:
/// shipping a subsystem shortens that list, and StreamRunHostConsumersTests asserts that it did.
///
/// THE ASSERTIONS THAT MATTER ARE THE THREE DECISIONS. A change whose feedback state is unchanged
/// sends no state - so a button press rides the history alone. A tick with nothing changed still
/// sends one every 200ms. And a flush leaves four events behind instead of emptying the buffer,
/// which is what makes a lost datagram cost nothing.
/// </summary>
public class ManagedFeedbackSenderTests(ITestOutputHelper output)
{
    /// <summary>A clock a test moves by hand, so the intervals are the assertion.</summary>
    private sealed class FakeClock : IMonotonicClock
    {
        public ulong Ms { get; set; }

        public ulong Us { get; set; }

        public ulong NowMs => Ms;

        public ulong NowUs => Us;
    }

    /// <summary>Every packet, in order, safe to read while a thread is still sending.</summary>
    private sealed class Recorder : IFeedbackSink
    {
        private readonly ConcurrentQueue<(ushort Seq, FeedbackMotion State)> states = new();
        private readonly ConcurrentQueue<(ushort Seq, byte[] Payload)> histories = new();

        public IReadOnlyList<(ushort Seq, FeedbackMotion State)> States => [.. states];

        public IReadOnlyList<(ushort Seq, byte[] Payload)> Histories => [.. histories];

        public void SendState(ushort seqNum, FeedbackMotion state) => states.Enqueue((seqNum, state));

        public void SendHistory(ushort seqNum, ReadOnlySpan<byte> payload)
            => histories.Enqueue((seqNum, payload.ToArray()));
    }

    private static FeedbackSnapshot Pressing(ChiakiControllerButton button)
        => FeedbackSnapshot.Idle with { Pad = PadSnapshot.Idle with { Buttons = button } };

    private static FeedbackSnapshot Nudged(short leftX)
        => FeedbackSnapshot.Idle with { Motion = default(FeedbackMotion) with { LeftX = leftX } };

    private static string? Read(string relativePath)
    {
        string? path = SanitizerSource.LocateRelative(relativePath);

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>The four numbers the C names, so a port that rounded 200 to a second is caught.</summary>
    [Fact]
    public void TheTimeoutsAndTheSizesAreTheCs()
    {
        Assert.Equal(8, ManagedFeedbackSender.StateTimeoutMinMs);
        Assert.Equal(200, ManagedFeedbackSender.StateTimeoutMaxMs);
        Assert.Equal(0x10, ManagedFeedbackSender.HistoryBufferSize);
        Assert.Equal(0x4, ManagedFeedbackSender.HistoryResendEventCount);
        Assert.Equal(0x40, ManagedFeedbackSender.PacketQueueSize);
        Assert.Equal(0x300, ManagedFeedbackSender.PacketBufSize);
    }

    /// <summary>
    /// THE DECISION: a press sends a history packet and no feedback state at all.
    ///
    /// The change sets send_feedback_state and the comparison clears it again, which is the arm a
    /// port written from a description does not have. Without it the console gets a state packet per
    /// button press, at whatever rate the pad is sampled.
    /// </summary>
    [Fact]
    public void AChangeThatMovesOnlyButtonsSendsNoState()
    {
        var sink = new Recorder();
        var clock = new FakeClock { Ms = 0, Us = 1000 };
        using var sender = new ManagedFeedbackSender(sink, clock);

        Assert.True(sender.SetControllerState(Pressing(ChiakiControllerButton.Cross)));

        FeedbackTick tick = sender.Tick();

        output.WriteLine($"state {tick.SentState}, history {tick.SentHistory}");

        Assert.False(tick.SentState);
        Assert.True(tick.SentHistory);
        Assert.Empty(sink.States);
        Assert.Single(sink.Histories);
    }

    /// <summary>And a stick that moved sends the state, which is the other half of the same arm.</summary>
    [Fact]
    public void AChangeThatMovesAStickSendsOne()
    {
        var sink = new Recorder();
        var clock = new FakeClock { Ms = 0, Us = 1000 };
        using var sender = new ManagedFeedbackSender(sink, clock);

        sender.SetControllerState(Nudged(1200));
        clock.Us = 3000;

        FeedbackTick tick = sender.Tick();

        Assert.True(tick.SentState);

        // No history: nothing the recorder watches moved, so there was nothing to flush.
        Assert.False(tick.SentHistory);
        Assert.Equal(1200, Assert.Single(sink.States).State.LeftX);
    }

    /// <summary>Motion within a hair is the same state; a hair past it is not.</summary>
    [Fact]
    public void MotionComparesWithinTheCsWindow()
    {
        var a = default(FeedbackMotion) with { GyroX = 1.0f };

        Assert.True(ManagedFeedbackSender.EqualsForFeedbackState(
            a, a with { GyroX = 1.0f + (ManagedFeedbackSender.MotionEpsilon / 2) }));

        Assert.False(ManagedFeedbackSender.EqualsForFeedbackState(
            a, a with { GyroX = 1.0f + (ManagedFeedbackSender.MotionEpsilon * 10) }));

        // The sticks are exact, with no window at all.
        Assert.False(ManagedFeedbackSender.EqualsForFeedbackState(a, a with { LeftY = 1 }));
    }

    /// <summary>
    /// THE KEEPALIVE: with nothing changed, one state still goes every 200ms and takes no sample.
    ///
    /// A send driven by the timeout carries no waiting input, so it must not be counted as an input
    /// delay - a port that sampled every send would report the keepalive's zero as an input that
    /// reached the wire instantly, five times a second.
    /// </summary>
    [Fact]
    public void NothingChangedStillSendsOneEveryTwoHundredMilliseconds()
    {
        var sink = new Recorder();
        var clock = new FakeClock { Ms = 0, Us = 1000 };
        using var sender = new ManagedFeedbackSender(sink, clock);

        Assert.False(sender.Tick().SentState);

        clock.Ms = ManagedFeedbackSender.StateTimeoutMaxMs - 1;
        Assert.False(sender.Tick().SentState);

        clock.Ms = ManagedFeedbackSender.StateTimeoutMaxMs;
        FeedbackTick tick = sender.Tick();

        Assert.True(tick.SentState);
        Assert.Null(tick.InputToWireUs);
        Assert.Empty(sender.InputToWireUs);

        // And the window restarts from the send.
        clock.Ms += ManagedFeedbackSender.StateTimeoutMaxMs - 1;
        Assert.False(sender.Tick().SentState);
    }

    /// <summary>Handing over a state that is already held does nothing at all.</summary>
    [Fact]
    public void TheSameStateTwiceIsNotAChange()
    {
        var sink = new Recorder();
        using var sender = new ManagedFeedbackSender(sink, new FakeClock { Us = 1000 });

        Assert.True(sender.SetControllerState(Nudged(500)));
        Assert.False(sender.SetControllerState(Nudged(500)));
    }

    /// <summary>
    /// The delay is measured from the FIRST unsent change, not from the last.
    ///
    /// Two presses arriving before one tick have both been waiting since the first, and a port that
    /// restamped on each would report the queue as shortest exactly when it was longest.
    /// </summary>
    [Fact]
    public void TheHandoverIsStampedFromTheFirstUnsentChange()
    {
        var sink = new Recorder();
        var clock = new FakeClock { Ms = 0, Us = 1000 };
        using var sender = new ManagedFeedbackSender(sink, clock);

        sender.SetControllerState(Nudged(100));
        clock.Us = 1500;
        sender.SetControllerState(Nudged(200));

        clock.Us = 3000;
        FeedbackTick tick = sender.Tick();

        output.WriteLine($"sample {tick.InputToWireUs}us");

        Assert.True(tick.SentState);
        Assert.Equal(2000ul, tick.InputToWireUs);
        Assert.Equal([2000ul], sender.InputToWireUs);
    }

    /// <summary>
    /// A FLUSH TRUNCATES: four events stay behind so the next packet sends them again.
    ///
    /// Six buttons pressed at once produce six events, and the flush leaves four. That is the
    /// redundancy the C buys over UDP, and a port that emptied the ring would lose a press per lost
    /// datagram - which reads as a controller fault rather than as a network one.
    /// </summary>
    [Fact]
    public void AFlushLeavesFourEventsBehind()
    {
        var sink = new Recorder();
        using var sender = new ManagedFeedbackSender(sink, new FakeClock { Us = 1000 });

        const ChiakiControllerButton six =
            ChiakiControllerButton.Cross
            | ChiakiControllerButton.Moon
            | ChiakiControllerButton.Box
            | ChiakiControllerButton.Pyramid
            | ChiakiControllerButton.DpadLeft
            | ChiakiControllerButton.DpadRight;

        sender.SetControllerState(Pressing(six));

        output.WriteLine($"{sender.PendingEvents} event(s) left, {sender.QueuedPackets} packet(s) queued");

        Assert.Equal(ManagedFeedbackSender.HistoryResendEventCount, sender.PendingEvents);
        Assert.Equal(1, sender.QueuedPackets);
    }

    /// <summary>
    /// A full packet queue drops its OLDEST, and says so, rather than refusing the newest.
    ///
    /// Sixty-five flushes with nothing draining them. A port that dropped the arriving packet would
    /// hold the console's history stale under exactly the load that produced it.
    /// </summary>
    [Fact]
    public void AFullQueueDropsTheOldest()
    {
        var sink = new Recorder();
        using var sender = new ManagedFeedbackSender(sink, new FakeClock { Us = 1000 });

        for (var at = 1; at <= ManagedFeedbackSender.PacketQueueSize; at++)
            sender.SetControllerState(FeedbackSnapshot.Idle with { Pad = PadSnapshot.Idle with { L2 = (byte)at } });

        Assert.Equal(ManagedFeedbackSender.PacketQueueSize, sender.QueuedPackets);
        Assert.Equal(0, sender.Overflows);

        sender.SetControllerState(FeedbackSnapshot.Idle with { Pad = PadSnapshot.Idle with { L2 = 0xff } });

        output.WriteLine($"{sender.QueuedPackets} queued, {sender.Overflows} overflow(s)");

        Assert.Equal(ManagedFeedbackSender.PacketQueueSize, sender.QueuedPackets);
        Assert.Equal(1, sender.Overflows);
    }

    /// <summary>
    /// One packet leaves per tick, at its own sequence number - and the two counters are separate.
    ///
    /// The state and the history each carry a ChiakiSeqNum16 of their own, so a port sharing one
    /// counter would number both streams out of a sequence the console tracks per stream.
    /// </summary>
    [Fact]
    public void EachQueuedPacketLeavesOnceAndTheCountersAreSeparate()
    {
        var sink = new Recorder();
        var clock = new FakeClock { Ms = 0, Us = 1000 };
        using var sender = new ManagedFeedbackSender(sink, clock);

        for (var at = 1; at <= 3; at++)
            sender.SetControllerState(FeedbackSnapshot.Idle with { Pad = PadSnapshot.Idle with { L2 = (byte)at } });

        Assert.Equal(3, sender.QueuedPackets);

        // Three ticks, each 200ms apart so a keepalive state rides with every one of them.
        for (var at = 0; at < 3; at++)
        {
            clock.Ms += ManagedFeedbackSender.StateTimeoutMaxMs;
            Assert.True(sender.Tick().SentHistory);
        }

        Assert.Equal(0, sender.QueuedPackets);
        Assert.False(sender.Tick().SentHistory);

        Assert.Equal([0, 1, 2], sink.Histories.Select(one => (int)one.Seq));

        // Each stream numbers from zero on its own, and the state's count is its own too: the
        // first of these three ticks sent no state, because the change that queued the packets
        // moved nothing the state carries.
        Assert.Equal(2, sink.States.Count);
        Assert.Equal(Enumerable.Range(0, sink.States.Count), sink.States.Select(one => (int)one.Seq));
    }

    /// <summary>Every sample reaches the destination PP712 found already existing.</summary>
    [Fact]
    public void LiftInputToWireHandsOverEverySample()
    {
        var sink = new Recorder();
        var clock = new FakeClock { Ms = 0, Us = 1000 };
        using var sender = new ManagedFeedbackSender(sink, clock);

        sender.SetControllerState(Nudged(10));
        clock.Us = 2000;
        sender.Tick();

        clock.Us = 5000;
        sender.SetControllerState(Nudged(20));
        clock.Us = 5500;
        clock.Ms = ManagedFeedbackSender.StateTimeoutMaxMs;
        sender.Tick();

        var taken = new List<ulong>();

        Assert.Equal(2, sender.LiftInputToWire(taken.Add));
        Assert.Equal([1000ul, 500ul], taken);
    }

    /// <summary>
    /// The thread: start, a change reaches the sink, stop, and a second stop is not an error.
    ///
    /// The two members of the run's host this answers for, driven the way the host would drive them.
    /// </summary>
    [Fact]
    public void TheThreadSendsUntilItIsStopped()
    {
        var sink = new Recorder();
        using var sender = new ManagedFeedbackSender(sink);

        Assert.False(sender.Running);
        sender.Start();
        Assert.True(sender.Running);
        Assert.Throws<InvalidOperationException>(sender.Start);

        sender.SetControllerState(Nudged(4096));

        var clock = Stopwatch.StartNew();
        while (sink.States.Count == 0 && clock.ElapsedMilliseconds < 5000)
            Thread.Sleep(10);

        output.WriteLine($"{sink.States.Count} state(s) in {clock.ElapsedMilliseconds}ms");

        Assert.NotEmpty(sink.States);

        sender.Stop();
        sender.Stop();

        Assert.False(sender.Running);
    }

    /// <summary>
    /// THE DRIFT CHECKS: the three decisions, still where this port read them.
    /// </summary>
    [Fact]
    public void TheCsSenderStillMakesTheDecisionsThisPortCopied()
    {
        if (Read(ManagedFeedbackSenderSource.RelativePath) is not { } source)
            return;

        string? loop = ManagedFeedbackSenderSource.LoopBody(source);
        string? flush = ManagedFeedbackSenderSource.FlushBody(source);

        Assert.NotNull(loop);
        Assert.NotNull(flush);

        Assert.True(
            ManagedFeedbackSenderSource.AnIrrelevantChangeStillCancelsTheStateSend(loop),
            "the loop no longer clears send_feedback_state after comparing the two states");

        Assert.True(
            ManagedFeedbackSenderSource.AQueuedPacketStillSkipsTheWait(loop),
            "the loop no longer skips its wait where a history packet is queued");

        Assert.True(
            ManagedFeedbackSenderSource.TheFlushStillTruncatesRatherThanEmpties(flush),
            "the flush no longer truncates the history buffer to the resend count");

        Assert.True(
            ManagedFeedbackSenderSource.AFullQueueStillDropsTheOldest(flush),
            "a full packet queue no longer drops its oldest");
    }

    /// <summary>
    /// And the two window ends are still the numbers this port follows, the minimum still unread.
    ///
    /// The minimum is the odd one: defined, mentioned once more in a TODO inside the branch where
    /// it would apply, and waited by nobody. Held as a fact about the C, so a port that started
    /// using it would be departing rather than catching up.
    /// </summary>
    [Fact]
    public void TheWindowsTwoEndsAreStillTheseAndTheMinimumIsStillUnread()
    {
        if (Read(ManagedFeedbackSenderSource.RelativePath) is not { } source)
            return;

        Assert.Equal(
            ManagedFeedbackSender.StateTimeoutMinMs.ToString(CultureInfo.InvariantCulture),
            ManagedFeedbackSenderSource.Defined(source, ManagedFeedbackSenderSource.UnusedMinimum));

        Assert.Equal(
            ManagedFeedbackSender.StateTimeoutMaxMs.ToString(CultureInfo.InvariantCulture),
            ManagedFeedbackSenderSource.Defined(source, "FEEDBACK_STATE_TIMEOUT_MAX_MS"));

        Assert.True(
            ManagedFeedbackSenderSource.TheMinimumIsStillDefinedAndUnused(source),
            "FEEDBACK_STATE_TIMEOUT_MIN_MS is now read, or its TODO is gone");
    }

    /// <summary>The packet queue's two sizes come from the header, not from this side.</summary>
    [Fact]
    public void ThePacketQueuesSizesAreTheHeaders()
    {
        if (Read(ManagedFeedbackSenderSource.HeaderRelativePath) is not { } header)
            return;

        Assert.Equal(
            $"0x{ManagedFeedbackSender.PacketQueueSize:x}",
            ManagedFeedbackSenderSource.Defined(header, "CHIAKI_FEEDBACK_HISTORY_PACKET_QUEUE_SIZE"));

        Assert.Equal(
            $"0x{ManagedFeedbackSender.PacketBufSize:x}",
            ManagedFeedbackSenderSource.Defined(header, "CHIAKI_FEEDBACK_HISTORY_PACKET_BUF_SIZE"));
    }
}
