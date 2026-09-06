using System.Diagnostics;
using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP745, under PP707: the run's host, implemented outside the test project for the first time.
///
/// Every member of IStreamRunHost had a counterpart and none of them was reached. These hold the
/// one part that is not delegation - the wait, which is chiaki_cond_timedwait_pred - and the two
/// members whose failure path the interface's own comments say the run has to arrive at.
/// </summary>
public class ManagedStreamRunHostTests(ITestOutputHelper output)
{
    private sealed class Sent : IStreamMessageSink
    {
        public List<StreamMessage> Messages { get; } = [];

        public bool Send(in StreamMessage message)
        {
            Messages.Add(message);
            return true;
        }
    }

    private sealed class Stages : IStageSink
    {
        public List<FrameStageTimer> Pushed { get; } = [];

        public List<ulong> InputToWireUs { get; } = [];

        public void Stage(FrameStageTimer stage, ulong sampleUs) => Pushed.Add(stage);

        public void InputToWire(ulong inputUs) => InputToWireUs.Add(inputUs);
    }

    private sealed class Silent : IFeedbackSink
    {
        public void SendState(ushort seqNum, FeedbackMotion state)
        {
        }

        public void SendHistory(ushort seqNum, ReadOnlySpan<byte> payload)
        {
        }
    }

    private sealed class NoReports : ICongestionSink
    {
        public void Send(CongestionReport report)
        {
        }
    }

    /// <summary>A host whose collaborators are real objects and whose receivers are present.</summary>
    private static ManagedStreamRunHost Host(
        Sent? sent = null,
        Stages? stages = null,
        Func<ManagedVideoReceiver?>? video = null,
        int expectTimeoutMs = 40,
        int idleTimeoutMs = 40)
        => new(
            new ManagedTakion(0x1234),
            new IPEndPoint(IPAddress.Loopback, 9295),
            new ManagedCongestionControl(new ManagedPacketStats(), new NoReports(), 0.05),
            new ManagedFeedbackSender(new Silent()),
            new ManagedSessionEvents(),
            sent ?? new Sent(),
            stages ?? new Stages(),
            () => StreamMessages.Heartbeat(),
            video ?? (() => new ManagedVideoReceiver((_, _, _) => true, new SilentOutbound())),
            () => new ManagedAudioReceiverPair(new SilentFrames(), new SilentFrames()),
            () => new ManagedAudioReceiverPair(new SilentFrames(), new SilentFrames()))
        {
            ExpectTimeoutMs = expectTimeoutMs,
            IdleTimeoutMs = idleTimeoutMs,
        };

    private sealed class SilentFrames : IAudioFrameSink
    {
        public void Header(in ManagedAudioHeader header)
        {
        }

        public void Frame(ReadOnlySpan<byte> frame)
        {
        }
    }

    private sealed class SilentOutbound : IVideoReceiverOutbound
    {
        public void SendCorruptFrame(ushort from, ushort to)
        {
        }

        public bool SendIdrRequest() => true;

        public void FecFailure(int frameIndex, bool idrRequestSent)
        {
        }
    }

    /// <summary>
    /// A WAIT THAT NOTHING SIGNALS RUNS ITS TIMEOUT AND SAYS SO.
    ///
    /// PP365: the predicate is finished, stopped or the remote going away - not failed. So a
    /// handler that failed leaves the wait running its whole window, which is the C's behaviour and
    /// the reason a run cannot read "the wait returned" as "the thing happened".
    /// </summary>
    [Fact]
    public void AWaitNobodyEndsTimesOut()
    {
        ManagedStreamRunHost host = Host(expectTimeoutMs: 30);

        var clock = Stopwatch.StartNew();
        (StreamWaitState flags, bool timedOut) = host.Wait(StreamState.ExpectBang);
        clock.Stop();

        output.WriteLine($"waited {clock.ElapsedMilliseconds}ms");

        Assert.True(timedOut);
        Assert.False(StreamConnectionStates.WaitEnds(flags));
    }

    /// <summary>And a failed flag does not end it, which is the half PP365 found nobody reads.</summary>
    [Fact]
    public void AFailedFlagDoesNotEndAWait()
    {
        ManagedStreamRunHost host = Host(expectTimeoutMs: 30);

        host.Signal(failed: true);

        (StreamWaitState flags, bool timedOut) = host.Wait(StreamState.ExpectBang);

        Assert.True(timedOut);
        Assert.True(flags.Failed);
    }

    /// <summary>
    /// A SIGNAL ENDS IT, and the flags come back as they read when the wait returned.
    ///
    /// Signalled from another thread, because that is where a handler runs in the C - the takion's
    /// receive loop writes the flag and signals the condition the run is waiting on.
    /// </summary>
    [Theory]
    [InlineData("finished")]
    [InlineData("stop")]
    [InlineData("remote")]
    public void ASignalEndsTheWait(string which)
    {
        ManagedStreamRunHost host = Host(expectTimeoutMs: 2000);

        using var signalled = new ManualResetEventSlim();

        var handler = new Thread(() =>
        {
            signalled.Wait();

            switch (which)
            {
                case "finished": host.Signal(finished: true); break;
                case "stop": host.Signal(shouldStop: true); break;
                default: host.Signal(remoteDisconnected: true); break;
            }
        });

        handler.Start();
        signalled.Set();

        (StreamWaitState flags, bool timedOut) = host.Wait(StreamState.ExpectBang);
        handler.Join();

        Assert.False(timedOut);
        Assert.True(StreamConnectionStates.WaitEnds(flags));
    }

    /// <summary>The idle loop's wait returns a timeout when nothing arrives, which is its work.</summary>
    [Fact]
    public void TheIdleWaitReportsATimeoutAsTheWorkBranch()
    {
        ManagedStreamRunHost host = Host(idleTimeoutMs: 30);

        Assert.Equal(ChiakiError.Timeout, host.WaitIdle());
        Assert.Equal(IdleStep.SendHeartbeat, StreamIdleLoop.Next(host.WaitIdle()));
    }

    /// <summary>
    /// A RECEIVER THAT CANNOT BE MADE IS A FALSE RETURN, which is the failure path the C returns on.
    ///
    /// The factories exist for this: a host handed its receivers could never reach the arm the
    /// interface documents, so the run's teardown cascade from that rung would be untestable.
    /// </summary>
    [Fact]
    public void AReceiverThatCannotBeMadeAnswersFalse()
    {
        ManagedStreamRunHost host = Host(video: () => null);

        Assert.False(host.CreateVideoReceiver());
        Assert.Null(host.Video);

        // And the two that can be made answer true and hold what they made.
        Assert.True(host.CreateAudioReceiver());
        Assert.True(host.CreateHapticsReceiver());
        Assert.NotNull(host.Audio);
        Assert.NotNull(host.Haptics);

        // The frees drop them, which is all a managed free can mean.
        host.FreeAudioReceiver();
        host.FreeHapticsReceiver();
        Assert.Null(host.Audio);
        Assert.Null(host.Haptics);
    }

    /// <summary>
    /// The four stage timings go out in the C's order, and Decode is not one of them.
    ///
    /// PP712's row says where they land; this says which four. The fifth timer belongs to the
    /// session rather than to the run, so a host pushing five would be filing a number nothing in
    /// this sequence measured.
    /// </summary>
    [Fact]
    public void TheStagesLiftedAreTheRunsFourInOrder()
    {
        var stages = new Stages();
        ManagedStreamRunHost host = Host(stages: stages);

        host.LiftStages();

        Assert.Equal(
            [FrameStageTimer.Receive, FrameStageTimer.Reorder, FrameStageTimer.Reassemble, FrameStageTimer.Correct],
            stages.Pushed);

        Assert.DoesNotContain(FrameStageTimer.Decode, stages.Pushed);
    }

    /// <summary>
    /// The three messages the host sends go through the one sink, built by the port's own builders.
    /// </summary>
    [Fact]
    public void TheMessagesItSendsAreTheBuildersOwn()
    {
        var sent = new Sent();
        ManagedStreamRunHost host = Host(sent);

        Assert.True(host.SendBig());
        Assert.True(host.SendHeartbeat());
        host.SendDisconnect();

        Assert.Equal(3, sent.Messages.Count);
        Assert.Equal(StreamMessages.Heartbeat().PayloadType, sent.Messages[1].PayloadType);
        Assert.Equal(StreamMessages.Disconnect().PayloadType, sent.Messages[2].PayloadType);
    }

    /// <summary>
    /// The lock pair is counted rather than taken, which PP712 already ruled the right answer.
    ///
    /// The C brackets the CONNECTED callback with an unlock and a lock so a handler may call back
    /// into the session. A real monitor here would need the run to hold it first, which is the C's
    /// ownership; the depth is what PP640's third ordering is actually about.
    /// </summary>
    [Fact]
    public void TheLockPairIsCountedAndTheEventGoesOutBetweenThem()
    {
        ManagedStreamRunHost host = Host();

        Assert.Equal(0, host.LockDepth);

        host.Unlock();
        Assert.Equal(-1, host.LockDepth);

        host.SendConnected();

        host.Lock();
        Assert.Equal(0, host.LockDepth);
    }

    /// <summary>An early streaminfo is held until it is replayed, and freed when it is.</summary>
    [Fact]
    public void AnEarlyStreaminfoIsHeldThenFreed()
    {
        ManagedStreamRunHost host = Host();

        Assert.False(host.HasEarlyStreaminfo);

        host.BufferEarlyStreaminfo([1, 2, 3]);
        Assert.True(host.HasEarlyStreaminfo);

        host.ReplayEarlyStreaminfo();
        Assert.False(host.HasEarlyStreaminfo);
    }

    /// <summary>Beginning a state clears what the C clears, and leaves what it leaves.</summary>
    [Fact]
    public void BeginningAStateClearsFinishedAndFailedOnly()
    {
        ManagedStreamRunHost host = Host();

        host.Signal(finished: true, shouldStop: true, remoteDisconnected: true, failed: true);
        host.BeginState();

        Assert.False(host.Flags.Finished);
        Assert.False(host.Flags.Failed);
        Assert.True(host.Flags.ShouldStop);
        Assert.True(host.Flags.RemoteDisconnected);
    }

    /// <summary>And the two flags the teardown reads come off those same fields.</summary>
    [Fact]
    public void TheTeardownFlagsAreTheOnesTheWaitSets()
    {
        ManagedStreamRunHost host = Host();

        Assert.False(host.ShouldStop);
        Assert.False(host.RemoteDisconnected);

        host.Signal(shouldStop: true, remoteDisconnected: true);

        Assert.True(host.ShouldStop);
        Assert.True(host.RemoteDisconnected);
    }
}
