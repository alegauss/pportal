namespace ChiakiNg.Protocol;

/// <summary>What one arrival came to, which is the three dispatch layers' answers together.</summary>
/// <param name="Route">Layer one: what the callback did with the takion event.</param>
/// <param name="Data">Layer two: which of the four data kinds, where the event was one.</param>
/// <param name="Handler">Layer three: which handler a protobuf reached, decided by the state.</param>
/// <param name="Bang">What the bang handler decided, where it was the one that ran.</param>
/// <param name="StreamInfo">And the streaminfo handler's verdict, likewise.</param>
/// <param name="Idle">And the idle arm's action.</param>
/// <param name="Raised">
/// Which flag this arrival raised on the host, or null where it raised none - which is most of them.
/// A reading whose handler decided something and whose Raised is null is the C's own shape: four of
/// the bang's six outcomes touch neither flag.
/// </param>
public readonly record struct ArrivalReading(
    TakionRoute Route,
    TakionData Data = TakionData.Other,
    ProtobufHandler? Handler = null,
    BangOutcome? Bang = null,
    StreamInfoVerdict? StreamInfo = null,
    IdleAction? Idle = null,
    StreamFlagRaised? Raised = null,
    TakionMessageVerdict? Verdict = null,
    int BaseType = -1);

/// <summary>Which of the host's flags an arrival raised.</summary>
public enum StreamFlagRaised
{
    /// <summary>state_finished, which is the only one a wait ends on.</summary>
    Finished,

    /// <summary>state_failed, which PP365 established nothing reads.</summary>
    Failed,

    /// <summary>remote_disconnected, which the disconnect handler sets with its reason.</summary>
    RemoteDisconnected,
}

/// <summary>
/// PP773: the wire between what arrives and the flags the run waits on.
///
/// PP721 ported the data layer's decisions, PP729 the bang's, PP686 the streaminfo's and PP684 the
/// idle arm's; PP366 modelled all three layers of the dispatch that chooses between them and PP745
/// built the host that holds the flags. Every piece existed and none of them was joined, so a live
/// run reached the takion connect state and every wait after it ran its whole timeout - the
/// arrivals reached the dispatch and the dispatch told nobody.
///
/// THIS IS stream_connection_takion_cb, AND IT IS THE ONE OBJECT THAT SPANS THE THREE LAYERS. The
/// C's callback switches on the event kind, hands DATA to a second switch on the data type, and
/// hands a protobuf to a third on the STATE - and the third is why this cannot be a function. Which
/// handler a message reaches depends on where the run's walk had got to, and the walk is on another
/// thread.
///
/// THE STATE IS READ FROM THE HOST, not held here. The C keeps one field beside the flags and
/// writes it at every state entry, under the mutex the waits take; two copies would be one commit
/// away from a message routed by a state the run had already left. <see cref="ManagedStreamRunHost.State"/>
/// is that field and <see cref="ManagedStreamRunHost.BeginState"/> is where it moves.
///
/// A BANG IS STILL REFUSED WITHOUT KEYING, which is where this stops. <see cref="IBangKeying"/> is a
/// seam on purpose - the derivation is OpenSSL's - and a caller that supplies none gets a bang read,
/// routed, and failed at the derive. So a console's bang now REACHES the handler, and the flag it
/// then raises is state_failed until something fills that seam.
/// </summary>
public sealed class StreamArrivals
{
    private readonly ManagedStreamRunHost host;
    private readonly IStreamMessageSink messages;
    private readonly IBangKeying? keying;
    private readonly ManagedStreamData? data;

    /// <summary>Takes the host whose flags it raises, and the sink the streaminfo's three sends use.</summary>
    /// <param name="host">Where the flags and the state live.</param>
    /// <param name="messages">Where the streaminfo's ack, controller connection and microphone go.</param>
    /// <param name="keying">
    /// The derivation a bang leads to. Absent refuses every bang at the derive, which is the C's own
    /// path when chiaki_ecdh_derive_secret says no.
    /// </param>
    /// <param name="data">
    /// The second layer's other three kinds - rumble, pad info, trigger effects. Absent reports the
    /// kind and does nothing with it, which none of the run's waits depends on.
    /// </param>
    public StreamArrivals(
        ManagedStreamRunHost host,
        IStreamMessageSink messages,
        IBangKeying? keying = null,
        ManagedStreamData? data = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(messages);

        this.host = host;
        this.messages = messages;
        this.keying = keying;
        this.data = data;
    }

    /// <summary>
    /// PP773: the tag an inbound header must carry, and the ledger a key position is committed to.
    ///
    /// The two things <see cref="Datagram"/> needs off the takion, taken as values rather than as
    /// the takion itself - which the composition root cannot hand over, because the takion is built
    /// with the callback that reaches this object. Set after construction for that reason; a
    /// datagram arriving before it is set is refused by the tag gate, as one carrying somebody
    /// else's tag is.
    /// </summary>
    public uint TagLocal { get; set; }

    /// <summary>The ledger, which is the takion's own and not a second one.</summary>
    public IKeyPositionLedger? Ledger { get; set; }

    /// <summary>How many arrivals reached a handler, which is what a live run has to show.</summary>
    public int Handled { get; private set; }

    /// <summary>What each arrival came to, newest last, for a caller reading a run afterwards.</summary>
    public IReadOnlyList<ArrivalReading> Readings => readings;

    private readonly List<ArrivalReading> readings = [];

    /// <summary>
    /// The takion's CONNECTED event, which is heard only in the state that waits for one.
    /// </summary>
    public ArrivalReading Connected() => Event(TakionEvent.Connected);

    /// <summary>And its DISCONNECT, which after the first state is dropped on the floor.</summary>
    public ArrivalReading Disconnected() => Event(TakionEvent.Disconnect);

    /// <summary>
    /// One control datagram, straight off the takion's receive loop.
    ///
    /// This is the whole path in one call: the message header, the nine-byte data header under it,
    /// and the type byte that says which of the four kinds arrived. A datagram that is not a DATA
    /// message - an ack, an unknown chunk type, a header carrying somebody else's tag - is ignored
    /// here exactly as the C's switch ignores it.
    /// </summary>
    /// <param name="datagram">The whole datagram, type byte included.</param>
    public ArrivalReading Datagram(ReadOnlySpan<byte> datagram)
    {
        if (Ledger is not { } ledger)
            return Record(new ArrivalReading(TakionRoute.Ignored));

        // PP773: the base type is layer zero and it is not this object's switch - the takion's own
        // dispatch routes video and audio to the AV arm. What reaches here is EVERY datagram, so a
        // reading has to be able to say "that was a video packet" rather than only "ignored".
        int baseType = TakionDispatch.BaseTypeOf(datagram);

        if (baseType != TakionDispatch.Control)
            return Record(new ArrivalReading(TakionRoute.Ignored, BaseType: baseType));

        TakionMessageReading message = TakionMessageIntake.Read(datagram, TagLocal, ledger);

        if (message.Verdict != TakionMessageVerdict.Data)
            return Record(new ArrivalReading(TakionRoute.Ignored, Verdict: message.Verdict, BaseType: baseType));

        TakionDataPushReading push = TakionDataPush.Read(
            datagram, message.PayloadOffset, message.PayloadSize, message.Header.ChunkFlags);

        if (push.Verdict != TakionDataPushVerdict.Pushed)
        {
            return Record(new ArrivalReading(
                TakionRoute.ToData, Verdict: message.Verdict, BaseType: baseType));
        }

        // One entry through the drain, which is what decides whether the type is one of the four and
        // where the body starts. The queue between the push and the drain is takion's own ordering
        // and not this layer's business: what the C hands the callback is the payload past its
        // nine-byte header, and this is that.
        TakionDrainOutcomeSet drained = TakionDataDrain.Drain([push.Entry]);

        if (drained.Deliveries.Count == 0)
        {
            return Record(new ArrivalReading(
                TakionRoute.ToData, Verdict: message.Verdict, BaseType: baseType));
        }

        TakionDelivery delivery = drained.Deliveries[0];

        return Data(delivery.DataType, delivery.Body);
    }

    /// <summary>
    /// One data message, past its header - which is what the C's second layer receives.
    /// </summary>
    /// <param name="type">The type byte, already known to be one of the four.</param>
    /// <param name="body">The payload past its nine-byte header.</param>
    public ArrivalReading Data(TakionDataType type, ReadOnlySpan<byte> body)
    {
        TakionData kind = ManagedStreamData.KindOf(type);

        if (kind != TakionData.Protobuf)
        {
            // The other three are events and never flags: nothing in the run's three waits is
            // decided by a rumble. Handed on where a caller supplied somewhere for them to go.
            data?.Deliver(type, body);

            return Record(new ArrivalReading(TakionRoute.ToData, kind));
        }

        return Record(Protobuf(body, StreamDispatch.HandlerFor(host.State)));
    }

    /// <summary>
    /// The buffered streaminfo, replayed into the state that wants it.
    ///
    /// Installed on the host rather than called by it, because the host holds the buffer and this
    /// holds the handler - and the C's replay is the same handler over the same bytes, one state
    /// later. Written as a method group so the composition root's line says what it joins.
    /// </summary>
    public void Replay(byte[] held)
    {
        ArgumentNullException.ThrowIfNull(held);
        Record(Protobuf(held, ProtobufHandler.ExpectStreaminfo));
    }

    private ArrivalReading Event(TakionEvent kind)
    {
        TakionRoute route = StreamDispatch.Route(kind, host.State);

        switch (route)
        {
            case TakionRoute.FinishConnect:
                host.Signal(finished: true);
                return Record(new ArrivalReading(route, Raised: StreamFlagRaised.Finished));

            case TakionRoute.FailConnect:
                host.Signal(failed: true);
                return Record(new ArrivalReading(route, Raised: StreamFlagRaised.Failed));

            default:
                return Record(new ArrivalReading(route));
        }
    }

    private ArrivalReading Protobuf(ReadOnlySpan<byte> payload, ProtobufHandler handler) => handler switch
    {
        ProtobufHandler.ExpectBang => Bang(payload),
        ProtobufHandler.ExpectStreaminfo => StreamInfo(payload),
        _ => Idle(payload),
    };

    private ArrivalReading Bang(ReadOnlySpan<byte> payload)
    {
        BangReading read = BangHandler.Read(payload, host.HasEarlyStreaminfo, keying ?? Refuses.Instance);

        switch (read.Outcome)
        {
            case BangOutcome.Keyed:
                host.Signal(finished: true);
                return Bang(read, StreamFlagRaised.Finished);

            case BangOutcome.Refused:
                host.Signal(failed: true);
                return Bang(read, StreamFlagRaised.Failed);

            case BangOutcome.SavedEarly:
                // Kept, not handled: the state that wants it has not been entered, and the run
                // replays this exact message when it is.
                host.BufferEarlyStreaminfo(payload);
                return Bang(read, null);

            case BangOutcome.ToDisconnect:
                return Bang(read, Disconnect(payload));

            default:
                return Bang(read, null);
        }
    }

    private ArrivalReading StreamInfo(ReadOnlySpan<byte> payload)
    {
        StreamInfoReading read = StreamInfoMessage.Read(payload);

        if (read.Verdict == StreamInfoVerdict.Disconnect)
            return Info(read, Disconnect(payload));

        if (read.Verdict != StreamInfoVerdict.Accepted)
        {
            // Undecodable, not-a-streaminfo and a wrong audio header differ in what the C LOGS and
            // not in what it leaves behind: the first two return with both flags untouched, and only
            // the audio header reaches the error label. PP372's ownership is the garbage collector's
            // here, which is why the three read alike from this side.
            if (read.Verdict == StreamInfoVerdict.AudioHeaderWrongSize)
            {
                host.Signal(failed: true);
                return Info(read, StreamFlagRaised.Failed);
            }

            return Info(read, null);
        }

        return Info(read, Accept(read));
    }

    /// <summary>
    /// The accepted path: the two receivers are told what the stream is, then the three sends.
    ///
    /// Every one of them is checked, which is PP370's repair - the streaminfo ack was the one send
    /// whose answer the C discarded, and reporting CONNECTED over a console still waiting to be told
    /// is a session that dies later for a reason nothing logged.
    /// </summary>
    private StreamFlagRaised Accept(StreamInfoReading read)
    {
        if (read.AudioHeader is { } header)
            host.Audio?.AudioArm.StreamInfo(ManagedAudioHeader.Load(header));

        host.Video?.StreamInfo([.. read.Profiles.Select(one => one.Header)]);

        StreamMessage ack = StreamMessages.StreamInfoAck();
        StreamMessage pad = StreamMessages.ControllerConnection(dualSense: false);
        StreamMessage mic = StreamMessages.MicrophoneStreamInfo();

        if (!messages.Send(in ack) || !messages.Send(in pad) || !messages.Send(in mic))
        {
            host.Signal(failed: true);
            return StreamFlagRaised.Failed;
        }

        host.Signal(finished: true);
        return StreamFlagRaised.Finished;
    }

    private ArrivalReading Idle(ReadOnlySpan<byte> payload)
    {
        ushort payloadType = PayloadTypeOf(payload);
        IdleAction action = StreamIdleHandler.Route(payloadType);

        StreamFlagRaised? raised = action == IdleAction.Disconnect ? Disconnect(payload) : null;

        Handled++;

        return new ArrivalReading(
            TakionRoute.ToData, TakionData.Protobuf, ProtobufHandler.Idle, Idle: action, Raised: raised);
    }

    private StreamFlagRaised? Disconnect(ReadOnlySpan<byte> payload)
    {
        DisconnectReading read = DisconnectMessage.Read(payload);

        if (!read.Disconnected)
            return null;

        host.SignalRemoteDisconnected(read.Reason);
        return StreamFlagRaised.RemoteDisconnected;
    }

    /// <summary>The protobuf's payload type, or one no arm names where it will not decode.</summary>
    private static ushort PayloadTypeOf(ReadOnlySpan<byte> payload)
    {
        Tkproto.TakionMessage message;

        try
        {
            message = Tkproto.TakionMessage.Parser.ParseFrom(payload.ToArray());
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            // The C's decode failure, which logs and returns - and the default arm does nothing
            // either, so an undecodable message and an unnamed type are the same silence.
            return NoArmNames;
        }

        // PP730: nanopb refuses a message missing a required field and protoc's parser does not, so
        // the idle handler's switch runs on bytes the console's own decoder would have thrown out.
        // The C reaches that switch only after pb_decode said yes, which is this line.
        return RequiredFields.AllPresentIn(message) ? (ushort)message.Type : NoArmNames;
    }

    /// <summary>A payload type the idle switch has no arm for, which is what a failed decode reads as.</summary>
    private const ushort NoArmNames = ushort.MaxValue;

    private ArrivalReading Bang(BangReading read, StreamFlagRaised? raised)
    {
        Handled++;

        return new ArrivalReading(
            TakionRoute.ToData, TakionData.Protobuf, ProtobufHandler.ExpectBang, read.Outcome, Raised: raised);
    }

    private ArrivalReading Info(StreamInfoReading read, StreamFlagRaised? raised)
    {
        Handled++;

        return new ArrivalReading(
            TakionRoute.ToData,
            TakionData.Protobuf,
            ProtobufHandler.ExpectStreaminfo,
            StreamInfo: read.Verdict,
            Raised: raised);
    }

    private ArrivalReading Record(ArrivalReading reading)
    {
        lock (readings)
            readings.Add(reading);

        return reading;
    }

    /// <summary>
    /// The keying a caller that supplied none gets, which refuses at the derive.
    ///
    /// Not a null check at each call site: the C always has a chiaki_ecdh_derive_secret to call and
    /// it is allowed to say no, so the case with no keying is that answer rather than a branch this
    /// port invents.
    /// </summary>
    private sealed class Refuses : IBangKeying
    {
        public static Refuses Instance { get; } = new();

        public bool DeriveSecret(ReadOnlySpan<byte> remotePubKey, ReadOnlySpan<byte> remoteSig) => false;

        public bool InitCrypt() => false;
    }
}
