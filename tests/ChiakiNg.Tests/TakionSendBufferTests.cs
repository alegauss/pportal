using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP27: the managed send buffer, against the C one through the wrapper PP125 already built.
///
/// takionsendbuffer.c is one of the modules with an oracle - test/takion.c exercises it, and the
/// managed wrapper can drive the native structure directly - so this is the comparison PP287 and
/// PP289 used rather than the case tables the untested modules need.
/// </summary>
public class TakionSendBufferTests(ITestOutputHelper output)
{
    /// <summary>Runs the same pushes and acks through both and compares what each holds.</summary>
    private static void Both(int capacity, Action<TakionSendBuffer, SendBuffer> script)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        var managed = new TakionSendBuffer(capacity);
        using var native = new SendBuffer(capacity);

        script(managed, native);

        Assert.Equal(native.Count, managed.Count);
    }

    /// <summary>Pushes are held, and the counts agree.</summary>
    [Fact]
    public void BothHoldWhatWasPushed()
    {
        Both(16, (managed, native) =>
        {
            for (uint i = 1; i <= 5; i++)
            {
                Assert.Equal(ChiakiError.Success, managed.Push(i, 32));
                Assert.Equal(ChiakiError.Success, native.Push(i, 32));
            }

            Assert.Equal(5, managed.Count);
        });
    }

    /// <summary>A duplicate sequence number is refused by both.</summary>
    [Fact]
    public void BothRefuseADuplicate()
    {
        Both(16, (managed, native) =>
        {
            Assert.Equal(ChiakiError.Success, managed.Push(7, 32));
            Assert.Equal(ChiakiError.Success, native.Push(7, 32));

            Assert.Equal(ChiakiError.InvalidData, managed.Push(7, 32));
            Assert.Equal(ChiakiError.InvalidData, native.Push(7, 32));
        });
    }

    /// <summary>And a full buffer overflows in both.</summary>
    [Fact]
    public void BothOverflowAtCapacity()
    {
        Both(4, (managed, native) =>
        {
            for (uint i = 0; i < 4; i++)
            {
                Assert.Equal(ChiakiError.Success, managed.Push(i, 8));
                Assert.Equal(ChiakiError.Success, native.Push(i, 8));
            }

            Assert.Equal(ChiakiError.Overflow, managed.Push(99, 8));
            Assert.Equal(ChiakiError.Overflow, native.Push(99, 8));
        });
    }

    /// <summary>
    /// An ack clears everything AT OR BEFORE it, not just the one named.
    ///
    /// That is what makes a lost ack harmless - the next one catches up - and it is the behaviour a
    /// port would most plausibly get wrong by removing only the match.
    /// </summary>
    [Fact]
    public void AnAckClearsEverythingAtOrBeforeIt()
    {
        Both(16, (managed, native) =>
        {
            foreach (uint seq in (uint[])[1, 2, 3, 4, 5])
            {
                managed.Push(seq, 8);
                native.Push(seq, 8);
            }

            IReadOnlyList<uint> acked = managed.Ack(3);
            Assert.Equal(ChiakiError.Success, native.Ack(3));

            Assert.Equal<uint>([1, 2, 3], acked);
            Assert.Equal<uint>([4, 5], managed.SeqNums);
        });
    }

    /// <summary>
    /// The survivors are compacted across SEVERAL gaps in one pass, which is the hard part.
    ///
    /// Packets are in push order rather than sequence order, so the acked ones can be scattered.
    /// The C tracks a shift window and memmoves when a new gap opens; getting that arithmetic wrong
    /// leaves a live packet overwritten by a dead one and nothing ever resends it.
    /// </summary>
    [Fact]
    public void SeveralGapsAreClosedInOnePass()
    {
        Both(16, (managed, native) =>
        {
            // Pushed out of order, so acking 20 leaves holes at 0, 2 and 4.
            foreach (uint seq in (uint[])[10, 100, 20, 200, 15, 300])
            {
                managed.Push(seq, 8);
                native.Push(seq, 8);
            }

            IReadOnlyList<uint> acked = managed.Ack(20);
            Assert.Equal(ChiakiError.Success, native.Ack(20));

            Assert.Equal<uint>([10, 20, 15], acked);
            Assert.Equal<uint>([100, 200, 300], managed.SeqNums);
        });
    }

    /// <summary>An ack for something not held removes nothing and keeps everything.</summary>
    [Fact]
    public void AnAckBelowEverythingHeldRemovesNothing()
    {
        Both(16, (managed, native) =>
        {
            foreach (uint seq in (uint[])[50, 60, 70])
            {
                managed.Push(seq, 8);
                native.Push(seq, 8);
            }

            Assert.Empty(managed.Ack(40));
            Assert.Equal(ChiakiError.Success, native.Ack(40));
            Assert.Equal(3, managed.Count);
        });
    }

    /// <summary>
    /// And it all holds across the 32-bit turnover.
    ///
    /// A buffer holding 0xfffffff0 when 0x00000005 is acked must clear it. Compared with a plain
    /// less-than it would keep that packet forever, resending a message the console acknowledged
    /// four billion sequence numbers ago - and the resend thread would never stop.
    /// </summary>
    [Fact]
    public void TheAckSurvivesTheSequenceWrap()
    {
        Both(16, (managed, native) =>
        {
            foreach (uint seq in (uint[])[0xfffffff0, 0xfffffffe, 3, 9])
            {
                managed.Push(seq, 8);
                native.Push(seq, 8);
            }

            IReadOnlyList<uint> acked = managed.Ack(5);
            Assert.Equal(ChiakiError.Success, native.Ack(5));

            Assert.Equal<uint>([0xfffffff0, 0xfffffffe, 3], acked);
            Assert.Equal<uint>([9], managed.SeqNums);

            output.WriteLine($"wrapped ack cleared {acked.Count}, {managed.Count} left");
        });
    }
}
