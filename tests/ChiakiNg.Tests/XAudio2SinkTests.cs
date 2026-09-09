using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP816's second stage: the decoder writes into the engine, and nothing sits between them.
///
/// AND PP797'S CLAIM, which moved here rather than lapsing. That line's symptom was that the
/// console's audio reached a receiver whose output went nowhere, and its assertions were
/// AudioPlaybackTests - deleted by PP820 with the ring they were about. What PP797 established is
/// that the root's arm reaches something that PLAYS, and that is what the tests below hold: the
/// same join, one seam over. Named here because the join is the claim and not the ring.
///
/// AudioRingSink is the seam this replaces - the decoder puts a frame in AudioRing, AudioPlayback's
/// thread drains the ring into WasapiRender, and that pump asks the engine how much room it has and
/// clamps the frame to fit. Three objects, two threads and the arithmetic PP810 is one wrong line
/// of. XAudio2Sink is the same seam with the middle taken out.
///
/// WHAT IS ASSERTED IS THE WIRING AND THE ACCOUNTING, not the sound. Whether the click is gone is a
/// person listening, which the criterion on PP816 says outright - PP815 shipped assertions that all
/// passed while its attribution was wrong, and that is the mistake being avoided rather than
/// repeated.
/// </summary>
public class XAudio2SinkTests(ITestOutputHelper output)
{
    /// <summary>An IPcmSink that records what it was told, so the wiring can be read.</summary>
    private sealed class Recording : IPcmSink
    {
        public (byte Channels, uint Rate)? Announced { get; private set; }

        public int Frames { get; private set; }

        public void Settings(byte channels, uint rate) => Announced = (channels, rate);

        public void Pcm(ReadOnlySpan<short> pcm, int samplesPerChannel) => Frames++;
    }

    /// <summary>
    /// THE ROOT'S PAIR HANDS THE DECODER THE SINK IT WAS GIVEN, which is the move itself.
    ///
    /// ManagedStreamPhase.AudioPair used to take an AudioPlayback and reach into its ring; it now
    /// takes the seam, so what a decoded frame lands in is the composition root's choice rather than
    /// a shape baked into the pair. Driven through the decoder's own header callback, because that
    /// is the path a STREAMINFO takes.
    /// </summary>
    [Fact]
    public void TheRootsPairWritesIntoTheSinkItWasGiven()
    {
        var recording = new Recording();

        using ManagedOpusDecoder speaker = Pair(recording);

        if (!ManagedOpusDecoder.IsAvailable())
            return;

        speaker.Header(ManagedAudioHeader.Set(channels: 2, bits: 16, rate: 48000, frameSize: 480));

        output.WriteLine($"announced {recording.Announced}");

        Assert.Equal(((byte)2, 48000u), recording.Announced);
    }

    /// <summary>
    /// THE SINK OPENS FOR WHAT THE STREAM ANNOUNCED, and not before it has been told.
    ///
    /// A source voice IS its rate and channel count, so one built before a STREAMINFO would be built
    /// for a guess. The ring had the same rule for the same reason - it is sized from the header -
    /// and this keeps it.
    /// </summary>
    [Fact]
    public void TheDeviceOpensOnlyOnceTheStreamHasSaidWhatItIs()
    {
        using var sink = new XAudio2Sink();

        Assert.Null(sink.Opened);
        Assert.Null(sink.Render);

        sink.Settings(2, 48000);

        output.WriteLine($"{sink.Opened}, announced {sink.Announced}");

        Assert.NotNull(sink.Opened);
        Assert.Equal(((byte)2, 48000u), sink.Announced);

        // The voice carries the announced format, whatever the endpoint behind it runs at.
        Assert.NotNull(sink.Render);
        Assert.Equal(48000, sink.Render.Format.Rate);
        Assert.Equal(2, sink.Render.Format.Channels);
    }

    /// <summary>
    /// AUDIO ARRIVING WITH NO DEVICE IS COUNTED AND NOT THROWN, which is PP708's rule kept.
    ///
    /// Whether a machine has speakers is a fact about the machine, and a session still has a run to
    /// finish. What matters is that a run which played nothing can SAY so - a silent drop here is
    /// the shape PP814 spent a whole task discovering the audio path was full of.
    /// </summary>
    [Fact]
    public void AFrameWithNoDeviceIsCountedRatherThanDropped()
    {
        using var sink = new XAudio2Sink();

        short[] frame = new short[480 * 2];

        // No Settings, so there is no engine at all - the state a header-less stream is in.
        sink.Pcm(frame, 480);
        sink.Pcm(frame, 480);

        output.WriteLine($"{sink.Submitted} submitted, {sink.Unplayable} unplayable");

        Assert.Equal(0, sink.Submitted);
        Assert.Equal(2, sink.Unplayable);
    }

    /// <summary>
    /// AND A FRAME IS SUBMITTED WHOLE, which is the property the pump could not offer.
    ///
    /// There is no count of how much was taken, because the engine does not take part of a buffer.
    /// Skipped where there is no endpoint, which is CI rather than a failure.
    /// </summary>
    [Fact]
    public void EveryFrameReachesTheEngineWhole()
    {
        using var sink = new XAudio2Sink();

        sink.Settings(2, 48000);

        if (sink.Opened != RenderResult.Running)
            return;

        short[] frame = new short[480 * 2];

        for (var i = 0; i < 8; i++)
            sink.Pcm(frame, 480);

        output.WriteLine(
            $"{sink.Submitted} submitted, {sink.Unplayable} unplayable, "
                + $"{sink.Render?.FramesSubmitted} frame(s) at the engine");

        Assert.Equal(8, sink.Submitted);
        Assert.Equal(0, sink.Unplayable);

        // Eight frames of 480, and not as much of each as something had room for.
        Assert.Equal(8 * 480, sink.Render?.FramesSubmitted);
    }

    /// <summary>
    /// PP833: the cushion holds until it is full, releases in order, and then gets out of the way.
    ///
    /// PP814 measured the engine running dry seven times in twenty-five seconds, touching a depth
    /// of one, in a stream every other counter called healthy. Nothing held a reserve because each
    /// frame went over the moment Opus produced it.
    ///
    /// THE "AND THEN GETS OUT OF THE WAY" HALF IS THE ONE THAT MATTERS. A cushion that kept holding
    /// would be AudioRing again - a second queue in front of the engine's, which is the shape PP816
    /// deleted and PP810 measured going wrong. Steady state after the release is frame-for-frame
    /// what shipped before.
    /// </summary>
    [Fact]
    public void ThePrefillHoldsUntilItIsFullAndThenReleasesInOrder()
    {
        using var sink = new XAudio2Sink { Prefill = 3 };
        sink.Prepare();

        // PP708's rule: whether this machine has speakers is a fact about the machine.
        if (sink.Render is not { Running: true })
            return;

        var frame = new short[960];

        sink.Pcm(frame, 480);
        sink.Pcm(frame, 480);

        // Held, not dropped: nothing has reached the engine and two frames are waiting.
        Assert.Equal(0, sink.Submitted);
        Assert.Equal(2, sink.Holding);

        sink.Pcm(frame, 480);

        // The third fills it, and all three go together - which is the depth the engine starts on.
        Assert.Equal(3, sink.Submitted);
        Assert.Equal(0, sink.Holding);

        sink.Pcm(frame, 480);

        Assert.Equal(4, sink.Submitted);
        Assert.Equal(0, sink.Holding);

        output.WriteLine($"{sink.Submitted} submitted, holding {sink.Holding}");
    }

    /// <summary>
    /// And no prefill is what shipped before it, which is what a caller who says nothing gets.
    ///
    /// The cushion costs latency - one frame for each held - and this is a remote play client, so
    /// the number is a trade somebody makes rather than a default this file picks. Zero has to stay
    /// the behaviour of every run that did not ask.
    /// </summary>
    [Fact]
    public void SayingNothingLeavesTheCushionOff()
    {
        using var sink = new XAudio2Sink();
        sink.Prepare();

        Assert.Equal(0, sink.Prefill);

        if (sink.Render is not { Running: true })
            return;

        sink.Pcm(new short[960], 480);

        Assert.Equal(1, sink.Submitted);
        Assert.Equal(0, sink.Holding);
    }

    private static ManagedOpusDecoder Pair(IPcmSink sink)
    {
        ManagedAudioReceiverPair pair = ManagedStreamPhase.AudioPair(sink, out ManagedOpusDecoder speaker);

        Assert.NotNull(pair.AudioArm);

        return speaker;
    }
}
