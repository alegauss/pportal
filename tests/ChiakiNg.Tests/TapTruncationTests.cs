using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP612, under PP27: the eighteen bytes are the C's decision, not the port's.
///
/// PP510's section gives the reason for keeping heads and reads like a choice this port made. The
/// truncation is in the vendored C, before a managed byte exists - so raising it is the local patch
/// PP601 established this line is not exempt from, and a session that assumed otherwise would write
/// the patch before finding out.
/// </summary>
public class TapTruncationTests
{
    /// <summary>The constant is defined in the vendored header, at the width the capture assumes.</summary>
    [Fact]
    public void TheVendoredHeaderDecidesTheWidth()
    {
        if (TapTruncation.LocateHeader() is not { } path)
            return;

        Assert.True(
            TapTruncation.TheHeaderDefinesTheHead(File.ReadAllText(path)),
            $"{TapTruncation.HeadConstant} is not {TapTruncation.Head} in messagetap.h any more - "
                + "if that moved, every head in a capture is a different length and PP510's reason "
                + "for the number no longer describes what is recorded");
    }

    /// <summary>And takion.c is what applies it, at the emit.</summary>
    [Fact]
    public void TheEmitAppliesIt()
    {
        if (TapTruncation.LocateSource() is not { } path)
            return;

        Assert.True(
            TapTruncation.TheEmitAppliesIt(File.ReadAllText(path)),
            "takion.c no longer truncates the tap emit by the constant, so the capture's heads are "
                + "some other length now");
    }

    /// <summary>
    /// The managed capture keeps exactly what the C hands it, and the two numbers are one.
    ///
    /// Held as an equality rather than as two constants that happen to agree: this is the join the
    /// whole argument rests on, and a managed side that kept fewer bytes would be a second
    /// truncation nobody decided.
    /// </summary>
    [Fact]
    public void TheManagedCaptureKeepsWhatTheCHandsIt()
    {
        Assert.Equal(TakionTimingCapture.HeadBytes, TapTruncation.Head);
        Assert.Equal(DatagramCorpus.HeadBytes, TapTruncation.Head);
    }

    /// <summary>
    /// PP27 IS NOT EXEMPT FROM THE RULE that would have to be narrowed, which is what makes this a
    /// decision rather than a task.
    ///
    /// Joined to VendoredCRule so narrowing that rule is done in one place and this follows it,
    /// exactly as PP601's own check does one layer up.
    /// </summary>
    [Fact]
    public void ThePatchRouteIsClosedUntilTheRuleSaysOtherwise()
    {
        Assert.DoesNotContain("PP27", VendoredCRule.LinesItDoesNotReach);

        // PP637: PP295 joined - its deliverable is a deletion and §PP295 says so. PP27's is not:
        // it wants a `static` removed so a capture can reach the receive loop, which is the one
        // thing the rule forbids.
        Assert.Equal(["PP33", "PP30", "PP295"], VendoredCRule.LinesItDoesNotReach);
    }

    /// <summary>And the readers see a moved constant, so neither check is green on a stale pattern.</summary>
    [Fact]
    public void TheReadersSeeAMovedConstant()
    {
        Assert.False(TapTruncation.TheHeaderDefinesTheHead(
            $"#define {TapTruncation.HeadConstant} 1500"));

        Assert.True(TapTruncation.TheHeaderDefinesTheHead(
            $"#define {TapTruncation.HeadConstant} {TapTruncation.Head}"));

        Assert.False(TapTruncation.TheEmitAppliesIt(
            $"\t// {TapTruncation.HeadConstant} is what PP612 is about\n"));

        Assert.True(TapTruncation.TheEmitAppliesIt(
            $"\t\tsize_t head = buf_size < {TapTruncation.HeadConstant}\n"));
    }
}
