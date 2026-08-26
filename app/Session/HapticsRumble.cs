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

/// <summary>
/// PP8: which motor a rumble goes to, and in which units - the last of the input path.
///
/// The strength <see cref="HapticsRumble"/> produces is one 16-bit number, and it reaches the pad
/// two different ways. A DualSense gets it through the effects report of PP127, whose rumble
/// fields are BYTES; anything else gets it through SDL_GameControllerRumble, which takes the
/// 16-bit value whole and a duration in milliseconds.
///
/// So there are two decisions here, both easy to lose and neither loud.
/// </summary>
public static class RumbleRouting
{
    /// <summary>
    /// The 16-bit strength as the byte a DualSense effects report carries.
    ///
    /// A SHIFT and not a scale. Dividing by 257 to map 0..65535 onto 0..255 would be the tidier
    /// arithmetic and would differ by one across most of the range - which for a rumble motor is
    /// nothing, except that the Qt client shifts, and two clients disagreeing by one on every
    /// haptic frame is a difference nobody can measure and everybody could argue about.
    /// </summary>
    public static byte ToDualSenseAmplitude(ushort strength) => (byte)(strength >> 8);

    /// <summary>
    /// The duration SDL is given, in milliseconds.
    ///
    /// SDL's rumble STOPS on its own when this expires, so it is not a formality: a session that
    /// stopped re-sending would have the pad fall silent five seconds later rather than keep
    /// buzzing, which is the safer failure and the reason a duration is passed at all.
    /// </summary>
    public const uint SdlRumbleDurationMs = 5000;
}

/// <summary>
/// PP8: the routing rules as the Qt client spells them.
/// </summary>
public static partial class RumbleRoutingSource
{
    /// <summary>The Qt client's controller code.</summary>
    public const string RelativePath = @"gui\src\controllermanager.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the DualSense path still shifts by eight rather than scaling.</summary>
    public static bool DualSenseAmplitudeIsShifted(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return CCall.Happens(text, "SetDualSenseRumble(left >> 8, right >> 8)");
    }

    /// <summary>Whether every other pad still gets the whole value and the same duration.</summary>
    public static bool OtherPadsGetTheWholeValue(string text, uint durationMs)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains(
            $"SDL_GameControllerRumble(controller, left, right, {durationMs});", StringComparison.Ordinal);
    }
}
