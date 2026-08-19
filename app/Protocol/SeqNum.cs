namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: RFC 1982 serial number comparison, which is the arithmetic the whole transport rests on.
///
/// Sequence numbers wrap. 0xffff is followed by 0, so a packet numbered 1 is NEWER than one
/// numbered 0xfff5 even though the integer is smaller. Every reorder decision, every duplicate
/// check and every "have I seen this already" in takion is one of these two comparisons.
///
/// A rewrite spells them <c>a &lt; b</c> and is right for 65535 of every 65536 packets - the counter
/// turns over once, and the queue starts discarding everything as stale while the picture freezes.
/// At a stream's packet rate that is minutes, not weeks, and the symptom points at the network.
///
/// This is now managed rather than a call across the seam, and it is the module PP23's harness was
/// built for: <see cref="NativeSeqNum"/> is the same four functions in C, and the tests compare the
/// two across the whole 16-bit domain at every boundary rather than at a handful of examples. Three
/// things had to be transcribed rather than chosen:
///
///   the difference is computed in a WIDER SIGNED type. seqnum.h's macro takes `greater_sint` -
///   int32_t for the 16-bit pair and int64_t for the 32-bit one - so `b - a` cannot wrap. A port
///   subtracting at the counter's own width gets a value that already wrapped and then compares it
///   against half the space, which is a different function;
///
///   the half-space bound is exclusive on BOTH sides. At a distance of exactly 2^(bits-1) neither
///   `lt` nor `gt` is true, which is RFC 1982's undefined case resolved to "neither". So
///   <c>Gt(a, b)</c> is NOT <c>!Lt(a, b) &amp;&amp; a != b</c>, and a port that defined one from the
///   other differs from libchiaki on exactly the pairs at the antipode;
///
///   and equality is checked first. It is redundant - a == b falls through both branches to false
///   anyway - and it is reproduced because a reader comparing the two files should not have to
///   prove that.
/// </summary>
public static class SeqNum
{
    /// <summary>Half the 16-bit sequence space. The bound is exclusive on both sides.</summary>
    public const int HalfSpace16 = 1 << 15;

    /// <summary>Half the 32-bit sequence space.</summary>
    public const long HalfSpace32 = 1L << 31;

    /// <summary>Whether <paramref name="a"/> is older than <paramref name="b"/>, wrap included.</summary>
    public static bool Lt(ushort a, ushort b)
    {
        if (a == b)
            return false;

        // int, not ushort: the C macro widens to int32_t so the subtraction cannot wrap.
        int d = b - a;
        return (a < b && d < HalfSpace16)
            || (a > b && -d > HalfSpace16);
    }

    /// <summary>Whether <paramref name="a"/> is newer than <paramref name="b"/>, wrap included.</summary>
    public static bool Gt(ushort a, ushort b)
    {
        if (a == b)
            return false;

        int d = b - a;
        return (a < b && d > HalfSpace16)
            || (a > b && -d < HalfSpace16);
    }

    /// <summary>The 32-bit pair, widened to long for the same reason.</summary>
    public static bool Lt(uint a, uint b)
    {
        if (a == b)
            return false;

        long d = (long)b - a;
        return (a < b && d < HalfSpace32)
            || (a > b && -d > HalfSpace32);
    }

    /// <summary>The 32-bit pair, widened to long for the same reason.</summary>
    public static bool Gt(uint a, uint b)
    {
        if (a == b)
            return false;

        long d = (long)b - a;
        return (a < b && d > HalfSpace32)
            || (a > b && -d < HalfSpace32);
    }

    /// <summary>
    /// Whether the two are exactly half the space apart, which is the case both comparisons answer
    /// false for.
    ///
    /// Named because it is the one input a caller might reasonably want to know about: at this
    /// distance "older" and "newer" are both false and the pair is indistinguishable from equal, so
    /// a caller deciding whether to discard a packet has no ordering to decide with.
    /// </summary>
    public static bool Incomparable(ushort a, ushort b)
    {
        int d = b - a;
        return d == HalfSpace16 || -d == HalfSpace16;
    }

    /// <summary>The same, at the wider width.</summary>
    public static bool Incomparable(uint a, uint b)
    {
        long d = (long)b - a;
        return d == HalfSpace32 || -d == HalfSpace32;
    }
}
