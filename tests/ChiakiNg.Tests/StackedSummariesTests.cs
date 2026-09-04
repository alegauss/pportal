using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP643: no member carries two summaries, because the wrong one wins silently.
///
/// Twelve did when this was written, and eleven of the twelve were the same accident: a new member
/// inserted directly beneath an existing docstring, leaving the old one stranded at the top of the
/// run and the member it belonged to with none. The ratchet joins tasks to tests by the id in a
/// summary, so one attached to the wrong member is a coverage claim about the wrong thing.
/// </summary>
public class StackedSummariesTests(ITestOutputHelper output)
{
    /// <summary>
    /// What stands in for a doc comment in this file's fixtures, and why it has to.
    ///
    /// The scan is a line reader and cannot tell a doc comment from one inside a string, so a
    /// fixture written literally would be found by the check over the real tree - this file's own
    /// examples were the only three hits on the first run. The same shape as the ratchet's rule that
    /// a fixture may only carry ids above PP9000: a check that reads the tree must not be able to
    /// read its own examples.
    /// </summary>
    private const string DocMarker = "@@@";

    /// <summary>One fixture, with the marker turned back into what it stands for.</summary>
    private static string Doc(string fixture)
        => fixture.Replace(DocMarker, StackedSummaries.DocPrefix, StringComparison.Ordinal);

    /// <summary>Every C# file this port owns, under the roots the scan asks of.</summary>
    private static IEnumerable<(string Where, string Source)> Ours()
    {
        foreach (string root in StackedSummaries.Roots)
        {
            if (StackedSummaries.LocateRoot(root) is not { } path)
                continue;

            foreach (string file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (StackedSummaries.IsOurs(file))
                    yield return (file, File.ReadAllText(file));
            }
        }
    }

    /// <summary>
    /// THE CHECK: no member in app or tests carries more than one summary.
    ///
    /// The failure names the file, the line the run starts on and the declaration it sits above, so
    /// what to do about it is reading two docstrings and deciding which member each describes.
    /// </summary>
    [Fact]
    public void NoMemberCarriesTwoSummaries()
    {
        var stacked = new List<StackedSummary>();
        var scanned = 0;

        foreach ((string where, string source) in Ours())
        {
            scanned++;
            stacked.AddRange(StackedSummaries.In(source, where));
        }

        if (scanned == 0)
            return;

        output.WriteLine($"{scanned} file(s) scanned");

        Assert.True(
            stacked.Count == 0,
            "these members carry more than one <summary>, so the generator keeps one and drops the "
                + "rest - and the dropped one is usually another member's:\n  "
                + string.Join(
                    "\n  ",
                    stacked.Select(one =>
                        $"{one.Where}:{one.Line} ({one.Summaries}) above {one.Declares}")));
    }

    /// <summary>
    /// And there are files to scan, so the check above is not passing over an empty tree.
    ///
    /// PP271's rule, which this needs more than most: the scan walks directories, and a root that
    /// stopped resolving would make it silently vacuous.
    /// </summary>
    [Fact]
    public void ThereAreFilesToScan()
    {
        if (StackedSummaries.LocateRoot(StackedSummaries.Roots[0]) is null)
            return;

        Assert.True(Ours().Count() > 100, "the scan found almost nothing, so a root stopped resolving");
    }

    /// <summary>
    /// THE DEFECT, written out: two summaries in one run, reported with the member they sit on.
    ///
    /// This is the shape found in CCall.cs, where Compact's docstring sat above Code.
    /// </summary>
    [Fact]
    public void TwoSummariesInOneRunAreFound()
    {
        const string fixture = """
            public static class Thing
            {
                @@@ <summary>The first one, which is really about something below.</summary>
                @@@ <summary>
                @@@ The second, which is this member's own.
                @@@ </summary>
                public static void Member()
                {
                }
            }
            """;

        StackedSummary one = Assert.Single(StackedSummaries.In(Doc(fixture), "Thing.cs"));

        Assert.Equal(2, one.Summaries);
        Assert.Equal(3, one.Line);
        Assert.Equal("public static void Member()", one.Declares);
    }

    /// <summary>Three is found too, and reported as three - App.xaml.cs had one.</summary>
    [Fact]
    public void ThreeAreReportedAsThree()
    {
        const string fixture = """
                @@@ <summary>One.</summary>
                @@@ <summary>Two.</summary>
                @@@ <summary>Three.</summary>
                private static bool Member() => true;
            """;

        Assert.Equal(3, Assert.Single(StackedSummaries.In(Doc(fixture), "x.cs")).Summaries);
    }

    /// <summary>
    /// One summary per member is the ordinary case and is not reported, however long the docstring
    /// or however many other elements it carries.
    /// </summary>
    [Fact]
    public void OneSummaryWithOtherElementsIsFine()
    {
        const string fixture = """
                @@@ <summary>
                @@@ What it does.
                @@@
                @@@ And why, at length, with <c>markup</c> and <see cref="Something"/> in it.
                @@@ </summary>
                @@@ <param name="a">The first.</param>
                @@@ <param name="b">The second.</param>
                @@@ <returns>What comes back.</returns>
                public static int Member(int a, int b) => a + b;
            """;

        Assert.Empty(StackedSummaries.In(Doc(fixture), "x.cs"));
    }

    /// <summary>
    /// Two members with one summary each are two runs, not one - which is the case the scan would
    /// get wrong by not resetting at the declaration between them.
    /// </summary>
    [Fact]
    public void TwoMembersWithOneEachAreTwoRuns()
    {
        const string fixture = """
                @@@ <summary>The first member.</summary>
                public static void One() { }

                @@@ <summary>The second.</summary>
                public static void Two() { }
            """;

        Assert.Empty(StackedSummaries.In(Doc(fixture), "x.cs"));
    }

    /// <summary>
    /// A test's docstring sits above its attribute, and the report names the attribute - which is
    /// right, because that is where the docstring belongs.
    /// </summary>
    [Fact]
    public void ATestsRunIsReportedAtItsAttribute()
    {
        const string fixture = """
                @@@ <summary>One.</summary>
                @@@ <summary>Two.</summary>
                [Fact]
                public void ATest() { }
            """;

        Assert.Equal("[Fact]", Assert.Single(StackedSummaries.In(Doc(fixture), "x.cs")).Declares);
    }

    /// <summary>The built and generated trees are not this port's prose to answer for.</summary>
    [Theory]
    [InlineData(@"D:\x\app\Session\Thing.cs", true)]
    [InlineData(@"D:\x\app\obj\Debug\Thing.cs", false)]
    [InlineData(@"D:\x\app\bin\Debug\Thing.cs", false)]
    [InlineData("D:/x/tests/obj/Thing.cs", false)]
    public void TheGeneratedTreesAreNotOurs(string path, bool ours)
        => Assert.Equal(ours, StackedSummaries.IsOurs(path));

    /// <summary>PP272: the reader says no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
        => Assert.Empty(StackedSummaries.In("", "x.cs"));
}
