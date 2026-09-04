using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP660, under PP33: the shim is an oracle for both libraries PP33 deletes, and only one had been
/// counted.
///
/// PP653 asked the linker what holds holepunch.c in the build. Its answer was true and narrow:
/// json-c was still LINKED at the time, so the shim's own json-c calls resolved and the linker had
/// nothing to say about them. An attempt at PP655's flip found the rest, by failing to link.
/// </summary>
public class DeletedLibraryOraclesTests(ITestOutputHelper output)
{
    private static string? Shim()
        => DeletedLibraryOracles.LocateShim() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// PP33: the json oracle's wrappers are GONE, and the predicate that counted them still counts.
    ///
    /// It counted fifteen and the flip removed all fifteen. The reader is kept rather than deleted,
    /// which is PP634's rule: what a model like this is for after a deletion is noticing the calls
    /// coming back, and a check that was deleted with its subject notices nothing.
    ///
    /// The comparison did not go with them. tests/oracles/json-c.json holds what json-c answered,
    /// taken from the library by --record-json-oracle while it was here, and JsonCTests and
    /// FrameParsingTests compare against it on every build - which is more than they did while the
    /// library was present and the flag was off.
    /// </summary>
    [Fact]
    public void TheJsonOraclesWrappersAreGone()
    {
        if (Shim() is not { } shim)
            return;

        IReadOnlyList<string> json = DeletedLibraryOracles.JsonWrappers(shim);
        output.WriteLine($"{json.Count} json wrappers: {string.Join(", ", json)}");

        Assert.Empty(json);
    }

    /// <summary>
    /// And the flip's surface is nothing, which is the end state rather than a progress bar.
    ///
    /// PP655 was sized from ten undefined references and PP660 found the surface was larger - the
    /// json oracle was invisible to a linker asked while json-c was still linked. Both oracles have
    /// now gone in one commit, so what this asserts is zero: neither library is reachable from the
    /// shim, and a wrapper that came back would be a number here rather than a link error later.
    /// </summary>
    [Fact]
    public void TheFlipSurfaceIsNothing()
    {
        if (Shim() is not { } shim)
            return;

        int surface = DeletedLibraryOracles.FlipSurface(shim);
        output.WriteLine($"flip surface: {surface} exports");

        Assert.Equal(0, surface);
    }

    /// <summary>
    /// PP661: the json oracle is available today, and the sixteen comparisons that need it run.
    ///
    /// One side of the pair PP655's first step wants. Three test files hold managed JSON against
    /// json-c, and every one of those comparisons needs the library present - so each declines when
    /// it is not, and this says which state the tree is in rather than leaving it to be inferred
    /// from sixteen quiet returns.
    /// </summary>
    [Fact]
    public void TheJsonOracleIsHereAndTheComparisonsRun()
    {
        if (SanitizerSource.LocateRelative(DeletedLibraryOracles.ShimHeaderRelativePath) is null)
            return;

        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
        {
            // PP661: the other side, asked of the BUILD. The flip leaves these declarations in the
            // header inside an #ifdef, so a text reader would say the oracle is still here.
            Assert.False(DeletedLibraryOracles.JsonOracleIsAvailable());
            return;
        }

        Assert.True(DeletedLibraryOracles.JsonOracleIsAvailable());
    }

    /// <summary>
    /// The reader tells a call from a name, which is what the holepunch count needed too.
    ///
    /// Both directions, because a reader that matched the word would find the include line and the
    /// comment above it and report the whole file as an oracle.
    /// </summary>
    [Theory]
    [InlineData("	return json_tokener_parse(text);", true)]
    [InlineData("	if(!json_object_object_get_ex((json_object *)node, key, &found))", true)]
    [InlineData("#include <json-c/json_object.h>", false)]
    [InlineData("/* PP33: json-c, reachable so the managed replacement can be held against it. */", false)]
    [InlineData("	json_object *found = NULL;", false)]
    public void ANameIsNotACall(string line, bool calls)
        => Assert.Equal(calls, DeletedLibraryOracles.CallsJsonC(line));
}
