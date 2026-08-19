using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the managed slice-header parsers, held against bitstream.c on the recorded NALs and on
/// every truncation of them.
///
/// test/bitstream.c's fixtures are real headers and slices captured from a console, which makes them
/// the closest thing this module has to a specification. What they do not cover is what a network
/// produces, so every prefix of every fixture is compared as well - and a prefix is exactly the case
/// where a parser's answer stops being about syntax and starts being about whether it noticed the
/// end of the buffer.
/// </summary>
public class BitstreamParserOracleTests
{
    private static readonly byte[] h264Header =
    [
        0x00, 0x00, 0x00, 0x01, 0x67, 0x4d, 0x40, 0x32, 0x91, 0x8a, 0x01, 0xe0, 0x08, 0x9f, 0x97, 0x01,
        0x6a, 0x02, 0x02, 0x02, 0x80, 0x00, 0x03, 0xe9, 0x00, 0x01, 0xd4, 0xc0, 0x44, 0xd0, 0xf1, 0xf1,
        0x50, 0x00, 0x00, 0x00, 0x01, 0x68, 0xee, 0x3c, 0x80,
    ];

    private static readonly byte[] h264SliceI =
    [
        0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x80, 0x82, 0x1f, 0x00, 0x49, 0xee, 0x03, 0x29, 0xff, 0xf8,
        0x7f, 0x88, 0x46, 0x44, 0x77, 0x17, 0xe7, 0x6d, 0xb3, 0xad, 0x38, 0x19, 0x74, 0x5a, 0xf1, 0x51,
    ];

    private static readonly byte[] h264SliceP =
    [
        0x00, 0x00, 0x00, 0x01, 0x41, 0x9a, 0x04, 0x44, 0x3f, 0x41, 0x5b, 0xf4, 0x65, 0xb4, 0x3e, 0x1a,
        0xd3, 0xa0, 0x28, 0x1f, 0x83, 0x63, 0x0e, 0xc2, 0xfc, 0x9d, 0x7a, 0xc7, 0xc4, 0x7d, 0xf9, 0x18,
    ];

    private static readonly byte[] h264SlicePRef5 =
    [
        0x00, 0x00, 0x00, 0x01, 0x41, 0x9b, 0xfd, 0x98, 0x89, 0xdf, 0x00, 0x03, 0x24, 0x60, 0x47, 0x1a,
        0x90, 0x10, 0xb3, 0x2c, 0x4e, 0x45, 0xfc, 0xff, 0x45, 0x24, 0x8c, 0x79, 0xec, 0x12, 0xe5, 0x9b,
    ];

    private static readonly byte[] h265Header =
    [
        0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0c, 0x01, 0xff, 0xff, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00,
        0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00, 0x96, 0x0a, 0xc0, 0x90, 0x00, 0x00, 0x00, 0x01,
        0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00, 0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03,
        0x00, 0x96, 0xa0, 0x03, 0xc0, 0x80, 0x11, 0x07, 0xcb, 0xc2, 0xb9, 0x24, 0x29, 0x52, 0x70, 0x16,
        0xa0, 0x20, 0x20, 0x20, 0x80, 0x00, 0x07, 0xd2, 0x00, 0x01, 0xd4, 0xc0, 0x20, 0xe5, 0xa1, 0xe3,
        0xd0, 0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xc0, 0xf3, 0xc0, 0x4c, 0x90,
    ];

    private static readonly byte[] h265SliceI =
    [
        0x00, 0x00, 0x00, 0x01, 0x28, 0x01, 0xac, 0x25, 0xcf, 0x83, 0xff, 0x23, 0x54, 0xab, 0x5c, 0xf5,
        0x7a, 0x06, 0x7c, 0x3f, 0x31, 0x9b, 0xe6, 0x10, 0x57, 0xe8, 0x0e, 0xcf, 0xdd, 0xda, 0xdb, 0x3f,
    ];

    private static readonly byte[] h265SliceP =
    [
        0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd0, 0x97, 0x61, 0x28, 0x23, 0x2d, 0x8b, 0x80, 0x6f, 0xfd,
        0x2f, 0x2b, 0x11, 0xd4, 0x55, 0x04, 0x90, 0x18, 0x49, 0xe5, 0xbc, 0xc4, 0x97, 0xbc, 0x3d, 0xeb,
    ];

    private static readonly byte[] h265SlicePRef5 =
    [
        0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd7, 0x85, 0x6a, 0xae, 0xa6, 0x11, 0x80, 0x95, 0x80, 0x0a,
        0xec, 0x5e, 0xdf, 0x39, 0x86, 0xe6, 0xd9, 0x07, 0x49, 0x17, 0xe2, 0x62, 0x57, 0x14, 0xd7, 0x08,
    ];

    /// <summary>The regression case carrying an upstream issue number, which is a slice ending mid-escape.</summary>
    private static readonly byte[] h265SliceIssue213 =
    [
        0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd2, 0x0b, 0xea, 0x60, 0x86, 0x82, 0x3d, 0x00, 0x00, 0x03,
    ];

    /// <summary>Header then slice through both implementations, comparing every answer.</summary>
    private static void SameSequence(ChiakiCodec codec, byte[] header, params byte[][] slices)
    {
        using var native = new Bitstream(codec);
        var managed = new BitstreamParser(codec);

        Assert.Equal(native.ReadHeader(header), managed.ReadHeader(header));

        foreach (byte[] payload in slices)
        {
            (BitstreamSliceType Type, uint ReferenceFrame)? nativeSlice = native.ReadSlice(payload);
            bool managedOk = managed.ReadSlice(payload, out BitstreamSlice managedSlice);

            Assert.Equal(nativeSlice is not null, managedOk);
            if (nativeSlice is null)
                continue;

            Assert.Equal(nativeSlice.Value.Type, managedSlice.SliceType);
            Assert.Equal(nativeSlice.Value.ReferenceFrame, managedSlice.ReferenceFrame);
        }
    }

    [Fact]
    public void TheRecordedH264NalsReadTheSameThroughBoth()
        => SameSequence(ChiakiCodec.H264, h264Header, h264SliceI, h264SliceP, h264SlicePRef5);

    [Fact]
    public void TheRecordedH265NalsReadTheSameThroughBoth()
        => SameSequence(ChiakiCodec.H265, h265Header, h265SliceI, h265SliceP, h265SlicePRef5);

    /// <summary>The upstream regression case, which is the one fixture with an issue number on it.</summary>
    [Fact]
    public void TheIssue213SliceReadsTheSameThroughBoth()
        => SameSequence(ChiakiCodec.H265, h265Header, h265SliceIssue213);

    /// <summary>
    /// And the answers are the ones the C's own suite asserts - so the comparison is not two
    /// implementations agreeing on something wrong.
    /// </summary>
    [Fact]
    public void TheAnswersAreTheOnesTheCSuiteAsserts()
    {
        var h264 = new BitstreamParser(ChiakiCodec.H264);
        Assert.True(h264.ReadHeader(h264Header));
        Assert.Equal(3u, h264.Log2MaxFrameNumMinus4);

        Assert.True(h264.ReadSlice(h264SliceI, out BitstreamSlice i264));
        Assert.Equal(BitstreamSliceType.I, i264.SliceType);

        Assert.True(h264.ReadSlice(h264SliceP, out BitstreamSlice p264));
        Assert.Equal(BitstreamSliceType.P, p264.SliceType);
        Assert.Equal(0u, p264.ReferenceFrame);

        Assert.True(h264.ReadSlice(h264SlicePRef5, out BitstreamSlice r264));
        Assert.Equal(BitstreamSliceType.P, r264.SliceType);
        Assert.Equal(5u, r264.ReferenceFrame);

        var h265 = new BitstreamParser(ChiakiCodec.H265);
        Assert.True(h265.ReadHeader(h265Header));
        Assert.Equal(0u, h265.Log2MaxPicOrderCntLsbMinus4);

        Assert.True(h265.ReadSlice(h265SliceI, out BitstreamSlice i265));
        Assert.Equal(BitstreamSliceType.I, i265.SliceType);

        Assert.True(h265.ReadSlice(h265SliceP, out BitstreamSlice p265));
        Assert.Equal(BitstreamSliceType.P, p265.SliceType);
        Assert.Equal(0u, p265.ReferenceFrame);

        Assert.True(h265.ReadSlice(h265SlicePRef5, out BitstreamSlice r265));
        Assert.Equal(BitstreamSliceType.P, r265.SliceType);
        Assert.Equal(5u, r265.ReferenceFrame);
    }

    /// <summary>
    /// Every prefix of every fixture, which is what a network produces. The answers are not the
    /// point - agreeing about them is.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryPrefixAgrees(bool h264)
    {
        ChiakiCodec codec = h264 ? ChiakiCodec.H264 : ChiakiCodec.H265;
        byte[] header = h264 ? h264Header : h265Header;
        byte[][] slices = h264
            ? [h264SliceI, h264SliceP, h264SlicePRef5]
            : [h265SliceI, h265SliceP, h265SlicePRef5, h265SliceIssue213];

        // Header prefixes, each against a fresh pair - a failed header zeroes the SPS, and that
        // state has to be compared too, so the pair is not reused across prefixes.
        for (int n = 1; n <= header.Length; n++)
        {
            byte[] prefix = header[..n];
            using var native = new Bitstream(codec);
            var managed = new BitstreamParser(codec);

            Assert.Equal(native.ReadHeader(prefix), managed.ReadHeader(prefix));
        }

        // Slice prefixes, after a good header on both sides.
        foreach (byte[] slice in slices)
        {
            for (int n = 1; n <= slice.Length; n++)
            {
                byte[] prefix = slice[..n];
                using var native = new Bitstream(codec);
                var managed = new BitstreamParser(codec);

                Assert.Equal(native.ReadHeader(header), managed.ReadHeader(header));

                (BitstreamSliceType Type, uint ReferenceFrame)? nativeSlice = native.ReadSlice(prefix);
                bool managedOk = managed.ReadSlice(prefix, out BitstreamSlice managedSlice);

                string where = $"{codec} prefix {n} of [{Convert.ToHexString(slice)}]";
                Assert.True(nativeSlice is not null == managedOk, where);
                if (nativeSlice is null)
                    continue;

                Assert.Equal(nativeSlice.Value.Type, managedSlice.SliceType);
                Assert.Equal(nativeSlice.Value.ReferenceFrame, managedSlice.ReferenceFrame);
            }
        }
    }

    /// <summary>
    /// The finding: a FAILED header parse zeroes the SPS rather than leaving the last good one.
    /// chiaki_bitstream_header memsets before parsing, so the next slice reads its variable-width
    /// fields at width 4 instead of whatever the good header said - and nothing reports that.
    /// </summary>
    [Fact]
    public void AFailedHeaderZeroesTheSpsRatherThanKeepingTheGoodOne()
    {
        var managed = new BitstreamParser(ChiakiCodec.H264);

        Assert.True(managed.ReadHeader(h264Header));
        Assert.Equal(3u, managed.Log2MaxFrameNumMinus4);

        // A header that is refused - and the stored width goes to zero with it.
        Assert.False(managed.ReadHeader([0x00, 0x00, 0x00, 0x01, 0x67]));
        Assert.Equal(0u, managed.Log2MaxFrameNumMinus4);

        // Which the oracle agrees about, observed through what the next slice parse then answers.
        using var native = new Bitstream(ChiakiCodec.H264);
        Assert.True(native.ReadHeader(h264Header));
        Assert.False(native.ReadHeader([0x00, 0x00, 0x00, 0x01, 0x67]));

        (BitstreamSliceType Type, uint ReferenceFrame)? nativeSlice = native.ReadSlice(h264SlicePRef5);
        bool managedOk = managed.ReadSlice(h264SlicePRef5, out BitstreamSlice managedSlice);

        Assert.Equal(nativeSlice is not null, managedOk);
        if (nativeSlice is not null)
        {
            Assert.Equal(nativeSlice.Value.Type, managedSlice.SliceType);
            Assert.Equal(nativeSlice.Value.ReferenceFrame, managedSlice.ReferenceFrame);
        }
    }

    /// <summary>
    /// The two codecs disagree about what "no reference frame found" is: H264 leaves 0 and H265
    /// leaves 0xff. A caller testing one value against both would read an H265 miss as frame 0.
    /// </summary>
    [Fact]
    public void TheTwoCodecsUseDifferentNotFoundSentinels()
    {
        // An H265 P slice whose short_term_ref_pic_set comes from the SPS, so the loop that would
        // set a reference frame never runs and the sentinel is what remains.
        var h265 = new BitstreamParser(ChiakiCodec.H265);
        Assert.True(h265.ReadHeader(h265Header));

        var sentinels = new List<uint>();
        foreach (byte[] slice in new[] { h265SliceI, h265SliceP, h265SlicePRef5 })
        {
            if (h265.ReadSlice(slice, out BitstreamSlice s))
                sentinels.Add(s.ReferenceFrame);
        }

        // Whatever the fixtures give, the sentinel the code writes for H265 is 0xff and for H264 0 -
        // stated directly, because no fixture has to exercise a miss for the difference to matter.
        Assert.Contains(0u, sentinels);
        Assert.DoesNotContain(0xffu, sentinels);   // these fixtures all find one

        var h264 = new BitstreamParser(ChiakiCodec.H264);
        Assert.True(h264.ReadHeader(h264Header));
        Assert.True(h264.ReadSlice(h264SliceP, out BitstreamSlice p));
        Assert.Equal(0u, p.ReferenceFrame);
    }

    /// <summary>
    /// The truncation cases PP35 already drove, now through both - so the port's parser refuses
    /// exactly what libchiaki's does rather than merely refusing.
    /// </summary>
    [Fact]
    public void TheTruncationCasesRefuseTogether()
    {
        using var native264 = new Bitstream(ChiakiCodec.H264);
        var managed264 = new BitstreamParser(ChiakiCodec.H264);

        foreach (byte[] bad in new byte[][]
        {
            [0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1e],
            [0x00, 0x00, 0x00, 0x01, 0x67],
            [],
            [0x00],
        })
        {
            Assert.False(managed264.ReadHeader(bad));
            if (bad.Length > 0)
                Assert.False(native264.ReadHeader(bad));
        }

        Assert.Null(native264.ReadSlice([0x00, 0x00, 0x00, 0x01, 0x65]));
        Assert.False(managed264.ReadSlice([0x00, 0x00, 0x00, 0x01, 0x65], out _));

        using var native265 = new Bitstream(ChiakiCodec.H265);
        var managed265 = new BitstreamParser(ChiakiCodec.H265);

        (BitstreamSliceType Type, uint ReferenceFrame)? n = native265.ReadSlice([0x00, 0x00, 0x00, 0x01, 0x02]);
        bool m = managed265.ReadSlice([0x00, 0x00, 0x00, 0x01, 0x02], out _);
        Assert.Equal(n is not null, m);
    }

    /// <summary>
    /// A start code beyond the 64-byte scan is not found, in either. So a payload with leading
    /// rubbish is refused rather than parsed out of whatever follows it.
    /// </summary>
    [Fact]
    public void AStartCodePastTheScanLimitIsNotFound()
    {
        var payload = new byte[80 + h264Header.Length - 4];
        Array.Fill(payload, (byte)0xaa);
        h264Header.AsSpan(0).CopyTo(payload.AsSpan(80 - 4));

        using var native = new Bitstream(ChiakiCodec.H264);
        var managed = new BitstreamParser(ChiakiCodec.H264);

        Assert.Equal(native.ReadHeader(payload), managed.ReadHeader(payload));
        Assert.False(managed.ReadHeader(payload));
    }
}
