using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>Where the client is in the exchange.</summary>
public enum TakionClientState
{
    /// <summary>The INIT is what goes out next; nothing has been accepted yet.</summary>
    AwaitingInitAck,

    /// <summary>The INIT_ACK passed both gates; the COOKIE is what goes out next.</summary>
    AwaitingCookieAck,

    /// <summary>The COOKIE_ACK arrived: takion is a transport, and the C would fire CONNECTED here.</summary>
    Connected,

    /// <summary>An INIT_ACK was refused at a gate, which no retry answers.</summary>
    Refused,
}

/// <summary>
/// What the handshake asks of the world: send one datagram, receive one into a buffer of a given size
/// within a timeout. The socket is supplied separately, the way PP606's responder has none of its
/// own, so the exchange runs in a test with no network and over loopback with one.
/// </summary>
public interface ITakionHandshakeWire
{
    /// <summary>One datagram out. Network for a failed send, as chiaki_takion_send_raw reports it.</summary>
    ChiakiError Send(ReadOnlySpan<byte> datagram);

    /// <summary>
    /// One datagram in, into <paramref name="into"/> and no further.
    /// </summary>
    /// <returns>
    /// Success with the length; Timeout, unlogged as the C leaves it; or Network for anything else -
    /// a recv of zero, a socket error, and winsock refusing a datagram longer than the buffer.
    /// </returns>
    ChiakiError Receive(Span<byte> into, int timeoutMs, out int length);
}

/// <summary>How a run of the handshake ended.</summary>
/// <param name="Error">Success, or what the C's takion_handshake would have returned.</param>
/// <param name="InitAttempts">How many INITs went out, one plus the retries.</param>
/// <param name="CookieAttempts">How many COOKIEs went out; zero where the INIT_ACK never passed.</param>
public readonly record struct TakionHandshakeOutcome(ChiakiError Error, int InitAttempts, int CookieAttempts);

/// <summary>
/// PP672, under PP27: takion's handshake with this port as the CLIENT - the four messages that have to
/// cross before there is a transport, run to a connected state against a peer that answers.
///
/// PP450 modelled the rules and PP603 to PP607 built and proved the console's side; the client's was
/// bytes in two test helpers and a loop nothing ran. This is that loop: INIT out and INIT_ACK back, up
/// to three times at five seconds each; the two gates on the ack, in the C's order; COOKIE out and
/// COOKIE_ACK back, the same three times. <see cref="TakionHandshake.Exchange"/> is the retry rule
/// and this is the first thing that runs it over a wire.
///
/// THE SHAPE IS THE RESPONDER'S, TURNED ROUND. A state machine that decides what to send and what a
/// datagram means, with no socket in it, so the exchange is tested with none; and <see cref="Run"/>,
/// which drives it through an <see cref="ITakionHandshakeWire"/> - <see cref="TakionUdpWire"/> over a
/// connected UDP socket, or a double.
///
/// TWO STEPS WHERE THE C HAS TWO. <see cref="TakeInitAck"/> is the receive's judgement of the
/// datagram's shape, which a retry answers; <see cref="Gate"/> is the two checks after the loop - a
/// zero tag, then the crossed stream counts - which no retry answers, because the C returns from them
/// rather than going round. Keeping them apart is what lets Run retry the one and abort on the other.
///
/// THE RECEIVES ARE THE C'S SIZE. The INIT_ACK is read into sixty-five bytes and the COOKIE_ACK into
/// seventeen, as the C declares its arrays, because on winsock a datagram longer than its buffer fails
/// the recv rather than truncating - and that is behaviour, not detail: it is why the late-INIT_ACK
/// tolerance in the cookie ack never fires on Windows and the retry covers it instead.
///
/// The handshake runs once a session, so what it allocates is not on PP44's path; the C's own runs on
/// the receive thread it just started, and here it runs on the caller's, since a takion that owns a
/// thread is PP678's.
/// </summary>
public sealed class TakionHandshakeClient
{
    private TakionInitAck ack;
    private byte[]? cookie;
    private bool ackRead;

    /// <param name="tagLocal">
    /// The tag this client draws for itself. The C takes it from chiaki_random_32 inside connect
    /// (PP602); the caller supplies it here so a test can name it and a byte comparison can use it.
    /// </param>
    public TakionHandshakeClient(uint tagLocal) => TagLocal = tagLocal;

    /// <summary>Our tag, which every inbound header has to carry and the INIT advertises twice.</summary>
    public uint TagLocal { get; }

    /// <summary>The peer's tag, zero until the INIT_ACK names it - and the header tag of everything sent after.</summary>
    public uint TagRemote { get; private set; }

    /// <summary>Where the exchange stands.</summary>
    public TakionClientState State { get; private set; } = TakionClientState.AwaitingInitAck;

    /// <summary>What <see cref="Gate"/> decided, or null before it ran.</summary>
    public TakionInitAckVerdict? Verdict { get; private set; }

    /// <summary>
    /// The sequence number the data queue is seeded with: the remote TAG, not the ack's wire field.
    /// Zero until the gate accepted an ack.
    /// </summary>
    public uint RemoteInitialSeqNum
        => Verdict == TakionInitAckVerdict.Accepted ? TakionHandshake.RemoteInitialSeqNum(ack) : 0;

    /// <summary>The INIT, which is the same bytes on every retry.</summary>
    public byte[] Init() => TakionClientDatagrams.WriteInit(TagLocal);

    /// <summary>
    /// One INIT_ACK datagram judged for its shape, as takion_recv_message_init_ack judges it.
    ///
    /// Success holds the five fields and the cookie for the gate; InvalidResponse is what the C's loop
    /// goes round on. The state does not move here - the C's loop breaks on a well-formed ack and the
    /// gates decide afterwards, which is <see cref="Gate"/>.
    /// </summary>
    public ChiakiError TakeInitAck(ReadOnlySpan<byte> datagram)
    {
        ChiakiError err = TakionInitAckDatagram.Read(datagram, TagLocal, out TakionInitAckReading reading);
        if (err != ChiakiError.Success)
            return err;

        ack = reading.Ack;
        cookie = reading.Cookie;
        ackRead = true;
        return ChiakiError.Success;
    }

    /// <summary>
    /// The two gates after the loop, in the C's order: a zero remote tag, then the crossed stream
    /// counts. Neither is retried.
    ///
    /// tag_remote is assigned BETWEEN the two, as the C does it: an ack refused for its stream counts
    /// has already named the peer, and one refused for a zero tag has not. Nothing downstream reads
    /// the difference today; it is kept because the C keeps it.
    /// </summary>
    public TakionInitAckVerdict Gate()
    {
        if (!ackRead)
            throw new InvalidOperationException("no INIT_ACK has been read, so there is nothing to gate");

        TakionInitAckVerdict verdict = TakionHandshake.Check(ack);
        Verdict = verdict;

        if (verdict != TakionInitAckVerdict.ZeroTag)
            TagRemote = ack.Tag;

        State = verdict == TakionInitAckVerdict.Accepted
            ? TakionClientState.AwaitingCookieAck
            : TakionClientState.Refused;

        return verdict;
    }

    /// <summary>The COOKIE, echoing what the accepted ack carried under the tag it named.</summary>
    public byte[] CookieMessage()
    {
        if (State != TakionClientState.AwaitingCookieAck || cookie is null)
            throw new InvalidOperationException("no INIT_ACK has been accepted, so there is no cookie to echo");

        return TakionClientDatagrams.WriteCookie(TagRemote, cookie);
    }

    /// <summary>
    /// One datagram where the COOKIE_ACK is expected, judged as takion_recv_message_cookie_ack judges
    /// it. Accepted is the connected state; ReadAnother asks the caller to receive once more and judge
    /// that one with <paramref name="secondRead"/> set.
    /// </summary>
    public TakionCookieAckVerdict TakeCookieAck(ReadOnlySpan<byte> datagram, bool secondRead = false)
    {
        TakionCookieAckVerdict verdict = TakionMessageHeader.ReadCookieAck(datagram, TagLocal, secondRead);
        if (verdict == TakionCookieAckVerdict.Accepted)
            State = TakionClientState.Connected;

        return verdict;
    }

    /// <summary>
    /// The whole exchange over a wire: takion_handshake, run to a connected state or to the error the
    /// C would return.
    /// </summary>
    /// <param name="wire">Where the datagrams go and come from.</param>
    /// <param name="expectTimeoutMs">
    /// How long one attempt waits for its ack: TAKION_EXPECT_TIMEOUT_MS unless a test that wants to
    /// see a retry says otherwise, since three of the C's are fifteen seconds.
    /// </param>
    public TakionHandshakeOutcome Run(
        ITakionHandshakeWire wire, int expectTimeoutMs = TakionHandshake.ExpectTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(wire);

        // INIT -> and INIT_ACK <-, into the C's sixty-five bytes.
        byte[] initAck = new byte[TakionHandshake.InitAckDatagramSize];
        TakionExchange first = TakionHandshake.Exchange(_ =>
        {
            ChiakiError sent = wire.Send(Init());
            if (sent != ChiakiError.Success)
                return (sent, ChiakiError.Success);

            ChiakiError received = wire.Receive(initAck, expectTimeoutMs, out int length);
            return received != ChiakiError.Success
                ? (ChiakiError.Success, received)
                : (ChiakiError.Success, TakeInitAck(initAck.AsSpan(0, length)));
        });

        if (first.Error != ChiakiError.Success)
            return new TakionHandshakeOutcome(first.Error, first.Attempts, 0);

        if (Gate() != TakionInitAckVerdict.Accepted)
            return new TakionHandshakeOutcome(ChiakiError.InvalidResponse, first.Attempts, 0);

        // COOKIE -> and COOKIE_ACK <-, into the C's seventeen, both receives with the buffer's own
        // capacity - PP451's second repair.
        byte[] cookieAck = new byte[TakionHandshake.CookieAckDatagramSize];
        TakionExchange second = TakionHandshake.Exchange(_ =>
        {
            ChiakiError sent = wire.Send(CookieMessage());
            if (sent != ChiakiError.Success)
                return (sent, ChiakiError.Success);

            ChiakiError received = wire.Receive(cookieAck, expectTimeoutMs, out int length);
            if (received != ChiakiError.Success)
                return (ChiakiError.Success, received);

            TakionCookieAckVerdict verdict = TakeCookieAck(cookieAck.AsSpan(0, length));
            if (verdict == TakionCookieAckVerdict.ReadAnother)
            {
                received = wire.Receive(cookieAck, expectTimeoutMs, out length);
                if (received != ChiakiError.Success)
                    return (ChiakiError.Success, received);

                verdict = TakeCookieAck(cookieAck.AsSpan(0, length), secondRead: true);
            }

            return (ChiakiError.Success, verdict == TakionCookieAckVerdict.Accepted
                ? ChiakiError.Success
                : ChiakiError.InvalidResponse);
        });

        return new TakionHandshakeOutcome(second.Error, first.Attempts, second.Attempts);
    }
}

/// <summary>
/// PP672: the wire over a connected UDP socket, which is the socket takion makes for itself.
///
/// chiaki_takion_connect creates a datagram socket, sets SO_RCVBUF to the window the INIT advertises
/// and IP_DONTFRAGMENT either way, and connects it - so recv and send name no address, and a
/// zero-length datagram is a legal arrival the C reads as a network error (PP488). PP477 modelled
/// those choices; this is the first place they are made on a socket rather than described.
///
/// THE ERROR MAPPING IS THE C'S: a timeout is Timeout and silent; everything else the socket reports,
/// a recv of zero included, is Network. That includes WSAEMSGSIZE, winsock's answer to a datagram
/// longer than the buffer it was asked into - which is what makes the C's fixed-size receives refuse
/// a long ack on Windows rather than truncate it.
/// </summary>
public sealed class TakionUdpWire : ITakionHandshakeWire, IDisposable
{
    private readonly Socket socket;

    private TakionUdpWire(Socket socket) => this.socket = socket;

    /// <summary>
    /// A socket made and configured as the C's MadeHere branch makes it, then connected to the peer.
    /// </summary>
    /// <param name="peer">Where takion is connecting to.</param>
    /// <param name="dontFragment">The connect info's ip_dontfrag, set on the socket whichever way it goes.</param>
    public static TakionUdpWire Connect(IPEndPoint peer, bool dontFragment = false)
    {
        ArgumentNullException.ThrowIfNull(peer);

        var socket = new Socket(peer.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // SO_RCVBUF is the advertised window: the socket's buffer and the protocol's promise are
            // one number (PP477).
            socket.ReceiveBufferSize = (int)TakionSocket.ReceiveBufferIs;
            socket.DontFragment = dontFragment;
            socket.Connect(peer);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new TakionUdpWire(socket);
    }

    /// <summary>
    /// PP769: a wire over a socket somebody else made, connected and owns.
    ///
    /// The C's stream connection does not open one. chiaki_takion_connect takes the caller's socket,
    /// and for the stream phase that caller is session.c handing over data_sock - the socket senkusha
    /// established and measured the link on. A run that opened its own instead started a second
    /// conversation on the well-known port, and a console in the middle of the first one does not
    /// answer it. That is measured: a live handover failed the moment it tried.
    ///
    /// OWNERSHIP DOES NOT CROSS. The handle stays the C's, so this wraps it without owning it and
    /// closing it is not this side's to do - a Socket built the ordinary way would close the
    /// session's socket underneath it the first time anything disposed a takion.
    /// </summary>
    /// <param name="handle">A connected UDP socket the caller keeps.</param>
    public static TakionUdpWire Adopt(nint handle)
    {
        var borrowed = new SafeSocketHandle(handle, ownsHandle: false);

        try
        {
            return new TakionUdpWire(new Socket(borrowed));
        }
        catch
        {
            borrowed.Dispose();
            throw;
        }
    }

    /// <summary>The address the socket bound to on connect, which is what the peer sees.</summary>
    public IPEndPoint LocalEndPoint => (IPEndPoint)socket.LocalEndPoint!;

    /// <inheritdoc />
    public ChiakiError Send(ReadOnlySpan<byte> datagram)
    {
        try
        {
            socket.Send(datagram);
            return ChiakiError.Success;
        }
        catch (SocketException)
        {
            return ChiakiError.Network;
        }
    }

    /// <inheritdoc />
    public ChiakiError Receive(Span<byte> into, int timeoutMs, out int length)
    {
        length = 0;

        try
        {
            socket.ReceiveTimeout = timeoutMs;
            length = socket.Receive(into);

            // `if(received_sz <= 0)`: a datagram of nothing ends the thread in the C, and is Network here.
            return length > 0 ? ChiakiError.Success : ChiakiError.Network;
        }
        catch (SocketException e)
        {
            length = 0;
            return e.SocketErrorCode == SocketError.TimedOut ? ChiakiError.Timeout : ChiakiError.Network;
        }
    }

    /// <summary>Closes the socket, which the C does at the end of its thread where it owns one.</summary>
    public void Dispose() => socket.Dispose();
}
