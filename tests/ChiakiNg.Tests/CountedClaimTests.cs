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
    ///
    /// PP23: the count of such claims is now allowed to be ZERO, and that is not the guard being
    /// dropped. §PP23 held the only directory-sized claim in the tree - "the 25394 lines of C in
    /// lib/src" - and shipping it took the claim with it. The reader is proved on text this test
    /// owns instead, which is what the old "at least one, or the scan is not working" line was for.
    /// </summary>
    [Fact]
    public void EveryTreeClaimMatchesTheDirectory()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.NotNull(root);

        // The reader, proved before it is trusted. A regex that stopped matching would otherwise
        // pass the loop below over nothing and say so about the tree.
        CountedClaim proof = Assert.Single(CountedClaims.In(
            ["there are 25394 lines of C in lib/src and no document to implement against"],
            "synthetic"));

        Assert.True(proof.SizesADirectory);
        Assert.Equal("lib/src", proof.Subject);
        Assert.Equal(25394, proof.Stated);

        CountedClaim[] claims = [.. CountedClaims.All(root).Where(c => c.SizesADirectory)];
        output.WriteLine($"{claims.Length} directory-sized claim(s) in the backlog");

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
        => InATemporaryRoot(prose, CountedClaims.All);

    /// <summary>
    /// One line of prose as the only content of a throwaway checkout, and whatever a reader makes
    /// of it.
    ///
    /// Shared by both readers so they see the same fixture: a scan that disagreed with the quote
    /// check about what a document contains would make one of the two meaningless.
    /// </summary>
    private static IReadOnlyList<CountedClaim> InATemporaryRoot(
        string prose, Func<string, IReadOnlyList<CountedClaim>> read)
    {
        string root = Path.Combine(
            Path.GetTempPath(), "pportal-claims-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllLines(
                Path.Combine(root, "docs", "IMPROVEMENTS.md"), ["### §PP1 A section", "", prose]);

            return read(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// PP412: THE GUARD. No governed section states a size inside quotes.
    ///
    /// PP410's widening turned two claims red: the stale one, and the citation of it inside the
    /// rationale explaining the fix. The second was not a defect, and the reader cannot tell one
    /// from the other - same filename, same shape, same number.
    ///
    /// The alternative was teaching the reader to skip quoted text, which makes a whole syntactic
    /// region invisible to this gate. That is PP410's own defect chosen on purpose, and its section
    /// is what settles it: a claim the scan misses is worse than no claim at all. So the shape is
    /// refused here instead, and an example writes a placeholder where the number would go.
    /// </summary>
    [Fact]
    public void NoSectionStatesASizeInsideQuotes()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.NotNull(root);

        // The sweep must be finding claims at all, or this passes for PP271's reason.
        Assert.True(
            CountedClaims.All(root).Count(c => c.Column >= 0) >= 5,
            "the scan is not working, so this guard is vacuous");

        IReadOnlyList<CountedClaim> quoted = CountedClaims.QuotedClaims(root);

        Assert.True(
            quoted.Count == 0,
            "a quoted example here reads as a claim, and correcting it would make the quotation say "
                + $"something it did not. Write the number as {CountedClaims.NumberPlaceholder}:\n  "
                + string.Join(
                    "\n  ",
                    quoted.Select(c => $"{c.Document}:{c.Line}  \"{c.Text}\"")));
    }

    /// <summary>
    /// And the parity reader itself, on the shapes that matter.
    ///
    /// Including the one this rule exists for: a placeholder inside quotes is not a claim, so it is
    /// not found at all - which is what makes the convention writable rather than merely stated.
    /// </summary>
    [Fact]
    public void QuoteParityDecidesWhatIsInsideAQuotation()
    {
        const string line = "so \"ctrl.c is the longest at 1574 lines\" puts four words between";

        int inside = line.IndexOf("ctrl.c", StringComparison.Ordinal);
        Assert.True(CountedClaims.IsInsideQuotes(line, inside));

        // Before the opening quote, and after the closing one, are both outside.
        Assert.False(CountedClaims.IsInsideQuotes(line, 0));
        Assert.False(
            CountedClaims.IsInsideQuotes(line, line.IndexOf("puts", StringComparison.Ordinal)));

        // A column outside the line is not "inside" anything.
        Assert.False(CountedClaims.IsInsideQuotes(line, -1));
        Assert.False(CountedClaims.IsInsideQuotes(line, line.Length + 10));

        // And the placeholder form holds no digits, so the reader finds no claim to place at all.
        IReadOnlyList<CountedClaim> none = ScanOf(
            $"so \"ctrl.c is the longest at {CountedClaims.NumberPlaceholder} lines\" puts four words");
        Assert.DoesNotContain(none, c => !c.SizesADirectory);
    }

    /// <summary>
    /// PP412: a real quoted size IS found, so the guard above is not passing on a technicality.
    ///
    /// The scan finds it and the parity reader places it inside the quotes. Both halves, against a
    /// synthetic document, because the governed ones are required to hold none.
    /// </summary>
    [Fact]
    public void AQuotedSizeIsFoundAndPlacedInsideItsQuotes()
    {
        const string prose = "the comment said \"ctrl.c is the longest at 1574 lines\" and was wrong";

        CountedClaim claim = Assert.Single(ScanOf(prose), c => !c.SizesADirectory);

        Assert.Equal("ctrl.c", claim.Subject);
        Assert.Equal(1574, claim.Stated);
        Assert.True(claim.Column >= 0);
        Assert.True(CountedClaims.IsInsideQuotes(prose, claim.Column));
    }

    /// <summary>
    /// PP412: and QuotedClaims itself reports it, which is what the guard over the real files runs.
    ///
    /// Against a synthetic document rather than by putting a quoted size into the backlog and taking
    /// it out again: the governed files are required to hold none, so they cannot be the fixture for
    /// the positive case. Both cases here, so "reports none" cannot be passing vacuously.
    /// </summary>
    [Fact]
    public void QuotedClaimsReportsAQuotedSizeAndIgnoresAPlainOne()
    {
        // Quoted: reported.
        CountedClaim offending = Assert.Single(
            QuotedClaimsOf("the comment said \"ctrl.c is the longest at 1574 lines\" and was wrong"));

        Assert.Equal("ctrl.c", offending.Subject);
        Assert.Equal(1574, offending.Stated);

        // Unquoted: a claim, and not this rule's business.
        Assert.Empty(QuotedClaimsOf("ctrl.c is the longest at 1574 lines and carries the most"));

        // Quoted, but with the placeholder: not a claim at all, so nothing to report.
        Assert.Empty(QuotedClaimsOf(
            $"the comment said \"ctrl.c is the longest at {CountedClaims.NumberPlaceholder} lines\""));

        // Quoted, but the number belongs to the next sentence - which the reader does not stitch.
        Assert.Empty(QuotedClaimsOf(
            "so \"http.c is not among them. It is 262 lines over rudp\" stays out"));
    }

    /// <summary>The quoted claims one line of IMPROVEMENTS.md yields, through the real function.</summary>
    private static IReadOnlyList<CountedClaim> QuotedClaimsOf(string prose)
        => InATemporaryRoot(prose, CountedClaims.QuotedClaims);

    /// <summary>
    /// PP417: the printed line IS the argument list rendered, and neither is parsed out of the other.
    ///
    /// This is what makes --apply safe to add: what it runs is what --recount showed. A version that
    /// re-parsed the printed line would be a second implementation of the quoting, which is exactly
    /// where the prose fields - apostrophes, quotes, backticks - stop meaning what they say.
    /// </summary>
    [Fact]
    public void ThePrintedRemedyIsTheArgumentListRendered()
    {
        const string line =
            "- ⏳ **PP293** (deps: PP297 ⏳) **session.c is 1182 lines and owns the session lifetime**"
            + " — the thread itself, over ctrl.c 1469. → §PP293";

        var claim = new CountedClaim(
            "docs/ROADMAP.md", 1, "session.c is 1182", "session.c", 1182, SizesADirectory: false);

        IReadOnlyList<string>? argv =
            CountedClaims.TaskLineRemedyArguments(line, claim, "session.c is 1192");
        Assert.NotNull(argv);

        Assert.Equal(
            ["restate", "PP293", "--symptom", "session.c is 1192 lines and owns the session lifetime"],
            argv);

        // And the line a person reads is exactly that, rendered - not a separately built string.
        Assert.Equal(
            CountedClaims.Render(argv),
            CountedClaims.TaskLineRemedy(line, claim, "session.c is 1192"));
    }

    /// <summary>Verbs, ids and flags render bare; a value after a flag renders quoted.</summary>
    [Fact]
    public void OnlyAValueAfterAFlagIsQuoted()
    {
        Assert.Equal(
            "roadkeep section amend PP28 --replace \"ctrl.c 1713\" --with \"ctrl.c 1726\"",
            CountedClaims.Render(
                ["section", "amend", "PP28", "--replace", "ctrl.c 1713", "--with", "ctrl.c 1726"]));
    }

    /// <summary>
    /// AND THE ARGUMENT CARRIES THE PROSE VERBATIM, which is the property the process call needs.
    ///
    /// A roadmap symptom can hold an apostrophe or a quote. As an argument it is passed through
    /// untouched and reaches roadkeep as one value; only the rendering for a human puts quotes round
    /// it, and that rendering is never what runs.
    /// </summary>
    [Fact]
    public void AValueWithQuotesInItIsPassedThroughUntouched()
    {
        const string awkward = "takion's \"data offset\" is 1868 lines and `wrong but works`";
        const string line =
            "- ⏳ **PP293** (deps: —) **" + awkward + "** — why. → §PP293";

        var claim = new CountedClaim(
            "docs/ROADMAP.md", 1, "is 1868", "takion.c", 1868, SizesADirectory: false);

        IReadOnlyList<string>? argv =
            CountedClaims.TaskLineRemedyArguments(line, claim, "is 1870");
        Assert.NotNull(argv);

        // One argument, holding the apostrophe, the quotes and the backticks as they were.
        Assert.Equal(4, argv.Count);
        Assert.Equal(awkward.Replace("is 1868", "is 1870", StringComparison.Ordinal), argv[3]);
        Assert.Contains('\'', argv[3]);
        Assert.Contains('"', argv[3]);
        Assert.Contains('`', argv[3]);
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
