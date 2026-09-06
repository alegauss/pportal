using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP790, under PP784: senkusha's run as a sequence, over a host that records what it asks.
///
/// PP788 gave the states and PP789 the measurements; neither is a run. The ordering is the
/// deliverable and it is truer here than for the stream connection: senkusha's whole output is
/// three numbers, and producing them in another order produces different ones.
///
/// THREE EXITS AND THEY ARE THREE DIFFERENT TEARDOWNS. A stop before anything opens closes nothing;
/// a failure between the connect and the bang closes the takion and tells the console NOTHING; and
/// only from the bang onward is the console told. The last is the one worth reading twice - a
/// senkusha that timed out waiting for the protocol ack leaves a console holding a conversation
/// that the stream connection then opens a second one beside.
/// </summary>
public class ManagedSenkushaRunTests(ITestOutputHelper output)
{
    /// <summary>A host that answers what a case wants and writes down everything it was asked.</summary>
    private sealed class Scripted : ISenkushaRunHost
    {
        public List<string> Trace { get; } = [];

        public bool ShouldStopAnswer { get; init; }

        public bool Connects { get; init; } = true;

        public bool Versions { get; init; } = true;

        public bool Bigs { get; init; } = true;

        /// <summary>Which states finish. A state absent from this set times out.</summary>
        public HashSet<SenkushaState> Finishes { get; init; } =
            [.. Enum.GetValues<SenkushaState>()];

        /// <summary>A state that answers with the stop flag rather than by finishing.</summary>
        public SenkushaState? StopsAt { get; init; }

        public ChiakiError Rtt { get; init; } = ChiakiError.Success;

        public ChiakiError In { get; init; } = ChiakiError.Success;

        public ChiakiError Out { get; init; } = ChiakiError.Success;

        public ulong RoundTrip { get; init; } = 2000;

        public ulong SawTimeoutMs { get; private set; }

        public uint SawMtuIn { get; private set; }

        public bool ShouldStop
        {
            get
            {
                Trace.Add("should stop");
                return ShouldStopAnswer;
            }
        }

        public void BeginState(SenkushaState state) => Trace.Add($"begin {state}");

        public bool ConnectTakion() => Say("connect takion", Connects);

        public (SenkushaWaitState Flags, bool TimedOut) Wait(SenkushaState state)
        {
            Trace.Add($"wait {state}");

            if (StopsAt == state)
                return (new SenkushaWaitState(ShouldStop: true), false);

            bool finished = Finishes.Contains(state);
            return (new SenkushaWaitState(Finished: finished), !finished);
        }

        public bool SetVersion() => Say("set version", Versions);

        public bool SendBig() => Say("send big", Bigs);

        public ChiakiError RunRttTest(out ulong roundTripMicroseconds)
        {
            Trace.Add("rtt test");
            roundTripMicroseconds = Rtt == ChiakiError.Success ? RoundTrip : 0;
            return Rtt;
        }

        public ChiakiError RunMtuInTest(ulong timeoutMs, out uint mtuIn)
        {
            Trace.Add("mtu in test");
            SawTimeoutMs = timeoutMs;
            mtuIn = In == ChiakiError.Success ? 1454u : 0u;
            return In;
        }

        public ChiakiError RunMtuOutTest(uint mtuIn, ulong timeoutMs, out uint mtuOut)
        {
            Trace.Add("mtu out test");
            SawMtuIn = mtuIn;
            mtuOut = Out == ChiakiError.Success ? 1454u : 0u;
            return Out;
        }

        public void SendDisconnect() => Trace.Add("send disconnect");

        public void CloseTakion() => Trace.Add("close takion");

        private bool Say(string what, bool answer)
        {
            Trace.Add(what);
            return answer;
        }
    }

    /// <summary>
    /// THE WHOLE RUN, in the order the C makes its calls.
    ///
    /// Read as a trace rather than as an outcome: a run that made every one of these calls in some
    /// other order would answer SUCCESS too, and produce three numbers measured against a link it
    /// had not finished setting up.
    /// </summary>
    [Fact]
    public void TheSequenceIsTheCs()
    {
        var host = new Scripted();
        SenkushaRunReading reading = ManagedSenkushaRun.Run(host);

        output.WriteLine(string.Join("\n", host.Trace));

        Assert.Equal(ChiakiError.Success, reading.Error);
        Assert.Equal(SenkushaExit.Disconnected, reading.Exit);
        Assert.Equal(SenkushaRung.MtuOutMeasured, reading.Rung);

        Assert.Equal(
            [
                "should stop",
                $"begin {SenkushaState.TakionConnect}",
                "connect takion",
                $"wait {SenkushaState.TakionConnect}",
                $"begin {SenkushaState.ExpectProtocolAck}",
                "set version",
                $"wait {SenkushaState.ExpectProtocolAck}",
                $"begin {SenkushaState.ExpectBang}",
                "send big",
                $"wait {SenkushaState.ExpectBang}",
                "rtt test",
                "mtu in test",
                "mtu out test",
                "send disconnect",
                "close takion",
            ],
            host.Trace);
    }

    /// <summary>
    /// AND THE THREE MEASUREMENTS ARE TIMED BY THE FIRST OF THEM.
    ///
    /// The round trip is measured before the two searches and the timeout they use is derived from
    /// it. A run that searched first would hand them a timeout computed from a number nobody has.
    /// </summary>
    [Fact]
    public void TheSearchesAreTimedByTheRoundTrip()
    {
        var host = new Scripted { RoundTrip = 20000 };
        SenkushaRunReading reading = ManagedSenkushaRun.Run(host);

        Assert.Equal(20000ul, reading.RoundTripMicroseconds);

        // Five round trips in milliseconds, clamped - which is a hundred here and not five seconds.
        Assert.Equal(100ul, host.SawTimeoutMs);
        Assert.Equal(SenkushaMeasurements.MtuTimeoutMs(20000), host.SawTimeoutMs);

        // And the outbound search starts where the inbound one finished.
        Assert.Equal(1454u, host.SawMtuIn);
        Assert.Equal(reading.MtuIn, host.SawMtuIn);
    }

    /// <summary>
    /// A STOP BEFORE ANYTHING OPENS CLOSES NOTHING, which is the one path that never tries.
    /// </summary>
    [Fact]
    public void AStopBeforeTheConnectOpensNothing()
    {
        var host = new Scripted { ShouldStopAnswer = true };
        SenkushaRunReading reading = ManagedSenkushaRun.Run(host);

        Assert.Equal(ChiakiError.Canceled, reading.Error);
        Assert.Equal(SenkushaExit.Quit, reading.Exit);
        Assert.Equal(SenkushaRung.Start, reading.Rung);

        Assert.Equal(["should stop"], host.Trace);
    }

    /// <summary>
    /// AND A CONNECT THAT FAILED DOES NOT CLOSE A TAKION THAT NEVER CAME UP.
    ///
    /// The rung PP295 found the stream connection's own teardown table wrong about, in the same
    /// place. The C leaves by `quit` here and by `quit_takion` one line later, and the difference is
    /// whether chiaki_takion_connect answered at all.
    /// </summary>
    [Fact]
    public void AFailedConnectSkipsTheClose()
    {
        var host = new Scripted { Connects = false };
        SenkushaRunReading reading = ManagedSenkushaRun.Run(host);

        output.WriteLine(string.Join(", ", host.Trace));

        Assert.Equal(SenkushaExit.Quit, reading.Exit);
        Assert.DoesNotContain("close takion", host.Trace);
        Assert.DoesNotContain("send disconnect", host.Trace);
    }

    /// <summary>
    /// THE CONSOLE IS TOLD NOTHING BEFORE THE BANG, which is the finding worth reading twice.
    ///
    /// Every failure between the connect and the bang closes the socket in silence. Senkusha runs
    /// before the stream connection on the same console, so the session then opens a second
    /// conversation beside one the console still thinks is live - and the refusal arrives at a
    /// different call with nothing pointing back here.
    /// </summary>
    [Theory]
    [InlineData(SenkushaState.TakionConnect, SenkushaRung.Start)]
    [InlineData(SenkushaState.ExpectProtocolAck, SenkushaRung.TakionConnected)]
    [InlineData(SenkushaState.ExpectBang, SenkushaRung.ProtocolAcked)]
    public void EveryFailureBeforeTheBangClosesInSilence(SenkushaState timesOutAt, SenkushaRung reached)
    {
        var host = new Scripted
        {
            Finishes = [.. Enum.GetValues<SenkushaState>().Where(one => one != timesOutAt)],
        };

        SenkushaRunReading reading = ManagedSenkushaRun.Run(host);

        output.WriteLine(string.Join(", ", host.Trace));

        Assert.Equal(ChiakiError.Unknown, reading.Error);
        Assert.Equal(SenkushaExit.CloseOnly, reading.Exit);
        Assert.Equal(reached, reading.Rung);

        Assert.Contains("close takion", host.Trace);
        Assert.DoesNotContain("send disconnect", host.Trace);
    }

    /// <summary>
    /// AND A STOP DURING A WAIT IS CANCELED RATHER THAN UNKNOWN, on the same label.
    ///
    /// The two arms the C spells four times: a stop is the session's answer and anything else is
    /// PP380's repair - the wait returns SUCCESS with the predicate false, and carrying that out
    /// reported success from a state that never finished.
    /// </summary>
    [Fact]
    public void AStopDuringAWaitIsCanceled()
    {
        var host = new Scripted { StopsAt = SenkushaState.ExpectBang };
        SenkushaRunReading reading = ManagedSenkushaRun.Run(host);

        Assert.Equal(ChiakiError.Canceled, reading.Error);
        Assert.Equal(SenkushaExit.CloseOnly, reading.Exit);
        Assert.DoesNotContain("send disconnect", host.Trace);
    }

    /// <summary>
    /// A FAILED MEASUREMENT AND A SUCCESSFUL RUN LEAVE IDENTICALLY, from the console's side.
    ///
    /// Both take the disconnect label, so what the console sees is the same two messages. What
    /// differs is the error the run carries out and how far the rung got - which is why the reading
    /// answers both rather than only a code.
    /// </summary>
    [Theory]
    [InlineData("rtt", SenkushaRung.BangAwaited)]
    [InlineData("in", SenkushaRung.RttMeasured)]
    [InlineData("out", SenkushaRung.MtuInMeasured)]
    public void AFailedMeasurementStillTellsTheConsole(string which, SenkushaRung reached)
    {
        var host = new Scripted
        {
            Rtt = which == "rtt" ? ChiakiError.Unknown : ChiakiError.Success,
            In = which == "in" ? ChiakiError.Unknown : ChiakiError.Success,
            Out = which == "out" ? ChiakiError.Unknown : ChiakiError.Success,
        };

        SenkushaRunReading reading = ManagedSenkushaRun.Run(host);

        output.WriteLine(string.Join(", ", host.Trace));

        Assert.Equal(ChiakiError.Unknown, reading.Error);
        Assert.Equal(SenkushaExit.Disconnected, reading.Exit);
        Assert.Equal(reached, reading.Rung);

        // The disconnect goes out BEFORE the close, which is the order the label falls through in.
        int told = host.Trace.IndexOf("send disconnect");
        int closed = host.Trace.IndexOf("close takion");

        Assert.True(told >= 0 && closed > told, "the console was told after the socket closed");
    }

    /// <summary>
    /// And the sequence is the file's, read rather than remembered.
    ///
    /// Five facts about the run's own text: the labels cascade, a failed connect skips the close,
    /// the console is told only past the bang, the measurements run in an order that lets two of
    /// them be timed, and the outbound search takes the inbound answer.
    /// </summary>
    [Fact]
    public void TheOrderIsHeldAgainstTheFile()
    {
        if (ManagedSenkushaRunSource.Locate() is not { } path)
            return;

        string run = ManagedSenkushaRunSource.RunBody(File.ReadAllText(path))
            ?? throw new InvalidOperationException("chiaki_senkusha_run is gone");

        Assert.True(ManagedSenkushaRunSource.TheLabelsStillCascade(run), "the three labels no longer cascade");
        Assert.True(ManagedSenkushaRunSource.AFailedConnectStillSkipsTheClose(run));
        Assert.True(ManagedSenkushaRunSource.TheConsoleIsStillToldOnlyAfterTheBang(run));
        Assert.True(ManagedSenkushaRunSource.TheMeasurementsStillRunInOrder(run));
        Assert.True(ManagedSenkushaRunSource.TheOutboundSearchStillTakesTheInboundAnswer(run));
    }

    /// <summary>The bounds the run passes are the ones PP789 read, and it chooses none of them.</summary>
    [Fact]
    public void TheSearchBoundsAreTheMeasurementsOwn()
    {
        Assert.Equal(SenkushaMeasurements.MtuMin, ManagedSenkushaRun.SearchMin);
        Assert.Equal(SenkushaMeasurements.MtuMax, ManagedSenkushaRun.SearchMax);
        Assert.Equal(SenkushaMeasurements.MtuRetries, ManagedSenkushaRun.SearchRetries);

        if (ManagedSenkushaRunSource.Locate() is not { } path)
            return;

        string run = ManagedSenkushaRunSource.RunBody(File.ReadAllText(path))!;

        Assert.Contains(
            $", {ManagedSenkushaRun.SearchMin}, {ManagedSenkushaRun.SearchMax}, {ManagedSenkushaRun.SearchRetries}, mtu_timeout_ms",
            run,
            StringComparison.Ordinal);
    }
}
