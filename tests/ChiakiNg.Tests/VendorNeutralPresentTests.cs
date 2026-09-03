using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP647: the floor row that says a present needs no vendor, checked instead of believed.
///
/// PP53 shipped a measurement of DXGI's tearing flags and it sits in Block I, which is titled
/// "NVIDIA path". The block is a schedule and the heading is not a requirement, but nothing in the
/// tree said so - and the non-goal binding vendor features to docs/HARDWARE-CONTRACT.md makes the
/// misreading cost something, because the floor's whole argument is that a machine with Intel
/// graphics is an ordinary laptop.
/// </summary>
public class VendorNeutralPresentTests(ITestOutputHelper output)
{
    /// <summary>
    /// Nothing in the present path names a GPU vendor.
    ///
    /// The swapchain probes, the DirectComposition trees and both tearing probes, together. It is
    /// true today by construction rather than by discipline - the tearing pair is DXGI's and there
    /// was never a vendor call to reach for - and this is what turns that into something a later
    /// commit has to argue with rather than quietly outlive.
    /// </summary>
    [Fact]
    public void ThePresentPathNamesNoVendor()
    {
        int read = 0;

        foreach (string relative in VendorNeutralPresent.PresentPathFiles)
        {
            if (VendorNeutralPresent.Locate(relative) is not { } path)
                continue;

            read++;
            IReadOnlyList<string> named =
                VendorNeutralPresent.VendorNamesIn(File.ReadAllText(path));

            output.WriteLine($"{relative}: {named.Count} vendor name(s)");
            Assert.True(
                named.Count == 0,
                $"{relative} names {string.Join(", ", named)}, so the present path has acquired a "
                    + "vendor and the floor row in docs/HARDWARE-CONTRACT.md that says it has not "
                    + "is now false");
        }

        // PP271: a list that resolved to nothing would satisfy the loop by finding nothing.
        if (VendorNeutralPresent.Locate(VendorNeutralPresent.PresentPathFiles[0]) is not null)
            Assert.Equal(VendorNeutralPresent.PresentPathFiles.Count, read);
    }

    /// <summary>
    /// And the check can find one, because the shim next door has one on purpose.
    ///
    /// Without this the test above passes on a reader that matches nothing - which is the shape the
    /// whole file exists to refuse, and the shape PP77's own row in the contract already warns
    /// about. chiaki_decoder_choice takes an nvidia_card flag because the decision turns on one.
    /// </summary>
    [Fact]
    public void TheDecoderChoiceShimNamesOneOnPurpose()
    {
        if (VendorNeutralPresent.Locate(VendorNeutralPresent.DecoderChoiceShim) is not { } path)
            return;

        IReadOnlyList<string> named = VendorNeutralPresent.VendorNamesIn(File.ReadAllText(path));

        output.WriteLine($"{VendorNeutralPresent.DecoderChoiceShim}: {string.Join(", ", named)}");
        Assert.Contains("nvidia", named);
    }

    /// <summary>
    /// A vendor in a comment counts, which is the case a token list would be tempted to skip.
    ///
    /// A path explained in terms of one card is one somebody implements in terms of that card. The
    /// match is case-insensitive for the same reason: NVAPI, NvAPI and nvapi are one decision.
    /// </summary>
    [Theory]
    [InlineData("// this is what NVAPI would do", "nvapi")]
    [InlineData("/* GeForce only */", "geforce")]
    [InlineData("AmD_AgS_Init();", "amd_ags")]
    public void AVendorInACommentIsStillAVendor(string source, string expected)
        => Assert.Contains(expected, VendorNeutralPresent.VendorNamesIn(source));

    /// <summary>And plain DXGI is not a vendor, which is the whole point of the row.</summary>
    [Fact]
    public void TheTearingPairIsNotAVendorCall()
        => Assert.Empty(VendorNeutralPresent.VendorNamesIn(
            "DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING; DXGI_PRESENT_ALLOW_TEARING; IDXGIFactory5"));

    /// <summary>
    /// PP647: and the design the ledger says went into the contract is in the contract.
    ///
    /// PP647 shipped with its rationale section deleted and "design recorded in
    /// docs/HARDWARE-CONTRACT.md" in its place. That clause requires the PATH to resolve and says
    /// nothing about what the file holds - PP642 is the line arguing for a check that reads the
    /// ledger and holds every such clause at once, and this is the second entry to carry one, so
    /// this is the second hand-written check standing in for it.
    ///
    /// What is asserted is the row and its reason, not the prose around them: a floor row is
    /// something a later change has to argue with, and the argument is that a heading is a schedule.
    /// </summary>
    [Fact]
    public void PP647sRecordedDesignIsInTheContractTheLedgerNames()
    {
        if (AssertionRatchet.LocateLedger() is not { } ledgerPath)
            return;

        Assert.Contains(
            "design recorded in `docs/HARDWARE-CONTRACT.md`",
            File.ReadAllText(ledgerPath),
            StringComparison.Ordinal);

        if (VendorNeutralPresent.Locate(@"docs\HARDWARE-CONTRACT.md") is not { } path)
            return;

        string contract = File.ReadAllText(path);

        foreach (string clause in (string[])
        [
            "A present that can tear, with no vendor extension",
            "A block heading is a schedule, not a requirement",
            "PP647",
        ])
        {
            Assert.Contains(clause, contract, StringComparison.Ordinal);
        }
    }
}
