using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>
/// PP846: where a rumble goes once the fold has produced one.
///
/// The pad is the only thing that can answer this and <see cref="PadFeed"/> is the only thing that
/// holds one - it owns the SDL handle and the thread SDL was started on, and every controller call
/// has to run there. So the player is armed with a target rather than opening a pad of its own,
/// which is the shape <see cref="ManagedControllerStateSink"/> already has for the other direction.
/// </summary>
public interface IRumbleTarget
{
    /// <summary>
    /// One strength to both motors, or zero to stop them.
    ///
    /// Both motors get the same number because the fold produced one: PP97's Strength takes the
    /// louder channel, and the Qt client's own call is SetHapticRumble(strength, strength).
    /// </summary>
    /// <returns>Whether the pad took it. False for no pad, or an SDL refusal.</returns>
    bool Rumble(ushort strength);
}

/// <summary>
/// PP851: where a haptics frame goes WHOLE, on a pad that can play the waveform rather than a
/// strength folded out of it.
/// </summary>
public interface IHapticsAudio
{
    /// <summary>One frame, stereo int16 at the console's haptics rate, exactly as it arrived.</summary>
    /// <returns>Whether it was taken.</returns>
    bool Play(ReadOnlySpan<byte> frame);
}

/// <summary>
/// PP846: the console's haptics reaching a motor, which nothing had ever done.
///
/// PP97 ported the fold, PP98 fixed the wrap inside it, PP127 built the 47-byte effects report and
/// PP8 wrote both routing rules. All four shipped and the only caller any of them had was a test:
/// audioreceiver.c is used twice in this port and the second one's far side was a NoFrames whose
/// methods are empty, so a console's rumble envelope arrived, was decoded, and went nowhere.
///
/// IT IS NOT ONE RUMBLE PER FRAME, and that is the part a rewrite loses. The Qt client queues each
/// folded strength and a timer averages three of them every thirty milliseconds, because haptics
/// arrive far faster than a motor can respond and a rumble command per frame is a motor being told
/// to change speed hundreds of times a second. The window is the feature, not a buffer.
///
/// AND A ZERO IS SENT ONCE. `strength > 0 || rumble_haptics_on` in the client: while the pad is
/// still, nothing is sent at all, and the single zero that follows a non-zero window is what stops
/// the motor. Drop it and the pad buzzes until the session ends; send it every window and the pad
/// gets thirty-three stop commands a second for as long as nobody is being shot at.
/// </summary>
public sealed class HapticsPlayer : IAudioFrameSink
{
    /// <summary>RUMBLE_HAPTICS_PACKETS_PER_RUMBLE: how many folded frames one rumble averages.</summary>
    public const int PacketsPerRumble = 3;

    /// <summary>The client's own interval - the window times ten milliseconds.</summary>
    public static TimeSpan Interval => TimeSpan.FromMilliseconds(PacketsPerRumble * 10);

    private readonly Queue<ushort> queued = new();
    private readonly object gate = new();

    private IRumbleTarget? target;
    private IHapticsAudio? audio;
    private RumbleHapticsIntensity intensity = RumbleHapticsIntensity.Normal;
    private bool rumbling;
    private bool consoleOff;
    private long folded;
    private long silent;
    private long sent;
    private long played;

    /// <summary>How many frames folded to a strength worth queueing.</summary>
    public long Folded => Interlocked.Read(ref folded);

    /// <summary>
    /// And how many folded to nothing, which is the ordinary case rather than a failure.
    ///
    /// Counted apart because the two answer different questions: silence is a stream carrying no
    /// haptics right now, and zero of BOTH is a stream whose haptics never arrived - which is the
    /// state this task found and which no number reported.
    /// </summary>
    public long Silent => Interlocked.Read(ref silent);

    /// <summary>Rumbles actually handed to a pad, windows and stops alike.</summary>
    public long Sent => Interlocked.Read(ref sent);

    /// <summary>
    /// PP851: frames handed whole to the pad's own actuators, which a fold never reaches.
    /// </summary>
    public long Played => Interlocked.Read(ref played);

    /// <summary>Whether a pad is armed to receive them.</summary>
    public bool Active => target is not null;

    /// <summary>PP851: whether the waveform is going to the actuators rather than being folded.</summary>
    public bool Playing => Volatile.Read(ref audio) is not null;

    /// <summary>PP857: whether the console last said its haptics are off.</summary>
    public bool ConsoleOff => Volatile.Read(ref consoleOff);

    /// <summary>
    /// PP857: the console's haptic intensity, as CHIAKI_EVENT_HAPTIC_INTENSITY raises it.
    ///
    /// ONLY OFF DECIDES ANYTHING HERE. The client keeps it as ps5_rumble_intensity = -1 and returns
    /// before both the fold and the waveform; Strong, Medium and Weak are a byte the effects report
    /// carries, which <see cref="DualSenseIntensity"/> packs, and not a gate. So the console's own
    /// menu beats this client's rumble setting, which is the client's order of checks.
    ///
    /// A PAD THAT WAS RUMBLING IS STOPPED ONCE when Off arrives, for the reason the window keeps:
    /// nothing will fold a frame again to send the stop that would otherwise end it.
    /// </summary>
    public void ConsoleIntensity(DualSenseEffectIntensity intensity)
    {
        bool off = intensity == DualSenseEffectIntensity.Off;

        lock (gate)
        {
            Volatile.Write(ref consoleOff, off);

            if (!off)
                return;

            if (rumbling && target is { } pad)
                pad.Rumble(0);

            rumbling = false;
            queued.Clear();
        }
    }

    /// <summary>
    /// PP851: play every frame whole on the pad's actuators, and stop folding.
    ///
    /// The fold is the client's answer only while haptics_output is zero: once a haptics device is
    /// open it returns before the fold and plays instead. So the two are exclusive here too - a pad
    /// told both would buzz its rumble motors under the waveform it is already playing.
    ///
    /// WHAT WAS QUEUED IS DROPPED, and a pad that was rumbling is stopped first, because nothing will
    /// fold a frame again to send the stop that would otherwise end it.
    /// </summary>
    public void ArmAudio(IHapticsAudio pad)
    {
        ArgumentNullException.ThrowIfNull(pad);

        lock (gate)
        {
            if (rumbling && target is { } motor)
                motor.Rumble(0);

            rumbling = false;
            queued.Clear();
            Volatile.Write(ref audio, pad);
        }
    }

    /// <summary>
    /// The pad, once one is open.
    ///
    /// Late because the pad is: StreamRun opens it off the drawing thread and the phase that holds
    /// this is built before that finishes. A frame arriving before the pad does is folded and
    /// queued rather than dropped, so the first window after a pad arrives is not a silent one.
    /// </summary>
    public void Arm(IRumbleTarget pad, RumbleHapticsIntensity setting)
    {
        ArgumentNullException.ThrowIfNull(pad);

        lock (gate)
        {
            target = pad;
            intensity = setting;
        }
    }

    /// <summary>
    /// Stops the motor and forgets the pad, for a session ending or a pad going away.
    ///
    /// The stop is sent BEFORE the target is dropped and only where one was rumbling, which is the
    /// same rule the window keeps: a pad left buzzing after a session is the failure this exists to
    /// avoid, and a stop for a pad that was still is a command nobody asked for.
    /// </summary>
    public void Disarm()
    {
        lock (gate)
        {
            if (rumbling && target is { } pad)
                pad.Rumble(0);

            rumbling = false;
            target = null;
            Volatile.Write(ref audio, null);
            queued.Clear();
        }
    }

    /// <summary>
    /// header_cb. Nothing here needs it: the fold reads a stereo int16 frame whatever the header
    /// said, and a rumble has no format to reset. Kept because the seam has two methods and an
    /// empty one that says why is not the same as a seam this could not satisfy.
    /// </summary>
    public void Header(in ManagedAudioHeader header)
    {
        // Deliberately nothing - see the summary.
    }

    /// <summary>
    /// One decoded haptics frame, folded and queued.
    ///
    /// Queued even with no pad armed, for the reason <see cref="Arm"/> gives. Off is checked here
    /// and not in the fold, because PP97's own note says the caller checks it before folding at all
    /// - a setting of Off is a user asking for no rumble, not a frame that folds to silence.
    /// </summary>
    public void Frame(ReadOnlySpan<byte> frame)
    {
        // PP857: the console's Off first, ahead of both arms - the client returns on it before the
        // fold and before the waveform alike.
        if (Volatile.Read(ref consoleOff))
            return;

        // PP851: the waveform, ahead of the rumble setting - which is the client's order. Off governs
        // the fold for pads that can only rumble; it is not consulted once a haptics device is open.
        if (Volatile.Read(ref audio) is { } pad)
        {
            if (pad.Play(frame))
                Interlocked.Increment(ref played);

            return;
        }

        if (intensity == RumbleHapticsIntensity.Off)
            return;

        ushort? strength = HapticsRumble.Strength(frame, intensity);

        if (strength is not { } value)
        {
            Interlocked.Increment(ref silent);
            return;
        }

        lock (gate)
        {
            queued.Enqueue(value);
        }

        Interlocked.Increment(ref folded);
    }

    /// <summary>
    /// One window: the mean of the next <see cref="PacketsPerRumble"/> strengths, sent or withheld.
    ///
    /// DIVIDED BY THE WINDOW AND NOT BY WHAT ARRIVED, which is the client's arithmetic and looks
    /// like a bug until you see what it is for. A window holding one loud frame and two gaps
    /// averages to a third of that frame - so a burst of haptics ramps the motor up over three
    /// windows instead of snapping it to full, which is what a motor with inertia does anyway.
    /// </summary>
    /// <returns>What was sent, or null where nothing was.</returns>
    public ushort? Tick()
    {
        lock (gate)
        {
            uint total = 0;
            for (var i = 0; i < PacketsPerRumble; i++)
            {
                if (queued.Count > 0)
                    total += queued.Dequeue();
            }

            var strength = (ushort)(total / PacketsPerRumble);

            // Nothing at all while the pad is still. The one zero that follows a rumble is what
            // stops the motor, and `rumbling` is the whole of what remembers that it needs one.
            if (strength == 0 && !rumbling)
                return null;

            rumbling = strength > 0;

            if (target is not { } pad || !pad.Rumble(strength))
                return null;

            Interlocked.Increment(ref sent);
            return strength;
        }
    }
}
