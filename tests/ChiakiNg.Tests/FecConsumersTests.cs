using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP574: the FEC decode's callers, which PP30's line counted as one.
/// </summary>
public class FecConsumersTests
{
    /// <summary>
    /// THREE, AND THE SHIM IS THE THIRD. PP289 shipped saying frameprocessor.c is the only caller
    /// and it was true then; the shim gained a wrapper and nothing re-read the sentence.
    ///
    /// The same fact PP563 found for holepunch and PP573 corrected on PP33's line - the shim wraps
    /// 130 entry points across every module, so it is a consumer of everything a deletion removes.
    /// </summary>
    [Fact]
    public void AllThreeStillCallIt()
    {
        foreach (string relative in FecConsumers.All)
        {
            if (FecConsumers.Locate(relative) is not { } path)
                continue;

            Assert.True(
                FecConsumers.Calls(File.ReadAllText(path)),
                $"{relative} no longer calls {FecConsumers.Export}");
        }

        Assert.Equal(3, FecConsumers.All.Count);
        Assert.Contains(@"shim\chiaki_shim.c", FecConsumers.All);
    }

    /// <summary>
    /// The files that DECLARE it are not callers, which is why a plain sweep overcounts: fec.h
    /// declares and fec.c defines, and neither depends on the export.
    /// </summary>
    [Fact]
    public void TheDeclaringFilesAreNotCallers()
    {
        Assert.Equal(2, FecConsumers.Declares.Count);

        foreach (string relative in FecConsumers.Declares)
            Assert.DoesNotContain(relative, FecConsumers.All);
    }

    /// <summary>
    /// PP30's line agrees with the list, and the old claim is refused by name - a line that merely
    /// stopped mentioning callers would otherwise pass.
    ///
    /// PP30 HAS SHIPPED AND THE LINE IS GONE, so the reader is exercised on text and the live
    /// subject is absent. That is the same exit DeletionCallerClaims' rows took when PP33 and PP295
    /// shipped: a shipped claim sits in the ledger, and the harm this check exists to catch -
    /// understating a deletion's cost while somebody is deciding whether to do it - is not
    /// available to a line nobody is deciding about.
    ///
    /// The absence is ASSERTED rather than tolerated. Read as "if the line is there, it must agree"
    /// this would also be green on a roadmap where the line came back saying one caller.
    /// </summary>
    [Fact]
    public void ThePP30LineAgreesWithTheList()
    {
        Assert.False(FecConsumers.TheRoadmapLineAgreesOnTheCount(
            "chiaki_fec_decode has one caller left, frameprocessor.c"));
        Assert.False(FecConsumers.TheRoadmapLineAgreesOnTheCount("says nothing about callers"));
        Assert.True(FecConsumers.TheRoadmapLineAgreesOnTheCount(
            "chiaki_fec_decode has three callers - frameprocessor.c, the C suite and this port's shim"));

        if (SanitizerSource.LocateRelative(@"docs\ROADMAP.md") is not { } path)
            return;

        Assert.DoesNotContain(
            "**PP30**",
            File.ReadAllText(path),
            StringComparison.Ordinal);
    }
}
