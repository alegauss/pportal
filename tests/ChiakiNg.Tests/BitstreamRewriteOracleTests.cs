using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the one function that EDITS the bitstream, compared with libchiaki byte for byte.
///
/// Every other module here is compared on what it answers. This one is compared on what it wrote:
/// both implementations get identical copies of the same slice, and the assertion is that the two
/// buffers are equal afterwards as well as the two return values. A rewriter that returned the right
/// answer and moved a different bit would pass any test that only read the answer.
///
/// PP69's guard is the reason the buffers are the interesting part. The write is positioned by the
/// reader, so an overrun parse or a slice too short to have consumed eight bytes puts it outside
/// what the caller owns - and the guard refuses rather than clamping.
/// </summary>
public class BitstreamRewriteOracleTests
{
    private static readonly byte[] h265Header =
    [
        0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0c, 0x01, 0xff, 0xff, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00,
        0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00, 0x96, 0x0a, 0xc0, 0x90, 0x00, 0x00, 0x00, 0x01,
        0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00, 0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03,
        0x00, 0x96, 0xa0, 0x03, 0xc0, 0x80, 0x11, 0x07, 0xcb, 0xc2, 0xb9, 0x24, 0x29, 0x52, 0x70, 0x16,
        0xa0, 0x20, 0x20, 0x20, 0x80, 0x00, 0x07, 0xd2, 0x00, 0x01, 0xd4, 0xc0, 0x20, 0xe5, 0xa1, 0xe3,
        0xd0, 0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xc0, 0xf3, 0xc0, 0x4c, 0x90,
    ];

    /// <summary>The P slice test/bitstream.c drives the rewriter with.</summary>
    private static readonly byte[] slicePFull =
    [
        0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd2, 0x85, 0x7a, 0xaa, 0xa6, 0x08, 0x60, 0x13, 0x55, 0x17,
        0x6b, 0x71, 0x72, 0xf9, 0x6e, 0xd4, 0xf2, 0x66, 0x78, 0x0c, 0x12, 0xe7, 0x79, 0xf0, 0xbc, 0xc9,
    ];

    /// <summary>An I slice, which the rewriter has to refuse: it is not a P slice.</summary>
    private static readonly byte[] sliceI =
    [
        0x00, 0x00, 0x00, 0x01, 0x28, 0x01, 0xac, 0x25, 0xcf, 0x83, 0xff, 0x23, 0x54, 0xab, 0x5c, 0xf5,
        0x7a, 0x06, 0x7c, 0x3f, 0x31, 0x9b, 0xe6, 0x10, 0x57, 0xe8, 0x0e, 0xcf, 0xdd, 0xda, 0xdb, 0x3f,
    ];

    /// <summary>Runs both rewriters on identical copies and compares the answer and the bytes.</summary>
    private static void SameRewrite(byte[] slice, int size, uint referenceFrame, string where)
    {
        byte[] forNative = (byte[])slice.Clone();
        byte[] forManaged = (byte[])slice.Clone();

        using var native = new Bitstream(ChiakiCodec.H265);
        var managed = new BitstreamParser(ChiakiCodec.H265);

        Assert.Equal(native.ReadHeader(h265Header), managed.ReadHeader(h265Header));

        bool nativeOk = native.SetReferenceFrame(forNative, size, referenceFrame);
        bool managedOk = managed.SetReferenceFrame(forManaged, size, referenceFrame);

        Assert.True(nativeOk == managedOk, $"{where}: answered {nativeOk} vs {managedOk}");
        Assert.True(
            forNative.AsSpan().SequenceEqual(forManaged),
            $"{where}: native [{Convert.ToHexString(forNative)}] managed [{Convert.ToHexString(forManaged)}]");
    }

    /// <summary>Every reference-frame index against the whole slice.</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(5u)]
    [InlineData(15u)]
    [InlineData(16u)]
    [InlineData(17u)]
    [InlineData(0xffu)]
    [InlineData(uint.MaxValue)]
    public void EveryReferenceIndexRewritesTheSameBytes(uint referenceFrame)
        => SameRewrite(slicePFull, slicePFull.Length, referenceFrame, $"ref={referenceFrame}");

    /// <summary>
    /// Every prefix of the slice, at every reference index in range - which is where PP69's guard
    /// does its work and where the two implementations have to refuse in the same places.
    /// </summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(5u)]
    public void EveryPrefixRewritesTheSameBytes(uint referenceFrame)
    {
        for (int n = 1; n <= slicePFull.Length; n++)
        {
            // A tail of sentinel bytes past the declared length, so a write beyond it is visible in
            // the comparison rather than only in a crash.
            const int Tail = 16;
            var arena = new byte[n + Tail];
            Array.Fill(arena, (byte)0xa5);
            slicePFull.AsSpan(0, n).CopyTo(arena);

            SameRewrite(arena, n, referenceFrame, $"prefix {n} ref={referenceFrame}");
        }
    }

    /// <summary>
    /// The eight-byte case, which is the one length where the lower-bound half of the guard changes
    /// anything: the write would reach before the buffer, so it must refuse AND leave the bytes.
    /// </summary>
    [Fact]
    public void TheEightByteSliceIsRefusedWithoutBeingWritten()
    {
        byte[] eight = [0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd2, 0x85];
        byte[] before = (byte[])eight.Clone();

        var managed = new BitstreamParser(ChiakiCodec.H265);
        Assert.True(managed.ReadHeader(h265Header));

        Assert.False(managed.SetReferenceFrame(eight, eight.Length, 0));
        Assert.Equal(before, eight);

        SameRewrite(eight, eight.Length, 0, "eight bytes");
    }

    /// <summary>An I slice is not a P slice, and is refused untouched by both.</summary>
    [Fact]
    public void AnISliceIsRefused()
    {
        SameRewrite(sliceI, sliceI.Length, 0, "I slice");

        byte[] copy = (byte[])sliceI.Clone();
        var managed = new BitstreamParser(ChiakiCodec.H265);
        Assert.True(managed.ReadHeader(h265Header));
        Assert.False(managed.SetReferenceFrame(copy, copy.Length, 0));
        Assert.Equal(sliceI, copy);
    }

    /// <summary>
    /// H264 is refused by the dispatcher without the data being looked at, so the buffer cannot be
    /// touched whatever it holds.
    /// </summary>
    [Fact]
    public void H264IsRefusedWithoutReadingAnything()
    {
        byte[] slice = (byte[])slicePFull.Clone();
        byte[] before = (byte[])slicePFull.Clone();

        var managed = new BitstreamParser(ChiakiCodec.H264);
        Assert.False(managed.SetReferenceFrame(slice, slice.Length, 0));
        Assert.Equal(before, slice);

        using var native = new Bitstream(ChiakiCodec.H264);
        byte[] nativeSlice = (byte[])slicePFull.Clone();
        Assert.False(native.SetReferenceFrame(nativeSlice, nativeSlice.Length, 0));
        Assert.Equal(before, nativeSlice);
    }

    /// <summary>
    /// The rewrite is observable through the read path: after marking a frame, parsing the slice
    /// reports it. That is the whole point of the function, and it is checked on both.
    /// </summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    public void WhatWasWrittenIsWhatTheParserThenReads(uint referenceFrame)
    {
        byte[] slice = (byte[])slicePFull.Clone();

        var managed = new BitstreamParser(ChiakiCodec.H265);
        Assert.True(managed.ReadHeader(h265Header));

        if (!managed.SetReferenceFrame(slice, slice.Length, referenceFrame))
            return;   // this index is not in this slice's set; nothing to read back

        Assert.True(managed.ReadSlice(slice, out BitstreamSlice parsed));
        Assert.Equal(BitstreamSliceType.P, parsed.SliceType);
        Assert.Equal(referenceFrame, parsed.ReferenceFrame);

        // And libchiaki reads the managed rewrite the same way, which is the half that matters:
        // the bytes go to a console, not to this parser.
        using var native = new Bitstream(ChiakiCodec.H265);
        Assert.True(native.ReadHeader(h265Header));
        (BitstreamSliceType Type, uint ReferenceFrame)? nativeRead = native.ReadSlice(slice);

        Assert.NotNull(nativeRead);
        Assert.Equal(BitstreamSliceType.P, nativeRead!.Value.Type);
        Assert.Equal(referenceFrame, nativeRead.Value.ReferenceFrame);
    }

    /// <summary>
    /// Rewriting after a REFUSED header, which zeroes the SPS - so the field widths change and the
    /// reader stops somewhere else, which moves the write. Both must move it to the same place.
    /// </summary>
    [Fact]
    public void ARewriteAfterAFailedHeaderStillAgrees()
    {
        byte[] forNative = (byte[])slicePFull.Clone();
        byte[] forManaged = (byte[])slicePFull.Clone();

        using var native = new Bitstream(ChiakiCodec.H265);
        var managed = new BitstreamParser(ChiakiCodec.H265);

        Assert.True(native.ReadHeader(h265Header));
        Assert.True(managed.ReadHeader(h265Header));

        byte[] bad = [0x00, 0x00, 0x00, 0x01, 0x42];
        Assert.Equal(native.ReadHeader(bad), managed.ReadHeader(bad));
        Assert.Equal(0u, managed.Log2MaxPicOrderCntLsbMinus4);

        bool nativeOk = native.SetReferenceFrame(forNative, forNative.Length, 1);
        bool managedOk = managed.SetReferenceFrame(forManaged, forManaged.Length, 1);

        Assert.Equal(nativeOk, managedOk);
        Assert.Equal(forNative, forManaged);
    }
}
