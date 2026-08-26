using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP378, under PP295: senkusha.c's multi-byte accesses all go through the unaligned type.
///
/// Stated over the file rather than over the line, so a fifth access added later is covered without
/// anyone remembering this task. The sweep is asserted to have found something first, which is
/// PP271's lesson: a rule over an empty set passes for the wrong reason.
/// </summary>
public class UnalignedAccessTests(ITestOutputHelper output)
{
    private static IReadOnlyList<PointerAccess>? Accesses()
    {
        string? path = UnalignedAccess.Locate();
        if (path is null)
            return null;

        return UnalignedAccess.AccessesIn(UnalignedAccess.RelativePath, File.ReadAllText(path));
    }

    /// <summary>THE TASK. Nothing in senkusha.c claims an alignment its pointer does not carry.</summary>
    [Fact]
    public void EveryAccessGoesThroughTheUnalignedType()
    {
        if (Accesses() is not { } accesses)
            return;

        // PP271: the sweep found something, or the rule below is about nothing. Four today - three
        // writes and the read PP378 corrected.
        Assert.True(accesses.Count >= 4, $"only {accesses.Count} pointer accesses were found");

        foreach (PointerAccess access in accesses)
            output.WriteLine(access.ToString());

        IReadOnlyList<PointerAccess> claiming = UnalignedAccess.ClaimingAlignment(accesses);

        Assert.True(
            claiming.Count == 0,
            "these read or write multiple bytes through a plain cast, on pointers that carry no "
            + "alignment guarantee:\n  " + string.Join("\n  ", claiming));
    }

    /// <summary>
    /// And the one PP378 was about is in the set, so the rule really does cover the pong tag rather
    /// than passing over it.
    /// </summary>
    [Fact]
    public void ThePongTagIsAmongThemAndIsUnaligned()
    {
        if (Accesses() is not { } accesses)
            return;

        PointerAccess tag = Assert.Single(
            accesses, a => a.Target.Contains("packet->data + 4", StringComparison.Ordinal));

        Assert.Equal(32, tag.Bits);
        Assert.True(tag.IsUnaligned);
    }

    /// <summary>
    /// The reader tells the two spellings apart, shown on the before and after of this task.
    ///
    /// Without this the rule could be a reader that returns "unaligned" for everything, and the
    /// green above would say nothing at all.
    /// </summary>
    [Fact]
    public void TheReaderTellsTheTwoSpellingsApart()
    {
        const string Before = "uint32_t tag = ntohl(*((uint32_t *)(packet->data + 4)));";
        const string After =
            "uint32_t tag = ntohl(*((chiaki_unaligned_uint32_t *)(packet->data + 4)));";

        PointerAccess before = Assert.Single(UnalignedAccess.AccessesIn("x.c", Before));
        Assert.False(before.IsUnaligned);
        Assert.Single(UnalignedAccess.ClaimingAlignment([before]));

        PointerAccess after = Assert.Single(UnalignedAccess.AccessesIn("x.c", After));
        Assert.True(after.IsUnaligned);
        Assert.Empty(UnalignedAccess.ClaimingAlignment([after]));
    }

    /// <summary>A commented-out access is not code, which is PP374's exclusion.</summary>
    [Fact]
    public void ACommentedOutAccessIsNotCounted()
    {
        const string Commented = "\t\t// uint32_t tag = ntohl(*((uint32_t *)(packet->data + 4)));";

        Assert.Empty(UnalignedAccess.AccessesIn("x.c", Commented));
    }

    /// <summary>And the reader answers nothing about a file with nothing in it (PP272).</summary>
    [Fact]
    public void TheReaderReadsTheFile()
    {
        Assert.Empty(UnalignedAccess.AccessesIn("x.c", ""));
    }

    /// <summary>
    /// PP381 closed the gap this test was written to record, so it now asserts the join instead.
    ///
    /// The pong tag is the one line both rules meet on: PP378 says the access goes through the
    /// unaligned type, PP374 says the swap matches the width it wraps - and until PP381 widened the
    /// reader, the second rule could not see this spelling at all and this test asserted its
    /// absence.
    /// </summary>
    [Fact]
    public void TheWidthRuleNowReachesThisLineToo()
    {
        string? path = UnalignedAccess.Locate();
        if (path is null)
            return;

        IReadOnlyList<ByteOrderRead> reads =
            ByteOrderWidths.ReadsIn(UnalignedAccess.RelativePath, File.ReadAllText(path));

        ByteOrderRead tag = Assert.Single(reads);

        Assert.Equal("ntohl", tag.Conversion);
        Assert.Equal(32, tag.ReadBits);
        Assert.Empty(ByteOrderWidths.Mismatches(reads));
    }
}
