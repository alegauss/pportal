using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP568: the rule about patching vendored C, held where a verb cannot drop it.
/// </summary>
public class VendoredCRuleTests
{
    private static string Roadmap()
    {
        string? path = VendoredCRule.LocateRoadmap();
        Assert.NotNull(path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// THE RULE IS A NON-GOAL. It was argued only in §PP107 - a deferred section, and a deferral is
    /// not terminal, so resolving that line would have taken the argument with it and left one
    /// ledger sentence for something that binds every task touching lib/.
    /// </summary>
    [Fact]
    public void TheRuleIsStillANonGoal() => Assert.True(VendoredCRule.IsStillANonGoal(Roadmap()));

    /// <summary>
    /// And it has to be under the non-goals, not merely somewhere in the file. A bullet that moved
    /// into a block would stop being read out on `brief` and stop being refused at input, which is
    /// the whole of what makes a non-goal bind.
    /// </summary>
    [Fact]
    public void ABulletOutsideTheNonGoalsDoesNotCount()
    {
        Assert.False(VendoredCRule.IsStillANonGoal(
            $"# Roadmap\n\n## Block F\n\n- {VendoredCRule.Lead} — moved here\n"));

        Assert.True(VendoredCRule.IsStillANonGoal(
            $"# Roadmap\n\n## Non-goals\n\n- **{VendoredCRule.Lead}** because\n"));
    }

    /// <summary>
    /// PP571: THE RULE NAMES THE LINE IT DOES NOT REACH, because otherwise it forbids it.
    ///
    /// PP568 wrote this rule while PP33 sat open and ready, and PP33's whole content is deleting
    /// holepunch.c and editing session.c and ctrl.c to stop calling it - a local change to vendored
    /// C by any plain reading. A roadmap whose non-goals forbid a line it lists as ready is a
    /// contradiction, and the exemption is what keeps the two consistent.
    ///
    /// The rule's own reason is what allows it: the drift checks would be left agreeing with a
    /// libchiaki nobody runs, and a deletion removes what they agree with.
    /// </summary>
    [Fact]
    public void TheRuleNamesTheDeletionItDoesNotReach()
    {
        Assert.Empty(VendoredCRule.MissingExemptions(Roadmap()));
        Assert.Equal("PP33", VendoredCRule.DoesNotReach);
    }

    /// <summary>
    /// PP593: AND PP30, which the rule was silent about while lint flagged it the same way.
    ///
    /// Both lines name "vendored" and `roadkeep lint` says the same thing about each - a constraint
    /// may bound a line without forbidding it, and nothing in the file decides which. PP571 decided
    /// it for PP33 and left PP30 reading as forbidden by a rule that does not mean to forbid it.
    ///
    /// §PP30 is what settles it, and it takes both of its outcomes: deleting the fec call sites
    /// removes what the drift checks agree with, which is PP33's argument exactly, and keeping the C
    /// - which §PP30 calls a legitimate outcome - changes nothing in lib/ at all. Neither is a patch.
    /// </summary>
    [Fact]
    public void TheRuleNamesPP30Too()
    {
        Assert.Contains("PP30", VendoredCRule.LinesItDoesNotReach);
        Assert.Equal(2, VendoredCRule.LinesItDoesNotReach.Count);

        // Named one at a time this would pass on PP33 alone, which is the state PP571 left.
        Assert.Equal(
            ["PP30"],
            VendoredCRule.MissingExemptions(
                $"## Non-goals\n\n- **{VendoredCRule.Lead}** but not PP33's deletion\n"));
    }

    /// <summary>
    /// And the exemption has to be inside the rule's own paragraph. PP33 is named all over this
    /// file, so a check that asked the whole roadmap would be green with the carve-out deleted.
    /// </summary>
    [Fact]
    public void TheExemptionMustBeInTheRulesOwnParagraph()
    {
        Assert.False(VendoredCRule.NamesWhatItDoesNotReach(
            $"## Non-goals\n\n- **{VendoredCRule.Lead}** no carve-out here\n"
                + "- **Something else** PP33 and PP30 live here\n"));

        Assert.True(VendoredCRule.NamesWhatItDoesNotReach(
            $"## Non-goals\n\n- **{VendoredCRule.Lead}** but not PP33's deletion or PP30's port\n"
                + "- **Something else** no\n"));
    }

    /// <summary>
    /// The deferral it points at is still there, because the non-goal carries a sentence and the
    /// argument is longer than one - they are joined by name rather than duplicated, and a pointer
    /// to a section nobody kept is worse than no pointer.
    /// </summary>
    [Fact]
    public void TheSectionItPointsAtIsStillThere()
    {
        Assert.Contains(VendoredCRule.ArguedIn, Roadmap() + File.ReadAllText(
            SanitizerSource.LocateRelative(@"docs\DEFERRED.md")!), StringComparison.Ordinal);
    }
}
