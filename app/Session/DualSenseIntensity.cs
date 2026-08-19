namespace ChiakiNg.Session;

/// <summary>
/// ChiakiDualSenseEffectIntensity, with the console's own numbers.
///
/// They are NOT ordered by strength: Strong is 1, Medium is 2 and Weak is 3. A port that compared
/// them - "anything above Medium is weaker" - would read as sensible code and invert the ladder.
/// </summary>
public enum DualSenseEffectIntensity
{
    Off = 0,
    Strong = 1,
    Medium = 2,
    Weak = 3,
}

/// <summary>
/// PP5: the two intensity events from StreamSession::Event, and the byte they pack into.
///
/// The console tells the client how hard the pad should buzz and how stiff its triggers should be,
/// as two separate events. Both end up in one byte sent to the DualSense: the trigger in the high
/// nibble, the rumble in the low one. Neither mapping is derivable from the enum it comes from,
/// and one of them is nearly derivable, which is worse.
///
///   rumble:  Off -&gt; off,  Strong -&gt; 0x00,  Medium -&gt; 0x02,  Weak -&gt; 0x03
///   trigger: Off -&gt; off,  Strong -&gt; 0x00,  Medium -&gt; 0x60,  Weak -&gt; 0x90
///
/// The rumble codes are the enum's own values for Medium and Weak and NOT for Strong, which is 1
/// as an enum and 0 as a code. Passing the enum straight through would work for three of the four
/// arms and send 0x01 for the fourth - a value the console was never given a meaning for.
///
/// "Off" is not a code. It is held as a negative, which is what gates the two paths that must not
/// run at all - haptics frames and trigger effects are dropped rather than sent at zero - and only
/// becomes a nibble at the moment the byte is packed.
/// </summary>
public sealed class DualSenseIntensity
{
    private const int OffSentinel = -1;

    /// <summary>The nibble an off rumble is packed as.</summary>
    public const byte RumbleOffNibble = 0x0F;

    /// <summary>The nibble an off trigger is packed as.</summary>
    public const byte TriggerOffNibble = 0xF0;

    /// <summary>Both start at Strong, which is StreamSession's own initialiser list.</summary>
    public int RumbleCode { get; private set; }

    /// <inheritdoc cref="RumbleCode"/>
    public int TriggerCode { get; private set; }

    /// <summary>What PushHapticsFrame scales a rumble by. Zero while rumble is off.</summary>
    public double RumbleMultiplier { get; private set; } = 1.0;

    /// <summary>False stops haptics frames being sent at all, rather than sending them at zero.</summary>
    public bool RumbleEnabled => RumbleCode >= 0;

    /// <summary>False stops trigger effects being forwarded at all.</summary>
    public bool TriggerEffectsEnabled => TriggerCode >= 0;

    /// <summary>CHIAKI_EVENT_HAPTIC_INTENSITY.</summary>
    public void SetRumble(DualSenseEffectIntensity intensity)
    {
        (RumbleCode, RumbleMultiplier) = intensity switch
        {
            DualSenseEffectIntensity.Off => (OffSentinel, 0.0),
            DualSenseEffectIntensity.Strong => (0x00, 1.0),
            DualSenseEffectIntensity.Weak => (0x03, 0.33),
            DualSenseEffectIntensity.Medium => (0x02, 0.5),
            _ => (RumbleCode, RumbleMultiplier),
        };
    }

    /// <summary>CHIAKI_EVENT_TRIGGER_INTENSITY.</summary>
    public void SetTrigger(DualSenseEffectIntensity intensity)
    {
        TriggerCode = intensity switch
        {
            DualSenseEffectIntensity.Off => OffSentinel,
            DualSenseEffectIntensity.Strong => 0x00,
            DualSenseEffectIntensity.Weak => 0x90,
            DualSenseEffectIntensity.Medium => 0x60,
            _ => TriggerCode,
        };
    }

    /// <summary>
    /// The byte the pad is sent: the trigger's nibble ORed with the rumble's, with an off side
    /// substituting all-ones for its own half only.
    /// </summary>
    public byte Packed
    {
        get
        {
            byte trigger = TriggerCode < 0 ? TriggerOffNibble : (byte)TriggerCode;
            byte rumble = RumbleCode < 0 ? RumbleOffNibble : (byte)RumbleCode;
            return (byte)(trigger | rumble);
        }
    }
}
