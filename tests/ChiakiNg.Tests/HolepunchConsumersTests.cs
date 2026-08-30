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
