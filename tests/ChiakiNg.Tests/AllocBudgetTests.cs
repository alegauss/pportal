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

        // PP670: the processor replayed here is the C's through the shim.
        if (ShimFramePathShape.WrappingHeader() is null)
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

    /// <summary>
    /// PP489: and the OTHER budget in that file, which is not zero.
    ///
    /// PP59 measured takion's receive step at three allocator calls per packet - the 1500-byte
    /// buffer, the realloc down to what arrived, and the queue entry. Read rather than remembered,
    /// and asserted to be above zero, because the whole comparison below is with a number that has
    /// something in it.
    /// </summary>
    [Fact]
    public void TheReceiveStepBudgetIsTheCSuitesOwnAndIsNotZero()
    {
        if (AllocBudgetSource.Locate() is not { } path)
            return;

        string text = File.ReadAllText(path);

        Assert.Equal(3, AllocBudgetSource.RecvCallsPerPacket(text));
        Assert.True(AllocBudgetSource.RecvCallsPerPacket(text) > 0);
    }

    /// <summary>
    /// PP489: the managed receive step costs none of those three, in steady state.
    ///
    /// The buffer is PP485's, rented once; there is no realloc because a Span carries its own
    /// length; and the queue's window is a bool[] and a long[] sized once in its constructor, so a
    /// push writes into them rather than allocating an entry. Measured over the same 200 packets the
    /// C's harness replays.
    ///
    /// STEADY STATE MEANS NO DROPS, and that is a real qualification rather than a convenience -
    /// see the test below, which is the case this one does not cover.
    /// </summary>
    [Fact]
    public void TheManagedReceiveStepCostsNoneOfTheCsThree()
    {
        using var buffer = new TakionReceiveBuffer();
        using var queue = new ReorderQueue(4, 0);

        long sink = 0;

        // Warm the pool, the JIT and the queue's own arrays, none of which is a per-packet cost -
        // the C's harness exempts its first packet for the same reason.
        for (int i = 0; i < 64; i++)
        {
            buffer.Received(153);
            queue.Push((ulong)i, buffer.Length);
            sink += queue.Pull()?.Payload ?? 0;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < Frames; i++)
        {
            buffer.Free[0] = 0x02;
            buffer.Received(153);
            queue.Push((ulong)(64 + i), buffer.Length);
            sink += queue.Pull()?.Payload ?? 0;
        }

        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(sink > 0);
        Assert.Equal(0, delta);
        Assert.Empty(queue.Drops);
    }

    /// <summary>
    /// PP489: BUT DROPS ARE RECORDED WITHOUT BOUND, and a reorder queue exists because packets drop.
    ///
    /// The managed queue appends every drop to a List that nothing clears, so the zero above holds on
    /// the path where nothing is lost and not on the path the module is for. The C's drop hands the
    /// element to a callback and forgets it: no allocation, and nothing retained.
    ///
    /// The cost is AMORTISED rather than per-drop - a List grows geometrically, so one drop into
    /// spare capacity is free, which is what the first version of this test got wrong. What is not
    /// amortised is the retention: the list only grows, for the life of the queue.
    /// </summary>
    [Fact]
    public void DropsAreRecordedWithoutBoundWhereTheCsDropForgets()
    {
        using var queue = new ReorderQueue(4, 0);

        // Sixteen slots and nothing pulling them, so the window fills and every push after that is
        // past its end - which the default strategy drops.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i <= 2000; i++)
            queue.Push((ulong)i, i);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        int recorded = queue.Drops.Count;
        Assert.True(recorded > 1000, $"only {recorded} drops were recorded");

        Assert.True(
            delta > 0,
            $"{recorded} drops were recorded and the list grew, so something was allocated; got {delta}");

        // And nothing clears it: a second pass only adds, for the life of the queue.
        for (int i = 2001; i <= 3000; i++)
            queue.Push((ulong)i, i);

        Assert.True(
            queue.Drops.Count > recorded,
            "the record of drops only grows - there is no clear and no bound");
    }

    /// <summary>
    /// PP489: 1500 is written in three places, and they are one number.
    ///
    /// takion.c sets received_size to it, allocbudget.c declares its own copy so the harness can
    /// allocate the same thing, and TakionReceiveBuffer carries it as the ceiling it rents. The
    /// harness measures its own replay, so a takion.c that moved would leave the C's budget green
    /// against a size the C had stopped using - and PP485's buffer would be the third opinion.
    /// </summary>
    [Fact]
    public void TheBufferSizeIsOneNumberInThreePlaces()
    {
        if (AllocBudgetSource.Locate() is not { } budgetPath
            || TakionReceiveBuffer.LocateTakion() is not { } takionPath)
        {
            return;
        }

        long inTheHarness = AllocBudgetSource.RecvBufferInitialSize(File.ReadAllText(budgetPath));
        int? inTakion = TakionReceiveBuffer.CapacityInTheC(File.ReadAllText(takionPath));

        Assert.Equal(TakionReceiveBuffer.DatagramCapacity, (int)inTheHarness);
        Assert.Equal(TakionReceiveBuffer.DatagramCapacity, inTakion);
    }
}
