using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP483: the reason two defects were left un-repaired, held to fact.
///
/// ReorderQueueSource and RegistRequest each record a defect rather than repairing it, and each
/// gives the same reason: this port does not edit lib/. Files under lib/src say otherwise, and one
/// of them is regist.c - the file RegistRequest's own defect is in.
///
/// These assertions do not decide whether either defect should now be repaired. That is the
/// author's call, and PP107's five predicates with PP109's five C assertions are what it would
/// have to move. They decide only that the false reason cannot come back quietly.
/// </summary>
public class LibRepairCensusTests
{
    /// <summary>
    /// THE GATE: no rationale in the managed half rests a reason on lib/ being untouched.
    ///
    /// Red before PP483 and green after, which is the whole point of writing it down as an
    /// assertion - the sentence had been false for a long time and nothing went red for it.
    /// </summary>
    [Fact]
    public void NoRationaleRestsAReasonOnLibBeingUntouched()
    {
        IReadOnlyList<string> offenders = LibRepairCensus.FilesStatingTheFalsePremise();

        Assert.True(
            offenders.Count == 0,
            "these say a reason rests on lib/ being untouched, and lib/src is full of this port's "
                + $"own repairs: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// And the fact that makes the gate above the right way round: this port does edit lib/.
    ///
    /// FLOORS, not counts. C files are meant to leave this build - PP28 and PP295 exist to delete
    /// them - so a test pinned to today's twenty would go red for the success it is waiting for.
    /// What has to stay true is that the tree carries this port's repairs at all.
    /// </summary>
    [Fact]
    public void ThisPortDoesEditLib()
    {
        IReadOnlyList<string> files = LibRepairCensus.RepairedFiles();
        if (files.Count == 0)
            return;

        Assert.True(files.Count >= 5, $"only {files.Count} file(s) under lib/src carry a repair marker");
        Assert.True(
            LibRepairCensus.RepairTaskIds().Count >= 20,
            "the repairs in lib/src name fewer tasks than the premise needs to be false");
    }

    /// <summary>
    /// The census reaches the remote/ subtree, which a flat glob would miss.
    ///
    /// Four of the repaired files are in it, and the last session put two of them there.
    /// </summary>
    [Fact]
    public void TheCensusReachesTheRemoteSubtree()
    {
        IReadOnlyList<string> files = LibRepairCensus.RepairedFiles();
        if (files.Count == 0)
            return;

        Assert.Contains(files, f => f.Contains("remote", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// regist.c carries a marker: the file whose defect RegistRequest declined to repair, on the
    /// grounds that this port does not edit it, has been edited by this port.
    ///
    /// Skipped where the file is gone, because that is what PP28 is for.
    /// </summary>
    [Fact]
    public void TheFileRegistRequestSaysIsUntouchedHasBeenEdited()
    {
        if (LibRepairCensus.LocateSource() is not { } root)
            return;

        string regist = Path.Combine(root, "regist.c");
        if (!File.Exists(regist))
            return;

        Assert.NotEmpty(LibRepairCensus.TaskIdsIn(File.ReadAllText(regist)));
    }

    /// <summary>The premise is a claim, not one spelling, so the guard matches more than one.</summary>
    [Theory]
    [InlineData("this port does not edit lib/")]
    [InlineData("This port never edits lib/, deliberately")]
    [InlineData("we cannot edit lib/ here")]
    [InlineData("it does not patch lib/ at all")]
    public void TheGuardMatchesTheClaimHoweverItIsPhrased(string text)
        => Assert.True(LibRepairCensus.StatesTheFalsePremise(text));

    /// <summary>And says no to prose that describes lib/ without resting a reason on it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("lib/ is the C half of this project, and it is retained.")]
    [InlineData("PP483 edits lib/src/takion.c and moves the model in the same commit.")]
    public void TheGuardSaysNoToProseThatMakesNoSuchClaim(string text)
        => Assert.False(LibRepairCensus.StatesTheFalsePremise(text));

    /// <summary>
    /// The scan excludes exactly one file - this census, which declares the needles as literals.
    ///
    /// Named rather than hidden: a guard that spliced its own needle together to dodge itself would
    /// be a guard nobody could read.
    /// </summary>
    [Fact]
    public void TheScanExcludesTheOneFileThatDeclaresTheNeedles()
    {
        Assert.Equal("LibRepairCensus.cs", LibRepairCensus.CensusFileName);
        Assert.True(LibRepairCensus.StatesTheFalsePremise(
            string.Join(' ', LibRepairCensus.FalsePremises)));
    }

    /// <summary>
    /// A premise that straddles a line break is caught. The first pass of this guard was not.
    ///
    /// The copy in SelfTest.cs read "lib/ is not" at the end of one line and "this port's to edit"
    /// at the start of the next, behind a comment marker, so no literal search over the raw file
    /// could find it - and none did, for a whole commit. Normalising first is what closed that, and
    /// it is why the guard reads flattened prose rather than the file.
    /// </summary>
    [Fact]
    public void APremiseThatWrapsAcrossLinesIsStillCaught()
    {
        const string wrapped =
            "\t\t\t\t// Recorded, not repaired: lib/ is not\r\n"
            + "\t\t\t\t// this port's to edit, and the managed side has no fixed buffer.\r\n";

        Assert.True(LibRepairCensus.StatesTheFalsePremise(wrapped));
    }

    /// <summary>And the same sentence unwrapped, so it is the claim being caught and not the wrap.</summary>
    [Fact]
    public void TheSameClaimOnOneLineIsCaughtToo()
        => Assert.True(LibRepairCensus.StatesTheFalsePremise(
            "// Recorded, not repaired: lib/ is not this port's to edit."));

    /// <summary>Normalising drops the comment markers and collapses runs of whitespace.</summary>
    [Fact]
    public void NormalisingFlattensCommentProse()
        => Assert.Equal(" one two three", LibRepairCensus.Normalise("/// one   two\n  /// three"));

    /// <summary>The id reader takes each task once, in the order it first appears.</summary>
    [Fact]
    public void TheIdReaderTakesEachTaskOnce()
    {
        IReadOnlyList<string> ids = LibRepairCensus.TaskIdsIn(
            "// PP68: and again PP68, then PP427. PPX is not one, and PP is not one.");

        Assert.Equal(["PP68", "PP427"], ids);
    }
}
