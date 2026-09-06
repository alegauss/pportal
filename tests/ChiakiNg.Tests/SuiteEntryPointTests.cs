using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP760, under PP696: the C suite's entry point taught both shapes.
///
/// PP758's trial ran the managed suite against an after-shape tree and found it green. It never
/// rebuilt the C, and test/main.c externs all four frame-path suites and lists all four - so the
/// commit that drops those files from the build would have failed to link, in the one commit that
/// may not edit a test file to fix it.
///
/// A NO-OP IN THE TREE IT LANDS ON, which is the point: the same sources, the same cases, the same
/// count. What changes is that the next commit edits a build list and nothing else.
/// </summary>
public class SuiteEntryPointTests(ITestOutputHelper output)
{
    private static string? EntryPointSource()
        => SuiteEntryPoint.LocateMain() is { } path ? File.ReadAllText(path) : null;

    private static string? List()
        => SuiteEntryPoint.LocateList() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE FOUR ARE BEHIND THE GUARD AND A FIFTH IS NOT.
    ///
    /// PP271 as this check's own shape: a reader that called everything guarded would agree with any
    /// list, so a suite that stays is asserted to be outside. tests_takion is one - takion.c is not
    /// one of the four files and has no reason to move.
    /// </summary>
    [Fact]
    public void TheFourFramePathSuitesAreGuardedAndTheOthersAreNot()
    {
        if (EntryPointSource() is not { } main)
            return;

        IReadOnlyList<string> guarded = SuiteEntryPoint.GuardedIn(main);
        output.WriteLine($"guarded: {string.Join(", ", guarded)}");

        Assert.Equal([.. SuiteEntryPoint.Guarded.Order(StringComparer.Ordinal)], guarded);
        Assert.DoesNotContain(SuiteEntryPoint.StaysUnguarded, guarded);
    }

    /// <summary>
    /// The four symbols are the four files', derived from the census rather than typed again.
    ///
    /// A second list of what leaves is a second place to forget one, and the failure of forgetting
    /// is an extern with no definition - which is the whole thing this task exists to prevent.
    /// </summary>
    [Fact]
    public void TheGuardedSymbolsAreTheFourFilesOwn()
    {
        Assert.Equal(FramePathConsumers.Suite.Count, SuiteEntryPoint.Guarded.Count);

        foreach (ConsumedTestFile file in FramePathConsumers.Suite)
            Assert.Contains(SuiteEntryPoint.SymbolFor(file.File), SuiteEntryPoint.Guarded);

        // And a file that is not one of the four has no symbol here to give.
        Assert.Throws<ArgumentOutOfRangeException>(() => SuiteEntryPoint.SymbolFor("takion.c"));
    }

    /// <summary>
    /// THE SWITCH AND THE SOURCE LIST AGREE, which is the pairing that must never drift.
    ///
    /// Two failures live here and they are opposite. A list that dropped the four files with the
    /// switch left on compiles four suites[] entries against nothing. A switch turned off while the
    /// files are still compiled quietly takes 72 munit cases out of every run - the exact failure
    /// the floor file was written for, arriving through the file that was meant to prevent it.
    /// </summary>
    [Fact]
    public void TheSwitchAndTheListAgree()
    {
        if (List() is not { } cmake)
            return;

        output.WriteLine($"{SuiteEntryPoint.Guard} = {SuiteEntryPoint.DefinedAs(cmake) ?? "(undefined)"}");

        Assert.True(
            SuiteEntryPoint.TheSwitchAgreesWithTheList(cmake),
            "the four files and the switch that compiles their suites disagree");
    }

    /// <summary>
    /// The readers themselves, on text rather than on whichever tree this runs against.
    ///
    /// Only one shape exists here to be exercised, and it is the one that already worked - so both
    /// are asked directly, which is what makes the other branch tested rather than written.
    /// </summary>
    [Fact]
    public void TheReaderTellsGuardedFromUnguarded()
    {
        const string Guarded = """
            extern MunitTest tests_takion[];
            #if CHIAKI_UNIT_HAVE_FRAMEPATH
            extern MunitTest tests_fec[];
            extern MunitTest tests_frame_processor[];
            extern MunitTest tests_alloc_budget[];
            extern MunitTest tests_video_receiver[];
            #endif
            """;

        Assert.Equal(
            ["tests_alloc_budget", "tests_fec", "tests_frame_processor", "tests_video_receiver"],
            SuiteEntryPoint.GuardedIn(Guarded));

        // A name inside the block AND outside it is not guarded: the outside mention is the one that
        // fails to link, and reporting it as guarded is the mistake that ships.
        const string Both = """
            extern MunitTest tests_fec[];
            #if CHIAKI_UNIT_HAVE_FRAMEPATH
            extern MunitTest tests_fec[];
            #endif
            """;

        Assert.Empty(SuiteEntryPoint.GuardedIn(Both));

        // A nested conditional does not close the block early.
        const string Nested = """
            #if CHIAKI_UNIT_HAVE_FRAMEPATH
            #if SOMETHING_ELSE
            #endif
            extern MunitTest tests_fec[];
            #endif
            extern MunitTest tests_takion[];
            """;

        Assert.Equal(["tests_fec"], SuiteEntryPoint.GuardedIn(Nested));

        // And a different guard is not this one.
        Assert.Empty(SuiteEntryPoint.GuardedIn(
            "#if CHIAKI_LIB_ENABLE_FFMPEG_DECODER\nextern MunitTest tests_fec[];\n#endif"));

        Assert.Empty(SuiteEntryPoint.GuardedIn(""));
    }

    /// <summary>And the switch reader tells a value from an absence, and one shape from the other.</summary>
    [Fact]
    public void TheSwitchReaderTellsOnFromOffFromAbsent()
    {
        Assert.Equal("1", SuiteEntryPoint.DefinedAs("target_compile_definitions(chiaki-unit PRIVATE CHIAKI_UNIT_HAVE_FRAMEPATH=1)"));
        Assert.Equal("0", SuiteEntryPoint.DefinedAs("CHIAKI_UNIT_HAVE_FRAMEPATH=0\n"));
        Assert.Null(SuiteEntryPoint.DefinedAs(""));

        const string Stays = "main.c\n\thttp.c\n\ttakion.c";

        string asking = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays}\n\tfec.c\n\tframeprocessor.c"
            + "\n\tallocbudget.c\n\tvideoreceiver.c)\nCHIAKI_UNIT_HAVE_FRAMEPATH=1";
        Assert.True(SuiteEntryPoint.TheSwitchAgreesWithTheList(asking));

        // The four gone and the switch still on, which is the link failure this prevents.
        string mismatched = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays})\nCHIAKI_UNIT_HAVE_FRAMEPATH=1";
        Assert.False(SuiteEntryPoint.TheSwitchAgreesWithTheList(mismatched));

        // The four still there and the switch off, which is 72 cases silently not run.
        string silenced = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays}\n\tfec.c\n\tframeprocessor.c"
            + "\n\tallocbudget.c\n\tvideoreceiver.c)\nCHIAKI_UNIT_HAVE_FRAMEPATH=0";
        Assert.False(SuiteEntryPoint.TheSwitchAgreesWithTheList(silenced));

        // And both off together, which is the shape PP696 leaves.
        Assert.True(SuiteEntryPoint.TheSwitchAgreesWithTheList($"set(CHIAKI_UNIT_SOURCES\n\t{Stays})"));
    }
}
