using System.Net;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>Where a takion is in its life, which is what says whether its state exists.</summary>
public enum TakionStage
{
    /// <summary>Built and owning nothing.</summary>
    Idle,

    /// <summary>The socket is open and the handshake has not finished.</summary>
    Connecting,

    /// <summary>The queues and the send buffer exist and the loop may run.</summary>
    Connected,

    /// <summary>Torn down, in the order below.</summary>
    Closed,
}

/// <summary>One thing a teardown releases, in the order takion_thread_func releases it.</summary>
public enum TakionTeardownStep
{
    /// <summary>chiaki_takion_send_buffer_fini.</summary>
    SendBuffer,

    /// <summary>The video queue, and only where it was initialised.</summary>
    VideoQueue,

    /// <summary>chiaki_reorder_queue_fini on the data queue, at the error label.</summary>
    DataQueue,

    /// <summary>PP474's release of anything still postponed, at `beach`.</summary>
    Postponed,

    /// <summary>The disconnected event.</summary>
    Disconnected,

    /// <summary>The socket, and only where this takion made it.</summary>
    Socket,
}

/// <summary>
/// PP678: a takion that OWNS things - the tag, the ledger, the queues, the send buffer, the socket.
///
/// PP487 modelled the loop and PP672 the client handshake, and every piece around them - the
/// postpone array, the send buffer, the reorder queues, the key-position ledger - was runnable on
/// its own and owned by nothing. So there was no takion a session could hold, and
/// <see cref="ITakionLoopHost"/> had implementations only in the test project.
///
/// This is the composition. The bookends are takion_thread_func's, in its order:
///
///   BEFORE the loop - the per-run stats zeroed, the handshake, the data queue seeded from the
///   REMOTE TAG rather than from the ack's wire field, the drop callback, the send buffer, then the
///   connected event. The queue seed is the subtlety: PP672's RemoteInitialSeqNum reads the tag, and
///   a port seeding from the field beside it starts the queue at a number the console never sends.
///
///   AFTER it - send buffer, video queue where it was initialised, data queue, anything still
///   postponed, the disconnected event, and the socket where this takion made it. PP474 put the
///   postpone release at `beach` because the flush above it is guarded on the cipher: a session that
///   dies before the cipher is agreed left the array and every datagram in it behind, and that is
///   the ORDINARY failure rather than an exotic one.
///
/// THE TEARDOWN IS RECORDED, not just performed. <see cref="Teardown"/> is what
/// "tears down in order" is asserted against, because an order is not visible in a Dispose that
/// works - and the C's is not the order the fields were created in.
///
/// NOTHING IS ALLOCATED PER DATAGRAM once the loop is warm. The receive goes into
/// <see cref="TakionReceiveBuffer"/>'s pooled span and the dispatch is handed the span itself, so
/// a handler that copies is the handler's decision and not this one's.
/// </summary>
public sealed class ManagedTakion : ITakionLoopHost, IDisposable
{
    /// <summary>TAKION_REORDER_QUEUE_SIZE_EXP: sixteen entries.</summary>
    public const int ReorderQueueSizeExp = 4;

    /// <summary>
    /// TAKION_SEND_BUFFER_SIZE.
    /// </summary>
    /// <remarks>
    /// The C's comment is a constraint rather than a note: this must match the acked-seqnums array
    /// in takion_handle_packet_message_data_ack, and the two are separated by six hundred lines.
    /// </remarks>
    public const int SendBufferSize = 16;

    private readonly List<TakionTeardownStep> teardown = [];
    private readonly List<byte[]> postponed = [];
    private readonly Action<ReadOnlySpan<byte>>? dispatch;

    private readonly ManagedAvArm? avArm;

    private TakionUdpWire? wire;
    private ReorderQueue? dataQueue;
    private TakionSendBuffer? sendBuffer;

    /// <param name="tagLocal">
    /// The tag this takion draws for itself, which is also its initial local sequence number - the
    /// C sets both from one chiaki_random_32. Supplied rather than drawn so a byte comparison can
    /// name it.
    /// </param>
    /// <param name="dispatch">
    /// Where a received datagram goes. Handed the pooled span, so an implementation that keeps the
    /// bytes must copy them itself - which is where a per-datagram allocation would come from and
    /// why it is the caller's to make.
    /// </param>
    /// <param name="av">
    /// PP703: where the AV branch goes, which is what gives this takion a video queue at all.
    ///
    /// Optional, and its absence is the state PP678 shipped in: the arm exists, nothing owned one,
    /// so <see cref="VideoQueueInitialised"/> was false for every takion the port could build and
    /// the teardown step reserved for the queue was never appended. With a sink there is an arm, and
    /// the arm opens the queue on its first video packet.
    /// </param>
    public ManagedTakion(
        uint tagLocal, Action<ReadOnlySpan<byte>>? dispatch = null, IAvArmSink? av = null)
    {
        Handshake = new TakionHandshakeClient(tagLocal);
        SeqNumLocal = tagLocal;
        this.dispatch = dispatch;

        // The takion's own ledger, not one of the arm's: the C's key state is a field of the takion
        // and every parse in the session advances the same counter.
        if (av is not null)
            avArm = new ManagedAvArm(av, ledger: Ledger);
    }

    /// <summary>The handshake, which owns both tags.</summary>
    public TakionHandshakeClient Handshake { get; }

    /// <summary>The key-position ledger, which PP677 put in managed code.</summary>
    public ManagedKeyState Ledger { get; } = new();

    /// <summary>seq_num_local, which the C seeds from the local tag rather than from zero.</summary>
    public uint SeqNumLocal { get; private set; }

    /// <summary>Where this takion is.</summary>
    public TakionStage Stage { get; private set; } = TakionStage.Idle;

    /// <summary>What the teardown released, in the order it released it.</summary>
    public IReadOnlyList<TakionTeardownStep> Teardown => teardown;

    /// <summary>How many datagrams the loop dispatched.</summary>
    public int Dispatched { get; private set; }

    /// <summary>Whether the connected event was raised, which the C raises before the loop.</summary>
    public bool RaisedConnected { get; private set; }

    /// <summary>The remote cipher's presence, which the loop reads to decide when to re-check.</summary>
    public bool CryptAvailable { get; set; }

    /// <summary>Whether this takion made the socket, which decides whether the teardown closes it.</summary>
    public bool OwnsSocket { get; private set; }

    /// <summary>Whether the video queue was ever initialised, which the teardown reads.</summary>
    public bool VideoQueueInitialised => avArm?.VideoQueue is not null;

    /// <summary>The AV branch, or null where this takion was built without a sink for one.</summary>
    public ManagedAvArm? AvArm => avArm;

    /// <summary>How long a receive waits when no AV queue is asking for less.</summary>
    public const int IdleTimeoutMs = 1000;

    /// <summary>The data queue, or null before the handshake seeded it.</summary>
    public ReorderQueue? DataQueue => dataQueue;

    /// <summary>The send buffer, or null before the handshake.</summary>
    public TakionSendBuffer? SendBuffer => sendBuffer;

    /// <summary>How many datagrams are held back waiting for the cipher.</summary>
    public int PostponedCount => postponed.Count;

    bool ITakionLoopHost.HasPostponed => postponed.Count > 0;

    /// <summary>
    /// How long the next receive may wait.
    ///
    /// PP703: the AV arm is who drives it now, which is what the comment here used to be waiting
    /// for. It is re-read after every dispatch and every flush - the C asks the queues at the top of
    /// each pass - and falls back to <see cref="IdleTimeoutMs"/> whenever the queues are not waiting
    /// for anything, so a takion with no arm behaves exactly as it did.
    /// </summary>
    public ulong NextTimeoutMs { get; set; } = IdleTimeoutMs;

    /// <summary>How many times the loop re-checked the queued MACs.</summary>
    public int Rechecks { get; private set; }

    /// <summary>How many times it flushed on a timeout, either kind.</summary>
    public int Flushes { get; private set; }

    /// <summary>How many data messages this takion has put on its socket.</summary>
    public int DataSent { get; private set; }

    /// <summary>How many congestion packets this takion has put on its socket.</summary>
    public int CongestionSent { get; private set; }

    /// <summary>
    /// PP749: chiaki_takion_send_congestion - the key position, the fifteen bytes, the socket.
    ///
    /// A RAW SEND AND NOT A DATA MESSAGE. The congestion packet carries its own type byte and its
    /// own key position and goes out whole; it is not wrapped in the message header
    /// <see cref="SendData"/> writes, which is why it does not take a channel and is not held for
    /// resend. The C's own send would MAC it where a local cipher exists, and this takion has none
    /// wired yet, so what leaves here is plain.
    /// </summary>
    public ChiakiError SendCongestion(ushort received, ushort lost)
    {
        ObjectDisposedException.ThrowIf(Stage == TakionStage.Closed, this);

        if (wire is null)
            return ChiakiError.Uninitialized;

        // The C advances by the packet's own size, before anything is formatted.
        ulong keyPos = Ledger.RequestPos(0, commit: true);

        Span<byte> datagram = stackalloc byte[TakionCongestion.PacketSize];
        TakionCongestion.Write(datagram, received, lost, keyPos);

        ChiakiError sent = wire.Send(datagram);
        if (sent == ChiakiError.Success)
            CongestionSent++;

        return sent;
    }

    /// <summary>
    /// PP748: chiaki_takion_send_message_data, over the socket the handshake used.
    ///
    /// THE PIECES ALL EXISTED AND NOTHING JOINED THEM. PP675 wrote the bytes, PP678 gave this class
    /// the wire and the send buffer, and PP671 modelled the stages one send passes through. What
    /// was missing was the member that spends them in that order - so a message the port could
    /// build had no way out, and three of the run's output seams were waiting on it.
    ///
    /// THE ORDER IS <see cref="TakionDataSend"/>'S, not a convenient one. The key position is taken
    /// first because it is the one thing a refusal spends nothing of; the sequence number is taken
    /// after the packet exists; the push that holds the packet for resend happens last and its
    /// failure is IGNORED, which is the C reporting success over a packet nothing will resend.
    ///
    /// A SEND BEFORE THE HANDSHAKE IS A REFUSAL AND NOT A THROW. The C's caller is the stream
    /// connection, which cannot reach a send before its takion connected; a port that threw here
    /// would turn a sequencing mistake into a crash where the C returns an error.
    /// </summary>
    /// <param name="channel">Which channel the payload belongs to.</param>
    /// <param name="payload">The message's own bytes.</param>
    /// <param name="chunkFlags">The C's type_b.</param>
    public TakionSendOutcome SendData(
        ushort channel, ReadOnlySpan<byte> payload, byte chunkFlags = TakionDataPush.ExpectedTypeB)
    {
        ObjectDisposedException.ThrowIf(Stage == TakionStage.Closed, this);

        if (wire is null || sendBuffer is null)
        {
            return new TakionSendOutcome(
                TakionSendStage.KeyPositionRefused, ChiakiError.Uninitialized, false, false, 0, false);
        }

        // The ledger's position for this packet, which a refusal spends nothing of.
        ulong keyPos = Ledger.RequestPos(0, commit: true);

        var datagram = new byte[TakionDataDatagrams.DataSize(payload.Length)];
        uint seqNum = SeqNumLocal++;

        TakionDataDatagrams.WriteData(
            datagram, Handshake.TagRemote, (uint)keyPos, chunkFlags, seqNum, channel, payload);

        ChiakiError sent = wire.Send(datagram);
        if (sent != ChiakiError.Success)
        {
            return new TakionSendOutcome(
                TakionSendStage.SendFailed, ChiakiError.Network, true, true, seqNum, false);
        }

        DataSent++;

        // Last, and its failure is not the caller's: the C ignores what the push returns, so a
        // packet the buffer would not hold is reported as sent and will never be resent.
        ChiakiError held = sendBuffer.Push(seqNum, datagram.Length);

        return held == ChiakiError.Success
            ? new TakionSendOutcome(TakionSendStage.SentAndHeld, ChiakiError.Success, true, true, seqNum, false)
            : new TakionSendOutcome(TakionSendStage.SentButNotHeld, ChiakiError.Success, true, true, seqNum, false);
    }

    /// <summary>
    /// takion_handshake, over a socket this takion opens and owns.
    /// </summary>
    /// <param name="peer">Where the console is.</param>
    /// <param name="expectTimeoutMs">How long one attempt waits, the C's fifteen seconds by default.</param>
    /// <returns>The outcome the handshake reached; any error leaves nothing owned.</returns>
    public TakionHandshakeOutcome Connect(
        IPEndPoint peer, int expectTimeoutMs = TakionHandshake.ExpectTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ObjectDisposedException.ThrowIf(Stage == TakionStage.Closed, this);

        if (Stage != TakionStage.Idle)
            throw new InvalidOperationException("this takion has already connected");

        // Zeroed on the thread that will fill them, so a takion reused for a second session does
        // not open with the first one's tail. Kept because the C keeps it, even where a fresh
        // object makes it redundant - the C reuses the struct and this records why.
        // PP703: the video queue is not among them any more, and that is the C's shape rather than
        // an omission. takion_thread_func zeroes video_queue_initialized because the STRUCT is
        // reused; an arm is an object, and one that has opened a queue is not the one a second
        // connect gets - so a takion reused after a stream would need a new arm, not a cleared flag.
        Dispatched = 0;
        Rechecks = 0;
        Flushes = 0;

        Stage = TakionStage.Connecting;
        wire = TakionUdpWire.Connect(peer);
        OwnsSocket = true;

        TakionHandshakeOutcome outcome = Handshake.Run(wire, expectTimeoutMs);
        if (outcome.Error != ChiakiError.Success)
            return outcome;

        // THE REMOTE TAG, not the ack's wire field beside it. See PP672's RemoteInitialSeqNum.
        dataQueue = ReorderQueue.Wide(ReorderQueueSizeExp, Handshake.RemoteInitialSeqNum);
        sendBuffer = new TakionSendBuffer(SendBufferSize);

        Stage = TakionStage.Connected;
        RaisedConnected = true;

        return outcome;
    }

    /// <summary>
    /// The receive loop, over the socket the handshake used.
    /// </summary>
    /// <param name="enableCrypt">The C's enable_crypt, which only the MAC re-check reads.</param>
    /// <param name="iterationLimit">A bound, because a real loop ends on the stop pipe.</param>
    public TakionLoopOutcome RunLoop(bool enableCrypt = true, int iterationLimit = 16)
    {
        if (Stage != TakionStage.Connected)
            throw new InvalidOperationException("the loop runs on a connected takion");

        return TakionReceiveLoop.Run(this, enableCrypt, iterationLimit);
    }

    /// <summary>Hold a datagram back until the cipher exists, which is what postponing is.</summary>
    public void Postpone(ReadOnlySpan<byte> datagram) => postponed.Add(datagram.ToArray());

    void ITakionLoopHost.RecheckMacs() => Rechecks++;

    void ITakionLoopHost.FlushPostponed()
    {
        foreach (byte[] one in postponed)
            dispatch?.Invoke(one);

        // The flush empties the array, which is why the release at teardown is a no-op on this path.
        postponed.Clear();
    }

    void ITakionLoopHost.FlushWithTimeout()
    {
        Flushes++;

        // takion_av_queues_flush_with_timeout, which is a no-op before the first video packet
        // because the C guards it on video_queue_initialized.
        avArm?.FlushWithTimeout();
        RefreshTimeout();
    }

    TakionReceiveResult ITakionLoopHost.Receive(Span<byte> into, ulong timeoutMs)
    {
        if (wire is null)
            return new TakionReceiveResult(TakionReceiveOutcome.Failed, 0);

        ChiakiError err = wire.Receive(into, (int)timeoutMs, out int length);

        return err switch
        {
            ChiakiError.Success => new TakionReceiveResult(TakionReceiveOutcome.Datagram, length),
            ChiakiError.Timeout => new TakionReceiveResult(TakionReceiveOutcome.Timeout, 0),
            _ => new TakionReceiveResult(TakionReceiveOutcome.Failed, 0),
        };
    }

    void ITakionLoopHost.Dispatch(Span<byte> datagram)
    {
        Dispatched++;
        dispatch?.Invoke(datagram);

        // PP703: and the AV branch, which is what an arm is FOR. The dispatch above stays: it is
        // the hook a caller passes to watch everything, and this is the one branch that acts.
        if (avArm is { } arm && !datagram.IsEmpty)
        {
            int baseType = TakionDispatch.BaseTypeOf(datagram);

            if (baseType is TakionDispatch.Video or TakionDispatch.Audio)
                arm.Handle(baseType, datagram);
        }

        RefreshTimeout();
    }

    /// <summary>
    /// Ask the AV queues how long the next receive may wait, as the C's loop does each pass.
    ///
    /// UINT64_MAX from the queues means nothing is waiting, and the C's thread then blocks on the
    /// socket alone. This port's loop is bounded rather than blocking, so that case falls back to
    /// <see cref="IdleTimeoutMs"/> - the value every takion here has always used.
    /// </summary>
    private void RefreshTimeout()
    {
        if (avArm is not { } arm)
            return;

        ulong next = arm.NextTimeoutMs();
        NextTimeoutMs = next == ulong.MaxValue ? IdleTimeoutMs : next;
    }

    /// <summary>
    /// takion_thread_func's teardown, in its order, recorded as it goes.
    /// </summary>
    /// <remarks>
    /// The order is NOT the order the fields were made in, and that is the point of recording it.
    /// The send buffer goes first though it was made last, the postpone array is released at the
    /// label every exit passes through, and the socket is closed after the event rather than before
    /// it - so a listener told the session ended can still read what it needs.
    /// </remarks>
    public void Dispose()
    {
        if (Stage == TakionStage.Closed)
            return;

        if (sendBuffer is not null)
        {
            sendBuffer = null;
            teardown.Add(TakionTeardownStep.SendBuffer);
        }

        // PP703: and only where a video packet opened one, which is the C's own guard - the step is
        // conditional because a session that never received video has no queue to release.
        if (avArm is not null && VideoQueueInitialised)
        {
            avArm.Dispose();
            teardown.Add(TakionTeardownStep.VideoQueue);
        }

        if (dataQueue is not null)
        {
            dataQueue.Dispose();
            dataQueue = null;
            teardown.Add(TakionTeardownStep.DataQueue);
        }

        // PP474: at `beach`, so every exit passes it - including the handshake's, which skips all
        // three above. A no-op where the flush already emptied the array.
        if (postponed.Count > 0)
        {
            postponed.Clear();
            teardown.Add(TakionTeardownStep.Postponed);
        }

        teardown.Add(TakionTeardownStep.Disconnected);

        if (OwnsSocket && wire is not null)
        {
            wire.Dispose();
            wire = null;
            teardown.Add(TakionTeardownStep.Socket);
        }

        Stage = TakionStage.Closed;
    }
}
