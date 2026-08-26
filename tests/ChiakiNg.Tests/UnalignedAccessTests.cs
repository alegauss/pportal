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
    /// PP374's width rule does NOT see this line, which is a finding rather than a gap in this
    /// task - and it is asserted so the gap is a fact somebody can act on.
    ///
    /// ByteOrderWidths matches `ntohl(*(T *)(...))` and this file spells it `ntohl(*((T *)(...)))`.
    /// Both are the same access; only the second is invisible. This assertion stands until the
    /// reader is widened, and turns red the moment it is - which is when it should be deleted
    /// along with the task that widened it.
    /// </summary>
    [Fact]
    public void TheWidthRuleDoesNotReachThisSpelling()
    {
        string? path = UnalignedAccess.Locate();
        if (path is null)
            return;

        IReadOnlyList<ByteOrderRead> reads =
            ByteOrderWidths.ReadsIn(UnalignedAccess.RelativePath, File.ReadAllText(path));

        Assert.Empty(reads);

        // And it does see the other spelling, so this is about the parentheses and not about the
        // file - which is the difference between a blind spot and a reader that finds nothing.
        Assert.Single(
            ByteOrderWidths.ReadsIn("x.c", "ntohl(*(chiaki_unaligned_uint32_t *)(packet->data + 4))"));
    }
}
