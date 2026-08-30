using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP544: the second consumer of libchiaki's holepunch, which PP33 recorded as not existing.
///
/// The interesting failure here is a PASS that should not be: if holepunch-test.c is deleted or
/// stops linking the library, PP544's question is settled by removal, and these say so rather than
/// quietly going green on a tree where the file is gone again.
/// </summary>
public class HolepunchConsumersTests
{
    /// <summary>
    /// PP563: THE THIRD CONSUMER, which this port wrote itself.
    ///
    /// PP481 wrapped nine holepunch exports in the shim so the managed side could drive the C. That
    /// was deliberate and is not reopened here. What was not written down is the consequence: the
    /// port's own seam is a caller of the file PP33 exists to delete, and it arrived from a task the
    /// roadmap lists among PP33's satisfied deps.
    /// </summary>
    [Fact]
    public void TheShimIsTheThirdConsumer()
    {
        if (HolepunchConsumers.LocateShim() is not { } path)
            return;

        Assert.Empty(HolepunchConsumers.MissingFromShim(File.ReadAllText(path)));
        Assert.Equal(9, HolepunchConsumers.ShimCalls.Count);
    }

    /// <summary>
    /// All four are named rather than counted - a deletion needs which, not how many.
    ///
    /// PP564: it was three until a linker was asked. Building chiaki-lib without holepunch.c named
    /// ctrl.c in thirty seconds, after PP563 had read the tree and concluded three.
    /// </summary>
    [Fact]
    public void TheDeletionHasFourNamedConsumers()
    {
        Assert.Equal(
            [
                @"lib\src\session.c",
                @"lib\src\ctrl.c",
                @"lib\src\remote\holepunch-test.c",
                @"shim\chiaki_shim.c",
            ],
            HolepunchConsumers.All);
    }

    /// <summary>
    /// PP564: ctrl.c asks for the control port and already has an answer for not getting one.
    ///
    /// That fallback is why it is both the cheapest of the four to remove and the easiest to miss:
    /// the file reads as though it does not depend on the holepunch at all.
    /// </summary>
    [Fact]
    public void CtrlAsksForThePortAndHasAFallback()
    {
        if (HolepunchConsumers.LocateCtrl() is not { } path)
            return;

        Assert.True(HolepunchConsumers.CtrlStillAsksWithAFallback(File.ReadAllText(path)));
    }

    /// <summary>
    /// PP564: and one export carries no chiaki_ prefix, so a prefix sweep misses it.
    ///
    /// session.c calls holepunch_session_create_offer, which is CHIAKI_EXPORT all the same. Every
    /// reader that finds these by their prefix - which is how they are found - walks past it.
    /// </summary>
    [Fact]
    public void OneExportCarriesNoPrefix()
    {
        Assert.DoesNotContain("chiaki_", HolepunchConsumers.UnprefixedExport, StringComparison.Ordinal);

        if (HolepunchConsumers.LocateHarness() is null)
            return;

        string header = Path.Combine(
            Path.GetDirectoryName(HolepunchConsumers.LocateHarness()!)!, "..", "..",
            "include", "chiaki", "remote", "holepunch.h");

        if (File.Exists(header))
        {
            Assert.Contains(
                "CHIAKI_EXPORT ChiakiErrorCode " + HolepunchConsumers.UnprefixedExport,
                File.ReadAllText(header), StringComparison.Ordinal);
        }
    }

    /// <summary>A shim that stopped wrapping one is caught, which is how the list stays true.</summary>
    [Fact]
    public void AShimMissingAWrapperIsCaught()
    {
        IReadOnlyList<string> missing = HolepunchConsumers.MissingFromShim("int nothing(void) { return 0; }");

        Assert.Equal(HolepunchConsumers.ShimCalls.Count, missing.Count);
    }

    /// <summary>
    /// PP565: the tree the curl-and-json-c measurement was taken on is the tree in front of us.
    ///
    /// The measurement itself needs a build with three lines commented out, so it cannot live in a
    /// test. What can is its precondition: holepunch.c still in the library's sources, and both
    /// libraries still linked. Change any of the three and the recorded result - that libchiaki.a
    /// builds with neither, once that one file is gone - is about a different tree.
    ///
    /// PP566: AND PP33'S BUILD CRITERION NOW CITES IT. That criterion said the build "configures
    /// and links without either" as one step; it is two, and the compile half is already true. Its
    /// reason names this measurement, so this assertion is what keeps the reason honest - a tree
    /// where these three lines moved is one the criterion is describing wrongly.
    /// </summary>
    [Fact]
    public void TheMeasurementsPreconditionStillHolds()
    {
        if (HolepunchConsumers.LocateLibCMake() is not { } path)
            return;

        Assert.True(HolepunchConsumers.TheMeasuredTreeIsStillThis(File.ReadAllText(path)));
        Assert.Equal(@"lib\src\remote\holepunch.c", HolepunchConsumers.OnlyFileNeedingCurlAndJsonC);
    }

    /// <summary>
    /// And the file the two libraries are for is the file the deletion is about - one claim, not
    /// two, which is what makes PP33's DoD line reachable by removing a single source.
    /// </summary>
    [Fact]
    public void TheFileTheyAreForIsTheFileBeingDeleted()
        => Assert.Contains(
            HolepunchConsumers.OnlyFileNeedingCurlAndJsonC,
            HolepunchConsumers.TestHarnessRelativePath.Replace(
                "holepunch-test.c", "holepunch.c", StringComparison.Ordinal));

    /// <summary>
    /// PP573: PP33'S OWN LINE AGREES ON THE COUNT, and for a long time it did not.
    ///
    /// Its reason read "session.c is its only caller" while PP544 found the harness, PP563 the shim
    /// and PP564 - by asking a linker - ctrl.c. Three shipped tasks each falsified it and none
    /// changed it, because a task's models are where a finding lands and its LINE is somewhere else.
    ///
    /// It is the first sentence a session picking the ready line reads, so the deletion's scope was
    /// wrong exactly where somebody decides what it costs. PP501 fixed the same shape on PP27's line.
    /// </summary>
    [Fact]
    public void ThePP33LineAgreesWithThisList()
    {
        string? path = SanitizerSource.LocateRelative(@"docs\ROADMAP.md");
        if (path is null)
            return;

        string? line = File.ReadLines(path)
            .FirstOrDefault(one => one.Contains("**PP33**", StringComparison.Ordinal));

        Assert.NotNull(line);
        Assert.True(
            HolepunchConsumers.TheRoadmapLineAgreesOnTheCount(line),
            $"PP33's line does not name four callers: {line}");

        Assert.Equal(4, HolepunchConsumers.All.Count);
    }

    /// <summary>And the old claim is what the check refuses, not merely an absent phrase.</summary>
    [Fact]
    public void TheOldOnlyCallerClaimIsRefused()
    {
        Assert.False(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount(
            "the deletion: session.c is its only caller."));

        Assert.False(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount("says nothing about callers"));

        Assert.True(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount(
            "and four files call it - session.c, ctrl.c, the harness, the shim."));
    }

    /// <summary>The harness exists, which is the whole of what PP33 got wrong.</summary>
    [Fact]
    public void TheHarnessIsInTheTree()
    {
        if (HolepunchConsumers.LocateHarness() is not { } path)
            return;

        Assert.True(File.Exists(path), $"{HolepunchConsumers.TestHarnessRelativePath} is not there");
    }

    /// <summary>
    /// And it calls all eight, so the deletion has that many call sites to answer for beyond
    /// session.c's nine.
    /// </summary>
    [Fact]
    public void TheHarnessStillCallsAllEightExports()
    {
        if (HolepunchConsumers.LocateHarness() is not { } path)
            return;

        var missing = HolepunchConsumers.MissingFromHarness(File.ReadAllText(path));

        Assert.True(missing.Count == 0,
            "the harness no longer calls: " + string.Join(", ", missing));
    }

    /// <summary>
    /// And it is a real target, linked against the library. A harness nothing builds would be a
    /// different and smaller problem than one every build produces.
    /// </summary>
    [Fact]
    public void TheTargetIsDeclaredAndLinksTheLibrary()
    {
        if (HolepunchConsumers.LocateLibCMake() is not { } path)
            return;

        Assert.True(HolepunchConsumers.TargetStillLinksTheLibrary(File.ReadAllText(path)),
            "holepunch-test is no longer declared as an executable linking chiaki-lib");
    }

    /// <summary>
    /// And the reader can see a target that stopped linking. Written out because the check above
    /// is two conditions and a rule that only tested the first would call a dangling declaration a
    /// consumer.
    /// </summary>
    [Fact]
    public void ADeclarationWithoutTheLinkIsNotAConsumer()
    {
        const string declaredOnly = """
            if(CHIAKI_ENABLE_TESTS)
                add_executable(holepunch-test include/chiaki/remote/holepunch.h src/remote/holepunch-test.c)
            endif()
            """;

        Assert.False(HolepunchConsumers.TargetStillLinksTheLibrary(declaredOnly));
    }
}
