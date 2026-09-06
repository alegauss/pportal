using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP746, under PP707: chiaki_stream_connection_run's sequence, over a socket and on real objects.
///
/// PP640's six orderings have always been asserted against a trace a Scripted host produced - which
/// proves the run ASKS in the right order, given a world that answers whatever the case wants.
/// PP745 wrote a host that delegates to the port's own takion, congestion thread, feedback sender
/// and message builders, and PP607 had already connected a real takion to PP606's responder over
/// loopback. This is the two together.
///
/// THE SIGNALS COME FROM ANOTHER THREAD, which is the part worth the trouble. In the C the flags a
/// wait watches are written by handlers on the takion's receive thread; a run that only completed
/// when signalled from its own thread would be a run that cannot work at all, and nothing about a
/// script would show that.
///
/// IT DOES NOT STREAM. No console answers a BIG over loopback, so the bang and the streaminfo are
/// signalled rather than parsed. What is proved is the sequence, on objects that own sockets and
/// threads, ending in a teardown that is read rather than trusted.
/// </summary>
public class StreamRunOverASocketTests(ITestOutputHelper output) : IDisposable
{
    private readonly UdpClient peer = new(new IPEndPoint(IPAddress.Loopback, 0));

    private IPEndPoint PeerEndPoint => (IPEndPoint)peer.Client.LocalEndPoint!;

    public void Dispose()
    {
        peer.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class Sent : IStreamMessageSink
    {
        public List<ushort> PayloadTypes { get; } = [];

        public bool Send(in StreamMessage message)
        {
            lock (PayloadTypes)
                PayloadTypes.Add(message.PayloadType);

            return true;
        }

        public ushort[] Snapshot()
        {
            lock (PayloadTypes)
                return [.. PayloadTypes];
        }
    }

    private sealed class Stages : IStageSink
    {
        public List<FrameStageTimer> Pushed { get; } = [];

        public int InputToWireCount { get; private set; }

        public void Stage(FrameStageTimer stage, ulong sampleUs) => Pushed.Add(stage);

        public void InputToWire(ulong inputUs) => InputToWireCount++;
    }

    private sealed class Quiet : IFeedbackSink
    {
        public void SendState(ushort seqNum, FeedbackMotion state)
        {
        }

        public void SendHistory(ushort seqNum, ReadOnlySpan<byte> payload)
        {
        }
    }

    private sealed class Quieter : ICongestionSink
    {
        public void Send(CongestionReport report)
        {
        }
    }

    private sealed class NoFrames : IAudioFrameSink
    {
        public void Header(in ManagedAudioHeader header)
        {
        }

        public void Frame(ReadOnlySpan<byte> frame)
        {
        }
    }

    private sealed class NoOutbound : IVideoReceiverOutbound
    {
        public void SendCorruptFrame(ushort from, ushort to)
        {
        }

        public bool SendIdrRequest() => true;

        public void FecFailure(int frameIndex, bool idrRequestSent)
        {
        }
    }

    /// <summary>Answers the handshake on the loopback socket, as PP607's own test does.</summary>
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
    /// THE RUN, END TO END, over a socket a handshake really crossed.
    ///
    /// The driver stands where the C's handlers stand: it ends each of the three waits and then
    /// leaves the idle loop, from a thread that is not the run's. What comes back is SUCCESS, and
    /// the takion has connected and been closed in the order PP678 recorded.
    /// </summary>
    [Fact]
    public void TheSequenceRunsOnRealObjectsOverARealSocket()
    {
        var responder = new TakionHandshakeResponder(0x0000_5745, [.. Enumerable.Repeat((byte)0x77, 32)]);
        Thread answering = AnswerHandshake(responder);

        var takion = new ManagedTakion(0x0000_1746);
        var sent = new Sent();
        var stages = new Stages();
        var congestion = new ManagedCongestionControl(new ManagedPacketStats(), new Quieter(), 0.05);
        var feedback = new ManagedFeedbackSender(new Quiet());

        var host = new ManagedStreamRunHost(
            takion,
            PeerEndPoint,
            congestion,
            feedback,
            new ManagedSessionEvents(),
            sent,
            stages,
            StreamMessages.Heartbeat,
            () => new ManagedVideoReceiver((_, _, _) => true, new NoOutbound()),
            () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()),
            () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()))
        {
            ExpectTimeoutMs = 4000,
            IdleTimeoutMs = 4000,
        };

        // The handler thread. Finished is set once and left set, which is what the C's flag does
        // between the states the run walks; then it keeps pulsing, because the idle loop leaves on
        // a wait that returns for any reason other than its timeout and a pulse landing between
        // two waits is simply lost. Signalling from HERE is the point: these flags are written by
        // the takion's receive thread in the C, never by the run's own.
        using var finished = new ManualResetEventSlim();

        var driver = new Thread(() =>
        {
            host.Signal(finished: true);

            while (!finished.Wait(5))
                host.Signal();
        })
        {
            IsBackground = true,
            Name = "stream handlers",
        };

        driver.Start();

        ChiakiError outcome = ManagedStreamRun.Run(host);

        finished.Set();
        driver.Join(TimeSpan.FromSeconds(10));
        answering.Join(TimeSpan.FromSeconds(10));

        output.WriteLine($"outcome {outcome}, payload types {string.Join(", ", sent.Snapshot())}");

        Assert.Equal(ChiakiError.Success, outcome);

        // The takion really connected and really closed, which is what "over a socket" means.
        Assert.Equal(TakionStage.Closed, takion.Stage);
        Assert.Equal(TakionResponderState.Done, responder.State);
        Assert.Equal(0x0000_1746u, responder.ClientTag);

        // Ordering 6: the disconnect goes out from the label, on the success path as well.
        ushort[] types = sent.Snapshot();
        Assert.Contains(StreamMessages.DisconnectType, types);
        Assert.Equal(StreamMessages.DisconnectType, types[^1]);

        // The four stage timings came off the takion after the close, in the C's order.
        Assert.Equal(ManagedStreamRunHost.StageOrder, stages.Pushed);

        // And nothing was lifted out of the feedback sender, which is the sender's own guard rather
        // than a missing call: no controller state was ever pushed, so it holds no sample to lift.
        // Ordering 4 - lift before fini - is asserted over the script, where a sample can be made.
        Assert.Equal(0, stages.InputToWireCount);
    }

    /// <summary>
    /// AND A CONNECT THAT CANNOT HAPPEN UNWINDS WITHOUT CLOSING A TAKION THAT NEVER OPENED.
    ///
    /// The rung PP295 found the old teardown table wrong about. Here it is over a socket with
    /// nothing answering: the handshake times out, the run enters at the video receiver, and the
    /// takion is not closed because it never connected.
    /// </summary>
    [Fact]
    public void AConnectThatNobodyAnswersUnwindsWithoutClosingTheTakion()
    {
        var takion = new ManagedTakion(0x0000_2746);
        var sent = new Sent();

        var host = new ManagedStreamRunHost(
            takion,
            // Nothing is listening here: the port is this test's own peer, closed before we start.
            Unanswered(),
            new ManagedCongestionControl(new ManagedPacketStats(), new Quieter(), 0.05),
            new ManagedFeedbackSender(new Quiet()),
            new ManagedSessionEvents(),
            sent,
            new Stages(),
            StreamMessages.Heartbeat,
            () => new ManagedVideoReceiver((_, _, _) => true, new NoOutbound()),
            () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()),
            () => new ManagedAudioReceiverPair(new NoFrames(), new NoFrames()))
        {
            ExpectTimeoutMs = 200,
            IdleTimeoutMs = 200,
        };

        ChiakiError outcome = ManagedStreamRun.Run(host);

        output.WriteLine($"outcome {outcome}, stage {takion.Stage}");

        Assert.Equal(ChiakiError.Unknown, outcome);

        // Never connected, so never closed - and no disconnect went out, because the run had not
        // reached the bang when it failed.
        Assert.NotEqual(TakionStage.Closed, takion.Stage);
        Assert.Empty(sent.Snapshot());
    }

    /// <summary>A loopback endpoint with nothing bound to it, so a handshake can only time out.</summary>
    private static IPEndPoint Unanswered()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var taken = (IPEndPoint)probe.Client.LocalEndPoint!;

        return new IPEndPoint(IPAddress.Loopback, taken.Port);
    }

}
