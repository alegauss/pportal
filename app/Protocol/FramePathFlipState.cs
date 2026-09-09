using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP843: which side of the frame path's flip the tree is on, asked of the BUILD and not of the last
/// task to touch the prose.
///
/// <see cref="ShimFramePathShape"/> already asks the DLL, which is PP661's lesson and is right. What
/// it could not do is notice that its own summary had stopped being true, and the summary is a
/// reasonable place for somebody to look up whether the flip has happened - so the sentence being
/// wrong sends a reader after an option that does not exist.
///
/// IT HAS BEEN WRONG TWICE, IN OPPOSITE DIRECTIONS. PP696 took the three files out and PP697 turned
/// nine files' sentences to the past tense. PP763 put the files back because the client could no
/// longer stream, and turned none of the sentences with them: for a whole block, every one of those
/// nine described a deletion the tree had undone. Then PP27 removed them again, which made the
/// sentences accidentally true - and that is the state PP843 was picked up in, its own design
/// describing a tree four commits stale.
///
/// SO THE GUARD IS NOT A PROSE EDIT. A third revert would falsify the same nine sentences the same
/// way, and nothing in the tree failed either of the first two times. What fails here is the
/// disagreement itself: three independent answers to one question - the library's source list, the
/// shim's option, and the shim the host loaded - plus the claim the prose makes, which has to be the
/// same answer.
///
/// THE PROSE IS READ FOR A PHRASE and that is the weak half, stated rather than hidden. A sentence
/// can be rewritten around the phrase and this would not see it. What it does catch is the case that
/// actually happened: the tree moving under prose nobody edited at all.
/// </summary>
public static class FramePathFlipState
{
    /// <summary>The library's build file, which carries the source list the flip empties.</summary>
    public const string LibraryRelativePath = @"lib\CMakeLists.txt";

    /// <summary>The shim's, which carries the option the oracle sits behind.</summary>
    public const string ShimRelativePath = @"shim\CMakeLists.txt";

    /// <summary>The file whose summary is held against them.</summary>
    public const string ProseRelativePath = @"app\Protocol\ShimFramePathShape.cs";

    /// <summary>The option a person turns, spelled as cmake reads it.</summary>
    public const string OracleOption = "CHIAKI_SHIM_FRAMEPATH_ORACLE";

    /// <summary>And the definition it hands the compiler, which the fourteen wrappers sit inside.</summary>
    public const string OracleDefine = "CHIAKI_SHIM_HAVE_FRAMEPATH";

    /// <summary>
    /// The claim <see cref="ShimFramePathShape"/>'s summary makes, in the words it makes it in.
    ///
    /// Spelled here rather than there, so the file under test carries no copy of the phrase that
    /// would satisfy the reader without saying anything - the trap PP847 is filed about.
    /// </summary>
    public const string FlipClaim = "THE FLIP HAS HAPPENED";

    /// <summary>
    /// The three library files the flip removes, by the bare names every note about them uses.
    ///
    /// streamconnection.c is deliberately not among them. It left in the same commit and it is not
    /// what the oracle wraps: the fourteen exports are over these three, so these three are what
    /// decides whether the wrapping side can link at all.
    /// </summary>
    public static IReadOnlyList<string> Files { get; } =
        ["fec.c", "frameprocessor.c", "videoreceiver.c"];

    /// <summary>The directory the source list prefixes them with, which is what makes an entry one.</summary>
    public const string SourcePrefix = "src/";

    /// <summary>The library's build file, or null outside a checkout.</summary>
    public static string? LocateLibrary() => SanitizerSource.LocateRelative(LibraryRelativePath);

    /// <summary>The shim's, or null outside a checkout.</summary>
    public static string? LocateShim() => SanitizerSource.LocateRelative(ShimRelativePath);

    /// <summary>The prose, or null outside a checkout.</summary>
    public static string? LocateProse() => SanitizerSource.LocateRelative(ProseRelativePath);

    /// <summary>
    /// Which of <see cref="Files"/> the library still compiles.
    ///
    /// TWO THINGS MAKE AN ENTRY, and neither on its own is enough here. <c>lib/CMakeLists.txt</c> is
    /// where this deletion is EXPLAINED, and PP27's note names all three - so a reader matching the
    /// bare name would report the flip as never having happened. The prefix is what a source-list
    /// entry has and a sentence does not; comments are skipped as well, because a note quoting the
    /// line it deleted is a shape this file could easily have and the prefix would not save it.
    /// </summary>
    public static IReadOnlyList<string> InTheLibrarySources(string libSource)
    {
        ArgumentNullException.ThrowIfNull(libSource);

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (string line in libSource.Split('\n'))
        {
            string code = line.Trim();
            if (code.Length == 0 || code.StartsWith('#'))
                continue;

            foreach (string one in Files)
            {
                if (code.Contains(SourcePrefix + one, StringComparison.Ordinal))
                    found.Add(one);
            }
        }

        return [.. Files.Where(found.Contains)];
    }

    /// <summary>
    /// The oracle option's declared default, or null where no <c>option()</c> declares it.
    ///
    /// Null is a state and not a failure to read: before PP27 the define was set unconditionally,
    /// with no option to turn, and that is a tree where the wrapping side is the only side there is.
    /// </summary>
    public static bool? OracleOptionDefault(string shimSource)
    {
        ArgumentNullException.ThrowIfNull(shimSource);

        foreach (string line in shimSource.Split('\n'))
        {
            string code = line.Trim();
            if (!code.StartsWith("option(", StringComparison.Ordinal)
                || !code.Contains(OracleOption, StringComparison.Ordinal))
            {
                continue;
            }

            // option(NAME "description" DEFAULT) - the last token before the close.
            int closes = code.LastIndexOf(')');
            if (closes < 0)
                continue;

            string[] words = code[..closes]
                .Split([' ', '\t', '"'], StringSplitOptions.RemoveEmptyEntries);

            if (words.Length > 0)
                return words[^1].Equals("ON", StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    /// <summary>
    /// The shape the two build files put the tree in.
    ///
    /// Bare needs BOTH: the sources gone and the option off. Either one alone is a tree somebody is
    /// halfway through, and PP27's note is what makes that more than pedantry - with the files out,
    /// the option on is a link failure rather than a preference, so the two disagreeing is a state
    /// no build can be in.
    /// </summary>
    public static ShimShape OfTheSources(string libSource, string shimSource)
    {
        ArgumentNullException.ThrowIfNull(libSource);
        ArgumentNullException.ThrowIfNull(shimSource);

        return InTheLibrarySources(libSource).Count == 0 && OracleOptionDefault(shimSource) == false
            ? ShimShape.Bare
            : ShimShape.Wrapping;
    }

    /// <summary>Whether the prose still claims the flip has happened.</summary>
    public static bool ProseClaimsTheFlip(string proseSource)
    {
        ArgumentNullException.ThrowIfNull(proseSource);
        return proseSource.Contains(FlipClaim, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every disagreement between the four answers, as sentences.
    ///
    /// Named rather than counted, because which two disagree is the whole diagnosis: sources against
    /// option is a half-done edit, either against the DLL is a tree that was not rebuilt, and prose
    /// against any of them is what happened twice.
    /// </summary>
    public static IReadOnlyList<string> Disagreements(
        string libSource, string shimSource, string proseSource, ShimShape built)
    {
        ArgumentNullException.ThrowIfNull(libSource);
        ArgumentNullException.ThrowIfNull(shimSource);
        ArgumentNullException.ThrowIfNull(proseSource);

        var apart = new List<string>();

        IReadOnlyList<string> compiled = InTheLibrarySources(libSource);
        bool? option = OracleOptionDefault(shimSource);

        if (compiled.Count == 0 && option != false)
        {
            apart.Add($"{LibraryRelativePath} compiles none of the three and {OracleOption} is "
                + (option is null ? "not an option at all" : "ON")
                + ", which is a build that cannot link");
        }

        if (compiled.Count > 0 && option == false)
        {
            apart.Add($"{OracleOption} is OFF and {LibraryRelativePath} still compiles "
                + string.Join(", ", compiled));
        }

        ShimShape sources = OfTheSources(libSource, shimSource);

        if (sources != built)
        {
            apart.Add($"the build files say {sources} and the loaded shim says {built} - "
                + "the tree moved without being rebuilt");
        }

        bool claimed = ProseClaimsTheFlip(proseSource);

        if (claimed && sources == ShimShape.Wrapping)
        {
            apart.Add($"{ProseRelativePath} says \"{FlipClaim}\" and the build files say "
                + $"{ShimShape.Wrapping}: {string.Join(", ", compiled)}");
        }

        if (!claimed && sources == ShimShape.Bare)
        {
            apart.Add($"{ProseRelativePath} no longer says \"{FlipClaim}\" and the flip is in place");
        }

        return apart;
    }
}
