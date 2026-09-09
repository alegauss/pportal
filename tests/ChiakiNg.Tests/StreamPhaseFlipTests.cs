using System.Net;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP783: the stream phase handed to the port, with the reason PP696 was reverted answered.
///
/// PP696 made session.c's run a callback and nothing installed one, so a live session reached the
/// stream phase and stopped. PP763 put the C's run back and PP764 wrote the check that names the
/// four states, which is what these read against a real checkout.
///
/// WHAT WAS MISSING IS THERE NOW: PP762's composition root, PP773's arrivals and receive thread,
/// PP777's BIG material, PP787's second door into the decoder, PP795's AV arm and PP799's listener.
/// So the tree moves off TheC, and it does not stop at Both - the C's run wins wherever both exist,
/// because session.c is the caller, and a port that thought it had taken over would be wrong about
/// which code was running with nothing to say so.
///
/// THESE DO NOT PROVE A RUN WORKS, and PP763's whole lesson is that a passing suite cannot. They
/// prove the two files that between them decide now say the same thing. The console is the gate.
/// </summary>
public class StreamPhaseFlipTests(ITestOutputHelper output)
{
    private static string SessionSource()
        => File.ReadAllText(
            StreamPhaseDriver.LocateSession()
                ?? throw new InvalidOperationException("session.c is not in this checkout."));

    /// <summary>
    /// THE DRIVER IS THE PORT, which is the whole of what this task changes.
    ///
    /// PP764's own check refuses only Nobody, because a tree mid-handover is a real state somebody
    /// may be standing in. This one is the claim PP783 makes and it is stricter by two states: not
    /// TheC, which is where the tree sat, and not Both, which is not a state to ship.
    /// </summary>
    [Fact]
    public void TheDriverIsThePortAndNeitherOfTheTwoStatesEitherSideOfIt()
    {
        if (StreamPhaseDriver.LocateSession() is null)
            return;

        IReadOnlyList<(string Name, string Text)> app = StreamPhaseDriver.AppFiles();

        // PP271: a sweep that read no files would answer about a tree it never looked at.
        Assert.True(app.Count > 100, $"only {app.Count} app files were read, so this is not a sweep");

        StreamDriver driver = StreamPhaseDriver.DriverOf(SessionSource(), app);
        output.WriteLine($"{app.Count} app file(s), driver: {driver}");

        Assert.Equal(StreamDriver.ThePort, driver);
    }

    /// <summary>
    /// AND BOTH CALLBACKS ARE CALLED, because a run taken over and a stop that was not is a hang.
    ///
    /// Stopping is four wake-ups and not a flag: the session thread can be blocked in a condition
    /// wait, in a socket select, or down in the stream phase, and the third of those is now on the
    /// far side of a callback. A session that flipped the run and kept poking the C's stream
    /// connection would wedge at exactly the moment somebody quits a live stream - the one moment
    /// chiaki_session_stop is ever called, and one no offline gate reaches.
    /// </summary>
    [Fact]
    public void SessionCReachesTheRunAndTheStopItWasGiven()
    {
        if (StreamPhaseDriver.LocateSession() is null)
            return;

        string code = CCall.Code(SessionSource());

        Assert.Contains("session->stream_run_cb(", code, StringComparison.Ordinal);
        Assert.Contains("session->stream_stop_cb(", code, StringComparison.Ordinal);

        // And the C's own pair is gone from this file, which is what keeps the state off Both.
        Assert.DoesNotContain(StreamPhaseDriver.TheCsRun, code, StringComparison.Ordinal);
        Assert.DoesNotContain("chiaki_stream_connection_stop(", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// A SESSION WITH NO PHASE INSTALLED FAILS BY NAME, rather than falling back to the C.
    ///
    /// Keeping a fallback is the tempting shape and it is the one PP764 refuses: the C's branch is
    /// the one that would run, so the tree would sit in Both while reading as handed over. The
    /// honest answer to "nobody took the stream" is an error the quit reason carries, which is what
    /// PP696's own state should have been instead of a session that reached the phase and idled.
    /// </summary>
    [Fact]
    public void NoPhaseInstalledIsAnErrorAndNotAFallback()
    {
        if (StreamPhaseDriver.LocateSession() is null)
            return;

        string code = CCall.Code(SessionSource());

        Assert.Contains("CHIAKI_ERR_UNINITIALIZED", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND THE PATH THAT OPENS A SESSION IS THE ONE THAT INSTALLS, which is PP767's question.
    ///
    /// An install anywhere says a composition root exists; an install on a path that BUILDS A
    /// SESSION says one is actually reached. PP762 made the difference visible by adding the first
    /// without the second, and this asserts which file crossed the gap.
    /// </summary>
    [Fact]
    public void TheWindowsRunIsWhatDrivesIt()
    {
        IReadOnlyList<string> driving =
            StreamPhaseDriver.DrivingSessionPathsIn(StreamPhaseDriver.AppFiles());

        output.WriteLine(string.Join(", ", driving.Select(Path.GetFileName)));

        Assert.Contains(
            driving,
            one => Path.GetFileName(one).Equals("StreamRun.cs", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// AND SESSION.C HAS STOPPED ASKING ALTOGETHER, which is what PP295's fourth criterion calls
    /// the one edit.
    ///
    /// The run and the stop are not separable from the init: chiaki_session_set_controller_state
    /// takes feedback_sender_mutex, and after the flip that lock belongs to a struct no init ever
    /// set up. So a flip that moved two of the five calls would leave one read that is not merely
    /// dead but wrong - and the census names the halfway state Partial for exactly this reason.
    ///
    /// THE FILES HAVE LEFT NOW TOO, and PP27 is what took them. This assertion held the other half
    /// for a block - the consumer stopped and the deletion waited - and the wait ended when the
    /// linker showed the two deletions were one: videoreceiver.c is the only caller of two symbols
    /// takion's removal strands, so the frame path could not stay behind.
    /// </summary>
    [Fact]
    public void SessionCHasStoppedAskingAndTheFilesHaveLeft()
    {
        if (FramePathConsumers.Locate(FramePathConsumers.SessionRelativePath) is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.Equal(ConsumerShape.Silent, FramePathConsumers.ShapeOf(ConsumerKind.Session, source));
        Assert.True(
            FramePathConsumers.WasActuallyRead(ConsumerKind.Session, source),
            "session.c calls none of them and holds none of what survives the flip either");

        if (FramePathConsumers.Locate(@"lib\CMakeLists.txt") is not { } cmake)
            return;

        // And the list agrees, which is the half that turned over. Silent here means lib's own build
        // file names none of them - the deletion PP295's criterion described, landed under PP27.
        Assert.Equal(
            ConsumerShape.Silent,
            StreamConnectionConsumers.ShapeOfTheList(File.ReadAllText(cmake)));
    }

    /// <summary>
    /// AND THE PAD REACHES THE PORT'S SENDER, which is the half no frame count would report.
    ///
    /// chiaki_session_set_controller_state now stores the state and forwards it nowhere, so a client
    /// still pushing at the C session would show a picture and send no input. PP756 wrote the
    /// managed sink for this and nothing had ever built one - the seam PP800 is about, met a second
    /// time in the commit that made it matter.
    ///
    /// PP845: THE SPELLING WAS THE CHECK AND IT SHOULD NOT HAVE BEEN. This asked for the exact text
    /// `new PadFeed(phase.Pad)`, so giving the feed a second argument - the console's touchpad -
    /// broke a check about which SINK the pad pushes at. That is PP844's finding one file over: a
    /// check keyed on furniture rather than on its subject. What it asks now is that the first
    /// argument is the phase's sink, whatever follows it.
    /// </summary>
    [Fact]
    public void ThePadPushesAtThePortsSinkAndNotAtTheCSession()
    {
        if (SanitizerSource.LocateRelative(@"app\Session\StreamRun.cs") is not { } path)
            return;

        string code = DeadAssertions.CodeOnly(File.ReadAllText(path));

        Assert.Contains("new PadFeed(phase.Pad", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new PadFeed(session", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND IT IS THERE TO PUSH AT BEFORE THE RUN IS, which is the C's own split.
    ///
    /// The session holds controller_state from the moment it exists and the sender is handed it when
    /// one starts, so a pad opened before the console answered has somewhere to go. A sink built
    /// inside the run would drop every push made before the stream came up, and the console would
    /// open on whatever the pad was doing afterwards rather than on what it is doing now - which is
    /// the behaviour PP756 read out of the C and the reason its hold is not optional.
    /// </summary>
    [Fact]
    public void ThePadHasSomewhereToPushBeforeTheConsoleHasAnswered()
    {
        ChiakiSession.LibInit();

        using var info = new ChiakiConnectInfo { Host = "127.0.0.1", Ps5 = true };
        info.SetRegistKey(new byte[16]);
        info.SetMorning(new byte[16]);
        info.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);

        using ChiakiSession? session = ChiakiSession.TryCreate(info, null, out _);
        if (session is null)
            return;

        using var baseline = new SessionBaseline();
        using var phase = new ManagedStreamPhase(session, IPAddress.Loopback, (_, _, _) => true, baseline);

        // Nothing has run, so no sender is armed - and a push is still a success that is kept.
        Assert.False(phase.Pad.Active);

        using var state = new ChiakiControllerState();
        state.SetIdle();

        Assert.Equal(ChiakiError.Success, phase.Pad.SetControllerState(state));
    }

    /// <summary>
    /// AND THE UNDRIVEN DOOR INTO THE DECODER IS GONE FROM THAT PATH.
    ///
    /// SessionDecoder.AttachTo installs the sink on the SESSION, so what decodes through it is the
    /// C's run calling the C's callback - and the C no longer runs the stream. PP787 opened the
    /// second door for this: the phase takes decoder.Sample and feeds the same decoder from the
    /// managed receiver. Leaving both would be two doors into one decoder with only one of them
    /// driven, which is the two-facts-in-two-files shape PP764 exists about.
    /// </summary>
    [Fact]
    public void ThePathFeedsTheDecoderThroughTheDoorTheRunActuallyUses()
    {
        if (SanitizerSource.LocateRelative(@"app\Session\StreamRun.cs") is not { } path)
            return;

        string code = DeadAssertions.CodeOnly(File.ReadAllText(path));

        Assert.Contains("decoder.Sample", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionDecoder.AttachTo", code, StringComparison.Ordinal);
    }
}
