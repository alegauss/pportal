using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP506: the deps group, and the one join in this backlog that had to be read to be found.
///
/// A missing dep is invisible the way a missing requirement was before PP486 - it holds a line back
/// and is written nowhere a check reads.
/// </summary>
public class BacklogDepsTests
{
    /// <summary>The group is read, markers and all, and an empty one is empty.</summary>
    [Theory]
    [InlineData("- 📋 **PP1** (deps: —) **s** — w. → §PP1", 0)]
    [InlineData("- ⏳ **PP2** (deps: PP24 ✅, PP293 ✅, PP340) **s** — w. → §PP2", 3)]
    [InlineData("- 📋 **PP3** (deps: PP9 ✅) (requires: console) **s** — w. → §PP3", 1)]
    public void TheGroupIsReadWithItsMarkersStripped(string line, int count)
        => Assert.Equal(count, BacklogDeps.Of(line).Count);

    /// <summary>The ids come back without their markers, so a set can be compared.</summary>
    [Fact]
    public void TheIdsComeBackBare()
    {
        IReadOnlySet<string> deps =
            BacklogDeps.Of("- ⏳ **PP33** (deps: PP24 ✅, PP293 ✅, PP340, PP481) **s** — w. → §PP33");

        Assert.Equal(["PP24", "PP293", "PP340", "PP481"], deps.Order());
    }

    /// <summary>
    /// A line with no deps group at all reads as no deps rather than throwing.
    ///
    /// Block headings and the non-goals share the file, and a reader that only works on task lines
    /// is one every caller has to guard.
    /// </summary>
    [Fact]
    public void ALineWithNoGroupHasNoDeps()
    {
        Assert.Empty(BacklogDeps.Of("## Block F — Managed core"));
        Assert.Empty(BacklogDeps.Of(string.Empty));
    }

    /// <summary>
    /// THE JOIN: while PP481 is open, PP33 waits on it.
    ///
    /// PP340 delivers a managed object that owns the PSN flow; PP481 is what puts an implementation
    /// behind it - nine shim wrappers over the real C. PP33 is the deletion, and it listed only the
    /// first. So shipping PP340 would have offered the deletion as ready while nothing managed had
    /// ever called holepunch.c.
    ///
    /// Both sides are conditional on being open, so this retires itself: when either ships its line
    /// leaves the roadmap and the check has nothing to hold.
    /// </summary>
    [Fact]
    public void WhilePP481IsOpenPP33WaitsOnIt()
    {
        if (BacklogDeps.LocateRoadmap() is not { } path)
            return;

        string roadmap = File.ReadAllText(path);

        if (!BacklogDeps.IsOpen(roadmap, "PP481") || BacklogDeps.LineFor(roadmap, "PP33") is not { } pp33)
            return;

        Assert.Contains("PP481", BacklogDeps.Of(pp33));
    }

    /// <summary>
    /// And PP340 too, which is the dep that was already there.
    ///
    /// Named beside the new one so the pair reads as what it is: the deletion waits on the object
    /// AND on the implementation, and either alone would let it through.
    /// </summary>
    [Fact]
    public void AndOnPP340ForTheSameReason()
    {
        if (BacklogDeps.LocateRoadmap() is not { } path)
            return;

        string roadmap = File.ReadAllText(path);

        if (!BacklogDeps.IsOpen(roadmap, "PP340") || BacklogDeps.LineFor(roadmap, "PP33") is not { } pp33)
            return;

        Assert.Contains("PP340", BacklogDeps.Of(pp33));
    }

    /// <summary>
    /// Every id any open line names as a dep is either shipped or is itself an open line.
    ///
    /// The general check the join above is one case of. A dep naming an id that is in neither state
    /// is a typo that reads as a real blocker - and the marker beside a dep says which it is, so
    /// this asks only about the ones with no marker.
    /// </summary>
    [Fact]
    public void EveryUnmarkedDepIsAnOpenLine()
    {
        if (BacklogDeps.LocateRoadmap() is not { } path)
            return;

        string roadmap = File.ReadAllText(path);
        var dangling = new List<string>();

        foreach (string line in roadmap.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!line.StartsWith("- ", StringComparison.Ordinal))
                continue;

            foreach (string dep in BacklogDeps.Of(line))
            {
                // A dep carrying ✅ is shipped and its line is gone by design; the rest must be here.
                if (line.Contains($"{dep} ✅", StringComparison.Ordinal))
                    continue;

                if (!BacklogDeps.IsOpen(roadmap, dep))
                    dangling.Add(dep);
            }
        }

        Assert.True(
            dangling.Count == 0,
            "these deps name no open line and carry no shipped marker: " + string.Join(", ", dangling));
    }
}
