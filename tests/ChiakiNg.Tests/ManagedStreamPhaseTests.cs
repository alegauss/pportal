using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP762: the composition root, driven from where session.c will drive it.
///
/// PP696 replaced the C's run with a callback and nothing installed one, so a live session reached
/// the stream phase and stopped. Every piece existed and no file put them together.
///
/// THIS CANNOT BE PROVEN END TO END ON THIS MACHINE, and saying so is the point. The C still runs
/// the stream (PP763 put it back), so the callback is installed and never called; and a run that
/// did start would want a console. What IS provable is the half that cost the revert: the parts
/// compose, the thread goes up before the install, and the BIG refuses by name rather than sending
/// a console four empty fields.
/// </summary>
public class ManagedStreamPhaseTests(ITestOutputHelper output)
{
    private static ChiakiSession? Build()
    {
        ChiakiSession.LibInit();

        using var info = new ChiakiConnectInfo { Host = "127.0.0.1", Ps5 = true };
        info.SetRegistKey(new byte[16]);
        info.SetMorning(new byte[16]);
        info.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);

        return ChiakiSession.TryCreate(info, null, out _);
    }

    /// <summary>
    /// THE BIG REFUSES BY NAME, which is the failure a live run has to survive legibly.
    ///
    /// Its four arguments arrive at four different moments and the last lives only across the run,
    /// so a factory evaluated a step early has nothing. Each refusal says WHICH thing is missing:
    /// the alternative - zeroes and an empty id - is a message a console rejects with nothing said
    /// about why, which is the shape of failure this port keeps refusing to ship.
    /// </summary>
    [Fact]
    public void TheBigSaysWhichPieceIsMissingRatherThanSendingEmptyFields()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => ManagedStreamPhase.Big(session));

        output.WriteLine(refused.Message);

        // The id is the first thing ctrl produces, so it is the first thing missing.
        Assert.Contains("id", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A START THAT NEVER COMES BUILDS NO HOST, which is PP754's rule reaching the composition root.
    ///
    /// The C is still running the stream on this tree, so an installed handover is never started -
    /// exactly the state this asserts. The runner waits its window and reports Started false, and
    /// nothing was constructed: building a host takes a socket, and a session that never handed over
    /// should not have opened one.
    /// </summary>
    [Fact]
    public void AnInstalledPhaseThatIsNeverStartedBuildsNothing()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        using var baseline = new SessionBaseline();
        using var phase = new ManagedStreamPhase(
            session,
            IPAddress.Loopback,
            (_, _, _) => true,
            baseline);

        phase.InstallOn();

        // Installing twice is a second thread over one handover, which is a race rather than a
        // second attempt.
        Assert.Throws<InvalidOperationException>(phase.InstallOn);

        Assert.Null(phase.Outcome);
        Assert.False(phase.Stopped);
    }

    /// <summary>
    /// PP768: DISPOSING ENDS THE WAIT BEFORE IT FREES WHAT IS BEING WAITED ON.
    ///
    /// The first version freed the handover while the runner's thread was inside await_start on it,
    /// and a wait on freed memory fails when it feels like it: three runs of the gate gave one
    /// truncated run and two clean ones. The phase's own tests passed every time, because the
    /// process exited before the thread noticed - which is why this asserts the thread has ENDED
    /// rather than that nothing crashed.
    /// </summary>
    [Fact]
    public void DisposingCancelsTheWaitAndTheRunnerAnswersCancelled()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        using var baseline = new SessionBaseline();
        var phase = new ManagedStreamPhase(
            session,
            IPAddress.Loopback,
            (_, _, _) => true,
            baseline);

        phase.InstallOn();
        phase.Dispose();

        // The thread is gone, which is what makes the free that follows it safe.
        Assert.True(phase.Join(TimeSpan.FromSeconds(2)));

        // And it answered rather than being killed: a cancelled wait is not a start, so no host was
        // built and no socket was opened for a phase that was going away.
        StreamRunnerOutcome outcome = Assert.NotNull(phase.Outcome);
        Assert.False(outcome.Started);
        Assert.Equal(ChiakiError.Canceled, outcome.Error);

        output.WriteLine($"outcome: started={outcome.Started} error={outcome.Error}");
    }

    /// <summary>
    /// And the phase is what PP764's check sees, which is the join between the two.
    ///
    /// PP764 refuses a tree where session.c hands over and nothing installs. This is the file that
    /// makes the second half true, so a rename that broke the join would leave that check reporting
    /// a driver this tree does not have.
    /// </summary>
    [Fact]
    public void ThePhaseIsWhatTheDriverCheckCounts()
    {
        (string Name, string Text) file = (
            @"app\Session\ManagedStreamPhase.cs",
            File.ReadAllText(
                SanitizerSource.LocateRelative(@"app\Session\ManagedStreamPhase.cs")
                ?? throw new InvalidOperationException("the phase is not in this checkout.")));

        Assert.Single(StreamPhaseDriver.InstallersIn([file]));
    }

    /// <summary>
    /// PP808: the root joins the takion's disconnect to the arrivals, which is a call and not a type.
    ///
    /// Read out of the source for the reason the check above is: <c>Build</c> is private and takes a
    /// session, so the join is only reachable from a start that wants a console. What was missing is
    /// a CALL, and PP764's lesson is that a census counting types cannot see one.
    ///
    /// Anchored at the start of a line, so the prose either side of the assignment - which discusses
    /// it at length - cannot be what satisfies this.
    /// </summary>
    [Fact]
    public void TheRootJoinsTheTakionsDisconnectToTheArrivals()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\ManagedStreamPhase.cs")
            ?? throw new InvalidOperationException("the phase is not in this checkout."));

        Assert.Matches(@"(?m)^\s*takion\.Disconnected\s*=\s*\(\)\s*=>\s*arrivals", text);
    }

    /// <summary>
    /// PP824: and the root hands the arrivals the takion's ack sink, which PP807 left out.
    ///
    /// The seam is PP807's and the assertion it carries is about ROUTING - StreamArrivals acks
    /// whatever it was given, and a null sink acknowledges nothing without failing. So the only
    /// thing that says this port answers a console's data messages is the composition root
    /// assigning one, and that is the line three live runs took out and two tasks put back.
    ///
    /// Read out of the source for PP808's reason: <c>Build</c> is private and takes a session, so
    /// the join is reachable only from a start that wants a console. Anchored at the start of a
    /// line, so the paragraphs above it - which name <c>Acks</c> while arguing about it - cannot be
    /// what satisfies this.
    /// </summary>
    [Fact]
    public void TheRootHandsTheArrivalsTheTakionsAckSink()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\ManagedStreamPhase.cs")
            ?? throw new InvalidOperationException("the phase is not in this checkout."));

        Assert.Matches(@"(?m)^\s*Acks\s*=\s*takion\s*,", text);

        // And it is the takion itself and not a second sink: the queue the ack's rule reads is the
        // one the receive loop pushes into, so anything else would acknowledge a different ledger.
        Assert.True(typeof(IDataAckSink).IsAssignableFrom(typeof(ManagedTakion)));
    }

    /// <summary>
    /// PP771: THE PHASE AIMS AT THE STREAM PORT, which the caller no longer gets to choose.
    ///
    /// The first version took an endpoint and the first caller passed 9295 - the ctrl and discovery
    /// port, which is the number everything about a session says. A console answers nothing on it:
    /// three INIT attempts, no reply, and a run that stopped at the connect looking like a handshake
    /// this port had got wrong. Aimed at 9296 the same handshake completed with a real PS5 on the
    /// first attempt each way.
    ///
    /// So the constructor takes an ADDRESS. The mistake is not one a caller can make any more, which
    /// is the only kind of fix worth making for a mistake that cost a console trial to find.
    /// </summary>
    [Fact]
    public void ThePhaseTakesAnAddressAndAimsAtTheStreamPort()
    {
        // An address, not an endpoint: there is no parameter to put the wrong number in.
        Assert.Equal(
            typeof(IPAddress),
            typeof(ManagedStreamPhase).GetConstructors().Single().GetParameters()[1].ParameterType);

        // And the port it uses is the stream takion's, which is not the ctrl one.
        Assert.Equal(9296, SessionRelay.StreamPort);
        Assert.NotEqual(CtrlConnect.CtrlPort, SessionRelay.StreamPort);
    }
}
