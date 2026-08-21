using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the allocation budget the C suite holds, held on the managed side too.
///
/// test/allocbudget.c is not a test of a module. It replays the captured video packet through the
/// frame processor two hundred times with a counting allocator installed, and asserts that the
/// steady state allocates ZERO bytes in ZERO calls per packet - the first frame is exempt, because
/// that is where the buffers are sized.
///
/// It is the one case in that suite the port did not carry, and it is the one a managed rewrite is
/// most likely to lose. C allocates when it is told to; C# allocates when it is not looking - a
/// byte[] per unit, a lambda capturing a local, a params array - and none of that fails a
/// correctness test. It fails at sixty frames a second, as a collection in the middle of a stream,
/// and it is reported as stutter rather than as a bug in a parser.
///
/// The counter here is <see cref="GC.GetAllocatedBytesForCurrentThread"/> rather than an allocator
/// hook: the managed equivalent of the C's counting malloc, exact for this thread, and it counts
/// what the C's cannot - allocations the runtime makes on this code's behalf.
///
/// What is NOT claimed is that the native side allocates nothing; that is the C's own test and it
/// still runs. What is claimed is that the SEAM adds nothing, which is the half this port owns.
/// </summary>
public class AllocBudgetTests
{
    /// <summary>The budget, from CHIAKI_ALLOC_BUDGET_BYTES_PER_PACKET and its sibling.</summary>
    private const long BytesPerPacket = 0;

    /// <summary>How many frames the C replays, so the two suites are charged alike.</summary>
    private const int Frames = 200;

    private static VideoPacketCase? Capture()
    {
        string? path = VideoStreamVectors.Locate();
        return path is null ? null : VideoStreamVectors.Parse(path).FirstOrDefault();
    }

    /// <summary>
    /// One frame replayed the way an arriving one is: allocate on unit zero, then put every source
    /// unit, then flush. The same shape as the C's replay_one_frame, using the span overloads -
    /// which is the point, since the byte[] ones would allocate by signature.
    /// </summary>
    private static void ReplayOneFrame(FrameProcessor processor, ReadOnlySpan<byte> nalu, Span<byte> sink)
    {
        const ushort total = 8;
        const ushort fec = 1;
        const ushort source = total - fec;

        processor.AllocFrameUnits(frameIndex: 5, unitIndex: 0, total, fec, nalu);

        for (ushort i = 0; i < source; i++)
            processor.PutUnit(frameIndex: 5, unitIndex: i, total, fec, nalu);

        processor.FlushInto(sink, out _);
    }

    /// <summary>
    /// The gate. Two hundred frames after a warm-up, and nothing allocated on this thread.
    /// </summary>
    [Fact]
    public void ReplayingTheCapturedFrameAllocatesNothing()
    {
        VideoPacketCase? capture = Capture();
        if (capture is null)
            return;

        byte[] nalu = capture.Value.Nalu;
        Assert.NotEmpty(nalu);

        using var processor = new FrameProcessor();

        // Sized here, and deliberately outside the count - the C exempts its first frame for the
        // same reason.
        byte[] sink = new byte[1 << 20];
        ReplayOneFrame(processor, nalu, sink);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int f = 0; f < Frames; f++)
            ReplayOneFrame(processor, nalu, sink);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        const int source = 7;
        long packets = (long)Frames * source;

        Assert.True(
            allocated / packets <= BytesPerPacket,
            $"{allocated} bytes over {packets} packets - the budget is {BytesPerPacket} per packet");

        // Stated absolutely as well, so the division cannot hide a handful of large allocations
        // behind a big packet count. The C's own test does the same.
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// The counter has to be able to see an allocation, or the gate above passes by being blind.
    /// The C suite carries this check for its own counter and the reason is the same here: a
    /// measurement that cannot fail is not a measurement.
    /// </summary>
    [Fact]
    public void TheCounterSeesAnAllocation()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();

        byte[] seen = new byte[4096];

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotEmpty(seen);
        Assert.True(allocated >= 4096, $"the counter reported {allocated} for a 4 KiB array");
    }

    /// <summary>
    /// And the budget the C states is still zero on both axes. Read out of its own header rather
    /// than remembered, so a suite that relaxed its budget does not leave this one asserting an
    /// older, stricter number the other client no longer holds.
    /// </summary>
    [Fact]
    public void TheBudgetIsStillTheCSuitesOwn()
    {
        string? path = AllocBudgetSource.Locate();
        if (path is null)
            return;

        string text = File.ReadAllText(path);

        Assert.Equal(BytesPerPacket, AllocBudgetSource.BytesPerPacket(text));
        Assert.Equal(0, AllocBudgetSource.CallsPerPacket(text));
    }
}
