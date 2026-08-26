using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP364, under PP295: the six-label cascade, and the numbers rescued between two of them.
///
/// None of this is observable from outside. A teardown that freed the video receiver one statement
/// early returns the same code, ends the same session, and produces a baseline of plausible zeros -
/// so every assertion here is a reading of the file, and the one thing that can be RUN is the
/// property that makes the reading necessary: nothing distinguishes a lost measurement from a
/// measured zero.
/// </summary>
public class StreamTeardownTests
{
    private static string? Run()
    {
        string? path = StreamTeardownSource.Locate();
        return path is null ? null : StreamTeardownSource.RunBody(path);
    }

    /// <summary>An entry point runs everything from itself down, and nothing above it.</summary>
    [Fact]
    public void AnEntryPointRunsEverythingBelowIt()
    {
        Assert.Equal(
            [StreamExitLabel.VideoReceiver, StreamExitLabel.HapticsReceiver, StreamExitLabel.AudioReceiver],
            StreamTeardown.From(StreamExitLabel.VideoReceiver));

        Assert.Equal([StreamExitLabel.AudioReceiver], StreamTeardown.From(StreamExitLabel.AudioReceiver));
        Assert.Equal(6, StreamTeardown.From(StreamExitLabel.Disconnect).Count);
    }

    /// <summary>
    /// And a failure releases exactly what was built, which is the whole point of six labels.
    ///
    /// The ladder read the other way: each thing that came up moves the entry one label earlier.
    /// </summary>
    [Theory]
    [InlineData(StreamBuilt.Nothing, StreamExitLabel.AudioReceiver)]
    [InlineData(StreamBuilt.AudioReceiver, StreamExitLabel.HapticsReceiver)]
    [InlineData(StreamBuilt.HapticsReceiver, StreamExitLabel.VideoReceiver)]
    [InlineData(StreamBuilt.VideoReceiver, StreamExitLabel.CloseTakion)]
    [InlineData(StreamBuilt.Takion, StreamExitLabel.CongestionControl)]
    [InlineData(StreamBuilt.CongestionControl, StreamExitLabel.Disconnect)]
    public void AFailureEntersWhereWhatItBuiltIsReleased(StreamBuilt built, StreamExitLabel entry)
    {
        Assert.Equal(entry, StreamTeardown.EntryAfter(built));
    }

    /// <summary>
    /// A failure never releases something that was never built - stated as a property over the
    /// whole ladder rather than trusted from the table above.
    /// </summary>
    [Fact]
    public void NothingUnbuiltIsEverReleased()
    {
        // Nothing built at all still runs one label: freeing a NULL audio receiver is a no-op, and
        // that is how the C is written rather than an oversight this port should improve on.
        Assert.Single(StreamTeardown.From(StreamTeardown.EntryAfter(StreamBuilt.Nothing)));

        foreach (StreamBuilt built in Enum.GetValues<StreamBuilt>())
        {
            IReadOnlyList<StreamExitLabel> run = StreamTeardown.From(StreamTeardown.EntryAfter(built));

            // One label per thing built, plus the audio one that always runs.
            Assert.Equal((int)built + 1, run.Count);
        }
    }

    /// <summary>
    /// THE PROPERTY THAT MAKES THE ORDER MATTER. A measurement lost by freeing first is
    /// indistinguishable from one that measured zero.
    /// </summary>
    [Fact]
    public void ALostMeasurementLooksLikeAMeasurement()
    {
        Assert.False(StreamTeardown.ALostMeasurementIsDistinguishable);
    }

    /// <summary>Both rescues are named, with what destroys the object each is lifted from.</summary>
    [Fact]
    public void EveryLiftedMeasurementNamesWhatDestroysItsSource()
    {
        Assert.Equal(5, StreamTeardown.Lifted.Count);

        Assert.Contains(
            StreamTeardown.Lifted,
            m => m.Measurement == "input_to_wire" && m.DestroyedBy == "chiaki_feedback_sender_fini");

        Assert.Equal(
            4, StreamTeardown.Lifted.Count(m => m.Measurement.StartsWith("stages.", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The takion counters may only be read once the close has joined the thread that writes them,
    /// which is why the two labels are adjacent.
    /// </summary>
    [Fact]
    public void TheTakionCountersAreOnlyReadAfterTheClose()
    {
        Assert.False(StreamTeardown.TakionCountersAreSafeToRead(StreamExitLabel.Disconnect));
        Assert.False(StreamTeardown.TakionCountersAreSafeToRead(StreamExitLabel.CloseTakion));

        Assert.True(StreamTeardown.TakionCountersAreSafeToRead(StreamExitLabel.VideoReceiver));
    }

    /// <summary>
    /// The early streaminfo buffer outlives the replay on every path that never reached it, which
    /// is why the label frees it as well.
    /// </summary>
    [Fact]
    public void TheEarlyBufferSurvivesEveryFailureBeforeItsReplay()
    {
        Assert.True(StreamTeardown.TheEarlyBufferOutlivesTheReplay(StreamState.TakionConnect));
        Assert.True(StreamTeardown.TheEarlyBufferOutlivesTheReplay(StreamState.ExpectBang));

        Assert.False(StreamTeardown.TheEarlyBufferOutlivesTheReplay(StreamState.ExpectStreaminfo));
    }

    /// <summary>And streamconnection.c still cascades, and still rescues both numbers.</summary>
    [Fact]
    public void TheRunStillTearsDownThisWay()
    {
        if (Run() is not { } run)
            return;

        Assert.True(
            StreamTeardownSource.TheSixLabelsAreStillInOrder(run),
            "the six exit labels are no longer in cascade order");
        Assert.True(
            StreamTeardownSource.TheCascadeStillFallsThrough(run),
            "something returns before the last label, so everything below it leaks");
        Assert.True(
            StreamTeardownSource.TheStageTimingsAreLiftedBetweenCloseAndFree(run),
            "the frame-path timings are no longer read between takion_close and the receiver's free");
        Assert.True(
            StreamTeardownSource.InputToWireIsLiftedBeforeTheFini(run),
            "input_to_wire is no longer copied out before the feedback sender's fini");
        Assert.True(
            StreamTeardownSource.TheEarlyBufferIsStillFreedAtTheLabel(run),
            "the disconnect label no longer frees the early streaminfo buffer");
    }

    /// <summary>
    /// And every construction failure still jumps to the label this port says it does.
    ///
    /// Read off the gotos rather than asserted as a count: a target that moved is the defect, and
    /// naming which one moved is what a failure here should say.
    /// </summary>
    [Fact]
    public void EveryFailureStillJumpsWhereThePortSaysItDoes()
    {
        if (Run() is not { } run)
            return;

        IReadOnlyList<string> targets = StreamTeardownSource.GotoTargetsBeforeTheFirstLabel(run);

        Assert.Contains("err_audio_receiver", targets);
        Assert.Contains("err_haptics_receiver", targets);
        Assert.Contains("err_video_receiver", targets);
        Assert.Contains("close_takion", targets);
        Assert.Contains("err_congestion_control", targets);
        Assert.Contains("disconnect", targets);
    }

    /// <summary>The readers read the file (PP272), and see the edits they were written for.</summary>
    [Fact]
    public void TheReadersSeeTheEditsTheyGuardAgainst()
    {
        Assert.False(StreamTeardownSource.TheSixLabelsAreStillInOrder(""));
        Assert.False(StreamTeardownSource.TheCascadeStillFallsThrough(""));
        Assert.False(StreamTeardownSource.TheStageTimingsAreLiftedBetweenCloseAndFree(""));
        Assert.False(StreamTeardownSource.InputToWireIsLiftedBeforeTheFini(""));
        Assert.False(StreamTeardownSource.TheEarlyBufferIsStillFreedAtTheLabel(""));
        Assert.Empty(StreamTeardownSource.GotoTargetsBeforeTheFirstLabel(""));

        // The edit that loses the numbers: free first, lift after.
        const string FreedFirst = """
            close_takion:
            	chiaki_takion_close(&stream_connection->takion);
            err_video_receiver:
            	chiaki_video_receiver_free(stream_connection->video_receiver);
            	stream_connection->stages.receive = stream_connection->takion.stage_receive;
            	stream_connection->stages.reorder = stream_connection->takion.stage_reorder;
            	stream_connection->stages.reassemble = 0;
            	stream_connection->stages.correct = 0;
            """;

        Assert.False(StreamTeardownSource.TheStageTimingsAreLiftedBetweenCloseAndFree(FreedFirst));

        // And the one that turns the cascade into six cleanups.
        const string EarlyReturn = """
            disconnect:
            	return err;
            err_congestion_control:
            close_takion:
            err_video_receiver:
            err_haptics_receiver:
            err_audio_receiver:
            """;

        Assert.True(StreamTeardownSource.TheSixLabelsAreStillInOrder(EarlyReturn));
        Assert.False(StreamTeardownSource.TheCascadeStillFallsThrough(EarlyReturn));
    }
}
