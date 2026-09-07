using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP797: the two joins that stood between a decoded audio frame and a speaker.
///
/// PP708 opened the render endpoint, PP751 filled PP32's ring, and neither could hear the other:
/// the ring's only caller outside a test was the selftest, and the composition root handed both
/// audio arms a sink whose two methods are empty. Every piece was written and the sound stopped
/// twice on the way between them.
///
/// NEITHER HALF NEEDS A DEVICE TO ASSERT. <see cref="WasapiRender.Write"/> queues without an
/// endpoint, so a pass moves bytes on a machine with no speakers exactly as it does on one with
/// them - which is what makes this a gate check rather than a console trial.
/// </summary>
public class AudioPlaybackTests(ITestOutputHelper output)
{
    /// <summary>One frame's worth for a 48kHz stereo stream, which is what a console announces.</summary>
    private const int Frame = 480 * 2 * sizeof(short);

    private static void Announce(AudioPlayback playback)
        => playback.Sink.Settings(channels: 2, rate: 48000);

    private static void Fill(AudioPlayback playback, int frames)
    {
        var pcm = new short[480 * 2];

        for (var i = 0; i < frames; i++)
            playback.Sink.Pcm(pcm, 480);
    }

    /// <summary>
    /// A PASS MOVES THE RING INTO THE RENDERER, which is the join that did not exist.
    ///
    /// Everything either side of it shipped: the decoder writes into the ring and the renderer's
    /// own pump takes units off its queue. What nothing did was read one and write the other.
    /// </summary>
    [Fact]
    public void APassCarriesTheRingIntoTheRenderer()
    {
        using var render = new WasapiRender();
        using var playback = new AudioPlayback(render);

        Announce(playback);
        Fill(playback, 2);

        Assert.Equal(0, render.QueuedBytes);

        int moved = playback.Pass();

        output.WriteLine($"{moved} unit(s), {render.QueuedBytes} byte(s) queued");

        Assert.True(moved > 0, "the drain moved nothing out of a ring that had frames in it");
        Assert.True(render.QueuedBytes > 0);
        Assert.Equal(moved, playback.Drained);
    }

    /// <summary>
    /// AND IT STOPS AT THE TARGET rather than emptying the ring into the sink.
    ///
    /// streamsession.cpp's drain fills the sink to twice the buffer and stops, which is the latency
    /// policy PP5 named and this is its first caller. A drain with no target would push the whole
    /// ring in and put the sound however far behind the ring happened to be.
    /// </summary>
    [Fact]
    public void TheDrainStopsAtTwoBuffers()
    {
        using var render = new WasapiRender();
        using var playback = new AudioPlayback(render);

        Announce(playback);

        // Eight frames is the whole ring, which is four times the drain target.
        Fill(playback, 8);

        playback.Pass();

        output.WriteLine($"queued {render.QueuedBytes} against a target of {AudioRing.DrainTargetFor(Frame)}");

        Assert.True(
            render.QueuedBytes >= AudioRing.DrainTargetFor(Frame),
            "the drain stopped short of the target it is supposed to fill to");

        // One unit over the target at most: the loop asks before each write and each write is one
        // buffer, so the overshoot is bounded by the unit rather than by the ring.
        Assert.True(
            render.QueuedBytes <= AudioRing.DrainTargetFor(Frame) + Frame,
            $"the drain pushed {render.QueuedBytes} bytes past a target of {AudioRing.DrainTargetFor(Frame)}");
    }

    /// <summary>
    /// A SINK MORE THAN THREE BUFFERS BEHIND IS THROWN AWAY, which is the drain's third branch.
    ///
    /// PP5 named <see cref="AudioRing.ClearThresholdFor"/> and nothing had ever called it. A sink
    /// that far behind is playing audio the picture has left, and draining it slowly keeps the lag
    /// rather than spending it - which is the failure that shows up as sound drifting behind over
    /// an evening rather than as anything a count would report.
    /// </summary>
    [Fact]
    public void ASinkThatIsFarBehindIsClearedRatherThanDrained()
    {
        using var render = new WasapiRender();
        using var playback = new AudioPlayback(render);

        Announce(playback);

        // Four buffers already waiting, which is past the threshold of three.
        for (var i = 0; i < 4; i++)
            render.Write(new byte[Frame]);

        Assert.True(render.QueuedBytes > AudioRing.ClearThresholdFor(Frame));

        Fill(playback, 1);
        playback.Pass();

        Assert.Equal(1, playback.Cleared);

        // And it refilled from the ring in the same pass rather than leaving the sink empty.
        Assert.True(render.QueuedBytes > 0);
        Assert.True(render.QueuedBytes <= AudioRing.DrainTargetFor(Frame) + Frame);
    }

    /// <summary>A pass before any header has said what the stream is moves nothing and does not throw.</summary>
    [Fact]
    public void APassBeforeAnyStreamInfoIsANoOp()
    {
        using var render = new WasapiRender();
        using var playback = new AudioPlayback(render);

        Assert.Equal(0, playback.Pass());
        Assert.Equal(0, render.QueuedBytes);
    }

    /// <summary>
    /// PP797: THE ROOT'S OWN ARM, which is the other join - and the one PP762's shape hides.
    ///
    /// The composition root ran only when a session handed over, so the sink it passed was
    /// unreachable from outside and a census counting types could not see that it was a no-op. The
    /// arm is built by a factory now, and this asks the factory for one and follows what it writes.
    /// </summary>
    [Fact]
    public void TheRootsAudioArmWritesIntoThePlaybacksOwnRing()
    {
        using var render = new WasapiRender();
        using var playback = new AudioPlayback(render);

        ManagedAudioReceiverPair pair = ManagedStreamPhase.AudioPair(playback, out ManagedOpusDecoder speaker);

        using (speaker)
        {
            Assert.NotNull(pair.AudioArm);
            Assert.Null(playback.Sink.Ring);

            if (!ManagedOpusDecoder.IsAvailable())
            {
                output.WriteLine("libopus is not behind the shim on this build, so the header half is skipped");
                return;
            }

            // A STREAMINFO reaches the decoder, and the decoder is what tells the ring its shape.
            // The ring existing afterwards is the whole of the join: it is THIS playback's.
            ((IAudioFrameSink)speaker).Header(
                new ManagedAudioHeader(Channels: 2, Bits: 16, Rate: 48000, FrameSize: 480, Unknown: 0));

            Assert.NotNull(playback.Sink.Ring);

            // And what the ring takes reaches the renderer through the drain above it.
            Fill(playback, 1);
            Assert.True(playback.Pass() > 0);
            Assert.True(render.QueuedBytes > 0);
        }
    }
}
