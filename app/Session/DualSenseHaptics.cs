using ChiakiNg.Native;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>
/// PP851: the console's haptic waveform played on the pad's own actuators, which is what a
/// DualSense is for and what PP846's fold throws away.
///
/// The Qt client does three things to a frame before SDL plays it: widens it from two channels to
/// four with the pair in the LAST two slots, scales it by the override with a saturating multiply,
/// and resamples it from 3000 Hz to 48000. ALL THREE ARE THE ENGINE'S HERE. A source voice is
/// created at the stream's own rate and XAudio2 converts it to the endpoint's, and an output matrix
/// puts left and right on channels two and three at the override's level - so a frame goes in as it
/// arrived and there is no buffer, resampler or multiply in managed code.
///
/// THE ENDPOINT IS THE PAD, found by <see cref="HapticsEndpoint"/> from its USB ids. A pad on
/// Bluetooth has none, and <see cref="Open"/> answering null is that ordinary case, not a failure.
/// </summary>
public sealed class DualSenseHaptics : IHapticsAudio, IDisposable
{
    /// <summary>The rate the console sends haptics at - STEAMDECK_HAPTIC_SAMPLING_RATE.</summary>
    public const int SampleRate = 3000;

    /// <summary>Left and right, which is what every haptics frame carries.</summary>
    public const int SourceChannels = 2;

    /// <summary>
    /// What the remix needs of the endpoint: the actuators are channels two and three, so an
    /// endpoint with fewer is not the device the Qt client's [0, 0, L, R] layout was written for.
    /// </summary>
    public const int ActuatorChannels = 4;

    private readonly Lock gate = new();
    private readonly XAudio2Render render;
    private long played;
    private bool disposed;

    private DualSenseHaptics(XAudio2Render render, PadEndpoint endpoint)
    {
        this.render = render;
        Endpoint = endpoint;
    }

    /// <summary>The endpoint this plays on.</summary>
    public PadEndpoint Endpoint { get; }

    /// <summary>Frames handed to the engine.</summary>
    public long Played => Interlocked.Read(ref played);

    /// <summary>
    /// The level matrix that puts a stereo frame on the actuators, scaled by the override.
    /// </summary>
    /// <remarks>
    /// Destination-major, as XAudio2 reads it: row D holds the level of each source channel in
    /// destination channel D. Rows zero and one are silent and rows two and three carry left and
    /// right - the client's <c>memset</c> of the first four bytes and <c>memcpy</c> into the next four.
    ///
    /// INSIDE THE BAND THE LEVEL IS EXACTLY ONE, because the client skips the multiply there rather
    /// than performing it with a number near one; <see cref="HapticOverrideSetting"/> owns the band.
    /// </remarks>
    /// <param name="destinationChannels">The endpoint's channel count, at least four.</param>
    /// <param name="hapticOverride">The setting, where one is the console's own intensity.</param>
    public static float[] Matrix(int destinationChannels, double hapticOverride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destinationChannels, ActuatorChannels);

        float level = HapticOverrideSetting.IsConsoleSetting(hapticOverride) ? 1f : (float)hapticOverride;
        var levels = new float[SourceChannels * destinationChannels];

        levels[(SourceChannels * 2) + 0] = level;
        levels[(SourceChannels * 3) + 1] = level;

        return levels;
    }

    /// <summary>
    /// Open the pad's endpoint and route a 3000 Hz stereo voice onto its actuators.
    /// </summary>
    /// <param name="hapticOverride">The override setting.</param>
    /// <param name="why">Where null comes back, what it was - for the run's log.</param>
    /// <returns>A running player, or null where there is no pad endpoint or it would not open.</returns>
    public static DualSenseHaptics? Open(double hapticOverride, out string why)
    {
        if (HapticsEndpoint.Find() is not { } endpoint)
        {
            why = "no DualSense audio endpoint (a pad on Bluetooth has none)";
            return null;
        }

        var render = new XAudio2Render(
            new MicrophoneAnnouncement(SourceChannels, 16, SampleRate, SampleRate / 100), endpoint.Id);

        RenderResult opened = render.Open();
        if (opened != RenderResult.Running)
        {
            why = $"{endpoint.Name}: {opened} (hr {render.LastError:X8})";
            render.Dispose();
            return null;
        }

        if (render.OutputChannels < ActuatorChannels)
        {
            why = $"{endpoint.Name}: {render.OutputChannels} channel(s), the actuators need {ActuatorChannels}";
            render.Dispose();
            return null;
        }

        if (!render.Route(Matrix(render.OutputChannels, hapticOverride)))
        {
            why = $"{endpoint.Name}: output matrix refused (hr {render.LastError:X8})";
            render.Dispose();
            return null;
        }

        why = $"{endpoint.Name}, {render.OutputChannels} channel(s)";
        return new DualSenseHaptics(render, endpoint);
    }

    /// <inheritdoc/>
    public bool Play(ReadOnlySpan<byte> frame)
    {
        // Under the gate because the frame arrives on the receive thread and the run disposes this
        // on its own: a write into a voice being destroyed is the one race this class can lose.
        lock (gate)
        {
            if (disposed || frame.IsEmpty)
                return false;

            render.Write(frame);
        }

        Interlocked.Increment(ref played);
        return true;
    }

    /// <summary>Stops the voice and lets the engine go.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            render.Dispose();
        }
    }
}
