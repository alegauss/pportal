using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP678: a managed takion that connects over a real socket, runs its loop and tears down in order.
///
/// The oracle for the connect is PP606's responder, driven on a loopback socket - so the handshake
/// crosses a UDP stack rather than a test double, which is the half PP672 could not reach. The loop
/// then runs over that same socket, and the teardown is READ rather than trusted: an order is not
/// visible in a Dispose that works, and the C's is not the order the fields were made in.
/// </summary>
public class ManagedTakionTests(ITestOutputHelper output) : IDisposable
{
    private readonly UdpClient peer = new(new IPEndPoint(IPAddress.Loopback, 0));

    private IPEndPoint PeerEndPoint => (IPEndPoint)peer.Client.LocalEndPoint!;

    public void Dispose()
    {
        peer.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Answers a client's handshake on the loopback socket until the responder is done.
    /// </summary>
    /// <remarks>
    /// On its own thread because the client's Run blocks on its own receive: a single-threaded
    /// harness would have the two waiting for each other, which is the deadlock a test double never
    /// shows.
    /// </remarks>
    private Thread AnswerHandshake(TakionHandshakeResponder responder, int datagramsAfter = 0)
    {
        var thread = new Thread(() =>
        {
            var from = new IPEndPoint(IPAddress.Loopback, 0);

            while (responder.State != TakionResponderState.Done)
            {
                byte[] datagram = peer.Receive(ref from);

                if (responder.Answer(datagram) is { } answer)
                    peer.Send(answer, answer.Length, from);
            }

            // Whatever the loop is meant to receive after the handshake, sent to wherever the
            // client last spoke from - which is the socket the handshake used, and the point of
            // the loop running over that one.
            for (var i = 0; i < datagramsAfter; i++)
            {
                byte[] datagram = [0x00, (byte)i, 0x02, 0x03];
                peer.Send(datagram, datagram.Length, from);
            }
        })
        {
            IsBackground = true,
            Name = "takion peer",
        };

        thread.Start();
        return thread;
    }

    /// <summary>THE CONNECT: a real socket, a real handshake, and the state it leaves owned.</summary>
    [Fact]
    public void ItConnectsOverASocketAndOwnsWhatTheHandshakeSeeded()
    {
        var responder = new TakionHandshakeResponder(0x1234_5678, [.. Enumerable.Repeat((byte)0x5a, 32)]);
        Thread answering = AnswerHandshake(responder);

        using var takion = new ManagedTakion(0x0000_4823);

        TakionHandshakeOutcome outcome = takion.Connect(PeerEndPoint, expectTimeoutMs: 2000);
        answering.Join(TimeSpan.FromSeconds(5));

        output.WriteLine($"{outcome.Error}, {outcome.InitAttempts} init, {outcome.CookieAttempts} cookie");

        Assert.Equal(ChiakiError.Success, outcome.Error);
        Assert.Equal(TakionStage.Connected, takion.Stage);

        // Both tags, and the local one is also the initial sequence number - one chiaki_random_32
        // in the C sets both, and a port drawing twice would send a header the console ignores.
        Assert.Equal(0x0000_4823u, takion.Handshake.TagLocal);
        Assert.Equal(0x0000_4823u, takion.SeqNumLocal);
        Assert.Equal(0x1234_5678u, takion.Handshake.TagRemote);

        // The queue is seeded from the REMOTE TAG, not from the ack's initial-seq-num field.
        Assert.NotNull(takion.DataQueue);
        Assert.Equal(0x1234_5678u, takion.Handshake.RemoteInitialSeqNum);

        Assert.NotNull(takion.SendBuffer);
        Assert.Equal(ManagedTakion.SendBufferSize, takion.SendBuffer.Capacity);
        Assert.True(takion.RaisedConnected);
    }

    /// <summary>And then the loop runs over that same socket, receiving what the peer sends.</summary>
    [Fact]
    public void TheLoopRunsOverTheSocketTheHandshakeUsed()
    {
        var responder = new TakionHandshakeResponder(0x0bad_f00d, [.. Enumerable.Repeat((byte)0x11, 32)]);
        Thread answering = AnswerHandshake(responder, datagramsAfter: 3);

        var seen = new List<int>();
        using var takion = new ManagedTakion(0x0000_1111, datagram => seen.Add(datagram.Length));

        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);

        takion.NextTimeoutMs = 500;
        TakionLoopOutcome loop = takion.RunLoop(enableCrypt: true, iterationLimit: 3);

        answering.Join(TimeSpan.FromSeconds(5));

        output.WriteLine(string.Join(", ", loop.Trace));

        Assert.Equal(3, takion.Dispatched);
        Assert.Equal([4, 4, 4], seen);
        Assert.True(loop.HitLimit);
    }

    /// <summary>
    /// THE TEARDOWN'S ORDER, which is takion_thread_func's and not the order things were made in.
    /// </summary>
    [Fact]
    public void ItTearsDownInTheCsOrder()
    {
        var responder = new TakionHandshakeResponder(0x0000_9999, [.. Enumerable.Repeat((byte)0x22, 32)]);
        Thread answering = AnswerHandshake(responder);

        var takion = new ManagedTakion(0x0000_2222);
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        // Something still postponed, which is the case PP474 filed: the flush is guarded on the
        // cipher, so a session dying before it is agreed leaves the array behind.
        takion.Postpone([0x01, 0x02]);

        takion.Dispose();

        Assert.Equal(
            [
                TakionTeardownStep.SendBuffer,
                TakionTeardownStep.DataQueue,
                TakionTeardownStep.Postponed,
                TakionTeardownStep.Disconnected,
                TakionTeardownStep.Socket,
            ],
            takion.Teardown);

        Assert.Equal(TakionStage.Closed, takion.Stage);
    }

    /// <summary>
    /// PP703: THE STEP THAT COULD NOT HAPPEN. A takion that received video releases its queue.
    ///
    /// PP678 recorded six teardown steps and one of them was unreachable: the field for the video
    /// queue was assigned null in the constructor and null again on connect, so
    /// VideoQueueInitialised was false for every takion the port could build and no test named the
    /// step. PP680 built the arm that opens the queue and nothing owned one.
    ///
    /// This is the join, asserted where it shows: a takion with a sink for the AV branch, one video
    /// datagram through the dispatch, and a teardown whose recorded order now CONTAINS the queue -
    /// between the send buffer and the data queue, which is where the C releases it.
    /// </summary>
    [Fact]
    public void ATakionThatReceivedVideoReleasesItsQueue()
    {
        var responder = new TakionHandshakeResponder(0x0000_aaaa, [.. Enumerable.Repeat((byte)0x33, 32)]);
        Thread answering = AnswerHandshake(responder);

        var sink = new CountingAvSink();
        var takion = new ManagedTakion(0x0000_4444, av: sink);

        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        Assert.False(takion.VideoQueueInitialised);

        ((ITakionLoopHost)takion).Dispatch(VideoDatagram());

        Assert.True(takion.VideoQueueInitialised, "one video datagram did not open a queue");
        Assert.NotNull(takion.AvArm);

        takion.Dispose();

        Assert.Equal(
            [
                TakionTeardownStep.SendBuffer,
                TakionTeardownStep.VideoQueue,
                TakionTeardownStep.DataQueue,
                TakionTeardownStep.Disconnected,
                TakionTeardownStep.Socket,
            ],
            takion.Teardown);
    }

    /// <summary>
    /// And a takion that received none does NOT release one, which is the C's own guard.
    ///
    /// Both halves, because a step appended unconditionally would satisfy the test above and say
    /// something false about every session that never carried video.
    /// </summary>
    [Fact]
    public void ATakionThatReceivedNoVideoReleasesNoQueue()
    {
        var responder = new TakionHandshakeResponder(0x0000_bbbb, [.. Enumerable.Repeat((byte)0x44, 32)]);
        Thread answering = AnswerHandshake(responder);

        var takion = new ManagedTakion(0x0000_5555, av: new CountingAvSink());
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        takion.Dispose();

        Assert.DoesNotContain(TakionTeardownStep.VideoQueue, takion.Teardown);
    }

    /// <summary>
    /// PP703's other half: the poll timeout comes from the AV queues once there are any.
    ///
    /// ManagedTakion carried the property and a comment saying it was "set by whoever drives the AV
    /// queues", and nobody did. A queue waiting for a missing head asks for the rest of its budget;
    /// with no queue the takion falls back to the value it has always used.
    /// </summary>
    [Fact]
    public void ThePollTimeoutComesFromTheAvQueuesOnceThereAreAny()
    {
        var responder = new TakionHandshakeResponder(0x0000_cccc, [.. Enumerable.Repeat((byte)0x55, 32)]);
        Thread answering = AnswerHandshake(responder);

        var takion = new ManagedTakion(0x0000_6666, av: new CountingAvSink());
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        Assert.Equal((ulong)ManagedTakion.IdleTimeoutMs, takion.NextTimeoutMs);

        // Packet index 40 at unit 8 opens the queue at 32, so the head is missing and the queue
        // starts waiting for it - which is the only state that asks for a shorter poll.
        ((ITakionLoopHost)takion).Dispatch(VideoDatagram(packetIndex: 40, unitIndex: 8));

        Assert.InRange(takion.NextTimeoutMs, 1UL, (ulong)ManagedTakion.IdleTimeoutMs);

        takion.Dispose();
    }

    /// <summary>A sink that counts, because what the arm delivers is PP680's subject and not this one's.</summary>
    private sealed class CountingAvSink : IAvArmSink
    {
        public int Packets { get; private set; }

        public void Av(in AvPacket packet, Span<byte> datagram) => Packets++;
    }

    /// <summary>One video datagram long enough for the v9 parse, with chosen indices.</summary>
    private static byte[] VideoDatagram(ushort packetIndex = 7, ushort unitIndex = 0)
    {
        var datagram = new byte[40];
        datagram[0] = (byte)TakionDispatch.Video;

        for (var i = 1; i < datagram.Length; i++)
            datagram[i] = (byte)(i + 0x10);

        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(1), packetIndex);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            datagram.AsSpan(5), (uint)unitIndex << 0x15);

        return datagram;
    }

    /// <summary>
    /// A handshake that never answers releases nothing it never made, and still closes its socket.
    ///
    /// The C's `goto beach` skips all three finis, which is why the postpone release and the socket
    /// close sit at the label every exit passes through.
    ///
    /// PP808: and so does the DISCONNECT, which is why the event is asserted on the path that never
    /// raised a CONNECTED. A takion that failed its handshake ran no receive thread at all, so the
    /// close is the only `beach` it has.
    /// </summary>
    [Fact]
    public void AFailedHandshakeStillClosesTheSocketAndNothingElse()
    {
        var takion = new ManagedTakion(0x0000_3333);

        var raised = 0;
        takion.Disconnected = () => raised++;

        // Nothing is answering, so the handshake exhausts its attempts.
        TakionHandshakeOutcome outcome = takion.Connect(PeerEndPoint, expectTimeoutMs: 20);

        Assert.NotEqual(ChiakiError.Success, outcome.Error);
        Assert.Equal(TakionStage.Connecting, takion.Stage);
        Assert.False(takion.RaisedConnected);
        Assert.False(takion.RaisedDisconnected);

        takion.Dispose();

        Assert.Equal([TakionTeardownStep.Disconnected, TakionTeardownStep.Socket], takion.Teardown);

        // The step recorded the position and raised nothing until PP808 put the event at it.
        Assert.True(takion.RaisedDisconnected);
        Assert.Equal(1, raised);
    }

    /// <summary>
    /// PP808: THE RECEIVE THREAD ENDING IS THE DISCONNECT, and nothing has to close the takion.
    ///
    /// The defect this holds is a wait on a transport that died running its whole timeout. The C
    /// raises DISCONNECT at `beach` and every exit takion_thread_func has passes that label, so the
    /// assertion is deliberately on a thread that stopped while the takion is still OPEN - the case
    /// an event written into the close alone would never reach.
    /// </summary>
    [Fact]
    public void AReceiveThreadThatEndsRaisesTheDisconnectWithoutAClose()
    {
        var responder = new TakionHandshakeResponder(0x0000_dddd, [.. Enumerable.Repeat((byte)0x66, 32)]);
        Thread answering = AnswerHandshake(responder);

        using var takion = new ManagedTakion(0x0000_7777);

        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        var raised = 0;
        takion.Disconnected = () => Interlocked.Increment(ref raised);

        // A millisecond a receive, so one slice is over in about the time a test can wait for it.
        // The default is a second, and a slice is sixty-four of them.
        takion.NextTimeoutMs = 1;
        takion.StartReceiving(enableCrypt: false);

        Assert.True(takion.ReceiveThreadAlive);
        Assert.Equal(0, Volatile.Read(ref raised));

        takion.StopReceiving();

        Assert.True(
            SpinWait.SpinUntil(() => !takion.ReceiveThreadAlive, TimeSpan.FromSeconds(10)),
            "the receive thread did not stop at its slice boundary");

        // Still open, which is the whole point: Stage is Connected and nothing was disposed.
        Assert.Equal(TakionStage.Connected, takion.Stage);
        Assert.True(takion.RaisedDisconnected);
        Assert.Equal(1, Volatile.Read(ref raised));
    }

    /// <summary>
    /// PP808: and it goes out ONCE, though both of the port's `beach`es reach it on an ordinary close.
    ///
    /// The C's thread runs once and passes that label once. Here the close stops the receiver AND
    /// walks the teardown, so the thread's own exit and <c>Dispose</c> both raise - and a listener
    /// told twice would answer a second event with a wait the first one already ended.
    /// </summary>
    [Fact]
    public void TheDisconnectGoesOutOnceThoughBothExitsReachIt()
    {
        var responder = new TakionHandshakeResponder(0x0000_eeee, [.. Enumerable.Repeat((byte)0x77, 32)]);
        Thread answering = AnswerHandshake(responder);

        var takion = new ManagedTakion(0x0000_8888);

        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        var raised = 0;
        takion.Disconnected = () => Interlocked.Increment(ref raised);

        takion.NextTimeoutMs = 1;
        takion.StartReceiving(enableCrypt: false);

        takion.Dispose();

        Assert.Equal(1, Volatile.Read(ref raised));
        Assert.Contains(TakionTeardownStep.Disconnected, takion.Teardown);
    }

    /// <summary>
    /// NOTHING IS ALLOCATED PER DATAGRAM once the loop is warm.
    ///
    /// PP44's budget. Measured as a DIFFERENCE rather than as a total, because the loop allocates
    /// its own trace - a List of steps and the outcome around it - and that is per RUN and would
    /// otherwise be charged to the datagrams.
    ///
    /// So two runs of the same length are compared: one where every iteration receives a datagram
    /// and one where every iteration times out. Same iteration count, same number of trace entries,
    /// and the only difference between them is the receive and the dispatch. Anything the datagram
    /// path allocated would show up as the gap between the two, and it is the gap that is asserted.
    /// </summary>
    [Fact]
    public void TheWarmLoopAllocatesNothingForTheDatagramItself()
    {
        var responder = new TakionHandshakeResponder(0x0000_7777, [.. Enumerable.Repeat((byte)0x33, 32)]);
        Thread answering = AnswerHandshake(responder, datagramsAfter: 16);

        var lengths = 0;
        using var takion = new ManagedTakion(0x0000_4444, datagram => lengths += datagram.Length);

        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        takion.NextTimeoutMs = 200;

        // Warm: the receive buffer's pool, the socket's own buffers, every generic instantiation.
        takion.RunLoop(enableCrypt: true, iterationLimit: 8);

        long before = GC.GetAllocatedBytesForCurrentThread();
        TakionLoopOutcome receiving = takion.RunLoop(enableCrypt: true, iterationLimit: 8);
        long withDatagrams = GC.GetAllocatedBytesForCurrentThread() - before;

        answering.Join(TimeSpan.FromSeconds(5));

        // Now the peer has stopped, so every iteration times out instead. Same length, same trace.
        before = GC.GetAllocatedBytesForCurrentThread();
        TakionLoopOutcome idling = takion.RunLoop(enableCrypt: true, iterationLimit: 8);
        long withoutDatagrams = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine(
            $"{withDatagrams} byte(s) receiving, {withoutDatagrams} idling, "
                + $"{receiving.Trace.Count} and {idling.Trace.Count} trace step(s)");

        // The two runs really did do different things, or the comparison is between two idles.
        Assert.Contains(TakionLoopStep.Dispatch, receiving.Trace);
        Assert.DoesNotContain(TakionLoopStep.Dispatch, idling.Trace);
        Assert.Equal(16 * 4, lengths);

        // RECEIVING IS THE CHEAPER OF THE TWO, which is the answer and not the one expected. Eight
        // datagrams cost the trace and nothing else - the same figure a run of eight that received
        // nothing new would cost - so the receive, the pooled buffer and the dispatch add none.
        Assert.True(
            withDatagrams <= withoutDatagrams,
            $"receiving eight datagrams cost {withDatagrams} byte(s) and timing out eight times cost "
                + $"{withoutDatagrams}; if the first is the larger, the datagram path has started "
                + "allocating and PP44's budget is the thing to re-read");

        // A LOOP THAT WAITS IS NOT FREE, and that is the finding rather than a defect here: the
        // timeout costs about seven hundred bytes a go, inside the socket rather than in this loop.
        // Stated because a reader measuring an idle session would otherwise attribute it to the
        // transport, and because a real loop times out whenever the console has nothing to say.
        long perTimeout = (withoutDatagrams - withDatagrams) / 8;
        output.WriteLine($"{perTimeout} byte(s) per timeout, in the socket rather than in the loop");

        Assert.True(perTimeout > 0, "the timeout path now allocates nothing, so this note is stale");
    }

    /// <summary>A takion connects once, and says so rather than opening a second socket.</summary>
    [Fact]
    public void ItConnectsOnlyOnce()
    {
        var responder = new TakionHandshakeResponder(0x0000_8888, [.. Enumerable.Repeat((byte)0x44, 32)]);
        Thread answering = AnswerHandshake(responder);

        using var takion = new ManagedTakion(0x0000_5555);
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        Assert.Throws<InvalidOperationException>(() => takion.Connect(PeerEndPoint));
    }

    /// <summary>And the loop refuses to run on one that has not connected.</summary>
    [Fact]
    public void TheLoopRefusesAnUnconnectedTakion()
    {
        using var takion = new ManagedTakion(0x0000_6666);

        Assert.Throws<InvalidOperationException>(() => takion.RunLoop());
    }

    /// <summary>A connected takion whose peer is this test's socket, drained of the handshake.</summary>
    private ManagedTakion Connected(uint tagRemote, uint tagLocal, byte fill)
    {
        var responder = new TakionHandshakeResponder(tagRemote, [.. Enumerable.Repeat(fill, 32)]);
        Thread answering = AnswerHandshake(responder);

        var takion = new ManagedTakion(tagLocal);
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        peer.Client.ReceiveTimeout = 2000;
        return takion;
    }

    /// <summary>The next datagram this takion put on the socket.</summary>
    private byte[] NextFromTheSocket()
    {
        var from = new IPEndPoint(IPAddress.Loopback, 0);
        return peer.Receive(ref from);
    }

    /// <summary>The MAC field of a datagram, found by its own type's layout rather than by a number.</summary>
    private static byte[] MacOf(byte[] datagram)
    {
        int baseType = datagram[0] & TakionDispatch.BaseTypeMask;

        if (TakionPacketMac.LayoutFor(baseType) is not { } layout)
        {
            Assert.Fail($"no MAC layout for base type {baseType}");
            return [];
        }

        return datagram.AsSpan(layout.MacOffset, TakionPacketMac.GmacSize).ToArray();
    }

    /// <summary>
    /// PP811: THE THREE SENDS THAT ARE chiaki_takion_send LEAVE SIGNED, once a cipher exists.
    ///
    /// This is the whole task in one assertion. TakionSendPath is PP675's port of that function and
    /// nothing called it: each of these wrote its key position, sent, and stamped nothing - so a
    /// live takion put unsigned control packets on the wire and the console verifies them. PP807
    /// measured what that costs: about 1170 of them and the session torn down.
    ///
    /// ASKED OF THE SOCKET AND NOT OF THE PATH, because the path already had tests and was still
    /// unreached. What is read here is the datagram that crossed a UDP stack.
    /// </summary>
    [Theory]
    [InlineData("ack")]
    [InlineData("congestion")]
    [InlineData("data")]
    public void TheSendsThatMacInTheCLeaveWithAMac(string which)
    {
        using ManagedTakion takion = Connected(0x0000_e811, 0x0000_8110, 0x66);

        takion.LocalCrypt = ManagedGkCrypt.Derive(
            2,
            [.. Enumerable.Repeat((byte)0x2b, GkDerivation.HandshakeKeySize)],
            [.. Enumerable.Repeat((byte)0x7c, GkDerivation.EcdhSecretSize)]);

        switch (which)
        {
            case "ack":
                Assert.Equal(ChiakiError.Success, takion.SendDataAck(99));
                break;
            case "congestion":
                Assert.Equal(ChiakiError.Success, takion.SendCongestion(received: 12, lost: 3));
                break;
            default:
                Assert.Equal(
                    TakionSendStage.SentAndHeld,
                    takion.SendData(channel: 0, payload: [1, 2, 3, 4]).Stage);
                break;
        }

        byte[] onTheWire = NextFromTheSocket();
        byte[] mac = MacOf(onTheWire);

        output.WriteLine($"{which}: {Convert.ToHexString(onTheWire)}");

        Assert.NotEqual(new byte[TakionPacketMac.GmacSize], mac);
    }

    /// <summary>
    /// AND WITH NO CIPHER THE SAME SENDS STILL GO, with four zeroes where the tag will be.
    ///
    /// The C tests gkcrypt_local inside chiaki_takion_packet_mac and blanks the field either way,
    /// returning Success - so a send before the bang is a real state and not an error. A port that
    /// refused here would break every send the handshake makes.
    /// </summary>
    [Fact]
    public void WithNoCipherTheSameSendsLeaveUnsignedRatherThanBeingRefused()
    {
        using ManagedTakion takion = Connected(0x0000_e812, 0x0000_8111, 0x67);

        Assert.Null(takion.LocalCrypt);
        Assert.Equal(ChiakiError.Success, takion.SendDataAck(7));

        Assert.Equal(new byte[TakionPacketMac.GmacSize], MacOf(NextFromTheSocket()));
    }

    /// <summary>
    /// PP811: AND THE FEEDBACK SEND TAKES THE SAME LOCK, which is what makes it exclude the others.
    ///
    /// It stamps its own tag and always did - the sequence was ported and correct. What it did not
    /// do was hold gkcrypt_local while doing it, so a feedback packet's ledger advance could
    /// interleave with a data message's stamp and desynchronise the stream cipher, which produces
    /// noise on the console rather than an error here. One lock object is what fixes that, and the
    /// observable half is that the packet is signed by the ported sequence at all.
    /// </summary>
    [Fact]
    public void TheFeedbackSendGoesThroughThePortedSequence()
    {
        using ManagedTakion takion = Connected(0x0000_e813, 0x0000_8112, 0x68);

        takion.LocalCrypt = ManagedGkCrypt.Derive(
            2,
            [.. Enumerable.Repeat((byte)0x2b, GkDerivation.HandshakeKeySize)],
            [.. Enumerable.Repeat((byte)0x7c, GkDerivation.EcdhSecretSize)]);

        Assert.Equal(
            ChiakiError.Success,
            takion.SendFeedback(TakionFeedbackSends.FeedbackStateType, seqNum: 5, payload: [9, 9, 9, 9]));

        Assert.Equal(1, takion.FeedbackSent);

        byte[] onTheWire = NextFromTheSocket();

        // Its own layout, which is not the one MacOf knows: a feedback packet is not a type either
        // of the C's offset switches answers for.
        Assert.NotEqual(
            new byte[TakionFeedbackSends.GmacSize],
            onTheWire.AsSpan(
                TakionFeedbackSends.Feedback.MacOffset, TakionFeedbackSends.GmacSize).ToArray());

        // And the payload was encrypted in place rather than sent as it was handed in.
        Assert.NotEqual(
            new byte[] { 9, 9, 9, 9 },
            onTheWire.AsSpan(TakionFeedbackSends.Feedback.HeadSize).ToArray());
    }

    /// <summary>
    /// PP823: THE SEND POSITION IS A COUNTER OF WHAT THIS CLIENT SENT, not what arrived.
    ///
    /// takion.c keeps two: key_pos_local, advanced by each packet this side sends, and key_state,
    /// which reconstructs 64 bits from the 32 an ARRIVING datagram carries. The port had only the
    /// second and all four position-taking sends asked it what wire value zero meant - so the
    /// answer moved with received traffic and counted nothing that left.
    ///
    /// Asserted through the wire, because the position is a field in the packet: three sends and
    /// the numbers they carry, against the four sizings the C passes at those sites.
    /// </summary>
    [Fact]
    public void TheSendPositionCountsWhatWasSentAndNotWhatArrived()
    {
        using ManagedTakion takion = Connected(0x0000_e823, 0x0000_8230, 0x6a);

        // No cipher, so every position is zero and the ledger does not move - the C's own answer
        // ahead of the bang, and the reason this half was invisible for so long.
        Assert.Equal(ChiakiError.Success, takion.SendDataAck(1));
        Assert.Equal(0u, takion.KeyPosLocal);
        Assert.Equal(0u, PositionOf(NextFromTheSocket()));

        takion.LocalCrypt = ManagedGkCrypt.Derive(
            2,
            [.. Enumerable.Repeat((byte)0x2b, GkDerivation.HandshakeKeySize)],
            [.. Enumerable.Repeat((byte)0x7c, GkDerivation.EcdhSecretSize)]);

        // WholePacket for the ack: twelve bytes, and the C's sizing adds the remainder rather than
        // rounding up - 12 + (12 % 16) is 24, where rounding up would give 16.
        Assert.Equal(ChiakiError.Success, takion.SendDataAck(2));
        Assert.Equal(0u, PositionOf(NextFromTheSocket()));

        ulong afterAck = TakionKeyPosition.Sized((ulong)TakionDataDatagrams.AckSize);
        Assert.Equal(afterAck, takion.KeyPosLocal);

        // FixedConstant for congestion: fifteen bytes, so 15 + 15 = 30.
        Assert.Equal(ChiakiError.Success, takion.SendCongestion(received: 1, lost: 0));
        Assert.Equal(afterAck, PositionOf(NextFromTheSocket()));

        ulong afterCongestion = afterAck + TakionKeyPosition.Sized(TakionCongestion.PacketSize);
        Assert.Equal(afterCongestion, takion.KeyPosLocal);

        // PayloadOnly for a data message: the PAYLOAD's four bytes and not the packet's thirty,
        // which is the sizing that looks like a bug in the C and is the protocol.
        Assert.Equal(
            TakionSendStage.SentAndHeld, takion.SendData(channel: 0, payload: [1, 2, 3, 4]).Stage);

        Assert.Equal(afterCongestion, PositionOf(NextFromTheSocket()));
        Assert.Equal(afterCongestion + TakionKeyPosition.Sized(4), takion.KeyPosLocal);

        output.WriteLine($"key_pos_local ended at {takion.KeyPosLocal}");
    }

    /// <summary>
    /// AND ARRIVING TRAFFIC DOES NOT MOVE IT, which is the defect stated as a difference.
    ///
    /// The receive ledger is dragged forward by every datagram the AV arm reads. Before this, a
    /// send asked THAT one what wire value zero meant - so a stream's twenty thousand video packets
    /// decided where the next control message claimed to be.
    ///
    /// THE SECOND SEND IS WHAT SEPARATES THE TWO ANSWERS, and a test without it proves nothing: on
    /// a fresh takion both counters read zero, and the old reading returned zero for the first send
    /// as well. So one send advances the send counter first, then an arrival moves the OTHER one,
    /// and the position the next packet carries has to be the first counter's - where the reading
    /// this replaces would have gone back to zero.
    /// </summary>
    [Fact]
    public void ReceivedTrafficMovesTheReceiveLedgerAndNotTheSendCounter()
    {
        using ManagedTakion takion = Connected(0x0000_e824, 0x0000_8231, 0x6b);

        takion.LocalCrypt = ManagedGkCrypt.Derive(
            2,
            [.. Enumerable.Repeat((byte)0x2b, GkDerivation.HandshakeKeySize)],
            [.. Enumerable.Repeat((byte)0x7c, GkDerivation.EcdhSecretSize)]);

        // One send, so the send counter is somewhere other than where it started.
        Assert.Equal(ChiakiError.Success, takion.SendDataAck(3));
        NextFromTheSocket();

        ulong sent = takion.KeyPosLocal;
        Assert.NotEqual(0ul, sent);

        // Now an arrival, committed exactly as the receive path commits one.
        ulong arrived = takion.Ledger.RequestPos(0x0001_0000, commit: true);

        output.WriteLine($"receive ledger at {arrived}, send counter at {takion.KeyPosLocal}");

        Assert.True(arrived >= 0x0001_0000ul, $"the receive ledger did not advance, it is at {arrived}");

        // Untouched by it, which is the whole finding.
        Assert.Equal(sent, takion.KeyPosLocal);

        // And the next packet carries the send counter. The reading this replaces asked the receive
        // ledger what wire value zero meant and would have put ZERO here, having already sent one.
        Assert.Equal(ChiakiError.Success, takion.SendDataAck(4));
        Assert.Equal((uint)sent, PositionOf(NextFromTheSocket()));
    }

    /// <summary>The key position a datagram carries, by its own type's layout.</summary>
    private static uint PositionOf(byte[] datagram)
    {
        int baseType = datagram[0] & TakionDispatch.BaseTypeMask;

        if (TakionPacketMac.LayoutFor(baseType) is not { } layout)
        {
            Assert.Fail($"no key position layout for base type {baseType}");
            return 0;
        }

        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            datagram.AsSpan(layout.KeyPosOffset, TakionPacketMac.KeyPosSize));
    }

    /// <summary>
    /// A raw send is still raw, which is the one of the five that must NOT be routed.
    ///
    /// chiaki_takion_send_raw is one write to the socket, and senkusha's two measurements are its
    /// callers: what they measure is the SIZE that reaches the console, so a send that stamped them
    /// would measure a link nobody has - and senkusha's takion has no cipher to stamp with anyway.
    /// </summary>
    [Fact]
    public void ARawSendIsStillUnstamped()
    {
        using ManagedTakion takion = Connected(0x0000_e814, 0x0000_8113, 0x69);

        takion.LocalCrypt = ManagedGkCrypt.Derive(
            2,
            [.. Enumerable.Repeat((byte)0x2b, GkDerivation.HandshakeKeySize)],
            [.. Enumerable.Repeat((byte)0x7c, GkDerivation.EcdhSecretSize)]);

        byte[] probe = [TakionMessageHeader.ControlPacketType, .. Enumerable.Repeat((byte)0xAB, 23)];

        Assert.Equal(ChiakiError.Success, takion.SendRaw(probe));
        Assert.Equal(probe, NextFromTheSocket());
    }
}
