using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP38: the count that makes the non-goal a gate.
///
/// "No line ships without an assertion that fails without it" was a sentence in a file. This is the
/// number, and it may fall and may not rise - see <see cref="AssertionRatchet"/> for the join it
/// rests on and for how coarse that join deliberately is.
///
/// It is here rather than in the host's selftest because it is about the suites, and the suite is
/// what a runner runs (PP36). A ratchet that only reported where somebody remembered to look is the
/// discipline it exists to replace.
/// </summary>
public class AssertionRatchetTests(ITestOutputHelper output)
{
    /// <summary>A ledger in the shapes the real one carries.</summary>
    private const string Ledger = """
        ## Block E — Windows-only build

        - ✅ **PP22 (the single-file publish)** **symptom here** — outcome here.
        - 🗑 **PP20** **abandoned** — deleted before this line was filed.
        - ✅ **PP277** **symptom here** — outcome here.
        """;

    /// <summary>Only the shipped marker, and a partial is its id.</summary>
    [Fact]
    public void TheLedgerNamesWhatShipped()
    {
        IReadOnlySet<string> shipped = AssertionRatchet.Shipped(Ledger);

        Assert.Contains("PP22", shipped);
        Assert.Contains("PP277", shipped);

        // Retired is work nobody built. Demanding a test for it would make the debt grow by not
        // doing things, which is the one way a ratchet can be actively harmful.
        Assert.DoesNotContain("PP20", shipped);
    }

    /// <summary>An id is a whole id: PP2 is not found inside PP292.</summary>
    [Fact]
    public void AnIdIsNotAPrefixOfAnother()
    {
        IReadOnlySet<string> named = AssertionRatchet.Named("/// PP292: the wrap, and PP29 beside it.");

        Assert.Contains("PP292", named);
        Assert.Contains("PP29", named);
        Assert.DoesNotContain("PP2", named);
    }

    /// <summary>The ceiling file explains itself, so the number is read past the comments.</summary>
    [Fact]
    public void TheCeilingIsReadPastWhatExplainsIt()
    {
        Assert.Equal(97, AssertionRatchet.CeilingIn("# why this exists\n#\n97\n"));

        // A file that carries no number is not a ceiling of zero, which would fail every run and
        // be fixed by deleting the check.
        Assert.Equal(-1, AssertionRatchet.CeilingIn("# nothing but prose\n"));
        Assert.Equal(-1, AssertionRatchet.CeilingIn(""));
    }

    /// <summary>
    /// THE RATCHET. It may fall and it may not rise, and both directions are a failure here.
    ///
    /// Above the ceiling means a task shipped with nothing naming it, and the commit that did it is
    /// the cheapest place that will ever be fixed. Below means the debt was paid and the ceiling was
    /// not lowered with it - which is not harmless: the ratchet has just quietly loosened by exactly
    /// what was gained, and the next task can ship uncovered for free.
    /// </summary>
    [Fact]
    public void TheDebtDoesNotGrow()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        int ceiling = AssertionRatchet.Ceiling(root);
        Assert.True(ceiling >= 0, $"no readable ceiling in {AssertionRatchet.CeilingRelativePath}");

        IReadOnlyList<string> uncovered = AssertionRatchet.Uncovered(root);

        // A scan that stopped working reports zero debt and passes, which is this test's own
        // subject wearing the other hat.
        Assert.True(uncovered.Count > 0 || ceiling == 0,
            "no uncovered tasks found at all and the ceiling is not zero - the scan is not working");

        output.WriteLine($"{uncovered.Count} shipped task(s) named by no assertion, ceiling {ceiling}");

        string newest = string.Join(", ", uncovered.Take(12));

        Assert.False(
            uncovered.Count > ceiling,
            $"{uncovered.Count} shipped tasks are named by no assertion and the ceiling is {ceiling}. "
                + "The non-goal says no line ships without an assertion that fails without it, so "
                + "the task that just shipped needs one - newest uncovered: " + newest);

        Assert.False(
            uncovered.Count < ceiling,
            $"the debt fell to {uncovered.Count} and {AssertionRatchet.CeilingRelativePath} still "
                + $"says {ceiling}. Lower it in this commit: a ratchet that is not tightened when it "
                + "could be has given the gain away.");
    }
}
