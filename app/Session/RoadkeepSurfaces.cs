namespace ChiakiNg.Session;

/// <summary>
/// PP595: the roadkeep surfaces this repository carries, and the wiring that makes them reachable
/// from a clone.
///
/// `roadkeep install` writes four files here - the launcher the hooks and the MCP server run
/// through, and the three skill files a session reads before it writes a governed file. They had
/// drifted: the launcher and SKILL.md were behind the engine, and writing.md and asking.md were not
/// in the tree at all. `roadkeep lint` said so on every gate run, and running the command cleared
/// it, so the note is a working signal and this is not about the note.
///
/// WHAT IS NOT GUARDED IS THE WIRING, and `install` is the command that could change it. This
/// project runs a launcher COMMITTED to the repository rather than a path to a checkout, and
/// .mcp.json reaches it through ${CLAUDE_PROJECT_DIR}. That is what makes the server start in an
/// environment that installs no plugin and clones no roadkeep - Claude Code on the web, and any
/// fresh clone of this repository. `install` detects the committed launcher and keeps the wiring on
/// it; `uninstall` then `install` moves it to a checkout, and the difference is one absolute path
/// on one machine.
///
/// A .mcp.json naming D:/Git/alegauss/roadkeep would work here and nowhere else, and it would fail
/// the way RK1446 describes: the harness holds a pipe nothing is on, reports CONNECT_TIMEOUT after
/// thirty seconds and drops every tool, while the hooks still fire - so a session is told to call
/// commands it was never handed. Silent, machine-specific, and invisible to anyone who has the
/// checkout.
/// </summary>
public static class RoadkeepSurfaces
{
    /// <summary>Where the MCP server is declared.</summary>
    public const string McpConfigRelativePath = ".mcp.json";

    /// <summary>The launcher the hooks and the server run through, committed here.</summary>
    public const string LauncherRelativePath = @".claude\hooks\roadkeep-launch.py";

    /// <summary>
    /// The three skill files `install` writes, all of them.
    ///
    /// Named rather than counted, and all three rather than the directory: two of them were absent
    /// while the directory existed with the third in it, which is the state this line was filed
    /// from. A check on the folder would have been green throughout.
    /// </summary>
    public static IReadOnlyList<string> SkillRelativePaths { get; } =
    [
        @".claude\skills\roadkeep\SKILL.md",
        @".claude\skills\roadkeep\writing.md",
        @".claude\skills\roadkeep\asking.md",
    ];

    /// <summary>Every surface `install` writes here, launcher and skills together.</summary>
    public static IReadOnlyList<string> All { get; } = [LauncherRelativePath, .. SkillRelativePaths];

    /// <summary>The ones that are not on disk. Empty is the only acceptable answer in a checkout.</summary>
    public static IReadOnlyList<string> Missing()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return [];

        return [.. All.Where(relative => !File.Exists(Path.Combine(root, relative)))];
    }

    /// <summary>The variable that makes the launcher's path the clone's own.</summary>
    public const string ProjectDirVariable = "CLAUDE_PROJECT_DIR";

    /// <summary>
    /// Whether .mcp.json reaches the committed launcher by a path any clone resolves.
    ///
    /// Three conditions, and the third is the one a working machine cannot notice. It has to name
    /// the launcher, reach it through ${CLAUDE_PROJECT_DIR}, and carry no absolute path - because a
    /// config that names a checkout is correct on the machine that has one and broken everywhere
    /// else, which is the half no local run reports.
    /// </summary>
    public static bool ReachesTheCommittedLauncher(string mcpConfig)
    {
        ArgumentNullException.ThrowIfNull(mcpConfig);

        // The launcher as JSON spells it, forward slashes rather than this project's backslashes.
        string launcher = LauncherRelativePath.Replace('\\', '/');

        return mcpConfig.Contains(launcher, StringComparison.OrdinalIgnoreCase)
            && mcpConfig.Contains(ProjectDirVariable, StringComparison.Ordinal)
            && !NamesAnAbsolutePath(mcpConfig);
    }

    /// <summary>
    /// Whether the config names a path rooted on one machine - a drive letter, or a UNC share.
    ///
    /// Read on the shape rather than on this checkout's own location: a config pointing at some
    /// other machine's roadkeep is the same defect, and a check keyed on "D:/Git/alegauss" would be
    /// green for everybody it broke.
    /// </summary>
    public static bool NamesAnAbsolutePath(string mcpConfig)
    {
        ArgumentNullException.ThrowIfNull(mcpConfig);

        foreach (string line in mcpConfig.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.Contains("\\\\\\\\", StringComparison.Ordinal))
                return true;

            for (int i = 0; i + 2 < line.Length; i++)
            {
                if (!char.IsAsciiLetter(line[i]) || line[i + 1] != ':')
                    continue;

                if (line[i + 2] is '/' or '\\')
                    return true;
            }
        }

        return false;
    }
}
