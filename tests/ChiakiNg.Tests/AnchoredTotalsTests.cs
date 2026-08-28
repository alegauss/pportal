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
    /// A directory claim anchors a total too - "the 25394 lines of C in lib/src" is the shape §PP23
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
    /// The numbers PP443 restated are anchored, named so the fix is legible in the suite.
    ///
    /// Two rows have gone: regist.c and discoveryservice.c were PP29's, and shipping PP29 moved its
    /// line into the ledger. A claim in the changelog is not in the backlog this reads, which is
    /// correct - the recount checks what is still open, and a shipped number is history rather than a
    /// promise. The rows go with the line rather than the assertion being loosened to tolerate them.
    /// </summary>
    [Theory]
    [InlineData("takion.c", 1979)] // PP451, PP474, PP491 then PP499 added lines; --recount restated, this follows
    [InlineData("session.c", 1263)] // PP470 added 19 lines; --recount restated the claim, this follows
    [InlineData("streamconnection.c", 1531)]
    public void TheRestatedNumbersAreCheckedClaimsNow(string file, int stated)
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<CountedClaim> claims = CountedClaims.All(root);

        Assert.Contains(claims, claim => claim.Subject == file && claim.Stated == stated);
    }
}
