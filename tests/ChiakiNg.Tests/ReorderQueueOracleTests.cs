using System.Text;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the managed reorder queue, driven through the same operation sequences as libchiaki's and
/// compared after every single step.
///
/// Example-based tests are what this module already had, and they are not enough for it: the whole
/// behaviour is which of four drop occasions a push takes, and the interesting combinations are the
/// ones nobody thinks to write down - a duplicate arriving after the window moved past its slot, an
/// overflow that empties the queue rather than making room, a packet exactly half the sequence space
/// away. So the two implementations are stepped together over pseudo-random sequences and every
/// observable is compared each time: count, the drop log, and a peek at every offset.
///
/// The seeds are fixed. A differential test that only fails on some runs is a test nobody can act
/// on, and a fixed seed that found something once keeps finding it.
/// </summary>
public class ReorderQueueOracleTests
{
    /// <summary>
    /// A tiny deterministic generator, written out rather than taken from Random - so a failure
    /// here reproduces on every machine and every runtime version.
    /// </summary>
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
        Assert.Equal(native.Count, managed.Count);
        Assert.Equal(native.Size, managed.Size);

        // The drop log, in order and with payloads - a queue that dropped the right count of the
        // wrong packets would pass a count comparison.
        Assert.Equal(native.Drops.Count, managed.Drops.Count);
        for (int i = 0; i < native.Drops.Count; i++)
            Assert.Equal(native.Drops[i], managed.Drops[i]);

        // And the whole window, offset by offset, including the gaps.
        for (ulong offset = 0; offset < native.Count; offset++)
            Assert.Equal(native.Peek(offset), managed.Peek(offset));

        // One past the end answers the same way on both.
        Assert.Equal(native.Peek(native.Count), managed.Peek(managed.Count));
    }

    /// <summary>
    /// Pushes, pulls, peeks and drops in a pseudo-random mix, with sequence numbers clustered near
    /// the window so overflows and duplicates actually happen rather than being theoretical.
    /// </summary>
    [Theory]
    [InlineData(1u, 3, ReorderDropStrategy.End)]
    [InlineData(2u, 3, ReorderDropStrategy.Begin)]
    [InlineData(3u, 4, ReorderDropStrategy.End)]
    [InlineData(4u, 4, ReorderDropStrategy.Begin)]
    [InlineData(5u, 1, ReorderDropStrategy.End)]
    [InlineData(6u, 1, ReorderDropStrategy.Begin)]
    [InlineData(7u, 0, ReorderDropStrategy.End)]
    [InlineData(8u, 0, ReorderDropStrategy.Begin)]
    [InlineData(99u, 6, ReorderDropStrategy.Begin)]
    public void BothQueuesAgreeStepForStep(uint seed, int sizeExp, ReorderDropStrategy strategy)
    {
        const ushort start = 0x1000;

        using var managed = new ReorderQueue(sizeExp, start) { DropStrategy = strategy };
        using var native = new NativeReorderQueue(sizeExp, start) { DropStrategy = strategy };

        var rng = new Lcg(seed);
        var log = new List<string>();
        int window = 1 << sizeExp;
        ushort cursor = start;

        for (int step = 0; step < 400; step++)
        {
            int op = rng.Next(10);

            if (op < 6)
            {
                // Mostly near the cursor, occasionally far - including backwards, which is the
                // "older than the window" occasion.
                int spread = rng.Next(3) switch
                {
                    0 => rng.Next(window + 2),
                    1 => -rng.Next(window + 2),
                    _ => rng.Next(2 * window + 4) - window - 2,
                };

                ushort seq = (ushort)(cursor + spread);
                long payload = (step << 8) | 0x5a;

                log.Add($"push {seq:x4} payload={payload}");
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
                cursor = (ushort)(cursor + rng.Next(window + 1));
                log.Add($"cursor -> {cursor:x4}");
            }

            AssertSameState(managed, native, log);
        }

        Assert.True(managed.Drops.Count > 0, Describe(managed, native, log));
    }

    /// <summary>
    /// The antipode, reaching the queue through `ge`. PP149 found that at half the sequence space
    /// apart neither comparison is true; `ge` is built as `a == b || gt`, so it is false there too,
    /// and the push has a branch that exists for nothing else.
    /// </summary>
    [Theory]
    [InlineData(ReorderDropStrategy.End)]
    [InlineData(ReorderDropStrategy.Begin)]
    public void TheAntipodeTakesItsOwnBranchInBoth(ReorderDropStrategy strategy)
    {
        const ushort start = 0x0100;

        using var managed = new ReorderQueue(3, start) { DropStrategy = strategy };
        using var native = new NativeReorderQueue(3, start) { DropStrategy = strategy };
        var log = new List<string> { $"antipode of {start:x4}, empty queue" };

        // Empty queue: the Begin strategy rebases onto the packet, End drops it.
        ushort antipode = (ushort)(start + SeqNum.HalfSpace16);
        Assert.True(SeqNum.Incomparable(start, antipode));

        managed.Push(antipode, 1);
        native.Push(antipode, 1);
        AssertSameState(managed, native, log);

        Assert.Equal(strategy == ReorderDropStrategy.End ? 1 : 0, managed.Drops.Count);

        // And again with something already queued, which is the other side of the same branch.
        using var managed2 = new ReorderQueue(3, start) { DropStrategy = strategy };
        using var native2 = new NativeReorderQueue(3, start) { DropStrategy = strategy };

        managed2.Push(start, 7);
        native2.Push(start, 7);
        managed2.Push((ushort)(start + SeqNum.HalfSpace16), 8);
        native2.Push((ushort)(start + SeqNum.HalfSpace16), 8);

        AssertSameState(managed2, native2, ["non-empty antipode"]);
    }

    /// <summary>
    /// The finding: Drop reports an element and does not remove it. chiaki_reorder_queue_drop fires
    /// the callback then runs `while(!entry->set)` to shrink the count - but nothing ever clears
    /// `set`, so the loop cannot execute and the element is still there to be pulled.
    ///
    /// Asserted on BOTH implementations, because the value of stating it is that the port matches.
    /// </summary>
    [Fact]
    public void DropReportsTheElementAndLeavesItInTheQueue()
    {
        using var managed = new ReorderQueue(3, 0);
        using var native = new NativeReorderQueue(3, 0);

        managed.Push(0, 111);
        native.Push(0, 111);
        managed.Push(1, 222);
        native.Push(1, 222);

        Assert.Equal(2ul, managed.Count);

        // Drop the LAST element, which is the only offset the shrink loop is even attempted for.
        managed.Drop(1);
        native.Drop(1);

        // Reported...
        Assert.Single(managed.Drops);
        Assert.Equal(new ReorderDrop(1, 222), managed.Drops[0]);
        Assert.Equal(native.Drops.Count, managed.Drops.Count);

        // ...and still counted, still peekable, and still pullable.
        Assert.Equal(2ul, managed.Count);
        Assert.Equal(native.Count, managed.Count);
        Assert.Equal((1ul, 222L), managed.Peek(1));
        Assert.Equal(native.Peek(1), managed.Peek(1));

        Assert.Equal((0ul, 111L), managed.Pull());
        Assert.Equal((1ul, 222L), managed.Pull());
    }

    /// <summary>
    /// Teardown is the one place the oracle cannot answer, and the reason is in the port's own seam.
    ///
    /// chiaki_reorder_queue_fini reports every element still queued. The shim clears the drop
    /// callback BEFORE calling it, deliberately - a callback into managed code that is about to stop
    /// being interested is a lifetime bug waiting to happen - so the native queue is silent at
    /// teardown no matter what libchiaki does.
    ///
    /// The managed queue has no such hazard, so it follows libchiaki: it reports. That makes this
    /// the only observable where the two implementations differ ON PURPOSE, which is worth an
    /// assertion of its own rather than an exception in the comparison helper. Where the oracle is
    /// blind, the C source answers instead.
    /// </summary>
    [Fact]
    public void OnlyTeardownDivergesAndTheSourceSaysWhy()
    {
        var managed = new ReorderQueue(3, 10);
        var native = new NativeReorderQueue(3, 10);

        // A gap at 11, so the window spans three and only two are set.
        managed.Push(10, 1);
        native.Push(10, 1);
        managed.Push(12, 3);
        native.Push(12, 3);

        managed.Dispose();
        native.Dispose();

        // The managed queue reports what libchiaki's fini reports, in window order and skipping
        // the gap.
        Assert.Equal(
            [new ReorderDrop(10, 1), new ReorderDrop(12, 3)],
            managed.Drops);

        // The native one reports nothing, because the seam unhooked the callback first.
        Assert.Empty(native.Drops);

        // And both halves of that are still true in the source, which is what licenses the
        // divergence above.
        string? queueFile = ReorderQueueSource.Locate();
        string? shim = ReorderQueueSource.LocateShim();
        if (queueFile is null || shim is null)
            return;

        string? fini = ReorderQueueSource.BodyOf(queueFile, "chiaki_reorder_queue_fini");
        Assert.NotNull(fini);
        Assert.True(ReorderQueueSource.FiniReportsWhatIsStillQueued(fini!), "fini still reports");
        Assert.True(
            ReorderQueueSource.ShimSuppressesFiniCallbacks(File.ReadAllText(shim)),
            "the shim still unhooks the callback before fini");
    }

    /// <summary>
    /// A pull does not clear the slot, in either implementation - the window moves past it and the
    /// grow loop resets it if the window ever comes round again. Reproduced because with a small
    /// window two sequence numbers 2^sizeExp apart share a slot, so a stale `set` decides whether
    /// the next arrival there reads as a duplicate.
    /// </summary>
    [Fact]
    public void APullLeavesTheSlotSetAndBothAgreeAboutWhatThatCosts()
    {
        using var managed = new ReorderQueue(2, 0);   // window of four
        using var native = new NativeReorderQueue(2, 0);

        for (ushort i = 0; i < 4; i++)
        {
            managed.Push(i, i + 1);
            native.Push(i, i + 1);
        }

        for (int i = 0; i < 4; i++)
            Assert.Equal(native.Pull(), managed.Pull());

        // Slot 0 again, four sequence numbers later.
        managed.Push(4, 99);
        native.Push(4, 99);

        AssertSameState(managed, native, ["four pushed, four pulled, then the slot reused"]);
    }
}
