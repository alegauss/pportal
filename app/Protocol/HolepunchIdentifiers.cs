using System.Globalization;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: the three small helpers the hole punching turns identifiers with, each wrong in its own
/// direction.
///
/// HEX DECODING NEITHER REFUSES NOR FILLS. <c>hex_to_bytes</c> clamps a string that is too long and
/// carries on - so a duid of a hundred and twenty-eight characters becomes the first thirty-two
/// bytes and reports success. A string that is too SHORT is accepted as well, and the bytes it did
/// not reach are left exactly as they were: the caller's device struct is a stack local that nobody
/// zeroes, so a short duid produces a device identifier ending in whatever the stack held, returned
/// as a success. And an ODD length reads its last two-character field off the end of the digits,
/// so "abcde" decodes to three bytes with the last one being a single nibble.
///
/// This port fills the destination and refuses the rest. A truncated identifier is not behaviour
/// worth carrying across - it is a device id that does not name a device - and the uninitialised
/// tail has no managed equivalent at all. Both divergences are pinned to the source so they stay
/// visible.
///
/// HEX ENCODING GUARDED THE WRONG WAY, AND PP399 CORRECTED IT. <c>bytes_to_hex</c> tested
/// <c>len > max_len * 2</c> before writing <c>len * 2 + 1</c> characters, so its bound permitted
/// four times the room it had. It clamps against <c>(max_len - 1) / 2</c> now, and answers a
/// zero-sized destination before subtracting - which a size_t makes necessary.
///
/// TWO THINGS THIS PARAGRAPH GOT WRONG, kept rather than quietly corrected because both are the
/// reason it was only recorded. It said the guard was safe because there is a SINGLE call site;
/// there are three, and all three happen to pass 2n+1 buffers. And "safe today" was the whole
/// argument for leaving it - which is an argument about the callers, not about the guard, and a
/// guard exists for the caller who gets it wrong.
///
/// Unlike PP235's misnamed logs, nothing here said the defect had to be reproduced. It was found
/// and written down, and a finding written down is not a decision to keep it.
///
/// AND THE SESSION UUID IS NOT RANDOM. <c>random_uuidv4</c> calls <c>srand(time(NULL))</c> on every
/// invocation and then draws from <c>rand()</c> - so two sessions created within the same second
/// get the SAME identifier, deterministically, and the same client run twice a second apart can
/// predict its own next one. The file has <c>chiaki_random_bytes_crypt</c> a few hundred lines away
/// and uses it for the five bytes of PP205; this is the identifier the whole session is keyed by.
///
/// The SHAPE is reproduced exactly - the dashes, the version nibble, the variant nibble - and the
/// numbers come from a real generator. The collision is demonstrated in a test rather than shipped.
/// </summary>
public static class HolepunchIdentifiers
{
    /// <summary>How long a device identifier is, in bytes.</summary>
    public const int DeviceUidLength = 32;

    /// <summary>And how long its text is.</summary>
    public const int DeviceUidTextLength = DeviceUidLength * 2;

    /// <summary>How much room the hex form is given, terminator included.</summary>
    public const int DeviceUidBuffer = 65;

    /// <summary>How long a UUID's text is.</summary>
    public const int UuidLength = 36;

    /// <summary>Where the dashes go.</summary>
    public static IReadOnlyList<int> UuidDashes { get; } = [8, 13, 18, 23];

    /// <summary>Where the version nibble goes, and it is always a four.</summary>
    public const int UuidVersionAt = 14;

    /// <summary>Where the variant nibble goes, and it is one of four values.</summary>
    public const int UuidVariantAt = 19;

    /// <summary>The digits a UUID is spelled with.</summary>
    public const string HexDigits = "0123456789abcdef";

    /// <summary>
    /// The bytes of a hex string, or null when it is not exactly the length asked for.
    ///
    /// The core clamps and fills what it can; this refuses - see the class note.
    /// </summary>
    public static byte[]? HexToBytes(string hex, int length)
    {
        ArgumentNullException.ThrowIfNull(hex);

        if (hex.Length != length * 2)
            return null;

        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            if (!byte.TryParse(
                hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
            {
                return null;
            }

            bytes[i] = value;
        }

        return bytes;
    }

    /// <summary>The hex form of some bytes, lowercase, as the core writes it.</summary>
    public static string BytesToHex(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return string.Concat(bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// How many characters <c>bytes_to_hex</c> would write for this many bytes, against the bound
    /// it actually tests. The two disagreeing is the finding.
    /// </summary>
    public static bool TheCoresEncoderWouldOverrun(int length, int buffer)
        => length <= buffer * 2 && (length * 2) + 1 > buffer;

    /// <summary>
    /// A version 4 UUID in the core's shape, with the digits taken from
    /// <paramref name="next"/> - which stands in for <c>rand()</c>.
    /// </summary>
    public static string Uuid(Func<int, int> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        char[] text = new char[UuidLength];
        for (int i = 0; i < UuidLength; i++)
        {
            if (UuidDashes.Contains(i))
                text[i] = '-';
            else if (i == UuidVersionAt)
                text[i] = '4';
            else if (i == UuidVariantAt)
                text[i] = HexDigits[next(4) + 8];
            else
                text[i] = HexDigits[next(16)];
        }

        return new string(text);
    }

    /// <summary>One from a real generator, which is what this port uses.</summary>
    public static string Uuid() => Uuid(System.Security.Cryptography.RandomNumberGenerator.GetInt32);
}

/// <summary>
/// PP33: the helpers' rules where the Qt core states them.
/// </summary>
public static class HolepunchIdentifiersSource
{
    /// <summary>Where they live.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether hex decoding still clamps a long string instead of refusing it.</summary>
    public static bool TheDecoderStillClamps(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if (len > max_len * 2) {", StringComparison.Ordinal)
            && core.Contains("len = max_len * 2;", StringComparison.Ordinal);
    }

    /// <summary>Whether it still walks two characters at a time with no length check.</summary>
    public static bool TheDecoderStillReadsInPairs(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("for (size_t i = 0; i < len; i += 2) {", StringComparison.Ordinal)
            && core.Contains("sscanf(hex_str + i, \"%2hhx\", &bytes[i / 2])", StringComparison.Ordinal);
    }

    /// <summary>Whether the device struct the decoder fills is still an unzeroed stack local.</summary>
    public static bool TheDeviceIsStillAnUnzeroedLocal(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("ChiakiHolepunchDeviceInfo device;", StringComparison.Ordinal)
            && !core.Contains("ChiakiHolepunchDeviceInfo device = {0};", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the encoder still guards a length it then multiplies past.
    ///
    /// PP399 corrected it, so this now answers FALSE and the assertion that used it was inverted
    /// rather than deleted: the shape it looks for is the one to notice coming back, and a check
    /// that only said the new guard is present would not recognise the old one returning under a
    /// different spelling.
    /// </summary>
    public static bool TheEncoderStillGuardsTheWrongWay(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // Anchored on the DEFINITION and not the prototype, which is spelled the same but for the
        // brace and appears first in the file.
        int at = core.IndexOf(
            "static void bytes_to_hex(const uint8_t* bytes, size_t len, char* hex_str, size_t max_len) {",
            StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = core.IndexOf("static void random_uuidv4(char* out)", at, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string body = core[at..end];
        return body.Contains("if (len > max_len * 2) {", StringComparison.Ordinal)
            && body.Contains("snprintf(hex_str + i * 2, 3, \"%02x\", bytes[i]);", StringComparison.Ordinal);
    }

    /// <summary>Whether the UUID is still reseeded from the clock on every call.</summary>
    public static bool TheUuidIsStillSeededFromTheClock(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return CCall.Happens(core, "srand((unsigned int)time(NULL))")
            && core.Contains("out[i] = hex[rand() % 16];", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the same file still has a crypto generator - which is what makes the line above a
    /// choice rather than an absence.
    /// </summary>
    public static bool ACryptoGeneratorIsStillInTheSameFile(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("chiaki_random_bytes_crypt(", StringComparison.Ordinal);
    }

    /// <summary>Whether the UUID's shape is still the one this port reproduces.</summary>
    public static bool TheUuidShapeIsStillTheSame(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if (i == 8 || i == 13 || i == 18 || i == 23) {", StringComparison.Ordinal)
            && core.Contains("} else if (i == 14) {", StringComparison.Ordinal)
            && core.Contains("out[i] = '4';", StringComparison.Ordinal)
            && core.Contains("} else if (i == 19) {", StringComparison.Ordinal)
            && core.Contains("out[i] = hex[(rand() % 4) + 8];", StringComparison.Ordinal);
    }
}
