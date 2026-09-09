using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>
/// PP762: the composition root the handover was waiting for.
///
/// PP696 replaced the C's stream run with a callback and nothing installed one, so a live session
/// reached the stream phase and stopped. Every piece existed - PP753's handover, PP754's runner,
/// PP745's host - and no file put them together. PP763 put the C's run back; this is what has to
/// exist before it can come out again.
///
/// PP765 MEASURED WHAT ONE COSTS: eleven parts, ten composing from work that shipped and one - the
/// BIG - reaching into the session. PP766 made that reach possible with four readers over fields the
/// C holds.
///
/// THE BIG IS BUILT LATE, AND THE FACTORY IS WHY. The host takes it as a Func rather than a message
/// because none of its material exists when this object is made: the session id arrives with ctrl's
/// handshake, the numbers with senkusha, and the ecdh pair is created on the line before the run and
/// freed on the line after. So the factory is evaluated inside the run, when the session has all
/// four - and a root that built the message eagerly would send a console four empty fields.
///
/// INSTALLED BEFORE THE SESSION STARTS. The C reaches the stream phase on its own thread and does
/// not ask twice; a handover installed after the start races that thread for the one moment it
/// looks.
/// </summary>
public sealed class ManagedStreamPhase : IDisposable
{
    private readonly StreamHandover handover = new();
    private readonly ManagedStreamRunner runner;
    private readonly ChiakiSession session;

    private Thread? thread;
    private ManagedOpusDecoder? speaker;

    /// <summary>
    /// PP771: THE PORT IS THIS OBJECT'S AND NOT THE CALLER'S, which is what an address takes.
    ///
    /// A caller handed an endpoint, and the first one written passed 9295 - the ctrl and discovery
    /// port, which is the number everything about a session says. The stream takion is on 9296, and
    /// a console answers nothing at all on the other one: three INIT attempts, no reply, and a run
    /// that stopped at the connect looking like a handshake this port had got wrong.
    ///
    /// It had not. Aimed at 9296 the same handshake completed with a real PS5 on the first attempt
    /// each way. So the number is taken here rather than asked for, and it is
    /// <see cref="SessionRelay.StreamPort"/> rather than a second 9296 - the rule CtrlConnect's own
    /// port follows, for the same reason.
    /// </summary>
    /// <param name="session">The C session, whose stream phase this takes. Not owned.</param>
    /// <param name="console">The console's address, which discovery already answered with.</param>
    /// <param name="video">Where a decoded frame goes, which is the caller's decoder.</param>
    /// <param name="baseline">The record the four stage timings are pushed into.</param>
    /// <param name="lossMax">The packet loss maximum, which rides on the connect info.</param>
    public ManagedStreamPhase(
        ChiakiSession session,
        IPAddress console,
        VideoSampleHandler video,
        SessionBaseline baseline,
        double lossMax = 0.05)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(baseline);

        this.session = session;

        var peer = new IPEndPoint(console, SessionRelay.StreamPort);
        runner = new ManagedStreamRunner(() => Build(session, peer, video, baseline, lossMax));
    }

    /// <summary>
    /// PP773: what arrived and what each arrival came to, or null before the run built a host.
    ///
    /// The next rung of the ladder PP770, PP771 and PP772 built. Those say how far the walk got and
    /// what the handshake answered; a walk that stops at a wait says nothing about WHY, and the two
    /// causes look identical from outside - nothing arrived, or something arrived and no handler
    /// claimed it. This is the reading that separates them, and it cost a console trial to learn
    /// that the first was the case.
    /// </summary>
    public StreamArrivals? Arrivals { get; private set; }

    /// <summary>What the run answered, or null while it has not finished.</summary>
    public StreamRunnerOutcome? Outcome { get; private set; }

    /// <summary>
    /// PP799: where the run's nine events land, or null before the run built a host.
    ///
    /// Held so a caller can read what the session's handler cannot carry - the rumble bytes, the LED
    /// colour, the player index - which is why PP747 made the router hold them rather than translate
    /// them into a second seam.
    /// </summary>
    public SessionEventRouter? Events { get; private set; }

    /// <summary>
    /// PP771: the host the run built, or null where no start ever came.
    ///
    /// Reached through so a caller can read what the outcome cannot carry - the handshake's own
    /// error and its attempt counts, which are what say which of takion's four messages went
    /// unanswered. The outcome says which rung; this says what happened on it.
    /// </summary>
    public ManagedStreamRunHost? Host => runner.Host;

    /// <summary>Whether the session's stop has reached the handover.</summary>
    public bool Stopped => handover.Stopped;

    /// <summary>
    /// PP783: where a pad pushes now, which is not the C session any more.
    ///
    /// chiaki_session_set_controller_state handed what it took to the stream connection's feedback
    /// sender, and the flip leaves that call storing a state nothing will ever send. PP756 wrote the
    /// managed replacement and nothing built one, so a client that flipped and kept pushing at the
    /// session would stream a picture and send no input - a regression no frame count would show.
    ///
    /// BUILT IN THE CONSTRUCTOR AND ARMED IN THE RUN, which is the C's own split: the session holds
    /// controller_state from the moment it exists, and the sender is handed it when one starts. A
    /// caller therefore has somewhere to push before the console has answered, and PP756's hold is
    /// what makes those pushes count rather than being dropped for arriving early.
    /// </summary>
    public ManagedControllerStateSink Pad { get; } = new();

    /// <summary>
    /// PP816: where the sound goes - the engine, and nothing before it.
    ///
    /// PP797's AudioPlayback and the ring behind it stood here and PP820 removed them. Built in the
    /// constructor and OPENED in the run, which is the same split the pad has and the same rule
    /// PP754 gives: a caller needs somewhere to read counts from before the console has answered,
    /// and opening a device is what should wait for a session that actually arrived.
    /// </summary>
    public XAudio2Sink Sound { get; } = new();

    /// <summary>
    /// PP825: whether to open a microphone, and which generation's head to format for.
    ///
    /// NULL IS OFF AND IS THE DEFAULT, which is a product rule rather than a porting one: a client
    /// that opened the microphone because a session started would be recording a room nobody said
    /// it could. So this is the one join in this root a caller has to ask for.
    ///
    /// THE GENERATION IS IN THE SAME FIELD BECAUSE THERE IS NO SAFE DEFAULT FOR IT. The head is
    /// nineteen bytes on a PS4 and twenty on a PS5, and a bool with a default would be silently
    /// wrong on one of them; a caller that knows enough to want a microphone knows which console it
    /// is talking to, because it chose the target when it built the connect info.
    /// </summary>
    public bool? Microphone { get; set; }

    /// <summary>The microphone this run opened, or null where none was asked for.</summary>
    public ManagedMicrophone? Capture { get; private set; }

    /// <summary>What decoded the sound, or null where no run built one.</summary>
    public ManagedOpusDecoder? Speaker => speaker;

    /// <summary>
    /// Install this as the session's stream phase and start waiting for it.
    ///
    /// Before <see cref="ChiakiSession.Start"/>, and the runner's thread goes up first: the C
    /// signals the handover and then blocks on it, so a runner that had not reached its wait would
    /// make the session thread wait out a slice for nothing.
    /// </summary>
    public void InstallOn()
    {
        if (thread is not null)
            throw new InvalidOperationException("this phase is already installed.");

        thread = new Thread(() => Outcome = runner.Run(handover))
        {
            IsBackground = true,
            Name = "managed stream run",
        };

        thread.Start();
        handover.InstallOn(session);
    }

    /// <summary>Waits for the run to finish, which the session thread has already been told about.</summary>
    public bool Join(TimeSpan timeout) => thread is null || thread.Join(timeout);

    /// <summary>
    /// The eleven parts, in the constructor's own order.
    ///
    /// Built inside the runner rather than in this object's own constructor, for the reason PP754
    /// gives: a start that never comes should build no host, because constructing one takes a
    /// socket.
    /// </summary>
    private ManagedStreamRunHost Build(
        ChiakiSession session,
        IPEndPoint peer,
        VideoSampleHandler video,
        SessionBaseline baseline,
        double lossMax)
    {
        // PP773: THE CALLBACK IS TIED IN A KNOT, and the knot is the C's own. The takion's dispatch
        // reaches the arrivals, the arrivals raise the host's flags, and the host owns the takion -
        // so one of the three has to be built before something it needs exists. The C resolves it
        // the same way, passing &stream_connection to chiaki_takion_connect after the struct is
        // there and before it is finished; here the closure reads a local the line below assigns.
        StreamArrivals? arrivals = null;

        var takion = new ManagedTakion(Tag(), datagram => arrivals?.Datagram(datagram));

        var messages = new TakionMessageSink(takion);
        var outbound = new StreamOutbound(messages);
        var events = new ManagedSessionEvents();

        // PP799: AND SOMEBODY LISTENING TO IT, which no file had ever been. PP719 built the seam and
        // PP747 built the sink, and the root that would have joined them made the seam and stopped -
        // so a live run took sixteen thousand datagrams, decoded 2675 frames, and raised a CONNECTED
        // that was counted Unheard four states before the window gave up waiting for it.
        Events = ListenerFor(session, events);

        // PP783: THE PAD'S SENDER, held rather than passed straight in, because two things need it.
        // The host runs it; the controller sink replays into it. streamconnection.c does the same in
        // one line after its own init, and PP756 wrote the managed half of that - what was missing is
        // the line that hands one to the other, which only a composition root can write.
        var sender = new ManagedFeedbackSender(new TakionFeedbackSink(takion));
        Pad.Arm(sender);

        // PP797: AND THE SOUND HAS SOMEWHERE TO GO, which is the first of the two joins that were
        // missing. Both arms took a NoFrames whose methods are empty, so audioreceiver.c's port ran
        // its jitter buffer and handed every frame to nothing - a sentence in a lambda rather than a
        // fact anything reported.
        //
        // THE AUDIO ARM ONLY. Haptics is the same receiver used twice and its far side is a pad's
        // motors rather than a speaker, so giving it a decoder would send a console's rumble
        // envelope to the endpoint the game's audio is on.
        //
        // OPENED HERE AND NOT IN THE CONSTRUCTOR, which is PP754's rule: a start that never comes
        // should open no device, and this runs only once the session has handed the phase over.
        // PP816: and the ring, the drain and the pump are out of the path. The decoder writes into
        // the engine on the thread that decoded, so there is no drain thread to start - Audio.Start()
        // used to put one up, and its absence IS the change.
        //
        // THE DEVICE IS OPENED HERE AND NOT IN THE HANDLER, which two live runs decided: opening it
        // from the STREAMINFO callback puts a device open on the takion's receive thread, and the
        // run completed after eleven arrivals both times. This is PP754's rule as well - a start
        // that never comes should open no device, and Build runs only once the session hands over.
        Sound.Prepare();
        ManagedAudioReceiverPair sound = AudioPair(Sound, out speaker);

        var host = new ManagedStreamRunHost(
            takion,
            peer,
            new ManagedCongestionControl(new ManagedPacketStats(), new TakionCongestionSink(takion), lossMax),
            sender,
            events,
            messages,
            new BaselineStages(baseline),
            // PP827: the BIG is the one message the C fragments, and the cut is decided HERE
            // because this is the moment senkusha's measurement is legible. The sink is built
            // before the link is measured, so a cut fixed at construction would be the fallback on
            // every session; read at BIG time it is what the console and this client agreed on.
            () =>
            {
                if (SessionBigMaterial.TransportOf(session) is { } measured)
                    messages.Mtu = TakionMessageSink.UsableMtu(measured.MtuIn, measured.MtuOut);

                return Big(session);
            },
            () => new ManagedVideoReceiver(video, outbound),
            () => sound,
            () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()));

        // PP773: AND THE KEYING, which this root passed null for in the commit that wired the
        // arrivals. A bang then reached the handler and was refused at the derive - one wait further
        // than before and still not a stream. SessionBangKeying derives against the session's OWN
        // ecdh pair, which is the only one whose public half the console was ever sent.
        // PP795: and the arm is joined here, because this is the one line that can see both ends -
        // the keying, which learns the crypt, and the host, which holds the receivers. Neither
        // knows the other and a composition root is what a knot like that is for.
        var keying = new SessionBangKeying(session, takion)
        {
            InstallArm = crypt => host.InstallAvArm(crypt),
        };

        // PP824: AND THE ACKS ARE JOINED HERE, which is the line PP807 built the seam for and left
        // out on purpose. ManagedTakion.AckData is the third thing the arrivals take off the takion
        // and the only one that is not a value, for the reason StreamArrivals' own note gives.
        //
        // WHAT THE JOIN COST WHILE THE ACK LEFT UNSIGNED is why it waited two tasks rather than a
        // rerun. Three live runs on 2026-09-07 against a console in an active game:
        //
        //   with it     1171 and 1265 arrival(s), 0 decoded, 0 audio frame(s), RemoteDisconnected
        //   without it  14158 arrival(s), 984 decoded, 1863 audio unit(s), no disconnect
        //
        // chiaki_takion_send MACs every packet under gkcrypt_local before handing it to send_raw,
        // so about 1170 unsigned control packets reached a console that verifies them, and it
        // dropped the session rather than the packets.
        //
        // PP811 SIGNED IT AND PP823 PUT THE SIGNATURE WHERE THE CONSOLE LOOKS FOR IT. The ack goes
        // out through TakionSendPath, so it is MAC'd the way the C's is; and the position it is
        // MAC'd AT is key_pos_local, the send counter the C keeps, rather than the receive ledger
        // this port had been reading for all four sends. A signed ack at a position the console did
        // not advance is a tag it cannot verify - grounds to reject, where an unsigned one was only
        // a field to ignore - which is why PP823 had to land before this line could be written.
        //
        // AND THE SIGNED ACK IS MEASURED NOW, which is what PP811 and PP823 left owing. Two live
        // runs on 2026-09-09 against a console in an active game, this line in:
        //
        //   5989 arrival(s), 977 decoded, 953 shown, 1848 audio unit(s), no teardown
        //   11093 arrival(s), 958 decoded, 936 shown, 1772 audio unit(s), 81 ack(s) sent
        //
        // The second run is the one that can say the acks LEFT rather than that the session
        // survived them: `[stream] data acks` reports what StreamArrivals counted, and 81 of them
        // went out MAC'd at key_pos_local against the 1170-odd unsigned ones that ended a session.
        arrivals = new StreamArrivals(
            host, messages, keying, new ManagedStreamData(events))
        {
            TagLocal = takion.TagLocal,
            Ledger = takion.Ledger,
            Acks = takion,
        };

        // PP808: AND THE DISCONNECT, the other half of the knot above. The dispatch closure carries
        // datagrams and an event is not one, so the takion going away told nobody and the connect
        // wait ran its whole timeout - PP366's silence, reachable here for the first time. The
        // handler is deliberately the same shape as the closure: it reads the local this line's
        // predecessor assigned.
        //
        // AFTER THE FIRST STATE IT IS DROPPED, and that is the C and not a gap here.
        // StreamDispatch.Route ignores a disconnect outside TAKION_CONNECT, which is the finding
        // PP365 and PP366 recorded rather than a rule this port gets to tighten.
        takion.Disconnected = () => arrivals?.Disconnected();

        // And the replay, which is the same handler over the message the bang state buffered.
        host.ReplayHandler = arrivals.Replay;
        Arrivals = arrivals;

        // PP825: THE MICROPHONE, and only where a caller asked for one. Every stage existed - the
        // capture, the canceller, the encoder, the formatter and the send - and the path had never
        // run with a device on one end and a socket on the other.
        //
        // OPENED HERE FOR THE REASON THE SOUND IS: PP754's rule says a start that never comes should
        // open no device, and this runs only once the session has handed the phase over. It is the
        // same argument twice, and a microphone is the one it matters most for.
        if (Microphone is { } micPs5)
        {
            var microphone = new ManagedMicrophone(
                micPs5,
                (packet, length) => takion.SendMicrophone(packet.AsSpan(0, length), micPs5));

            // Reported rather than thrown: a machine with no capture endpoint still has a session,
            // and Opened says which of the three it was.
            microphone.Start();
            Capture = microphone;
        }

        return host;
    }

    /// <summary>
    /// PP797: the sound arm, built where a machine with no console can ask for one.
    ///
    /// Public and static for <see cref="Big"/>'s reason and PP767's: <see cref="Build"/> runs only
    /// once a session hands over, so a join made inside it is unreachable through the object - and
    /// this is exactly the join whose absence let a stream decode a picture and drop every audio
    /// frame it produced.
    ///
    /// THE HAPTICS ARM STAYS A NO-OP, which is not an omission. audioreceiver.c is used twice and
    /// the second one's far side is a pad's motors; a decoder there would send a rumble envelope to
    /// the endpoint the game's audio is on.
    /// </summary>
    /// <param name="sound">
    /// Where a decoded frame goes. PP820: the seam rather than a playback, because the ring this
    /// used to reach through is gone - <see cref="XAudio2Sink"/> hands the frame to the engine and
    /// there is nothing in between to name.
    /// </param>
    /// <param name="speaker">The decoder, which the caller owns and must dispose.</param>
    public static ManagedAudioReceiverPair AudioPair(IPcmSink sound, out ManagedOpusDecoder speaker)
    {
        ArgumentNullException.ThrowIfNull(sound);

        speaker = new ManagedOpusDecoder(sound);

        return new ManagedAudioReceiverPair(speaker, new NoFrames());
    }

    /// <summary>
    /// PP799: attach the run's events to the application, and answer with what holds them.
    ///
    /// Public and static for <see cref="Big"/>'s reason: <see cref="Build"/> runs only once a start
    /// arrives, so on a machine with no console this join is unreachable through the object - and it
    /// is the join whose absence let a streaming session report "the console did not connect".
    ///
    /// CONNECTED IS THE ONE THAT TRAVELS. The other eight stop at the router by design, which PP747
    /// argues: a screen saying "Rumble" would be reading the enum aloud, and what a pad driver wants
    /// is the value rather than the occurrence. This one goes on to the session's own handler, so a
    /// caller that installed one hears the port's run exactly as it hears the C's - which is what
    /// makes the driver behind <see cref="StreamPhaseDriver"/> invisible from outside.
    /// </summary>
    /// <param name="session">The session whose handler the connected event is forwarded to.</param>
    /// <param name="events">The run's seam, which drops what it raises until this is called.</param>
    public static SessionEventRouter ListenerFor(ChiakiSession session, ManagedSessionEvents events)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(events);

        var router = new SessionEventRouter(() => session.Raise(ChiakiEventType.Connected));
        events.Listen(router);

        return router;
    }

    /// <summary>
    /// The BIG, built when the run asks for it and not before.
    ///
    /// Public because it is the interesting half and the only half a machine with no console can
    /// be asked about: what each of its four refusals says when the session is not where this
    /// thinks it is.
    ///
    /// Every one of its four session-side arguments arrives at a different moment, and the last is
    /// alive only across the run - so this is evaluated inside the stream phase or not at all. A
    /// null from any reader is a session that is not where this thinks it is, and the disconnect
    /// that follows is better than a message the console refuses without saying why.
    /// </summary>
    public static StreamMessage Big(ChiakiSession session)
    {
        string id = SessionBigMaterial.IdOf(session)
            ?? throw new InvalidOperationException("the session has no id yet, so a BIG cannot name one.");

        byte[] handshakeKey = SessionBigMaterial.HandshakeKeyOf(session)
            ?? throw new InvalidOperationException("the session has no handshake key yet.");

        SessionTransport transport = SessionBigMaterial.TransportOf(session)
            ?? throw new InvalidOperationException("senkusha has not measured this link yet.");

        SessionEcdhMaterial ecdh = SessionBigMaterial.EcdhOf(session)
            ?? throw new InvalidOperationException("the session's ecdh pair does not exist yet.");

        // PP777: the two the root used to invent. A console acked a BIG built from zeroes and a
        // fixed 720p and answered nothing at all, which is what a message it cannot read looks like
        // from this side - there is no refusal to log.
        SessionAuthMaterial auth = SessionBigMaterial.AuthOf(session)
            ?? throw new InvalidOperationException("ctrl's handshake has not produced a nonce yet.");

        SessionVideoProfile profile = SessionBigMaterial.ProfileOf(session)
            ?? throw new InvalidOperationException("the connect info describes no stream.");

        var fields = new LaunchSpecFields(
            Width: profile.Width,
            Height: profile.Height,
            MaxFps: profile.MaxFps,
            BwKbpsSent: profile.BitrateKbps,

            // PP777: MTU_IN and not out. The C reads session->mtu_in here; senkusha measures the two
            // separately and they need not agree, so the other one describes a link in the wrong
            // direction.
            Mtu: transport.MtuIn,

            // Milliseconds, which is what the spec's field is - senkusha measures microseconds.
            Rtt: (uint)(transport.RoundTripMicroseconds / 1000),
            Target: auth.Target,
            Codec: profile.Codec);

        // The SESSION's crypt, which is chiaki_rpcrypt_init_auth over the same three values.
        using var crypt = new RpCrypt(auth.Target, auth.Nonce, auth.Morning);

        string spec = BigMessage.EncodedLaunchSpec(crypt, fields, handshakeKey)
            ?? throw new InvalidOperationException("the launch spec would not fit the C's buffer.");

        byte[] body = BigMessage.Encode(
            clientVersion: 9,
            sessionKey: id,
            encodedLaunchSpec: spec,
            ecdhPubKey: ecdh.PublicKey,
            ecdhSig: ecdh.Signature);

        // PP778: the BIG goes out on channel ONE, and this spelled zero. The C's two senders both
        // pass 1 there - chiaki_takion_send_message_data(&takion, 1, 1, ...) - and the payload type
        // is zero because a BIG has none: it is not one of the typed stream messages.
        return new StreamMessage(DataType: StreamMessages.OrdinaryData, PayloadType: 0, Body: body);
    }

    /// <summary>
    /// A takion's local tag, which is drawn per session and identifies nothing else.
    ///
    /// chiaki_random_32's job on the C side, and the same property is what matters: the console
    /// echoes it, so two sessions must not share one.
    /// </summary>
    private static uint Tag()
    {
        Span<byte> four = stackalloc byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(four);
        return BitConverter.ToUInt32(four);
    }

    /// <summary>
    /// PP768: cancel, join, then free - in that order and never any other.
    ///
    /// The first version of this freed the handover while the runner's thread was still blocked in
    /// await_start on it, so the wait ran on memory the allocator had taken back. It did not fail
    /// reliably: three runs of the gate gave one truncated run and two clean ones, and the phase's
    /// own tests passed every time because the process exited before the thread noticed.
    ///
    /// The join is bounded because a free is not worth hanging a shutdown over, and it is longer
    /// than a cancelled wait needs: cancel signals the condition the thread is on, so the only way
    /// this waits the whole window is a thread that is not where this thinks it is - and in that
    /// case NOT freeing is the right answer, which is what the guard below does.
    /// </summary>
    public void Dispose()
    {
        if (thread is { } running)
        {
            handover.Cancel();

            if (!running.Join(TimeSpan.FromSeconds(2)))
            {
                // Leaked deliberately. A handover freed under a live waiter is the defect this
                // whole method exists for, and a leak of one object at shutdown is the cheaper of
                // the two by a long way.
                GC.SuppressFinalize(this);
                return;
            }
        }

        // PP816: the sound path comes down with the run, decoder first. It is what submits, so it
        // goes down before the voice it submits into - a voice destroyed under a live decoder would
        // be handed a buffer it no longer owns. PP797's order was the same argument about a ring.
        speaker?.Dispose();
        Sound.Dispose();

        // PP825: and the microphone, which goes down for the same reason and in the same direction -
        // the capture's pump thread runs the whole path, so it is stopped before the takion it sends
        // into is torn down.
        Capture?.Dispose();

        handover.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Audio that goes nowhere, which is what this root has for it today.
    ///
    /// Stated rather than left as a lambda: the picture has a decoder to reach and the sound has
    /// none yet, and a reader deserves to see which of the two that is.
    /// </summary>
    private sealed class NoFrames : IAudioFrameSink
    {
        public void Header(in ManagedAudioHeader header)
        {
        }

        public void Frame(ReadOnlySpan<byte> frame)
        {
        }
    }
}
