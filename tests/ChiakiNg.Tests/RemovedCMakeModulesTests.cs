using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP431: the build uses no CMake module CMake has removed.
///
/// CMP0148 removed FindPythonInterp and FindPythonLibs. The call survived only because
/// cmake_minimum_required names 3.10 and so selects that policy's OLD behaviour.
/// </summary>
public class RemovedCMakeModulesTests
{
    /// <summary>
    /// THE RULE. No removed module is called, and no variable one of them set is read.
    ///
    /// Both halves, because reverting one is the shape a hurried edit takes: a build that stopped
    /// calling FindPythonInterp and kept reading PYTHON_EXECUTABLE would configure cleanly and then
    /// run the nanopb generator as an empty string.
    /// </summary>
    [Fact]
    public void NoRemovedModuleOrItsVariablesAreUsed()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<string> uses = RemovedCMakeModules.Uses(root);

        Assert.True(
            uses.Count == 0,
            "the build uses a CMake module that has been removed, or a variable nothing sets:\n  "
                + string.Join("\n  ", uses));
    }

    /// <summary>
    /// And the replacement is there at both ends, so the rule above did not pass by deletion.
    ///
    /// PP271's lesson applied to a fix: "no removed module" is also true of a build that finds no
    /// interpreter at all, and that build cannot generate the protobuf.
    /// </summary>
    [Fact]
    public void TheReplacementIsThereAtBothEnds()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        Assert.True(
            RemovedCMakeModules.TheInterpreterIsStillFound(root),
            "the top-level build no longer asks FindPython3 for an interpreter");

        Assert.True(
            RemovedCMakeModules.TheGeneratorStillNamesTheInterpreter(root),
            "lib/protobuf no longer runs the nanopb generator as the interpreter it was given");
    }

    /// <summary>
    /// A comment naming the removed module does not count as using it.
    ///
    /// This fix left comments naming both the module and the variable it replaced, so a reader that
    /// counted those would report the thing it removed - PP400's rule, earned twice over here.
    /// </summary>
    [Fact]
    public void ACommentNamingTheOldModuleIsNotAUse()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        // The real tree has exactly those comments, and passes.
        Assert.Empty(RemovedCMakeModules.Uses(root));

        // The comments are really there, or this test proves nothing.
        string top = File.ReadAllText(Path.Combine(root, "CMakeLists.txt"));
        Assert.Contains("FindPythonInterp", top, StringComparison.Ordinal);
        Assert.Contains("PYTHON_EXECUTABLE", top, StringComparison.Ordinal);
    }

    /// <summary>Both module names CMP0148 removed are held, not just the one that was used.</summary>
    [Fact]
    public void BothRemovedModulesAreNamed()
    {
        Assert.Contains("PythonInterp", RemovedCMakeModules.Removed);
        Assert.Contains("PythonLibs", RemovedCMakeModules.Removed);
        Assert.Contains("PYTHON_EXECUTABLE", RemovedCMakeModules.AbandonedVariables);
    }

    /// <summary>PP272: and an empty tree reports nothing rather than passing about nothing.</summary>
    [Fact]
    public void AnEmptyTreeHasNoReplacementEither()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "pportal-cmake-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);

            Assert.Empty(RemovedCMakeModules.Uses(root));

            // And the positive checks answer no, so "no uses" alone is never the whole verdict.
            Assert.False(RemovedCMakeModules.TheInterpreterIsStillFound(root));
            Assert.False(RemovedCMakeModules.TheGeneratorStillNamesTheInterpreter(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
