using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// One recorded erasure case: the frame buffer as it arrived, which units were lost, and the bytes
/// that had to come back.
/// </summary>
/// <param name="K">Data units.</param>
/// <param name="M">Parity units.</param>
/// <param name="Erasures">Which unit indices were lost.</param>
/// <param name="UnitSize">Bytes per unit, before the stride padding.</param>
/// <param name="FrameBuffer">Every unit's true bytes, k+m of them, unpadded and end to end.</param>
public readonly record struct FecCase(
    uint K, uint M, uint[] Erasures, int UnitSize, byte[] FrameBuffer);

/// <summary>
/// PP23 and PP30: the largest oracle this protocol has, read out of the suite that holds it.
///
/// test/fec_test_cases.inl is 3081 lines of erasure cases taken off a real stream. They are not a
/// specification of forward error correction - they are something better for a port, which is
/// sixty-four recorded answers a console's own stream already agreed to.
///
/// Parsed rather than copied, for the reason <see cref="CryptoVectors"/> is: two copies of an
/// oracle agree with each other long after either agrees with hardware.
/// </summary>
public static partial class FecVectors
{
    /// <summary>Where the cases live, relative to the repository root.</summary>
    public const string RelativePath = @"test\fec_test_cases.inl";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The stride the recorded cases use: the unit size rounded up to a multiple of sixteen.
    ///
    /// That padding is the layout the decoder expects and not a convenience of the test - the
    /// units are addressed at stride intervals, so a rewrite that packed them tightly would decode
    /// the wrong bytes into the right places.
    /// </summary>
    public static int StrideFor(int unitSize) => (unitSize + 0xf) / 0x10 * 0x10;

    /// <summary>Every case in the file, in the order it declares them.</summary>
    public static IReadOnlyList<FecCase> Parse(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var cases = new List<FecCase>();
        foreach (Match m in CaseRegex().Matches(File.ReadAllText(filePath)))
        {
            uint[] erasures = m.Groups["erasures"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => int.Parse(t, CultureInfo.InvariantCulture))
                // The list is terminated by -1, which is how the C loop finds its end. It is a
                // sentinel and not an index, so it stops the list rather than joining it.
                .TakeWhile(v => v >= 0)
                .Select(v => (uint)v)
                .ToArray();

            // Adjacent C string literals are one string. The recorded base64 is broken across a
            // hundred lines that way, and joining them is what makes it decodable at all.
            var b64 = new StringBuilder();
            foreach (Match piece in LiteralRegex().Matches(m.Groups["b64"].Value))
                b64.Append(piece.Groups[1].Value);

            cases.Add(new FecCase(
                K: uint.Parse(m.Groups["k"].Value, CultureInfo.InvariantCulture),
                M: uint.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture),
                Erasures: erasures,
                UnitSize: int.Parse(m.Groups["unit"].Value, CultureInfo.InvariantCulture),
                FrameBuffer: Convert.FromBase64String(b64.ToString())));
        }

        return cases;
    }

    [GeneratedRegex(
        @"\{\s*(?<k>\d+)\s*,\s*(?<m>\d+)\s*,\s*\{(?<erasures>[^}]*)\}\s*,\s*(?<b64>(?:""[^""]*""\s*)+),\s*(?<unit>\d+)\s*\}",
        RegexOptions.Singleline)]
    private static partial Regex CaseRegex();

    [GeneratedRegex(@"""([^""]*)""")]
    private static partial Regex LiteralRegex();
}
