using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP846: the console's haptics reaching a motor, which nothing had ever done.
///
/// PP97 ported the fold, PP98 fixed the wrap inside it, PP127 built the 47-byte effects report and
/// PP8 wrote both routing rules. Four shipped tasks whose only caller was a test: audioreceiver.c
/// is used twice in this port and PP797 gave the second one a NoFrames whose methods are empty.
///
/// THE WINDOW IS THE SUBSTANCE and it is what a rewrite would drop. Haptics arrive far faster than
/// a motor can respond, so the client queues each folded strength and averages three of them every
/// thirty milliseconds - and sends a single zero, once, to stop. Both of those are decisions that
/// fail silently: no window is a motor told to change speed hundreds of times a second, and no stop
/// is a pad that buzzes until the session ends.
/// </summary>
public class HapticsPlayerTests(ITestOutputHelper output)
{
    /// <summary>A pad that records what it was told, in the order it was told.</summary>
    private sealed class Motor : IRumbleTarget
    {
        public List<ushort> Told { get; } = [];

        public bool Refuse { get; set; }

        public bool Rumble(ushort strength)
        {
            if (Refuse)
                return false;

            Told.Add(strength);
            return true;
        }
    }

    /// <summary>A frame both of whose channels sit at <paramref name="amplitude"/>.</summary>
    private static byte[] Frame(short amplitude, int samples)
    {
        var frame = new byte[samples * HapticsRumble.SampleSize];

        for (var i = 0; i < samples; i++)
        {
            int at = i * HapticsRumble.SampleSize;
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(at), amplitude);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(at + 2), amplitude);
        }

        return frame;
    }

    /// <summary>
    /// THE WINDOW IS THREE AND THE DIVISOR IS THREE, whatever arrived.
    ///
    /// A window holding one loud frame and two gaps averages to a third of that frame, which looks
    /// like a bug until you see what it buys: a burst of haptics ramps the motor over three windows
    /// instead of snapping it to full, which is what a motor with inertia does anyway. Dividing by
    /// what arrived would send the loud frame at full and make the ramp a step.
    /// </summary>
    [Fact]
    public void OneWindowIsThreeFramesAveragedByThree()
    {
        var motor = new Motor();
        var player = new HapticsPlayer();
        player.Arm(motor, RumbleHapticsIntensity.Normal);

        Assert.Equal(3, HapticsPlayer.PacketsPerRumble);
        Assert.Equal(30, HapticsPlayer.Interval.TotalMilliseconds);

        // One loud frame alone in its window.
        player.Frame(Frame(1000, 8));
        ushort? one = player.Tick();

        Assert.Equal(1, player.Folded);
        Assert.NotNull(one);

        ushort loud = HapticsRumble.Strength(Frame(1000, 8), RumbleHapticsIntensity.Normal)!.Value;
        output.WriteLine($"one frame of {loud} in a window of three sent {one}");

        Assert.Equal((ushort)(loud / 3), one);

        // And three of the same fill it, so the mean is the frame.
        player.Frame(Frame(1000, 8));
        player.Frame(Frame(1000, 8));
        player.Frame(Frame(1000, 8));

        Assert.Equal(loud, player.Tick());
    }

    /// <summary>
    /// A ZERO IS SENT ONCE, and only after a rumble.
    ///
    /// `strength > 0 || rumble_haptics_on` in the client. While the pad is still nothing is sent at
    /// all; the one zero that follows a non-zero window is what stops the motor. Drop it and the
    /// pad buzzes until the session ends, send it every window and a still pad gets thirty-three
    /// stop commands a second.
    /// </summary>
    [Fact]
    public void TheStopIsSentOnceAndOnlyAfterARumble()
    {
        var motor = new Motor();
        var player = new HapticsPlayer();
        player.Arm(motor, RumbleHapticsIntensity.Normal);

        // Still, and nothing is sent - not a zero.
        Assert.Null(player.Tick());
        Assert.Null(player.Tick());
        Assert.Empty(motor.Told);

        player.Frame(Frame(1000, 8));
        Assert.NotNull(player.Tick());

        // The window after a rumble sends the stop.
        Assert.Equal((ushort)0, player.Tick());

        // And the ones after it send nothing again.
        Assert.Null(player.Tick());
        Assert.Null(player.Tick());

        output.WriteLine("told: " + string.Join(", ", motor.Told));

        Assert.Equal(2, motor.Told.Count);
        Assert.NotEqual((ushort)0, motor.Told[0]);
        Assert.Equal((ushort)0, motor.Told[1]);
    }

    /// <summary>
    /// Silence folds to nothing and is counted apart, which is the reading PP838's shape needs.
    ///
    /// A stream carrying no haptics right now and a stream whose haptics never arrived are both
    /// zero rumbles. Silence separates them: a count of folded-to-nothing means frames are reaching
    /// this, and zero of both means they are not.
    /// </summary>
    [Fact]
    public void SilenceIsCountedApartFromNothingArriving()
    {
        var player = new HapticsPlayer();
        player.Arm(new Motor(), RumbleHapticsIntensity.Normal);

        // Under HAPTIC_RUMBLE_MIN_STRENGTH in both channels, which is what the fold calls silence.
        player.Frame(Frame(40, 8));
        player.Frame(Frame(40, 8));

        // And an empty frame, which is the C's concealed one rather than silence.
        player.Frame([]);

        Assert.Equal(0, player.Folded);
        Assert.Equal(3, player.Silent);
        Assert.Null(player.Tick());
    }

    /// <summary>
    /// Off sends nothing at all, and is checked before the fold rather than inside it.
    ///
    /// PP97's own note: the caller checks Off before folding, because a user asking for no rumble is
    /// not a frame that folded to silence - and counting it as silence would report a setting as a
    /// quiet stream.
    /// </summary>
    [Fact]
    public void OffIsNeitherFoldedNorSilent()
    {
        var motor = new Motor();
        var player = new HapticsPlayer();
        player.Arm(motor, RumbleHapticsIntensity.Off);

        player.Frame(Frame(1000, 8));

        Assert.Equal(0, player.Folded);
        Assert.Equal(0, player.Silent);
        Assert.Null(player.Tick());
        Assert.Empty(motor.Told);
    }

    /// <summary>
    /// Frames arriving before the pad does are QUEUED, not dropped.
    ///
    /// The pad is opened off the drawing thread and the phase that holds the player is built before
    /// that finishes, so the first haptics of a session can arrive with no target. Dropping them
    /// would make the first window after a pad appears a silent one for no reason.
    /// </summary>
    [Fact]
    public void FramesBeforeThePadAreKept()
    {
        var player = new HapticsPlayer();

        Assert.False(player.Active);

        player.Frame(Frame(1000, 8));
        player.Frame(Frame(1000, 8));
        player.Frame(Frame(1000, 8));

        Assert.Equal(3, player.Folded);

        // No target: the window runs and sends nothing, and the queue is not consumed by it.
        Assert.Null(player.Tick());

        var motor = new Motor();
        player.Arm(motor, RumbleHapticsIntensity.Normal);

        Assert.True(player.Active);

        player.Frame(Frame(1000, 8));
        player.Frame(Frame(1000, 8));
        player.Frame(Frame(1000, 8));

        Assert.NotNull(player.Tick());
        Assert.NotEmpty(motor.Told);
    }

    /// <summary>
    /// A refused window is not counted as sent, and the disarm still stops a rumbling pad.
    /// </summary>
    [Fact]
    public void ARefusedWindowIsNotSentAndTheDisarmStops()
    {
        var motor = new Motor();
        var player = new HapticsPlayer();
        player.Arm(motor, RumbleHapticsIntensity.Normal);

        motor.Refuse = true;
        player.Frame(Frame(1000, 8));

        Assert.Null(player.Tick());
        Assert.Equal(0, player.Sent);

        motor.Refuse = false;
        player.Frame(Frame(1000, 8));
        Assert.NotNull(player.Tick());
        Assert.Equal(1, player.Sent);

        // Disarming while the motor is running sends the stop, once.
        player.Disarm();

        Assert.False(player.Active);
        Assert.Equal((ushort)0, motor.Told[^1]);

        // And a disarm on a still player sends nothing.
        var still = new Motor();
        var quiet = new HapticsPlayer();
        quiet.Arm(still, RumbleHapticsIntensity.Normal);
        quiet.Disarm();

        Assert.Empty(still.Told);
    }

    /// <summary>
    /// PP846's routing: a DualSense takes the effects report and everything else takes SDL's rumble.
    ///
    /// The Qt client keys on (vendor, product) rather than on SDL_GameControllerGetType, and this
    /// follows it: the type enum tells a DualSense from an Xbox pad, and what decides the ROUTE is
    /// whether this exact device implements the 47-byte report.
    /// </summary>
    [Theory]
    [InlineData(PadIdentity.SonyVendor, PadIdentity.DualSenseProduct, true)]
    [InlineData(PadIdentity.SonyVendor, PadIdentity.DualSenseEdgeProduct, true)]
    // A DualShock 4 is Sony's and takes SDL's rumble, which is the case the fold is FOR.
    [InlineData(PadIdentity.SonyVendor, (ushort)0x09cc, false)]
    // And an Xbox pad, where neither number is Sony's.
    [InlineData((ushort)0x045e, (ushort)0x02ea, false)]
    // The product alone is not enough: a third-party pad reusing the id is not Sony's.
    [InlineData((ushort)0x0079, PadIdentity.DualSenseProduct, false)]
    public void OnlyADualSenseTakesTheEffectsReport(ushort vendor, ushort product, bool report)
    {
        Assert.Equal(report, new PadIdentity(vendor, product, 0x0300).TakesEffectsReport);

        // A pad nothing was read from takes SDL's rumble, which is the safe answer: an effects
        // report to a device that does not implement it is a report the pad ignores in silence.
        Assert.False(default(PadIdentity).TakesEffectsReport);
    }

    /// <summary>
    /// And the firmware split is what the report is built by, not diagnostics.
    ///
    /// PP127's finding: below 0x0224 the values are halved under one enable bit and from that
    /// version they are sent whole under another. Asserted here because PP846 is what first reads a
    /// real pad's firmware - before it, the parameter had no caller passing anything but a fixture.
    /// </summary>
    [Fact]
    public void TheFirmwareDecidesHowTheRumbleBytesAreWritten()
    {
        byte[] older = DualSenseEffects.Rumble(0x80, 0x80, DualSenseEffects.WholeRumbleFirmware - 1, 0);
        byte[] newer = DualSenseEffects.Rumble(0x80, 0x80, DualSenseEffects.WholeRumbleFirmware, 0);

        Assert.Equal(0x40, older[DualSenseEffects.Offset.RumbleLeft]);
        Assert.Equal(0x80, newer[DualSenseEffects.Offset.RumbleLeft]);

        Assert.Equal(
            DualSenseEffects.Bit.Rumble1,
            (byte)(older[DualSenseEffects.Offset.EnableBits1] & DualSenseEffects.Bit.Rumble1));

        Assert.Equal(
            DualSenseEffects.Bit.Rumble3,
            (byte)(newer[DualSenseEffects.Offset.EnableBits3] & DualSenseEffects.Bit.Rumble3));

        // PP8's shift, which is what turns the fold's 16 bits into the report's byte.
        Assert.Equal(0x80, RumbleRouting.ToDualSenseAmplitude(0x8040));
    }

    /// <summary>
    /// The wiring, read out of the two files that hold it.
    ///
    /// PP797 left the haptics arm a NoFrames on purpose and said why; this is what says it is no
    /// longer one, and that the window is driven by a timer rather than by the draw loop - a stream
    /// that stalls with the motor running is exactly when the single stop matters, and a window
    /// driven by frames would stop firing at the same moment.
    /// </summary>
    [Fact]
    public void ThePhaseFeedsThePlayerAndTheRunDrivesTheWindow()
    {
        if (SanitizerSource.LocateRelative(@"app\Session\ManagedStreamPhase.cs") is { } phase)
        {
            string code = CCall.Code(File.ReadAllText(phase));

            Assert.Contains("ManagedAudioReceiverPair(Haptics,", code, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "ManagedAudioReceiverPair(new NoFrames(), new NoFrames())", code, StringComparison.Ordinal);
        }

        if (SanitizerSource.LocateRelative(@"app\Session\StreamRun.cs") is { } run)
        {
            string code = CCall.Code(File.ReadAllText(run));

            // PP850: the arm is asked as a callee and its FIRST argument, so the intensity
            // argument beside it - and any third the setting screen adds - is nobody's business
            // here. `phase.Haptics.Arm(pad` alone would also match a `padding` local.
            Assert.True(
                CCall.CalledWith(code, "phase.Haptics.Arm", "pad"),
                "the run does not arm the player on the pad it opened");

            Assert.True(CCall.Happens(code, "phase.Haptics.Tick()"));
            Assert.True(CCall.Happens(code, "phase.Haptics.Disarm()"));
            Assert.Contains("HapticsPlayer.Interval", code, StringComparison.Ordinal);
        }
    }
}
