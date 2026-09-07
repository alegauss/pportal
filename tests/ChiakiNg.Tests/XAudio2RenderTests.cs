using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP816: the queue Windows already ships, opened and played into.
///
/// WasapiRender is a pump - it asks the engine how much room there is, works out how much of a unit
/// fits, and copies that much. PP810 is one line of that arithmetic going wrong, and both of its
/// measured fixes moved the loss instead of ending it: 2374 and 2410 ring evictions against one.
/// XAudio2 takes a buffer WHOLE, so there is no room to ask about and no tail to lose.
///
/// WHAT THESE ASSERT IS THAT THE SEAM WORKS, not that the sound is right - a person listening is
/// what says that, which is how PP815's attribution was found to be wrong after its assertions
/// passed. The engine opens, takes buffers, reports its own queue depth, and lets its pins go.
///
/// AND THEY SKIP RATHER THAN FAIL WHERE THERE IS NO ENDPOINT. CI has no speakers, and a machine
/// without them is a fact about the machine - the same reading WasapiRender's own tests take.
/// </summary>
public class XAudio2RenderTests(ITestOutputHelper output)
{
    /// <summary>48 kHz stereo 16-bit, which is what every PS5 stream has announced here.</summary>
    private const int UnitFrames = 480;

    private static XAudio2Render Render()
        => new(new MicrophoneAnnouncement(Channels: 2, Bits: 16, Rate: 48000, FrameSize: UnitFrames));

    /// <summary>One decoded frame's worth of quiet tone, so a run that is heard is not a squeal.</summary>
    private static byte[] Unit(int frames = UnitFrames)
    {
        byte[] unit = new byte[frames * 2 * sizeof(short)];

        for (var i = 0; i < frames; i++)
        {
            var sample = (short)(2000 * Math.Sin(2 * Math.PI * 440 * i / 48000.0));
            BitConverter.TryWriteBytes(unit.AsSpan(i * 4), sample);
            BitConverter.TryWriteBytes(unit.AsSpan((i * 4) + 2), sample);
        }

        return unit;
    }

    /// <summary>
    /// THE ENGINE OPENS AND TAKES A BUFFER WHOLE, which is the whole of stage one.
    ///
    /// Every byte handed over is accepted - there is no room parameter, no clamp and no count of
    /// what was taken, which is the shape that makes PP810's defect unwritable.
    /// </summary>
    [Fact]
    public void TheEngineOpensAndTakesWholeBuffers()
    {
        using XAudio2Render render = Render();

        RenderResult opened = render.Open();

        output.WriteLine($"{opened}, hr {render.LastError:X8}");

        if (opened == RenderResult.NoDevice)
            return;

        Assert.Equal(RenderResult.Running, opened);
        Assert.True(render.Running);

        byte[] unit = Unit();

        for (var i = 0; i < 8; i++)
            render.Write(unit);

        output.WriteLine($"{render.FramesSubmitted} frame(s) submitted, {render.Queued} queued");

        // Every one of them, and not as much of each as something had room for.
        Assert.Equal(UnitFrames * 8, render.FramesSubmitted);
    }

    /// <summary>
    /// THE ENGINE REPORTS ITS OWN QUEUE DEPTH, which is the number WASAPI made us derive.
    ///
    /// WasapiRender.QueuedBytes walks a managed queue and adds lengths up; this is the engine
    /// answering. It also drains on its own - the count falls as the audio plays, with nothing
    /// polling and no drain thread, which is the pump this line deletes.
    /// </summary>
    [Fact]
    public async Task TheQueueDrainsWithNothingDrivingIt()
    {
        using XAudio2Render render = Render();

        if (render.Open() != RenderResult.Running)
            return;

        byte[] unit = Unit();

        // Eighty milliseconds of audio, which is the depth AudioRing was built to hold.
        for (var i = 0; i < 8; i++)
            render.Write(unit);

        int queuedAtOnce = render.Queued;

        for (var i = 0; i < 6; i++)
        {
            output.WriteLine(
                $"  +{i * 40}ms: {render.Queued} queued, {render.SamplesPlayed} played, hr {render.LastError:X8}");
            await Task.Delay(TimeSpan.FromMilliseconds(40));
        }

        int queuedLater = render.Queued;
        ulong played = render.SamplesPlayed;

        output.WriteLine($"{queuedAtOnce} queued, then {queuedLater}, {played} sample(s) played");

        Assert.True(queuedAtOnce > 0, "the engine reported nothing queued after eight submits");

        // It emptied itself. Nothing here drove it: no pump, no poll, no room arithmetic.
        Assert.True(queuedLater < queuedAtOnce, $"queue did not drain: {queuedAtOnce} then {queuedLater}");
        Assert.True(played > 0, "the engine played nothing");
    }

    /// <summary>
    /// A CLEAR DROPS WHAT IS QUEUED, which is the sink being too far behind.
    ///
    /// The counterpart of WasapiRender.Clear, and the pins go with it: a flushed buffer is one the
    /// engine will never report as finished, so waiting for it would hold that memory for the life
    /// of the session.
    /// </summary>
    [Fact]
    public async Task AClearDropsWhatTheEngineHasNotPlayed()
    {
        using XAudio2Render render = Render();

        if (render.Open() != RenderResult.Running)
            return;

        byte[] unit = Unit();

        for (var i = 0; i < 8; i++)
            render.Write(unit);

        render.Clear();

        // A processing pass, because GetState answers about the LAST one. The flush takes effect
        // immediately and the state the engine reports does not, so reading straight after it gives
        // the count from before - which is not the flush failing and reads exactly like it.
        await Task.Delay(TimeSpan.FromMilliseconds(40));

        output.WriteLine($"{render.Queued} queued after the flush");

        Assert.Equal(0, render.Queued);
    }

    /// <summary>
    /// Volume is the engine's, which is what makes a managed pass over every sample unnecessary.
    ///
    /// AudioVolume.Apply multiplies and clamps each sample in C#; this hands the same job to the
    /// mixer that is already touching every sample anyway. Asserted as accepted rather than as
    /// audible - what it sounds like is not a thing a test can read.
    /// </summary>
    [Fact]
    public void TheEngineTakesTheVolumeItself()
    {
        using XAudio2Render render = Render();

        if (render.Open() != RenderResult.Running)
            return;

        render.SetVolume(64f / AudioVolume.MaxVolume);

        Assert.Equal(0, render.LastError);
    }

    /// <summary>
    /// And the format the source voice was built for is the one it was asked for.
    ///
    /// The mastering voice takes the endpoint's own channels and rate, so the engine's resampler
    /// sits between the two - which is what AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM did on the WASAPI
    /// side, moved from a flag to the shape of the graph.
    /// </summary>
    [Fact]
    public void TheSourceVoiceKeepsTheAnnouncedFormat()
    {
        using XAudio2Render render = Render();

        Assert.Equal(2, render.Format.Channels);
        Assert.Equal(48000, render.Format.Rate);
        Assert.Equal(4, render.FrameBytes);
    }
}
