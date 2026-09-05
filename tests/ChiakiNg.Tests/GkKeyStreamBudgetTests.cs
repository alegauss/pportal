using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP737: what one key stream costs, measured before anything was decided about it.
///
/// PP731 left the C's key buffer out on the grounds that it is a cache - true about the bytes, and
/// silent about what producing them costs. PP44's budget names this exact case: thousands of small
/// packets a second, each an allocation if written carelessly, with Span named as the answer.
///
/// THE NUMBER CAME FIRST. The byte[] path allocated the stream, and a fresh sixteen-byte counter
/// block for every block it wrote - so the per-packet cost grew with the packet. The span overloads
/// take both to zero. What is LEFT is the Aes, which holds a key schedule and a native handle, and
/// which stays per-call because caching it would give the crypt something to release - the lifetime
/// PP731 decided against, on its own reasoning about the C's thread.
///
/// The counter here is GC.GetAllocatedBytesForCurrentThread, as AllocBudgetTests uses: it counts
/// what this thread asked for rather than what survived, which is the question a budget asks.
/// </summary>
public class GkKeyStreamBudgetTests(ITestOutputHelper output)
{
    /// <summary>A typical AV payload, and what the per-block cost used to scale with.</summary>
    private const int PacketBytes = 1408;

    private static readonly byte[] Key =
        [.. Enumerable.Range(0, GkKeyStream.BlockSize).Select(one => (byte)(one + 1))];

    private static readonly byte[] Iv =
        [.. Enumerable.Range(0, GkKeyStream.BlockSize).Select(one => (byte)(0x80 + one))];

    private static long Measure(Action once)
    {
        // Once to page in whatever the first call sets up, then the measured run.
        once();

        long before = GC.GetAllocatedBytesForCurrentThread();
        once();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// THE SPAN PATH ALLOCATES NEITHER THE STREAM NOR A BLOCK PER BLOCK.
    ///
    /// What remains is the Aes and its key copy, which do not grow with the packet - so the cost is
    /// per CALL rather than per block, and that is the whole of the improvement. Asserted as a
    /// ceiling rather than a figure, because the runtime's own allocation for a cipher handle is
    /// not this port's number to fix.
    /// </summary>
    [Fact]
    public void TheSpanPathDoesNotGrowWithThePacket()
    {
        byte[] small = new byte[GkKeyStream.BlockSize];
        byte[] large = new byte[PacketBytes];

        long oneBlock = Measure(() => GkKeyStream.Generate(Key, Iv, 0, small));
        long manyBlocks = Measure(() => GkKeyStream.Generate(Key, Iv, 0, large));

        output.WriteLine($"one block {oneBlock} bytes, {PacketBytes / GkKeyStream.BlockSize} blocks {manyBlocks} bytes");

        // The packet is eighty-eight blocks. If a block still allocated, the second number would be
        // the first plus eighty-seven blocks' worth.
        Assert.Equal(oneBlock, manyBlocks);
    }

    /// <summary>
    /// And the byte[] overload costs the stream itself, which is what a caller pays for asking.
    ///
    /// Kept rather than removed: PP416's own tests and every non-packet caller read better with it,
    /// and the difference between the two is now a number instead of a preference.
    /// </summary>
    [Fact]
    public void TheArrayOverloadCostsTheStreamOnTop()
    {
        byte[] into = new byte[PacketBytes];

        long span = Measure(() => GkKeyStream.Generate(Key, Iv, 0, into));
        long array = Measure(() => GkKeyStream.Generate(Key, Iv, 0, PacketBytes));

        output.WriteLine($"span {span} bytes, array {array} bytes, difference {array - span}");

        Assert.True(
            array - span >= PacketBytes,
            $"the array overload cost {array - span} more, and the stream alone is {PacketBytes}");
    }

    /// <summary>
    /// The two overloads produce the same bytes, which is what makes the cheaper one usable.
    ///
    /// PP416's vectors hold the CONTENT against the C; this holds the two signatures against each
    /// other, so a span path that drifted would be caught without re-deriving the oracle.
    /// </summary>
    [Theory]
    [InlineData(0UL, GkKeyStream.BlockSize)]
    [InlineData(0UL, PacketBytes)]
    [InlineData(45000UL - 8, GkKeyStream.BlockSize * 4)]
    public void BothOverloadsAgree(ulong keyPos, int length)
    {
        // The key position has to be a whole number of blocks, as both overloads require.
        ulong aligned = keyPos - (keyPos % GkKeyStream.BlockSize);

        byte[] into = new byte[length];
        GkKeyStream.Generate(Key, Iv, aligned, into);

        Assert.Equal(GkKeyStream.Generate(Key, Iv, aligned, length), into);
    }

    /// <summary>And the counter overloads agree too, block for block.</summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(ulong.MaxValue)]
    public void BothCounterOverloadsAgree(ulong value)
    {
        Span<byte> into = stackalloc byte[GkKeyStream.BlockSize];
        GkKeyStream.CounterAdd(Iv, value, into);

        Assert.Equal(GkKeyStream.CounterAdd(Iv, value), into.ToArray());
    }

    /// <summary>A span of the wrong size is refused rather than half filled.</summary>
    [Fact]
    public void AMisSizedDestinationIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GkKeyStream.Generate(Key, Iv, 0, new byte[GkKeyStream.BlockSize + 1]));

        Assert.Throws<ArgumentException>(
            () => GkKeyStream.CounterAdd(Iv, 1, new byte[GkKeyStream.BlockSize - 1]));
    }

    /// <summary>The crypt's own overload carries the saving through to a caller of PP731's object.</summary>
    [Fact]
    public void TheCryptsSpanOverloadCarriesItThrough()
    {
        ManagedGkCrypt crypt = ManagedGkCrypt.Derive(
            2,
            [.. Enumerable.Range(0, GkDerivation.HandshakeKeySize).Select(one => (byte)one)],
            [.. Enumerable.Range(0, GkDerivation.EcdhSecretSize).Select(one => (byte)(one * 3))]);

        byte[] into = new byte[PacketBytes];
        crypt.KeyStream(0, into);

        Assert.Equal(crypt.KeyStream(0, PacketBytes), into);
    }
}
