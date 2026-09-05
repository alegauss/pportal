using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP720: the staleness warning, asked of the build instead of the tree.
///
/// PP56's guard exists so nobody trusts a green that answers about the previous build. It globbed
/// lib and test for a .c newer than the executable, and once holepunch.c left the build with PP33
/// and stayed in the checkout, that glob fired on every single run - with advice that could not
/// clear it, because compile.cmd answers "no work to do" and ninja is right.
///
/// A WARNING NOBODY CAN CLEAR IS A WARNING NOBODY READS, which is the guard failing rather than
/// being noisy. What is asserted here is that the launcher asks ninja, and that the case which
/// broke the old one is still exactly the case: a file newer than the binary and in no graph.
/// </summary>
public class SuiteFreshnessTests(ITestOutputHelper output)
{
    private static string? Read(string relativePath)
    {
        string? path = SuiteFreshness.Locate(relativePath);

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>The launcher asks ninja about the target, and the old glob is gone.</summary>
    [Fact]
    public void TheLauncherAsksNinjaAndNotTheTree()
    {
        if (Read(SuiteFreshness.ScriptRelativePath) is not { } script)
            return;

        Assert.True(
            SuiteFreshness.TheLauncherAsksTheBuild(script),
            "the launcher no longer asks ninja for the unit target, or the tree glob is back");
    }

    /// <summary>
    /// THE CASE THAT BROKE IT: a source kept in the tree that no target compiles.
    ///
    /// The mtime half is deliberately NOT asserted. Whether holepunch.c is newer than the binary
    /// right now depends on when either was last touched - a rebuild flips it, and a test asserting
    /// it would go red for a reason that has nothing to do with the guard. That coincidence is what
    /// REVEALED the defect; what makes it a defect is the durable half below, which a tree glob
    /// cannot see and a build graph answers exactly.
    /// </summary>
    [Fact]
    public void TheFileThatBrokeItIsStillOutsideTheGraph()
    {
        if (SuiteFreshness.Locate(SuiteFreshness.OutOfTheGraph) is not { } source
            || Read(SuiteFreshness.BuildGraphRelativePath) is not { } graph)
        {
            return;
        }

        output.WriteLine($"{SuiteFreshness.OutOfTheGraph} is in the tree at {source}");

        Assert.True(File.Exists(source), "holepunch.c has left the tree, so PP720's case has gone");

        Assert.False(
            SuiteFreshness.IsInTheBuildGraph(graph, SuiteFreshness.OutOfTheGraph),
            "holepunch.c is back in the build graph, so PP33's deletion has been undone");
    }

    /// <summary>
    /// And a file that IS compiled is in the graph, so the reader is not answering false to all.
    ///
    /// PP271's shape: a reader that found nothing would satisfy the assertion above by failing to
    /// find anything at all.
    /// </summary>
    [Fact]
    public void AFileTheSuiteCompilesIsInTheGraph()
    {
        if (Read(SuiteFreshness.BuildGraphRelativePath) is not { } graph)
            return;

        Assert.True(
            SuiteFreshness.IsInTheBuildGraph(graph, @"lib\src\takion.c"),
            "takion.c is not in the build graph, so the reader is not reading it");

        Assert.True(SuiteFreshness.IsInTheBuildGraph(graph, @"test\main.c"));
    }
}
