using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP582: every partially-shipped line carries a definition of done.
/// </summary>
public class PartialCriteriaTests(ITestOutputHelper output)
{
    private static string? Roadmap()
        => PartialCriteria.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// EVERY PARTIAL LINE HAS CRITERIA, which five of the six did not.
    ///
    /// Partial means some of it shipped and some has not - exactly when somebody asks how much is
    /// left. Without criteria the only measure is a count that reads its full value until the end,
    /// which is the failure roadkeep.toml declares the table to fix. It was declared, used on PP33,
    /// and left empty for PP11, PP27, PP322, PP46 and PP76.
    /// </summary>
    [Fact]
    public void EveryPartialLineHasADefinitionOfDone()
    {
        if (Roadmap() is not { } roadmap)
            return;

        IReadOnlyList<string> partial = PartialCriteria.PartialIds(roadmap);
        output.WriteLine($"{partial.Count} partial: {string.Join(", ", partial)}");

        IReadOnlyList<string> without = PartialCriteria.WithoutCriteria(roadmap);
        Assert.True(without.Count == 0, $"partial with no criteria: {string.Join(", ", without)}");
    }

    /// <summary>
    /// And there are partial lines to check. A roadmap with none would pass the test above without
    /// saying anything, which is the shape of a check that stops being a check.
    /// </summary>
    [Fact]
    public void ThereArePartialLinesToCheck()
    {
        if (Roadmap() is not { } roadmap)
            return;

        Assert.NotEmpty(PartialCriteria.PartialIds(roadmap));
        Assert.NotEmpty(PartialCriteria.IdsWithCriteria(roadmap));
    }

    /// <summary>
    /// A partial line with no heading is reported by id, so the failure names what to write rather
    /// than that something is missing.
    /// </summary>
    [Fact]
    public void APartialLineWithNoCriteriaIsNamed()
    {
        const string roadmap = """
            - ⏳ **PP900** (deps: —) **something half done** — a reason. → §PP900
            - 📋 **PP901** (deps: —) **not started** — a reason. → §PP901

            ## Done when — PP901

            - **Irrelevant** this one is not partial.
            """;

        Assert.Equal(["PP900"], PartialCriteria.WithoutCriteria(roadmap));

        // The planned line is not asked for criteria: partial is the state that raises the question.
        Assert.Equal(["PP900"], PartialCriteria.PartialIds(roadmap));
    }

    /// <summary>And a heading of its own satisfies it.</summary>
    [Fact]
    public void AHeadingSatisfiesTheLine()
    {
        const string roadmap = """
            - ⏳ **PP900** (deps: —) **something half done** — a reason. → §PP900

            ## Done when — PP900

            - **A thing that must be true** how it is checked.
            """;

        Assert.Empty(PartialCriteria.WithoutCriteria(roadmap));
    }
}
