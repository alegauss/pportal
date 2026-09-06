using System.Net;
using System.Net.Sockets;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP754, under PP696: the managed side of PP753's seam, on the far end from the session thread.
///
/// Until this, every construction of a host and every call of the run was in this project - so a
/// session.c that handed over would have waited its whole timeout. These stand the two sides on
/// different threads, as PP753's own do, and check that a run really happens between them.
/// </summary>
public class ManagedStreamRunnerTests(ITestOutputHelper output) : IDisposable
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
        public int Count { get; private set; }

        public bool Send(in StreamMessage message)
        {
            Count++;
            return true;
        }
    }

    private sealed class Stages : IStageSink
    {
        public void Stage(FrameStageTimer stage, ulong sampleUs)
        {
        }

        public void InputToWire(ulong inputUs)
        {
        }
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

    /// <summary>A host whose collaborators are real, pointed at a socket nothing answers.</summary>
    private ManagedStreamRunHost Host(Sent sent, ManagedTakion takion)
        => new(
            takion,
            PeerEndPoint,
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
            ExpectTimeoutMs = 150,
            IdleTimeoutMs = 150,

            // Nothing answers this socket, and the C's own fifteen seconds would outlast the
            // session thread's wait below - which is how PP754 found the knob was missing.
            ConnectTimeoutMs = 150,
        };

    /// <summary>
    /// THE SEAM IS ANSWERED: the thread hands over, the runner builds and runs, the thread wakes.
    ///
    /// This is what PP696 needs and did not have. The session thread's side is stood in for here -
    /// its own edit is that commit's - and what matters is that something on this side answers at
    /// all, on a different thread, with the outcome the C would write a quit reason from.
    /// </summary>
    [Fact]
    public void TheRunnerAnswersTheHandoverTheSessionThreadWaitsOn()
    {
        using var handover = new StreamHandover();
        using var takion = new ManagedTakion(0x0000_1754);
        var sent = new Sent();

        var runner = new ManagedStreamRunner(() => Host(sent, takion)) { StartTimeoutMs = 5000 };

        // Standing where session.c will: reach the stream phase, then block on the outcome.
        ChiakiError taken = ChiakiError.Unknown;

        var sessionThread = new Thread(() =>
        {
            handover.Start();
            taken = handover.AwaitFinish(15000);
        })
        {
            IsBackground = true,
            Name = "session thread",
        };

        sessionThread.Start();

        StreamRunnerOutcome outcome = runner.Run(handover, "the console hung up");

        Assert.True(sessionThread.Join(TimeSpan.FromSeconds(20)));

        output.WriteLine($"runner answered {outcome.Error}, the thread took back {taken}");

        Assert.True(outcome.Started);
        Assert.NotNull(runner.Host);

        // The connect has nothing to answer it, so the run unwinds - which is the C's own rung and
        // exactly what PP746 asserts about it. What matters here is that it RAN and reported.
        Assert.Equal(ChiakiError.Unknown, outcome.Error);
        Assert.Equal(taken, outcome.Error);
        Assert.Equal("the console hung up", handover.Reason);
    }

    /// <summary>
    /// A START THAT NEVER COMES BUILDS NOTHING, which is the other half of taking parts in.
    ///
    /// A host owns a socket. One made for a session that never arrived would have to be torn down
    /// by whoever noticed, so the runner declines to build it and says so.
    /// </summary>
    [Fact]
    public void AStartThatNeverComesBuildsNoHost()
    {
        using var handover = new StreamHandover();
        var built = 0;

        var runner = new ManagedStreamRunner(() =>
        {
            built++;
            throw new InvalidOperationException("the runner must not build a host here");
        })
        {
            StartTimeoutMs = 60,
        };

        StreamRunnerOutcome outcome = runner.Run(handover);

        output.WriteLine($"started {outcome.Started}, error {outcome.Error}, built {built}");

        Assert.False(outcome.Started);
        Assert.Equal(ChiakiError.Timeout, outcome.Error);
        Assert.Equal(0, built);
        Assert.Null(runner.Host);

        // And the seam still carries an answer, so a thread waiting on it is not left forever.
        Assert.Equal(ChiakiError.Timeout, handover.AwaitFinish(0));
    }

    /// <summary>
    /// The report happens before the runner answers its own caller.
    ///
    /// The session thread is blocked on it: a runner that returned first would leave that thread
    /// waiting on a run which was already over, for as long as its timeout.
    /// </summary>
    [Fact]
    public void TheHandoverIsReportedBeforeTheRunnerReturns()
    {
        using var handover = new StreamHandover();
        using var takion = new ManagedTakion(0x0000_2754);

        handover.Start();

        var runner = new ManagedStreamRunner(() => Host(new Sent(), takion)) { StartTimeoutMs = 5000 };

        StreamRunnerOutcome outcome = runner.Run(handover, reason: null);

        // Zero timeout: it is already there, or it was reported too late.
        Assert.Equal(outcome.Error, handover.AwaitFinish(0));
        Assert.Null(handover.Reason);
    }
}
