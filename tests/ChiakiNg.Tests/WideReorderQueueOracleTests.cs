using System.Text;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP674: the THIRTY-TWO-BIT queue, stepped against the C's other instantiation.
///
/// reorderqueue.c stamps one body out twice through REORDER_QUEUE_INIT and injects three sequence
/// functions at each width. takion uses both - the video queue at sixteen bits, the DATA queue at
/// thirty-two, seeded with tag_remote - and only the sixteen-bit init had a shim wrapper, so a
/// managed queue had nothing to be held against at the width the data path uses.
///
/// SAME METHOD AS PP23'S, ONE WIDTH OVER. Both implementations stepped over pseudo-random operation
/// sequences with every observable compared after each: count, the drop log with payloads, and a
/// peek at every offset in the window. Fixed seeds, because a differential that only fails on some
/// runs is one nobody can act on.
///
/// AND THE WRAP IS WHERE THE WIDTHS DIVERGE. A sixteen-bit queue seeded near 0xFFFF crosses its
/// boundary in a few hundred operations; a thirty-two-bit one seeded anywhere ordinary never would.
/// So one of the seeds starts near 0xFFFFFF00, where only the wide arithmetic gives the right
/// answer - a queue still casting to ushort would call every one of those pushes a wrap it is not.
/// </summary>
public class WideReorderQueueOracleTests(ITestOutputHelper output)
{
    /// <summary>The same deterministic generator PP23's oracle uses, so a failure reproduces.</summary>
    private sealed class Lcg(uint seed)
    {
        private uint state = seed == 0 ? 1u : seed;

        public uint Next()
        {
            state = (1664525u * state) + 1013904223u;
            return state;
        }

        public int Next(int bound) => (int)(Next() % (uint)bound);
    }

    private static string Describe(
        ReorderQueue managed, NativeReorderQueue native, IReadOnlyList<string> log)
    {
        var text = new StringBuilder();
        text.AppendLine($"after {log.Count} ops:");
        foreach (string line in log.TakeLast(24))
            text.AppendLine("  " + line);

        text.AppendLine($"managed count={managed.Count} drops={managed.Drops.Count}");
        text.AppendLine($"native  count={native.Count} drops={native.Drops.Count}");
        return text.ToString();
    }

    private static void AssertSameState(
        ReorderQueue managed, NativeReorderQueue native, IReadOnlyList<string> log)
    {
        Assert.True(native.Count == managed.Count, Describe(managed, native, log));
        Assert.Equal(native.Size, managed.Size);

        Assert.Equal(native.Drops.Count, managed.Drops.Count);
        for (int i = 0; i < native.Drops.Count; i++)
            Assert.Equal(native.Drops[i], managed.Drops[i]);

        for (ulong offset = 0; offset < native.Count; offset++)
            Assert.Equal(native.Peek(offset), managed.Peek(offset));

        Assert.Equal(native.Peek(native.Count), managed.Peek(managed.Count));
    }

    /// <summary>
    /// Both wide queues agree step for step, including across the thirty-two-bit wrap.
    ///
    /// The last two seeds start near 0xFFFFFF00, which is the only place the width is visible: a
    /// queue doing sixteen-bit arithmetic answers those pushes wrongly and every comparison after
    /// them inherits it.
    /// </summary>
    [Theory]
    [InlineData(1u, 3, ReorderDropStrategy.End, 0x10000000u)]
    [InlineData(2u, 3, ReorderDropStrategy.Begin, 0x10000000u)]
    [InlineData(3u, 4, ReorderDropStrategy.End, 0x7FFFFFF0u)]
    [InlineData(4u, 4, ReorderDropStrategy.Begin, 0x00000010u)]
    [InlineData(5u, 1, ReorderDropStrategy.End, 0x10000000u)]
    [InlineData(6u, 0, ReorderDropStrategy.Begin, 0x10000000u)]
    [InlineData(7u, 3, ReorderDropStrategy.End, 0xFFFFFF00u)]
    [InlineData(8u, 4, ReorderDropStrategy.Begin, 0xFFFFFF00u)]
    public void BothWideQueuesAgreeStepForStep(
        uint seed, int sizeExp, ReorderDropStrategy strategy, uint start)
    {
        using ReorderQueue managed = ReorderQueue.Wide(sizeExp, start);
        using NativeReorderQueue native = NativeReorderQueue.Wide(sizeExp, start);

        managed.DropStrategy = strategy;
        native.DropStrategy = strategy;

        Assert.Equal(ReorderWidth.ThirtyTwo, managed.Width);
        Assert.Equal(ReorderWidth.ThirtyTwo, native.Width);

        var rng = new Lcg(seed);
        var log = new List<string>();
        int window = 1 << sizeExp;
        uint cursor = start;

        for (int step = 0; step < 400; step++)
        {
            int op = rng.Next(10);

            if (op < 6)
            {
                int spread = rng.Next(3) switch
                {
                    0 => rng.Next(window + 2),
                    1 => -rng.Next(window + 2),
                    _ => rng.Next((2 * window) + 4) - window - 2,
                };

                uint seq = (uint)(cursor + spread);
                long payload = (step << 8) | 0x5a;

                log.Add($"push {seq:x8} payload={payload}");
                managed.Push(seq, payload);
                native.Push(seq, payload);
            }
            else if (op < 8)
            {
                log.Add("pull");
                Assert.Equal(native.Pull(), managed.Pull());
            }
            else if (op < 9)
            {
                ulong index = (ulong)rng.Next(window + 1);
                log.Add($"drop offset={index}");
                managed.Drop(index);
                native.Drop(index);
            }
            else
            {
                cursor = (uint)(cursor + rng.Next(window + 1));
                log.Add($"cursor -> {cursor:x8}");
            }

            AssertSameState(managed, native, log);
        }

        output.WriteLine($"seed {seed}: {log.Count} ops, {managed.Drops.Count} drop(s)");
        Assert.Equal(400, log.Count);
    }

    /// <summary>
    /// THE WIDTHS REALLY DIFFER, which is what says the injection did something.
    ///
    /// Two sequence numbers a wrap apart at sixteen bits are not a wrap apart at thirty-two, so the
    /// two comparisons disagree - and a wide queue that had kept the narrow arithmetic would answer
    /// the second like the first.
    /// </summary>
    [Theory]
    [InlineData(0x00010000UL, 0x00000001UL)]
    [InlineData(0xFFFF0000UL, 0x0000FFFFUL)]
    [InlineData(0x00020000UL, 0x00010000UL)]
    public void TheTwoWidthsDisagreeWhereTheyShould(ulong a, ulong b)
    {
        // At sixteen bits both are the same number, so neither is greater.
        Assert.Equal((ushort)a == (ushort)b, !ReorderQueue.SeqNumGt(a, b) && !ReorderQueue.SeqNumGt(b, a));

        // At thirty-two they are different numbers, and exactly one of the two directions holds.
        Assert.NotEqual(ReorderQueue.SeqNumGtWide(a, b), ReorderQueue.SeqNumGtWide(b, a));
    }

    /// <summary>
    /// A wide queue crossing zero puts its entries where a narrow one would not.
    ///
    /// Pushed at 0xFFFFFFFE, 0xFFFFFFFF and 0x00000000 in order, all three pull back in order -
    /// which is the wrap the data queue actually meets, and the one PP674 exists for.
    /// </summary>
    [Fact]
    public void TheWideQueuePullsAcrossZeroInOrder()
    {
        using ReorderQueue managed = ReorderQueue.Wide(3, 0xFFFFFFFE);
        using NativeReorderQueue native = NativeReorderQueue.Wide(3, 0xFFFFFFFE);

        foreach ((uint seq, long payload) in ((uint, long)[])
            [(0xFFFFFFFE, 10), (0xFFFFFFFF, 11), (0x00000000, 12), (0x00000001, 13)])
        {
            managed.Push(seq, payload);
            native.Push(seq, payload);
        }

        foreach (long expected in (long[])[10, 11, 12, 13])
        {
            (ulong SeqNum, long Payload)? theirs = native.Pull();
            (ulong SeqNum, long Payload)? ours = managed.Pull();

            Assert.Equal(theirs, ours);
            Assert.NotNull(ours);
            Assert.Equal(expected, ours.Value.Payload);
        }

        Assert.Null(managed.Pull());
        Assert.Null(native.Pull());
    }

    /// <summary>The narrow constructor still makes a narrow queue, which nothing above changed.</summary>
    [Fact]
    public void TheNarrowQueueIsUntouched()
    {
        using var managed = new ReorderQueue(3, (ushort)0x1000);
        using var native = new NativeReorderQueue(3, (ushort)0x1000);

        Assert.Equal(ReorderWidth.Sixteen, managed.Width);
        Assert.Equal(ReorderWidth.Sixteen, native.Width);
    }
}
