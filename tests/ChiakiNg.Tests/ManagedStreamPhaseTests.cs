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
            new IPEndPoint(IPAddress.Loopback, 9295),
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
}
