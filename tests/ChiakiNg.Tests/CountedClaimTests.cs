using System.Text.RegularExpressions;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP280: the line counts the backlog states about files, checked against the files.
///
/// PP73 found that three of four spot-checked task lines no longer matched the tree, and fixed it
/// for the premises a regex can express: PP16, PP30 and PP33 each declare a roadkeep-remaining query
/// now, and roadkeep answers it from the tree on demand. What it could not cover is the sentence
/// next to the number. "takion.c is 1845 lines" is prose, nothing recomputes it, and takion.c is
/// 1868 lines - it grew twenty-three lines and the plan that sizes a rewrite from it did not notice.
///
/// The same drift is what PP33's own section had: it described http.c as the file curl lives in,
/// months after http.c stopped containing a single curl symbol. That one pointed a future worker at
/// the wrong file entirely, which is worse than a stale number and started from the same cause.
///
/// The ledger is deliberately NOT read. docs/CHANGELOG.md records what was true when a task
/// shipped, and an entry saying a file was 1845 lines in April is not wrong because the file grew
/// in August - correcting it would be rewriting history to match the present. Only ROADMAP.md and
/// IMPROVEMENTS.md are checked, because those two describe work that has not happened yet, in the
/// present tense, and a premise there is load-bearing for a decision nobody has made.
/// </summary>
public partial class CountedClaimTests(ITestOutputHelper output)
{
    /// <summary>The two governed files that describe unshipped work, and so speak in the present.</summary>
    public static IReadOnlyList<string> Backlog { get; } = ["docs/ROADMAP.md", "docs/IMPROVEMENTS.md"];

    /// <summary>Where a file the backlog sizes is looked for. Not test\, which shares basenames.</summary>
    public static IReadOnlyList<string> ImplementationTrees { get; } = ["lib", "gui", "shim", "app"];

    /// <summary>One "&lt;file&gt; is &lt;n&gt; lines" claim, and where it was written.</summary>
    public readonly record struct Claim(string Document, int Line, string File, int Lines);

    /// <summary>
    /// Every claim in the backlog. Both orders are matched, because both are written:
    /// "takion.c is 1845 lines" and "bitstream.c 406" in a list continuing an earlier "is".
    /// </summary>
    public static IReadOnlyList<Claim> Claims(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var claims = new List<Claim>();

        foreach (string document in Backlog)
        {
            string path = Path.Combine(root, document.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in ClaimRegex().Matches(lines[i]))
                {
                    claims.Add(new Claim(
                        document, i + 1, match.Groups["file"].Value,
                        int.Parse(match.Groups["lines"].Value, System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
        }

        return claims;
    }

    /// <summary>One "&lt;n&gt; lines of C in &lt;dir&gt;" claim, and where it was written.</summary>
    public readonly record struct TreeClaim(string Document, int Line, string Directory, int Lines);

    /// <summary>
    /// PP285: every claim in the backlog that sizes a DIRECTORY rather than a file.
    ///
    /// The form is deliberately narrow - "24527 lines of C in lib/src" and nothing looser - because
    /// the counting method has to be IN the claim. PP23 said "the 16935 lines in lib/src", and no
    /// reading of the tree produces that number: recursive .c is 24527, non-recursive is 17590,
    /// .c and .h together are 25958, non-blank is 22017. It was wrong by about 45%, and which of
    /// those four it was ever meant to be cannot be recovered. A sentence that does not say how it
    /// counted cannot be checked, so this only matches one that does.
    /// </summary>
    public static IReadOnlyList<TreeClaim> TreeClaims(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var claims = new List<TreeClaim>();

        foreach (string document in Backlog)
        {
            string path = Path.Combine(root, document.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in TreeClaimRegex().Matches(lines[i]))
                {
                    claims.Add(new TreeClaim(
                        document, i + 1, match.Groups["dir"].Value,
                        int.Parse(match.Groups["lines"].Value, System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
        }

        return claims;
    }

    /// <summary>Every .c line under a directory, which is what "lines of C in" is defined to mean.</summary>
    public static int LinesOfCIn(string root, string relative)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(relative);

        string directory = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
            return -1;

        return Directory.EnumerateFiles(directory, "*.c", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}third-party{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Sum(p => File.ReadAllLines(p).Length);
    }

    /// <summary>
    /// THE OTHER GUARD. Every directory the backlog sizes still holds that many lines of C.
    /// </summary>
    [Fact]
    public void EveryTreeClaimMatchesTheDirectory()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.NotNull(root);

        IReadOnlyList<TreeClaim> claims = TreeClaims(root);
        Assert.True(claims.Count >= 1, "no directory-sized claims found - the scan is not working");

        var wrong = new List<string>();

        foreach (TreeClaim claim in claims)
        {
            int actual = LinesOfCIn(root, claim.Directory);
            if (actual < 0)
                wrong.Add($"{claim.Document}:{claim.Line}  {claim.Directory} is not a directory");
            else if (actual != claim.Lines)
                wrong.Add($"{claim.Document}:{claim.Line}  {claim.Directory} says {claim.Lines} lines of C and holds {actual}");
        }

        Assert.True(
            wrong.Count == 0,
            "the backlog sizes unshipped work from these totals, and they are not the tree's:\n  "
                + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// THE GUARD. Every file the backlog gives a line count for still has that many lines.
    /// </summary>
    [Fact]
    public void EveryCountedClaimMatchesTheFile()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.NotNull(root);

        IReadOnlyList<Claim> claims = Claims(root);

        // A regex that stopped matching would pass the loop below over nothing, which is this
        // file's own subject wearing the other hat.
        Assert.True(claims.Count >= 5, $"only {claims.Count} counted claims found - the scan is not working");

        var wrong = new List<string>();

        foreach (Claim claim in claims)
        {
            // The implementation trees only. Six of these basenames exist twice - lib\src\takion.c
            // is the transport and test\takion.c is the C suite's vectors for it - and a claim that
            // takion is 1845 lines of C over raw sockets is plainly about the first. Searching the
            // whole checkout made all six unresolvable, which reads as a broken scan rather than as
            // the answer being obvious.
            string[] found =
            [
                .. ImplementationTrees
                    .Select(tree => Path.Combine(root, tree))
                    .Where(Directory.Exists)
                    .SelectMany(tree => Directory.EnumerateFiles(tree, claim.File, SearchOption.AllDirectories))
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}third-party{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)),
            ];

            if (found.Length != 1)
            {
                wrong.Add($"{claim.Document}:{claim.Line}  {claim.File} matches {found.Length} files, so the claim cannot be checked");
                continue;
            }

            int actual = File.ReadAllLines(found[0]).Length;
            if (actual != claim.Lines)
                wrong.Add($"{claim.Document}:{claim.Line}  {claim.File} says {claim.Lines} lines and is {actual}");
        }

        output.WriteLine($"{claims.Count} counted claim(s) in the backlog, {wrong.Count} that no longer hold");

        Assert.True(
            wrong.Count == 0,
            "the backlog sizes unshipped work from these numbers, and they are not the tree's:\n  "
                + string.Join("\n  ", wrong));
    }

    // "<name>.c is 1845 lines" and "<name>.c 406" - the second is how a list continues one "is"
    // across several files. Deliberately not matching a bare number near a filename with nothing
    // between them, which is most of a sentence.
    [GeneratedRegex(@"(?<file>[A-Za-z0-9_.-]+\.(?:c|h|cpp|cs|qml))\s+(?:is\s+)?(?<lines>\d{2,5})\b(?=\s*(?:lines|and|,|:|\.|$))")]
    private static partial Regex ClaimRegex();

    // "24527 lines of C in lib/src". The "of C in" is required and is the point: it names the
    // counting rule, which is what PP23's "the 16935 lines in lib/src" did not. A directory total
    // stated any other way is not matched here and is not checkable anywhere.
    [GeneratedRegex(@"(?<lines>\d{3,6})\s+lines of C in\s+(?<dir>[A-Za-z0-9_./-]+)")]
    private static partial Regex TreeClaimRegex();
}
