using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP443: every line total in the backlog is one the recount can find.
///
/// PP280 found two of seven stale and PP410 found three the reader could not see. This is the third
/// instance, and what it adds is not arithmetic - the recount does that - but whether a number is
/// checkable at all. Three were not, and all three were wrong; the one that was anchored was exact.
/// </summary>
public class AnchoredTotalsTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE RULE. No "N lines" in the backlog sits outside a claim the recount recognised.
    /// </summary>
    [Fact]
    public void EveryTotalInTheBacklogIsAnchored()
    {
        IReadOnlyList<UnanchoredTotal> loose = AnchoredTotals.All();

        foreach (UnanchoredTotal total in loose)
            output.WriteLine($"{total.Document}:{total.Line}  {total.Text}");

        Assert.True(
            loose.Count == 0,
            "these state a line total no filename anchors, so the recount has never checked them - "
                + "state the files separately with one number each:\n  "
                + string.Join("\n  ", loose.Select(t => $"{t.Document}:{t.Line} \"{t.Text}\"")));
    }

    /// <summary>
    /// And the reader is working, which is what the rule above rests on.
    ///
    /// PP271: a regex that stopped matching would report nothing loose and be believed. The backlog
    /// does hold "N lines" phrases - PP27's is one - so the count examined is not zero.
    /// </summary>
    [Fact]
    public void TheReaderFindsTheTotalsThatAreThere()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        string roadmap = Path.Combine(root, "docs", "ROADMAP.md");
        if (!File.Exists(roadmap))
            return;

        int examined = AnchoredTotals.Examined(File.ReadAllLines(roadmap));
        output.WriteLine($"{examined} \"N lines\" phrase(s) in the roadmap");

        Assert.True(examined >= 1, "no line totals read from the roadmap - the reader is not working");
    }

    /// <summary>
    /// An unanchored total is reported, and an anchored one is not. Both on synthetic text: the real
    /// backlog is required to hold none, so it cannot be the fixture for the case that matters.
    /// </summary>
    [Fact]
    public void AnUnanchoredTotalIsReportedAndAnAnchoredOneIsNot()
    {
        // PP28's old shape: three modules named without extensions, and a sum.
        UnanchoredTotal loose = Assert.Single(AnchoredTotals.In(
            ["- session, ctrl and streamconnection are 3977 lines of state machine"], "test.md"));

        Assert.Equal(3977, loose.Stated);
        Assert.Equal(1, loose.Line);

        // PP294's shape, which the recount does see.
        Assert.Empty(AnchoredTotals.In(["- ctrl.c is 1763 lines of control channel"], "test.md"));
    }

    /// <summary>
    /// A directory claim anchors a total too - "the 25394 lines of C in lib/src" is the shape Â§PP23
    /// used, and CountedClaims reads it.
    /// </summary>
    [Fact]
    public void ADirectoryClaimAnchorsATotal()
    {
        Assert.Empty(AnchoredTotals.In(
            ["there are 25394 lines of C in lib/src and no document to implement against"],
            "test.md"));
    }

    /// <summary>
    /// Position, not value. A line carrying an anchored total AND a loose one reports only the loose
    /// one, even where the two numbers are equal.
    /// </summary>
    [Fact]
    public void TheAnchoredOneDoesNotCoverTheLooseOne()
    {
        IReadOnlyList<UnanchoredTotal> loose = AnchoredTotals.In(
            ["ctrl.c is 1763 lines and the other two are 1763 lines"], "test.md");

        UnanchoredTotal only = Assert.Single(loose);
        Assert.Equal(1763, only.Stated);

        // It is the SECOND one: the first is inside the claim's span.
        Assert.Contains("1763 lines", only.Text, StringComparison.Ordinal);
    }

    /// <summary>Line numbers are 1-based and count every line, so a report points at the right one.</summary>
    [Fact]
    public void TheLineNumberIsWhereItIs()
    {
        UnanchoredTotal loose = Assert.Single(AnchoredTotals.In(
            ["first", "second", "the three are 4000 lines together"], "test.md"));

        Assert.Equal(3, loose.Line);
    }

    /// <summary>PP272: and empty text holds no totals, anchored or otherwise.</summary>
    [Fact]
    public void EmptyTextHoldsNoTotals()
    {
        Assert.Empty(AnchoredTotals.In([], "test.md"));
        Assert.Empty(AnchoredTotals.In([""], "test.md"));
        Assert.Equal(0, AnchoredTotals.Examined([]));
        Assert.Equal(0, AnchoredTotals.Examined([""]));
    }

    /// <summary>
    /// The files PP443 restated are claims the recount can see, which is what this class is about.
    ///
    /// Two rows have gone: regist.c and discoveryservice.c were PP29's, and shipping PP29 moved its
    /// line into the ledger. A claim in the changelog is not in the backlog this reads, which is
    /// correct - the recount checks what is still open, and a shipped number is history rather than a
    /// promise. The rows go with the line rather than the assertion being loosened to tolerate them.
    ///
    /// PP28 took two more the same way, and by the same rule. Its line carried session.c 1196 and
    /// ctrl.c 1767 alongside streamconnection.c 1531, and shipping it moved all three into the
    /// ledger - so the backlog's counted claims fell from sixteen to ten in one commit. Only
    /// streamconnection.c is still claimed by an open line, PP295's, which is why it is the one of
    /// the three that stayed. Nothing was lost: `--recount` reads what is open, and what is open is
    /// what a promise about a number can still be made of.
    ///
    /// PP594: AND THE NUMBER IS GONE FROM THE ROWS, deliberately.
    ///
    /// It was here as `[InlineData("session.c", 1267)]`, and every comment added to one of these
    /// files made two things stale at once. `ChiakiNg.exe --recount` reads the backlog and prints
    /// the roadkeep call that fixes each claim it finds; it does not read tests/, so it could not
    /// print this one. PP590 added four comment lines to session.c and four to ctrl.c: the tool
    /// named three corrections and the fourth arrived after the build, as a failure about a number
    /// rather than about the file that moved.
    ///
    /// WHAT THE NUMBER ASSERTED WAS ALREADY ASSERTED. CountedClaimTests.EveryCountedClaimMatchesTheFile
    /// holds every counted claim in the backlog against the tree, this one included, and it is where
    /// the arithmetic belongs. What only this row says is that the subject is still a claim the
    /// reader RECOGNISES - PP410's finding was three totals sitting in a shape it could not see, and
    /// all three were wrong precisely because nothing was checking them.
    ///
    /// So the row keeps the half that is this class's and drops the half that was a second copy. A
    /// file whose claim disappears, or slips back into an unreadable shape, still turns this red -
    /// and `--recount` is now the only place a .c line change is answered.
    /// </summary>
    /// <remarks>
    /// PP295: streamconnection.c was the second row and has gone with the line that sized it. A
    /// shipped line's number is in the ledger, which this class deliberately does not read - an
    /// entry saying a file was 1540 lines is a record of what was true, not a premise sizing work
    /// nobody has done. takion.c's claim is PP27's and is still open, so the row that stays is the
    /// one still deciding something.
    /// </remarks>
    [Theory]
    [InlineData("takion.c")]
    public void TheRestatedFilesAreCheckedClaimsNow(string file)
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<CountedClaim> claims = CountedClaims.All(root);

        Assert.Contains(claims, claim => claim.Subject == file && !claim.SizesADirectory);
    }

    /// <summary>
    /// PP594: and no row here states a number, so the double bookkeeping cannot come back quietly.
    ///
    /// The rule is about this file rather than about the suite: a count asserted where `--recount`
    /// cannot read it is a correction the tool cannot print, and the next person to add a comment to
    /// a .c file pays for it twice. Read off the attributes themselves, because a comment saying so
    /// is what the rows already had.
    ///
    /// THE COMPILER CATCHES THE EASY HALF, which was measured rather than assumed: putting 2007 back
    /// on the takion.c row is xUnit1011 - "no matching method parameter for value" - and this project
    /// carries TreatWarningsAsErrors, so it does not build. What the analyzer cannot see is a number
    /// arriving WITH a parameter to hold it, which is exactly how the old rows were written and how
    /// they would be written again. That case is this one's.
    /// </summary>
    [Fact]
    public void NoRowInThisFileStatesALineCount()
    {
        IEnumerable<InlineDataAttribute> rows = typeof(AnchoredTotalsTests)
            .GetMethod(nameof(TheRestatedFilesAreCheckedClaimsNow))!
            .GetCustomAttributes(typeof(InlineDataAttribute), inherit: false)
            .Cast<InlineDataAttribute>();

        var stated = new List<string>();
        var subjects = new List<string>();

        foreach (InlineDataAttribute row in rows)
        {
            foreach (object? value in row.GetData(null!).Single())
            {
                if (value is int number)
                    stated.Add(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (value is string subject)
                    subjects.Add(subject);
            }
        }

        // PP271's shape: reflection that found nothing would report no numbers and be believed.
        //
        // Asserted by NAME and not by count. This guard used to demand four rows, and PP28 shipping
        // took two of them - session.c and ctrl.c, whose claims went into the ledger with the line
        // that made them. A floor that falls every time a line ships is a floor somebody lowers
        // without reading, which is the opposite of what a guard is for. takion.c is PP27's and PP27
        // is open; the day that stops being true this says so instead of quietly passing.
        Assert.Contains("takion.c", subjects);

        Assert.True(
            stated.Count == 0,
            "these rows state a count `--recount` cannot see, so one .c edit needs two fixes and the "
                + "tool can only name one: " + string.Join(", ", stated));
    }
}
