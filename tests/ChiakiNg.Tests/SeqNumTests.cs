using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the managed serial-number comparison, held against libchiaki's across the whole domain.
///
/// This module is small enough to be checked properly rather than sampled, which is what makes it
/// worth taking first: the 16-bit pair is compared at every one of the 65536 starting values, for
/// every delta that matters, against the C functions through the shim. A rewrite of these two lines
/// is either exactly right or wrong on a set of inputs a stream reaches in minutes.
/// </summary>
public class SeqNumTests
{
    /// <summary>
    /// Every delta a boundary lives on, plus a few ordinary ones. 0x8000 is the antipode, which is
    /// the case both comparisons answer false for.
    /// </summary>
    private static readonly int[] deltas =
    [
        0, 1, 2, 7, 0x7ffe, 0x7fff, 0x8000, 0x8001, 0x8002, 0xfffe, 0xffff,
    ];

    /// <summary>
    /// The whole 16-bit domain, at every interesting delta: 65536 x 11 pairs, each compared with
    /// libchiaki. Not a sample - if the managed version disagrees anywhere in this space, this
    /// fails.
    /// </summary>
    [Fact]
    public void The16BitDomainAgreesWithLibchiakiEverywhere()
    {
        var mismatches = new List<string>();

        foreach (int delta in deltas)
        {
            for (int i = 0; i <= ushort.MaxValue; i++)
            {
                ushort a = (ushort)i;
                ushort b = (ushort)(i + delta);

                bool lt = SeqNum.Lt(a, b);
                bool gt = SeqNum.Gt(a, b);
                bool nativeLt = NativeSeqNum.Lt(a, b);
                bool nativeGt = NativeSeqNum.Gt(a, b);

                if (lt != nativeLt || gt != nativeGt)
                {
                    mismatches.Add(
                        $"a={a:x4} b={b:x4} lt={lt}/{nativeLt} gt={gt}/{nativeGt}");
                    if (mismatches.Count >= 10)
                        break;
                }
            }
        }

        Assert.Empty(mismatches);
    }

    /// <summary>
    /// The 32-bit pair cannot be walked exhaustively, so it is walked at the boundaries: every
    /// delta above applied around the values where the wider subtraction would overflow if it were
    /// done at the counter's own width.
    /// </summary>
    [Fact]
    public void The32BitPairAgreesAtEveryBoundary()
    {
        uint[] anchors =
        [
            0, 1, 2, 0x7ffffffe, 0x7fffffff, 0x80000000, 0x80000001,
            0xfffffffd, 0xfffffffe, 0xffffffff,
        ];

        long[] wideDeltas =
        [
            0, 1, 2, 0x7ffffffe, 0x7fffffff, 0x80000000, 0x80000001, 0xfffffffe, 0xffffffff,
        ];

        foreach (uint a in anchors)
        {
            foreach (long delta in wideDeltas)
            {
                uint b = unchecked((uint)(a + delta));

                Assert.Equal(NativeSeqNum.Lt(a, b), SeqNum.Lt(a, b));
                Assert.Equal(NativeSeqNum.Gt(a, b), SeqNum.Gt(a, b));

                // And the other direction, since the two branches are not symmetric in the source.
                Assert.Equal(NativeSeqNum.Lt(b, a), SeqNum.Lt(b, a));
                Assert.Equal(NativeSeqNum.Gt(b, a), SeqNum.Gt(b, a));
            }
        }
    }

    /// <summary>
    /// The finding. At exactly half the space apart, BOTH comparisons are false - RFC 1982's
    /// undefined case, resolved to "neither". So Gt is not the negation of Lt, and a port that
    /// defined one from the other would differ from libchiaki on 65536 pairs.
    /// </summary>
    [Fact]
    public void AtTheAntipodeNeitherIsOlderNorNewer()
    {
        for (int i = 0; i <= ushort.MaxValue; i++)
        {
            ushort a = (ushort)i;
            ushort b = (ushort)(i + SeqNum.HalfSpace16);

            Assert.False(SeqNum.Lt(a, b));
            Assert.False(SeqNum.Gt(a, b));
            Assert.False(NativeSeqNum.Lt(a, b));
            Assert.False(NativeSeqNum.Gt(a, b));
            Assert.True(SeqNum.Incomparable(a, b));
        }
    }

    /// <summary>
    /// Stated as the trap rather than as the property: the tempting definition of Gt is wrong, and
    /// wrong on precisely the antipodal pairs and nowhere else.
    /// </summary>
    [Fact]
    public void GtIsNotTheNegationOfLt()
    {
        int disagreements = 0;

        for (int i = 0; i <= ushort.MaxValue; i++)
        {
            ushort a = 0x1234;
            ushort b = (ushort)i;

            bool tempting = !SeqNum.Lt(a, b) && a != b;
            if (tempting != SeqNum.Gt(a, b))
            {
                disagreements++;
                Assert.True(SeqNum.Incomparable(a, b), $"disagreed at b={b:x4} without being antipodal");
            }
        }

        // Exactly one b in the whole space is the antipode of a given a.
        Assert.Equal(1, disagreements);
    }

    /// <summary>
    /// And the antipode is the only incomparable distance - one pair per starting value, so the
    /// case is rare enough to survive a rewrite unnoticed and common enough for a stream to reach.
    /// </summary>
    [Fact]
    public void OnlyTheAntipodeIsIncomparable()
    {
        ushort a = 0x8000;
        int incomparable = 0;

        for (int i = 0; i <= ushort.MaxValue; i++)
        {
            ushort b = (ushort)i;
            bool neither = !SeqNum.Lt(a, b) && !SeqNum.Gt(a, b);

            if (neither && a != b)
                incomparable++;
        }

        Assert.Equal(1, incomparable);
    }

    /// <summary>
    /// The wrap itself, which is the whole reason these functions exist: 1 is newer than 0xfff5
    /// even though the integer is smaller.
    /// </summary>
    [Fact]
    public void TheWrapIsWhatOrdersThem()
    {
        Assert.True(SeqNum.Gt((ushort)1, (ushort)0xfff5));
        Assert.False(SeqNum.Gt((ushort)0xfff5, (ushort)1));
        Assert.True(SeqNum.Lt((ushort)0xfff5, (ushort)1));

        Assert.True(SeqNum.Gt(1u, 0xfffffff5u));
        Assert.False(SeqNum.Gt(0xfffffff5u, 1u));
        Assert.True(SeqNum.Lt(0xfffffff5u, 1u));

        // Which is exactly where a naive `a < b` is wrong, and the assertion says so.
        Assert.NotEqual((ushort)1 > (ushort)0xfff5, SeqNum.Gt((ushort)1, (ushort)0xfff5));
    }

    /// <summary>Equality is neither, at both widths.</summary>
    [Fact]
    public void EqualIsNeitherOlderNorNewer()
    {
        Assert.False(SeqNum.Lt((ushort)42, (ushort)42));
        Assert.False(SeqNum.Gt((ushort)42, (ushort)42));
        Assert.False(SeqNum.Lt(42u, 42u));
        Assert.False(SeqNum.Gt(42u, 42u));
        Assert.False(SeqNum.Incomparable((ushort)42, (ushort)42));
    }

    /// <summary>
    /// Away from the wrap the comparison is ordinary, which is the half that lets a broken rewrite
    /// pass a hand-written test: 65535 of every 65536 packets take this path.
    /// </summary>
    [Fact]
    public void NearZeroItBehavesLikeOrdinaryComparison()
    {
        for (ushort a = 0; a < 200; a++)
        {
            for (ushort b = 0; b < 200; b++)
            {
                Assert.Equal(a < b, SeqNum.Lt(a, b));
                Assert.Equal(a > b, SeqNum.Gt(a, b));
            }
        }
    }
}
