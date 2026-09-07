using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP816's second stage: the decoder writes into the engine, and nothing sits between them.
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

    private static ManagedOpusDecoder Pair(IPcmSink sink)
    {
        ManagedAudioReceiverPair pair = ManagedStreamPhase.AudioPair(sink, out ManagedOpusDecoder speaker);

        Assert.NotNull(pair.AudioArm);

        return speaker;
    }
}
