using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>
/// PP786: the C suite's other switch, for the three test files takion's deletion takes.
///
/// PP780 asked the linker what PP27's end state costs and named eight consumers. Three of them are
/// the suite's: test/takion.c holds seven of the twenty-four symbols, test/reorderqueue.c six, and
/// test/allocbudget.c one. They go with the modules they test - a suite testing code that is gone is
/// a suite about nothing - and nothing in the tree was arranged for that.
///
/// PP760 DID THIS ONCE ALREADY, one flip over. <see cref="SuiteEntryPoint"/> put the frame path's
/// four suites behind CHIAKI_UNIT_HAVE_FRAMEPATH so the entry point knows both shapes before the
/// commit that changes which one it is - because that commit may not edit a test file, and an extern
/// with no definition is a link failure before a single case runs.
///
/// WHAT IS NEW IS THE OVERLAP. allocbudget.c is claimed by BOTH flips: it links frameprocessor.c and
/// videoreceiver.c, and it calls chiaki_takion_v9_av_packet_parse and takion_handle_packet_av. So it
/// leaves on whichever deletion lands first, its entry in main.c sits inside both guards, and
/// neither switch may be decided by it - which is why each side names its OWN files.
///
/// AND THE FLOOR MOVES BY A MEASURED NUMBER. <see cref="CSuiteFloor.TakionCases"/> is nine, three of
/// which the frame path already counts, so the two removals overlap and adding them would lower the
/// floor by three more than the suite actually loses.
/// </summary>
public static class TakionSuiteEntryPoint
{
    /// <summary>The switch main.c reads for takion's suites.</summary>
    public const string Guard = "CHIAKI_UNIT_HAVE_TAKION";

    /// <summary>The one file both flips claim, named rather than inferred from two lists.</summary>
    public const string SharedWithTheFramePath = "allocbudget.c";

    /// <summary>
    /// The three C test files that leave, by leaf name.
    ///
    /// Derived from <see cref="TakionConsumers"/> rather than typed here: that census came off the
    /// linker, and a second list would be a second place to forget a file.
    /// </summary>
    public static IReadOnlyList<string> Files { get; } =
        [.. TakionConsumers.Consumers
            .Where(one => one.Kind == TakionConsumerKind.Suite)
            .Select(one => Path.GetFileName(one.File))
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// The two this flip alone owns, which are what decide whether its switch should be on.
    ///
    /// The shared file is excluded for the reason <see cref="SuiteEntryPoint.OwnFiles"/> excludes it
    /// from the other side: it is gone on either flip, so its absence says nothing about which one
    /// happened, and reading it as this flip's evidence would make each switch answer for the other.
    /// </summary>
    public static IReadOnlyList<string> OwnFiles { get; } =
        [.. Files.Where(one => one != SharedWithTheFramePath)];

    /// <summary>The three suites that go behind the guard, by the symbol main.c externs.</summary>
    public static IReadOnlyList<string> Guarded { get; } = [.. Files.Select(SymbolFor)];

    /// <summary>The munit symbol a C test file defines, which main.c externs.</summary>
    public static string SymbolFor(string file) => file switch
    {
        "takion.c" => "tests_takion",
        "reorderqueue.c" => "tests_reorder_queue",
        "allocbudget.c" => "tests_alloc_budget",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "not one of the three that leave"),
    };

    /// <summary>
    /// A suite that stays, which is what makes "all three are guarded" mean something.
    ///
    /// http.c is claimed by neither flip. The frame path's control used to be tests_takion and could
    /// not stay one: a symbol behind the OTHER guard is not evidence about this one.
    /// </summary>
    public const string StaysUnguarded = "tests_http";

    /// <summary>Whatever of the three main.c has behind this guard, and nothing that is also outside it.</summary>
    public static IReadOnlyList<string> GuardedIn(string mainSource)
        => SuiteEntryPoint.GuardedIn(mainSource, Guard, [.. Guarded, StaysUnguarded]);

    /// <summary>What the build defines this switch as, or null where it defines it not at all.</summary>
    public static string? DefinedAs(string cmake) => SuiteEntryPoint.DefinedAs(cmake, Guard);

    /// <summary>Whether this switch and the source list agree, over the two files it alone owns.</summary>
    public static bool TheSwitchAgreesWithTheList(string cmake)
        => SuiteEntryPoint.SwitchAgrees(cmake, Guard, OwnFiles);
}
