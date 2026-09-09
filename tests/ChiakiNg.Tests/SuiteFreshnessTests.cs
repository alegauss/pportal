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
    ///
    /// PP844: THE CONTROL IS READ AND NO LONGER TYPED. It was session.c here, and takion.c before
    /// PP27 took that file out of the build - which made this red on the day the deletion landed, a
    /// failure about the fixture wearing the reader's name. <see cref="ControlFiles"/> is where the
    /// choice lives now, so the rule the fix wrote as prose - a file a deletion is aiming at is the
    /// one thing a control must never be - is a check over a declaration rather than a sentence in
    /// a comment. Typed here it was also a second copy: two places to update and one that anybody
    /// would notice.
    /// </summary>
    [Fact]
    public void AFileTheSuiteCompilesIsInTheGraph()
    {
        if (Read(SuiteFreshness.BuildGraphRelativePath) is not { } graph)
            return;

        string[] positives =
        [
            .. ControlFiles.All
                .Where(one => one.Where == nameof(SuiteFreshness) && one.MustBeCompiled)
                .Select(one => one.RelativePath)
        ];

        Assert.NotEmpty(positives);

        foreach (string control in positives)
        {
            Assert.True(
                SuiteFreshness.IsInTheBuildGraph(graph, control),
                $"{control} is not in the build graph, so the reader is not reading it");
        }
    }
}
