using System.Globalization;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP450: one reader for an integer `#define`, which is the second commonest thing the drift checks
/// do after reading a function body.
///
/// It lives beside <see cref="CFunction"/> and for the reason that class states: a reader named after
/// a subsystem gets copied rather than reused, and the copy is the version that gets the details
/// wrong. Three private ones existed - two regexes inside a class about the AV reorder timeout, one
/// inside a class about touchpad extents - before this was written, and a fourth was about to be.
///
/// TWO BASES, BECAUSE THIS TREE'S C USES BOTH. A size is `0x20` and a count is `3`, in the same file,
/// eleven lines apart. A reader that handled only decimal would return null for half of them, which
/// reads exactly like a define that has been renamed.
/// </summary>
public static class CDefine
{
    /// <summary>
    /// The value of `#define <paramref name="name"/> <value>`, or null where the file has no such
    /// define with an integer value.
    /// </summary>
    /// <remarks>
    /// The name is matched whole, because whitespace is required on both sides of it: asking for
    /// TAKION_INBOUND_STREAMS does not answer with TAKION_INBOUND_STREAMS_MAX, and a define that is a
    /// prefix of another does not silently read the wrong line.
    /// </remarks>
    public static long? Value(string source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(name);

        Match m = Regex.Match(
            source,
            @"^[ \t]*#[ \t]*define[ \t]+" + Regex.Escape(name) + @"[ \t]+(0[xX][0-9a-fA-F]+|\d+)\b",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(1));

        if (!m.Success)
            return null;

        string literal = m.Groups[1].Value;

        return literal.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(literal[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(literal, CultureInfo.InvariantCulture);
    }

    /// <summary>The same, read out of a file. Null where the file cannot be read.</summary>
    public static long? ValueIn(string filePath, string name)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        return File.Exists(filePath) ? Value(File.ReadAllText(filePath), name) : null;
    }
}
