using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One multi-byte access through a pointer cast, and whether it claims alignment.</summary>
/// <param name="File">Which file it is in.</param>
/// <param name="Line">The line, 1-based, so a failure is clickable.</param>
/// <param name="Bits">The width of the cast: 16, 32 or 64.</param>
/// <param name="IsUnaligned">Whether it went through <c>chiaki_unaligned_uintN_t</c>.</param>
/// <param name="Target">The expression being cast, which is what says where it points.</param>
public readonly record struct PointerAccess(
    string File, int Line, int Bits, bool IsUnaligned, string Target)
{
    /// <summary>Said the way a failure should read.</summary>
    public override string ToString()
        => $"{File}:{Line}  {(IsUnaligned ? "unaligned" : "PLAIN")} uint{Bits}_t over ({Target})";
}

/// <summary>
/// PP378: every multi-byte access in senkusha.c goes through the unaligned type.
///
/// The pong handler read its tag with a plain <c>uint32_t</c> cast at <c>packet-&gt;data + 4</c>,
/// three lines of reasoning away from three writes in the same file that spell the same thing
/// <c>chiaki_unaligned_uint32_t</c>. The type exists precisely because these offsets carry no
/// alignment guarantee: the pointer is into a received AV packet, at whatever offset its header
/// happened to end, and the read is four bytes past that.
///
/// THE SWAP WAS RIGHT, which is what separates this from PP374. There a four-byte value went through
/// a two-byte swap and the number came out wrong on every packet. Here the number is correct on x86
/// and the defect is the ACCESS - undefined behaviour a compiler is free to assume cannot happen,
/// and which on a stricter target faults rather than answering wrongly.
///
/// THE RULE IS THE FILE, NOT THE LINE. Three writes had the treatment and one read did not, so this
/// was one place that missed what its neighbours got - the same shape as PP367, PP370 and PP377.
/// A rule stated over the file covers a fifth access added later without anyone remembering this.
///
/// AND IT IS SCOPED TO THIS FILE DELIBERATELY. A tree-wide version would flag ctrl.c's reads out of
/// <c>recv_buf</c>, and those are aligned by construction - the field carries
/// <c>__attribute__((aligned(__alignof__(uint32_t))))</c> for exactly that reason. The question
/// "does this pointer carry a guarantee" is answered per buffer, so the rule is stated where the
/// answer is known rather than guessed everywhere.
/// </summary>
public static partial class UnalignedAccess
{
    /// <summary>The file this rule is stated over.</summary>
    public const string RelativePath = @"lib\src\senkusha.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    [GeneratedRegex(
        @"\*\s*\(\s*\(\s*(?<unaligned>chiaki_unaligned_)?uint(?<bits>16|32|64)_t\s*\*\s*\)\s*\((?<target>[^)]*)\)",
        RegexOptions.None)]
    private static partial Regex CastAccess();

    /// <summary>
    /// Every multi-byte access through a pointer cast in the file.
    ///
    /// Only casts are matched, for the reason PP374 gives: an access through a plain variable says
    /// nothing about alignment at the call site, and guessing would produce findings nobody can
    /// judge from the line.
    /// </summary>
    public static IReadOnlyList<PointerAccess> AccessesIn(string file, string source)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(source);

        var accesses = new List<PointerAccess>();

        foreach (Match match in CastAccess().Matches(source))
        {
            // Commented-out code is not code - PP374's exclusion, for the same reason.
            int lineStart = source.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
            if (source[lineStart..match.Index].Contains("//", StringComparison.Ordinal))
                continue;

            accesses.Add(new PointerAccess(
                file,
                source.Take(match.Index).Count(c => c == '\n') + 1,
                int.Parse(match.Groups["bits"].Value),
                match.Groups["unaligned"].Success,
                match.Groups["target"].Value.Trim()));
        }

        return accesses;
    }

    /// <summary>The ones claiming an alignment the pointer does not carry.</summary>
    public static IReadOnlyList<PointerAccess> ClaimingAlignment(IEnumerable<PointerAccess> accesses)
    {
        ArgumentNullException.ThrowIfNull(accesses);

        return accesses.Where(a => !a.IsUnaligned).ToList();
    }
}
