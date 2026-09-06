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

        var host = new ManagedStreamRunHost(
            takion,
            peer,
            new ManagedCongestionControl(new ManagedPacketStats(), new TakionCongestionSink(takion), lossMax),
            new ManagedFeedbackSender(new TakionFeedbackSink(takion)),
            events,
            messages,
            new BaselineStages(baseline),
            () => Big(session),
            () => new ManagedVideoReceiver(video, outbound),
            () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()),
            () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()));

        // PP773: AND THE KEYING, which this root passed null for in the commit that wired the
        // arrivals. A bang then reached the handler and was refused at the derive - one wait further
        // than before and still not a stream. SessionBangKeying derives against the session's OWN
        // ecdh pair, which is the only one whose public half the console was ever sent.
        arrivals = new StreamArrivals(
            host, messages, new SessionBangKeying(session, takion), new ManagedStreamData(events))
        {
            TagLocal = takion.TagLocal,
            Ledger = takion.Ledger,
        };

        // And the replay, which is the same handler over the message the bang state buffered.
        host.ReplayHandler = arrivals.Replay;
        Arrivals = arrivals;

        return host;
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

        var fields = new LaunchSpecFields(
            Width: 1280,
            Height: 720,
            MaxFps: 60,
            BwKbpsSent: 10000,
            Mtu: transport.MtuOut,

            // Milliseconds, which is what the spec's field is - senkusha measures microseconds.
            Rtt: (uint)(transport.RoundTripMicroseconds / 1000),
            Target: ChiakiTarget.Ps5_1,
            Codec: ChiakiCodec.H264);

        var crypt = new RpCrypt(ChiakiTarget.Ps5_1, new byte[16], new byte[16]);

        string spec = BigMessage.EncodedLaunchSpec(crypt, fields, handshakeKey)
            ?? throw new InvalidOperationException("the launch spec would not fit the C's buffer.");

        byte[] body = BigMessage.Encode(
            clientVersion: 9,
            sessionKey: id,
            encodedLaunchSpec: spec,
            ecdhPubKey: ecdh.PublicKey,
            ecdhSig: ecdh.Signature);

        return new StreamMessage(DataType: 0, PayloadType: 0, Body: body);
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
