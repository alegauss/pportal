using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP374: a conversion has to match the width of the read it wraps.
///
/// The value this was found on is logged and used nowhere else, so nothing about behaviour rests on
/// it. What rests on it is the diagnostic on the motion-reset path, which is where somebody looks
/// when motion control drifts - and it was wrong by a factor of 65536 for every session.
/// </summary>
public class ByteOrderWidthsTests
{
    /// <summary>
    /// THE RULE, over every conversion in the library rather than the one that was wrong.
    ///
    /// Six reads pair ntohs with a 16-bit cast, including the seqnum on the line directly above the
    /// one that did not - which is what made a four-byte read through a two-byte swap look deliberate.
    /// A rule over the group is the only kind that would have caught it.
    /// </summary>
    [Fact]
    public void EveryConversionMatchesTheWidthItIsGiven()
    {
        string? dir = ByteOrderWidths.LocateSources();
        if (dir is null)
            return;

        var reads = new List<ByteOrderRead>();
        foreach (string file in Directory.EnumerateFiles(dir, "*.c", SearchOption.AllDirectories))
            reads.AddRange(ByteOrderWidths.ReadsIn(Path.GetFileName(file), File.ReadAllText(file)));

        // PP381: NOT NotEmpty. That is what this said, and it held while the reader was matching
        // five of the thirty-eight conversions in the tree - a clean sweep over five reads exactly
        // like a clean sweep over all of them, and nothing here could tell the two apart. The
        // failure was a doubled parenthesis the regex did not allow, and it was found by another
        // task rather than by this one. A floor is what makes the sweep's REACH an assertion.
        Assert.True(
            reads.Count >= ByteOrderWidths.Floor,
            $"the sweep found {reads.Count} conversions, under the floor of {ByteOrderWidths.Floor} - "
                + "the reader has stopped matching a spelling the tree uses");

        IReadOnlyList<ByteOrderRead> mismatched = ByteOrderWidths.Mismatches(reads);

        Assert.True(
            mismatched.Count == 0,
            "these conversions do not match the width of the read they wrap:\n  "
                + string.Join("\n  ", mismatched));
    }

    /// <summary>
    /// PP381: the reader sees BOTH spellings, which is the whole of what was wrong with it.
    ///
    /// The tree writes the doubled parenthesis in takion.c, audio.c, frameprocessor.c, senkusha.c
    /// and ctrl.c, and the single one in ctrl.c and streamconnection.c. Only the second was matched,
    /// so thirty-three of thirty-eight conversions were outside a rule that reported them clean.
    /// </summary>
    [Fact]
    public void TheReaderSeesBothSpellingsOfTheCast()
    {
        const string Single = "uint32_t n = ntohl(*(chiaki_unaligned_uint32_t *)(buf + 4));";
        const string Doubled = "uint32_t n = ntohl(*((chiaki_unaligned_uint32_t *)(buf + 4)));";

        ByteOrderRead single = Assert.Single(ByteOrderWidths.ReadsIn("x.c", Single));
        ByteOrderRead doubled = Assert.Single(ByteOrderWidths.ReadsIn("x.c", Doubled));

        Assert.Equal(32, single.ReadBits);
        Assert.Equal(32, doubled.ReadBits);
        Assert.True(single.Matches);
        Assert.True(doubled.Matches);
    }

    /// <summary>
    /// And it finds a mismatch in the doubled spelling too, so the widening bought a rule and not
    /// just a count.
    /// </summary>
    [Fact]
    public void AMismatchInTheDoubledSpellingIsFound()
    {
        const string Wrong = "uint32_t timestamp = ntohs(*((chiaki_unaligned_uint32_t *)(buf + 4)));";

        ByteOrderRead read = Assert.Single(ByteOrderWidths.ReadsIn("x.c", Wrong));

        Assert.False(read.Matches);
        Assert.Single(ByteOrderWidths.Mismatches([read]));
    }

    /// <summary>
    /// The reader finds the mismatch as it was written, so the sweep means something.
    /// </summary>
    [Fact]
    public void TheReaderFindsAFourByteReadThroughATwoByteSwap()
    {
        const string asItWas =
            "\t\t\tuint32_t timestamp = ntohs(*(chiaki_unaligned_uint32_t *)(buf + 4));";

        ByteOrderRead found = Assert.Single(ByteOrderWidths.ReadsIn("streamconnection.c", asItWas));

        Assert.Equal("ntohs", found.Conversion);
        Assert.Equal(32, found.ReadBits);
        Assert.False(found.Matches);
        Assert.Single(ByteOrderWidths.Mismatches([found]));
    }

    /// <summary>And the inverse, which nobody has written yet.</summary>
    [Fact]
    public void TheReaderFindsTheInverseMismatchToo()
    {
        const string inverse = "\tuint16_t n = ntohl(*(chiaki_unaligned_uint16_t *)(buf));";

        ByteOrderRead found = Assert.Single(ByteOrderWidths.ReadsIn("nowhere.c", inverse));

        Assert.Equal("ntohl", found.Conversion);
        Assert.Equal(16, found.ReadBits);
        Assert.False(found.Matches);
    }

    /// <summary>And leaves a matched pair alone, in both widths and with or without the alias.</summary>
    [Theory]
    [InlineData("\tuint16_t n = ntohs(*(chiaki_unaligned_uint16_t *)(buf));", "ntohs", 16)]
    [InlineData("\tuint32_t n = ntohl(*(chiaki_unaligned_uint32_t *)(buf + 4));", "ntohl", 32)]
    [InlineData("\tuint16_t n = ntohs(*(uint16_t *)(buf));", "ntohs", 16)]
    public void TheReaderLeavesAMatchedPairAlone(string line, string conversion, int bits)
    {
        ByteOrderRead found = Assert.Single(ByteOrderWidths.ReadsIn("nowhere.c", line));

        Assert.Equal(conversion, found.Conversion);
        Assert.Equal(bits, found.ReadBits);
        Assert.True(found.Matches);
        Assert.Empty(ByteOrderWidths.Mismatches([found]));
    }

    /// <summary>
    /// And commented-out code is not code.
    ///
    /// The pad info handler has a disabled read sitting between the two live ones. Flagging it would
    /// be a finding nobody can act on, which is the fastest way to teach people to ignore a check.
    /// </summary>
    [Fact]
    public void ADisabledReadIsNotARead()
    {
        const string disabled =
            "\t\t\t// int16_t unknown = ntohs(*(chiaki_unaligned_uint32_t *)(buf + 2));";

        Assert.Empty(ByteOrderWidths.ReadsIn("streamconnection.c", disabled));
    }

    /// <summary>And a mismatch names its file and line, so a failure is somewhere to go.</summary>
    [Fact]
    public void AMismatchNamesWhereItIs()
    {
        const string twoLines = """
            	uint16_t a = ntohs(*(chiaki_unaligned_uint16_t *)(buf));
            	uint32_t b = ntohs(*(chiaki_unaligned_uint32_t *)(buf + 4));
            """;

        ByteOrderRead bad = Assert.Single(
            ByteOrderWidths.Mismatches(ByteOrderWidths.ReadsIn("streamconnection.c", twoLines)));

        Assert.Equal(2, bad.Line);
        Assert.Contains("streamconnection.c:2", bad.ToString(), StringComparison.Ordinal);
    }
}
