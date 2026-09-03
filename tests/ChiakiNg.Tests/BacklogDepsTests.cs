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

    /// <summary>
    /// PP28: and the check facing the other way - no open line's sentence names an open id its deps
    /// group leaves out.
    ///
    /// This is PP486's shape one group along. That one found a line saying in words that it needed
    /// hardware while declaring no requirement; this finds a line saying in words that it waits on
    /// another line while declaring no dep. Both are a queue offering something that is not ready.
    ///
    /// PP28 IS THE CASE THAT TAUGHT IT THE DIFFERENCE, and it taught it by being wrong. Its why read
    /// "once PP293, PP294 and PP295 have each landed" while its deps left PP295 out, which reads
    /// exactly like the defect - and adding the dep turned <c>DeletionEndStateTests</c> red, because
    /// PP639 had already settled that PP295's deletion waits on PP28. What the sentence meant was
    /// the PORT, and PP295's line closes after this one. Its why now says so, and the reader skips
    /// that edge rather than asking for it.
    ///
    /// So it reports rather than judges, and there are three ways a mention is not a wait: the named
    /// id shipped, the named line already deps on this one, or PP639 puts its end state after this
    /// one. What is left is a dep to add or a sentence to reword, and both are somebody's call.
    /// </summary>
    [Fact]
    public void NoLineWaitsInProseOnSomethingItsDepsLeaveOut()
    {
        if (BacklogDeps.LocateRoadmap() is not { } path)
            return;

        IReadOnlyList<(string Id, string Names)> mentions =
            BacklogDeps.MentionedButNotDepended(File.ReadAllText(path));

        Assert.True(
            mentions.Count == 0,
            "these lines name an open id their deps do not: "
                + string.Join(", ", mentions.Select(m => $"{m.Id} names {m.Names}")));
    }

    /// <summary>
    /// The reader finds the case PP28 was, and not the four that look like it.
    ///
    /// A dep that IS declared, a mention of a shipped id, a line's own id in its pointer, and a
    /// criterion under a "Done when" heading are all ordinary and none is a finding. A check
    /// reporting any of them would be turned off within a week, which is the failure mode a report
    /// like this actually has.
    /// </summary>
    [Fact]
    public void OnlyTheUndeclaredOpenMentionIsReported()
    {
        const string roadmap = """
            - 📋 **PP900** (deps: PP901) **something** — waits on PP901 and on PP902. → §PP900
            - 📋 **PP901** (deps: —) **declared** — names PP903 ✅, which shipped. → §PP901
            - 📋 **PP902** (deps: —) **the one nobody declared** — nothing here. → §PP902

            ## Done when — PP900

            - **A criterion naming PP902** which is prose about a line and not a line.
            """;

        IReadOnlyList<(string Id, string Names)> mentions =
            BacklogDeps.MentionedButNotDepended(roadmap);

        Assert.Equal([("PP900", "PP902")], mentions);
    }

    /// <summary>
    /// PP63 is the fifth case, and it is the one the first run of this check actually found.
    ///
    /// Its symptom names PP46 - "PP46's before cannot be produced at all" - and PP46 deps on PP63.
    /// That is a line saying what it UNBLOCKS, and the dep the check would otherwise ask for is the
    /// edge that closes a cycle. Kept as its own test rather than folded into the fixture above,
    /// because it is the distinction between "waits on" and "is waited on" and that is the whole
    /// judgement this reader makes.
    /// </summary>
    [Fact]
    public void ALineNamingWhatItUnblocksIsNotWaitingOnIt()
    {
        const string roadmap = """
            - 📋 **PP900** (deps: PP901) **the one that waits** — needs PP901 first. → §PP900
            - 📋 **PP901** (deps: —) **the one that unblocks** — without this PP900 cannot start. → §PP901
            """;

        Assert.Empty(BacklogDeps.MentionedButNotDepended(roadmap));
    }

    /// <summary>
    /// PP639's edge is the sixth case, and the two rules have to compose on the real backlog.
    ///
    /// PP28's why names PP295 and PP28 declares no dep on it, which is the shape this reader looks
    /// for - and the dep it would ask for is the one <c>DeletionEndStateTests</c> refuses, because
    /// PP295's deletion waits on PP28. Asserted against the roadmap rather than a fixture: what
    /// matters is that the pair of rules leaves this particular backlog with no edit that satisfies
    /// one and breaks the other, which a fixture cannot say.
    /// </summary>
    [Fact]
    public void TheEndStateEdgeIsNotReportedAsAMissingDep()
    {
        if (BacklogDeps.LocateRoadmap() is not { } path)
            return;

        string roadmap = File.ReadAllText(path);

        // The premise: PP28 still names PP295 in its own sentence, and still does not depend on it.
        string line = Assert.IsType<string>(BacklogDeps.LineFor(roadmap, "PP28"));
        Assert.Contains("PP295", BacklogDeps.Prose(line), StringComparison.Ordinal);
        Assert.DoesNotContain("PP295", BacklogDeps.Of(line));

        // And PP639 is why that is right rather than an omission.
        Assert.Contains("PP28", DeletionEndState.WaitsOn["PP295"]);

        Assert.DoesNotContain(("PP28", "PP295"), BacklogDeps.MentionedButNotDepended(roadmap));
    }

    /// <summary>
    /// And the prose is the author's half: the groups and the derived pointer are cut away.
    ///
    /// Without the first cut every declared dep would be read as a mention of itself; without the
    /// second every line would be reported as waiting on its own id.
    /// </summary>
    [Fact]
    public void TheProseIsWhatWasWrittenRatherThanWhatWasDerived()
    {
        Assert.Equal(
            " **something** — waits on PP901 and on PP902. ",
            BacklogDeps.Prose(
                "- 📋 **PP900** (deps: PP901) (requires: console) **something** — waits on PP901 and on PP902. → §PP900"));
    }
}
