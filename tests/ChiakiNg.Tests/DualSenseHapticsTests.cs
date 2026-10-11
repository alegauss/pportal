using ChiakiNg.Native;
using ChiakiNg.Session;
using ChiakiNg.Settings;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP851: the console's haptic waveform played on a DualSense's actuators, which PP846's fold
/// threw away.
///
/// THREE THINGS ARE ASSERTED AND ONE IS NOT. The endpoint is chosen by the pad's USB ids and not
/// its localised name; the remix puts left and right on channels two and three at the override's
/// level; and once the waveform plays, the fold stops. Whether a pad on the desk FEELS it is a
/// person holding one, which no assertion here can be - the engine calls are exercised on the
/// default endpoint, and skip where a machine has none.
/// </summary>
public class DualSenseHapticsTests(ITestOutputHelper output)
{
    /// <summary>A pad's actuators, recording each frame they were handed.</summary>
    private sealed class Actuators : IHapticsAudio
    {
        public List<byte[]> Played { get; } = [];

        public bool Play(ReadOnlySpan<byte> frame)
        {
            Played.Add(frame.ToArray());
            return true;
        }
    }

    /// <summary>A pad's motors, recording each strength they were told.</summary>
    private sealed class Motor : IRumbleTarget
    {
        public List<ushort> Told { get; } = [];

        public bool Rumble(ushort strength)
        {
            Told.Add(strength);
            return true;
        }
    }

    private static byte[] Frame(short amplitude, int samples = 30)
    {
        var frame = new byte[samples * HapticsRumble.SampleSize];

        for (var i = 0; i < samples; i++)
        {
            BitConverter.TryWriteBytes(frame.AsSpan(i * 4), amplitude);
            BitConverter.TryWriteBytes(frame.AsSpan((i * 4) + 2), amplitude);
        }

        return frame;
    }

    /// <summary>
    /// THE PAD IS ITS USB IDS, read as this machine's MMDevices store spells them.
    ///
    /// The two DualSense paths are the ones measured on this machine (docs/HARDWARE-CONTRACT.md);
    /// the rest are endpoints beside them that a name match could confuse or a loose parse accept -
    /// a USB microphone, the GPU's HDMI audio, a Bluetooth headset whose path also carries VID and
    /// PID, and a Sony pad that is not a DualSense.
    /// </summary>
    [Theory]
    [InlineData(@"{1}.USB\VID_054C&PID_0CE6&MI_00\6&16692FFF&0&0000", true)]
    [InlineData(@"USB\VID_054C&PID_0DF2&MI_00\6&1&0&0000", true)]
    [InlineData(@"{1}.USB\VID_054C&PID_05C4&MI_00\6&1&0&0000", false)]
    [InlineData(@"{1}.USB\VID_0951&PID_16DF&MI_00\6&168D5B31&0&0000", false)]
    [InlineData(@"{1}.HDAUDIO\FUNC_01&VEN_10DE&DEV_00A7&SUBSYS_10285162&REV_1001\5&39A0515B&0&0001", false)]
    [InlineData(@"{1}.BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&000105D6_PID&000A\7&3&0&C0", false)]
    [InlineData(@"{1}.ROOT\STEAMSTREAMINGSPEAKERS\0000", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ThePadIsChosenByItsUsbIds(string? parent, bool pad)
        => Assert.Equal(pad, HapticsEndpoint.IsPad(parent));

    /// <summary>
    /// [0, 0, L, R]: the actuators are the last two channels, which is the client's memset of the
    /// first four bytes and memcpy into the next four.
    /// </summary>
    [Fact]
    public void TheRemixPutsLeftAndRightOnTheLastTwoChannels()
    {
        float[] levels = DualSenseHaptics.Matrix(4, HapticOverrideSetting.Default);

        // Destination-major: two source levels per destination channel.
        Assert.Equal(
            new float[] { 0, 0, 0, 0, 1, 0, 0, 1 },
            levels);
    }

    /// <summary>
    /// INSIDE THE BAND THE LEVEL IS EXACTLY ONE, because the client skips the multiply there; and
    /// outside it the override is the level, which is the gain the client applies per sample.
    /// </summary>
    [Fact]
    public void TheOverrideIsTheLevelOutsideTheBand()
    {
        Assert.Equal(1f, DualSenseHaptics.Matrix(4, 1.005)[4]);
        Assert.Equal(0.5f, DualSenseHaptics.Matrix(4, 0.5)[4]);
        Assert.Equal(0.5f, DualSenseHaptics.Matrix(4, 0.5)[7]);
        Assert.Equal(2f, DualSenseHaptics.Matrix(4, 2.0)[7]);
    }

    /// <summary>
    /// An endpoint with MORE channels keeps them silent, and one with fewer than four is refused -
    /// it is not the device the layout was written for.
    /// </summary>
    [Fact]
    public void TheMatrixIsSizedToTheEndpoint()
    {
        float[] six = DualSenseHaptics.Matrix(6, 1.0);

        Assert.Equal(12, six.Length);
        Assert.Equal(2f, six.Sum());
        Assert.All(six[8..], level => Assert.Equal(0f, level));

        Assert.Throws<ArgumentOutOfRangeException>(() => DualSenseHaptics.Matrix(2, 1.0));
    }

    /// <summary>
    /// ONCE THE WAVEFORM PLAYS, NOTHING IS FOLDED - the client returns before the fold whenever a
    /// haptics device is open. Each frame goes to the actuators whole, the rumble window has
    /// nothing to send, and Off (a setting for pads that can only rumble) does not silence it.
    /// </summary>
    [Fact]
    public void TheWaveformReplacesTheFold()
    {
        var motor = new Motor();
        var actuators = new Actuators();
        var player = new HapticsPlayer();

        player.Arm(motor, RumbleHapticsIntensity.Off);
        player.ArmAudio(actuators);

        byte[] frame = Frame(1000);
        player.Frame(frame);
        player.Frame(frame);

        Assert.True(player.Playing);
        Assert.Equal(2, player.Played);
        Assert.Equal(frame, actuators.Played[0]);
        Assert.Equal(0, player.Folded);
        Assert.Null(player.Tick());
        Assert.Empty(motor.Told);
    }

    /// <summary>
    /// A PAD THAT WAS RUMBLING IS STOPPED when the waveform takes over, because nothing will fold
    /// a frame again to send the stop; and the disarm lets the actuators go with the motors.
    /// </summary>
    [Fact]
    public void ArmingTheWaveformStopsTheMotorOnce()
    {
        var motor = new Motor();
        var player = new HapticsPlayer();
        player.Arm(motor, RumbleHapticsIntensity.Normal);

        player.Frame(Frame(1000));
        Assert.NotNull(player.Tick());

        player.ArmAudio(new Actuators());

        Assert.Equal((ushort)0, motor.Told[^1]);
        Assert.Null(player.Tick());

        player.Disarm();

        Assert.False(player.Playing);
    }

    /// <summary>
    /// THE ENGINE CALLS PP851 ADDED, on whatever endpoint this machine defaults to: a mastering
    /// voice reports its channel count, a source at the haptics rate opens, and an output matrix
    /// is taken. A wrong vtable index fails none of these loudly, which is why each is read back.
    /// </summary>
    [Fact]
    public void AHapticsRateVoiceRoutesThroughAMatrix()
    {
        using var render = new XAudio2Render(
            new MicrophoneAnnouncement(DualSenseHaptics.SourceChannels, 16, DualSenseHaptics.SampleRate, 30));

        RenderResult opened = render.Open();
        output.WriteLine($"{opened}, {render.OutputChannels} channel(s), hr {render.LastError:X8}");

        if (opened == RenderResult.NoDevice)
            return;

        Assert.Equal(RenderResult.Running, opened);
        Assert.True(render.OutputChannels > 0);

        // The default endpoint is rarely four channels, so the matrix is the plain pass-through of
        // left and right onto its first two - what matters here is that the call is the right one.
        var levels = new float[DualSenseHaptics.SourceChannels * render.OutputChannels];
        levels[0] = 1;
        if (render.OutputChannels > 1)
            levels[3] = 1;
        else
            levels[1] = 1;

        Assert.True(render.Route(levels), $"hr {render.LastError:X8}");
        Assert.False(render.Route(new float[1]));

        render.Write(Frame(0));
        Assert.Equal(30, render.FramesSubmitted);
    }

    /// <summary>
    /// A NAMED ENDPOINT THAT DOES NOT EXIST IS NoDevice, not a throw - the state a pad unplugged
    /// between the search and the open leaves.
    /// </summary>
    [Fact]
    public void AMissingEndpointIsNoDevice()
    {
        using var render = new XAudio2Render(
            new MicrophoneAnnouncement(2, 16, DualSenseHaptics.SampleRate, 30),
            "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}");

        RenderResult opened = render.Open();
        output.WriteLine($"{opened}, hr {render.LastError:X8}");

        Assert.NotEqual(RenderResult.Running, opened);
        Assert.False(render.Running);
    }

    /// <summary>
    /// The search runs on a real machine and answers either way: an endpoint when a pad is on a
    /// cable, null otherwise. Logged, because which one this machine gives is the hardware's call.
    /// </summary>
    [Fact]
    public void TheSearchAnswersWithoutThrowing()
    {
        PadEndpoint? found = HapticsEndpoint.Find();
        output.WriteLine(found is { } endpoint ? $"{endpoint.Name} at {endpoint.Id}" : "no DualSense endpoint");

        if (found is { } some)
            Assert.False(string.IsNullOrEmpty(some.Id));
    }
}
