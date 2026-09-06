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
        // PP295 HAS SHIPPED, and its entry emptied PP27's: the six files that call takion no longer
        // include streamconnection.c, so what PP27's end state waits on is no open line at all.
        // That is the shape every entry here works towards, and PP33 reached it a different way.
        Assert.Empty(DeletionEndState.WaitsOn["PP27"]);
        Assert.False(DeletionEndState.WaitsOn.ContainsKey("PP33"));
        Assert.False(DeletionEndState.WaitsOn.ContainsKey("PP295"));

        // SO THE RULE IS CORRECTLY INERT, and it demonstrates itself on a table this test owns
        // rather than going quiet. A check exercisable only while a breach exists stops meaning
        // anything exactly when the backlog reaches the state it is supposed to be in.
        string ends = "PP" + 9004.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string waits = "PP" + 9005.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string other = "PP" + 9006.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var table = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [ends] = [waits],
        };

        string criterion =
            $"## Done when — {ends}\n\n- **the three leave** It is an end state, not a progress bar.\n";

        // An ordinary dependency: the end state does not wait on this one, so the dep is fine.
        string ordinary = $"- 📋 **{other}** (deps: {ends}) **fec** — y. → §{other}\n" + criterion;

        Assert.DoesNotContain(
            DeletionEndState.Breaches(ordinary, table),
            one => one.Contains(other, StringComparison.Ordinal));

        // And the one that IS a breach is reported: the end state waits on the line depending on it.
        string breach = $"- 📋 **{waits}** (deps: {ends}) **stream** — y. → §{waits}\n" + criterion;

        Assert.Contains(
            DeletionEndState.Breaches(breach, table),
            one => one.Contains($"{waits} declares a dep on {ends}", StringComparison.Ordinal));
    }

    /// <summary>
    /// PP639: each carries the criterion, and the words are the ones they already used.
    ///
    /// PP27's says "the end state and not a progress bar" and PP295's was written to match. Matched
    /// as two words rather than one sentence, because both spellings were in the file before this
    /// rule existed.
    ///
    /// PP33: THREE UNTIL ITS DELETION LANDED. Its end state waited on the shim rather than on a
    /// line, and the shim's two oracles have gone with curl, json-c and the option - so the line is
    /// in the ledger and a rule still counting it would be asserting about shipped work.
    /// </summary>
    [Fact]
    public void EachCarriesAnEndStateCriterion()
    {
        if (Roadmap() is not { } roadmap)
            return;

        // PP295 SHIPPED AND LEFT THIS TABLE, which is the third line to do so and the second whose
        // deletion actually landed. One remains, and a rule counting a shipped line would be
        // asserting about work in the ledger.
        Assert.Single(DeletionEndState.Lines);
        Assert.DoesNotContain("PP33", DeletionEndState.Lines);

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
