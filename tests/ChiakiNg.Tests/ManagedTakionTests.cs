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
    /// A handshake that never answers releases nothing it never made, and still closes its socket.
    ///
    /// The C's `goto beach` skips all three finis, which is why the postpone release and the socket
    /// close sit at the label every exit passes through.
    /// </summary>
    [Fact]
    public void AFailedHandshakeStillClosesTheSocketAndNothingElse()
    {
        var takion = new ManagedTakion(0x0000_3333);

        // Nothing is answering, so the handshake exhausts its attempts.
        TakionHandshakeOutcome outcome = takion.Connect(PeerEndPoint, expectTimeoutMs: 20);

        Assert.NotEqual(ChiakiError.Success, outcome.Error);
        Assert.Equal(TakionStage.Connecting, takion.Stage);
        Assert.False(takion.RaisedConnected);

        takion.Dispose();

        Assert.Equal([TakionTeardownStep.Disconnected, TakionTeardownStep.Socket], takion.Teardown);
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
}
