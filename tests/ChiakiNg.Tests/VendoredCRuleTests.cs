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
