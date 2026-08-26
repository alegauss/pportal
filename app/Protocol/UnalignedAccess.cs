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
    /// <summary>The file PP378 stated this rule over, kept because that is where it started.</summary>
    public const string RelativePath = @"lib\src\senkusha.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>PP382: where the library's C lives, which is what the rule now reads.</summary>
    public const string SourceRelativePath = @"lib\src";

    /// <summary>The directory, or null outside a checkout. See PP382's note on the locator.</summary>
    public static string? LocateSources() => SanitizerSource.LocateDirectory(SourceRelativePath);

    /// <summary>
    /// PP382: the accesses that keep a plain cast, each because a buffer carries the guarantee.
    ///
    /// PP378 scoped its rule to one file because "does this pointer carry a guarantee" is answered
    /// per buffer and could not be guessed everywhere. PP381's widened reader made the rest of the
    /// tree readable, so the answer is now given per site - and these four are the sites where it
    /// is yes.
    ///
    /// BOTH BUFFERS SAY SO IN THE C. ctrl.c's recv_buf and its eight-byte header are each declared
    /// under <c>__attribute__((aligned(__alignof__(uint32_t))))</c>, and the attribute is there
    /// BECAUSE of these reads. A plain cast on either is the guarantee being used rather than one
    /// being assumed, which is the whole distinction this rule is about.
    ///
    /// An exception is a file and a target, not a line number: line numbers move and the reason
    /// does not. A fifth exception added here without an attribute beside it is the failure mode,
    /// and it is a review question rather than something a regex can judge.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Aligned { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl.c"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "ctrl->recv_buf",
                "header",
                "header + 4",
                "header + 6",
            },
        };

    /// <summary>Whether this access is one of the four that may keep a plain cast.</summary>
    public static bool IsDeliberatelyAligned(PointerAccess access)
        => Aligned.TryGetValue(access.File, out IReadOnlySet<string>? targets)
            && targets.Contains(access.Target);

    /// <summary>
    /// The CAST only, up to its closing parenthesis. What follows it is read by hand.
    ///
    /// PP382: the first version of this ended in <c>\((?&lt;target&gt;[^)]*)\)</c> and so matched
    /// only targets that were parenthesised. Every write in holepunch.c is
    /// <c>*(uint32_t*)&amp;confirm_buf[0]</c> and ctrl.c's guarded read is
    /// <c>*((uint32_t *)ctrl-&gt;recv_buf)</c> - neither has parentheses round the target, and the
    /// sweep saw neither. The inner parenthesis is optional for the same reason PP381 made it
    /// optional one file over: the tree writes both.
    /// </summary>
    [GeneratedRegex(
        @"\*\s*\(\s*\(?\s*(?<unaligned>chiaki_unaligned_)?uint(?<bits>16|32|64)_t\s*\*\s*\)",
        RegexOptions.None)]
    private static partial Regex CastPrefix();

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

        foreach (Match match in CastPrefix().Matches(source))
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
                TargetAfter(source, match.Index + match.Length)));
        }

        return accesses;
    }

    /// <summary>
    /// What the cast is applied to, read from just past it.
    ///
    /// Two shapes, because the tree writes both. A parenthesised target is taken whole, counting
    /// parentheses so <c>(buf + f(x))</c> would survive; a bare one runs to the first delimiter at
    /// bracket depth zero, so <c>&amp;confirm_buf[0x44]</c> keeps its subscript and
    /// <c>ctrl-&gt;recv_buf</c> keeps its arrow.
    /// </summary>
    private static string TargetAfter(string source, int at)
    {
        while (at < source.Length && char.IsWhiteSpace(source[at]))
            at++;

        if (at >= source.Length)
            return string.Empty;

        if (source[at] == '(')
        {
            var depth = 0;
            for (int scan = at; scan < source.Length; scan++)
            {
                if (source[scan] == '(')
                {
                    depth++;
                }
                else if (source[scan] == ')' && --depth == 0)
                {
                    return source[(at + 1)..scan].Trim();
                }
            }

            return source[(at + 1)..].Trim();
        }

        var brackets = 0;
        int start = at;
        for (; at < source.Length; at++)
        {
            char c = source[at];

            if (c == '[')
                brackets++;
            else if (c == ']')
                brackets--;
            else if (brackets == 0 && (c is ')' or ';' or ',' or '=' or '\n'))
                break;
        }

        return source[start..at].Trim();
    }

    /// <summary>
    /// The ones claiming an alignment the pointer does not carry.
    ///
    /// PP382: the four in <see cref="Aligned"/> are not among them - they claim one the buffer
    /// really has. Everything else that reaches for a plain cast is a site where nobody checked.
    /// </summary>
    public static IReadOnlyList<PointerAccess> ClaimingAlignment(IEnumerable<PointerAccess> accesses)
    {
        ArgumentNullException.ThrowIfNull(accesses);

        return accesses.Where(a => !a.IsUnaligned && !IsDeliberatelyAligned(a)).ToList();
    }
}
