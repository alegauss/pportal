using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One byte-order conversion in the C, and the width of the read inside it.</summary>
/// <param name="File">Which file it is in.</param>
/// <param name="Line">The line, 1-based, so a failure is clickable.</param>
/// <param name="Conversion">`ntohs` or `ntohl`.</param>
/// <param name="ReadBits">The width of the pointer cast inside it: 16, 32 or 64.</param>
public readonly record struct ByteOrderRead(string File, int Line, string Conversion, int ReadBits)
{
    /// <summary>How many bits the conversion itself handles.</summary>
    public int ConversionBits => Conversion == "ntohs" ? 16 : 32;

    /// <summary>Whether the conversion matches the read it wraps.</summary>
    public bool Matches => ConversionBits == ReadBits;

    /// <summary>Said the way a failure should read.</summary>
    public override string ToString()
        => $"{File}:{Line}  {Conversion} over a {ReadBits}-bit read";
}

/// <summary>
/// PP374: every byte-order conversion in lib/src handles the width it is given.
///
/// The pad info handler read a timestamp four bytes wide and swapped it with `ntohs`, which takes a
/// uint16_t. The 32-bit value was truncated to its low half BEFORE anything was swapped - on a
/// little-endian host that half is the two MOST significant network bytes, and the two least
/// significant were discarded. The result was the top of the field, in a uint32_t, logged as seconds.
///
/// THE MISMATCH IS THE THING TO CHECK, not the line. Searching for the shape rather than the value
/// found it to be the only one in the tree: six other reads pair `ntohs` with a 16-bit cast, including
/// the seqnum on the line directly above, which is exactly what made this one unremarkable at a
/// glance. So the rule is stated over every conversion, and it also catches the inverse - an `ntohl`
/// over a 16-bit read - which nobody has written yet.
/// </summary>
public static partial class ByteOrderWidths
{
    /// <summary>The files this reads, which is every C source in the library.</summary>
    public const string SourceGlob = "*.c";

    /// <summary>Where the library's C lives.</summary>
    public const string SourceRelativePath = @"lib\src";

    /// <summary>
    /// The directory, or null outside a checkout.
    ///
    /// PP382: through <see cref="SanitizerSource.LocateDirectory(string)"/>. This asked the FILE
    /// locator for a directory and was handed null in every checkout there has ever been, so the
    /// sweep below early-returned and the rule this class exists for never ran once.
    /// </summary>
    public static string? LocateSources() => SanitizerSource.LocateDirectory(SourceRelativePath);

    /// <summary>
    /// PP381: the parenthesis that hid thirty-three of thirty-eight.
    ///
    /// The tree writes both `ntohl(*(T *)x)` and `ntohl(*((T *)x))`, and this matched only the
    /// first - so the sweep read two lines of ctrl.c and three of streamconnection.c, and never
    /// looked at audio.c, frameprocessor.c, senkusha.c or the twenty-five in takion.c. Nothing
    /// failed: a clean sweep over five sites reads exactly like a clean sweep over all of them,
    /// which is why <see cref="Floor"/> now exists beside the rule.
    /// </summary>
    [GeneratedRegex(
        @"(?<conv>ntohs|ntohl)\s*\(\s*\*\s*\(\s*\(?\s*(chiaki_unaligned_)?uint(?<bits>16|32|64)_t\s*\*\s*\)",
        RegexOptions.None)]
    private static partial Regex ConversionOverACast();

    /// <summary>
    /// The fewest conversions a sweep of lib/src may find before the rule is about nothing.
    ///
    /// Thirty-eight today. A floor rather than the exact count, because conversions come and go
    /// with the port and a number that has to be edited on every deletion gets edited without
    /// being read. What it catches is the regex quietly stopping matching, which is the failure
    /// this task exists because of - and which nothing else in the test can see.
    /// </summary>
    public const int Floor = 30;

    /// <summary>
    /// Every conversion wrapping a pointer cast, with the width of each.
    ///
    /// Only reads THROUGH A CAST are matched. A conversion over a plain variable says nothing about
    /// width at the call site - the declaration is where that lives - and guessing at it would produce
    /// findings that cannot be judged from the line.
    /// </summary>
    public static IReadOnlyList<ByteOrderRead> ReadsIn(string file, string source)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(source);

        var reads = new List<ByteOrderRead>();

        foreach (Match match in ConversionOverACast().Matches(source))
        {
            // Commented-out code is not code. The pad info handler has a disabled read directly
            // between the two live ones, and flagging it would be a finding nobody can act on.
            int lineStart = source.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
            string before = source[lineStart..match.Index];
            if (before.Contains("//", StringComparison.Ordinal))
                continue;

            int line = source.Take(match.Index).Count(c => c == '\n') + 1;

            reads.Add(new ByteOrderRead(
                file,
                line,
                match.Groups["conv"].Value,
                int.Parse(match.Groups["bits"].Value)));
        }

        return reads;
    }

    /// <summary>The ones whose conversion does not match the width it was handed.</summary>
    public static IReadOnlyList<ByteOrderRead> Mismatches(IEnumerable<ByteOrderRead> reads)
    {
        ArgumentNullException.ThrowIfNull(reads);

        return reads.Where(r => !r.Matches).ToList();
    }
}
