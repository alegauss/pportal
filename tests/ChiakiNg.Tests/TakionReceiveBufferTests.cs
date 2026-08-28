using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP485, under PP27: the allocation §PP27 says is the block's one real runtime question.
///
/// takion.c mallocs 1500 bytes per datagram and then reallocs the block down to what arrived - two
/// heap operations per packet, thousands of packets a second, on the thread the whole stream rides
/// on, with upstream's own "TODO: no malloc?" on the first of them. The managed path rents once and
/// reuses, so the number below is zero.
/// </summary>
public class TakionReceiveBufferTests
{
    /// <summary>
    /// THE GATE: ten thousand datagrams through the receive path allocate nothing at all.
    ///
    /// Measured rather than argued, because "should not allocate" is the kind of claim that stays
    /// true until someone adds a ToArray. Exactly zero and not a budget: there is nothing on this
    /// path that has any business allocating, so a byte of drift is a change worth reading.
    /// </summary>
    [Fact]
    public void ReceivingDatagramsAllocatesNothingPerPacket()
    {
        using var buffer = new TakionReceiveBuffer();

        // Warm the pool and the JIT first - the first pass through anything allocates.
        long sink = 0;
        for (int i = 0; i < 256; i++)
        {
            buffer.Free[0] = (byte)i;
            buffer.Received(64);
            sink += buffer.Datagram[0];
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
        {
            Span<byte> free = buffer.Free;
            free[0] = (byte)i;
            buffer.Received(1 + (i % TakionReceiveBuffer.DatagramCapacity));
            sink += buffer.Datagram[0] + buffer.Length;
        }

        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(sink > 0);
        Assert.Equal(0L, delta);
    }

    /// <summary>
    /// And the instrument is live: the one allocation this class DOES make shows up in the same
    /// counter, measured the same way.
    ///
    /// Without this, the zero above would be worth nothing - a counter that cannot see a byte reports
    /// zero for a path that allocates freely, and the gate would pass by being blind rather than by
    /// being right.
    /// </summary>
    [Fact]
    public void TheSameCounterSeesTheOneAllocationRetainMakes()
    {
        using var buffer = new TakionReceiveBuffer();
        buffer.Received(512);
        _ = buffer.Retain();

        long before = GC.GetAllocatedBytesForCurrentThread();
        byte[] kept = buffer.Retain();
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(512, kept.Length);
        Assert.True(delta >= 512, $"the counter reported {delta} bytes for a 512-byte copy");
    }

    /// <summary>
    /// The ceiling is the one the C declares, read out of the file rather than agreed by memory.
    /// </summary>
    [Fact]
    public void TheCeilingIsTheOneTheCDeclares()
    {
        if (TakionReceiveBuffer.LocateTakion() is not { } path)
            return;

        Assert.Equal(
            TakionReceiveBuffer.DatagramCapacity,
            TakionReceiveBuffer.CapacityInTheC(File.ReadAllText(path)));
    }

    /// <summary>
    /// And the C still does it the expensive way, which is what makes the sentence above true.
    ///
    /// If upstream ever answers its own TODO, this fails - and it should, because at that moment the
    /// claim about two heap operations per packet stops describing anything.
    /// </summary>
    [Fact]
    public void TheCStillAllocatesOncePerDatagramAndReallocsItDown()
    {
        if (TakionReceiveBuffer.LocateTakion() is not { } path)
            return;

        Assert.True(TakionReceiveBuffer.TheCAllocatesPerDatagram(File.ReadAllText(path)));
    }

    /// <summary>
    /// Retain copies, so the next receive may reuse the buffer under a packet somebody kept.
    ///
    /// This is the postpone path: it holds datagrams until crypt initialises, which is several
    /// receives later. A caller that held the span instead would be reading whatever arrived since.
    /// </summary>
    [Fact]
    public void RetainCopiesSoTheBufferMayBeReusedUnderIt()
    {
        using var buffer = new TakionReceiveBuffer();

        buffer.Free[0] = 0xAB;
        buffer.Free[1] = 0xCD;
        buffer.Received(2);

        byte[] kept = buffer.Retain();

        // The next datagram lands in the same memory.
        buffer.Free[0] = 0x11;
        buffer.Free[1] = 0x22;
        buffer.Received(2);

        Assert.Equal(new byte[] { 0xAB, 0xCD }, kept);
        Assert.Equal((byte)0x11, buffer.Datagram[0]);
    }

    /// <summary>Retain is sized to the datagram, not to the ceiling - the C's realloc, in one line.</summary>
    [Fact]
    public void RetainIsSizedToTheDatagramAndNotTheCeiling()
    {
        using var buffer = new TakionReceiveBuffer();
        buffer.Received(7);

        Assert.Equal(7, buffer.Retain().Length);
        Assert.Equal(7, buffer.Datagram.Length);
    }

    /// <summary>A datagram the C would have truncated is refused rather than accepted.</summary>
    [Fact]
    public void MoreThanTheCeilingIsRefused()
    {
        using var buffer = new TakionReceiveBuffer();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => buffer.Received(TakionReceiveBuffer.DatagramCapacity + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Received(-1));

        // And the ceiling itself is fine, because that is a full datagram and not an overrun.
        buffer.Received(TakionReceiveBuffer.DatagramCapacity);
        Assert.Equal(TakionReceiveBuffer.DatagramCapacity, buffer.Length);
    }

    /// <summary>
    /// Dispose returns the array once however many times it is called.
    ///
    /// Returning one array to an ArrayPool twice gives the same memory to two owners, which no
    /// exception reports - it just corrupts whoever rents next. The loop this sits under has several
    /// exits, so idempotence is the property that matters rather than a guard clause.
    /// </summary>
    [Fact]
    public void DisposeReturnsTheBufferOnceHoweverOftenItIsCalled()
    {
        var buffer = new TakionReceiveBuffer();
        buffer.Received(4);

        buffer.Dispose();
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.Free[0] = 1);
    }

    /// <summary>The reader says null rather than a number when the C stops declaring one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("size_t other_size = 1500;")]
    public void ACeilingTheCNoLongerDeclaresIsNull(string text)
        => Assert.Null(TakionReceiveBuffer.CapacityInTheC(text));
}
