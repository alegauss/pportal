using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP35: test/bitstream.c's four truncation cases, none of which the port drove.
///
/// The suite checks the four cases that parse a WELL-FORMED slice. These are the other four, and
/// they are the ones a network produces: a packet cut short is what packet loss looks like after
/// the frame processor has given up waiting, and a parser reading past the end of one does it on
/// data an attacker on the path chose (PP105 established there is such a position).
///
/// Their assertions are not about answers. Three are about refusing, one is about terminating,
/// and the last is about not writing outside the buffer on the way to refusing - which a return
/// value alone cannot show.
/// </summary>
public class BitstreamTruncationTests
{
    /// <summary>
    /// A NAL header with three bytes where a whole SPS should be, and then a NAL header with
    /// nothing behind it at all. Both must be refused rather than parsed out of whatever follows.
    /// </summary>
    [Fact]
    public void ATruncatedH264HeaderIsRefused()
    {
        using var bs = new Bitstream(ChiakiCodec.H264);

        Assert.False(bs.ReadHeader([0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1e]));
        Assert.False(bs.ReadHeader([0x00, 0x00, 0x00, 0x01, 0x67]));
    }

    /// <summary>A startcode and an IDR NAL type, with no slice header behind them.</summary>
    [Fact]
    public void ATruncatedH264SliceIsRefused()
    {
        using var bs = new Bitstream(ChiakiCodec.H264);
        Assert.Null(bs.ReadSlice([0x00, 0x00, 0x00, 0x01, 0x65]));
    }

    /// <summary>
    /// Five bytes of H265 where a slice should be. The assertion is that it RETURNS - the value is
    /// incidental, and the failure this guards is a reader that spins on an exhausted buffer.
    ///
    /// Bounded on a thread of its own, because the failure mode is a hang and a test run that
    /// hangs reports nothing at all. That lesson cost this port two suites.
    /// </summary>
    [Fact]
    public void AnExhaustedH265ReaderTerminates()
    {
        var done = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            using var bs = new Bitstream(ChiakiCodec.H265);
            bs.ReadSlice([0x00, 0x00, 0x00, 0x01, 0x02]);
            done.Set();
        })
        { IsBackground = true };

        worker.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "the H265 reader did not return within 5s");
    }

    /// <summary>
    /// Every prefix of a real P slice, handed to the reference rewriter, with the bytes past the
    /// declared length asserted untouched.
    ///
    /// This is the one worth having. The C's version puts the slice inside a 0xa5 arena and checks
    /// both sides of it; here the array IS the arena, so what is covered is everything after the
    /// length and not the padding before it - a write before the pointer is not reachable from
    /// managed code without unsafe arithmetic that would prove less than it costs.
    ///
    /// The return value is deliberately not asserted. Whether a given prefix is a legal P slice is
    /// not this test's business; where the writes land is.
    /// </summary>
    [Theory]
    [MemberData(nameof(PrefixLengths))]
    public void RewritingATruncatedH265SliceWritesNothingPastItsLength(int n)
    {
        byte[] full =
        [
            0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd2, 0x85, 0x7a, 0xaa, 0xa6, 0x08, 0x60, 0x13, 0x55, 0x17,
            0x6b, 0x71, 0x72, 0xf9, 0x6e, 0xd4, 0xf2, 0x66, 0x78, 0x0c, 0x12, 0xe7, 0x79, 0xf0, 0xbc, 0xc9,
        ];

        // The slice, then a tail of sentinel bytes the parser has no business reaching.
        const int Tail = 16;
        var arena = new byte[n + Tail];
        Array.Fill(arena, (byte)0xa5);
        full.AsSpan(0, n).CopyTo(arena);

        using var bs = new Bitstream(ChiakiCodec.H265);
        bs.SetReferenceFrame(arena, n, 0);

        for (int i = n; i < arena.Length; i++)
            Assert.Equal(0xa5, arena[i]);
    }

    /// <summary>
    /// Eight bytes is the one length where the guard changes anything, and there the refusal has
    /// to be clean: it must say no AND leave the eight bytes as it found them.
    /// </summary>
    [Fact]
    public void TheEightByteSliceIsRefusedWithoutBeingWritten()
    {
        byte[] eight = [0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd2, 0x85];
        byte[] copy = (byte[])eight.Clone();

        using var bs = new Bitstream(ChiakiCodec.H265);
        Assert.False(bs.SetReferenceFrame(eight, eight.Length, 0));
        Assert.Equal(copy, eight);
    }

    public static TheoryData<int> PrefixLengths()
    {
        var data = new TheoryData<int>();
        for (int n = 1; n <= 32; n++)
            data.Add(n);
        return data;
    }
}
