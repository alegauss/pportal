using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP672, under PP27: the client side of the handshake - the two readers, the state machine, and the
/// exchange run over loopback against PP606's responder, which PP607 proved the C accepts.
///
/// The readers are held to the C's refusals in the C's order; the state machine runs the whole
/// exchange with no socket, the way the responder's tests do; and the loopback cases put a real UDP
/// socket under it, including the one winsock fact that decides which of the C's branches this port
/// can reach at all.
/// </summary>
public class TakionHandshakeClientTests
{
    private const uint OurTag = 0x11223344;
    private const uint PeerTag = 0x55667788;

    private static readonly byte[] Cookie =
        [.. Enumerable.Range(0, TakionHandshake.CookieSize).Select(i => (byte)(0x5A + i))];

    /// <summary>An INIT_ACK the responder would send us, with the fields a real one carries.</summary>
    private static byte[] GoodAck(uint tagLocal = OurTag, uint peer = PeerTag)
        => TakionInitAckDatagram.Write(
            tagLocal,
            new TakionInitAck(
                Tag: peer,
                ARwnd: TakionHandshake.ARwnd,
                OutboundStreams: TakionHandshake.OutboundStreams,
                InboundStreams: TakionHandshake.InboundStreams,
                InitialSeqNum: peer),
            Cookie);

    /// <summary>The INIT_ACK reads back what the responder wrote: the five fields and the cookie.</summary>
    [Fact]
    public void AnInitAckReadsBackWhatTheResponderWrote()
    {
        ChiakiError err = TakionInitAckDatagram.Read(GoodAck(), OurTag, out TakionInitAckReading reading);

        Assert.Equal(ChiakiError.Success, err);
        Assert.Equal(PeerTag, reading.Ack.Tag);
        Assert.Equal(TakionHandshake.ARwnd, reading.Ack.ARwnd);
        Assert.Equal(TakionHandshake.OutboundStreams, reading.Ack.OutboundStreams);
        Assert.Equal(TakionHandshake.InboundStreams, reading.Ack.InboundStreams);
        Assert.Equal(PeerTag, reading.Ack.InitialSeqNum);
        Assert.Equal(Cookie, reading.Cookie);
    }

    /// <summary>
    /// THE FIVE REFUSALS, each the C's INVALID_RESPONSE: short, not control, a foreign tag, the wrong
    /// chunk or a flag, and a length field that lies.
    /// </summary>
    [Fact]
    public void TheInitAckRefusalsAreTheCs()
    {
        Assert.Equal(
            ChiakiError.InvalidResponse,
            TakionInitAckDatagram.Read(GoodAck().AsSpan(0, TakionHandshake.InitAckDatagramSize - 1), OurTag, out _));

        byte[] notControl = GoodAck();
        notControl[0] = 2;
        Assert.Equal(ChiakiError.InvalidResponse, TakionInitAckDatagram.Read(notControl, OurTag, out _));

        // The header carries OUR tag or the parse refuses it - which is the 32 bits an off-path
        // sender has to guess (PP369).
        Assert.Equal(ChiakiError.InvalidResponse, TakionInitAckDatagram.Read(GoodAck(), OurTag + 1, out _));

        byte[] wrongChunk = GoodAck();
        wrongChunk[TakionHandshake.ChunkTypeOffsetInDatagram] = TakionMessageHeader.CookieAckChunkType;
        Assert.Equal(ChiakiError.InvalidResponse, TakionInitAckDatagram.Read(wrongChunk, OurTag, out _));

        byte[] flagged = GoodAck();
        flagged[TakionHandshake.ChunkTypeOffsetInDatagram + 1] = 1;
        Assert.Equal(ChiakiError.InvalidResponse, TakionInitAckDatagram.Read(flagged, OurTag, out _));

        byte[] lying = GoodAck();
        BinaryPrimitives.WriteUInt16BigEndian(
            lying.AsSpan(TakionInitAckDatagram.HeaderOffset + TakionMessageHeader.SizeFieldOffset),
            (ushort)(TakionInitAckDatagram.PayloadSize + TakionInitAckDatagram.SizeFieldAddend - 1));
        Assert.Equal(ChiakiError.InvalidResponse, TakionInitAckDatagram.Read(lying, OurTag, out _));
    }

    /// <summary>
    /// Handed more than the ack, the reader takes the first sixty-five bytes - what a truncating recv
    /// would have delivered. On winsock the receive refuses the long datagram before this runs, and
    /// <see cref="WinsockRefusesADatagramLongerThanTheReceive"/> holds that half.
    /// </summary>
    [Fact]
    public void ALongerDatagramIsReadToTheAcksLength()
    {
        byte[] longer = [.. GoodAck(), 0xFF];

        Assert.Equal(ChiakiError.Success, TakionInitAckDatagram.Read(longer, OurTag, out TakionInitAckReading reading));
        Assert.Equal(Cookie, reading.Cookie);
    }

    /// <summary>The COOKIE_ACK is accepted under our tag and under no other.</summary>
    [Fact]
    public void TheCookieAckIsAcceptedUnderOurTagOnly()
    {
        byte[] ack = TakionMessageHeader.WriteCookieAck(OurTag);

        Assert.Equal(TakionCookieAckVerdict.Accepted, TakionMessageHeader.ReadCookieAck(ack, OurTag));
        Assert.Equal(TakionCookieAckVerdict.Refused, TakionMessageHeader.ReadCookieAck(ack, PeerTag));
    }

    /// <summary>
    /// A LATE INIT_ACK ASKS FOR ANOTHER READ, and the datagram read in its place is judged without
    /// that test - so a second INIT_ACK in a row is refused for its chunk, as the C refuses it.
    /// </summary>
    [Fact]
    public void ALateInitAckAsksForAnotherRead()
    {
        Assert.Equal(TakionCookieAckVerdict.ReadAnother, TakionMessageHeader.ReadCookieAck(GoodAck(), OurTag));
        Assert.Equal(
            TakionCookieAckVerdict.Refused,
            TakionMessageHeader.ReadCookieAck(GoodAck(), OurTag, secondRead: true));
    }

    /// <summary>
    /// THE LATE-ACK TEST COMES BEFORE THE TYPE BYTE, which is the C's order and not a tidy one: a
    /// datagram of the ack's size that is no control packet still asks for another read if it carries
    /// INIT_ACK at 0xd. Reproduced, because a port that checked the type first would cost a different
    /// number of receives on the same bytes.
    /// </summary>
    [Fact]
    public void TheLateAckTestComesBeforeTheTypeByte()
    {
        byte[] oddTypeLateAck = new byte[TakionHandshake.CookieAckDatagramSize];
        oddTypeLateAck[0] = 7;
        oddTypeLateAck[TakionHandshake.ChunkTypeOffsetInDatagram] = TakionMessageHeader.InitAckChunkType;

        Assert.Equal(TakionCookieAckVerdict.ReadAnother, TakionMessageHeader.ReadCookieAck(oddTypeLateAck, OurTag));

        byte[] oddTypeCookieAck = TakionMessageHeader.WriteCookieAck(OurTag);
        oddTypeCookieAck[0] = 7;

        Assert.Equal(TakionCookieAckVerdict.Refused, TakionMessageHeader.ReadCookieAck(oddTypeCookieAck, OurTag));
    }

    /// <summary>
    /// PP451's first repair, kept: a datagram shorter than the ack is refused before the byte at 0xd is
    /// looked at, so a short one carrying INIT_ACK there does not ask for another read.
    /// </summary>
    [Fact]
    public void AShortCookieAckIsRefusedBeforeAnyByteIsRead()
    {
        byte[] shortLateAck = new byte[TakionHandshake.CookieAckDatagramSize - 1];
        shortLateAck[TakionHandshake.ChunkTypeOffsetInDatagram] = TakionMessageHeader.InitAckChunkType;

        Assert.Equal(TakionCookieAckVerdict.Refused, TakionMessageHeader.ReadCookieAck(shortLateAck, OurTag));
        Assert.Equal(TakionCookieAckVerdict.Refused, TakionMessageHeader.ReadCookieAck([], OurTag));
    }

    /// <summary>
    /// THE WHOLE EXCHANGE WITH NO SOCKET: the client's datagrams into the responder, the responder's
    /// back into the client, and both end where the C ends - connected here, Done there.
    /// </summary>
    [Fact]
    public void TheExchangeRunsToConnectedWithoutASocket()
    {
        var client = new TakionHandshakeClient(OurTag);
        var responder = new TakionHandshakeResponder(PeerTag, Cookie);

        Assert.Equal(TakionClientState.AwaitingInitAck, client.State);
        Assert.Equal(0u, client.TagRemote);

        byte[] initAck = Assert.IsType<byte[]>(responder.Answer(client.Init()));
        Assert.Equal(ChiakiError.Success, client.TakeInitAck(initAck));
        Assert.Equal(TakionInitAckVerdict.Accepted, client.Gate());

        Assert.Equal(TakionClientState.AwaitingCookieAck, client.State);
        Assert.Equal(PeerTag, client.TagRemote);
        Assert.Equal(PeerTag, client.RemoteInitialSeqNum);

        byte[] cookieAck = Assert.IsType<byte[]>(responder.Answer(client.CookieMessage()));
        Assert.Equal(TakionCookieAckVerdict.Accepted, client.TakeCookieAck(cookieAck));

        Assert.Equal(TakionClientState.Connected, client.State);
        Assert.Equal(TakionResponderState.Done, responder.State);
    }

    /// <summary>
    /// A ZERO TAG IS REFUSED AT THE GATE, NOT AT THE READ. The datagram is well-formed, so the C's
    /// loop breaks on it; the gate then returns without naming a peer and without a retry.
    /// </summary>
    [Fact]
    public void AZeroTagIsRefusedAtTheGateAndNamesNoPeer()
    {
        var client = new TakionHandshakeClient(OurTag);

        Assert.Equal(ChiakiError.Success, client.TakeInitAck(GoodAck(peer: 0)));
        Assert.Equal(TakionInitAckVerdict.ZeroTag, client.Gate());

        Assert.Equal(TakionClientState.Refused, client.State);
        Assert.Equal(0u, client.TagRemote);
        Assert.Equal(0u, client.RemoteInitialSeqNum);
    }

    /// <summary>
    /// Bad stream counts are the gate's OTHER refusal, and by then the C has already assigned
    /// tag_remote - kept, because the C keeps it, though nothing reads it after a refusal.
    /// </summary>
    [Fact]
    public void BadStreamCountsAreRefusedAfterThePeerIsNamed()
    {
        byte[] ack = TakionInitAckDatagram.Write(
            OurTag,
            new TakionInitAck(PeerTag, TakionHandshake.ARwnd, OutboundStreams: 0x65,
                TakionHandshake.InboundStreams, PeerTag),
            Cookie);

        var client = new TakionHandshakeClient(OurTag);

        Assert.Equal(ChiakiError.Success, client.TakeInitAck(ack));
        Assert.Equal(TakionInitAckVerdict.StreamCountsRefused, client.Gate());
        Assert.Equal(TakionClientState.Refused, client.State);
        Assert.Equal(PeerTag, client.TagRemote);
        Assert.Equal(0u, client.RemoteInitialSeqNum);
    }

    /// <summary>Gating with no ack read, or echoing with none accepted, is a caller's mistake and says so.</summary>
    [Fact]
    public void TheStepsRefuseToRunOutOfOrder()
    {
        var client = new TakionHandshakeClient(OurTag);

        Assert.Throws<InvalidOperationException>(() => client.Gate());
        Assert.Throws<InvalidOperationException>(() => client.CookieMessage());
    }

    /// <summary>A wire that scripts what each receive produces, and records what was sent.</summary>
    private sealed class ScriptedWire(
        ChiakiError sendResult,
        Queue<Func<byte[]>> receives) : ITakionHandshakeWire
    {
        public List<byte[]> Sent { get; } = [];

        public ChiakiError Send(ReadOnlySpan<byte> datagram)
        {
            Sent.Add(datagram.ToArray());
            return sendResult;
        }

        public ChiakiError Receive(Span<byte> into, int timeoutMs, out int length)
        {
            length = 0;
            if (receives.Count == 0)
                return ChiakiError.Timeout;

            byte[] datagram = receives.Dequeue()();
            if (datagram.Length == 0)
                return ChiakiError.Timeout;

            if (datagram.Length > into.Length)
                return ChiakiError.Network;

            datagram.CopyTo(into);
            length = datagram.Length;
            return ChiakiError.Success;
        }
    }

    /// <summary>A SEND FAILURE ABORTS AT ONCE, with the send's error and one attempt - no retry, no receive.</summary>
    [Fact]
    public void ASendFailureAbortsTheRunAtOnce()
    {
        var wire = new ScriptedWire(ChiakiError.Network, new Queue<Func<byte[]>>());
        var client = new TakionHandshakeClient(OurTag);

        TakionHandshakeOutcome outcome = client.Run(wire, expectTimeoutMs: 10);

        Assert.Equal(new TakionHandshakeOutcome(ChiakiError.Network, 1, 0), outcome);
        Assert.Single(wire.Sent);
        Assert.Equal(TakionClientState.AwaitingInitAck, client.State);
    }

    /// <summary>Three silent attempts report a timeout, and three INITs went out to earn it.</summary>
    [Fact]
    public void ThreeTimeoutsReportATimeout()
    {
        var wire = new ScriptedWire(ChiakiError.Success, new Queue<Func<byte[]>>());
        var client = new TakionHandshakeClient(OurTag);

        TakionHandshakeOutcome outcome = client.Run(wire, expectTimeoutMs: 10);

        Assert.Equal(new TakionHandshakeOutcome(ChiakiError.Timeout, TakionHandshake.MaxConnectResendTries, 0), outcome);
        Assert.Equal(TakionHandshake.MaxConnectResendTries, wire.Sent.Count);
        Assert.All(wire.Sent, sent => Assert.Equal(TakionClientDatagrams.WriteInit(OurTag), sent));
    }

    /// <summary>
    /// A MALFORMED ACK COSTS A RETRY AND A GATE REFUSAL DOES NOT. The first ack lies about its length,
    /// so the loop goes round and a second INIT goes out; the second is good, and the exchange
    /// completes through the cookie. Then, separately, a zero-tag ack: one attempt, no cookie sent.
    /// </summary>
    [Fact]
    public void AMalformedAckIsRetriedAndAGateRefusalIsNot()
    {
        byte[] lying = GoodAck();
        lying[TakionInitAckDatagram.HeaderOffset + TakionMessageHeader.SizeFieldOffset + 1] ^= 0x01;

        var responder = new TakionHandshakeResponder(PeerTag, Cookie);
        var client = new TakionHandshakeClient(OurTag);
        var receives = new Queue<Func<byte[]>>();
        receives.Enqueue(() => lying);
        receives.Enqueue(() => GoodAck());
        receives.Enqueue(() => TakionMessageHeader.WriteCookieAck(OurTag));
        var wire = new ScriptedWire(ChiakiError.Success, receives);

        TakionHandshakeOutcome outcome = client.Run(wire, expectTimeoutMs: 10);

        Assert.Equal(new TakionHandshakeOutcome(ChiakiError.Success, 2, 1), outcome);
        Assert.Equal(TakionClientState.Connected, client.State);
        Assert.Equal(3, wire.Sent.Count);
        Assert.Equal(TakionClientDatagrams.WriteCookie(PeerTag, Cookie), wire.Sent[2]);

        // And the responder would have accepted that cookie, so the two sides agree on the echo.
        Assert.NotNull(responder.Answer(client.Init()));
        Assert.NotNull(responder.Answer(wire.Sent[2]));

        var refusing = new Queue<Func<byte[]>>();
        refusing.Enqueue(() => GoodAck(peer: 0));
        var refused = new ScriptedWire(ChiakiError.Success, refusing);
        var second = new TakionHandshakeClient(OurTag);

        Assert.Equal(
            new TakionHandshakeOutcome(ChiakiError.InvalidResponse, 1, 0),
            second.Run(refused, expectTimeoutMs: 10));
        Assert.Equal(TakionClientState.Refused, second.State);
        Assert.Single(refused.Sent);
    }

    /// <summary>
    /// The responder, pumped on a socket of its own until it is Done or told to stop - the console's
    /// side of the loopback, which PP606 wrote without a socket on purpose.
    /// </summary>
    private static Task PumpResponder(
        UdpClient peer, TakionHandshakeResponder responder, CancellationToken stop,
        Func<byte[], bool>? drop = null)
        => Task.Run(() =>
        {
            var from = new IPEndPoint(IPAddress.Any, 0);

            while (!stop.IsCancellationRequested && responder.State != TakionResponderState.Done)
            {
                byte[] arrived;

                try
                {
                    arrived = peer.Receive(ref from);
                }
                catch (SocketException)
                {
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (drop is not null && drop(arrived))
                    continue;

                if (responder.Answer(arrived) is { } reply)
                    peer.Send(reply, reply.Length, from);
            }
        });

    /// <summary>
    /// THE RUN, OVER LOOPBACK: a managed client on a connected UDP socket completes the handshake
    /// against PP606's responder - the peer PP607 showed the real C accepts - in one attempt each way.
    ///
    /// Bounded like PP607's: the responder's socket has a receive timeout so its pump notices the stop,
    /// and the client's attempts are the C's, so a responder that stopped answering fails this test
    /// in fifteen seconds rather than hanging the suite.
    /// </summary>
    [Fact]
    public async Task TheClientCompletesTheHandshakeAgainstTheResponderOverLoopback()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        peer.Client.ReceiveTimeout = 500;
        var responder = new TakionHandshakeResponder(PeerTag, Cookie);

        using var stop = new CancellationTokenSource();
        Task pump = PumpResponder(peer, responder, stop.Token);

        using var wire = TakionUdpWire.Connect((IPEndPoint)peer.Client.LocalEndPoint!);
        var client = new TakionHandshakeClient(OurTag);

        TakionHandshakeOutcome outcome;
        try
        {
            outcome = client.Run(wire);
        }
        finally
        {
            await stop.CancelAsync();
            await pump.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(new TakionHandshakeOutcome(ChiakiError.Success, 1, 1), outcome);
        Assert.Equal(TakionClientState.Connected, client.State);
        Assert.Equal(PeerTag, client.TagRemote);
        Assert.Equal(PeerTag, client.RemoteInitialSeqNum);
        Assert.Equal(TakionResponderState.Done, responder.State);
        Assert.Equal(OurTag, responder.ClientTag);
        Assert.Equal(1, responder.InitsSeen);
    }

    /// <summary>
    /// A DROPPED INIT IS RETRIED, and the retry is what connects. The attempt's wait is shortened from
    /// the C's five seconds to keep the suite honest about its time; the count of attempts is the
    /// C's rule, and the responder saw exactly one INIT because the first never reached it.
    /// </summary>
    [Fact]
    public async Task ADroppedInitIsRetried()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        peer.Client.ReceiveTimeout = 500;
        var responder = new TakionHandshakeResponder(PeerTag, Cookie);

        // Counts INITs that REACHED the peer's socket; the first is the one dropped, so the count
        // ending at two is the retry having arrived rather than the client having given up.
        int initsArrived = 0;
        using var stop = new CancellationTokenSource();
        Task pump = PumpResponder(peer, responder, stop.Token, drop: arrived =>
            TakionHandshakeIntake.Read(arrived).Kind == TakionInboundKind.Init
            && Interlocked.Increment(ref initsArrived) == 1);

        using var wire = TakionUdpWire.Connect((IPEndPoint)peer.Client.LocalEndPoint!);
        var client = new TakionHandshakeClient(OurTag);

        TakionHandshakeOutcome outcome;
        try
        {
            outcome = client.Run(wire, expectTimeoutMs: 300);
        }
        finally
        {
            await stop.CancelAsync();
            await pump.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(new TakionHandshakeOutcome(ChiakiError.Success, 2, 1), outcome);
        Assert.Equal(TakionClientState.Connected, client.State);

        // Two INITs crossed the socket and the responder answered the second: the retry is what
        // connected, which is the difference between this and the case above.
        Assert.Equal(2, initsArrived);
        Assert.Equal(1, responder.InitsSeen);
    }

    /// <summary>
    /// THE WINSOCK FACT: a datagram longer than the receive's buffer is refused, not truncated, and
    /// the wire reports it as the C's takion_recv does - a network error. This is what makes the C's
    /// late-INIT_ACK branch unreachable on Windows: the sixty-five-byte ack never gets as far as the
    /// byte at 0xd, and the retry is what covers it.
    /// </summary>
    [Fact]
    public void WinsockRefusesADatagramLongerThanTheReceive()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var wire = TakionUdpWire.Connect((IPEndPoint)peer.Client.LocalEndPoint!);

        byte[] lateAck = GoodAck();
        peer.Send(lateAck, lateAck.Length, wire.LocalEndPoint);

        byte[] cookieAckBuffer = new byte[TakionHandshake.SecondReceiveCapacity];
        ChiakiError err = wire.Receive(cookieAckBuffer, timeoutMs: 2000, out int length);

        Assert.Equal(ChiakiError.Network, err);
        Assert.Equal(0, length);

        // And a datagram that fits is delivered whole, with its length.
        byte[] cookieAck = TakionMessageHeader.WriteCookieAck(OurTag);
        peer.Send(cookieAck, cookieAck.Length, wire.LocalEndPoint);

        Assert.Equal(ChiakiError.Success, wire.Receive(cookieAckBuffer, timeoutMs: 2000, out length));
        Assert.Equal(cookieAck.Length, length);
        Assert.Equal(TakionCookieAckVerdict.Accepted, TakionMessageHeader.ReadCookieAck(cookieAckBuffer, OurTag));
    }

    /// <summary>Nothing arriving inside the timeout is Timeout, silent, and not a fault.</summary>
    [Fact]
    public void AQuietSocketIsATimeout()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var wire = TakionUdpWire.Connect((IPEndPoint)peer.Client.LocalEndPoint!);

        Assert.Equal(
            ChiakiError.Timeout,
            wire.Receive(new byte[TakionHandshake.CookieAckDatagramSize], timeoutMs: 50, out int length));
        Assert.Equal(0, length);
    }

    /// <summary>The socket is made the way the C makes its own: the receive buffer is the advertised window.</summary>
    [Fact]
    public void TheSocketsReceiveBufferIsTheAdvertisedWindow()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var wire = TakionUdpWire.Connect((IPEndPoint)peer.Client.LocalEndPoint!);

        Assert.Equal(TakionHandshake.ARwnd, TakionSocket.ReceiveBufferIs);
        Assert.Equal(IPAddress.Loopback, wire.LocalEndPoint.Address);
    }

    /// <summary>The C still refuses the three things the header reader refuses, in that order.</summary>
    [Fact]
    public void TheCStillRefusesTheseThree()
    {
        if (TakionMessageHeader.LocateSource() is not { } path)
            return;

        string? body = TakionMessageHeader.ParseBody(File.ReadAllText(path));

        Assert.NotNull(body);
        Assert.True(
            TakionMessageHeader.TheCStillRefusesTheseThree(body!),
            "takion_parse_message no longer refuses a short header, a foreign tag and a lying length in "
                + "that order, or no longer takes the addend off afterwards - so the client's readers "
                + "are behind the C");
    }

    /// <summary>PP272: the reader says no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(TakionMessageHeader.ParseBody(""));
        Assert.False(TakionMessageHeader.TheCStillRefusesTheseThree(""));
    }
}
