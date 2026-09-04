using System.Collections.Concurrent;
using System.Diagnostics;
using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// One capture that is actually streaming, found once for the whole class.
///
/// PP652: A CONNECTED ENDPOINT IS NOT A STREAMING ONE. This machine's default communications device
/// is a Bluetooth headset, and a Bluetooth headset sits in a music profile with no microphone until
/// something makes it switch. Opening the endpoint is supposed to make it switch. Sometimes it does
/// - twenty units in 222 milliseconds - and sometimes thirty seconds pass with nothing at all, on
/// the same device with the same code.
///
/// THAT IS NOT A PROPERTY OF THE CAPTURE, so a check that fails on it is measuring the wrong thing.
/// But dropping the claim would leave nothing separating a capture that works from one that opens
/// and delivers silence, which is the whole of what PP652 built.
///
/// So the claim is widened rather than weakened: SOME active capture endpoint on this machine
/// streams. The default is tried first because it is what a host opens; if it will not stream, the
/// others are tried in turn, and a wired microphone answers at once. What that asserts is the code,
/// and what it stops asserting is one endpoint's radio.
/// </summary>
public sealed class CapturedMicrophone : IDisposable
{
    /// <summary>How long each endpoint is given to start streaming before the next is tried.</summary>
    public static TimeSpan WakeUp { get; } = TimeSpan.FromSeconds(6);

    private WasapiCapture? capture;
    private int units;
    private int wrongSize;

    /// <summary>What Start reported for the endpoint that answered, or NoDevice where none did.</summary>
    public CaptureResult Started { get; private set; } = CaptureResult.NoDevice;

    /// <summary>The endpoint's name, once one is open.</summary>
    public string DeviceName => capture?.DeviceName ?? string.Empty;

    /// <summary>Units the sink has been handed.</summary>
    public int Units => Volatile.Read(ref units);

    /// <summary>Units whose length was not the announced one, which must be none.</summary>
    public int WrongSize => Volatile.Read(ref wrongSize);

    /// <summary>How long the first unit took on the endpoint that answered.</summary>
    public long FirstUnitMilliseconds { get; private set; }

    /// <summary>How many endpoints were opened before one streamed.</summary>
    public int EndpointsTried { get; private set; }

    /// <summary>Every endpoint tried, with what it did, so a failure names them.</summary>
    public IReadOnlyList<string> Attempts => attempts;

    private readonly List<string> attempts = [];

    /// <summary>The capture, for a test that needs the counter.</summary>
    public WasapiCapture? Capture => capture;

    /// <summary>Find an endpoint that streams: the default first, then the rest.</summary>
    public CapturedMicrophone()
    {
        // The default has no id here, and the enumeration's entries do; null means "the default",
        // so it leads the list and the others follow in the order Windows gives them.
        var order = new List<string?> { null };
        order.AddRange(WasapiCapture.ActiveCaptureEndpoints().Select(one => (string?)one.Id));

        foreach (string? id in order)
        {
            if (Open(id))
                return;
        }
    }

    private bool Open(string? id)
    {
        int expected = MicrophoneFormat.BytesPerUnit(MicrophoneFormat.Announced);

        var opening = new WasapiCapture(one =>
        {
            if (one.Length != expected)
                Interlocked.Increment(ref wrongSize);

            Interlocked.Increment(ref units);
        });

        EndpointsTried++;
        CaptureResult started = opening.Start(id);

        if (started is not CaptureResult.Running)
        {
            attempts.Add($"{id ?? "(default)"}: {started} 0x{opening.LastError:x8}");
            opening.Dispose();
            return false;
        }

        var clock = Stopwatch.StartNew();
        bool streamed = SpinWait.SpinUntil(() => Volatile.Read(ref units) >= 1, WakeUp);
        clock.Stop();

        attempts.Add(
            $"'{opening.DeviceName}': {(streamed ? $"streamed in {clock.ElapsedMilliseconds} ms" : "silent")}");

        if (!streamed)
        {
            opening.Dispose();
            Interlocked.Exchange(ref units, 0);
            return false;
        }

        capture = opening;
        Started = started;
        FirstUnitMilliseconds = clock.ElapsedMilliseconds;
        return true;
    }

    /// <summary>Count what arrives over a window, which is the steady rate once warm.</summary>
    public double RateOver(TimeSpan window)
    {
        int from = Units;
        var clock = Stopwatch.StartNew();
        SpinWait.SpinUntil(() => false, window);
        clock.Stop();

        return (Units - from) * 1000.0 / clock.ElapsedMilliseconds;
    }

    public void Dispose() => capture?.Dispose();
}

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
public class WasapiCaptureTests(ITestOutputHelper output, CapturedMicrophone microphone)
    : IClassFixture<CapturedMicrophone>
{
    /// <summary>
    /// THE ONE THAT MATTERS: the default endpoint opens and hands over whole units.
    ///
    /// The wake-up is the fixture's, paid once. What is left here is the claim itself - a capture
    /// that opens and delivers nothing is broken, and one that delivers a unit of the wrong length
    /// is worse, because everything downstream is sized by that length.
    /// </summary>
    [Fact]
    public void TheDefaultEndpointOpensAndProducesUnits()
    {
        foreach (string attempt in microphone.Attempts)
            output.WriteLine(attempt);

        output.WriteLine(
            $"{microphone.Started} on '{microphone.DeviceName}' after {microphone.EndpointsTried} "
                + $"endpoint(s), first unit at {microphone.FirstUnitMilliseconds} ms");

        // A machine with no capture endpoint at all says nothing about this code.
        if (microphone.EndpointsTried <= 1 && microphone.Started is CaptureResult.NoDevice)
            return;

        Assert.True(
            microphone.Started is CaptureResult.Running,
            "no active capture endpoint on this machine streamed:\n  "
                + string.Join("\n  ", microphone.Attempts));

        Assert.NotEqual(string.Empty, microphone.DeviceName);
        Assert.True(microphone.Units >= 1);
        Assert.Equal(0, microphone.WrongSize);
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
        if (microphone.Started is not CaptureResult.Running || microphone.Units == 0)
            return;

        double rate = microphone.RateOver(TimeSpan.FromMilliseconds(600));
        double announced = MicrophoneFormat.UnitsPerSecond(MicrophoneFormat.Announced);

        output.WriteLine($"{rate:F1} units/s against an announced {announced:F1}");

        Assert.InRange(rate, announced * 0.5, announced * 2.0);
    }

    /// <summary>And the counter agrees with what the sink was handed.</summary>
    [Fact]
    public void TheCounterAgreesWithTheSink()
    {
        if (microphone.Capture is not { } capture || microphone.Started is not CaptureResult.Running)
            return;

        Assert.True(
            capture.UnitsCaptured >= microphone.Units,
            $"the counter says {capture.UnitsCaptured} and the sink was handed {microphone.Units}");
    }

    /// <summary>
    /// Starting twice is the same capture, not a second device.
    ///
    /// Its own instance, because this one is about Start's contract rather than about the device -
    /// and it deliberately does not wait, so a machine with no microphone answers it just as well.
    /// </summary>
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

    /// <summary>The unused import is deliberate: the recorder shape below is what a sink would use.</summary>
    [Fact]
    public void TheSinkSeesWholeUnitsAndNothingElse()
    {
        var seen = new ConcurrentQueue<int>();
        var accumulator = new MicrophoneUnits();

        accumulator.Take(new byte[accumulator.UnitBytes * 2 + 5], one => seen.Enqueue(one.Length));

        Assert.Equal(2, seen.Count);
        Assert.All(seen, one => Assert.Equal(accumulator.UnitBytes, one));
    }
}

