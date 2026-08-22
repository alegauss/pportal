using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>Why a freshness check could not answer, or that it did.</summary>
public enum FreshnessVerdict
{
    /// <summary>The loaded library is at least as new as its sources.</summary>
    Fresh,

    /// <summary>It is older than one of them.</summary>
    Stale,

    /// <summary>The library was never loaded, so there is nothing to compare.</summary>
    NotLoaded,

    /// <summary>There is no checkout beside this host, which is an ordinary way to run.</summary>
    NoSources,
}

/// <summary>What a check found.</summary>
/// <param name="Verdict">The answer.</param>
/// <param name="Library">Which file was loaded, where one was.</param>
/// <param name="Newest">The newest source, where any were found.</param>
public readonly record struct Freshness(FreshnessVerdict Verdict, string? Library, string? Newest);

/// <summary>
/// PP270: whether the native library a run loaded is the one its sources describe.
///
/// PP269 measured a shim four hours older than the build that had just been run, because the deploy
/// step that refreshes the portable tree had been switched off with the Qt client and the resolver
/// looks there first. It was found only because a task happened to add an entry point that was then
/// missing. Nothing would have found the next one.
///
/// The C suite already guards this shape - anything under lib or test newer than the unit binary
/// prints a warning, on the reasoning that a green over a binary the sources have moved past is
/// worse than a red. This is the same question for the other side, and it is asked as a FAILURE
/// rather than a warning: a line on standard error in the middle of two thousand passing tests is
/// what PP269's DLL would have produced, and nobody would have read it.
///
/// It answers <see cref="FreshnessVerdict.NotLoaded"/> and <see cref="FreshnessVerdict.NoSources"/>
/// rather than failing where there is nothing to compare - a published host has no checkout beside
/// it, and a run that never touched the shim has no file to date.
/// </summary>
public static class NativeFreshness
{
    /// <summary>
    /// A file the shim is built from, used to find the directory holding the rest.
    ///
    /// A FILE and not the directory: <see cref="SanitizerSource.LocateRelative"/> tests File.Exists,
    /// so a directory handed to it always answers null - and this check would then report having no
    /// sources on every run, passing for the reason it exists to catch. Measured by backdating the
    /// library four hours and watching the guard stay green.
    /// </summary>
    public const string ShimAnchor = @"shim\chiaki_shim.c";

    /// <summary>What counts as a source of it.</summary>
    public static IReadOnlyList<string> SourcePatterns { get; } = ["*.c", "*.h"];

    /// <summary>
    /// Compares the library a run loaded against the newest source it is built from.
    /// </summary>
    /// <param name="loadedFrom">
    /// The path the resolver reported, which is <see cref="ChiakiNative.LoadedFrom"/> in a run that
    /// has called into the shim.
    /// </param>
    /// <param name="sourceDirectory">
    /// Where the sources are, or null to look for them beside the running assembly.
    /// </param>
    public static Freshness Check(string? loadedFrom, string? sourceDirectory = null)
    {
        if (string.IsNullOrEmpty(loadedFrom) || !File.Exists(loadedFrom))
            return new Freshness(FreshnessVerdict.NotLoaded, loadedFrom, null);

        string? sources = sourceDirectory
            ?? Path.GetDirectoryName(SanitizerSource.LocateRelative(ShimAnchor));

        if (sources is null || !Directory.Exists(sources))
            return new Freshness(FreshnessVerdict.NoSources, loadedFrom, null);

        DateTime built = File.GetLastWriteTimeUtc(loadedFrom);

        string? newest = null;
        DateTime newestAt = DateTime.MinValue;

        foreach (string pattern in SourcePatterns)
        {
            foreach (string file in Directory.EnumerateFiles(sources, pattern, SearchOption.AllDirectories))
            {
                DateTime at = File.GetLastWriteTimeUtc(file);
                if (at <= newestAt)
                    continue;

                newestAt = at;
                newest = file;
            }
        }

        if (newest is null)
            return new Freshness(FreshnessVerdict.NoSources, loadedFrom, null);

        // Equal counts as fresh: a build writes the DLL after reading the sources, and a filesystem
        // whose stamps agree to the second would otherwise fail every run.
        return new Freshness(
            newestAt > built ? FreshnessVerdict.Stale : FreshnessVerdict.Fresh, loadedFrom, newest);
    }

    /// <summary>
    /// What to say when it is stale, which names both files because either could be the surprise.
    /// </summary>
    public static string Explain(Freshness freshness)
        => $"the shim loaded from {freshness.Library} is older than {freshness.Newest}, "
            + "so this run is testing a library the sources have moved past - "
            + "run compile.cmd, which refreshes the portable tree the resolver reads first";
}
