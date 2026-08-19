using System.Buffers.Binary;

namespace ChiakiNg.Session;

/// <summary>settings/rumble_haptics_intensity, in the order settings.h declares it.</summary>
public enum RumbleHapticsIntensity
{
    Off = 0,
    VeryWeak,
    Weak,
    Normal,
    Strong,
    VeryStrong,
}

/// <summary>
/// PP5: the haptics-to-rumble fold from PushHapticsFrame, with the SDL and the controller list off
/// it.
///
/// A DualSense receives haptics as a stereo PCM stream. A pad that has no haptic motors gets this
/// instead: the frame is folded to one rumble strength, scaled by the user's intensity setting,
/// and sent as a single number. It is the whole of what a PS4 pad feels during a PS5 session, and
/// every step of the fold has a decision in it that a rewrite would smooth over.
/// </summary>
public static class HapticsRumble
{
    /// <summary>HAPTIC_RUMBLE_MIN_STRENGTH: below this a channel is treated as silent.</summary>
    public const uint MinStrength = 100;

    /// <summary>
    /// The floor a non-zero strength is raised to, "for controllers that shift up to 9 bits when
    /// rumbling". Anything audible but under 512 would otherwise be shifted away to nothing.
    /// </summary>
    public const ushort NineBitFloor = 1 << 9;

    /// <summary>Stereo int16: four bytes per sample.</summary>
    public const int SampleSize = 4;

    /// <summary>
    /// The rumble strength for one haptics frame, or null when nothing should be sent.
    ///
    /// Null covers three cases the Qt client treats alike by returning early: an empty frame, a
    /// frame whose length is not a whole number of stereo samples, and a frame both of whose
    /// channels sit under <see cref="MinStrength"/>. The third is the common one - it is silence,
    /// and sending a zero would be a rumble command per frame for a pad that should be still.
    /// </summary>
    public static ushort? Strength(ReadOnlySpan<byte> frame, RumbleHapticsIntensity intensity)
    {
        if (frame.Length == 0 || frame.Length % SampleSize != 0)
            return null;

        int samples = frame.Length / SampleSize;
        uint sumLeft = 0;
        uint sumRight = 0;

        for (int i = 0; i < samples; i++)
        {
            int at = i * SampleSize;
            short left = BinaryPrimitives.ReadInt16LittleEndian(frame[at..]);
            short right = BinaryPrimitives.ReadInt16LittleEndian(frame[(at + 2)..]);

            // Doubled on the way in, which is where the headroom goes: |short.MinValue| is 32768
            // and twice that is 65536, one past what a ushort holds. See the narrowing below.
            sumLeft += (uint)Math.Abs((long)left) * 2;
            sumRight += (uint)Math.Abs((long)right) * 2;
        }

        uint tempLeft = sumLeft / (uint)samples;
        uint tempRight = sumRight / (uint)samples;

        // Below the floor is silence, and silence in both channels sends nothing at all.
        tempLeft = tempLeft > MinStrength ? tempLeft : 0;
        tempRight = tempRight > MinStrength ? tempRight : 0;
        if (tempLeft == 0 && tempRight == 0)
            return null;

        ushort left16;
        ushort right16;
        switch (intensity)
        {
            case RumbleHapticsIntensity.VeryWeak:
                (left16, right16) = (Saturate(tempLeft / 5), Saturate(tempRight / 5));
                break;
            case RumbleHapticsIntensity.Weak:
                (left16, right16) = (Saturate(tempLeft / 2), Saturate(tempRight / 2));
                break;
            case RumbleHapticsIntensity.Strong:
                (left16, right16) = (Saturate(tempLeft * 2), Saturate(tempRight * 2));
                break;
            case RumbleHapticsIntensity.VeryStrong:
                (left16, right16) = (Saturate(tempLeft * 5), Saturate(tempRight * 5));
                break;
            default:
                // Normal, and everything else including Off - which never reaches here, because
                // the caller checks it before folding the frame at all.
                //
                // PP98: this branch, and the two that divide, used to narrow without saturating.
                // The two that multiply did, because an overflow there was obvious - but the mean
                // itself already reaches 65536, twice the magnitude of short.MinValue and one past
                // a ushort, so a fully clipped frame wrapped to zero here and the pad was told to
                // rumble at nothing while Strong stayed at full.
                (left16, right16) = (Saturate(tempLeft), Saturate(tempRight));
                break;
        }

        // Anything non-zero is raised to the nine-bit floor, then the louder channel wins: a
        // rumble motor takes one number, not two.
        left16 = left16 is > 0 and < NineBitFloor ? NineBitFloor : left16;
        right16 = right16 is > 0 and < NineBitFloor ? NineBitFloor : right16;
        return Math.Max(left16, right16);
    }

    private static ushort Saturate(uint value) => value > ushort.MaxValue ? ushort.MaxValue : (ushort)value;
}
