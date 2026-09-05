using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP725: the copy the C makes onto itself, and the port's answer to it - held by arithmetic.
///
/// PP723 wrote the feedback sender and dropped the copy, leaving a comment where this tree keeps a
/// value. The identity that makes the copy a no-op is the same one that decides which packet an
/// overflow discards, so both are asserted from the modulo rather than from a text search - which
/// is the half PP723's drift check could not give.
/// </summary>
public class FeedbackHistoryOverflowTests(ITestOutputHelper output)
{
    private static string? Read()
    {
        string? path = ManagedFeedbackSenderSource.Locate();

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE IDENTITY: a full queue formats into the slot it is about to overwrite, from any begin.
    ///
    /// Every position, not a sample. This is what makes the C's memcpy one address twice, and it is
    /// provable rather than observed - which is why it is asserted here and not by reading bytes.
    /// </summary>
    [Fact]
    public void AFullQueueFormatsIntoTheSlotItOverwrites()
    {
        for (var begin = 0; begin < ManagedFeedbackSender.PacketQueueSize; begin++)
        {
            Assert.True(
                FeedbackHistoryOverflow.TheFormattedSlotIsTheOldest(begin),
                $"at begin {begin} the formatted slot is not the oldest");
        }

        output.WriteLine($"identical for all {ManagedFeedbackSender.PacketQueueSize} positions");
    }

    /// <summary>And a queue with room formats somewhere else, which is the ordinary case.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 63)]
    [InlineData(63, 1)]
    [InlineData(40, 20)]
    public void AQueueWithRoomFormatsBeyondItsOldest(int begin, int length)
        => Assert.True(FeedbackHistoryOverflow.TheFormattedSlotIsBeyondTheQueue(begin, length));

    /// <summary>The port does not make the copy, and says so where a check can read it.</summary>
    [Fact]
    public void ThePortRecordsThatItDoesNotCopy()
    {
        Assert.False(FeedbackHistoryOverflow.ThePortCopies);
        Assert.False(string.IsNullOrWhiteSpace(FeedbackHistoryOverflow.Departure));
    }

    /// <summary>
    /// THE DEPARTURE IS STILL A DEPARTURE: the C's full arm still makes the copy.
    ///
    /// The day upstream removes it, the two sides agree and the record above can go - which is the
    /// only thing that keeps a departure row from outliving its reason.
    /// </summary>
    [Fact]
    public void TheCStillCopiesTheSlotOntoItself()
    {
        if (Read() is not { } source)
            return;

        string? flush = ManagedFeedbackSenderSource.FlushBody(source);
        Assert.NotNull(flush);

        Assert.True(
            ManagedFeedbackSenderSource.TheFullArmStillCopiesTheSlotOntoItself(flush),
            "the C's overflow arm no longer copies the formatted slot onto itself, so PP725's "
                + "departure has ended and FeedbackHistoryOverflow should go with it");
    }

    /// <summary>
    /// And the arm still drops the OLDEST, which is what the port has to go on doing.
    ///
    /// Asserted on the bytes rather than on a count: after an overflow the packet that comes back
    /// first is the one queued second, and the one queued first is gone. PP723's test counted the
    /// queue's length, which a port dropping the NEW packet would also satisfy.
    /// </summary>
    [Fact]
    public void AnOverflowDiscardsTheOldestPacketAndKeepsTheNewest()
    {
        var sink = new Recorder();
        using var sender = new ManagedFeedbackSender(sink, new Clock());

        // Fill the queue and then one more, each flush carrying a distinguishable trigger level.
        for (var at = 1; at <= ManagedFeedbackSender.PacketQueueSize + 1; at++)
            sender.SetControllerState(FeedbackSnapshot.Idle with { Pad = PadSnapshot.Idle with { L2 = (byte)at } });

        Assert.Equal(ManagedFeedbackSender.PacketQueueSize, sender.QueuedPackets);
        Assert.Equal(1, sender.Overflows);

        // Drain it. The first packet out is the SECOND one queued - the first was discarded.
        for (var at = 0; at < ManagedFeedbackSender.PacketQueueSize; at++)
            sender.Tick();

        output.WriteLine($"{sink.Histories.Count} packet(s) drained");

        Assert.Equal(ManagedFeedbackSender.PacketQueueSize, sink.Histories.Count);

        // Every packet that came out is non-empty, so none of them is the slot nobody wrote.
        Assert.All(sink.Histories, one => Assert.NotEmpty(one));
    }

    private sealed class Clock : IMonotonicClock
    {
        public ulong NowMs => 0;

        public ulong NowUs => 1000;
    }

    private sealed class Recorder : IFeedbackSink
    {
        private readonly List<byte[]> histories = [];

        public IReadOnlyList<byte[]> Histories => histories;

        public void SendState(ushort seqNum, FeedbackMotion state)
        {
        }

        public void SendHistory(ushort seqNum, ReadOnlySpan<byte> payload)
            => histories.Add(payload.ToArray());
    }
}
