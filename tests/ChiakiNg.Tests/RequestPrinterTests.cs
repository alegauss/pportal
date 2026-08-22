using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP261: handled, then printed anyway.
///
/// <see cref="TheFailureBranchIsUnreachableAndTheLineBelowItIsWhy"/> carries the task: the branch
/// cannot fire today, and what makes that matter is what the line after it would do.
/// </summary>
public class RequestPrinterTests
{
    /// <summary>
    /// THE ARITHMETIC. Both encoded buffers are exactly the right size, so the failure branch cannot
    /// be reached - computed, not asserted.
    /// </summary>
    [Fact]
    public void TheFailureBranchIsUnreachableAndTheLineBelowItIsWhy()
    {
        foreach (PrintBuffer buffer in RequestPrinter.Buffers.Where(b => b.Name != "mac_addr"))
        {
            Assert.True(RequestPrinter.Fits(buffer), $"{buffer.Name} does not fit");
            Assert.False(RequestPrinter.CanFail(buffer));

            // Exactly, with nothing spare.
            Assert.Equal(buffer.Size, RequestPrinter.EncodedLength(buffer.SourceBytes) + 1);

            // And if it ever did fail, this is what the next line would print.
            Assert.True(RequestPrinter.WouldPrintUnterminated(buffer));
        }

        // The print is not guarded, which is what joins the two halves.
        Assert.False(RequestPrinter.ThePrintIsGuarded);
    }

    /// <summary>The encoding lengths, on their own.</summary>
    [Theory]
    [InlineData(16, 24)]
    [InlineData(20, 28)]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(3, 4)]
    public void TheEncodingLengthIsFourPerThree(int bytes, int chars)
        => Assert.Equal(chars, RequestPrinter.EncodedLength(bytes));

    /// <summary>
    /// Two of three buffers are left bare, and the one that is zeroed is the one that never goes
    /// through the encoder.
    /// </summary>
    [Fact]
    public void TwoOfThreeBuffersAreBare()
    {
        Assert.Equal(2, RequestPrinter.Buffers.Count(b => !b.Initialised));

        PrintBuffer zeroed = RequestPrinter.Buffers.Single(b => b.Initialised);
        Assert.Equal("mac_addr", zeroed.Name);
    }

    /// <summary>
    /// The label for a static candidate says remote, and PP248 measured that it is not.
    /// </summary>
    [Fact]
    public void TheStaticCandidateIsLabelledRemote()
    {
        Assert.Equal("REMOTE CANDIDATE", RequestPrinter.LabelFor(CandidateType.Static));
        Assert.False(RequestPrinter.LabelDescribesWhoseItIs(CandidateType.Static));

        foreach (CandidateType type in Enum.GetValues<CandidateType>().Where(t => t != CandidateType.Static))
            Assert.True(RequestPrinter.LabelDescribesWhoseItIs(type));
    }

    /// <summary>Every type has a label, and anything else falls through to one.</summary>
    [Fact]
    public void EveryTypeHasALabel()
    {
        foreach (CandidateType type in Enum.GetValues<CandidateType>())
            Assert.NotEqual(RequestPrinter.UnknownLabel, RequestPrinter.LabelFor(type));

        Assert.Equal(RequestPrinter.UnknownLabel, RequestPrinter.LabelFor((CandidateType)9));
    }

    /// <summary>And the MAC block never runs, because this client never sends one.</summary>
    [Fact]
    public void TheMacBlockNeverRuns()
        => Assert.False(RequestPrinter.ThePrintsMac(StunLookup.MacSent));

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void ThePrintersAreStillTheCores()
    {
        string? file = RequestPrinterSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            RequestPrinterSource.ThePrintAfterAFailureIsStillUnconditional(core),
            "the print after each failure branch is still unconditional");
        Assert.True(
            RequestPrinterSource.TheTwoEncodedBuffersAreStillBare(core),
            "the two encoded buffers are still bare and the third still zeroed");
        Assert.True(
            RequestPrinterSource.TheSizesStillMakeItUnreachable(core),
            "and the source sizes still make the branch unreachable");

        Assert.True(
            RequestPrinterSource.TheStaticIsStillLabelledRemote(core),
            "the static candidate is still labelled remote");
        Assert.True(
            RequestPrinterSource.EveryTypeStillHasALabel(core), "every type still has a label");
    }

    /// <summary>
    /// And the encoder still gives up without a terminator, which is what the unreachable branch
    /// would run into.
    /// </summary>
    [Fact]
    public void TheEncoderStillLeavesItUnterminated()
    {
        string? file = RequestPrinterSource.LocateEncoder();
        if (file is null)
            return;

        Assert.True(
            RequestPrinterSource.TheEncoderStillLeavesItUnterminated(File.ReadAllText(file)),
            "the encoder terminates on the way out now, which would make the print harmless");
    }
}
