namespace ChiakiNg.Session;

/// <summary>What a check of the running host found, or why it could not answer.</summary>
public enum HostBuildState
{
    /// <summary>The host running is at least as new as every source under app/.</summary>
    Fresh,

    /// <summary>A source is newer than it, so this host predates the tree it is answering about.</summary>
    Stale,

    /// <summary>No checkout beside it, which is how a published host runs and is not a fault.</summary>
    NoCheckout,

    /// <summary>The host's own path could not be read, so there is nothing to date.</summary>
    Unknown,
}

/// <summary>What a check found.</summary>
/// <param name="State">The answer.</param>
/// <param name="Host">The executable that was dated, where one was found.</param>
/// <param name="Newest">The newest source under app/, where any were found.</param>
public readonly record struct HostBuild(HostBuildState State, string? Host, string? Newest);

/// <summary>
/// PP530: whether the host answering a question about this tree was built from it.
///
/// PP304 and PP305 put two checks in the host - --recount reads the sizes the backlog states,
/// --ratchet lists the shipped tasks no assertion names - and both are run by hand before the
/// gate rather than by it. The skill that documents them writes the command as ChiakiNg.exe with
/// no path, because for a reader there is one host. There are two: compile.cmd builds and prints
/// Debug, and app\bin\Release holds whatever a publish last left.
///
/// On 2026-08-30 that Release copy was forty-four commits behind and did not know two flags that
/// had shipped in between, and it answered both checks all day without a word. Nothing it said
/// was wrong, which is the hazard rather than a mitigation: both read the docs at run time, so
/// their INPUT was current while their rules were two days old.
///
/// PP269 is the same shape one layer down and PP270 answered it with a guard that FAILS, on the
/// reasoning that a line on standard error scrolls past. This is that guard for the host's own
/// two flags, and only those two: they are the ones whose whole value is being trusted without
/// being re-derived.
///
/// <see cref="HostBuildState.NoCheckout"/> rather than a failure where there is nothing to
/// compare. A published host has no app\ beside it and is the ordinary way to run; a check that
/// refused there would be a check nobody could ship.
/// </summary>
public static class HostFreshness
{
    /// <summary>The tree the host is built from, relative to the repository root.</summary>
    public const string SourceRelativePath = "app";

    /// <summary>What counts as a source of it.</summary>
    public static IReadOnlyList<string> SourcePatterns { get; } = ["*.cs", "*.xaml", "*.csproj"];

    /// <summary>
    /// Build output, skipped.
    ///
    /// Not an optimisation: app\obj holds generated .cs that a build writes, and app\bin holds the
    /// host being dated. Reading either would compare a build against its own products, which is a
    /// rule that answers about the wrong thing and would do it inconsistently.
    /// </summary>
    public static IReadOnlyList<string> ExcludedDirectories { get; } = ["bin", "obj"];

    /// <summary>The check against the process that is running.</summary>
    public static HostBuild Check() => Check(Environment.ProcessPath);

    /// <summary>
    /// The same check over a host the caller names, which is what makes it testable: a test cannot
    /// move the process it runs in, so a rule that only ever read Environment.ProcessPath could be
    /// asserted about only from wherever the runner happens to sit.
    /// </summary>
    /// <param name="hostPath">The executable to date.</param>
    /// <param name="sourceDirectory">Where app/ is, or null to find it from the checkout.</param>
    public static HostBuild Check(string? hostPath, string? sourceDirectory = null)
    {
        if (string.IsNullOrEmpty(hostPath) || !File.Exists(hostPath))
            return new HostBuild(HostBuildState.Unknown, hostPath, null);

        string? sources = sourceDirectory ?? SanitizerSource.LocateDirectory(SourceRelativePath);
        if (sources is null || !Directory.Exists(sources))
            return new HostBuild(HostBuildState.NoCheckout, hostPath, null);

        DateTime built = File.GetLastWriteTimeUtc(hostPath);

        string? newest = null;
        DateTime newestAt = DateTime.MinValue;

        foreach (string file in Sources(sources))
        {
            DateTime at = File.GetLastWriteTimeUtc(file);
            if (at <= newestAt)
                continue;

            newestAt = at;
            newest = file;
        }

        if (newest is null)
            return new HostBuild(HostBuildState.NoCheckout, hostPath, null);

        // Equal counts as fresh, for the reason NativeFreshness records: a build writes the
        // executable after reading the sources, and a filesystem whose stamps agree to the second
        // would otherwise fail every run.
        return new HostBuild(
            newestAt > built ? HostBuildState.Stale : HostBuildState.Fresh, hostPath, newest);
    }

    /// <summary>Every source under app/, with the build output left out.</summary>
    private static IEnumerable<string> Sources(string root)
    {
        foreach (string pattern in SourcePatterns)
        {
            foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                if (!IsBuildOutput(root, file))
                    yield return file;
            }
        }
    }

    /// <summary>
    /// Whether a file sits under one of the excluded directories, as a segment and not as a
    /// substring: a source named binding.cs is not build output, and a check that matched "bin"
    /// anywhere in the path would decide that it was.
    /// </summary>
    private static bool IsBuildOutput(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file);
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            foreach (string excluded in ExcludedDirectories)
            {
                if (string.Equals(segment, excluded, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What to say when it is stale. It names both files because either could be the surprise -
    /// the source that moved, or the host nobody remembered building - and it names the path to
    /// run, because the whole defect is that two of them exist and only one is built.
    /// </summary>
    public static string Explain(HostBuild build)
        => $"[stale] {build.Host} is older than {build.Newest}, so this host predates the tree "
            + "it is about to answer for. Run compile.cmd, then the host it prints: "
            + @"app\bin\Debug\net10.0-windows\win-x64\ChiakiNg.exe";
}
