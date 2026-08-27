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
///
/// PP304 moved the scanning and the counting into <see cref="CountedClaims"/>, so that
/// `ChiakiNg.exe --recount` answers the same question this does and additionally prints the
/// correction. The assertions stayed here; only where they read from changed.
/// </summary>
public class CountedClaimTests(ITestOutputHelper output)
{
    /// <summary>A backlog in the two shapes this reads, for the resolution tests.</summary>
    private static readonly string[] Improvements =
    [
        "### §PP28 The state machines",
        "",
        "session.c is 1182 lines, ctrl.c 1469 and streamconnection.c 1326. Together they are the",
        "connection: what is sent in which order.",
        "",
        "### §PP293 The session thread",
        "",
        "The thread itself: init, start, the connect sequence, stop and join.",
    ];

    /// <summary>
    /// THE OTHER GUARD. Every directory the backlog sizes still holds that many lines of C.
    /// </summary>
    [Fact]
    public void EveryTreeClaimMatchesTheDirectory()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.NotNull(root);

        CountedClaim[] claims = [.. CountedClaims.All(root).Where(c => c.SizesADirectory)];
        Assert.True(claims.Length >= 1, "no directory-sized claims found - the scan is not working");

        IReadOnlyList<string> wrong = Stale(root, claims);

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

        CountedClaim[] claims = [.. CountedClaims.All(root).Where(c => !c.SizesADirectory)];

        // A regex that stopped matching would pass the loop below over nothing, which is this
        // file's own subject wearing the other hat.
        Assert.True(claims.Length >= 5, $"only {claims.Length} counted claims found - the scan is not working");

        IReadOnlyList<string> wrong = Stale(root, claims);
        output.WriteLine($"{claims.Length} counted claim(s) in the backlog, {wrong.Count} that no longer hold");

        Assert.True(
            wrong.Count == 0,
            "the backlog sizes unshipped work from these numbers, and they are not the tree's:\n  "
                + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// PP304: the anchor is the nearest heading ABOVE the claim, which is the part a person gets
    /// wrong. session.c is sized in §PP28 and described in §PP293, and addressing the correction to
    /// the second is a refusal rather than a wrong write - but it is a round trip either way.
    /// </summary>
    [Fact]
    public void AClaimIsAddressedToTheSectionItIsIn()
    {
        Assert.Equal("PP28", CountedClaims.AnchorAbove(Improvements, 3));
        Assert.Equal("PP293", CountedClaims.AnchorAbove(Improvements, 8));

        // Above the first heading there is no section, and no remedy to offer.
        Assert.Null(CountedClaims.AnchorAbove(Improvements, 0));
    }

    /// <summary>The corrected text is the claim's own with one number replaced, and nothing else.</summary>
    [Fact]
    public void OnlyTheNumberChanges()
    {
        var claim = new CountedClaim(
            "docs/IMPROVEMENTS.md", 3, "session.c is 1182", "session.c", 1182, SizesADirectory: false);

        Assert.Equal("session.c is 1192", CountedClaims.Corrected(claim, 1192));
    }

    /// <summary>
    /// A roadmap line's two prose fields take two different verbs, and which one the number is in
    /// decides between them. `restate` writes the symptom and refuses a why; `amend` the reverse.
    /// </summary>
    [Fact]
    public void ARoadmapClaimNamesTheVerbForTheFieldItIsIn()
    {
        const string line =
            "- ⏳ **PP293** (deps: PP297 ⏳) **session.c is 1182 lines and owns the session lifetime**"
            + " — the thread itself, over ctrl.c 1469. → §PP293";

        var inSymptom = new CountedClaim(
            "docs/ROADMAP.md", 1, "session.c is 1182", "session.c", 1182, SizesADirectory: false);
        string? symptom = CountedClaims.TaskLineRemedy(line, inSymptom, "session.c is 1192");
        Assert.NotNull(symptom);
        Assert.StartsWith("roadkeep restate PP293 --symptom \"session.c is 1192 lines", symptom);

        var inWhy = new CountedClaim(
            "docs/ROADMAP.md", 1, "ctrl.c 1469", "ctrl.c", 1469, SizesADirectory: false);
        string? why = CountedClaims.TaskLineRemedy(line, inWhy, "ctrl.c 1479");
        Assert.NotNull(why);
        Assert.StartsWith("roadkeep amend PP293 --why \"", why);
        Assert.Contains("ctrl.c 1479", why, StringComparison.Ordinal);

        // And the pointer is not part of either field.
        Assert.DoesNotContain("§PP293", why, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP410: a number separated from its filename by words is still that filename's claim.
    ///
    /// The pattern allowed "is" between the two and nothing else, which left three claims in the
    /// backlog unscanned - and unscanned meant unchecked, so one of them sat 139 lines stale while
    /// this file's own guard reported every claim holding. These are the three shapes.
    /// </summary>
    [Theory]
    [InlineData("ctrl.c is the longest at 1574 lines and carries the most", "ctrl.c", 1574)]
    [InlineData("takion.c is 1868 lines plus takionsendbuffer.c at 267 and more", "takion.c", 1868)]
    [InlineData("regist.c sits at 918 lines, and", "regist.c", 918)]
    public void AClaimSeparatedFromItsFilenameByWordsIsStillRead(
        string prose, string subject, int stated)
    {
        CountedClaim[] claims = [.. ScanOf(prose).Where(c => c.Subject == subject)];

        CountedClaim claim = Assert.Single(claims);
        Assert.Equal(stated, claim.Stated);
        Assert.False(claim.SizesADirectory);
    }

    /// <summary>And the list continuations on one line are read as the several claims they are.</summary>
    [Fact]
    public void EveryClaimOnOneLineIsRead()
    {
        CountedClaim[] claims =
            [.. ScanOf("takion.c is 1868 lines plus takionsendbuffer.c at 267 and reorderqueue.c at 200: the")];

        Assert.Equal(3, claims.Length);
        Assert.Equal(
            [("takion.c", 1868), ("takionsendbuffer.c", 267), ("reorderqueue.c", 200)],
            claims.Select(c => (c.Subject, c.Stated)));
    }

    /// <summary>
    /// THE BOUND, WHICH IS WHAT KEEPS THIS A READER RATHER THAN A GUESSER.
    ///
    /// A number in the next sentence belongs to its subject only to a person reading the prose, and
    /// a run of words long enough to cross a clause is long enough to pick up somebody else's
    /// number. Both stay out, and staying out is the correct answer rather than a gap.
    /// </summary>
    [Theory]
    [InlineData("http.c is not among them. It is 262 lines over rudp and winsock and")]
    [InlineData("ctrl.c is a thing this sentence keeps talking about well past any bound at 999 lines")]
    [InlineData("session.c is described in the section above, and the tree holds 24527 lines")]
    public void ANumberTooFarFromItsFilenameIsNotClaimedForIt(string prose)
    {
        Assert.DoesNotContain(ScanOf(prose), c => !c.SizesADirectory);
    }

    /// <summary>Every file claim one line of IMPROVEMENTS.md yields, read through the real scan.</summary>
    private static IReadOnlyList<CountedClaim> ScanOf(string prose)
    {
        string root = Path.Combine(
            Path.GetTempPath(), "pportal-claims-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllLines(
                Path.Combine(root, "docs", "IMPROVEMENTS.md"), ["### §PP1 A section", "", prose]);

            return CountedClaims.All(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A line that is not a task line has no remedy rather than a wrong one.</summary>
    [Fact]
    public void AShapeItDoesNotKnowIsSaidRatherThanGuessed()
    {
        var claim = new CountedClaim(
            "docs/ROADMAP.md", 1, "session.c is 1182", "session.c", 1182, SizesADirectory: false);

        Assert.Null(CountedClaims.TaskLineRemedy("## Block F — Managed core", claim, "session.c is 1192"));
    }

    /// <summary>The claims the tree disagrees with, as the message the guards print.</summary>
    private static IReadOnlyList<string> Stale(string root, IReadOnlyList<CountedClaim> claims)
    {
        var wrong = new List<string>();

        foreach (CountedClaim claim in claims)
        {
            int actual = CountedClaims.Actual(root, claim);

            if (actual < 0)
                wrong.Add($"{claim.Document}:{claim.Line}  {claim.Subject} does not resolve to one thing, so the claim cannot be checked");
            else if (actual != claim.Stated)
                wrong.Add($"{claim.Document}:{claim.Line}  {claim.Subject} says {claim.Stated} and is {actual}");
        }

        return wrong;
    }
}
