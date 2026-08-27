using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>The five fields an INIT_ACK carries, before any of them is judged.</summary>
public readonly record struct TakionInitAck(
    uint Tag,
    uint ARwnd,
    ushort OutboundStreams,
    ushort InboundStreams,
    uint InitialSeqNum);

/// <summary>Why an INIT_ACK was refused, or that it was not.</summary>
public enum TakionInitAckVerdict
{
    /// <summary>Both gates passed.</summary>
    Accepted,

    /// <summary>The remote tag was zero, which is the first gate.</summary>
    ZeroTag,

    /// <summary>The stream counts did not fit, which is the second.</summary>
    StreamCountsRefused,
}

/// <summary>One send-then-wait exchange, run up to three times.</summary>
/// <param name="Attempts">How many attempts were made, including the one that ended it.</param>
/// <param name="Error">
/// What the exchange returned. A send failure returns the SEND's error; otherwise it is the last
/// receive's, which is the value the C's `err` still holds when the loop runs out.
/// </param>
public readonly record struct TakionExchange(int Attempts, ChiakiError Error);

/// <summary>
/// PP450: PP27's connect - the four messages that have to cross before takion is a transport.
///
/// INIT out, INIT_ACK back, COOKIE out, COOKIE_ACK back. PP449 covered the receive thread's timer and
/// PP27's first part the send buffer; this is what happens before either of them has anything to do,
/// and a port that got the retry rule wrong would fail to connect on a lossy link while looking
/// correct on a clean one.
///
/// A SEND FAILURE ABORTS; A LOST ACK RETRIES. Both loops are `for(tries &lt; 3)` around send-then-wait,
/// and the two failures are not symmetric: a send that fails returns its own error at once, and only
/// a receive that fails goes round again. Three attempts, 5000ms of patience each, and the error the
/// caller sees after all three is the LAST RECEIVE's - so a run of timeouts reports a timeout and not
/// a "gave up".
///
/// THE TAG IS BOTH INITIAL SEQUENCE NUMBERS. `tag_local` is a random 32-bit value and
/// `seq_num_local` is assigned FROM it, so the number the client starts counting at is its own tag.
/// The remote half is the same convention read the other way round: the data queue is seeded with
/// `tag_remote`, and the INIT_ACK's own `initial_seq_num` field is parsed off the wire and then
/// ignored - the C has it commented out at the assignment. So this is a protocol convention on both
/// sides rather than a field being dropped, and a port that used the wire value would agree with it
/// only for as long as the console kept setting the two equal.
///
/// AND THE HEADER TAG IS THE PEER'S, NOT ITS OWN. Every outbound message writes `tag_remote` into the
/// header - which is 0 for the INIT, because that is the message asking what it is - while
/// takion_parse_message refuses any INBOUND message whose tag is not `tag_local`. Two fields with one
/// name, in opposite directions.
///
/// THE COOKIE ACK HAD TWO COUPLED DEFECTS AND PP451 REPAIRED BOTH. It read the chunk type at offset
/// 0xd before checking the datagram's length, and then gave the second receive the first datagram's
/// length as its capacity. See <see cref="DatagramIsLongEnoughToRead"/> and
/// <see cref="SecondReceiveCapacity"/>: the assertions hold the repair, and they are written so that
/// the old shape coming back turns them red rather than reading as a rename.
/// </summary>
public static class TakionHandshake
{
    /// <summary>MAX_CONNECT_RESEND_TRIES.</summary>
    public const int MaxConnectResendTries = 3;

    /// <summary>TAKION_EXPECT_TIMEOUT_MS - how long one attempt waits for its ack.</summary>
    public const int ExpectTimeoutMs = 5000;

    /// <summary>TAKION_A_RWND, the receive window the INIT advertises.</summary>
    public const uint ARwnd = 0x19000;

    /// <summary>TAKION_OUTBOUND_STREAMS.</summary>
    public const ushort OutboundStreams = 0x64;

    /// <summary>TAKION_INBOUND_STREAMS.</summary>
    public const ushort InboundStreams = 0x64;

    /// <summary>TAKION_COOKIE_SIZE - the blob the INIT_ACK hands over and the COOKIE hands back.</summary>
    public const int CookieSize = 0x20;

    /// <summary>TAKION_MESSAGE_HEADER_SIZE.</summary>
    public const int MessageHeaderSize = 0x10;

    /// <summary>The exact length an INIT_ACK datagram must have: type byte, header, payload, cookie.</summary>
    public const int InitAckDatagramSize = 1 + MessageHeaderSize + 0x10 + CookieSize;

    /// <summary>The exact length a COOKIE_ACK datagram must have: type byte and header, no payload.</summary>
    public const int CookieAckDatagramSize = 1 + MessageHeaderSize;

    /// <summary>
    /// Where the cookie ack's second-init-ack test reads - the chunk type's offset inside the
    /// datagram, which is one past the header's own 0xc because of the leading packet-type byte.
    /// </summary>
    public const int ChunkTypeOffsetInDatagram = 0xd;

    /// <summary>
    /// The two gates on an INIT_ACK, in the order the C applies them.
    ///
    /// The order is the behaviour: an ack with a zero tag AND impossible stream counts is refused for
    /// the tag, and a port that checked the streams first would report the other reason. The C logs
    /// each separately, which is the only place that distinction is visible.
    /// </summary>
    public static TakionInitAckVerdict Check(TakionInitAck ack)
    {
        if (ack.Tag == 0)
            return TakionInitAckVerdict.ZeroTag;

        return StreamCountsAgree(ack.OutboundStreams, ack.InboundStreams)
            ? TakionInitAckVerdict.Accepted
            : TakionInitAckVerdict.StreamCountsRefused;
    }

    /// <summary>
    /// Whether the console's stream counts fit ours, which is CROSSED and asymmetric.
    ///
    /// The console's OUTBOUND is bounded above by our INBOUND, and its INBOUND bounded below by our
    /// OUTBOUND: it may not send on more streams than we will listen to, and it must be willing to
    /// listen to at least as many as we will send on. So one comparison is `&gt;` and the other `&lt;`,
    /// and neither compares like with like. Both constants are 0x64 today, which means an inverted
    /// port passes every real handshake and fails only against a console that answered with different
    /// numbers - the worst kind of agreement to rely on.
    ///
    /// Zero on either side is refused first, and separately: 0 would pass the lower bound if the
    /// bound were the only test.
    /// </summary>
    public static bool StreamCountsAgree(ushort outbound, ushort inbound)
    {
        if (outbound == 0 || inbound == 0)
            return false;

        return outbound <= InboundStreams && inbound >= OutboundStreams;
    }

    /// <summary>
    /// The sequence number the data queue is seeded with: the remote TAG, not the INIT_ACK's
    /// initial_seq_num.
    /// </summary>
    /// <param name="ack">
    /// Taken whole, and deliberately: the parameter it does not read is the point. `initial_seq_num`
    /// is parsed off the wire at payload offset 0xc and used by nothing.
    /// </param>
    public static uint RemoteInitialSeqNum(TakionInitAck ack) => ack.Tag;

    /// <summary>
    /// The local side of the same convention: the first sequence number the client sends from is its
    /// own tag.
    /// </summary>
    public static uint LocalInitialSeqNum(uint tagLocal) => tagLocal;

    /// <summary>
    /// The INIT the client advertises, built from the constants and its own tag.
    ///
    /// Both tag fields are `tag_local` - the payload's because that is who is asking, and
    /// initial_seq_num's because of the convention above. The HEADER's tag is not this: see the
    /// type's note.
    /// </summary>
    public static TakionInitAck Init(uint tagLocal)
        => new(tagLocal, ARwnd, OutboundStreams, InboundStreams, LocalInitialSeqNum(tagLocal));

    /// <summary>
    /// The tag an OUTBOUND message's header carries, which is the peer's and is 0 until the INIT_ACK
    /// says otherwise.
    /// </summary>
    public static uint OutboundHeaderTag(uint tagRemote) => tagRemote;

    /// <summary>Whether an INBOUND message's header tag is acceptable: it must be OUR tag.</summary>
    public static bool InboundHeaderTagAccepted(uint headerTag, uint tagLocal) => headerTag == tagLocal;

    /// <summary>
    /// One of the handshake's two loops: send, wait for the ack, up to three times.
    /// </summary>
    /// <param name="attempt">
    /// What one attempt costs, by zero-based attempt number. The receive result is read only where
    /// the send succeeded, which is what makes the two failures asymmetric.
    /// </param>
    public static TakionExchange Exchange(Func<int, (ChiakiError Send, ChiakiError Receive)> attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        // The C's `err` is uninitialised before the loop, so a MAX_CONNECT_RESEND_TRIES of 0 would
        // test a garbage value. Unreachable at 3, and asserted against the define rather than assumed.
        ChiakiError err = ChiakiError.Success;

        for (var tries = 0; tries < MaxConnectResendTries; tries++)
        {
            (ChiakiError send, ChiakiError receive) = attempt(tries);

            if (send != ChiakiError.Success)
                return new TakionExchange(tries + 1, send);

            err = receive;
            if (err == ChiakiError.Success)
                return new TakionExchange(tries + 1, ChiakiError.Success);
        }

        // Whatever the LAST receive said. Not a "gave up" error of its own.
        return new TakionExchange(MaxConnectResendTries, err);
    }

    /// <summary>
    /// Whether a datagram of this length is read at all, or refused before any byte of it is
    /// inspected.
    ///
    /// PP451 FIXED THIS, and the shape of the fix is what the assertion holds: the length check now
    /// runs first, so a short datagram is refused rather than deciding the second-init-ack branch on
    /// whatever was last on the stack. The old order read `message[0xd]` first, and 0xd is inside a
    /// 17-byte array, so the read was uninitialised without ever being out of bounds - which is why
    /// no sanitiser pointed at it in the years it stood.
    /// </summary>
    public static bool DatagramIsLongEnoughToRead(int receivedSize)
        => receivedSize >= CookieAckDatagramSize;

    /// <summary>
    /// The capacity the SECOND receive is given: the buffer's own, not the first datagram's length.
    ///
    /// PP451 FIXED THIS TOO, and it was the half that lost handshakes. takion_recv's size argument is
    /// in-out - the capacity going in, the received length coming back - and the old code passed the
    /// same variable to both calls. After a short first datagram the second receive asked the socket
    /// for that many bytes, and UDP threw the rest away: a cookie ack that had arrived intact was
    /// truncated, failed the length check, and cost one of the three cookie attempts.
    ///
    /// Both halves needed the same trigger, and it was exactly the datagram the reordered check now
    /// refuses.
    ///
    /// It takes no argument, and that absence is the fix: the first datagram's length used to be the
    /// answer.
    /// </summary>
    public static int SecondReceiveCapacity => CookieAckDatagramSize;

    /// <summary>takion.c, where all of this lives.</summary>
    public const string RelativePath = @"lib\src\takion.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The handshake's body.</summary>
    public static string? HandshakeBody(string source)
        => CFunction.Body(source, "static ChiakiErrorCode takion_handshake");

    /// <summary>The cookie ack receive's body, which holds both defects.</summary>
    public static string? CookieAckBody(string source)
        => CFunction.Body(source, "static ChiakiErrorCode takion_recv_message_cookie_ack");

    /// <summary>The init ack receive's body.</summary>
    public static string? InitAckBody(string source)
        => CFunction.Body(source, "static ChiakiErrorCode takion_recv_message_init_ack");

    /// <summary>A `#define NAME value` in the file, or null. Decimal or 0x, as the file writes it.</summary>
    public static long? DefineIn(string source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return CDefine.Value(source, name);
    }

    /// <summary>
    /// Whether a send failure still returns rather than retrying, in both loops.
    ///
    /// The predicate is the RETURN inside the loop: a `continue` there, or no test at all, would make
    /// the two failures symmetric and change how many packets a dead socket costs.
    /// </summary>
    public static bool ASendFailureStillAborts(string handshakeBody)
    {
        ArgumentNullException.ThrowIfNull(handshakeBody);

        // Each loop logs its own send failure and returns; the two after the loops are the receive
        // failures. Four `return err;` in all, and a `continue` in place of any of the first two
        // would make a dead socket cost three packets instead of one.
        return handshakeBody.Contains("Takion failed to send init", StringComparison.Ordinal)
            && handshakeBody.Contains("Takion failed to send cookie", StringComparison.Ordinal)
            && CountOf(handshakeBody, "return err;") == 4
            && !handshakeBody.Contains("continue;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether both loops still break on a received ack rather than running to the end, which is what
    /// makes three the maximum and not the count.
    /// </summary>
    public static bool AReceivedAckStillEndsTheLoop(string handshakeBody)
    {
        ArgumentNullException.ThrowIfNull(handshakeBody);

        return CountOf(handshakeBody, "if(err == CHIAKI_ERR_SUCCESS)") == 2
            && CountOf(handshakeBody, "break;") == 2;
    }

    /// <summary>Whether both loops are still bounded by MAX_CONNECT_RESEND_TRIES.</summary>
    public static bool BothLoopsAreStillBounded(string handshakeBody)
    {
        ArgumentNullException.ThrowIfNull(handshakeBody);

        return CountOf(handshakeBody, "tries < MAX_CONNECT_RESEND_TRIES") == 2;
    }

    /// <summary>
    /// Whether the remote initial sequence number is still the tag, with the wire's own field still
    /// commented out beside it.
    ///
    /// Both halves matter. The assignment alone could be a coincidence; the commented-out alternative
    /// on the same line is what says it was a choice.
    /// </summary>
    public static bool TheRemoteSeqNumIsStillTheTag(string handshakeBody)
    {
        ArgumentNullException.ThrowIfNull(handshakeBody);

        return handshakeBody.Contains(
            "*seq_num_remote_initial = takion->tag_remote; //init_ack_payload.initial_seq_num;",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the zero-tag gate still comes before the stream counts.</summary>
    public static bool TheZeroTagGateIsStillFirst(string handshakeBody)
    {
        ArgumentNullException.ThrowIfNull(handshakeBody);

        int tag = handshakeBody.IndexOf("init_ack_payload.tag == 0", StringComparison.Ordinal);
        int streams = handshakeBody.IndexOf(
            "init_ack_payload.outbound_streams == 0", StringComparison.Ordinal);

        return tag >= 0 && streams > tag;
    }

    /// <summary>
    /// Whether the crossed stream comparison is still written the way <see cref="StreamCountsAgree"/>
    /// models it.
    /// </summary>
    public static bool TheStreamCheckIsStillCrossed(string handshakeBody)
    {
        ArgumentNullException.ThrowIfNull(handshakeBody);

        return handshakeBody.Contains(
                "init_ack_payload.outbound_streams > TAKION_INBOUND_STREAMS", StringComparison.Ordinal)
            && handshakeBody.Contains(
                "init_ack_payload.inbound_streams < TAKION_OUTBOUND_STREAMS", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the cookie ack still reads no byte of a datagram it has not length-checked - PP451's
    /// first half.
    ///
    /// The predicate is that the body reads `message[0xd]` and does NOT length-check inside itself:
    /// both receives go through <c>takion_recv_cookie_ack_datagram</c>, which refuses a short
    /// datagram before returning. A `received_size` reappearing here is the old shape coming back.
    /// </summary>
    public static bool NoByteIsReadBeforeTheLengthCheck(string cookieAckBody)
    {
        ArgumentNullException.ThrowIfNull(cookieAckBody);

        return cookieAckBody.Contains(
                "message[0xd] == TAKION_CHUNK_TYPE_INIT_ACK", StringComparison.Ordinal)
            && !cookieAckBody.Contains("received_size", StringComparison.Ordinal)
            && CountOf(cookieAckBody, "takion_recv_cookie_ack_datagram(takion, message, sizeof(message))")
                == 2;
    }

    /// <summary>
    /// Whether the helper still takes its capacity from its own parameter rather than from a length a
    /// previous receive wrote back - PP451's second half.
    ///
    /// `received_size` is assigned from `message_size`, which is the CALLER's sizeof. Both call sites
    /// pass it fresh, so neither receive can be narrowed by the other.
    /// </summary>
    public static bool EachReceiveGetsTheBuffersCapacity(string helperBody)
    {
        ArgumentNullException.ThrowIfNull(helperBody);

        return helperBody.Contains("size_t received_size = message_size;", StringComparison.Ordinal)
            && helperBody.Contains("received_size < message_size", StringComparison.Ordinal);
    }

    /// <summary>The helper PP451 introduced, or null where it is gone.</summary>
    public static string? CookieAckDatagramBody(string source)
        => CFunction.Body(source, "static ChiakiErrorCode takion_recv_cookie_ack_datagram");

    /// <summary>
    /// Whether the init ack receive still demands the exact datagram length, which is the check the
    /// cookie ack makes too late.
    /// </summary>
    public static bool TheInitAckChecksItsLengthFirst(string initAckBody)
    {
        ArgumentNullException.ThrowIfNull(initAckBody);

        int size = initAckBody.IndexOf("received_size < sizeof(message)", StringComparison.Ordinal);
        int read = initAckBody.IndexOf("message[0]", StringComparison.Ordinal);

        return size >= 0 && read > size;
    }

    private static int CountOf(string haystack, string needle)
    {
        var found = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }
}
