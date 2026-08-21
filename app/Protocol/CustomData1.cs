using System.Text;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: the sixteen bytes the console hides in customData1, behind base64 TWICE.
///
/// The field arrives base64-encoded, and what comes out of that decode is not the payload - it is
/// another base64 string. So the first decode's output is TEXT and the second one's is data, and a
/// port that stopped after one would get twenty-four bytes of base64 characters and no reason to
/// think anything went wrong: the length is plausible and the bytes are printable.
///
/// The length rule is a BAND rather than an equality, and both edges mean something:
///
///   fewer than sixteen bytes is refused, because the sixteen are what the session needs;
///
///   more than twenty is refused, because a field that grew that much is not the field this code
///   was written against;
///
///   and between the two the extras are IGNORED, not refused - the console appends bytes this
///   client has no use for, and a port that demanded exactly sixteen would reject a session the Qt
///   client accepts.
/// </summary>
public static class CustomData1
{
    /// <summary>What the session takes from the field.</summary>
    public const int Length = 16;

    /// <summary>How many bytes beyond that are tolerated and thrown away.</summary>
    public const int ExtraBytesMax = 4;

    /// <summary>Why a decode failed, or that it did not.</summary>
    public enum Result
    {
        Ok,

        /// <summary>Either round was not valid base64.</summary>
        NotBase64,

        /// <summary>Fewer than <see cref="Length"/> bytes came out.</summary>
        TooShort,

        /// <summary>More than <see cref="Length"/> plus <see cref="ExtraBytesMax"/> did.</summary>
        TooLong,
    }

    /// <summary>
    /// The sixteen bytes, or null with a reason.
    /// </summary>
    /// <param name="extras">
    /// How many bytes were thrown away. Reported rather than silent: the Qt client logs it, and a
    /// field that started carrying extras is worth noticing before the day it carries five.
    /// </param>
    public static byte[]? Decode(string customData1, out Result result, out int extras)
    {
        ArgumentNullException.ThrowIfNull(customData1);

        extras = 0;

        if (!TryFromBase64(customData1, out byte[]? round1))
        {
            result = Result.NotBase64;
            return null;
        }

        // The first decode produced TEXT, not data. Read as ASCII because base64 has no character
        // outside it, and a decoder handed the raw bytes would be decoding the same thing anyway -
        // this way the intermediate's nature is visible in the code.
        string round1Text = Encoding.ASCII.GetString(round1!);

        if (!TryFromBase64(round1Text, out byte[]? round2))
        {
            result = Result.NotBase64;
            return null;
        }

        if (round2!.Length < Length)
        {
            result = Result.TooShort;
            return null;
        }

        if (round2.Length > Length + ExtraBytesMax)
        {
            result = Result.TooLong;
            return null;
        }

        extras = round2.Length - Length;
        result = Result.Ok;

        return round2[..Length];
    }

    private static bool TryFromBase64(string text, out byte[]? bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(text);
            return true;
        }
        catch (FormatException)
        {
            bytes = null;
            return false;
        }
    }
}

/// <summary>
/// PP33: customData1's rules where the Qt core states them.
/// </summary>
public static class CustomData1Source
{
    /// <summary>Where the field is decoded.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether it is still decoded twice.</summary>
    public static bool ItIsStillDecodedTwice(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("chiaki_base64_decode(customdata1, strlen(customdata1)", StringComparison.Ordinal)
            && core.Contains("chiaki_base64_decode((const char*)customdata1_round1", StringComparison.Ordinal);
    }

    /// <summary>Whether the tolerated overshoot is still four bytes.</summary>
    public static bool TheBandIsStillFourBytes(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"#define CUSTOMDATA1_EXTRA_BYTES_MAX {CustomData1.ExtraBytesMax}", StringComparison.Ordinal);
    }

    /// <summary>Whether the session still takes sixteen bytes from it.</summary>
    public static bool TheSessionStillTakesSixteen(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"uint8_t custom_data1[{CustomData1.Length}];", StringComparison.Ordinal);
    }

    /// <summary>Whether extras are still ignored rather than refused.</summary>
    public static bool ExtrasAreStillIgnored(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("extra byte(s); ignoring extras", StringComparison.Ordinal);
    }
}
