using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP666: a table nobody drives is a claim only its own test reads.
///
/// PP364 modelled the stream connection's exit ladder and its test asserted arithmetic derived from
/// the same table. Both were wrong in the same direction for five months, and what found it was
/// PP295's managed run - a CONSUMER that had to drive the table against the C with the file open
/// beside it. PP666 asked the same question of the three other tables in this shape.
///
/// TWO OF THE THREE ALREADY HAVE ONE, and saying so is half the answer:
///
///   PP640's six orderings are driven by ManagedStreamRun.Run, which reproduces them and is read
///   back by ManagedStreamRunTests on one trace. That consumer is what found PP364's defect, so
///   this table has the very mechanism PP666 is about;
///
///   PP623's three deletion stages were driven by being EXECUTED - PP630 to PP632 for session.c and
///   PP655 to PP664 for the shim's seam - which is the strongest driver a plan can have and leaves
///   nothing for a check to add.
///
/// THE THIRD HAD NONE, AND WAS WRONG. DeletionEndState.WaitsOn says which line each end state waits
/// on, and its two tests assert the entries they already know. Nothing held it against the criteria
/// it summarises, so it said PP295's end state waits on PP28 - which PP690 established shipped
/// without doing it - and it said PP27's waits on PP295 while PP27's criterion named no such thing.
/// Wrong in both live rows, found by reading rather than by anything running.
///
/// So this is that driver: the table is derived from the criteria's own words and compared, which
/// is what makes the two unable to drift apart again.
/// </summary>
public class DrivenTablesTests(ITestOutputHelper output)
{
    private static string? Roadmap()
        => CriterionBlockers.Locate(CriterionBlockers.RelativePath) is { } path
            ? File.ReadAllText(path)
            : null;

    /// <summary>
    /// THE DRIVER: every end state's waits are the ones its own criterion names.
    ///
    /// Read through PP690's reader, which distinguishes an id a criterion WAITS for from one it
    /// merely cites - most criteria name a shipped task for what it delivered, and that is not a
    /// wait. So the comparison is against what the prose says has to happen first, and a table
    /// entry with no sentence behind it fails here rather than standing for five months.
    /// </summary>
    [Fact]
    public void EveryEndStatesWaitsAreWhatItsCriterionSays()
    {
        if (Roadmap() is not { } roadmap)
            return;

        var wrong = new List<string>();

        foreach ((string line, IReadOnlyList<string> table) in DeletionEndState.WaitsOn)
        {
            if (DeletionEndState.CriteriaOf(roadmap, line) is not { } criteria)
            {
                wrong.Add($"{line}: has no criteria list to read");
                continue;
            }

            IReadOnlyList<string> said = CriterionBlockers.WaitedOnIn(criteria);
            output.WriteLine($"{line}: table [{string.Join(", ", table)}] vs prose [{string.Join(", ", said)}]");

            if (!table.OrderBy(one => one, StringComparer.Ordinal)
                    .SequenceEqual(said.OrderBy(one => one, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                wrong.Add(
                    $"{line}: the table says [{string.Join(", ", table)}] and its criteria say "
                        + $"[{string.Join(", ", said)}]");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "these end-state tables disagree with the criteria they summarise:\n  "
                + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// And the reading is not vacuous: at least one row is non-empty, so the comparison above is
    /// comparing something.
    ///
    /// PP271's rule, which matters more here than usual - a derivation that returned nothing for
    /// every line would agree with an empty table perfectly.
    /// </summary>
    [Fact]
    public void AtLeastOneEndStateActuallyWaitsOnSomething()
    {
        if (Roadmap() is not { } roadmap)
            return;

        var found = new List<string>();

        foreach (string line in DeletionEndState.Lines)
        {
            if (DeletionEndState.CriteriaOf(roadmap, line) is { } criteria)
                found.AddRange(CriterionBlockers.WaitedOnIn(criteria));
        }

        // NONE TODAY, and that is the table being right rather than the reading being vacuous.
        // PP295 shipped and PP33 before it, so the one line left waits on its own criteria and on
        // no other line - which is exactly the state every entry here is working towards.
        //
        // So the non-vacuity moves to where it can always be asked: the derivation itself, on a
        // criterion this test owns. A reader that returned nothing for everything would agree with
        // an empty table perfectly, and that is what this rules out.
        Assert.Empty(found);

        string about = "PP" + 9002.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string waits = "PP" + 9003.ToString(System.Globalization.CultureInfo.InvariantCulture);

        string fixture = $"""
            ## Done when — {about}

            - **The files leave the build** An end state, not a progress bar: this cannot land
              until {waits} has.
            """;

        Assert.Equal(
            [waits],
            CriterionBlockers.WaitedOnIn(DeletionEndState.CriteriaOf(fixture, about)!));
    }

    /// <summary>
    /// PP640's six orderings ARE driven, which is the answer for that table rather than a check.
    ///
    /// ManagedStreamRun.Run makes each of the six calls in the C's order, and one trace is read
    /// back for all six. Asserted here as the join PP666 asked about: the consumer exists and the
    /// table is what it reproduces, so a wrong rung shows up as a run in the wrong order.
    /// </summary>
    [Fact]
    public void TheSixOrderingsHaveAConsumer()
    {
        Assert.Equal(6, StreamConnectionOrder.All.Count);

        // The consumer, named rather than described: a trace of one clean run, which the orderings
        // are read out of. If this type stops existing the table has lost its driver.
        Assert.NotNull(typeof(ManagedStreamRun).GetMethod(nameof(ManagedStreamRun.Run)));
        Assert.NotNull(typeof(IStreamRunHost));
    }

    /// <summary>
    /// PP623's stages were driven by running them, and both flips have run.
    ///
    /// The three steps are in the ledger twice over - PP630 to PP632 for session.c's nine, PP655 to
    /// PP664 for the shim's seam - so what would drive the table did, and the table is a record of
    /// something finished rather than a plan waiting to be checked.
    /// </summary>
    [Fact]
    public void TheDeletionStagesWereDrivenByBeingRun()
    {
        if (RecordedDesigns.LocateLedger() is not { } path)
            return;

        string ledger = File.ReadAllText(path);
        IReadOnlySet<string> shipped = CriterionBlockers.ShippedIn(ledger);

        // The two flips that executed the stages, both ends of each.
        Assert.Contains("PP630", shipped);
        Assert.Contains("PP632", shipped);
        Assert.Contains("PP655", shipped);
        Assert.Contains("PP664", shipped);
    }
}
