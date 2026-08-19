using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP127: the 47-byte effects report a DualSense is sent, built here instead of by SDL_zero and
/// a struct.
///
/// This is a wire format to hardware. Every field is a fixed offset in a buffer handed to
/// SDL_GameControllerSendEffect, so a port that placed one wrongly does not fail - it sets the
/// LED instead of the trigger, or rumbles the wrong motor, and the pad reports nothing at all.
/// controllermanager.cpp writes it through a struct, which is why the offsets are implicit there
/// and explicit here; <see cref="DualSenseSource"/> holds them against that struct's own layout.
///
/// Three reports, and each carries a decision rather than a payload:
///
///   rumble depends on the pad's FIRMWARE. Below 0x0224 the values are halved and enable bit
///   0x01 is set; from 0x0224 they are sent whole under bit 0x04 of the third enable byte. A port
///   that picked one branch rumbles at half strength on half the pads in the world, or not at all
///   on the other half, and neither says anything;
///
///   the trigger effects are ten bytes behind a type byte, twice, and the two are not adjacent to
///   each other in the order a reader expects - RIGHT comes first;
///
///   and the mic light and the mic mute are one report with two bits, so turning the light on
///   without the mute is a state the pad can be left in.
/// </summary>
public static class DualSenseEffects
{
    /// <summary>sizeof(DS5EffectsState_t).</summary>
    public const int ReportSize = 47;

    /// <summary>The firmware from which rumble values are sent whole rather than halved.</summary>
    public const int WholeRumbleFirmware = 0x0224;

    /// <summary>Offsets into the report, as controllermanager.cpp's struct lays them out.</summary>
    public static class Offset
    {
        public const int EnableBits1 = 0;
        public const int EnableBits2 = 1;
        public const int RumbleRight = 2;
        public const int RumbleLeft = 3;
        public const int MicLightMode = 8;
        public const int AudioMuteBits = 9;
        public const int RightTriggerEffect = 10;
        public const int LeftTriggerEffect = 21;
        public const int Unknown1 = 32;
        public const int EnableBits3 = 38;
    }

    /// <summary>The bits each report turns on, as the Qt client ORs them.</summary>
    public static class Bit
    {
        public const byte Rumble1 = 0x01;      // EnableBits1, firmware below 0x0224
        public const byte Intensity = 0x02;    // EnableBits1
        public const byte LeftTrigger = 0x04;  // EnableBits1
        public const byte RightTrigger = 0x08; // EnableBits1
        public const byte MicLight = 0x01;     // EnableBits2
        public const byte Mic = 0x02;          // EnableBits2
        public const byte Effects = 0x40;      // EnableBits2
        public const byte Rumble3 = 0x04;      // EnableBits3, firmware 0x0224 and up
    }

    /// <summary>
    /// The rumble report, with the firmware split the Qt client makes.
    ///
    /// <paramref name="intensity"/> is the user's DualSense intensity setting, which rides in
    /// rgucUnknown1[4] - an unnamed field at offset 36, and the kind of thing a port drops by
    /// reading the struct's names rather than its bytes.
    /// </summary>
    public static byte[] Rumble(byte left, byte right, int firmwareVersion, byte intensity)
    {
        var report = new byte[ReportSize];

        if (firmwareVersion < WholeRumbleFirmware)
        {
            // Halved, because the older firmware scales what it is given differently. Sending the
            // full value there is not louder, it is the same rumble the newer pads give at half.
            report[Offset.EnableBits1] |= Bit.Rumble1;
            report[Offset.RumbleLeft] = (byte)(left >> 1);
            report[Offset.RumbleRight] = (byte)(right >> 1);
        }
        else
        {
            report[Offset.EnableBits3] |= Bit.Rumble3;
            report[Offset.RumbleLeft] = left;
            report[Offset.RumbleRight] = right;
        }

        report[Offset.Unknown1 + 4] = intensity;
        report[Offset.EnableBits2] |= Bit.Effects;
        report[Offset.EnableBits1] |= Bit.Intensity;
        return report;
    }

    /// <summary>
    /// The trigger effects report: a type byte and ten bytes of parameters, for each trigger.
    ///
    /// Both are always sent, even when only one is being changed - the report has no way to say
    /// "leave the other alone", so a caller changing one has to resend the other as it was.
    /// </summary>
    public static byte[] TriggerEffects(
        byte typeLeft, ReadOnlySpan<byte> dataLeft,
        byte typeRight, ReadOnlySpan<byte> dataRight, byte intensity)
    {
        if (dataLeft.Length != 10)
            throw new ArgumentException("a trigger effect is ten bytes", nameof(dataLeft));
        if (dataRight.Length != 10)
            throw new ArgumentException("a trigger effect is ten bytes", nameof(dataRight));

        var report = new byte[ReportSize];

        report[Offset.Unknown1 + 4] = intensity;
        report[Offset.EnableBits2] |= Bit.Effects;
        report[Offset.EnableBits1] |= Bit.LeftTrigger | Bit.RightTrigger;

        report[Offset.LeftTriggerEffect] = typeLeft;
        dataLeft.CopyTo(report.AsSpan(Offset.LeftTriggerEffect + 1, 10));
        report[Offset.RightTriggerEffect] = typeRight;
        dataRight.CopyTo(report.AsSpan(Offset.RightTriggerEffect + 1, 10));
        return report;
    }

    /// <summary>
    /// The microphone report. The light and the mute move together here, which is why they are
    /// one call: a pad left lit with an open mic is the state that matters to a person.
    /// </summary>
    public static byte[] Microphone(bool muted)
    {
        var report = new byte[ReportSize];

        report[Offset.EnableBits2] |= Bit.MicLight | Bit.Mic;
        report[Offset.MicLightMode] = muted ? (byte)0x01 : (byte)0x00;
        report[Offset.AudioMuteBits] = muted ? (byte)0x08 : (byte)0x00;
        return report;
    }
}

/// <summary>
/// PP127: the DS5 report's layout and bits, read out of controllermanager.cpp.
///
/// The offsets above are implicit in the Qt client - it writes through a struct whose field order
/// IS the layout, and the struct carries its byte offsets in trailing comments. Those comments are
/// what this reads, so the port's explicit numbers and the C++'s implicit ones cannot drift apart
/// without something going red.
/// </summary>
public static partial class DualSenseSource
{
    /// <summary>The Qt client's controller code.</summary>
    public const string RelativePath = @"gui\src\controllermanager.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Each DS5EffectsState_t field with the byte offset its own trailing comment claims.
    ///
    /// The comments and not a computed layout: they are what a reader of that struct believes,
    /// and if they ever disagree with the fields the port would be right about the wrong thing.
    /// </summary>
    public static IReadOnlyDictionary<string, int> FieldOffsets(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Match block = StructRegex().Match(text);
        if (!block.Success)
            return new Dictionary<string, int>();

        return FieldRegex().Matches(block.Groups[1].Value)
            .ToDictionary(m => m.Groups["name"].Value, m => int.Parse(m.Groups["off"].Value));
    }

    /// <summary>Whether the firmware split is still where the rumble values are halved.</summary>
    public static bool RumbleIsHalvedBelowFirmware(string text, int firmware)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains($"firmware_version < 0x{firmware:x4}", StringComparison.Ordinal)
            && text.Contains("state.ucRumbleLeft = left >> 1;", StringComparison.Ordinal)
            && text.Contains("state.ucRumbleRight = right >> 1;", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"typedef struct\s*\{(.*?)\}\s*DS5EffectsState_t;", RegexOptions.Singleline)]
    private static partial Regex StructRegex();

    [GeneratedRegex(@"Uint8\s+(?<name>\w+)(?:\[\d+\])?;\s*/\*\s*(?<off>\d+)\s*\*/")]
    private static partial Regex FieldRegex();
}
