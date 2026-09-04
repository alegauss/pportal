using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP710, under PP52: the rate bridge PP709's reading made necessary.
///
/// PP709 asked the Voice Capture DSP which output rates it takes and it answered 22050 and below,
/// refusing the 48000 streamconnection.c announces. That is the whole reason this exists: a cleaning
/// stage cannot sit between PP652's capture and PP694's encoder while the two ends disagree.
///
/// THE WAY IN NEEDS NOTHING. WasapiCapture asks for a format and AUTOCONVERTPCM puts the engine's
/// own resampler in front, so a microphone or PP698's reference at 16000 costs no code. The way OUT
/// has no engine in it, and that is what this is for.
///
/// WHAT IS ASSERTED IS THE PROPORTION, not a waveform. A resampler's output is a filter's opinion
/// and comparing samples would be asserting a filter design; what a rate change has to be right
/// about is how many samples come back per sample in, and that is arithmetic anybody can check.
/// </summary>
public class AudioResamplerTests(ITestOutputHelper output)
{
    /// <summary>The resampler, or null on a machine without it.</summary>
    private static AudioResampler? Created()
    {
        var resampler = new AudioResampler();

        if (resampler.Create())
            return resampler;

        resampler.Dispose();
        return null;
    }

    /// <summary>16-bit mono PCM of a tone, one frame's worth at a rate.</summary>
    private static byte[] Tone(int rate, int milliseconds)
    {
        int samples = rate * milliseconds / 1000;
        var frame = new byte[samples * 2];

        for (var i = 0; i < samples; i++)
        {
            double v = 0.5 * Math.Sin(2 * Math.PI * 440 * i / rate);
            var sample = (short)(v * short.MaxValue);

            frame[i * 2] = (byte)(sample & 0xff);
            frame[(i * 2) + 1] = (byte)((sample >> 8) & 0xff);
        }

        return frame;
    }

    /// <summary>The transform exists and has the one-in one-out shape a resampler is.</summary>
    [Fact]
    public void TheResamplerExistsAndHasOneStreamEachWay()
    {
        using AudioResampler? resampler = Created();

        if (resampler is null)
        {
            output.WriteLine("this machine has no audio resampler DSP");
            return;
        }

        (int inputs, int outputs) = resampler.StreamCounts();
        output.WriteLine($"{inputs} input(s), {outputs} output(s)");

        Assert.Equal(AudioResampler.InputStreams, inputs);
        Assert.Equal(AudioResampler.OutputStreams, outputs);
    }

    /// <summary>
    /// THE BRIDGE PP52 NEEDS: the canceller's best rate up to the announced one.
    ///
    /// 22050 is the highest PP709 found accepted and 48000 is what the console was told, so this is
    /// the exact conversion a cleaning stage would need on the way out. Asserted as a proportion,
    /// with a wide band: the transform holds samples back while its filter fills, so the ratio is
    /// only right over enough passes to have caught up.
    /// </summary>
    [Fact]
    public void ItCarriesTheCancellersRateUpToTheAnnouncedOne()
    {
        using AudioResampler? resampler = Created();
        if (resampler is null)
            return;

        const int From = 22050;
        int to = MicrophoneFormat.Announced.Rate;

        Assert.True(
            resampler.Configure(From, to),
            $"the resampler would not go {From} to {to}: 0x{resampler.LastError:x8}");

        Assert.Equal(From, resampler.FromRate);
        Assert.Equal(to, resampler.ToRate);

        byte[] frame = Tone(From, 10);
        var scratch = new byte[frame.Length * 8];

        var fed = 0;
        var produced = 0;

        for (var pass = 0; pass < 50; pass++)
        {
            ResamplePass one = resampler.Process(frame, scratch);
            fed += one.Fed;
            produced += one.Produced;
        }

        double ratio = produced / (double)fed;
        output.WriteLine($"{fed} in, {produced} out, ratio {ratio:F3} against {to / (double)From:F3}");

        Assert.True(produced > 0, $"the resampler returned nothing: 0x{resampler.LastError:x8}");
        Assert.InRange(ratio, to / (double)From * 0.9, to / (double)From * 1.1);
    }

    /// <summary>
    /// And the other direction, which is the way IN if a caller does not use the engine's converter.
    ///
    /// WasapiCapture's AUTOCONVERTPCM makes this unnecessary for an endpoint, and it is asserted
    /// anyway: the same object has to do both, and a transform configured one way that silently
    /// refused the other would be found the first time a path did not go through an endpoint.
    /// </summary>
    [Fact]
    public void ItAlsoCarriesTheAnnouncedRateDown()
    {
        using AudioResampler? resampler = Created();
        if (resampler is null)
            return;

        int from = MicrophoneFormat.Announced.Rate;
        const int To = 16000;

        Assert.True(resampler.Configure(from, To), $"0x{resampler.LastError:x8}");

        byte[] frame = Tone(from, 10);
        var scratch = new byte[frame.Length];

        var fed = 0;
        var produced = 0;

        for (var pass = 0; pass < 50; pass++)
        {
            ResamplePass one = resampler.Process(frame, scratch);
            fed += one.Fed;
            produced += one.Produced;
        }

        double ratio = produced / (double)fed;
        output.WriteLine($"{fed} in, {produced} out, ratio {ratio:F3} against {To / (double)from:F3}");

        Assert.True(produced > 0);
        Assert.InRange(ratio, To / (double)from * 0.9, To / (double)from * 1.1);
    }

    /// <summary>
    /// The two transforms meet: every rate the canceller takes, the resampler carries to the
    /// announced one.
    ///
    /// The join PP52's remaining criterion actually rests on, and it is a claim about this machine's
    /// two DLLs rather than about either alone. A resampler that refused the canceller's rates would
    /// leave the gap exactly where PP709 found it.
    /// </summary>
    [Fact]
    public void EveryRateTheCancellerTakesCanReachTheAnnouncedOne()
    {
        using var canceller = new VoiceCaptureDsp();
        if (!canceller.Create())
            return;

        int announced = MicrophoneFormat.Announced.Rate;
        var carried = 0;

        foreach (DspFormatAnswer answer in canceller.Accepts().Where(one => one.Accepted))
        {
            using AudioResampler? resampler = Created();
            if (resampler is null)
                return;

            bool ok = resampler.Configure(answer.Rate, announced);
            output.WriteLine($"{answer.Rate,6} -> {announced}: {(ok ? "yes" : $"no (0x{resampler.LastError:x8})")}");

            Assert.True(ok, $"the resampler refused {answer.Rate} to {announced}");
            carried++;
        }

        Assert.True(carried > 0, "the canceller accepted no rate, so nothing was carried");
    }

    /// <summary>A resampler nobody configured produces nothing rather than throwing.</summary>
    [Fact]
    public void AnUnconfiguredResamplerProducesNothing()
    {
        using AudioResampler? resampler = Created();
        if (resampler is null)
            return;

        ResamplePass pass = resampler.Process(new byte[960], new byte[960]);

        Assert.Equal(0, pass.Produced);
        Assert.Equal(0, resampler.FromRate);
    }

    /// <summary>Disposing twice is safe, which every failure path in a host does.</summary>
    [Fact]
    public void DisposingTwiceIsSafe()
    {
        var resampler = new AudioResampler();

        resampler.Create();
        resampler.Dispose();
        resampler.Dispose();

        Assert.False(resampler.Created);
    }

    /// <summary>
    /// The plumbing is shared, which is what stops a second copy of a COM vtable.
    ///
    /// Both transforms are DMOs and both are driven through <see cref="Dmo"/>. A check rather than a
    /// comment because PP693's rule is about exactly this surface: a missing method above one that
    /// is called sends the call to the wrong slot, and one copy has one chance to be wrong.
    /// </summary>
    [Fact]
    public void BothTransformsAreDrivenThroughTheSamePlumbing()
    {
        Type dmo = typeof(AudioResampler).Assembly.GetType("ChiakiNg.Native.Dmo")
            ?? throw new InvalidOperationException("the shared DMO plumbing is gone");

        Type media = dmo.GetNestedType("IMediaObject", System.Reflection.BindingFlags.Public)
            ?? throw new InvalidOperationException("IMediaObject is not on it");

        // Twenty-one methods, which is IMediaObject's whole vtable after IUnknown.
        Assert.Equal(21, media.GetMethods().Length);

        Assert.NotNull(dmo.GetNestedType("IMediaBuffer", System.Reflection.BindingFlags.Public));
        Assert.NotNull(dmo.GetNestedType("Buffer", System.Reflection.BindingFlags.Public));
    }
}
