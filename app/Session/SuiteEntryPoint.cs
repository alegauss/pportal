namespace ChiakiNg.Session;

/// <summary>
/// PP760: the C suite's entry point, and the switch that decides which suites it has.
///
/// PP758 taught the managed readers both shapes and proved it by running them against an after-shape
/// tree. That trial edited text and never rebuilt the C, which is what it was for and also what it
/// could not see: test/main.c declares an extern for every suite and lists every one in suites[].
/// Four of those are the frame path's, and taking their files out of the build leaves four externs
/// with no definition - a link failure before a single case runs.
///
/// THE SHAPE IS ALREADY IN THAT FILE. ffmpegdecoder.c is conditional, because a configure that
/// cannot find ffmpeg turns its tri_option off, and both its extern and its suites[] entry sit
/// behind a macro. A conditional suite here has a settled spelling; the four just never needed one.
///
/// WHAT DIFFERS IS WHERE THE MACRO COMES FROM. ffmpeg's rides on config.h out of lib/, and PP623's
/// shape gives this deletion exactly one commit that edits lib/ - so a definition added there would
/// be that edit made twice. This one is on the test target, beside the list it has to agree with.
///
/// READ AND NOT ASSUMED, in both directions. A guard around the wrong four would compile today and
/// fail on the day it matters, and a guard the build never defines would silently take four suites
/// out of every run - which is exactly the failure the floor file exists to catch, arriving through
/// the file that was supposed to prevent it.
/// </summary>
public static partial class SuiteEntryPoint
{
    /// <summary>The suite's entry point, which names every suite it runs.</summary>
    public const string MainRelativePath = @"test\main.c";

    /// <summary>The list that compiles it, and defines the switch.</summary>
    public const string ListRelativePath = FramePathConsumers.SuiteListRelativePath;

    /// <summary>The switch main.c reads.</summary>
    public const string Guard = "CHIAKI_UNIT_HAVE_FRAMEPATH";

    /// <summary>
    /// The four suites that go behind it, by the symbol main.c externs.
    ///
    /// Derived from the files that leave rather than typed beside them: <see cref="FramePathConsumers.Suite"/>
    /// is where the four C test files are named, and a second list here would be a second place to
    /// forget one.
    /// </summary>
    public static IReadOnlyList<string> Guarded { get; } =
        [.. FramePathConsumers.Suite.Select(one => SymbolFor(one.File))];

    /// <summary>The munit symbol a C test file defines, which main.c externs.</summary>
    public static string SymbolFor(string file) => file switch
    {
        "fec.c" => "tests_fec",
        "frameprocessor.c" => "tests_frame_processor",
        "allocbudget.c" => "tests_alloc_budget",
        "videoreceiver.c" => "tests_video_receiver",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "not one of the four that leave"),
    };

    /// <summary>
    /// A suite that stays, which is what makes "all four are guarded" mean something.
    ///
    /// PP786: tests_takion, until takion's own three got a guard of their own. It was the right
    /// control while this file knew one switch and became the wrong one the moment there were two -
    /// a symbol behind ANOTHER guard is not evidence that this guard is placed correctly. http.c is
    /// claimed by neither flip, which is what a control has to be.
    /// </summary>
    public const string StaysUnguarded = "tests_http";

    /// <summary>
    /// PP786: the frame path's own three, which are what decide whether its switch should be on.
    ///
    /// allocbudget.c is not among them and that is the point. PP780's link named it under takion as
    /// well - it calls chiaki_takion_v9_av_packet_parse and takion_handle_packet_av - so it leaves
    /// on whichever of the two deletions lands first. Asking about all four would report this
    /// switch as half-finished on the day the OTHER one lands, which is a red with nothing wrong.
    /// </summary>
    public static IReadOnlyList<string> OwnFiles { get; } =
        [.. FramePathConsumers.Suite.Select(one => one.File).Where(one => one != SharedWithTakion)];

    /// <summary>The one file both flips claim, named rather than inferred from two lists.</summary>
    public const string SharedWithTakion = "allocbudget.c";

    /// <summary>main.c, or null outside a checkout.</summary>
    public static string? LocateMain() => SanitizerSource.LocateRelative(MainRelativePath);

    /// <summary>The list, or null outside a checkout.</summary>
    public static string? LocateList() => SanitizerSource.LocateRelative(ListRelativePath);

    /// <summary>
    /// Every symbol that appears only inside a <c>#if</c> on <see cref="Guard"/>.
    ///
    /// Inside AND NOT ALSO OUTSIDE: a name mentioned in both places is not guarded, because the
    /// unguarded mention is the one that fails to link. Nested conditionals are counted so an
    /// <c>#if</c> within the block does not close it early.
    /// </summary>
    public static IReadOnlyList<string> GuardedIn(string mainSource)
        => GuardedIn(mainSource, Guard, [.. Guarded, StaysUnguarded]);

    /// <summary>
    /// PP786: the same walk, asked about any guard and any set of symbols.
    ///
    /// main.c has two switches now and will have a third the day another module's C leaves, and one
    /// preprocessor walker per switch is the duplication this repository has already paid for twice.
    /// A NESTED guard is read correctly from either side, which is what allocbudget.c needs: its
    /// entry sits inside the frame path's block and inside takion's, and both questions have to
    /// answer yes about it.
    /// </summary>
    public static IReadOnlyList<string> GuardedIn(
        string mainSource, string guard, IReadOnlyList<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(mainSource);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(symbols);

        var inside = new HashSet<string>(StringComparer.Ordinal);
        var outside = new HashSet<string>(StringComparer.Ordinal);

        int depth = 0;
        int opened = 0;

        foreach (string line in mainSource.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("#if", StringComparison.Ordinal))
            {
                depth++;
                if (opened == 0 && trimmed.Contains(guard, StringComparison.Ordinal))
                    opened = depth;

                continue;
            }

            if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
            {
                if (opened == depth)
                    opened = 0;

                depth--;
                continue;
            }

            // An #else flips which side of the guard the lines are on, and neither side is what this
            // is about - a symbol there is available on one shape only in the other direction.
            if (trimmed.StartsWith("#else", StringComparison.Ordinal) && opened == depth)
                opened = 0;

            foreach (string symbol in symbols)
            {
                if (!line.Contains(symbol, StringComparison.Ordinal))
                    continue;

                _ = opened > 0 ? inside.Add(symbol) : outside.Add(symbol);
            }
        }

        inside.ExceptWith(outside);
        return [.. inside.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// What the build defines the switch as, or null where it defines it not at all.
    ///
    /// The value matters and not only the presence: a list that dropped the four files and left this
    /// at one would compile main.c's four entries against nothing.
    /// </summary>
    public static string? DefinedAs(string cmake) => DefinedAs(cmake, Guard);

    /// <summary>PP786: and the same reader, for whichever of the list's switches is being asked about.</summary>
    public static string? DefinedAs(string cmake, string guard)
    {
        ArgumentNullException.ThrowIfNull(cmake);
        ArgumentNullException.ThrowIfNull(guard);

        int at = cmake.IndexOf(guard + "=", StringComparison.Ordinal);
        if (at < 0)
            return null;

        int from = at + guard.Length + 1;
        int to = from;
        while (to < cmake.Length && !char.IsWhiteSpace(cmake[to]) && cmake[to] != ')')
            to++;

        return cmake[from..to];
    }

    /// <summary>
    /// Whether the switch and the source list agree, which is the one thing that must never drift.
    ///
    /// On is the shape where the four files are compiled; off is the shape where they are not. Any
    /// other pairing is a build that either links nothing or links four suites it did not compile.
    ///
    /// PP786: asked about <see cref="OwnFiles"/> and not about all four. allocbudget.c leaves on
    /// whichever of the two deletions lands first, so a question over the whole set would call this
    /// switch half-finished on the day takion's lands - a red about a file that left correctly, for
    /// the other flip's reason.
    /// </summary>
    public static bool TheSwitchAgreesWithTheList(string cmake)
        => SwitchAgrees(cmake, Guard, OwnFiles);

    /// <summary>
    /// PP786: the agreement, for whichever switch and whichever files decide it.
    ///
    /// One implementation because the failure is one failure: a switch left on with its files gone
    /// links suites that were never compiled, and a switch turned off with its files present takes
    /// their cases out of every run silently - which is the floor's own subject.
    /// </summary>
    public static bool SwitchAgrees(string cmake, string guard, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(cmake);
        ArgumentNullException.ThrowIfNull(files);

        IReadOnlyList<string> listedFiles = FramePathConsumers.SuiteFilesIn(cmake);
        bool listed = files.All(one => listedFiles.Contains(one, StringComparer.Ordinal));

        string? defined = DefinedAs(cmake, guard);

        return listed ? defined == "1" : defined is null or "0";
    }

    /// <summary>The condition the allocator's link options have to sit inside.</summary>
    public const string AllocatorCondition = "if(\"" + SharedWithTakion + "\" IN_LIST";

    /// <summary>
    /// PP786: whether the allocator wrap follows the one file that defines its symbols.
    ///
    /// THE LINK FAILURE NEITHER FLIP WAS READY FOR, found by building the bare shape rather than by
    /// reading. <c>-Wl,--wrap=malloc</c> turns every allocation in every linked object - libchiaki's,
    /// munit's and jerasure's - into a reference to <c>__wrap_malloc</c>, and the only definition of
    /// that symbol is in allocbudget.c. Both deletions remove that file, so both would have stopped
    /// at a link error listing every allocation site in the tree, in the one commit that may not
    /// edit a test file.
    ///
    /// Asserted rather than remembered because the options and the file are three screens apart, and
    /// the tie between them is invisible from either end.
    /// </summary>
    public static bool TheAllocatorWrapFollowsItsFile(string cmake)
    {
        ArgumentNullException.ThrowIfNull(cmake);

        string code = CMakeCommentRegex().Replace(cmake, "");

        int open = code.IndexOf(AllocatorCondition, StringComparison.Ordinal);
        if (open < 0)
            return false;

        int close = code.IndexOf("endif()", open, StringComparison.Ordinal);
        if (close < 0)
            return false;

        bool found = false;

        foreach (string flag in (string[])["-Wl,--wrap=", "-fno-builtin-"])
        {
            int at = code.IndexOf(flag, StringComparison.Ordinal);
            while (at >= 0)
            {
                if (at < open || at > close)
                    return false;

                found = true;
                at = code.IndexOf(flag, at + 1, StringComparison.Ordinal);
            }
        }

        // An empty list inside the condition would satisfy every check above and wrap nothing, which
        // is PP44's counter reading zero for every allocation.
        return found;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?m)^[ \t]*#.*$")]
    private static partial System.Text.RegularExpressions.Regex CMakeCommentRegex();
}
