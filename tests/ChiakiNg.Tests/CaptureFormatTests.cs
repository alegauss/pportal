using System.Buffers.Binary;
using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP711: a capture can be asked for a format, and the cleaning stage has a door for one that was.
///
/// PP710 recorded that a rate change in this port is Windows's own DMO and that the way IN stays
/// free, because the capture engine's converter already does it. PP52's stage did not take that
/// route - it converted both inputs down itself - for one reason: WasapiCapture asked for
/// MicrophoneFormat.Announced and had no way to be asked for anything else.
///
/// AUTOCONVERTPCM IS WHAT MAKES IT A CHOICE. PP652 found that no capture device here takes the
/// announced format in shared mode, and that the flag puts a converter in front so the client reads
/// exactly what it asked for. That works for any format, not only the announced one - so a caller
/// that wants sixteen thousand asks for it and Windows does the conversion.
///
/// AND THE BYTES-IN DOOR STAYS, which is the trade this line owed a decision on. A stage that could
/// only be fed by devices could not be asserted on a machine without one, and PP52's assertions run
/// on any machine with the transform. Two doors, one cancellation.
/// </summary>
public class CaptureFormatTests(ITestOutputHelper output)
{
    /// <summary>The format a caller wanting the canceller's rate asks for.</summary>
    private static MicrophoneAnnouncement AtCancellerRate(CleanedMicrophone stage)
        => MicrophoneFormat.Announced with
        {
            Rate = stage.CancellerRate,
            FrameSize = stage.CancellerFrameBytes / 2,
        };

    /// <summary>A unit of a tone at a rate, in whole samples.</summary>
    private static byte[] Unit(int rate, int frameSize, int at, double hertz = 440, double amplitude = 0.5)
    {
        var unit = new byte[frameSize * 2];

        for (var i = 0; i < frameSize; i++)
        {
            double t = (at + i) / (double)rate;
            var sample = (short)(amplitude * Math.Sin(2 * Math.PI * hertz * t) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(unit.AsSpan(i * 2), sample);
        }

        return unit;
    }

    private static double Rms(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length < 2)
            return 0;

        double total = 0;
        int samples = pcm.Length / 2;

        for (var i = 0; i < samples; i++)
        {
            double v = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * 2)..]);
            total += v * v;
        }

        return Math.Sqrt(total / samples);
    }

    /// <summary>A capture with no format asked for is still the announced one.</summary>
    [Fact]
    public void TheDefaultFormatIsStillTheAnnouncedOne()
    {
        using var capture = new WasapiCapture(_ => { });

        Assert.Equal(MicrophoneFormat.Announced, capture.Format);
    }

    /// <summary>
    /// AND A CAPTURE ASKED FOR ANOTHER OPENS AT IT, delivering units of that format's size.
    ///
    /// The whole of what PP711 needed from the capture. 16000 Hz makes a ten-millisecond unit 320
    /// bytes rather than 960, and a sink still sized from the announced format would be reading
    /// three units as one - so the unit follows the format and this is where that is asserted.
    ///
    /// The device is real. A machine with no capture endpoint returns early, the same shape every
    /// device test here has.
    /// </summary>
    [Fact]
    public void ACaptureAskedForTheCancellersRateDeliversItsUnits()
    {
        if (!CleanedMicrophone.IsAvailable())
            return;

        using var stage = new CleanedMicrophone();
        if (!stage.Start())
            return;

        MicrophoneAnnouncement wanted = AtCancellerRate(stage);
        int expected = MicrophoneFormat.BytesPerUnit(wanted);

        output.WriteLine($"{wanted.Rate} Hz, {wanted.FrameSize} frames, {expected} byte units");
        Assert.Equal(stage.CancellerFrameBytes, expected);

        var units = 0;
        var wrongSize = 0;

        using var capture = new WasapiCapture(
            one =>
            {
                if (one.Length != expected)
                    Interlocked.Increment(ref wrongSize);

                Interlocked.Increment(ref units);
            },
            wanted);

        Assert.Equal(wanted, capture.Format);

        if (capture.Start() is not CaptureResult.Running)
        {
            output.WriteLine($"no endpoint opened at {wanted.Rate} Hz: 0x{capture.LastError:x8}");
            return;
        }

        SpinWait.SpinUntil(() => Volatile.Read(ref units) >= 1, TimeSpan.FromSeconds(6));

        output.WriteLine($"'{capture.DeviceName}': {Volatile.Read(ref units)} unit(s)");

        Assert.Equal(0, Volatile.Read(ref wrongSize));

        // A silent or absent microphone is a fact about the machine and not about this code, so
        // what is asserted is the SIZE of whatever arrived rather than that something did.
        if (Volatile.Read(ref units) == 0)
            output.WriteLine("the endpoint opened and said nothing, which is PP695's state");
    }

    /// <summary>
    /// THE SECOND DOOR CLEANS, which is what makes the saving real rather than theoretical.
    ///
    /// A caller whose endpoints are already at the canceller's rate hands whole frames straight to
    /// the transform, and the two downward converters are never touched. What has to hold is that
    /// the cleaning is the same KIND of thing: a perfect echo goes quiet either way.
    ///
    /// Not the same bytes. The two doors take different paths through different filters, so
    /// comparing samples would be asserting that two resamplers agree - which they do not, and
    /// which is not what either door is for.
    /// </summary>
    [Fact]
    public void TheDeviceRateDoorCleansToo()
    {
        if (!CleanedMicrophone.IsAvailable())
            return;

        using var stage = new CleanedMicrophone();
        if (!stage.Start())
            return;

        int frameSize = stage.CancellerFrameBytes / 2;
        var into = new byte[stage.UnitBytes * 4];

        double rawEnergy = 0;
        double cleanedEnergy = 0;
        var judged = 0;

        for (var frame = 0; frame < 200; frame++)
        {
            byte[] unit = Unit(stage.CancellerRate, frameSize, frame * frameSize);

            CleanedUnit pass = stage.CleanAtCancellerRate(unit, unit, into);

            if (frame >= 150 && pass.Cleaned > 0)
            {
                rawEnergy += Rms(unit);
                cleanedEnergy += Rms(into.AsSpan(0, pass.Cleaned));
                judged++;
            }
        }

        output.WriteLine(
            $"{stage.UnitsCleaned} cleaned, {judged} judged: raw {rawEnergy / Math.Max(judged, 1):F0} "
                + $"against cleaned {cleanedEnergy / Math.Max(judged, 1):F0}");

        Assert.True(judged > 0, "nothing reached the judgement window through the device-rate door");
        Assert.True(
            cleanedEnergy < rawEnergy,
            $"the second door did not cancel: {cleanedEnergy:F0} against {rawEnergy:F0}");
    }

    /// <summary>And it produces nothing before Start, the same as the other one.</summary>
    [Fact]
    public void TheDeviceRateDoorProducesNothingBeforeStart()
    {
        using var stage = new CleanedMicrophone();

        Assert.Equal(default, stage.CleanAtCancellerRate(new byte[320], [], new byte[960]));
    }

    /// <summary>
    /// The rate a caller opens at is only knowable after the stage started, which is the order.
    ///
    /// The transform is asked which rates it takes, so nothing before <see cref="CleanedMicrophone.Start"/>
    /// knows what to ask an endpoint for. That is why the bytes-in door stays: a check that had to
    /// open a device to run could not run at all on a machine without one.
    /// </summary>
    [Fact]
    public void TheRateIsOnlyKnownAfterTheStageStarted()
    {
        using var stage = new CleanedMicrophone();

        Assert.Equal(0, stage.CancellerRate);
        Assert.Equal(0, stage.CancellerFrameBytes);

        if (!stage.Start())
            return;

        Assert.True(stage.CancellerRate > 0);
        Assert.Equal(CleanedMicrophone.ExpectedCancellerRate, stage.CancellerRate);
    }
}
