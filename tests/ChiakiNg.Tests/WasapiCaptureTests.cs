using System.Diagnostics;
using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP652: the microphone, opened - which is the sentence MicrophoneSurface existed to deny.
///
/// Four subsystems assumed a stream of samples and nothing produced one. This runs the real capture
/// against the real default endpoint, because that is the only assertion that says the device
/// opened: everything short of it is a shape check on code that has never met a driver.
///
/// WHAT IS ASSERTED AND WHAT IS REPORTED. That the device opens, that units arrive, and that they
/// arrive at about the announced rate are assertions. WHAT IS IN THEM is not - a machine with a
/// muted microphone produces silence, and asserting on the samples would fail on a quiet room
/// rather than on a defect. PP22's line about what only a runner can say applies: whether a person
/// can be heard is a run, not a test.
///
/// A MACHINE WITH NO MICROPHONE IS NOT A FAILURE. Start reports rather than throwing, and these
/// return early on NoDevice, which is the same shape SurfacePresenter's tests have for a machine
/// with no display.
/// </summary>
public class WasapiCaptureTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE ONE THAT MATTERS: the default endpoint opens and hands over whole units.
    ///
    /// Bounded at two seconds, which is two hundred units at the announced ten-millisecond frame -
    /// so a capture producing even a tenth of the expected rate still passes, and one producing
    /// nothing at all fails.
    /// </summary>
    [Fact]
    public void TheDefaultEndpointOpensAndProducesUnits()
    {
        int units = 0;
        int wrongSize = 0;

        using var capture = new WasapiCapture(one =>
        {
            if (one.Length != MicrophoneFormat.BytesPerUnit(MicrophoneFormat.Announced))
                Interlocked.Increment(ref wrongSize);

            Interlocked.Increment(ref units);
        });

        CaptureResult started = capture.Start();
        output.WriteLine($"{started} on '{capture.DeviceName}' (0x{capture.LastError:x8})");

        if (started == CaptureResult.NoDevice)
            return;

        Assert.Equal(CaptureResult.Running, started);
        Assert.NotEqual(string.Empty, capture.DeviceName);

        var clock = Stopwatch.StartNew();
        SpinWait.SpinUntil(() => Volatile.Read(ref units) >= 20, TimeSpan.FromSeconds(2));
        clock.Stop();

        output.WriteLine($"{units} unit(s) in {clock.ElapsedMilliseconds} ms");

        Assert.True(units >= 20, $"the device opened and produced {units} units in {clock.ElapsedMilliseconds} ms");
        Assert.Equal(0, wrongSize);
        Assert.Equal(units, capture.UnitsCaptured);
    }

    /// <summary>
    /// And the units arrive at about the announced rate, which is what says the format took.
    ///
    /// A hundred units a second is what 480 frames at 48000 Hz means. A capture that had silently
    /// been given the device's own 16000 Hz would produce them at a third of that, and a stereo one
    /// at twice - so the rate is where a conversion that did not happen shows up.
    ///
    /// The band is wide on purpose: this is a shared engine on a machine running a test suite, and
    /// the claim is the order of magnitude rather than the clock.
    /// </summary>
    [Fact]
    public void TheUnitsArriveAtAboutTheAnnouncedRate()
    {
        int units = 0;

        using var capture = new WasapiCapture(_ => Interlocked.Increment(ref units));

        if (capture.Start() is not CaptureResult.Running)
            return;

        // A first interval discarded: the engine fills its buffer before the loop starts reading,
        // so the opening burst is not the steady rate.
        SpinWait.SpinUntil(() => Volatile.Read(ref units) >= 10, TimeSpan.FromSeconds(2));

        int from = Volatile.Read(ref units);
        var clock = Stopwatch.StartNew();
        SpinWait.SpinUntil(() => false, TimeSpan.FromMilliseconds(600));
        clock.Stop();

        double rate = (Volatile.Read(ref units) - from) * 1000.0 / clock.ElapsedMilliseconds;
        double announced = MicrophoneFormat.UnitsPerSecond(MicrophoneFormat.Announced);

        output.WriteLine($"{rate:F1} units/s against an announced {announced:F1}");

        Assert.InRange(rate, announced * 0.5, announced * 2.0);
    }

    /// <summary>Starting twice is the same capture, not a second device.</summary>
    [Fact]
    public void StartingTwiceIsTheSameCapture()
    {
        using var capture = new WasapiCapture(_ => { });

        CaptureResult first = capture.Start();
        if (first is CaptureResult.NoDevice)
            return;

        Assert.Equal(first, capture.Start());
    }

    /// <summary>Disposing twice is safe, which every failure path in a host does.</summary>
    [Fact]
    public void DisposingTwiceIsSafe()
    {
        var capture = new WasapiCapture(_ => { });

        capture.Start();
        capture.Dispose();
        capture.Dispose();

        Assert.Throws<ObjectDisposedException>(() => capture.Start());
    }

    /// <summary>And a null sink is refused, rather than opening a device nothing reads.</summary>
    [Fact]
    public void ANullSinkIsRefused()
        => Assert.Throws<ArgumentNullException>(() => new WasapiCapture(null!));

    /// <summary>
    /// The two flags are the ones the spike measured, named rather than inlined.
    ///
    /// AUTOCONVERTPCM is the whole finding: without it no device here takes the announced format,
    /// and IsFormatSupported cannot be asked about it. If either constant changed, the capture
    /// would still open and would hand back the device's own format instead.
    /// </summary>
    [Fact]
    public void TheFlagsAreTheOnesThatMakeTheFormatReachable()
    {
        Assert.Equal(unchecked((int)0x80000000), WasapiCapture.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM);
        Assert.Equal(0x08000000, WasapiCapture.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY);
    }
}
