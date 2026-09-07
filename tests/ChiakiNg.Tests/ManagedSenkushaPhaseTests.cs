using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP809, under PP784: the composition root, driven from where session.c drives it.
///
/// PP792 gave session.c a callback for senkusha and PP805 wired the arrivals; nothing constructed a
/// phase, so every session took the other arm. That is PP762's state one phase earlier, and it
/// shipped green there because a census counting types cannot see a missing call.
///
/// WHAT IS PROVABLE HERE IS THE HALF THAT COST THE REVERT: the parts compose, the thread goes up
/// before the install, and a wait that is cancelled builds no host. The run itself wants a console.
/// </summary>
public class ManagedSenkushaPhaseTests(ITestOutputHelper output)
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
    /// AN INSTALLED PHASE THAT IS NEVER STARTED BUILDS NOTHING, which is PP754's rule at this root.
    ///
    /// Nothing in a gate reaches the senkusha phase, so an installed handover is never started -
    /// exactly the state this asserts. The runner waits its window and nothing was constructed:
    /// building a host opens a socket, and senkusha's would be a second one on a machine that is
    /// already holding the stream's.
    /// </summary>
    [Fact]
    public void AnInstalledPhaseThatIsNeverStartedBuildsNothing()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        using var phase = new ManagedSenkushaPhase(session, IPAddress.Loopback);

        phase.InstallOn();

        // Installing twice is a second thread over one handover, which is a race rather than a
        // second attempt.
        Assert.Throws<InvalidOperationException>(phase.InstallOn);

        Assert.Null(phase.Outcome);
        Assert.Null(phase.Host);
        Assert.Null(phase.Router);
        Assert.False(phase.Stopped);
    }

    /// <summary>
    /// PP768: DISPOSING ENDS THE WAIT BEFORE IT FREES WHAT IS BEING WAITED ON.
    ///
    /// The order the stream's phase paid for: a handover freed under a live waiter runs a wait on
    /// memory the allocator has taken back, and it fails when it feels like it rather than reliably.
    /// So this asserts the thread has ENDED, and that it answered rather than being killed.
    /// </summary>
    [Fact]
    public void DisposingCancelsTheWaitAndTheRunnerAnswersCancelled()
    {
        using ChiakiSession? session = Build();
        if (session is null)
            return;

        var phase = new ManagedSenkushaPhase(session, IPAddress.Loopback);

        phase.InstallOn();
        phase.Dispose();

        Assert.True(phase.Join(TimeSpan.FromSeconds(2)));

        SenkushaRunnerOutcome outcome = Assert.NotNull(phase.Outcome);

        output.WriteLine($"outcome: started={outcome.Started} error={outcome.Error}");

        Assert.False(outcome.Started);
        Assert.Equal(ChiakiError.Canceled, outcome.Error);

        // And no host was built, so no socket was opened for a phase that was going away.
        Assert.Null(phase.Host);
    }

    /// <summary>
    /// The port senkusha dials is 9297 and it is TAKEN rather than asked for.
    ///
    /// PP771 paid for this on the other seam: a run aimed at the wrong number got three unanswered
    /// INIT attempts and a connect that read like a handshake this port had got wrong. A console
    /// answers nothing at all on a port it is not listening on, so the three live together.
    /// </summary>
    [Fact]
    public void SenkushaDialsItsOwnPortAndNotTheStreams()
    {
        Assert.Equal(9297, SessionRelay.SenkushaPort);
        Assert.NotEqual(SessionRelay.StreamPort, SessionRelay.SenkushaPort);
        Assert.NotEqual(SessionRelay.ControlPort, SessionRelay.SenkushaPort);
    }
}
