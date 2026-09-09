using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP786: the C suite's second switch, and the arithmetic the two of them owe the floor.
///
/// PP780 asked the linker what PP27's end state costs and named three test files among the eight
/// consumers: test/takion.c, test/reorderqueue.c and test/allocbudget.c exercise the modules
/// directly. They leave with the C they test, and nothing in the tree was arranged for it - so the
/// deletion would have taken nine munit cases out of the run, and test.cmd's floor would have gone
/// red in the one commit that is not allowed to edit a test file.
///
/// PP760 SOLVED THIS SHAPE ONCE, one flip over, and what is new here is the overlap. allocbudget.c
/// is claimed by both deletions, so it leaves on whichever lands first: its entry in main.c has to
/// sit inside both guards, neither switch may be decided by it, and the two case counts may not
/// simply be added.
/// </summary>
public class TakionSuiteEntryPointTests(ITestOutputHelper output)
{
    private static string? EntryPointSource()
        => SuiteEntryPoint.LocateMain() is { } path ? File.ReadAllText(path) : null;

    private static string? List()
        => SuiteEntryPoint.LocateList() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// PP786: main.c has all three of takion's suites behind the guard, and the control outside it.
    ///
    /// Both halves. A guard around nothing compiles today and fails on the day it matters, and a
    /// guard around everything would take http's cases out of a run that still has http.c.
    /// </summary>
    [Fact]
    public void TheThreeAreGuardedAndTheControlIsNot()
    {
        if (EntryPointSource() is not { } main)
            return;

        IReadOnlyList<string> guarded = TakionSuiteEntryPoint.GuardedIn(main);
        output.WriteLine($"behind {TakionSuiteEntryPoint.Guard}: {string.Join(", ", guarded)}");

        Assert.Equal([.. TakionSuiteEntryPoint.Guarded.Order(StringComparer.Ordinal)], guarded);
        Assert.DoesNotContain(TakionSuiteEntryPoint.StaysUnguarded, guarded);
    }

    /// <summary>
    /// PP786: and the frame path's four are still behind theirs, with the shared one behind both.
    ///
    /// The nesting is the claim. tests_alloc_budget answers yes to both questions because its entry
    /// is inside both blocks, and a reader that stopped at the first <c>#endif</c> would report one
    /// of the two as unguarded - which is the extern with no definition, one deletion later.
    /// </summary>
    [Fact]
    public void TheSharedSuiteIsBehindBothGuards()
    {
        if (EntryPointSource() is not { } main)
            return;

        string shared = TakionSuiteEntryPoint.SymbolFor(TakionSuiteEntryPoint.SharedWithTheFramePath);

        Assert.Contains(shared, TakionSuiteEntryPoint.GuardedIn(main));
        Assert.Contains(shared, SuiteEntryPoint.GuardedIn(main));
    }

    /// <summary>PP786: the build defines the switch, and it agrees with the list it sits beside.</summary>
    [Fact]
    public void TheSwitchAgreesWithTheList()
    {
        if (List() is not { } cmake)
            return;

        output.WriteLine(
            $"{TakionSuiteEntryPoint.Guard} = {TakionSuiteEntryPoint.DefinedAs(cmake) ?? "(undefined)"}");

        Assert.True(
            TakionSuiteEntryPoint.TheSwitchAgreesWithTheList(cmake),
            "the switch and takion's two test files disagree about which shape this tree is in");

        Assert.True(
            SuiteEntryPoint.TheSwitchAgreesWithTheList(cmake),
            "and the frame path's switch no longer agrees with its own three");
    }

    /// <summary>
    /// PP786: the allocator wrap follows allocbudget.c, which is the link failure both flips had.
    ///
    /// Found by BUILDING the bare shape rather than by reading it. Taking the three files out and
    /// turning the switch off produced a link error naming every allocation site in libchiaki, munit
    /// and jerasure - <c>__wrap_malloc</c> undefined - because <c>-Wl,--wrap=malloc</c> was
    /// unconditional and the only definition of that symbol is in the file both deletions remove.
    ///
    /// The bare shape links now, runs 140 of the 149 cases, and those nine are exactly
    /// <see cref="CSuiteFloor.TakionCases"/>.
    /// </summary>
    [Fact]
    public void TheAllocatorWrapFollowsTheFileThatDefinesIt()
    {
        // The reader tells the two shapes apart before it is pointed at the tree.
        Assert.False(SuiteEntryPoint.TheAllocatorWrapFollowsItsFile(
            "target_link_options(chiaki-unit PRIVATE -Wl,--wrap=malloc)"));

        Assert.True(SuiteEntryPoint.TheAllocatorWrapFollowsItsFile(
            $"{SuiteEntryPoint.AllocatorCondition} CHIAKI_UNIT_SOURCES)\n"
            + "\ttarget_link_options(chiaki-unit PRIVATE -Wl,--wrap=malloc)\n"
            + "\ttarget_compile_options(chiaki-unit PRIVATE -fno-builtin-malloc)\nendif()"));

        // A condition wrapping nothing is not the tie: PP44's counter would read zero for every
        // allocation, which is a green measuring the absence of its own instrument.
        Assert.False(SuiteEntryPoint.TheAllocatorWrapFollowsItsFile(
            $"{SuiteEntryPoint.AllocatorCondition} CHIAKI_UNIT_SOURCES)\nendif()"));

        // And one of the two flags left outside is the same link failure, half of it.
        Assert.False(SuiteEntryPoint.TheAllocatorWrapFollowsItsFile(
            "target_compile_options(chiaki-unit PRIVATE -fno-builtin-malloc)\n"
            + $"{SuiteEntryPoint.AllocatorCondition} CHIAKI_UNIT_SOURCES)\n"
            + "\ttarget_link_options(chiaki-unit PRIVATE -Wl,--wrap=malloc)\nendif()"));

        if (List() is not { } cmake)
            return;

        Assert.True(
            SuiteEntryPoint.TheAllocatorWrapFollowsItsFile(cmake),
            "the allocator wrap does not follow allocbudget.c, so whichever deletion lands first "
                + "leaves __wrap_malloc undefined for every allocation in the tree");
    }

    /// <summary>
    /// PP786: the three are derived from the linker's census rather than typed here.
    ///
    /// PP780 measured them. A second list would be a second place to forget a file, and the file
    /// that would be forgotten is the one nobody expects to be a takion consumer at all.
    /// </summary>
    [Fact]
    public void TheGuardedThreeComeFromTheLinkersCensus()
    {
        IReadOnlyList<string> census =
            [.. TakionConsumers.Consumers
                .Where(one => one.Kind == TakionConsumerKind.Suite)
                .Select(one => Path.GetFileName(one.File))
                .Order(StringComparer.Ordinal)];

        Assert.Equal(census, TakionSuiteEntryPoint.Files);
        Assert.Equal(3, TakionSuiteEntryPoint.Files.Count);

        foreach (string file in census)
            Assert.Contains(TakionSuiteEntryPoint.SymbolFor(file), TakionSuiteEntryPoint.Guarded);

        Assert.Throws<ArgumentOutOfRangeException>(() => TakionSuiteEntryPoint.SymbolFor("fec.c"));
    }

    /// <summary>
    /// PP786: the shared file is in both censuses and decides neither switch.
    ///
    /// Stated as an assertion rather than as a comment because it is the whole reason each side has
    /// an OwnFiles: a switch decided by a file that leaves for the other flip's reason answers about
    /// the wrong deletion.
    /// </summary>
    [Fact]
    public void TheSharedFileDecidesNeitherSwitch()
    {
        Assert.Contains(TakionSuiteEntryPoint.SharedWithTheFramePath, TakionSuiteEntryPoint.Files);
        Assert.Contains(
            TakionSuiteEntryPoint.SharedWithTheFramePath,
            FramePathConsumers.Suite.Select(one => one.File));

        Assert.DoesNotContain(
            TakionSuiteEntryPoint.SharedWithTheFramePath, TakionSuiteEntryPoint.OwnFiles);
        Assert.DoesNotContain(TakionSuiteEntryPoint.SharedWithTheFramePath, SuiteEntryPoint.OwnFiles);

        Assert.Equal(SuiteEntryPoint.SharedWithTakion, TakionSuiteEntryPoint.SharedWithTheFramePath);

        // Two files each, which is what is left of three and four once the shared one is out.
        Assert.Equal(2, TakionSuiteEntryPoint.OwnFiles.Count);
        Assert.Equal(3, SuiteEntryPoint.OwnFiles.Count);
    }

    /// <summary>
    /// PP786: ONE FLIP DOES NOT MAKE THE OTHER LOOK HALF-FINISHED.
    ///
    /// The check that would have failed before this line. Take takion's two out of the list and turn
    /// its switch off, and the frame path's switch is still on with its own three still there - a
    /// tree that is correct and that a question over all four files would have called Partial.
    /// </summary>
    [Fact]
    public void EitherFlipLeavesTheOtherSwitchAgreeing()
    {
        const string Stays = "main.c\n\thttp.c\n\trpcrypt.c";
        const string Frame = "fec.c\n\tframeprocessor.c\n\tvideoreceiver.c";
        const string Takion = "takion.c\n\treorderqueue.c";
        const string Shared = "allocbudget.c";

        string both = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays}\n\t{Frame}\n\t{Takion}\n\t{Shared})\n"
            + "CHIAKI_UNIT_HAVE_FRAMEPATH=1\nCHIAKI_UNIT_HAVE_TAKION=1";

        Assert.True(SuiteEntryPoint.TheSwitchAgreesWithTheList(both));
        Assert.True(TakionSuiteEntryPoint.TheSwitchAgreesWithTheList(both));

        // Takion's deletion first: its two and the shared one are gone, its switch is off, and the
        // frame path's stays on because its own three are untouched.
        string takionGone = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays}\n\t{Frame})\n"
            + "CHIAKI_UNIT_HAVE_FRAMEPATH=1";

        Assert.True(SuiteEntryPoint.TheSwitchAgreesWithTheList(takionGone));
        Assert.True(TakionSuiteEntryPoint.TheSwitchAgreesWithTheList(takionGone));

        // And the frame path's first, which is the same claim the other way round.
        string frameGone = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays}\n\t{Takion})\n"
            + "CHIAKI_UNIT_HAVE_TAKION=1";

        Assert.True(SuiteEntryPoint.TheSwitchAgreesWithTheList(frameGone));
        Assert.True(TakionSuiteEntryPoint.TheSwitchAgreesWithTheList(frameGone));

        // The two failures each switch exists to catch, on its own files.
        string leftOn = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays}\n\t{Frame})\n"
            + "CHIAKI_UNIT_HAVE_FRAMEPATH=1\nCHIAKI_UNIT_HAVE_TAKION=1";
        Assert.False(TakionSuiteEntryPoint.TheSwitchAgreesWithTheList(leftOn));

        string silenced = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays}\n\t{Frame}\n\t{Takion}\n\t{Shared})\n"
            + "CHIAKI_UNIT_HAVE_FRAMEPATH=1\nCHIAKI_UNIT_HAVE_TAKION=0";
        Assert.False(TakionSuiteEntryPoint.TheSwitchAgreesWithTheList(silenced));
    }

    /// <summary>
    /// PP786: the floor's bound moves with both flips, and subtracts the overlap exactly once.
    ///
    /// The bound has one job - stay below the suite it guards, on every shape the tree can be in -
    /// and a bound that double-counted allocbudget's three would sit three lower than the honest
    /// one. Asserted as the relation rather than as four numbers, so the day a case is added the
    /// claim is still the claim.
    /// </summary>
    [Fact]
    public void TheFloorsBoundSubtractsTheOverlapOnce()
    {
        Assert.Equal(
            CSuiteFloor.PlausibleMinimum - CSuiteFloor.FramePathCases - CSuiteFloor.TakionCases
                + CSuiteFloor.SharedWithTheFramePathCases,
            CSuiteFloor.MinimumFor(ConsumerShape.Silent, ConsumerShape.Silent));

        Assert.Equal(
            CSuiteFloor.PlausibleMinimum - CSuiteFloor.TakionCases,
            CSuiteFloor.MinimumFor(ConsumerShape.Asking, ConsumerShape.Silent));

        // The one-argument form is the same question with takion's flip not yet made, which is what
        // every caller written before this line meant by it.
        Assert.Equal(
            CSuiteFloor.MinimumFor(ConsumerShape.Silent),
            CSuiteFloor.MinimumFor(ConsumerShape.Silent, ConsumerShape.Asking));

        if (CSuiteFloor.LocateFloor() is not { } path)
            return;

        int? recorded = CSuiteFloor.Read(File.ReadAllText(path));
        Assert.NotNull(recorded);

        int floor = recorded.Value;
        int union = CSuiteFloor.FramePathCases + CSuiteFloor.TakionCases
            - CSuiteFloor.SharedWithTheFramePathCases;

        // PP27: THE RECORDED FLOOR IS THE AFTER-BOTH NUMBER NOW, so the ladder collapses to its last
        // rung. This subtracted from the floor to model each shape the tree could still be put in,
        // and both flips landed in one commit - so the tree is in exactly one of the four, and
        // pretending otherwise would compare 71 minus 78 against a bound and call it a finding.
        //
        // What the arithmetic above still holds is the part that was never about today's tree: the
        // overlap is subtracted once, whichever shape is asked about.
        output.WriteLine(
            $"floor {floor}; the two flips cost {union} together, "
            + $"and the bound for the shape the tree is in is "
            + $"{CSuiteFloor.MinimumFor(ConsumerShape.Silent, ConsumerShape.Silent)}");

        Assert.True(
            floor > CSuiteFloor.MinimumFor(ConsumerShape.Silent, ConsumerShape.Silent),
            "the bound is not below what the suite still runs now that both deletions have landed");

        // And the recorded number is the one the flips actually cost, which is the join the file's
        // own prose made in advance: PP786 wrote "71 after both" from reading the suites, and this
        // is that prediction held against the number the suite reports.
        Assert.Equal(CSuiteFloor.BeforeTheFlips - union, floor);
    }
}
