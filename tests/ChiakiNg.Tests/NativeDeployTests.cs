using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP492: the deploy path a GUI-off build takes, and what it has to leave in the portable tree.
///
/// None of this can be checked by looking at the tree: the closure is twenty-five files here, a
/// different set on a runner, and nothing at all on a machine with no MSYS2. What a checkout can
/// answer is whether the branch calls the walk, which is the line PP269 was missing.
/// </summary>
public class NativeDeployTests
{
    /// <summary>The three scripts this joins together are all on disk.</summary>
    [Fact]
    public void TheThreeScriptsExist()
    {
        if (SanitizerSource.Locate() is null)
            return;

        Assert.NotNull(NativeDeploy.LocateBuild());
        Assert.NotNull(NativeDeploy.LocateQtDeploy());
        Assert.NotNull(NativeDeploy.LocateClosure());
    }

    /// <summary>
    /// THE REPAIR: the native-only branch hands the two libraries to the closure walk.
    ///
    /// PP269's version copied them and stopped, which is correct on every incremental build and
    /// leaves a tree nothing can load from after a clean. The assertion names both the pair and
    /// the call, because a check for the copy loop alone would pass on the broken version.
    /// </summary>
    [Fact]
    public void TheNativeOnlyPathCollectsWhatTheLibrariesImport()
    {
        if (NativeDeploy.LocateBuild() is not { } path)
            return;

        Assert.True(NativeDeploy.TheNativeOnlyPathCollectsTheClosure(File.ReadAllText(path)));
    }

    /// <summary>
    /// And the Qt path calls the same script rather than keeping the copy it had.
    ///
    /// Two implementations of one walk would agree until one was edited, and the one nobody runs
    /// on this machine is the Qt one - so the copy left behind would be the stale half.
    /// </summary>
    [Fact]
    public void TheQtPathDelegatesTheSameWalk()
    {
        if (NativeDeploy.LocateQtDeploy() is not { } path)
            return;

        Assert.True(NativeDeploy.TheQtPathDelegatesItsWalk(File.ReadAllText(path)));
    }

    /// <summary>
    /// The walk is transitive, and it does not bundle Windows' own DLLs.
    ///
    /// One level would carry the shim's imports and miss libplacebo's, which shows up as this
    /// machine working and another one not. Bundling a system DLL is the opposite failure and is
    /// quieter still - nothing is missing, so nothing reports it.
    /// </summary>
    [Fact]
    public void TheWalkIsTransitiveAndSkipsSystemLibraries()
    {
        if (NativeDeploy.LocateClosure() is not { } path)
            return;

        string script = File.ReadAllText(path);

        Assert.True(NativeDeploy.TheWalkIsTransitive(script));
        Assert.True(NativeDeploy.SystemLibrariesAreNotBundled(script));
    }

    /// <summary>
    /// Every library the resolver loads from that tree is owed by one of the two sources, and the
    /// split between them is not a preference.
    ///
    /// Two are built here and copied out of the build directory. SDL2 is not: the host opens it by
    /// name at runtime, so nothing imports it and no ldd walk will ever report it - which is why a
    /// GUI-off build lost it and package.cmd is what said so. The set has to equal the resolver's
    /// table, because a library in the table and in neither source is one nothing puts there.
    /// </summary>
    [Fact]
    public void EveryLibraryTheResolverLoadsIsStagedBySomething()
    {
        Assert.Equal(
            ChiakiNg.Native.ChiakiNative.NativeLibraries.Values.Order(),
            NativeDeploy.HostLibraries.Order());

        Assert.Empty(NativeDeploy.BuiltHere.Intersect(NativeDeploy.StagedFromMsys2));
    }
}
