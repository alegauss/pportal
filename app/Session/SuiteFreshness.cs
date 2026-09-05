namespace ChiakiNg.Session;

/// <summary>
/// PP720: how the suite decides its binary is stale, and why the tree is the wrong thing to ask.
///
/// PP56 fixed a stale green - ctest answering about a binary older than the code - and the launcher
/// warns when it sees one. It used to warn by globbing lib and test for a .c newer than the
/// executable, which is a question about the TREE while the failure is about the BUILD.
///
/// THE TWO STOPPED BEING THE SAME THING. lib/src/remote/holepunch.c left the build with PP33 and
/// stayed in the checkout, because this port's drift checks read C that no target compiles - the
/// same reason gui/ is still here. Its mtime went ahead of the executable's, and from then on every
/// run of test.cmd opened with a warning whose own advice could not clear it: compile.cmd answers
/// "ninja: no work to do", and ninja is right, because the file is in no graph.
///
/// A WARNING NOBODY CAN CLEAR IS A WARNING NOBODY READS. PP56's guard exists so a reader does not
/// trust a green about the previous build, and firing on every invocation is how it stops doing
/// that - the one run where a real lib/ edit went uncompiled looks like all the others.
///
/// SO NINJA IS ASKED. A dry run of the unit target answers whether anything the binary is built
/// from has moved, and acting on that answer changes it. What this deliberately is NOT is a list of
/// files to skip: PP279's finding is that a hand-kept list guards only what somebody thought of,
/// and holepunch.c would have joined such a list only after somebody noticed.
/// </summary>
public static class SuiteFreshness
{
    /// <summary>The launcher the check lives in.</summary>
    public const string ScriptRelativePath = @"scripts\test-windows.sh";

    /// <summary>The build graph, which is what now answers the question.</summary>
    public const string BuildGraphRelativePath = @"build\build.ninja";

    /// <summary>The target whose freshness the suite depends on.</summary>
    public const string Target = "chiaki-unit";

    /// <summary>What ninja says when the target is current, and the whole of the test.</summary>
    public const string UpToDate = "no work to do";

    /// <summary>
    /// The file that made the old warning unclearable, and still would.
    ///
    /// Named because it is the case, not because it is excepted: nothing skips it, and the check
    /// below is what says it is still outside the graph rather than a rule saying to ignore it.
    /// </summary>
    public const string OutOfTheGraph = @"lib\src\remote\holepunch.c";

    /// <summary>The glob the check used to be, which must not come back.</summary>
    public const string TheOldGlob = "find lib test -name";

    /// <summary>One of the two files, or null outside a checkout.</summary>
    public static string? Locate(string relativePath) => SanitizerSource.LocateRelative(relativePath);

    /// <summary>
    /// Whether the launcher asks ninja about the target rather than globbing the tree.
    /// </summary>
    public static bool TheLauncherAsksTheBuild(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return script.Contains($"-n {Target}", StringComparison.Ordinal)
            && script.Contains(UpToDate, StringComparison.Ordinal)
            && !script.Contains(TheOldGlob, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a source is a file some target actually compiles, as the graph spells it.
    ///
    /// The graph writes paths with forward slashes and escapes the drive colon, so the match is on
    /// the file's own tail rather than on a path this side builds.
    /// </summary>
    public static bool IsInTheBuildGraph(string buildGraph, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(buildGraph);
        ArgumentNullException.ThrowIfNull(relativePath);

        return buildGraph.Contains(
            relativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }
}
