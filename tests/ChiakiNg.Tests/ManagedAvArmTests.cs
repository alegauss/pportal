using System.Buffers.Binary;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP680, under PP27: takion_handle_packet_av assembled, and driven over a console's own stream.
///
/// The branch had two ends and no middle. <see cref="TakionReceivePath"/> said a datagram was video
/// and <see cref="AvReorderTimeout"/> knew what a queue should do next; between them were the gates,
/// the parse, the audio shortcut, the queue opened on its first packet, the entry, the stage stamp
/// and the push.
///
/// THE ORACLE IS THE FLUSH ITSELF, which is what the criterion asks for. The corpus's video heads go
/// through the assembled arm, and the same sequence numbers go through a bare
/// <see cref="ReorderQueue"/> driven by <see cref="AvReorderTimeout.Flush"/> on the same clock. The
/// two must deliver the same packets in the same order and drop the same ones - so an arm that
/// reproduced the ordering with a private copy of the rule, or that perturbed it while bookkeeping,
/// fails here rather than on a console.
/// </summary>
public class ManagedAvArmTests(ITestOutputHelper output)
{
    private static readonly byte[] Key = [.. Enumerable.Range(0, 16).Select(i => (byte)(0x10 + i))];
    private static readonly byte[] Iv = [.. Enumerable.Range(0, 16).Select(i => (byte)(0xA0 + i))];

    /// <summary>A sink that remembers the order it was handed things in.</summary>
    private sealed class Recording : IAvArmSink
    {
        public List<ushort> PacketIndexes { get; } = [];

        public List<bool> WasVideo { get; } = [];

        public void Av(in AvPacket packet, Span<byte> datagram)
        {
            PacketIndexes.Add(packet.PacketIndex);
            WasVideo.Add(packet.IsVideo);
        }
    }

    private sealed class Outbound : IVideoReceiverOutbound
    {
        public void SendCorruptFrame(ushort from, ushort to) { }

        public bool SendIdrRequest() => true;

        public void FecFailure(int frameIndex, bool idrRequestSent) { }
    }

    private sealed class SilentAudio : IAudioSink
    {
        public int Audios { get; private set; }

        public void Audio(in AvPacket packet, ReadOnlySpan<byte> payload) => Audios++;

        public void Haptics(in AvPacket packet, ReadOnlySpan<byte> payload) { }
    }

    /// <summary>A clock a test writes, so both sides of a comparison read the same microsecond.</summary>
    private sealed class ScriptedClock(params long[] readings)
    {
        private int at;

        /// <summary>The next reading, or the last one for ever after.</summary>
        public long Now() => readings[Math.Min(at++, readings.Length - 1)];

        /// <summary>How many times it was read, which is itself part of the behaviour.</summary>
        public int Reads => at;
    }

    /// <summary>One video datagram with a chosen packet and unit index, long enough to parse.</summary>
    private static byte[] Video(ushort packetIndex, ushort unitIndex, int size = 40)
    {
        byte[] datagram = Filled(TakionDispatch.Video, size);

        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(1), packetIndex);

        // The video layout: unit index at bit 21, total minus one at bit 10, FEC in the low ten.
        uint dword2 = (uint)unitIndex << 0x15;
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(5), dword2);

        return datagram;
    }

    private static byte[] Filled(int baseType, int size)
    {
        var datagram = new byte[size];
        datagram[0] = (byte)baseType;

        for (var i = 1; i < size; i++)
            datagram[i] = (byte)(i + 0x10);

        return datagram;
    }

    /// <summary>
    /// THE CRITERION: the arm delivers the corpus's video in AvReorderTimeout's order.
    ///
    /// PP608's heads seed and order the queue - packet index at buf+1 and unit index inside the
    /// packed word at buf+5, both inside the eighteen bytes the capture kept. The bytes AFTER them
    /// are synthetic, and have to be: the v9 video header is twenty-five bytes and the capture
    /// stops at eighteen, so a parse of the head alone is refused for its length. Nothing the
    /// ordering depends on comes from the padding.
    ///
    /// The reference side has no gates, no parse and no entries - it is sequence numbers pushed onto
    /// a queue seeded the same way and flushed on the same clock. That is what makes this a check on
    /// the ARM rather than on the rule it uses.
    /// </summary>
    [Fact]
    public void TheArmDeliversTheCorpusVideoInTheFlushesOrder()
    {
        if (DatagramCorpus.Read() is not { } corpus)
            return;

        var sink = new Recording();
        long nowUs = 0;

        using var arm = new ManagedAvArm(sink, clock: () => nowUs);

        var reference = new ManagedKeyState();
        ReorderQueue? referenceQueue = null;
        var referenceWait = default(AvHeadWait);
        var referenceDispatched = new List<ulong>();

        var queued = 0;

        foreach (CapturedDatagram datagram in corpus)
        {
            if (datagram.BaseType != TakionDispatch.Video)
                continue;

            byte[] bytes = Padded(datagram.Head, 40);
            nowUs = datagram.ArrivalMicroseconds;

            AvArmOutcome outcome = arm.Handle(TakionDispatch.Video, bytes);
            Assert.Equal(AvArmOutcome.Queued, outcome);
            queued++;

            // The reference: the same two indices, read again, and nothing else.
            AvPacket packet = AvPacketParse.Parse(false, reference, bytes, out _)!.Value;

            if (referenceQueue is null)
            {
                ushort begin = packet.PacketIndex;
                if (packet.UnitIndex > 0)
                    begin = (ushort)(packet.PacketIndex - packet.UnitIndex);

                referenceQueue = new ReorderQueue(AvReorderTimeout.VideoQueueSizeExp, begin)
                {
                    DropStrategy = ReorderDropStrategy.Begin,
                };

                referenceWait = new AvHeadWait(0, begin);
            }

            referenceQueue.Push(packet.PacketIndex, 0);

            AvFlushOutcome flushed = AvReorderTimeout.Flush(referenceQueue, nowUs, referenceWait);
            referenceWait = flushed.Wait;
            referenceDispatched.AddRange(flushed.Dispatched);
        }

        Assert.NotNull(referenceQueue);

        output.WriteLine(
            $"{queued} video heads: {arm.Dispatched.Count} dispatched, "
                + $"{arm.Dropped} dropped, {arm.Held} still held");

        Assert.True(queued > 0, "the capture holds no video, so nothing was compared");
        Assert.True(arm.Dispatched.Count > 0, "nothing was dispatched, so the order is about an empty list");

        Assert.Equal(referenceDispatched, arm.Dispatched);
        Assert.Equal(
            referenceQueue.Drops.Select(one => one.SeqNum),
            arm.VideoQueue!.Drops.Select(one => one.SeqNum));

        // What went out is what the sink saw, in the same order - the join between the queue's
        // sequence numbers and the packets the far end actually received.
        Assert.Equal(arm.Dispatched, sink.PacketIndexes.Select(one => (ulong)one));
        Assert.All(sink.WasVideo, Assert.True);

        // Nothing leaked: every entry is either out, dropped, or still in the window.
        Assert.Equal((int)arm.VideoQueue.Count - CountGaps(arm.VideoQueue), arm.Held);
    }

    /// <summary>How many slots of the window are gaps, so what is HELD can be stated exactly.</summary>
    private static int CountGaps(ReorderQueue queue)
    {
        var gaps = 0;

        for (ulong i = 0; i < queue.Count; i++)
        {
            if (queue.Peek(i) is null)
                gaps++;
        }

        return gaps;
    }

    /// <summary>A head with a deterministic tail, because the capture stops before the header ends.</summary>
    private static byte[] Padded(byte[] head, int size)
    {
        byte[] padded = new byte[Math.Max(size, head.Length)];
        head.CopyTo(padded, 0);

        for (int i = head.Length; i < padded.Length; i++)
            padded[i] = (byte)(i + 0x10);

        return padded;
    }

    /// <summary>
    /// THE VIDEO GATE RUNS BEFORE THE PARSE, which is visible in the ledger and nowhere else.
    ///
    /// Both halves matter and only one of them is obvious. The packet is dropped - that is the
    /// obvious half - and the key-position ledger has not moved, which is what says the parse never
    /// ran. A port that gated after parsing would drop the same packets and advance the counter for
    /// every one of them, and the session would key its cipher at an offset nothing else agrees
    /// with.
    /// </summary>
    [Fact]
    public void VideoDisabledDropsBeforeTheParseAndLeavesTheLedgerAlone()
    {
        var sink = new Recording();
        using var arm = new ManagedAvArm(sink) { Disabled = AudioVideoDisabled.Video };

        byte[] datagram = Video(packetIndex: 7, unitIndex: 0);

        // A key position the parse would certainly commit, so "the ledger did not move" is a claim
        // about the parse rather than about the value happening to be zero.
        BinaryPrimitives.WriteUInt32BigEndian(datagram.AsSpan(0xe), 0x1234_5678);

        Assert.Equal(AvArmOutcome.VideoDisabled, arm.Handle(TakionDispatch.Video, datagram));
        Assert.Empty(sink.PacketIndexes);
        Assert.Null(arm.VideoQueue);
        Assert.Equal(0UL, arm.Ledger.Previous);

        // And with the gate off, the same datagram does move it - so the check above is about the
        // gate and not about a ledger that never moves.
        using var open = new ManagedAvArm(new Recording());
        open.Handle(TakionDispatch.Video, datagram);
        Assert.NotEqual(0UL, open.Ledger.Previous);
    }

    /// <summary>
    /// The audio gate runs AFTER the parse, and spares haptics.
    ///
    /// Haptics rides the audio base type, so a gate that ran first could not tell them apart: the
    /// bit it has to test is one the parse produces. A port that silenced both would turn off the
    /// pad whenever somebody muted the stream, which is a setting doing something it does not say.
    /// </summary>
    [Fact]
    public void AudioDisabledSparesHapticsAndStillRanTheParse()
    {
        var sink = new Recording();
        using var arm = new ManagedAvArm(sink) { Disabled = AudioVideoDisabled.Audio, V12 = true };

        byte[] plain = Filled(TakionDispatch.Audio, 40);
        Assert.Equal(AvArmOutcome.AudioDisabled, arm.Handle(TakionDispatch.Audio, plain));
        Assert.Empty(sink.PacketIndexes);

        // The parse ran, which is what makes the exception below reachable at all.
        Assert.NotEqual(0UL, arm.Ledger.Previous);

        byte[] haptics = Filled(TakionDispatch.Audio, 40);
        haptics[1 + AvPacketParse.FixedHeader + 1] = AvPacketParse.HapticsMarker;

        Assert.Equal(AvArmOutcome.Audio, arm.Handle(TakionDispatch.Audio, haptics));
        Assert.Single(sink.PacketIndexes);
        Assert.Equal([false], sink.WasVideo);
    }

    /// <summary>Audio goes straight out: no queue is opened and nothing is held.</summary>
    [Fact]
    public void AudioIsHandedStraightOnWithNoQueue()
    {
        var sink = new Recording();
        using var arm = new ManagedAvArm(sink);

        Assert.Equal(AvArmOutcome.Audio, arm.Handle(TakionDispatch.Audio, Filled(TakionDispatch.Audio, 40)));

        Assert.Single(sink.PacketIndexes);
        Assert.Null(arm.VideoQueue);
        Assert.Equal(0, arm.Held);
        Assert.Empty(arm.Dispatched);
    }

    /// <summary>A datagram the parse refuses is dropped, and opens nothing.</summary>
    [Fact]
    public void AParseFailureDropsAndOpensNoQueue()
    {
        var sink = new Recording();
        using var arm = new ManagedAvArm(sink);

        // Twenty bytes: past the audio bound and well short of video's twenty-five.
        Assert.Equal(AvArmOutcome.ParseFailed, arm.Handle(TakionDispatch.Video, Filled(TakionDispatch.Video, 20)));

        Assert.Empty(sink.PacketIndexes);
        Assert.Null(arm.VideoQueue);
    }

    /// <summary>
    /// THE SEED: the queue opens at packet_index MINUS unit_index, not at packet_index.
    ///
    /// A stream is joined mid-frame more often than at a frame boundary. Seeding at the packet that
    /// arrived puts the earlier units of that frame BEHIND the window, where the queue drops them as
    /// older than begin - a first frame missing its head on every single connect, which decodes as a
    /// green flash and blames the console.
    ///
    /// So the packet that opens the queue at index 20 with unit 5 is held rather than dispatched:
    /// begin is 15 and fifteen has not arrived.
    /// </summary>
    [Fact]
    public void TheQueueOpensAtPacketIndexMinusUnitIndex()
    {
        var sink = new Recording();
        using var arm = new ManagedAvArm(sink);

        Assert.Equal(AvArmOutcome.Queued, arm.Handle(TakionDispatch.Video, Video(packetIndex: 20, unitIndex: 5)));

        Assert.NotNull(arm.VideoQueue);
        Assert.Equal(15UL, arm.VideoQueue.Begin);
        Assert.Equal(ReorderDropStrategy.Begin, arm.VideoQueue.DropStrategy);
        Assert.Empty(sink.PacketIndexes);
        Assert.Equal(1, arm.Held);

        // And the units before it are still reachable, which is the whole point of the seed.
        arm.Handle(TakionDispatch.Video, Video(packetIndex: 15, unitIndex: 0));
        arm.Handle(TakionDispatch.Video, Video(packetIndex: 16, unitIndex: 1));

        Assert.Equal([(ushort)15, 16], sink.PacketIndexes);
    }

    /// <summary>
    /// And a packet at unit zero opens the queue at its own index, which the guard is what decides.
    /// </summary>
    [Fact]
    public void AFirstUnitOpensTheQueueAtItsOwnIndex()
    {
        var sink = new Recording();
        using var arm = new ManagedAvArm(sink);

        Assert.Equal(AvArmOutcome.Queued, arm.Handle(TakionDispatch.Video, Video(packetIndex: 20, unitIndex: 0)));

        Assert.Equal([(ushort)20], sink.PacketIndexes);
        Assert.Equal(0, arm.Held);
    }

    /// <summary>
    /// A QUEUE THAT WILL NOT OPEN DISPATCHES UNREORDERED, and is tried again on the next packet.
    ///
    /// The C's init fails on an allocation and the managed one cannot, so this is reached through
    /// the factory - the same seam PP367 left for a decrypt that cannot fail managed for the C's
    /// reason. What is being asserted is that the arm is not a drop: the packet reaches the receiver
    /// out of order, which is a visible glitch, where losing it silently is a missing frame nobody
    /// can account for.
    /// </summary>
    [Fact]
    public void AQueueThatWillNotOpenDispatchesUnreorderedAndIsRetried()
    {
        var sink = new Recording();
        var refused = 0;

        using var arm = new ManagedAvArm(
            sink,
            videoQueueFactory: (exp, begin) => refused++ == 0 ? null : new ReorderQueue(exp, begin));

        Assert.Equal(
            AvArmOutcome.Unreordered, arm.Handle(TakionDispatch.Video, Video(packetIndex: 9, unitIndex: 0)));

        Assert.Equal([(ushort)9], sink.PacketIndexes);
        Assert.Null(arm.VideoQueue);
        Assert.Empty(arm.Dispatched);

        // Tried again, because the C leaves video_queue_initialized false and comes back here.
        Assert.Equal(
            AvArmOutcome.Queued, arm.Handle(TakionDispatch.Video, Video(packetIndex: 10, unitIndex: 0)));

        Assert.NotNull(arm.VideoQueue);
        Assert.Equal(2, refused);
    }

    /// <summary>
    /// The two stage stamps, at the two moments the C reads its clock for them.
    ///
    /// The receive stage is the PARSE's cost - the clock read before the gate against the one read
    /// after the entry is built - and the reorder stage is the DWELL, read again inside the pull
    /// loop rather than reused from the top of the flush. Four readings and each one is placed, so a
    /// stamp taken at the wrong point reports a plausible number about the wrong thing.
    /// </summary>
    [Fact]
    public void BothStagesAreChargedAtTheMomentsTheCReadsTheClock()
    {
        using var receive = new BaselineStat();
        using var reorder = new BaselineStat();

        var clock = new ScriptedClock(100, 150, 150, 175);
        var sink = new Recording();

        using var arm = new ManagedAvArm(
            sink, clock: clock.Now, stageReceive: receive, stageReorder: reorder);

        Assert.Equal(AvArmOutcome.Queued, arm.Handle(TakionDispatch.Video, Video(packetIndex: 3, unitIndex: 0)));

        Assert.Equal(4, clock.Reads);

        Assert.Equal(1UL, receive.Samples);
        Assert.Equal(50UL, receive.MinimumUs);

        Assert.Equal(1UL, reorder.Samples);
        Assert.Equal(25UL, reorder.MinimumUs);
    }

    /// <summary>
    /// A dropped packet's entry goes with it, which is what takion_av_drop frees.
    ///
    /// The duplicate is the cheapest of the queue's four drop occasions to reach and the one a port
    /// leaks on quietly: the packet is refused, the arm keeps holding its bytes, and the loss only
    /// shows up as memory on a connection bad enough to retransmit.
    /// </summary>
    [Fact]
    public void ADroppedPacketReleasesItsEntry()
    {
        var sink = new Recording();
        using var arm = new ManagedAvArm(sink);

        // Opens at 0 and waits for it, so the packet at 5 is held rather than dispatched.
        arm.Handle(TakionDispatch.Video, Video(packetIndex: 5, unitIndex: 5));
        Assert.Equal(1, arm.Held);
        Assert.Equal(0, arm.Dropped);

        // The same index again: a duplicate, which the queue drops.
        arm.Handle(TakionDispatch.Video, Video(packetIndex: 5, unitIndex: 5));

        Assert.Equal(1, arm.Dropped);
        Assert.Equal(1, arm.Held);
        Assert.Empty(sink.PacketIndexes);
    }

    /// <summary>
    /// And disposing reports everything still queued as dropped, so nothing is left behind.
    ///
    /// chiaki_reorder_queue_fini fires the callback for every remaining element and the port's queue
    /// does the same. That is not a formality: those callbacks are how the video path learns a frame
    /// will never complete, and the buffers behind them are the ones a session that ended badly
    /// would otherwise still be holding.
    /// </summary>
    [Fact]
    public void DisposeReleasesWhatIsStillQueued()
    {
        var sink = new Recording();
        var arm = new ManagedAvArm(sink);

        arm.Handle(TakionDispatch.Video, Video(packetIndex: 5, unitIndex: 5));
        Assert.Equal(1, arm.Held);

        arm.Dispose();

        Assert.Equal(1, arm.Dropped);
        Assert.Equal(0, arm.Held);
    }

    /// <summary>
    /// The flush with no packet behind it, which is how a queue stalled on a lost head gets past it.
    ///
    /// The head at 0 never arrives. Before the timeout the flush declines to skip; one microsecond
    /// past it - the deadline test is `&lt;=`, so equal is not past - the window jumps straight to
    /// the first buffered packet and the held entry goes out.
    /// </summary>
    [Fact]
    public void AFlushOnTheTimeoutSkipsTheMissingHead()
    {
        var sink = new Recording();
        long nowUs = 1000;

        using var arm = new ManagedAvArm(sink, clock: () => nowUs);

        arm.Handle(TakionDispatch.Video, Video(packetIndex: 5, unitIndex: 5));
        Assert.Empty(sink.PacketIndexes);

        nowUs = 1000 + AvReorderTimeout.TimeoutUs;
        arm.FlushWithTimeout();
        Assert.Empty(sink.PacketIndexes);

        nowUs = 1000 + AvReorderTimeout.TimeoutUs + 1;
        arm.FlushWithTimeout();

        Assert.Equal([(ushort)5], sink.PacketIndexes);
        Assert.Equal(0, arm.Held);
    }

    /// <summary>A flush before the first video packet does nothing, as the C's guard has it.</summary>
    [Fact]
    public void AFlushBeforeTheFirstVideoPacketDoesNothing()
    {
        var sink = new Recording();
        using var arm = new ManagedAvArm(sink);

        arm.FlushWithTimeout();

        Assert.Null(arm.VideoQueue);
        Assert.Equal(ulong.MaxValue, arm.NextTimeoutMs());
    }

    /// <summary>
    /// The arm's far end is PP667's route, not a test double - which is what "hands StreamAvDispatch"
    /// means.
    ///
    /// One video packet through the whole composition: gate, parse, queue, flush, decrypt, receiver.
    /// The route it comes out on is the join, and it is the one thing neither half asserts alone.
    /// </summary>
    [Fact]
    public void TheArmDrivesStreamAvDispatch()
    {
        var audio = new SilentAudio();
        var frames = new List<int>();

        var receiver = new ManagedVideoReceiver(
            (frame, lost, recovered) => { frames.Add(frame.Length); return true; },
            new Outbound());

        var sink = new StreamAvArmSink(Key, Iv, receiver, audio);
        using var arm = new ManagedAvArm(sink);

        Assert.Equal(AvArmOutcome.Queued, arm.Handle(TakionDispatch.Video, Video(packetIndex: 4, unitIndex: 0)));

        Assert.Equal([AvRoute.Video], sink.Routes);

        // And an audio packet takes the other arm through the same sink.
        Assert.Equal(AvArmOutcome.Audio, arm.Handle(TakionDispatch.Audio, Filled(TakionDispatch.Audio, 40)));

        Assert.Equal([AvRoute.Video, AvRoute.Audio], sink.Routes);
        Assert.Equal(1, audio.Audios);
    }

    /// <summary>A base type that is neither is the caller's bug, and says so rather than queueing it.</summary>
    [Theory]
    [InlineData(TakionDispatch.Control)]
    [InlineData(0x0f)]
    public void ABaseTypeThatIsNeitherIsRefused(int baseType)
    {
        using var arm = new ManagedAvArm(new Recording());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => arm.Handle(baseType, Filled(baseType, 40)));
    }

    /// <summary>The C's own body, so the composition above cannot drift off the order it copies.</summary>
    [Fact]
    public void TheCsBodyStillHasTheOrderThisArmReproduces()
    {
        if (ManagedAvArmSource.Locate() is not { } path)
            return;

        string body = Assert.IsType<string>(ManagedAvArmSource.Body(File.ReadAllText(path)));

        Assert.True(
            ManagedAvArmSource.TheVideoGateIsBeforeTheParseAndTheAudioGateAfter(body),
            "the gates are no longer either side of the parse");

        Assert.True(ManagedAvArmSource.TheAudioGateSparesHaptics(body));
        Assert.True(ManagedAvArmSource.TheQueueOpensAtPacketIndexMinusUnitIndex(body));
        Assert.True(ManagedAvArmSource.TheDropStrategyAndCallbackAreSetOnInit(body));
        Assert.True(ManagedAvArmSource.AFailedInitDispatchesUnreordered(body));
        Assert.True(ManagedAvArmSource.TheReceiveStageIsChargedBeforeThePush(body));
    }

    /// <summary>
    /// And each predicate refuses a body that lost the thing it names, so none of them is a
    /// tautology over any C at all.
    /// </summary>
    [Fact]
    public void EachPredicateRefusesABodyThatLostIt()
    {
        Assert.False(ManagedAvArmSource.TheVideoGateIsBeforeTheParseAndTheAudioGateAfter("nothing"));
        Assert.False(ManagedAvArmSource.TheAudioGateSparesHaptics("if(disabled) free(buf);"));
        Assert.False(ManagedAvArmSource.TheQueueOpensAtPacketIndexMinusUnitIndex(
            "ChiakiSeqNum16 queue_begin = packet.packet_index;"));
        Assert.False(ManagedAvArmSource.TheDropStrategyAndCallbackAreSetOnInit(
            "chiaki_reorder_queue_set_drop_cb(queue, takion_av_drop, takion);"));
        Assert.False(ManagedAvArmSource.AFailedInitDispatchesUnreordered("free(buf); return;"));
        Assert.False(ManagedAvArmSource.TheReceiveStageIsChargedBeforeThePush(
            "chiaki_reorder_queue_push(queue, packet.packet_index, entry);"));
    }
}
