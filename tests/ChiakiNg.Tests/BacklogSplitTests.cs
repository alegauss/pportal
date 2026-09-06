using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP583: the open lines split into what can be started and what waits on something absent.
/// </summary>
public class BacklogSplitTests(ITestOutputHelper output)
{
    private static string? Roadmap()
        => BacklogRequirements.LocateRoadmap() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE TWO HALVES PARTITION THE OPEN SET - every open line is in exactly one.
    ///
    /// A total mixes two states a reader needs apart. Six of this backlog's lines cannot be begun
    /// until a console, a certificate, a runner or a second toolchain arrives, and no work in this
    /// repository supplies one. PP312 made that expressible - a requirement is not a dep and never
    /// becomes one - and then only `pick` used it, so the count a reader meets first still said
    /// twenty.
    /// </summary>
    [Fact]
    public void TheSplitCoversEveryOpenLineOnce()
    {
        if (Roadmap() is not { } roadmap)
            return;

        IReadOnlyList<BacklogRequirements.OpenLine> all = BacklogRequirements.OpenLines(roadmap);
        IReadOnlyList<BacklogRequirements.OpenLine> startable = BacklogRequirements.Startable(roadmap);
        IReadOnlyList<BacklogRequirements.OpenLine> waiting = BacklogRequirements.Waiting(roadmap);

        output.WriteLine($"{all.Count} open: {startable.Count} startable, {waiting.Count} waiting");

        Assert.Equal(all.Count, startable.Count + waiting.Count);
        Assert.NotEmpty(all);

        // No line is in both, which a predicate written the wrong way round would allow.
        Assert.Empty(startable.Select(one => one.Id).Intersect(waiting.Select(one => one.Id)));
    }

    /// <summary>
    /// Everything a waiting line names is a requirement the config declares. A line waiting on a
    /// word nobody declared is PP312's own gap check, and this must not disagree with it.
    /// </summary>
    [Fact]
    public void EveryThingWaitedOnIsDeclared()
    {
        if (Roadmap() is not { } roadmap
            || BacklogRequirements.LocateConfig() is not { } configPath)
        {
            return;
        }

        IReadOnlySet<string> declared = BacklogRequirements.Declared(File.ReadAllText(configPath));

        foreach (BacklogRequirements.OpenLine line in BacklogRequirements.Waiting(roadmap))
        {
            foreach (string need in line.Requirements)
                Assert.Contains(need, declared);
        }
    }

    /// <summary>
    /// A line with no requirement is startable and one with two is counted once, which is the shape
    /// that would go wrong if the split were built by counting requirements rather than lines.
    /// </summary>
    [Fact]
    public void ALineWithTwoRequirementsIsStillOneLine()
    {
        const string roadmap = """
            - 📋 **PP9900** (deps: —) **plain** — a reason. → §PP9900
            - 📋 **PP9901** (deps: —) (requires: console, a-person-looking) **needs two** — a reason. → §PP9901
            """;

        Assert.Equal(2, BacklogRequirements.OpenLines(roadmap).Count);
        Assert.Equal(["PP9900"], BacklogRequirements.Startable(roadmap).Select(one => one.Id));

        BacklogRequirements.OpenLine waiting = Assert.Single(BacklogRequirements.Waiting(roadmap));
        Assert.Equal("PP9901", waiting.Id);
        Assert.Equal(["console", "a-person-looking"], waiting.Requirements);
    }

    /// <summary>Every open marker counts, not only the planned one.</summary>
    [Fact]
    public void EveryOpenMarkerIsAnOpenLine()
    {
        const string roadmap = """
            - 📋 **PP9900** (deps: —) **planned** — a. → §PP9900
            - 💭 **PP9901** (deps: —) **idea** — a. → §PP9901
            - ⏳ **PP9902** (deps: —) **partial** — a. → §PP9902
            - ✅ **PP9903** **shipped** — a.
            """;

        Assert.Equal(
            ["PP9900", "PP9901", "PP9902"],
            BacklogRequirements.OpenLines(roadmap).Select(one => one.Id));
    }
}
