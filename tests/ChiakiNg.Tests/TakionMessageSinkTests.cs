using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP748: the takion's send, and the seam it was the only thing missing from.
///
/// PP675 wrote the bytes, PP678 gave the takion its wire and send buffer, PP671 modelled the stages
/// - and nothing joined them, so no message the port builds had a way out. These put a real
/// datagram on a real socket and read it off the other end.
/// </summary>
public class TakionMessageSinkTests(ITestOutputHelper output) : IDisposable
{
    private readonly UdpClient peer = new(new IPEndPoint(IPAddress.Loopback, 0));

    private IPEndPoint PeerEndPoint => (IPEndPoint)peer.Client.LocalEndPoint!;

    public void Dispose()
    {
        peer.Dispose();
        GC.SuppressFinalize(this);
    }

    private Thread AnswerHandshake(TakionHandshakeResponder responder)
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
        })
        {
            IsBackground = true,
            Name = "takion peer",
        };

        thread.Start();
        return thread;
    }

    /// <summary>
    /// A MESSAGE GOES OUT AND ARRIVES, framed the way chiaki_takion_send_message_data frames it.
    ///
    /// Read off the peer's own socket rather than from the sink's counters: a send that incremented
    /// a number and wrote nothing would satisfy every count this class keeps.
    /// </summary>
    [Fact]
    public void AMessageReachesThePeerWithTheCsFraming()
    {
        var responder = new TakionHandshakeResponder(0x0000_4a4a, [.. Enumerable.Repeat((byte)0x61, 32)]);
        Thread answering = AnswerHandshake(responder);

        using var takion = new ManagedTakion(0x0000_1748);
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        var sink = new TakionMessageSink(takion);
        StreamMessage heartbeat = StreamMessages.Heartbeat();

        Assert.True(sink.Send(heartbeat));

        var from = new IPEndPoint(IPAddress.Loopback, 0);
        peer.Client.ReceiveTimeout = 5000;
        byte[] arrived = peer.Receive(ref from);

        output.WriteLine($"{arrived.Length} bytes, stage {sink.Last?.Stage}");

        // Exactly the size the framing says, for this payload.
        Assert.Equal(TakionDataDatagrams.DataSize(heartbeat.Body.Length), arrived.Length);

        // And the payload really is at the end of it, past the header and the nine-byte data header.
        Assert.Equal(heartbeat.Body, arrived[^heartbeat.Body.Length..]);

        Assert.Equal(TakionSendStage.SentAndHeld, sink.Last?.Stage);
        Assert.Equal(1, sink.Sent);
        Assert.Equal(1, takion.DataSent);
    }

    /// <summary>
    /// THE SEQUENCE NUMBER ADVANCES ONCE PER SEND, and the send buffer holds each one.
    ///
    /// PP671's stages: the packet is held for resend last, and the console waits on a number that
    /// went out. A counter that did not move would let two messages claim one sequence number.
    /// </summary>
    [Fact]
    public void EachSendTakesItsOwnSequenceNumberAndIsHeldForResend()
    {
        var responder = new TakionHandshakeResponder(0x0000_4b4b, [.. Enumerable.Repeat((byte)0x62, 32)]);
        Thread answering = AnswerHandshake(responder);

        using var takion = new ManagedTakion(0x0000_2748);
        Assert.Equal(ChiakiError.Success, takion.Connect(PeerEndPoint, expectTimeoutMs: 2000).Error);
        answering.Join(TimeSpan.FromSeconds(5));

        var sink = new TakionMessageSink(takion);

        Assert.True(sink.Send(StreamMessages.Heartbeat()));
        uint first = sink.Last!.Value.SeqNum;

        Assert.True(sink.Send(StreamMessages.Disconnect()));
        uint second = sink.Last!.Value.SeqNum;

        output.WriteLine($"seq {first} then {second}");

        Assert.Equal(first + 1, second);
        Assert.Equal(2, sink.Sent);
        Assert.Equal(2, sink.Offered);

        // Both are in the buffer the resend loop reads, under the numbers that went out.
        Assert.NotNull(takion.SendBuffer);
        Assert.Equal(2, takion.SendBuffer.Count);
        Assert.Equal([first, second], takion.SendBuffer.SeqNums);
    }

    /// <summary>
    /// A SEND BEFORE THE HANDSHAKE IS REFUSED, and refused where nothing has been spent.
    ///
    /// The C's caller cannot reach a send before its takion connected, so a port that threw here
    /// would turn a sequencing mistake into a crash where the C returns an error.
    /// </summary>
    [Fact]
    public void ASendBeforeTheHandshakeIsRefusedRatherThanThrown()
    {
        using var takion = new ManagedTakion(0x0000_3748);
        var sink = new TakionMessageSink(takion);

        Assert.False(sink.Send(StreamMessages.Heartbeat()));

        Assert.Equal(TakionSendStage.KeyPositionRefused, sink.Last?.Stage);
        Assert.Equal(ChiakiError.Uninitialized, sink.Last?.Error);

        // Nothing spent: no sequence number taken, no key position, nothing sent.
        Assert.False(sink.Last?.SequenceNumberSpent);
        Assert.False(sink.Last?.KeyPositionSpent);
        Assert.Equal(0, sink.Sent);
        Assert.Equal(1, sink.Offered);
        Assert.Equal(0, takion.DataSent);
    }

    /// <summary>
    /// PP741: and the seam it fills is off the unreached list, with nothing taking its place.
    /// </summary>
    [Fact]
    public void TheMessageSeamIsNoLongerUnreached()
    {
        IReadOnlyList<string> unreached = SeamReach.UnreachedIn(typeof(TakionMessageSink).Assembly);

        output.WriteLine(string.Join(", ", unreached));

        Assert.DoesNotContain(nameof(IStreamMessageSink), unreached);
        Assert.Equal([.. SeamReach.Expected.Select(one => one.Interface).Order(StringComparer.Ordinal)], unreached);
    }
}
