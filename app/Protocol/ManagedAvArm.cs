using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What takion_handle_packet_av did with one datagram.</summary>
public enum AvArmOutcome
{
    /// <summary>Video, and the session had it disabled. Dropped BEFORE the parse.</summary>
    VideoDisabled,

    /// <summary>The parse refused it. Dropped, and the buffer released.</summary>
    ParseFailed,

    /// <summary>Audio, disabled, and not haptics - which is the exception the C carves out.</summary>
    AudioDisabled,

    /// <summary>Audio, handed straight to the callback with no queue in the way.</summary>
    Audio,

    /// <summary>Video, but the queue would not initialise: dispatched unreordered rather than dropped.</summary>
    Unreordered,

    /// <summary>Video, queued, and whatever the flush then pulled has gone out.</summary>
    Queued,
}

/// <summary>
/// TakionAVPacketEntry: the datagram, its parsed header, and when it went into the queue.
/// </summary>
/// <param name="BaseType">
/// Carried because the C carries it, though nothing downstream reads it: only video is ever queued,
/// so the field can only ever say video by the time it is looked at.
/// </param>
/// <param name="Buffer">
/// The datagram, COPIED. The C mallocs the buffer in the receive loop and this entry takes
/// ownership of it; the port's loop hands out a pooled span that is reused on the next receive, so
/// keeping it past the call means copying it.
/// </param>
/// <param name="QueuedUs">
/// When the entry was built, which the reorder stage measures the dwell from. Zero means unstamped,
/// and the flush skips the sample for it - the C's own guard, <c>if(entry->queued_us)</c>.
/// </param>
public readonly record struct AvQueueEntry(int BaseType, byte[] Buffer, AvPacket Packet, long QueuedUs);

/// <summary>Where the arm's dispatch goes: takion's callback with an AV event, as an interface.</summary>
public interface IAvArmSink
{
    /// <summary>One AV packet, with the buffer its offsets name into.</summary>
    /// <param name="datagram">Mutable, because the route below decrypts the payload in place.</param>
    void Av(in AvPacket packet, Span<byte> datagram);
}

/// <summary>
/// PP680, under PP27: takion_handle_packet_av, assembled.
///
/// The AV branch had a managed classifier and a managed flush and nothing between them.
/// <see cref="TakionReceivePath"/> decides that a datagram is video and hands the bytes to a sink;
/// <see cref="AvReorderTimeout"/> knows what a queue full of packets should do next. What did not
/// exist was the body of the function: the two disable gates, the parse, the audio shortcut, the
/// queue created on its first packet, the entry, the stage stamp, and the push-then-flush.
///
/// THE ORDER OF THE GATES IS THE BEHAVIOUR, and it is not the order a rewrite would choose:
///
///   VIDEO IS DROPPED BEFORE THE PARSE. A session with video off does not pay for a header it will
///   not use - and, less obviously, does not advance the key-position ledger for it either, because
///   the parse is what requests a position.
///
///   AUDIO IS DROPPED AFTER IT, and only when the packet is not haptics. Haptics rides the audio
///   base type, so a gate that ran first would silence the pad along with the speakers, and one
///   that forgot the exception would do it while the sound was already off.
///
/// THE QUEUE OPENS AT packet_index MINUS unit_index, not at packet_index. A stream is joined
/// mid-frame far more often than at a frame boundary, so seeding at the packet that arrived would
/// make the units before it in that frame look older than the window and drop them - a first frame
/// missing its head on every connect. <see cref="ReorderDropStrategy.Begin"/> and the drop callback
/// go on in the same breath, because a queue with the default strategy drops from the far END and
/// would discard the newest packet under overflow instead of the stalest.
///
/// A QUEUE THAT WILL NOT OPEN DISPATCHES UNREORDERED. The C's init can fail on an allocation and
/// the managed one cannot, so the failure is reachable here only through
/// <see cref="ManagedAvArm(IAvArmSink, Func{int, ushort, ReorderQueue}?, Func{long}?, BaselineStat?, BaselineStat?)"/>'s
/// factory - which is the same seam PP367 left for a decrypt that cannot fail managed for the C's
/// reason. The fallback matters because it is not a drop: the packet still goes out, just out of
/// order, and the queue is tried again on the next one.
///
/// THE ENTRY IS AN ALLOCATION AND SO IS ITS BUFFER, which is not a slip against PP44's budget. The
/// C mallocs both here too, and the budget is on <see cref="TakionReceivePath"/> - the branch that
/// decides. This is the branch that KEEPS, and keeping a datagram past the call is the one copy
/// PP490 named.
/// </summary>
public sealed class ManagedAvArm : IDisposable
{
    private readonly IAvArmSink sink;
    private readonly Func<int, ushort, ReorderQueue?> videoQueueFactory;
    private readonly Func<long> clock;
    private readonly BaselineStat? stageReceive;
    private readonly BaselineStat? stageReorder;

    /// <summary>Built once: a lambda made per datagram would be an allocation per datagram.</summary>
    private readonly Action<ulong, long> onPull;

    private readonly Dictionary<long, AvQueueEntry> entries = [];
    private readonly List<ulong> dispatched = [];

    private ReorderQueue? videoQueue;
    private AvHeadWait wait;
    private long nextTicket;
    private int dropsSeen;
    private bool disposed;

    /// <param name="sink">Where a dispatched packet goes - takion's callback, as a seam.</param>
    /// <param name="videoQueueFactory">
    /// How the video queue is made, so a test can refuse. Returning null is the C's failed
    /// <c>chiaki_reorder_queue_init_16</c>, which no managed allocation reproduces.
    /// </param>
    /// <param name="clock">The monotonic microsecond clock, read where the C reads it.</param>
    /// <param name="stageReceive">takion->stage_receive, or null where nothing is measuring.</param>
    /// <param name="stageReorder">takion->stage_reorder, likewise.</param>
    /// <param name="ledger">
    /// PP703: the takion's own key-position ledger, where one owns this arm.
    ///
    /// One per takion and not one per arm: the C's is a field of the takion and every parse in the
    /// session advances the same counter. An arm with a ledger of its own is right for a test and
    /// wrong for a session, which is why the caller decides.
    /// </param>
    public ManagedAvArm(
        IAvArmSink sink,
        Func<int, ushort, ReorderQueue?>? videoQueueFactory = null,
        Func<long>? clock = null,
        BaselineStat? stageReceive = null,
        BaselineStat? stageReorder = null,
        ManagedKeyState? ledger = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        this.sink = sink;
        Ledger = ledger ?? new ManagedKeyState();
        this.videoQueueFactory = videoQueueFactory ?? DefaultVideoQueue;
        this.clock = clock ?? Monotonic;
        this.stageReceive = stageReceive;
        this.stageReorder = stageReorder;
        onPull = Deliver;
    }

    /// <summary>takion->disable_audio_video, which both gates read.</summary>
    public AudioVideoDisabled Disabled { get; set; }

    /// <summary>Which layout the parse takes - the version chosen at connect, as a flag.</summary>
    public bool V12 { get; set; }

    /// <summary>The key-position ledger the parse advances, which is per takion.</summary>
    public ManagedKeyState Ledger { get; }

    /// <summary>The video queue, or null until the first video packet opened it.</summary>
    public ReorderQueue? VideoQueue => videoQueue;

    /// <summary>The head-wait carried between flushes, which is what the poll timeout is read from.</summary>
    public AvHeadWait Wait => wait;

    /// <summary>Every sequence number dispatched, in the order it went out.</summary>
    public IReadOnlyList<ulong> Dispatched => dispatched;

    /// <summary>How many entries are still in the queue, held against the queue's own count.</summary>
    public int Held => entries.Count;

    /// <summary>How many entries the queue dropped, which the C's drop callback frees.</summary>
    public int Dropped { get; private set; }

    /// <summary>
    /// One AV datagram, which is the whole of takion_handle_packet_av.
    /// </summary>
    /// <param name="baseType">
    /// <see cref="TakionDispatch.Video"/> or <see cref="TakionDispatch.Audio"/>. The C asserts this
    /// and the dispatch above is what guarantees it, so anything else is a caller's bug rather than
    /// a packet's.
    /// </param>
    /// <param name="datagram">
    /// The bytes. Taken as a Span because the route on the far end decrypts in place, and because
    /// the C takes ownership of the buffer here.
    /// </param>
    public AvArmOutcome Handle(int baseType, Span<byte> datagram)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (baseType != TakionDispatch.Video && baseType != TakionDispatch.Audio)
            throw new ArgumentOutOfRangeException(nameof(baseType), baseType, "not an AV base type.");

        // Before the gate and before the parse, because the parse is the work the receive stage is
        // meant to be charged for.
        long arrivalUs = clock();

        bool isVideo = baseType == TakionDispatch.Video;

        if (isVideo && Disabled.HasFlag(AudioVideoDisabled.Video))
            return AvArmOutcome.VideoDisabled;

        AvPacket? parsed = AvPacketParse.Parse(V12, Ledger, datagram, out _);
        if (parsed is null)
            return AvArmOutcome.ParseFailed;

        AvPacket packet = parsed.Value;

        // Haptics rides the audio base type, so the exception is what keeps the pad working when
        // the speakers are off.
        if (!isVideo && Disabled.HasFlag(AudioVideoDisabled.Audio) && !packet.IsHaptics)
            return AvArmOutcome.AudioDisabled;

        if (!isVideo)
        {
            sink.Av(packet, datagram);
            return AvArmOutcome.Audio;
        }

        if (videoQueue is null)
        {
            ushort begin = packet.PacketIndex;
            if (packet.UnitIndex > 0)
                begin = (ushort)(packet.PacketIndex - packet.UnitIndex);

            ReorderQueue? opened = videoQueueFactory(AvReorderTimeout.VideoQueueSizeExp, begin);
            if (opened is null)
            {
                // Not a drop: the packet goes out unreordered and the queue is tried again on the
                // next one, which is the C's fallback exactly.
                sink.Av(packet, datagram);
                return AvArmOutcome.Unreordered;
            }

            videoQueue = opened;
            videoQueue.DropStrategy = ReorderDropStrategy.Begin;
            wait = new AvHeadWait(0, begin);
        }

        long queuedUs = clock();
        if (queuedUs >= arrivalUs)
            stageReceive?.Push((ulong)(queuedUs - arrivalUs));

        long ticket = nextTicket++;
        entries[ticket] = new AvQueueEntry(baseType, datagram.ToArray(), packet, queuedUs);

        videoQueue.Push(packet.PacketIndex, ticket);

        // The push itself can drop - an overflow walks the window's start forward, entry by entry -
        // so what the C's callback would have freed is released before the flush runs.
        ReleaseDropped();

        wait = AvReorderTimeout.Flush(videoQueue, clock(), wait, onPull).Wait;
        ReleaseDropped();

        return AvArmOutcome.Queued;
    }

    /// <summary>
    /// takion_av_queues_flush_with_timeout: the flush with no packet behind it.
    ///
    /// The receive thread calls this when its poll expired, which is how a queue stalled on a lost
    /// head ever gets past it. A no-op before the first video packet, because the C guards it on
    /// <c>video_queue_initialized</c>.
    /// </summary>
    public void FlushWithTimeout()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (videoQueue is null)
            return;

        wait = AvReorderTimeout.Flush(videoQueue, clock(), wait, onPull).Wait;
        ReleaseDropped();
    }

    /// <summary>How long the next receive may wait, given the head this queue is waiting for.</summary>
    public ulong NextTimeoutMs() => videoQueue is null
        ? ulong.MaxValue
        : AvReorderTimeout.NextTimeoutMs(wait, clock());

    /// <summary>
    /// Releases the queue, which reports everything still in it as dropped.
    ///
    /// chiaki_reorder_queue_fini fires the drop callback for every remaining element, and the port's
    /// queue does the same - so the entries those callbacks name are released here rather than left
    /// to the collector, which is the ownership the C spells out.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        videoQueue?.Dispose();
        ReleaseDropped();
        entries.Clear();
        disposed = true;
    }

    /// <summary>One entry coming out of the queue, in order, with its dwell charged.</summary>
    private void Deliver(ulong seqNum, long ticket)
    {
        if (!entries.Remove(ticket, out AvQueueEntry entry))
            return;

        if (entry.QueuedUs != 0)
        {
            // The clock is read AGAIN here rather than reused from the top of the flush: a burst
            // pulled after a timeout would otherwise all report the same dwell. The C says so in as
            // many words.
            long pulledUs = clock();
            if (pulledUs >= entry.QueuedUs)
                stageReorder?.Push((ulong)(pulledUs - entry.QueuedUs));
        }

        dispatched.Add(seqNum);

        AvPacket packet = entry.Packet;
        sink.Av(packet, entry.Buffer);
    }

    /// <summary>
    /// Frees what the queue has dropped since the last look, which is takion_av_drop's whole body.
    ///
    /// A watermark over the queue's own list rather than a callback, because the port's queue
    /// records drops instead of calling out. The entries have to go somewhere either way: a video
    /// packet dropped for an overflow is a buffer the C frees, and one kept here would be a leak
    /// that only shows up on a lossy connection.
    /// </summary>
    private void ReleaseDropped()
    {
        if (videoQueue is null)
            return;

        IReadOnlyList<ReorderDrop> drops = videoQueue.Drops;

        for (int i = dropsSeen; i < drops.Count; i++)
        {
            if (entries.Remove(drops[i].Payload))
                Dropped++;
        }

        dropsSeen = drops.Count;
    }

    private static ReorderQueue DefaultVideoQueue(int sizeExp, ushort begin) => new(sizeExp, begin);

    /// <summary>
    /// chiaki_time_now_monotonic_us. Stopwatch and not DateTime, because only differences are read
    /// and a wall clock can step backwards mid-session.
    /// </summary>
    private static long Monotonic()
        => System.Diagnostics.Stopwatch.GetTimestamp() * 1_000_000L
            / System.Diagnostics.Stopwatch.Frequency;
}

/// <summary>
/// PP680: takion_handle_packet_av as the C writes it, so the composition above cannot drift off it.
///
/// Six claims, and every one of them is an ORDER or a guard rather than a presence. That is what a
/// port of a function body gets wrong: the calls are all there in any arrangement of them, and it
/// is the arrangement that decides whether a disabled stream still keys its cipher and whether the
/// pad goes quiet with the speakers.
///
/// Each reads the compacted body, so a comment naming a call is not the call.
/// </summary>
public static class ManagedAvArmSource
{
    /// <summary>takion.c, where the function is.</summary>
    public const string RelativePath = @"lib\src\takion.c";

    /// <summary>It, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The function's body, or null where the file declares no definition for it.</summary>
    public static string? Body(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CFunction.Body(source, "void takion_handle_packet_av");
    }

    /// <summary>
    /// Whether the video gate is still BEFORE the parse and the audio gate still after it.
    ///
    /// One check and not two, because the claim is the sandwich: a file with both gates above the
    /// parse and a file with both below it each satisfy half of this, and the second one keys the
    /// cipher for packets a disabled stream throws away.
    /// </summary>
    public static bool TheVideoGateIsBeforeTheParseAndTheAudioGateAfter(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return CCall.InOrder(
            CCall.Compact(CCall.Code(body)),
            "CHIAKI_VIDEO_DISABLED",
            "takion->av_packet_parse(&packet, &takion->key_state, buf, buf_size)",
            "CHIAKI_AUDIO_DISABLED");
    }

    /// <summary>Whether the audio gate still spares a haptics packet.</summary>
    public static bool TheAudioGateSparesHaptics(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        // Mark and not Contains: the compaction welds `&& !` into `&&!`, so a needle written the
        // way the C writes it has to be put through the same reader as the haystack.
        return CCall.Mark(CCall.Code(body), "&& !packet.is_haptics") >= 0;
    }

    /// <summary>
    /// Whether the queue is still seeded at packet_index minus unit_index, under its guard.
    ///
    /// Both halves. The subtraction alone would be satisfied by a file that always did it, and
    /// unit_index is unsigned - so the guard is not defensive, it is what keeps a first packet at
    /// unit zero from being rewritten into the same number by a different route.
    /// </summary>
    public static bool TheQueueOpensAtPacketIndexMinusUnitIndex(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string compact = CCall.Compact(CCall.Code(body));

        return CCall.InOrder(
            compact,
            "ChiakiSeqNum16 queue_begin = packet.packet_index;",
            "if(packet.unit_index > 0)",
            "queue_begin = (ChiakiSeqNum16)(packet.packet_index - packet.unit_index);");
    }

    /// <summary>
    /// Whether the strategy and the drop callback are still set in the same breath as the init.
    ///
    /// The default strategy drops from the far END, so a queue built without this line discards the
    /// packet that just arrived instead of the stalest one - which looks like loss on the newest
    /// frame rather than on the oldest.
    /// </summary>
    public static bool TheDropStrategyAndCallbackAreSetOnInit(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return CCall.InOrder(
            CCall.Compact(CCall.Code(body)),
            "chiaki_reorder_queue_set_drop_strategy(queue, CHIAKI_REORDER_QUEUE_DROP_STRATEGY_BEGIN);",
            "chiaki_reorder_queue_set_drop_cb(queue, takion_av_drop, takion);");
    }

    /// <summary>
    /// Whether a failed init still DISPATCHES rather than dropping.
    ///
    /// The distinction the fallback exists for: a packet that could not be queued still reaches the
    /// receiver, out of order. A port reading this arm as an error path would lose one packet per
    /// failure and report nothing.
    /// </summary>
    public static bool AFailedInitDispatchesUnreordered(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string compact = CCall.Compact(CCall.Code(body));

        int init = CCall.Mark(
            compact,
            "if(chiaki_reorder_queue_init_16(queue, size_exp, queue_begin) != CHIAKI_ERR_SUCCESS)");
        if (init < 0)
            return false;

        int entry = CCall.Mark(compact, "TakionAVPacketEntry *entry = malloc", init);
        if (entry < 0)
            return false;

        return CCall.Mark(compact[init..entry], "takion->cb(&event, takion->cb_user);") >= 0;
    }

    /// <summary>
    /// Whether the receive stage is still charged between the entry and the push.
    ///
    /// Not decoration: the sample is the parse's cost, so a charge taken after the push would be
    /// measuring the queue as well and a charge taken before the parse would measure nothing.
    /// </summary>
    public static bool TheReceiveStageIsChargedBeforeThePush(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return CCall.InOrder(
            CCall.Compact(CCall.Code(body)),
            "chiaki_session_baseline_stat_push(&takion->stage_receive, queued_us - arrival_us);",
            "chiaki_reorder_queue_push(queue, packet.packet_index, entry);",
            "takion_av_queue_flush_with_timeout(takion, queue, head_wait, head_wait_seq_num);");
    }
}

/// <summary>
/// The arm's far end when it is not a test: PP667's route, driven per dispatched packet.
///
/// The seam exists because takion's callback is a function pointer and the port's answer to it is
/// <see cref="StreamAvDispatch.Dispatch"/> - which needs the session's key, its IV and both
/// receivers. Holding those is this adapter's whole job, so the arm itself owns none of them.
/// </summary>
public sealed class StreamAvArmSink(
    byte[] keyBase, byte[] iv, ManagedVideoReceiver video, IAudioSink audio) : IAvArmSink
{
    /// <summary>Where each packet went, oldest first.</summary>
    public List<AvRoute> Routes { get; } = [];

    /// <inheritdoc/>
    public void Av(in AvPacket packet, Span<byte> datagram)
        => Routes.Add(StreamAvDispatch.Dispatch(packet, datagram, keyBase, iv, video, audio));
}
