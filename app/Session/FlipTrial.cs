namespace ChiakiNg.Session;

/// <summary>One arranged deletion, and what its trial takes out of the suite.</summary>
/// <param name="Name">What <c>flip.cmd</c> is given, and what cmake compares against.</param>
/// <param name="Removes">The suite's own files that go with it, as the source list spells them.</param>
/// <param name="Switch">The definition main.c reads, which follows the files rather than leading them.</param>
public readonly record struct ArrangedFlip(
    string Name, IReadOnlyList<string> Removes, string Switch);

/// <summary>
/// PP803: the two arranged flips, and the trial that BUILDS the shape each one leaves.
///
/// Two deletions are prepared and neither has landed: PP696 takes the frame path's C files, PP27's
/// end state takes takion's. Both are arranged the same way - a define beside the source list,
/// guards in main.c, readers in app/ asserting the two agree - and every one of those checks reads
/// TEXT.
///
/// PP786 BUILT ONE INSTEAD, once, and it failed. <c>-Wl,--wrap=malloc</c> makes every allocation in
/// every linked object an undefined reference to <c>__wrap_malloc</c>, and the only definition of
/// that symbol is in allocbudget.c - the one file BOTH flips remove. The link error named allocation
/// sites in libchiaki, munit and jerasure, and every text check was green while it was true. PP760's
/// own trial had edited text and never rebuilt the C, which is exactly where this could hide.
///
/// WHAT THE TRIAL BUILDS IS THE SUITE'S SHAPE, and the bound is deliberate. chiaki-unit is the
/// target PP786's failure was in, it links chiaki-lib and munit and not the shim, and it needs no
/// edit to lib/ - so a trial asks a whole question without doing any of the flip. The LIBRARY's
/// after-shape wants PP780's four files and the shim's wrappers behind their define, and both of
/// those are the flip's own commit.
///
/// THIS CLASS IS THE JOIN. The trial's file sets live in cmake, where the build can act on them, and
/// here, where a check can compare them against the list they are removed from - so a file renamed
/// on one side is red rather than a trial that quietly removes nothing.
/// </summary>
public static class FlipTrial
{
    /// <summary>The launcher a person runs, which is not part of the gate.</summary>
    public const string LauncherFileName = "flip.cmd";

    /// <summary>Where its steps are, the way compile.cmd keeps its own.</summary>
    public const string ScriptRelativePath = @"scripts\trial-flip.sh";

    /// <summary>The suite's build file, which holds the source list and the trial's own reading.</summary>
    public const string SuiteRelativePath = @"test\CMakeLists.txt";

    /// <summary>The cache variable the trial sets, spelled as cmake reads it.</summary>
    public const string CacheVariable = "CHIAKI_TRIAL_FLIP";

    /// <summary>
    /// PP27: NONE, because both landed in one commit and a trial of a shape the tree is in measures
    /// nothing.
    ///
    /// takion and framepath were the two. They were never independent - videoreceiver.c is the only
    /// caller of two symbols takion's deletion strands - so the linker made them one commit, and the
    /// day they landed is the day this list emptied. The apparatus stays: <c>flip.cmd</c>, the script
    /// and the reader below are what PP786's failure bought, and a third arranged deletion needs a
    /// row here and an arm in the build file rather than a rebuilt instrument.
    ///
    /// EMPTY IS NOT NOTHING TO CHECK. The build file refuses every flip name now, and the pair have
    /// to agree about that the way they agreed about the file sets - a row here with no arm, or an
    /// arm with no row, is what the join is for in either direction.
    /// </summary>
    public static IReadOnlyList<ArrangedFlip> Flips { get; } = [];

    /// <summary>
    /// The switches main.c reads, which OUTLIVE the flips that were arranged around them.
    ///
    /// PP786 wrote them and PP803's rule about them is the one that does not expire: each follows the
    /// source list rather than being written flat, so the file leaving is the whole edit. They answer
    /// zero on this tree and the files they gate are gone, and both of those are still statements the
    /// build file has to keep making - a switch hard-coded to 0 would compile the same today and be
    /// a lie the moment anything came back.
    /// </summary>
    public static IReadOnlyList<string> Switches { get; } =
        ["CHIAKI_UNIT_HAVE_TAKION", "CHIAKI_UNIT_HAVE_FRAMEPATH"];

    /// <summary>
    /// The suite files both landed flips took, which the list must no longer hold.
    ///
    /// allocbudget.c is the one PP786's failure was about: claimed by both, gone with whichever
    /// landed first, and the only definition of __wrap_malloc. The --wrap block follows it out.
    /// </summary>
    public static IReadOnlyList<string> Departed { get; } =
        ["takion.c", "reorderqueue.c", "fec.c", "frameprocessor.c", "videoreceiver.c", "allocbudget.c"];

    /// <summary>The suite's build file, or null outside a checkout.</summary>
    public static string? LocateSuite() => SanitizerSource.LocateRelative(SuiteRelativePath);

    /// <summary>And the trial's own script.</summary>
    public static string? LocateScript() => SanitizerSource.LocateRelative(ScriptRelativePath);

    /// <summary>
    /// The files cmake removes for one flip, read out of the build file itself.
    ///
    /// The REMOVE_ITEM line and nothing else: a name in a comment is not a removal, which is why
    /// the arm is found by its cmake call rather than by the flip's name appearing nearby.
    /// </summary>
    public static IReadOnlyList<string> RemovedBy(string suiteSource, string flip)
    {
        ArgumentNullException.ThrowIfNull(suiteSource);
        ArgumentException.ThrowIfNullOrEmpty(flip);

        var wanted = false;

        foreach (string line in suiteSource.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith('#'))
                continue;

            if (trimmed.Contains($"{CacheVariable} STREQUAL \"{flip}\"", StringComparison.Ordinal))
            {
                wanted = true;
                continue;
            }

            if (!wanted)
                continue;

            // The first REMOVE_ITEM after the arm opens is that arm's, and the arm's end is the
            // next one opening - so a flip that removed nothing answers empty rather than the
            // following flip's list.
            if (trimmed.Contains("REMOVE_ITEM", StringComparison.Ordinal))
                return NamesIn(trimmed);

            if (trimmed.StartsWith("elseif", StringComparison.Ordinal)
                || trimmed.StartsWith("endif", StringComparison.Ordinal))
            {
                return [];
            }
        }

        return [];
    }

    /// <summary>Whether the suite's source list still holds a file, which a removal is about.</summary>
    public static bool SuiteListsFile(string suiteSource, string file)
    {
        ArgumentNullException.ThrowIfNull(suiteSource);
        ArgumentException.ThrowIfNullOrEmpty(file);

        int list = suiteSource.IndexOf("set(CHIAKI_UNIT_SOURCES", StringComparison.Ordinal);
        if (list < 0)
            return false;

        int closes = suiteSource.IndexOf(')', list);

        return closes > list
            && NamesIn(suiteSource[list..closes]).Contains(file, StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether a switch follows its files rather than being written flat.
    ///
    /// PP803's own trap: a trial that removed the four files and left the definition at 1 would
    /// compile main.c's guarded suites against sources that are not there, and the failure would be
    /// about the trial rather than about the flip.
    /// </summary>
    public static bool SwitchFollowsTheList(string suiteSource, string definition)
    {
        ArgumentNullException.ThrowIfNull(suiteSource);
        ArgumentException.ThrowIfNullOrEmpty(definition);

        return suiteSource.Contains($"{definition}=1", StringComparison.Ordinal)
            && suiteSource.Contains($"{definition}=0", StringComparison.Ordinal);
    }

    /// <summary>The .c and .h names in one span of cmake, which is how both lists are read.</summary>
    private static IReadOnlyList<string> NamesIn(string span)
        => [.. span
            .Split([' ', '\t', '\r', '\n', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(one => one.EndsWith(".c", StringComparison.Ordinal))];
}
