using System.Runtime.CompilerServices;

namespace AllocBudget;

/// <summary>
/// PP44: bytes allocated per packet processed, over a replayed capture, failing when it rises
/// above the number that was agreed.
///
/// The number is 0, and it is not a wish: test/allocbudget.c measures the C receive-and-reassemble
/// path over the same captured packet and finds it allocates nothing per packet in steady state -
/// buffers are sized once from the frame's own header and then reused. So the bar the managed
/// transport inherits is the bar the code it replaces already meets.
///
/// What is under test here is a deliberately minimal reference parse, not a transport. PP27 writes
/// the real one and replaces <see cref="TakionAvHeader"/>'s caller; what it must not do is replace
/// this assertion. A gate that only exists once the thing it gates is written is a gate that gets
/// written to pass.
///
/// Exit code 0 means every budget held. Non-zero names the one that did not.
/// </summary>
internal static class Program
{
    /// <summary>Bytes the managed path may allocate per packet processed, in steady state.</summary>
    private const long BudgetBytesPerPacket = 0;

    private const int WarmupPackets = 1_000;
    private const int MeasuredPackets = 100_000;

    private static int Main()
    {
        int failures = 0;

        // The instrument before the measurement. A counter that cannot see an allocation reports
        // zero for every path, and that reads exactly like success - which is the failure mode this
        // whole task exists to prevent, so it is checked rather than assumed.
        if (!CounterSeesAnAllocation())
        {
            Console.Error.WriteLine("FAIL instrument: GC.GetAllocatedBytesForCurrentThread did not observe a known allocation");
            failures++;
        }

        if (!ParsesTheCapture())
        {
            Console.Error.WriteLine("FAIL capture: the reference parse did not read the captured packet's known fields");
            failures++;
        }

        long perPacket = MeasureBytesPerPacket();
        Console.WriteLine($"replayed {MeasuredPackets} packets ({WarmupPackets} warmup discarded)");
        Console.WriteLine($"allocated {perPacket * MeasuredPackets} bytes total, {perPacket} bytes per packet, budget {BudgetBytesPerPacket}");

        if (perPacket > BudgetBytesPerPacket)
        {
            Console.Error.WriteLine($"FAIL budget: {perPacket} bytes per packet exceeds the budget of {BudgetBytesPerPacket}");
            failures++;
        }

        Console.WriteLine(failures == 0 ? "OK all budgets held" : $"{failures} budget(s) broken");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Replay the capture and charge every byte the thread allocates. Nothing is pooled here
    /// because nothing is allocated here: the packet is a span over a buffer that already exists,
    /// which is the shape ArrayPool and the async socket APIs are for in the real transport.
    /// </summary>
    private static long MeasureBytesPerPacket()
    {
        ReadOnlySpan<byte> packet = Capture.RealVideoPacket;

        for (int i = 0; i < WarmupPackets; i++)
            Consume(packet);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < MeasuredPackets; i++)
            Consume(packet);
        long after = GC.GetAllocatedBytesForCurrentThread();

        return (after - before) / MeasuredPackets;
    }

    /// <summary>
    /// One packet's worth of work: parse the header, then touch the payload so neither the parse
    /// nor the payload can be optimised away. A gate that is fast and allocation-free because the
    /// JIT deleted the work is not a gate.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume(ReadOnlySpan<byte> packet)
    {
        if (!TakionAvHeader.TryParse(packet, out TakionAvHeader header))
            throw new InvalidOperationException("the captured packet stopped parsing");

        ReadOnlySpan<byte> payload = header.Payload(packet);
        Sink += payload.Length == 0 ? 0 : payload[0] + payload[^1] + header.FrameIndex;
    }

    /// <summary>Written so the work above has an observable effect and survives optimisation.</summary>
    internal static long Sink;

    private static bool CounterSeesAnAllocation()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        byte[] deliberate = AllocateDeliberately(4096);
        long after = GC.GetAllocatedBytesForCurrentThread();
        // Kept alive past the second read so the allocation cannot be elided.
        Sink += deliberate.Length;
        return after - before >= 4096;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] AllocateDeliberately(int size) => new byte[size];

    private static bool ParsesTheCapture()
    {
        if (!TakionAvHeader.TryParse(Capture.RealVideoPacket, out TakionAvHeader h))
            return false;
        // The values test/takion.c asserts for this same packet, so a parse that drifts is caught
        // here rather than producing an allocation number for the wrong bytes.
        return h.IsVideo && h.PacketIndex == 45 && h.FrameIndex == 5;
    }
}
