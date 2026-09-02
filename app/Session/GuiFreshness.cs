namespace ChiakiNg.Session;

/// <summary>What a check of the Qt client's build found, or why it could not answer.</summary>
public enum GuiBuildState
{
    /// <summary>The client that was built is at least as new as every source under gui/.</summary>
    Fresh,

    /// <summary>A source is newer than the client, so gui/ was edited and not rebuilt.</summary>
    Stale,

    /// <summary>No client has ever been built in this checkout, so there is nothing to compare.</summary>
    NeverBuilt,

    /// <summary>There is no checkout beside this host, which is an ordinary way to run.</summary>
    NoCheckout,

    /// <summary>
    /// PP632: nothing can rebuild the client, so nothing about it can be stale.
    ///
    /// This is the state PP597 asked for by name. It established that PP33's deletion and a
    /// buildable Qt client are mutually exclusive, and that the commit removing the field owed one
    /// of two things - "retire the client's build, or give GuiFreshness a state for it". PP598 chose
    /// the retirement and it turns out to owe this state anyway: the binary is still on disk for
    /// anybody who ever built one, so a check that went on comparing timestamps would be
    /// permanently red with nothing able to clear it. That is the failure PP597 was filed to
    /// prevent, reached from the other side.
    /// </summary>
    Retired,
}

/// <summary>What a check found.</summary>
/// <param name="State">The answer.</param>
/// <param name="Client">The client binary, where one was found.</param>
/// <param name="Newest">The newest source under gui/, where any were found.</param>
public readonly record struct GuiBuild(GuiBuildState State, string? Client, string? Newest);

/// <summary>
/// PP529: whether the Qt client anyone last built still matches the gui/ sources beside it.
///
/// No build in this repository compiles gui/. PP21 passes -DCHIAKI_ENABLE_GUI=OFF on every
/// configure so a stale cache cannot turn it back on, and build.yml passes the same, so a syntax
/// error there is green locally and green on push. gui/ is not dead all the same: the port's drift
/// checks read it, so it is edited whenever a fact they assert moves - PP528 changed
/// qmlbackend.cpp and qmlbackend.h, and the only reason that is known to compile is that the flag
/// was set by hand for one run.
///
/// This does not make the gate build gui/, and it is not pretending to. What it closes is the case
/// that actually happens: somebody doing gui/ work has built the client at least once, and after
/// that an edit they did not rebuild is a file on disk older than the change. That is a comparison
/// this can make, and it is made as a FAILURE for PP270's reason - a line on standard error in the
/// middle of four thousand passing tests is one nobody reads.
///
/// A checkout where the client was NEVER built answers <see cref="GuiBuildState.NeverBuilt"/>
/// rather than failing, which is the honest limit of this rule and is the state a fresh clone is
/// in. `compile.cmd gui` is what leaves it in any other.
/// </summary>
public static class GuiFreshness
{
    /// <summary>Where the Qt client lands, relative to the repository root.</summary>
    public const string ClientRelativePath = @"build\gui\chiaki.exe";

    /// <summary>
    /// The trees it is built from, relative to the repository root.
    ///
    /// gui\src and gui\include and not gui\ itself: the parent also holds CMakeLists.txt, which
    /// every configure of this tree touches whether or not a source moved, and a rule that read it
    /// would report the client stale after a build that had just refreshed it.
    /// </summary>
    public static IReadOnlyList<string> SourceRelativePaths { get; } = [@"gui\src", @"gui\include"];

    /// <summary>
    /// What counts as a source of it. .qml is here with the C++ because qmlcachegen compiles it
    /// into the binary, so a QML file that no longer parses fails the same build.
    /// </summary>
    public static IReadOnlyList<string> SourcePatterns { get; } = ["*.cpp", "*.h", "*.qml"];

    /// <summary>The check against this checkout.</summary>
    public static GuiBuild Check()
    {
        string? root = SanitizerSource.RepositoryRoot();
        if (root is null)
            return new GuiBuild(GuiBuildState.NoCheckout, null, null);

        return CheckIn(root);
    }

    /// <summary>
    /// The same check rooted at a directory the caller names, which is what makes it testable: a
    /// test cannot move a checkout, so a rule that only ever reads one arrangement is a rule
    /// nobody has run against the case it exists to catch.
    /// </summary>
    public static GuiBuild CheckIn(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        string client = Path.Combine(root, ClientRelativePath);

        // PP632: asked before anything is compared, and it is the answer on every tree from here.
        // gui/ calls eleven holepunch exports and session.c has stopped asking, so no argument
        // builds a client and no comparison against gui/ can mean anything. A binary found here is
        // one somebody built before the retirement.
        if (!File.Exists(client))
            return new GuiBuild(GuiBuildState.NeverBuilt, null, null);

        return new GuiBuild(GuiBuildState.Retired, client, null);
    }

    /// <summary>
    /// PP632: the comparison this used to make, kept for what it says about the state that is gone.
    ///
    /// Not called any more, and not deleted: a client that becomes buildable again - a port of gui/,
    /// or a PSN path that comes back - needs exactly this, and reconstructing it from the ledger is
    /// how a rule loses the two details that took a task each. Both are in the last four lines.
    /// </summary>
    public static GuiBuild WouldHaveCheckedIn(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        string client = Path.Combine(root, ClientRelativePath);
        if (!File.Exists(client))
            return new GuiBuild(GuiBuildState.NeverBuilt, null, null);

        DateTime built = File.GetLastWriteTimeUtc(client);

        string? newest = null;
        DateTime newestAt = DateTime.MinValue;

        foreach (string relative in SourceRelativePaths)
        {
            string directory = Path.Combine(root, relative);
            if (!Directory.Exists(directory))
                continue;

            foreach (string pattern in SourcePatterns)
            {
                foreach (string file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
                {
                    DateTime at = File.GetLastWriteTimeUtc(file);
                    if (at <= newestAt)
                        continue;

                    newestAt = at;
                    newest = file;
                }
            }
        }

        if (newest is null)
            return new GuiBuild(GuiBuildState.NoCheckout, client, null);

        // Equal counts as fresh, for the reason NativeFreshness records: a build writes the binary
        // after reading the sources, and a filesystem whose stamps agree to the second would
        // otherwise fail every run.
        return new GuiBuild(
            newestAt > built ? GuiBuildState.Stale : GuiBuildState.Fresh, client, newest);
    }

    /// <summary>
    /// What to say about an answer.
    ///
    /// PP529 wrote the stale sentence and named the COMMAND rather than the environment variable,
    /// because the variable was what nobody knew about. PP632 took the command away, so the stale
    /// sentence now names something that does not exist - and the retired one says what happened
    /// instead of what to run.
    /// </summary>
    public static string Explain(GuiBuild build)
        => build.State == GuiBuildState.Retired
            ? $"{build.Client} is from before the Qt client's build was retired (PP598, PP632). "
                + "gui/ calls eleven holepunch exports and session.c has stopped asking, so nothing "
                + "builds it - the source stays because the port's drift checks read it."
            : $"{build.Newest} is newer than {build.Client}, so gui/ was edited and the Qt client "
                + "was not rebuilt - and no ordinary build compiles it, so nothing else would have "
                + "said so.";
}
