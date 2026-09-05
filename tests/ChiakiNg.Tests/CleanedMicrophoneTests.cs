using System.Buffers.Binary;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP52's second criterion: a stage between the capture and the encoder that actually cleans.
///
/// The first half of the line shipped as a reading - the vendor SDK is absent on a machine with the
/// card, Windows's transform is registered. PP709 drove it and found it refuses the announced 48000;
/// PP710 brought the bridge; PP706 built the path. This is the stage in that path, and PP648's rule
/// binds what may be asserted about it: a call that succeeds is not a feature that ran, so what is
/// checked is what came BACK and what the samples turned into.
///
/// THE EFFECT IS MEASURED, NOT THE RETURN CODE. A microphone hearing exactly what the speakers are
/// playing is the case an echo canceller exists for, and what it should do to that is make it
/// quieter. So the assertion is on energy: the cleaned signal against the raw one, over enough
/// frames for the filter to converge. No decibel figure is pinned - that is a number about one
/// filter on one machine - but "quieter than what went in" is the claim the feature makes.
/// </summary>
public class CleanedMicrophoneTests(ITestOutputHelper output)
{
    private static readonly MicrophoneAnnouncement Announced = MicrophoneFormat.Announced;

    /// <summary>A unit of a tone at the announced rate, starting at a sample offset.</summary>
    private static byte[] Unit(int at, double amplitude = 0.5, double hertz = 440)
    {
        int samples = Announced.FrameSize;
        var unit = new byte[samples * 2];

        for (var i = 0; i < samples; i++)
        {
            double t = (at + i) / (double)Announced.Rate;
            var sample = (short)(amplitude * Math.Sin(2 * Math.PI * hertz * t) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(unit.AsSpan(i * 2), sample);
        }

        return unit;
    }

    /// <summary>Root mean square of 16-bit samples, which is the energy a canceller removes.</summary>
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

    /// <summary>
    /// THE UNIT PICKS THE RATE, not the transform's preference.
    ///
    /// The path moves in ten-millisecond units, so a rate has to divide into a whole number of
    /// samples per one. 22050 gives 220.5 and 11025 gives 110.25 - so the canceller's best two rates
    /// are unusable here, and the stage runs at the third. That is a fact about the unit length, and
    /// a path moving in twenty-millisecond units would get a different answer from the same list.
    /// </summary>
    [Fact]
    public void TheUnitLengthAndNotThePreferencePicksTheRate()
    {
        IReadOnlyList<int> survive = CleanedMicrophone.RatesWholeUnitsSurvive(
            [22050, 16000, 11025, 8000], Announced);

        output.WriteLine(
            $"{MicrophoneFormat.UnitMilliseconds(Announced)} ms units: {string.Join(", ", survive)}");

        Assert.Equal([16000, 8000], survive);

        // The two the arithmetic refuses are rates the transform DOES take, which is what makes this
        // a constraint the unit imposes rather than one the transform does.
        Assert.DoesNotContain(22050, survive);
        Assert.DoesNotContain(11025, survive);

        // And a longer unit would take them: 22050 at twenty milliseconds is 441 whole samples.
        Assert.Contains(
            22050,
            CleanedMicrophone.RatesWholeUnitsSurvive([22050], Announced with { FrameSize = 960 }));
    }

    /// <summary>
    /// The frame the transform is fed is a whole unit at whatever rate it was configured at.
    ///
    /// Read from the running stage rather than from a constant: the rate is the machine's answer, so
    /// what has to hold is that the two sides are the same LENGTH of time - which is what makes a
    /// frame of microphone line up with a frame of reference at all.
    /// </summary>
    [Fact]
    public void TheCancellerFrameIsAWholeUnitAtWhateverRateItTook()
    {
        using var stage = new CleanedMicrophone();

        Assert.Equal(MicrophoneFormat.BytesPerUnit(Announced), stage.UnitBytes);

        if (!stage.Start())
            return;

        output.WriteLine($"{stage.CancellerRate} Hz, {stage.CancellerFrameBytes} byte frames");

        Assert.Equal(CleanedMicrophone.ExpectedCancellerRate, stage.CancellerRate);

        Assert.Equal(
            stage.UnitBytes / (double)Announced.Rate,
            stage.CancellerFrameBytes / (double)stage.CancellerRate,
            3);
    }

    /// <summary>
    /// THE CRITERION: the stage cleans, and the cleaning is read off the samples.
    ///
    /// The microphone hears exactly what the speakers play - a perfect echo, which is the case the
    /// transform exists for - and what comes back is compared with what went in. PP648's rule in one
    /// assertion: every call in the chain returns success either way, so the return codes say
    /// nothing and the energy says everything.
    ///
    /// Enough frames for the filter to converge, and the LAST of them is what is judged: the first
    /// are the filter learning, and averaging them in would measure the convergence rather than the
    /// result.
    /// </summary>
    [Fact]
    public void TheStageCleansAndTheCleaningIsVisibleInTheSamples()
    {
        if (!CleanedMicrophone.IsAvailable())
        {
            output.WriteLine("this machine has no Voice Capture DSP");
            return;
        }

        using var stage = new CleanedMicrophone();
        Assert.True(stage.Start(), $"the stage would not start: 0x{stage.LastError:x8}");

        var into = new byte[stage.UnitBytes * 4];
        double rawEnergy = 0;
        double cleanedEnergy = 0;
        var judged = 0;

        for (var frame = 0; frame < 200; frame++)
        {
            byte[] unit = Unit(frame * Announced.FrameSize);

            // The microphone hears the speakers exactly: an echo with no room in between.
            CleanedUnit pass = stage.Clean(unit, unit, into);

            // The last fifty, once the filter has had a second and a half to converge.
            if (frame >= 150 && pass.Cleaned > 0)
            {
                rawEnergy += Rms(unit);
                cleanedEnergy += Rms(into.AsSpan(0, pass.Cleaned));
                judged++;
            }
        }

        output.WriteLine(
            $"{stage.UnitsCleaned} cleaned, {stage.UnitsWithNothingBack} with nothing back, "
                + $"{judged} judged: raw {rawEnergy / Math.Max(judged, 1):F0} against cleaned "
                + $"{cleanedEnergy / Math.Max(judged, 1):F0}");

        Assert.True(stage.UnitsCleaned > 0, "the stage returned nothing for every unit");
        Assert.True(judged > 0, "no unit reached the judgement window");

        Assert.True(
            cleanedEnergy < rawEnergy,
            $"the cleaned signal is not quieter than the microphone: {cleanedEnergy:F0} against "
                + $"{rawEnergy:F0} - every call succeeded and nothing was cancelled");
    }

    /// <summary>
    /// And a microphone hearing something the speakers are NOT playing keeps it.
    ///
    /// The other half, and the one that says the stage is a canceller rather than a gate. A voice is
    /// not an echo, so subtracting the reference must leave it - a stage that quietened everything
    /// would pass the test above and make the microphone useless.
    /// </summary>
    [Fact]
    public void AVoiceTheSpeakersAreNotPlayingSurvives()
    {
        if (!CleanedMicrophone.IsAvailable())
            return;

        using var stage = new CleanedMicrophone();
        Assert.True(stage.Start());

        var into = new byte[stage.UnitBytes * 4];
        double voiceEnergy = 0;
        double cleanedEnergy = 0;
        var judged = 0;

        for (var frame = 0; frame < 200; frame++)
        {
            int at = frame * Announced.FrameSize;

            // A different tone on each side: what the microphone hears is not what is playing.
            byte[] voice = Unit(at, hertz: 700);
            byte[] playing = Unit(at, hertz: 180, amplitude: 0.2);

            CleanedUnit pass = stage.Clean(voice, playing, into);

            if (frame >= 150 && pass.Cleaned > 0)
            {
                voiceEnergy += Rms(voice);
                cleanedEnergy += Rms(into.AsSpan(0, pass.Cleaned));
                judged++;
            }
        }

        output.WriteLine(
            $"{judged} judged: voice {voiceEnergy / Math.Max(judged, 1):F0} against cleaned "
                + $"{cleanedEnergy / Math.Max(judged, 1):F0}");

        Assert.True(judged > 0);

        // A tenth of what went in is the floor: the transform also does noise suppression and gain
        // control, so an exact match would be asserting a filter's taste rather than that a voice
        // came through it.
        Assert.True(
            cleanedEnergy > voiceEnergy / 10,
            $"the voice did not survive: {cleanedEnergy:F0} against {voiceEnergy:F0}");
    }

    /// <summary>
    /// An empty reference is silence rather than an error, which is what PP698 delivers.
    ///
    /// A loopback client on a render endpoint playing nothing produces NO packets at all, so a
    /// caller with nothing to hand over hands over nothing. The canceller still needs a frame on its
    /// second input, and zeroes are the honest one: there was no echo, so nothing is subtracted.
    /// </summary>
    [Fact]
    public void AnEmptyReferenceIsSilenceAndTheMicrophoneSurvives()
    {
        if (!CleanedMicrophone.IsAvailable())
            return;

        using var stage = new CleanedMicrophone();
        Assert.True(stage.Start());

        var into = new byte[stage.UnitBytes * 4];
        var produced = 0;

        for (var frame = 0; frame < 100; frame++)
            produced += stage.Clean(Unit(frame * Announced.FrameSize), [], into).Cleaned;

        output.WriteLine($"{produced} byte(s) back with no reference at all");

        Assert.True(produced > 0, "a quiet render endpoint stopped the microphone");
    }

    /// <summary>A stage nobody started produces nothing rather than throwing.</summary>
    [Fact]
    public void AnUnstartedStageProducesNothing()
    {
        using var stage = new CleanedMicrophone();

        Assert.False(stage.Running);
        Assert.Equal(default, stage.Clean(new byte[stage.UnitBytes], [], new byte[stage.UnitBytes]));
    }

    /// <summary>Disposing twice is safe, which every failure path in a host does.</summary>
    [Fact]
    public void DisposingTwiceIsSafe()
    {
        var stage = new CleanedMicrophone();

        stage.Start();
        stage.Dispose();
        stage.Dispose();

        Assert.False(stage.Running);
    }

    /// <summary>
    /// THE STAGE SITS BETWEEN THE CAPTURE AND THE ENCODER, which is the criterion's own words.
    ///
    /// Captured bytes into the unit splitter, units through the cleaner, cleaned units into PP694's
    /// encoder, encoded frames into PP706's sender - and packets out. What this adds over PP706's
    /// own end-to-end is the stage in the middle: the encoder is fed what the canceller returned and
    /// not what the microphone heard, and the packets still come out the right length.
    /// </summary>
    [Fact]
    public void ThePathStillRunsWithTheStageInIt()
    {
        if (!CleanedMicrophone.IsAvailable() || !NativeOpusEncoder.IsAvailable())
            return;

        using var stage = new CleanedMicrophone();
        Assert.True(stage.Start());

        using var encoder = new ManagedOpusEncoder();
        Assert.True(encoder.Header(Announced.Rate, Announced.Channels));

        var packets = new List<byte[]>();
        var sender = new ManagedAudioSender(ps5: true, one => packets.Add(one.ToArray()));

        var units = new MicrophoneUnits();
        var cleanedInto = new byte[stage.UnitBytes * 4];
        var encoded = 0;

        for (var frame = 0; frame < 200; frame++)
        {
            byte[] captured = Unit(frame * Announced.FrameSize, hertz: 700);

            units.Take(captured, unit =>
            {
                CleanedUnit pass = stage.Clean(unit, [], cleanedInto);
                if (pass.Cleaned < stage.UnitBytes)
                    return;

                OpusFrameOutcome outcome = encoder.Frame(
                    System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(
                        cleanedInto.AsSpan(0, stage.UnitBytes)),
                    out ReadOnlySpan<byte> opus);

                if (outcome != OpusFrameOutcome.Sent)
                    return;

                encoded++;
                sender.OpusData(opus);
            });
        }

        output.WriteLine(
            $"{units.Emitted} unit(s), {stage.UnitsCleaned} cleaned, {encoded} encoded, "
                + $"{packets.Count} packet(s)");

        Assert.True(encoded > 0, "nothing reached the encoder through the stage");
        Assert.Equal(Math.Max(encoded - 2, 0), packets.Count);
        Assert.All(packets, one => Assert.Equal(sender.PacketBytes, one.Length));
    }
}
