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
public static class SuiteEntryPoint
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

    /// <summary>A suite that stays, which is what makes "all four are guarded" mean something.</summary>
    public const string StaysUnguarded = "tests_takion";

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
    {
        ArgumentNullException.ThrowIfNull(mainSource);

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
                if (opened == 0 && trimmed.Contains(Guard, StringComparison.Ordinal))
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

            foreach (string symbol in Guarded.Append(StaysUnguarded))
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
    public static string? DefinedAs(string cmake)
    {
        ArgumentNullException.ThrowIfNull(cmake);

        int at = cmake.IndexOf(Guard + "=", StringComparison.Ordinal);
        if (at < 0)
            return null;

        int from = at + Guard.Length + 1;
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
    /// </summary>
    public static bool TheSwitchAgreesWithTheList(string cmake)
    {
        ArgumentNullException.ThrowIfNull(cmake);

        bool listed = FramePathConsumers.ShapeOf(ConsumerKind.Suite, cmake) == ConsumerShape.Asking;
        string? defined = DefinedAs(cmake);

        return listed ? defined == "1" : defined is null or "0";
    }
}
