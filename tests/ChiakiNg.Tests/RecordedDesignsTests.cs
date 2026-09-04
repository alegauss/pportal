using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP642: every design the ledger says was recorded into a file is in that file.
///
/// `ship --recorded-in` checks that the path RESOLVES and never opens it, so an entry can name a
/// file that never received the paragraph - and the flag is passed in the same call that deletes
/// the section it claims to have moved. Two hand-written checks had already grown for two entries,
/// each saying in its own docstring that it stood in for this one.
/// </summary>
public class RecordedDesignsTests(ITestOutputHelper output)
{
    /// <summary>The ledger, or null outside a checkout.</summary>
    private static string? Ledger()
        => RecordedDesigns.LocateLedger() is { } path ? File.ReadAllText(path) : null;

    /// <summary>Reads a path the ledger spells with forward slashes, or null where it is gone.</summary>
    private static string? Read(string where)
    {
        string? path = SanitizerSource.LocateRelative(where.Replace('/', Path.DirectorySeparatorChar));
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE CHECK: every clause's file resolves and names the id.
    ///
    /// The join is the assertion ratchet's, and exactly as strong: it cannot tell a recording from a
    /// mention, and it can tell a recording from nothing at all - which is what a paragraph that was
    /// never written looks like.
    /// </summary>
    [Fact]
    public void EveryRecordedDesignIsInTheFileTheLedgerNames()
    {
        if (Ledger() is not { } ledger)
            return;

        IReadOnlyList<string> missing = RecordedDesigns.NotRecorded(ledger, Read);

        Assert.True(
            missing.Count == 0,
            "the ledger says these designs were recorded and the files do not carry them:\n  "
                + string.Join("\n  ", missing));
    }

    /// <summary>
    /// And there are clauses to check, so the test above is not passing over an empty set.
    ///
    /// PP271's rule. Seven entries carry one today; the number is not asserted, because a new ship
    /// with the flag should not turn this red.
    /// </summary>
    [Fact]
    public void TheClausesAreThereToRead()
    {
        if (Ledger() is not { } ledger)
            return;

        IReadOnlyList<RecordedDesign> recordings = RecordedDesigns.In(ledger);
        foreach (RecordedDesign one in recordings)
            output.WriteLine($"{one.Id} -> {one.Where}");

        Assert.NotEmpty(recordings);

        // The two that already have hand-written checks are among them, which is what says the
        // reader finds the same clauses those two found by hand.
        Assert.Contains(recordings, one => one.Id == "PP11");
        Assert.Contains(recordings, one => one.Id == "PP647");
    }

    /// <summary>
    /// Every exemption still names a clause the ledger carries, so a row cannot outlive its subject.
    ///
    /// The ratchet's own rule for the same shape: a list that outlives what it exempts is one nobody
    /// notices is wrong, and what it costs here is a recording nothing checks.
    /// </summary>
    [Fact]
    public void EveryExemptionStillHasAnEntry()
    {
        if (Ledger() is not { } ledger)
            return;

        IReadOnlyList<string> stale = RecordedDesigns.ExemptionsWithNoEntry(ledger);

        Assert.True(
            stale.Count == 0,
            "these exemptions name a clause the ledger no longer carries: " + string.Join(", ", stale));
    }

    /// <summary>And every one carries a reason, because no reason is a loophole rather than a record.</summary>
    [Fact]
    public void EveryExemptionCarriesItsReason()
    {
        Assert.Empty(RecordedDesigns.ExemptionsWithNoReason());
        Assert.NotEmpty(RecordedDesigns.Exempt);
    }

    /// <summary>
    /// The exempt file really cannot carry an id: it is a recording whose first line is its format's
    /// version, and every line after it is tab-separated fields.
    ///
    /// Asserted rather than asserted-in-prose, because the exemption's whole justification is a
    /// claim about the file and a claim about a file is checkable.
    /// </summary>
    [Fact]
    public void TheExemptFileIsARecordingAndNotProse()
    {
        foreach (UnnameableRecording one in RecordedDesigns.Exempt)
        {
            if (Read(one.Where) is not { } text)
                continue;

            string[] lines = text.ReplaceLineEndings("\n").Split('\n');

            Assert.StartsWith("chiaki-exchange-", lines[0], StringComparison.Ordinal);
            Assert.Contains('\t', lines[1]);
        }
    }

    /// <summary>The reader finds a clause and reports the pair, on the ledger's own spelling.</summary>
    [Fact]
    public void TheReaderFindsAClause()
    {
        const string ledger =
            "- ✅ **PP900** **something did not work** — it does now "
            + "(design recorded in `some/where.md`).";

        RecordedDesign one = Assert.Single(RecordedDesigns.In(ledger));

        Assert.Equal("PP900", one.Id);
        Assert.Equal("some/where.md", one.Where);
    }

    /// <summary>An entry with no clause yields nothing, which is most of the ledger.</summary>
    [Fact]
    public void AnEntryWithNoClauseYieldsNothing()
    {
        Assert.Empty(RecordedDesigns.In("- ✅ **PP900** **something** — it does now."));
        Assert.Empty(RecordedDesigns.In(""));
    }

    /// <summary>
    /// A missing file and a file that does not name the id are different sentences, because they are
    /// different mistakes: one is a path that moved and the other is a paragraph that never arrived.
    /// </summary>
    [Fact]
    public void TheTwoFailuresAreToldApart()
    {
        const string ledger =
            "- ✅ **PP900** **a** — b (design recorded in `gone.md`).\n"
            + "- ✅ **PP901** **c** — d (design recorded in `silent.md`).\n"
            + "- ✅ **PP902** **e** — f (design recorded in `good.md`).";

        IReadOnlyList<string> missing = RecordedDesigns.NotRecorded(
            ledger,
            where => where switch
            {
                "gone.md" => null,
                "silent.md" => "a file with no id in it",
                _ => "this one mentions PP902 by name",
            });

        Assert.Equal(2, missing.Count);
        Assert.Contains("PP900: gone.md does not resolve", missing);
        Assert.Contains("PP901: silent.md does not name it", missing);
    }

    /// <summary>
    /// The id join matches a whole id and not a prefix, so PP90 in a file does not answer for PP900.
    /// </summary>
    [Fact]
    public void TheIdJoinMatchesAWholeId()
    {
        Assert.True(RecordedDesigns.NamesTheId("the reason is PP900's", "PP900"));
        Assert.False(RecordedDesigns.NamesTheId("the reason is PP9001's", "PP900"));
        Assert.False(RecordedDesigns.NamesTheId("nothing here", "PP900"));
    }

    /// <summary>An exemption is by PAIR, so the same file exempted for one id does not cover another.</summary>
    [Fact]
    public void AnExemptionIsByPairAndNotByFile()
    {
        Assert.True(RecordedDesigns.IsExempt(
            new RecordedDesign("PP396", "tests/corpus/exchange-ps5-four-channels.txt")));
        Assert.False(RecordedDesigns.IsExempt(
            new RecordedDesign("PP900", "tests/corpus/exchange-ps5-four-channels.txt")));
        Assert.False(RecordedDesigns.IsExempt(new RecordedDesign("PP396", "somewhere/else.md")));
    }
}
