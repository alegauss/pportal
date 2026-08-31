using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP595: the four surfaces `roadkeep install` writes here, and the wiring that reaches them.
///
/// They had drifted - the launcher and SKILL.md behind the engine, writing.md and asking.md not in
/// the tree at all - and `roadkeep lint` reported it on every gate run. Running the command cleared
/// all four notes, so that signal works and needs nothing from this file.
///
/// What has no signal is the wiring, and `install` is what could change it. That is what these are.
/// </summary>
public class RoadkeepSurfacesTests
{
    /// <summary>
    /// All four are on disk, named rather than counted.
    ///
    /// Two of them were absent while the directory existed with the third in it, so a check on the
    /// folder would have been green through the whole state this was filed from.
    /// </summary>
    [Fact]
    public void EverySurfaceInstallWritesIsHere()
    {
        IReadOnlyList<string> missing = RoadkeepSurfaces.Missing();

        Assert.True(
            missing.Count == 0,
            "`roadkeep install` writes these and they are not in the tree: "
                + string.Join(", ", missing));

        Assert.Equal(4, RoadkeepSurfaces.All.Count);
    }

    /// <summary>
    /// THE GUARD: .mcp.json reaches the committed launcher by a path any clone resolves.
    ///
    /// This project runs a launcher committed to the repository rather than a path to a checkout,
    /// which is what makes the server start where no plugin is installed and no roadkeep is cloned.
    /// `install` detects that and keeps the wiring on it; `uninstall` then `install` moves it to a
    /// checkout, and the difference is one absolute path on one machine.
    /// </summary>
    [Fact]
    public void TheServerIsReachedThroughTheProjectDirectory()
    {
        if (SanitizerSource.LocateRelative(RoadkeepSurfaces.McpConfigRelativePath) is not { } path)
            return;

        Assert.True(
            RoadkeepSurfaces.ReachesTheCommittedLauncher(File.ReadAllText(path)),
            ".mcp.json no longer reaches the committed launcher through ${CLAUDE_PROJECT_DIR} - so "
                + "the server starts on this machine and nowhere else, silently");
    }

    /// <summary>
    /// And the reader refuses a config rooted on one machine, which is the half a working machine
    /// cannot notice.
    ///
    /// Read on the shape rather than on this checkout's location: a config naming somebody else's
    /// roadkeep is the same defect, and a rule keyed on this one's path would be green for every
    /// clone it broke.
    /// </summary>
    [Fact]
    public void AConfigRootedOnOneMachineIsRefused()
    {
        const string checkout = """
            { "mcpServers": { "roadkeep": { "command": "python",
              "args": ["D:/Git/alegauss/roadkeep/scripts/roadkeep.py", "mcp"] } } }
            """;

        Assert.True(RoadkeepSurfaces.NamesAnAbsolutePath(checkout));
        Assert.False(RoadkeepSurfaces.ReachesTheCommittedLauncher(checkout));

        // A relative path that names the launcher but not the variable resolves against whatever
        // directory the harness happened to start in, which is not a promise.
        Assert.False(RoadkeepSurfaces.ReachesTheCommittedLauncher(
            """{ "args": [".claude/hooks/roadkeep-launch.py", "mcp"] }"""));

        Assert.True(RoadkeepSurfaces.ReachesTheCommittedLauncher(
            """{ "args": ["${CLAUDE_PROJECT_DIR:-.}/.claude/hooks/roadkeep-launch.py", "mcp"] }"""));
    }

    /// <summary>
    /// PP278's corpus knows .mcp.json, which is how a root file gets into that list.
    ///
    /// A one-segment path has no shape to be recognised by, so RootFiles is what separates it from
    /// any other constant carrying a dot. Naming it here as a constant is what reported it, and
    /// this is the pair of that guard rather than a second copy of it.
    /// </summary>
    [Fact]
    public void TheCorpusKnowsTheServerConfig()
    {
        Assert.Contains(
            RoadkeepSurfaces.McpConfigRelativePath,
            DriftCorpus.RootFiles,
            StringComparer.OrdinalIgnoreCase);

        Assert.Empty(DriftCorpus.UndeclaredRootFiles());
    }
}
