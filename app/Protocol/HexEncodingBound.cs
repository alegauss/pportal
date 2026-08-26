using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP399: how many bytes a hex encoding may take, given the room it writes into.
///
/// holepunch.c's bytes_to_hex clamps its input against the output buffer, and the clamp was
/// inverted: <c>if (len &gt; max_len * 2) len = max_len * 2;</c> where max_len is the SIZE of the
/// destination. Every input byte writes two characters, and the last snprintf writes a terminator
/// after them, so what fits is <c>(max_len - 1) / 2</c>. The test permitted four times that.
///
/// NOTHING OVERFLOWED. All three callers pass a buffer of exactly <c>2 * len + 1</c>, so the clamp
/// has never fired and its being wrong has never shown. That is the argument for correcting it
/// rather than leaving it: a guard exists for the caller who gets it wrong, and this one would have
/// let that caller write four times past the end while looking like it had checked.
///
/// THE SHAPE IS PP346's. There the bound was right and the arithmetic in front of it made the test
/// unreachable; here the bound is the arithmetic and it is inverted. Both read as protection.
/// </summary>
public static class HexEncodingBound
{
    /// <summary>Where the encoder lives.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// How many input bytes fit, writing two characters each plus one terminator.
    /// </summary>
    /// <param name="destinationSize">sizeof the char buffer being written into.</param>
    public static int Fits(int destinationSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(destinationSize);

        return destinationSize == 0 ? 0 : (destinationSize - 1) / 2;
    }

    /// <summary>
    /// How many the old test permitted, kept so the defect is named rather than described.
    /// </summary>
    public static int PermittedAsItWas(int destinationSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(destinationSize);

        return destinationSize * 2;
    }

    /// <summary>
    /// How many bytes an encoding of this many inputs writes, terminator included.
    ///
    /// The last snprintf writes at <c>hex_str + 2 * (len - 1)</c> with room for three, so the byte
    /// after the final pair is written too.
    /// </summary>
    public static int Writes(int inputBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputBytes);

        return inputBytes == 0 ? 0 : (inputBytes * 2) + 1;
    }

    /// <summary>
    /// Whether the C still clamps against the room it has rather than against twice it.
    ///
    /// Both halves: the test and the value assigned, because a corrected test that assigned the old
    /// value would clamp to something larger than it had just refused.
    /// </summary>
    public static bool TheClampIsAgainstTheRoomItHas(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = CFunction.Body(core, "void bytes_to_hex(");
        if (body is null)
            return false;

        // The absent half is the ASSIGNMENT, not the phrase. The first version asked for
        // "max_len * 2" to be gone and the comment above the fix quotes the old test verbatim, so
        // the check failed on the sentence explaining why it exists - PP390's shape, where a
        // comment mentioning a thing was read as the thing.
        return CCall.Mark(body, "if (len > (max_len - 1) / 2)") >= 0
            && CCall.Mark(body, "len = (max_len - 1) / 2;") >= 0
            && CCall.Mark(body, "len = max_len * 2;") < 0;
    }

    /// <summary>
    /// And whether a zero-sized destination is answered before the subtraction.
    ///
    /// max_len is a size_t, so <c>(0 - 1) / 2</c> is not a small number - it is half of SIZE_MAX,
    /// and the clamp would permit everything. The one case where correcting the arithmetic without
    /// looking at its type would have made the guard worse than it was.
    /// </summary>
    public static bool AZeroDestinationLeavesFirst(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = CFunction.Body(core, "void bytes_to_hex(");
        if (body is null)
            return false;

        int guard = CCall.Mark(body, "if (max_len == 0)");
        int clamp = CCall.Mark(body, "if (len > (max_len - 1) / 2)");

        return guard >= 0 && clamp > guard;
    }
}
