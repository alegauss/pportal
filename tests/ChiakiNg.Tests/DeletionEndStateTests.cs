using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP639: the shape all three deletions have, as a rule rather than three decisions.
///
/// PP636 untied PP295 from PP27 by hand and PP638 found the third instance. What is asserted here is
/// the rule that makes a fourth arrive answered: a line whose last criterion is an end state cannot
/// be what another open line waits on, because what that criterion waits for IS the dependent.
/// </summary>
public class DeletionEndStateTests
{
    private static string? Roadmap()
        => DeletionEndState.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// PP639: THE RULE, over the roadmap as it stands.
    ///
    /// Both halves in one report, because they fail together in practice: a line that lost its
    /// end-state criterion would look like an ordinary dependency again, and a dep restored would
    /// jam whatever waits behind it with nothing saying why.
    /// </summary>
    [Fact]
    public void NoOpenLineWaitsOnAnEndState()
    {
        if (Roadmap() is not { } roadmap)
            return;

        IReadOnlyList<string> breaches = DeletionEndState.Breaches(roadmap);

        Assert.True(
            breaches.Count == 0,
            "a deletion line's end state comes after its own dependents: "
                + string.Join("; ", breaches));
    }

    /// <summary>
    /// PP639: all three carry the criterion, and the words are the ones the three already used.
    ///
    /// PP33's says "It is an end state, not a progress bar"; PP27's says "the end state and not a
    /// progress bar"; PP295's was written to match. Matched as two words rather than one sentence,
    /// because the two spellings were already in the file before this rule existed.
    /// </summary>
    /// <summary>
    /// PP639: THE RULE IS NO BROADER THAN ITS REASON, which its own first run had to teach it.
    ///
    /// Written as "no open line may depend on an end-state line" it reported PP30's dep on PP27. Not
    /// a breach: PP27's end state waits on six files that call takion, fec.c is not one, and §PP30
    /// does not mention takion at all. PP30 waits on the PORT above the end state, which is an
    /// ordinary dependency.
    ///
    /// So the check knows what each end state waits FOR. A rule that flagged every dependent would
    /// be one somebody narrows by deleting it.
    /// </summary>
    [Fact]
    public void AnOrdinaryDependencyIsNotABreach()
    {
        Assert.DoesNotContain("PP30", DeletionEndState.WaitsOn["PP27"]);
        Assert.Contains("PP295", DeletionEndState.WaitsOn["PP27"]);

        // PP33's waits on the shim, which is this port's own seam and not a line at all.
        Assert.Empty(DeletionEndState.WaitsOn["PP33"]);

        const string ordinary =
            "- 📋 **PP30** (deps: PP23 ✅, PP27) **fec** — y. → §PP30\n"
                + "## Done when — PP27\n\n- **the three leave** It is an end state, not a progress bar.\n";

        Assert.DoesNotContain(
            DeletionEndState.Breaches(ordinary),
            one => one.Contains("PP30", StringComparison.Ordinal));

        // And the one that IS a breach is still reported.
        const string breach =
            "- 📋 **PP295** (deps: PP27) **stream** — y. → §PP295\n"
                + "## Done when — PP27\n\n- **the three leave** It is an end state, not a progress bar.\n";

        Assert.Contains(
            DeletionEndState.Breaches(breach),
            one => one.Contains("PP295 declares a dep on PP27", StringComparison.Ordinal));
    }

    [Fact]
    public void AllThreeCarryAnEndStateCriterion()
    {
        if (Roadmap() is not { } roadmap)
            return;

        Assert.Equal(3, DeletionEndState.Lines.Count);

        foreach (string id in DeletionEndState.Lines)
        {
            Assert.True(
                DeletionEndState.CarriesAnEndState(roadmap, id),
                $"{id} has no criterion saying its deletion is an end state");
        }
    }

    /// <summary>
    /// PP639: and the readers see what they are looking for, so neither half is green on a pattern
    /// that stopped matching.
    ///
    /// The deps reader is the one that would go quietly wrong: an id is named all over a roadmap - in
    /// a why, in a criterion, in another line's prose - so a check that asked the whole line would
    /// report a dependency wherever one was mentioned, and one that matched loosely would read PP2
    /// inside PP295.
    /// </summary>
    [Fact]
    public void TheDepsReaderIsExactAndInsideTheParenthesis()
    {
        Assert.True(DeletionEndState.DepsName("PP293 ✅, PP295, PP23 ✅", "PP295"));
        Assert.False(DeletionEndState.DepsName("PP293 ✅, PP23 ✅", "PP295"));

        // A prefix is not a dep: PP2 is not PP295, and PP29 is not either.
        Assert.False(DeletionEndState.DepsName("PP295", "PP2"));
        Assert.False(DeletionEndState.DepsName("PP295", "PP29"));

        // And a mention outside the parenthesis is not one.
        const string mentioned =
            "- 📋 **PP28** (deps: PP23 ✅) **x** — once PP295 has landed. → §PP28\n";

        Assert.Empty(DeletionEndState.OpenLinesDependingOn(mentioned, "PP295"));

        const string declared =
            "- 📋 **PP28** (deps: PP23 ✅, PP295) **x** — y. → §PP28\n";

        Assert.Equal(["PP28"], DeletionEndState.OpenLinesDependingOn(declared, "PP295"));
    }

    /// <summary>
    /// PP639: and a line with no end-state criterion is reported, so the rule is not satisfied by
    /// silence.
    ///
    /// A criteria list that lost the criterion would leave the dep rule enforcing something nobody
    /// had decided - which is the state PP636 was untying by hand.
    /// </summary>
    [Fact]
    public void AMissingCriterionIsReported()
    {
        const string without =
            "## Done when — PP33\n\n- **libchiaki builds** two steps, measured apart.\n";

        Assert.False(DeletionEndState.CarriesAnEndState(without, "PP33"));
        Assert.Null(DeletionEndState.CriteriaOf(without, "PP27"));

        const string with =
            "## Done when — PP33\n\n- **the query reads zero** It is an end state, not a\n"
                + "  progress bar, and reading it as one is what made four look like none.\n";

        Assert.True(DeletionEndState.CarriesAnEndState(with, "PP33"));
    }
}
