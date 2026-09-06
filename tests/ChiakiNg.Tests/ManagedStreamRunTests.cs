using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP295: the stream connection's run as a sequence, with the six orderings read off its trace.
///
/// PP640 stated the six as checks on the C. This asserts them on the managed run's own behaviour -
/// which is what "ported, not only its functions" means. A host records every call; the trace is
/// the evidence; and a run that made these calls in some other order would pass any comparison of
/// messages and fail here.
/// </summary>
public class ManagedStreamRunTests(ITestOutputHelper output)
{
    /// <summary>
    /// A host that answers as told and writes down what it was asked, in order.
    ///
    /// Every failure path is reached by turning one answer false or setting one flag, which is what
    /// makes the cascade testable without a console: the C reaches each label from exactly one
    /// place, and so does this.
    /// </summary>
    private sealed class Scripted : IStreamRunHost
    {
        public List<string> Trace { get; } = [];

        public bool Audio = true, Haptics = true, Video = true, Takion = true, Congestion = true;
        public bool Big = true, Feedback = true;
        public StreamWaitState ConnectFlags = new(Finished: true);
        public StreamWaitState BangFlags = new(Finished: true);
        public StreamWaitState StreaminfoFlags = new(Finished: true);
        public bool TimedOut;
        public bool EarlyStreaminfo;
        public StreamWaitState AfterReplay = new(Finished: true);
        public Queue<ChiakiError> IdleWaits = new([ChiakiError.Timeout, ChiakiError.Success]);
        public bool ShouldStop { get; set; }
        public bool RemoteDisconnected { get; set; }

        private bool Say(string what, bool answer)
        {
            Trace.Add(what);
            return answer;
        }

        public bool CreateAudioReceiver() => Say("create audio", Audio);
        public bool CreateHapticsReceiver() => Say("create haptics", Haptics);
        public bool CreateVideoReceiver() => Say("create video", Video);
        public bool ConnectTakion() => Say("connect takion", Takion);
        public bool StartCongestionControl() => Say("start congestion", Congestion);
        public bool SendBig() => Say("send big", Big);
        public bool StartFeedbackSender() => Say("start feedback", Feedback);

        public (StreamWaitState Flags, bool TimedOut) Wait(StreamState state)
        {
            Trace.Add($"wait {state}");
            StreamWaitState flags = state switch
            {
                StreamState.TakionConnect => ConnectFlags,
                StreamState.ExpectBang => BangFlags,
                _ => StreaminfoFlags,
            };
            return (flags, TimedOut);
        }

        public bool HasEarlyStreaminfo => EarlyStreaminfo;

        public StreamWaitState ReplayEarlyStreaminfo()
        {
            Trace.Add("replay early streaminfo");
            return AfterReplay;
        }

        public void Unlock() => Trace.Add("unlock");
        public void Lock() => Trace.Add("lock");
        public void SendConnected() => Trace.Add("send connected");

        public ChiakiError WaitIdle()
        {
            Trace.Add("wait idle");
            return IdleWaits.Count > 0 ? IdleWaits.Dequeue() : ChiakiError.Success;
        }

        public bool SendHeartbeat() => Say("send heartbeat", true);
        public void LiftInputToWire() => Trace.Add("lift input_to_wire");
        public void FiniFeedbackSender() => Trace.Add("fini feedback");
        public void SendDisconnect() => Trace.Add("send disconnect");
        public void StopCongestionControl() => Trace.Add("stop congestion");
        public void CloseTakion() => Trace.Add("close takion");
        public void LiftStages() => Trace.Add("lift stages");
        public void FreeVideoReceiver() => Trace.Add("free video");
        public void FreeHapticsReceiver() => Trace.Add("free haptics");
        public void FreeAudioReceiver() => Trace.Add("free audio");
    }

    private static int At(List<string> trace, string what) => trace.IndexOf(what);

    private static bool Before(List<string> trace, string first, string then)
        => At(trace, first) >= 0 && At(trace, then) > At(trace, first);

    /// <summary>
    /// A clean session: every ordering holds on the one path the C takes when nothing fails.
    ///
    /// All six on one trace, because that is what the C does - each is a fact about a single
    /// sequence, and asserting them separately would let a run satisfy each on a different path.
    /// </summary>
    [Fact]
    public void ACleanRunKeepsAllSixOrderings()
    {
        var host = new Scripted();

        ChiakiError code = ManagedStreamRun.Run(host);
        foreach (string step in host.Trace)
            output.WriteLine(step);

        Assert.Equal(ChiakiError.Success, code);
        List<string> t = host.Trace;

        // 1: created audio, haptics, video; freed video, haptics, audio.
        Assert.True(Before(t, "create audio", "create haptics") && Before(t, "create haptics", "create video"));
        Assert.True(Before(t, "free video", "free haptics") && Before(t, "free haptics", "free audio"));

        // 3: CONNECTED between an unlock and a lock.
        int connected = At(t, "send connected");
        Assert.Equal("unlock", t[connected - 1]);
        Assert.Equal("lock", t[connected + 1]);

        // 4: input_to_wire before the feedback fini.
        Assert.True(Before(t, "lift input_to_wire", "fini feedback"));

        // 5: stages after the close and before the video free.
        Assert.True(Before(t, "close takion", "lift stages") && Before(t, "lift stages", "free video"));

        // 6: the disconnect is sent, from the label, before the cascade.
        Assert.True(Before(t, "send disconnect", "stop congestion"));
    }

    /// <summary>
    /// Ordering 2: an early streaminfo is replayed before the wait, and the wait is skipped when the
    /// replay finished the state.
    ///
    /// Both halves. Draining and waiting anyway would time out on a message already handled, which
    /// is the deadlock the buffer exists to prevent.
    /// </summary>
    [Fact]
    public void AnEarlyStreaminfoIsReplayedAndTheWaitSkipped()
    {
        var host = new Scripted { EarlyStreaminfo = true, AfterReplay = new(Finished: true) };

        ManagedStreamRun.Run(host);

        Assert.Contains("replay early streaminfo", host.Trace);
        Assert.DoesNotContain("wait ExpectStreaminfo", host.Trace);
    }

    /// <summary>And a replay that did NOT finish the state still waits, as the C's guard says.</summary>
    [Fact]
    public void AReplayThatDidNotFinishStillWaits()
    {
        var host = new Scripted { EarlyStreaminfo = true, AfterReplay = new(Finished: false) };

        ManagedStreamRun.Run(host);

        Assert.True(Before(host.Trace, "replay early streaminfo", "wait ExpectStreaminfo"));
    }

    /// <summary>
    /// Every failure enters the cascade at the C's own goto, and frees exactly what was built.
    ///
    /// The rung that found the table wrong is the takion connect: the old table entered at
    /// CloseTakion and would have closed a takion that never came up. Each row here is one failure
    /// turned on, and the assertion is what the trace does and does not contain.
    /// </summary>
    [Theory]
    [InlineData("haptics", new[] { "free audio" }, new[] { "free haptics", "free video", "close takion" })]
    [InlineData("video", new[] { "free haptics", "free audio" }, new[] { "free video", "close takion" })]
    [InlineData("takion", new[] { "lift stages", "free video", "free haptics", "free audio" }, new[] { "close takion", "stop congestion" })]
    [InlineData("congestion", new[] { "close takion", "free video", "free audio" }, new[] { "stop congestion", "send disconnect" })]
    public void AFailureUnwindsExactlyWhatWasBuilt(string fails, string[] expected, string[] absent)
    {
        var host = new Scripted();
        switch (fails)
        {
            case "haptics": host.Haptics = false; break;
            case "video": host.Video = false; break;
            case "takion": host.Takion = false; break;
            case "congestion": host.Congestion = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(fails));
        }

        ChiakiError code = ManagedStreamRun.Run(host);
        output.WriteLine(string.Join(" > ", host.Trace));

        Assert.Equal(ChiakiError.Unknown, code);
        foreach (string step in expected)
            Assert.Contains(step, host.Trace);
        foreach (string step in absent)
            Assert.DoesNotContain(step, host.Trace);
    }

    /// <summary>
    /// The audio receiver failing has no label: the C unlocks and returns, freeing nothing.
    /// </summary>
    [Fact]
    public void AnAudioReceiverFailureReturnsWithoutTheCascade()
    {
        var host = new Scripted { Audio = false };

        Assert.Equal(ChiakiError.Unknown, ManagedStreamRun.Run(host));
        Assert.Equal(["lock", "create audio", "unlock"], host.Trace);
    }

    /// <summary>
    /// Ordering 6 on a failure path: a bang that never arrives still sends the disconnect.
    ///
    /// This is the reason the send is on the label. A disconnect that never reached the console is
    /// why the NEXT session is refused with "RP in use", and that happens on the paths that FAILED.
    /// </summary>
    [Fact]
    public void AMissingBangStillTellsTheConsole()
    {
        var host = new Scripted { BangFlags = new(Finished: false), TimedOut = true };

        ChiakiError code = ManagedStreamRun.Run(host);

        Assert.Equal(ChiakiError.Unknown, code);
        Assert.Contains("send disconnect", host.Trace);
        Assert.DoesNotContain("send connected", host.Trace);
    }

    /// <summary>The run's code is decided at the label in the C's order: a stop beats a remote disconnect.</summary>
    [Theory]
    [InlineData(true, true, ChiakiError.Canceled)]
    [InlineData(false, true, ChiakiError.Disconnected)]
    [InlineData(false, false, ChiakiError.Success)]
    public void TheOutcomeIsDecidedAtTheLabel(bool stop, bool remote, ChiakiError expected)
    {
        var host = new Scripted { ShouldStop = stop, RemoteDisconnected = remote };

        Assert.Equal(expected, ManagedStreamRun.Run(host));
    }

    /// <summary>A heartbeat that fails is ignored and the loop carries on, as PP363 says.</summary>
    [Fact]
    public void AFailedHeartbeatDoesNotEndTheStream()
    {
        var host = new Scripted
        {
            IdleWaits = new([ChiakiError.Timeout, ChiakiError.Timeout, ChiakiError.Success]),
        };

        ManagedStreamRun.Run(host);

        Assert.Equal(2, host.Trace.Count(step => step == "send heartbeat"));
        Assert.Contains("send connected", host.Trace);
    }

    /// <summary>
    /// The corrected entry table agrees with the C's own gotos, rung by rung.
    ///
    /// This is what makes the correction a check rather than a second opinion: the gotos are read
    /// out of the file in the order the failures can happen, and each is what the table says.
    /// </summary>
    [Fact]
    public void TheEntryTableMatchesTheGotosInTheC()
    {
        if (StreamTeardownSource.Locate() is not { } path)
            return;

        string? run = StreamTeardownSource.RunBody(path);
        Assert.NotNull(run);

        IReadOnlyList<string> gotos = StreamTeardownSource.GotoTargetsBeforeTheFirstLabel(run);
        output.WriteLine(string.Join(", ", gotos));

        foreach ((StreamBuilt built, string target) in StreamTeardown.GotosByRung)
        {
            Assert.Contains(target, gotos);
            Assert.Equal(StreamTeardown.LabelOf(target), StreamTeardown.EntryAfter(built));
        }
    
}

    /// <summary>
    /// PP770: THE RUN SAYS HOW FAR IT GOT, which the error alone never did.
    ///
    /// Every rung's failure leaves by the same cascade with the same code, so a run that failed at
    /// the connect and one that failed at the BIG were the same sentence. Found the expensive way: a
    /// live handover reached the run, answered Unknown, and locating that one word would have cost
    /// another rebuild and another console.
    ///
    /// NOT A NEW CODE PER STEP - the codes are the C's. What this carries out is the value each exit
    /// already hands the teardown, carried out instead of only down.
    /// </summary>
    [Theory]
    [InlineData("audio", StreamBuilt.Nothing)]
    [InlineData("haptics", StreamBuilt.AudioReceiver)]
    [InlineData("video", StreamBuilt.HapticsReceiver)]
    [InlineData("takion", StreamBuilt.VideoReceiver)]
    [InlineData("congestion", StreamBuilt.Takion)]
    public void TheRunSaysWhichRungItStoppedAt(string fails, StreamBuilt expected)
    {
        var host = new Scripted();
        switch (fails)
        {
            case "audio": host.Audio = false; break;
            case "haptics": host.Haptics = false; break;
            case "video": host.Video = false; break;
            case "takion": host.Takion = false; break;
            default: host.Congestion = false; break;
        }

        ChiakiError error = ManagedStreamRun.Run(host, out StreamBuilt reached);

        output.WriteLine($"{fails} failed: reached {reached}, error {error}");

        // Reported as the rung BEFORE the one that failed, which is what "how far it got" means and
        // is the same value the cascade frees against.
        Assert.Equal(expected, reached);

        // And the code is the same for all five, which is exactly why it was never enough.
        Assert.Equal(ChiakiError.Unknown, error);
    }

    /// <summary>
    /// And the one-argument overload answers what it always did.
    ///
    /// Every caller outside this task passes a host and reads a code; making them all pass an
    /// argument they do not want would be this task charging for itself.
    /// </summary>
    [Fact]
    public void TheOldShapeStillAnswersTheCode()
    {
        var host = new Scripted { Takion = false };
        Assert.Equal(ChiakiError.Unknown, ManagedStreamRun.Run(host));
    }
}