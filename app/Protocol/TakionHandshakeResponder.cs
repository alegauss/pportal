namespace ChiakiNg.Protocol;

/// <summary>Where a responder is in the three-step exchange.</summary>
public enum TakionResponderState
{
    /// <summary>Nothing has arrived; the next INIT is answered with an INIT_ACK.</summary>
    AwaitingInit,

    /// <summary>The ack has gone out; the cookie coming back completes the handshake.</summary>
    AwaitingCookie,

    /// <summary>The COOKIE_ACK has gone out and the client's loop is running.</summary>
    Done,
}

/// <summary>
/// PP606, under PP27: the three pieces made into a participant.
///
/// PP605 reads the two datagrams the client sends and PP603 and PP604 write the two it expects
/// back. That is a handshake in parts. This is the part that has a state: which answer is owed
/// next, what tag was learnt, and which cookie has to come back.
///
/// IT ANSWERS A REPEATED INIT WITH A REPEATED ACK, which is not politeness. takion retries its INIT
/// up to MAX_CONNECT_RESEND_TRIES when no ack arrives in time, and
/// takion_recv_message_cookie_ack tolerates a second INIT_ACK arriving where it expects the cookie
/// ack - it reads one more datagram and carries on. So a responder that answered the retry with
/// silence, or with a cookie ack, would turn a slow first answer into a failed connect.
///
/// THE COOKIE IS ITS OWN AND FIXED FOR THE EXCHANGE. takion echoes whatever thirty-two bytes it was
/// given, so what comes back is checked against what went out rather than accepted for its shape -
/// PP605's reason, and this is where the value it compares against lives.
///
/// NO SOCKET HERE. This decides what to send; something else sends it. That split is what lets the
/// whole exchange be tested without a socket at all, and it is the same shape PP479 used for the
/// holepunch sequences - steps behind an interface, the live one supplied separately.
/// </summary>
public sealed class TakionHandshakeResponder
{
    private readonly uint tag;
    private readonly byte[] cookie;
    private byte[]? lastAck;

    /// <param name="tag">The tag this peer chooses for itself, which the client will address.</param>
    /// <param name="cookie">The thirty-two bytes the client has to echo.</param>
    public TakionHandshakeResponder(uint tag, byte[] cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);

        if (cookie.Length != TakionHandshake.CookieSize)
        {
            throw new ArgumentException(
                $"a cookie is {TakionHandshake.CookieSize} bytes and this one is {cookie.Length}",
                nameof(cookie));
        }

        this.tag = tag;
        this.cookie = cookie;
    }

    /// <summary>Which answer is owed next.</summary>
    public TakionResponderState State { get; private set; } = TakionResponderState.AwaitingInit;

    /// <summary>The client's tag, once its INIT has arrived. Zero before that.</summary>
    public uint ClientTag { get; private set; }

    /// <summary>How many INITs arrived, which is one plus the retries the client had to make.</summary>
    public int InitsSeen { get; private set; }

    /// <summary>
    /// What to send back, or null where the datagram is not one this answers.
    ///
    /// Null rather than an exception for an unknown datagram: a peer on a real socket receives
    /// whatever the network hands it, and a harness that threw on the first stray packet would be
    /// reporting on the network rather than on takion.
    /// </summary>
    public byte[]? Answer(ReadOnlySpan<byte> datagram)
    {
        TakionInbound read = TakionHandshakeIntake.Read(datagram);

        switch (read.Kind)
        {
            case TakionInboundKind.Init when State != TakionResponderState.Done:
                InitsSeen++;
                ClientTag = read.Init!.Value.Tag;
                State = TakionResponderState.AwaitingCookie;

                // Rebuilt rather than cached across a changed tag: a retry carries the same one, but
                // a second client on the same socket would not, and answering it with the first
                // one's header is a bug that only appears with two.
                lastAck = TakionInitAckDatagram.Write(
                    ClientTag,
                    new TakionInitAck(
                        Tag: tag,
                        ARwnd: TakionHandshake.ARwnd,
                        OutboundStreams: TakionHandshake.OutboundStreams,
                        InboundStreams: TakionHandshake.InboundStreams,
                        InitialSeqNum: tag),
                    cookie);

                return lastAck;

            case TakionInboundKind.Cookie when State == TakionResponderState.AwaitingCookie:
                if (!TakionHandshakeIntake.CookieEchoesTheOneSent(read.Cookie, cookie))
                    return null;

                if (read.HeaderTag != tag)
                    return null;

                State = TakionResponderState.Done;
                return TakionMessageHeader.WriteCookieAck(ClientTag);

            default:
                return null;
        }
    }

    /// <summary>
    /// The ack that went out, for a caller that wants to resend it without a datagram arriving.
    ///
    /// Kept because the client's retry is on ITS timer: a responder whose first ack was dropped
    /// hears nothing until the next INIT, and a harness measuring the loop wants to know that
    /// happened rather than to see the connect fail.
    /// </summary>
    public byte[]? LastInitAck => lastAck;
}
