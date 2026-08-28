using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP500, under PP27: the composed receive path, and the budget that is the reason it exists.
///
/// The correctness assertions here are thin on purpose - PP490 already holds the branch table. What
/// this file is for is the allocation count, which no model under PP485 to PP499 could make about
/// itself because every one of them builds something to describe what it did.
/// </summary>
public class TakionReceivePathTests
{
    /// <summary>
    /// A sink that allocates nothing after construction.
    ///
    /// The kept bytes go into a buffer it already owns. A sink that did `datagram.ToArray()` would
    /// be the honest shape for a queue and would put the allocation back, so the queues this
    /// eventually feeds have to rent too - which is what makes this sink a fair stand-in rather
    /// than a rigged one.
    /// </summary>
    private sealed class CountingSink : ITakionSink
    {
        private readonly byte[] kept = new byte[TakionReceiveBuffer.DatagramCapacity];

        public int Kept { get; private set; }

        public int Borrowed { get; private set; }

        public int LastKeptLength { get; private set; }

        public TakionDispatchBranch LastBranch { get; private set; }

        public void Keep(TakionDispatchBranch branch, ReadOnlySpan<byte> datagram)
        {
            datagram.CopyTo(kept);
            LastKeptLength = datagram.Length;
            LastBranch = branch;
            Kept++;
        }

        public void Borrow(TakionDispatchBranch branch, ReadOnlySpan<byte> datagram)
        {
            LastBranch = branch;
            Borrowed++;
        }
    }

    private static byte[] Datagram(int baseType, int size = 64)
    {
        var packet = new byte[size];
        packet[0] = (byte)baseType;
        return packet;
    }

    /// <summary>The path agrees with PP490's table, branch for branch.</summary>
    [Theory]
    [InlineData(TakionDispatch.Control, TakionDispatchBranch.Control, true)]
    [InlineData(TakionDispatch.Video, TakionDispatchBranch.Video, true)]
    [InlineData(TakionDispatch.Audio, TakionDispatchBranch.Audio, false)]
    [InlineData(6, TakionDispatchBranch.UnknownType, false)]
    public void ThePathTakesTheBranchTheTableNames(
        int baseType, TakionDispatchBranch expected, bool keeps)
    {
        var sink = new CountingSink();
        var counters = default(TakionReceiveCounters);

        TakionDispatchBranch branch = TakionReceivePath.Handle(
            Datagram(baseType), sink, ref counters,
            macOk: true, enableCrypt: true, cryptAvailable: true);

        Assert.Equal(expected, branch);
        Assert.Equal(keeps ? 1 : 0, sink.Kept);
        Assert.Equal(keeps ? 0 : 1, sink.Borrowed);
        Assert.Equal(1, counters.Seen);
    }

    /// <summary>A failed MAC is counted and borrowed, never copied.</summary>
    [Fact]
    public void AFailedMacIsBorrowedAndCounted()
    {
        var sink = new CountingSink();
        var counters = default(TakionReceiveCounters);

        TakionReceivePath.Handle(
            Datagram(TakionDispatch.Video), sink, ref counters,
            macOk: false, enableCrypt: true, cryptAvailable: true);

        Assert.Equal(1, counters.MacRejected);
        Assert.Equal(0, counters.Video);
        Assert.Equal(0, sink.Kept);
        Assert.Equal(0, counters.CopiedBytes);
    }

    /// <summary>Only the three keeping branches are charged for a copy, and for their own length.</summary>
    [Fact]
    public void OnlyTheKeepingBranchesAreChargedForACopy()
    {
        var sink = new CountingSink();
        var counters = default(TakionReceiveCounters);

        foreach ((int baseType, bool cryptAvailable) in new[]
        {
            (TakionDispatch.Control, true),
            (TakionDispatch.Video, true),
            (TakionDispatch.Video, false),
            (TakionDispatch.Audio, true),
            (6, true),
        })
        {
            TakionReceivePath.Handle(
                Datagram(baseType, size: 100), sink, ref counters,
                macOk: true, enableCrypt: true, cryptAvailable: cryptAvailable);
        }

        Assert.Equal(5, counters.Seen);
        Assert.Equal(1, counters.Control);
        Assert.Equal(1, counters.Video);
        Assert.Equal(1, counters.Postponed);
        Assert.Equal(1, counters.Audio);
        Assert.Equal(1, counters.UnknownType);

        // Three copies of 100 bytes. Audio and the unknown type paid nothing.
        Assert.Equal(3, sink.Kept);
        Assert.Equal(300, counters.CopiedBytes);
    }

    /// <summary>An empty datagram is counted as unknown rather than throwing out of the path.</summary>
    [Fact]
    public void AnEmptyDatagramDoesNotThrow()
    {
        var sink = new CountingSink();
        var counters = default(TakionReceiveCounters);

        Assert.Equal(
            TakionDispatchBranch.UnknownType,
            TakionReceivePath.Handle(
                [], sink, ref counters, macOk: true, enableCrypt: true, cryptAvailable: true));

        Assert.Equal(1, counters.UnknownType);
    }

    /// <summary>
    /// THE BUDGET. Two hundred datagrams through the composed path allocate zero bytes.
    ///
    /// The same shape as test/allocbudget.c and as PP23's managed version of it: warm up once so
    /// the first-call costs are outside the window, then count. Zero is not "small" - the C's
    /// budget is zero bytes in zero calls per packet and this side is charged the same.
    ///
    /// What would fail it: a trace list in the path, a ToArray in a branch, a lambda capturing a
    /// counter, an enum boxed into a dictionary lookup. Every one of those reads as ordinary C#.
    /// </summary>
    [Fact]
    public void TwoHundredDatagramsAllocateNothing()
    {
        var sink = new CountingSink();
        var counters = default(TakionReceiveCounters);

        byte[] control = Datagram(TakionDispatch.Control, size: 200);
        byte[] video = Datagram(TakionDispatch.Video, size: 1300);
        byte[] audio = Datagram(TakionDispatch.Audio, size: 400);
        byte[] unknown = Datagram(6, size: 64);

        // Warm-up: every branch taken once, so nothing first-call is charged to the window.
        Run(control, video, audio, unknown, sink, ref counters, rounds: 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Run(control, video, audio, unknown, sink, ref counters, rounds: 50);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);

        // And it actually did the work, so a path that returned early would not pass by doing
        // nothing - which is the failure mode a zero-allocation assertion invites.
        Assert.Equal(204, counters.Seen);
    }

    /// <summary>Four datagrams per round, one per branch that a dispatch can reach.</summary>
    private static void Run(
        byte[] control, byte[] video, byte[] audio, byte[] unknown,
        ITakionSink sink, ref TakionReceiveCounters counters, int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            TakionReceivePath.Handle(control, sink, ref counters, true, true, true);
            TakionReceivePath.Handle(video, sink, ref counters, true, true, true);
            TakionReceivePath.Handle(audio, sink, ref counters, true, true, true);
            TakionReceivePath.Handle(unknown, sink, ref counters, true, true, true);
        }
    }

    /// <summary>
    /// And the models are NOT this: the loop allocates, which is what made a separate path
    /// necessary.
    ///
    /// Asserted rather than asserted-about, because "the harness allocates" is the premise of the
    /// whole line. If PP487's loop ever became allocation-free this test says so, and the argument
    /// for two pieces would need re-making.
    /// </summary>
    [Fact]
    public void TheModelledLoopStillAllocatesWhichIsWhyThisExists()
    {
        var host = new AllocatingHost();

        // Warm up the machinery so the window measures the run and not the first JIT.
        TakionReceiveLoop.Run(host, enableCrypt: true, iterationLimit: 2);

        long before = GC.GetAllocatedBytesForCurrentThread();
        TakionReceiveLoop.Run(host, enableCrypt: true, iterationLimit: 8);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(after - before > 0, "the trace list stopped allocating");
    }

    /// <summary>A host that always yields a datagram, so the loop runs to its limit.</summary>
    private sealed class AllocatingHost : ITakionLoopHost
    {
        public bool CryptAvailable => true;

        public bool HasPostponed => false;

        public ulong NextTimeoutMs => 50;

        public void RecheckMacs()
        {
        }

        public void FlushPostponed()
        {
        }

        public void FlushWithTimeout()
        {
        }

        public TakionReceiveResult Receive(Span<byte> into, ulong timeoutMs)
            => new(TakionReceiveOutcome.Datagram, 16);

        public void Dispatch(ReadOnlySpan<byte> datagram)
        {
        }
    }
}
