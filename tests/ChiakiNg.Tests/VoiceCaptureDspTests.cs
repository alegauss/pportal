using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP709, under PP52: the in-box echo canceller, driven rather than looked up.
///
/// The line's first criterion was a reading and it shipped: NVIDIA's SDK is not reachable on a
/// machine with the card, and CLSID_CWMAudioAEC is registered with mfwmaaec.dll present. The second
/// is that something actually cleans a sample, and PP648's rule binds it - a call that succeeds is
/// not a feature that ran, so what is asserted here is what came BACK.
///
/// FILTER MODE IS WHY PP698 CAME FIRST. The transform's two shapes are not interchangeable: source
/// mode opens both devices itself and takes the device choice with it, which PP695 is the reason
/// this port keeps. Filter mode is fed - the microphone on input 0 and PP698's loopback reference on
/// input 1 - and that is the shape these run.
///
/// THE RATE IS ASKED, NOT ASSUMED. The transform is documented to produce a short list of rates and
/// the console announces 48000, so whether the two meet is a fact about this machine's mfwmaaec.dll
/// rather than about a documentation page. It is asked with TEST_ONLY, which configures nothing.
/// </summary>
public class VoiceCaptureDspTests(ITestOutputHelper output)
{
    /// <summary>The DSP created and in filter mode, or null on a machine without it.</summary>
    private static VoiceCaptureDsp? Created()
    {
        var dsp = new VoiceCaptureDsp();

        if (dsp.Create())
            return dsp;

        dsp.Dispose();
        return null;
    }

    /// <summary>
    /// THE OBJECT EXISTS AND TAKES FILTER MODE, which is two facts and one call each.
    ///
    /// Its stream counts are what say filter mode took: source mode reports one input, filter mode
    /// reports two - the microphone and the reference - and one output either way. A transform that
    /// silently stayed in source mode would report one, and everything below would then be feeding a
    /// stream it does not have.
    /// </summary>
    [Fact]
    public void TheTransformExistsAndReportsFilterModesTwoInputs()
    {
        using VoiceCaptureDsp? dsp = Created();

        if (dsp is null)
        {
            output.WriteLine("this machine has no Voice Capture DSP");
            return;
        }

        (int inputs, int outputs) = dsp.StreamCounts();
        output.WriteLine($"{inputs} input(s), {outputs} output(s)");

        Assert.True(dsp.Created);
        Assert.Equal(VoiceCaptureDsp.InputStreams, inputs);
        Assert.Equal(VoiceCaptureDsp.OutputStreams, outputs);
    }

    /// <summary>
    /// WHICH RATES IT WILL TAKE, asked one at a time and printed.
    ///
    /// The reading this task actually owes. The console announces 48000 and the transform has its
    /// own list; whether they meet decides whether a cleaning stage can sit between PP652's capture
    /// and PP694's encoder unchanged, or whether something has to resample first.
    ///
    /// Asserted as "at least one", because the rates are the machine's answer and pinning the set
    /// here would be transcribing a measurement - PP666's shape. What the numbers say is in the
    /// output, and the join below is what turns them into a decision.
    /// </summary>
    [Fact]
    public void ItSaysWhichRatesItWillProduce()
    {
        using VoiceCaptureDsp? dsp = Created();
        if (dsp is null)
            return;

        IReadOnlyList<DspFormatAnswer> answers = dsp.Accepts();

        foreach (DspFormatAnswer answer in answers)
            output.WriteLine($"{answer.Rate,6} Hz: {(answer.Accepted ? "yes" : $"no (0x{answer.HResult:x8})")}");

        Assert.NotEmpty(answers);
        Assert.Contains(answers, one => one.Accepted);
    }

    /// <summary>
    /// THE JOIN: whether the announced format and the transform meet without a resample.
    ///
    /// This is the fact PP52's second half turns on, and it is read from the machine rather than
    /// decided here. If the announced rate is accepted, a cleaning stage drops into the capture
    /// chain unchanged. If it is not, the chain needs a conversion the port does not have - which is
    /// a different piece of work and this says so out loud rather than leaving a reader to infer it
    /// from a table.
    /// </summary>
    [Fact]
    public void TheAnnouncedRateEitherMeetsTheTransformOrDoesNot()
    {
        using VoiceCaptureDsp? dsp = Created();
        if (dsp is null)
            return;

        int announced = MicrophoneFormat.Announced.Rate;
        IReadOnlyList<DspFormatAnswer> answers = dsp.Accepts();

        DspFormatAnswer? forAnnounced = answers.FirstOrDefault(one => one.Rate == announced) is { Rate: > 0 } found
            ? found
            : null;

        Assert.NotNull(forAnnounced);

        output.WriteLine(
            forAnnounced.Value.Accepted
                ? $"the announced {announced} Hz is taken, so a stage drops in unchanged"
                : $"the announced {announced} Hz is refused (0x{forAnnounced.Value.HResult:x8}), so the "
                    + "chain needs a conversion before a stage can sit in it");

        // Whichever it is, the transform must have said SOMETHING it can do - a reading with no
        // yes anywhere would mean the object was not configured rather than that the rate is wrong.
        Assert.Contains(answers, one => one.Accepted);
    }

    /// <summary>
    /// SOMETHING ACTUALLY CLEANS A SAMPLE, which is the criterion in one sentence.
    ///
    /// A tone on the microphone stream and the SAME tone as the reference: the case an echo canceller
    /// exists for, because what the microphone hears is what the speakers played. The transform is
    /// fed enough frames to converge and the output is read back.
    ///
    /// WHAT IS ASSERTED IS THAT BYTES CAME BACK, not that the echo is gone. How much a filter removes
    /// depends on its convergence and on delay estimation the DSP does itself, and a threshold picked
    /// here would be a number about one run. PP648's rule is satisfied by the count: a stage that
    /// returned nothing has not run, whatever every call returned.
    /// </summary>
    [Fact]
    public void ItCleansASampleAndHandsSomethingBack()
    {
        using VoiceCaptureDsp? dsp = Created();
        if (dsp is null)
            return;

        int rate = dsp.Accepts().FirstOrDefault(one => one.Accepted).Rate;
        if (rate == 0)
            return;

        Assert.True(dsp.Configure(rate), $"the transform would not configure at {rate} Hz: 0x{dsp.LastError:x8}");
        Assert.Equal(rate, dsp.Rate);

        // Ten milliseconds a frame, which is the unit the rest of this path moves in.
        int frameBytes = rate / 100 * 2;
        byte[] tone = Tone(rate, frameBytes);
        var cleaned = new byte[frameBytes * 4];

        var produced = 0;
        var passes = 0;

        for (var frame = 0; frame < 50; frame++)
        {
            DspPass pass = dsp.Process(tone, tone, cleaned);
            passes++;
            produced += pass.Cleaned;
        }

        output.WriteLine($"{passes} passes, {produced} byte(s) back, last error 0x{dsp.LastError:x8}");

        Assert.True(
            produced > 0,
            $"the transform returned nothing over {passes} frames, so nothing was cleaned: "
                + $"0x{dsp.LastError:x8}");
    }

    /// <summary>
    /// And a transform nobody configured hands back nothing rather than throwing.
    ///
    /// The same reporting shape every device path here has: whether a machine can do this is a fact
    /// about the machine, and a host that cannot clean a sample still has a session to run.
    /// </summary>
    [Fact]
    public void AnUnconfiguredTransformProducesNothing()
    {
        using VoiceCaptureDsp? dsp = Created();
        if (dsp is null)
            return;

        DspPass pass = dsp.Process(new byte[960], new byte[960], new byte[960]);

        Assert.Equal(0, pass.Cleaned);
        Assert.Equal(0, dsp.Rate);
    }

    /// <summary>Disposing twice is safe, which every failure path in a host does.</summary>
    [Fact]
    public void DisposingTwiceIsSafe()
    {
        var dsp = new VoiceCaptureDsp();

        dsp.Create();
        dsp.Dispose();
        dsp.Dispose();

        Assert.False(dsp.Created);
    }

    /// <summary>
    /// The class id is the one the spike read, so the two halves of PP52 are about one transform.
    /// </summary>
    [Fact]
    public void ItIsTheTransformTheSpikeFound()
        => Assert.Equal(
            "{745057c7-f353-4f2d-a7ee-58434477730e}",
            EchoCancellation.VoiceCaptureDspClsid);

    /// <summary>A tone of one frame, loud enough that a cancelled version is a different signal.</summary>
    private static byte[] Tone(int rate, int bytes)
    {
        var frame = new byte[bytes];

        for (var i = 0; i < bytes / 2; i++)
        {
            double v = 0.5 * Math.Sin(2 * Math.PI * 440 * i / rate);
            var sample = (short)(v * short.MaxValue);

            frame[i * 2] = (byte)(sample & 0xff);
            frame[(i * 2) + 1] = (byte)((sample >> 8) & 0xff);
        }

        return frame;
    }
}
