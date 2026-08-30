using ChiakiNg.Protocol;
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
    /// All three are named rather than counted - "three" is a number, and a deletion needs which.
    /// </summary>
    [Fact]
    public void TheDeletionHasThreeNamedConsumers()
    {
        Assert.Equal(
            [@"lib\src\session.c", @"lib\src\remote\holepunch-test.c", @"shim\chiaki_shim.c"],
            HolepunchConsumers.All);

        // session.c is PP340's seam and keeps its own model; the other two are held here.
        Assert.Contains(HolepunchConsumers.TestHarnessRelativePath, HolepunchConsumers.All);
        Assert.Contains(HolepunchConsumers.ShimRelativePath, HolepunchConsumers.All);
    }

    /// <summary>A shim that stopped wrapping one is caught, which is how the list stays true.</summary>
    [Fact]
    public void AShimMissingAWrapperIsCaught()
    {
        IReadOnlyList<string> missing = HolepunchConsumers.MissingFromShim("int nothing(void) { return 0; }");

        Assert.Equal(HolepunchConsumers.ShimCalls.Count, missing.Count);
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
