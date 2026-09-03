using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP31: the boundary is a constraint now, and the promise it refuses cannot come back quietly.
///
/// §PP31's whole complaint was that nothing said where managed code stops - so the decoder would be
/// chosen deliberately or discovered late, and late means after a block of work built on the other
/// assumption. Saying it in a rationale file was not enough, because a rationale is deleted when its
/// line ships and prose does not go red either way.
/// </summary>
public class ManagedBoundaryRuleTests(ITestOutputHelper output)
{
    private static string? Roadmap()
        => ManagedBoundaryRule.LocateRoadmap() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// The non-goal is there, and it still names both halves of the framing.
    ///
    /// Two phrases and not the sentence: a constraint should be re-wordable without a test failing.
    /// What it may not lose is the distinction it exists for - 100% Windows is reachable and 100%
    /// managed is not - because a non-goal that dropped the second half would read as a preference.
    /// </summary>
    [Fact]
    public void TheNonGoalStatesWhatIsReachableAsWellAsWhatIsNot()
    {
        if (Roadmap() is not { } roadmap)
            return;

        string paragraph = Assert.IsType<string>(ManagedBoundaryRule.NonGoalIn(roadmap));
        output.WriteLine(paragraph);

        foreach (string half in ManagedBoundaryRule.NonGoalMustSay)
        {
            Assert.Contains(half, paragraph, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// And nothing under app/ promises the thing it refuses.
    ///
    /// §PP31's own rationale carried the claim - "not one that is 100% managed" - which was a
    /// correction rather than a promise, and shipping the line took it. What this stops is a
    /// docstring picking it back up as an aspiration in a commit about something else.
    /// </summary>
    [Fact]
    public void NoManagedFilePromisesADecoderThisPortWillNotWrite()
    {
        IReadOnlyList<string> promising = ManagedBoundaryRule.ManagedFilesPromisingIt();

        Assert.True(
            promising.Count == 0,
            "these files promise a managed decode path the non-goal refuses: "
                + string.Join(", ", promising));
    }

    /// <summary>
    /// The reader finds a claim that wrapped across a line, which is how the first one hid.
    ///
    /// LibRepairCensus was caught by exactly this: a premise reading "lib/ is not" at the end of one
    /// comment line and "this port's to edit" at the start of the next. A guard that searched raw
    /// text would pass on the file it was written for.
    /// </summary>
    [Fact]
    public void APromiseSplitAcrossTwoCommentLinesIsStillFound()
    {
        const string wrapped = """
            /// The core is going to be 100%
            /// managed by the end of Block F.
            """;

        Assert.True(ManagedBoundaryRule.PromisesAManagedDecoder(wrapped));
    }

    /// <summary>
    /// And ordinary prose about the managed core is not a promise.
    ///
    /// The block is called "Managed core" and dozens of docstrings say a thing is managed. A guard
    /// that reported those would be turned off in a week, which is the failure mode this shape of
    /// check actually has.
    /// </summary>
    [Theory]
    [InlineData("The managed transport is timed against the C over captured traffic.")]
    [InlineData("A managed flow drives PP460's order against the nine asks.")]
    [InlineData("the session runs Qt-free: settings, log, input and feedback are managed")]
    public void SayingSomethingIsManagedIsNotPromisingEverythingIs(string prose)
        => Assert.False(ManagedBoundaryRule.PromisesAManagedDecoder(prose));

    /// <summary>
    /// The non-goal reader stops at the next non-goal, so it is about one paragraph.
    ///
    /// Without the boundary it would return the rest of the list, and the two phrases it checks for
    /// would be satisfied by any later entry that happened to say them.
    /// </summary>
    [Fact]
    public void TheParagraphReadIsTheOneNonGoalAndNotTheRestOfTheList()
    {
        const string roadmap = """
            ## Non-goals

            - **No managed video decoder** the reachable goal is 100% Windows, not 100% managed.
            - **No something else** this paragraph mentions 100% Windows and 100% managed too.
            """;

        string paragraph = Assert.IsType<string>(ManagedBoundaryRule.NonGoalIn(roadmap));

        Assert.Contains("the reachable goal", paragraph, StringComparison.Ordinal);
        Assert.DoesNotContain("No something else", paragraph, StringComparison.Ordinal);
    }
}
