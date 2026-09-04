using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP690: no criterion waits for a task the ledger already holds.
///
/// PP295's fourth said the four files could not leave until PP28 stopped session.c. PP28 had
/// shipped, and what it shipped was models. PP639 had already released the DEP, so the graph was
/// right and only the sentence was wrong - which is the half no check read, because a criterion is
/// prose and PP584's was the only check reading any of it.
/// </summary>
public class CriterionBlockersTests(ITestOutputHelper output)
{
    private static string? Read(string relative)
        => CriterionBlockers.Locate(relative) is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE CHECK: every id a criterion says it waits for is still open.
    ///
    /// A criterion may cite a shipped task freely for what it DELIVERED, and most of them do - that
    /// is how this port records what closed a thing. What it may not do is name one as something
    /// still to come, because a reader asking what is left would get an answer of nothing.
    /// </summary>
    [Fact]
    public void NoCriterionWaitsForSomethingAlreadyShipped()
    {
        if (Read(CriterionBlockers.RelativePath) is not { } roadmap
            || Read(CriterionBlockers.LedgerRelativePath) is not { } ledger)
        {
            return;
        }

        IReadOnlySet<string> shipped = CriterionBlockers.ShippedIn(ledger);
        output.WriteLine($"{shipped.Count} shipped ids");

        IReadOnlyList<CriterionBlocker> waiting = CriterionBlockers.BlockersIn(roadmap, shipped);

        Assert.True(
            waiting.Count == 0,
            "these criteria wait for tasks that have shipped:\n  "
                + string.Join(
                    "\n  ",
                    waiting.Select(one => $"{one.About}: \"{one.Lead}\" waits on {one.Named}")));
    }

    /// <summary>
    /// And the ledger is actually being read, so the check above is not passing over an empty set.
    ///
    /// PP271's rule: a comparison against nothing matches.
    /// </summary>
    [Fact]
    public void TheLedgerIsThereToRead()
    {
        if (Read(CriterionBlockers.LedgerRelativePath) is not { } ledger)
            return;

        IReadOnlySet<string> shipped = CriterionBlockers.ShippedIn(ledger);

        Assert.True(shipped.Count > 100, $"only {shipped.Count} shipped ids were found");
        Assert.Contains("PP28", shipped);

        // PP666: and a PART is not a ship. `ship --part` writes `**PP295 (the run's ordering)**`
        // and leaves the line open, so counting it would make an open line's blockers unwaitable-on
        // and this check red about a sentence that is right.
        Assert.DoesNotContain("PP295", shipped);
        Assert.DoesNotContain("PP27", shipped);
    }

    /// <summary>The two ledger shapes, told apart on their own text rather than on the real file.</summary>
    [Fact]
    public void APartialShipIsNotAShip()
    {
        const string ledger = """
            - ✅ **PP900** **a whole one** — it is done.
            - ✅ **PP901 (half of it)** **a partial one** — the rest is still open.
            """;

        IReadOnlySet<string> shipped = CriterionBlockers.ShippedIn(ledger);

        Assert.Contains("PP900", shipped);
        Assert.DoesNotContain("PP901", shipped);
    }

    /// <summary>
    /// THE DEFECT ITSELF, as the reader would have seen it: PP295's old sentence, against a ledger
    /// holding PP28.
    ///
    /// Written out rather than described, because a check that cannot demonstrate the thing it
    /// catches is one nobody can review.
    /// </summary>
    [Fact]
    public void PP295sOldFourthCriterionWouldBeCaught()
    {
        const string roadmap = """
            ## Done when — PP295

            - **streamconnection.c, videoreceiver.c, frameprocessor.c and fec.c leave the build** It
              is an end state, not a progress bar: PP638 measured that session.c drives the stream
              connection, so this cannot land until PP28 stops it - and PP28 is what waits on the
              three criteria above. Porting into app removes no C.
            """;

        IReadOnlyList<CriterionBlocker> waiting =
            CriterionBlockers.BlockersIn(roadmap, new HashSet<string>(StringComparer.Ordinal) { "PP28" });

        CriterionBlocker one = Assert.Single(waiting);
        Assert.Equal("PP295", one.About);
        Assert.Equal("PP28", one.Named);
    }

    /// <summary>
    /// And the replacement is NOT caught, which is what says the check reads the verb rather than
    /// the id.
    ///
    /// It cites three shipped tasks - PP623, PP655, PP669, PP638 - for what each delivered, which is
    /// the ordinary and correct shape.
    /// </summary>
    [Fact]
    public void TheReplacementIsNotCaught()
    {
        const string roadmap = """
            ## Done when — PP295

            - **streamconnection.c, videoreceiver.c, frameprocessor.c and fec.c leave the build** The
              end state, and the order is PP623's and PP655's: the counterparts first, which PP669
              mapped; then the one edit that stops session.c asking, which PP638 measured at five
              calls; then the four files. Porting into app removes no C.
            """;

        Assert.Empty(
            CriterionBlockers.BlockersIn(
                roadmap,
                new HashSet<string>(StringComparer.Ordinal) { "PP623", "PP655", "PP669", "PP638" }));
    }

    /// <summary>A criterion waiting on an id that is still OPEN is fine, which is the ordinary case.</summary>
    [Fact]
    public void WaitingOnAnOpenTaskIsFine()
    {
        const string roadmap = """
            ## Done when — PP900

            - **The thing happens** It cannot land until PP901 has landed.
            """;

        Assert.Empty(
            CriterionBlockers.BlockersIn(roadmap, new HashSet<string>(StringComparer.Ordinal) { "PP902" }));
    }

    /// <summary>
    /// The phrase has to REACH the id, so a citation and a wait in one criterion are told apart.
    ///
    /// This is the case that would make the check useless in either direction: too loose and every
    /// "Met by" reads as a wait, too tight and the sentence that caught PP295 slips through.
    /// </summary>
    [Fact]
    public void AWaitAndACitationInOneCriterionAreToldApart()
    {
        Assert.Equal(["PP902"], CriterionBlockers.WaitedOnIn("Met by PP901. It waits on PP902."));
        Assert.Empty(CriterionBlockers.WaitedOnIn("Met by PP901, which mapped every one."));

        // And the phrase before the id in the same sentence, which is the shape PP295 had.
        Assert.Equal(["PP28"], CriterionBlockers.WaitedOnIn("this cannot land until PP28 stops it"));
    }

    /// <summary>Every criterion under a heading is read, not only the first.</summary>
    [Fact]
    public void EveryCriterionUnderAHeadingIsRead()
    {
        const string roadmap = """
            ## Done when — PP900

            - **First** Nothing here.
            - **Second** It waits on PP901.
            - **Third** And this one until PP902.
            """;

        IReadOnlyList<CriterionBlocker> waiting = CriterionBlockers.BlockersIn(
            roadmap, new HashSet<string>(StringComparer.Ordinal) { "PP901", "PP902" });

        Assert.Equal(2, waiting.Count);
        Assert.Equal(["Second", "Third"], waiting.Select(one => one.Lead));
    }

    /// <summary>
    /// A heading that is not a criteria list ends the reading, so a task line elsewhere in the
    /// roadmap is not read as a criterion.
    /// </summary>
    [Fact]
    public void AnotherHeadingEndsTheList()
    {
        const string roadmap = """
            ## Done when — PP900

            - **First** It waits on PP901.

            ## Non-goals

            - **Something** which waits on PP901 too.
            """;

        CriterionBlocker one = Assert.Single(
            CriterionBlockers.BlockersIn(
                roadmap, new HashSet<string>(StringComparer.Ordinal) { "PP901" }));

        Assert.Equal("First", one.Lead);
    }

    /// <summary>PP272: the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Empty(CriterionBlockers.ShippedIn(""));
        Assert.Empty(CriterionBlockers.WaitedOnIn(""));
        Assert.Empty(
            CriterionBlockers.BlockersIn("", new HashSet<string>(StringComparer.Ordinal)));
    }
}
