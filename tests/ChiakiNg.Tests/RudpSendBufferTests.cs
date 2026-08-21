using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: sixteen packets waiting to be acknowledged, and what happens when one never is.
/// </summary>
public class RudpSendBufferTests
{
    private static RudpSendBuffer Filled(long now, params ushort[] seqNums)
    {
        var buffer = new RudpSendBuffer();
        foreach (ushort seqNum in seqNums)
            Assert.Equal(RudpPushResult.Ok, buffer.Push(seqNum, [1, 2, 3], now));

        return buffer;
    }

    private static ushort[] Waiting(RudpSendBuffer buffer)
        => [.. buffer.Packets.Select(p => p.SeqNum)];

    /// <summary>
    /// THE COMPARISON IS NOT A TOTAL ORDER. At exactly half the space the two guards disagree by
    /// one comparison, so 0 and 32768 are neither equal nor either-way-round.
    ///
    /// A port using a plain less-than would order them; one using subtraction with a less-or-equal
    /// would order them the other way. Both are wrong in the same one place.
    /// </summary>
    [Fact]
    public void AtExactlyHalfTheSpaceNeitherIsLess()
    {
        Assert.False(SeqNum16.LessThan(0, 32768));
        Assert.False(SeqNum16.LessThan(32768, 0));
        Assert.NotEqual<ushort>(0, 32768);

        // One either side of it, and the order comes back.
        Assert.True(SeqNum16.LessThan(0, 32767));
        Assert.True(SeqNum16.LessThan(32769, 0));
    }

    /// <summary>Ordinary neighbours, and the wrap.</summary>
    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, false)]
    [InlineData(65534, 3, true)]
    [InlineData(3, 65534, false)]
    [InlineData(7, 7, false)]
    public void TheOrderIsTheWrappingOne(int a, int b, bool less)
        => Assert.Equal(less, SeqNum16.LessThan((ushort)a, (ushort)b));

    /// <summary>A packet goes in and waits.</summary>
    [Fact]
    public void APushedPacketWaits()
    {
        RudpSendBuffer buffer = Filled(0, 1, 2, 3);

        Assert.Equal<ushort[]>([1, 2, 3], Waiting(buffer));
    }

    /// <summary>Sixteen fit, and the seventeenth does not.</summary>
    [Fact]
    public void TheSeventeenthPacketOverflows()
    {
        var buffer = new RudpSendBuffer();
        for (int i = 0; i < RudpSendBuffer.Size; i++)
            Assert.Equal(RudpPushResult.Ok, buffer.Push((ushort)i, [1], 0));

        Assert.Equal(RudpPushResult.Overflow, buffer.Push(99, [1], 0));
        Assert.Equal(16, buffer.Packets.Count);
    }

    /// <summary>And the same sequence number twice is refused rather than queued twice.</summary>
    [Fact]
    public void ADuplicateSequenceNumberIsRefused()
    {
        RudpSendBuffer buffer = Filled(0, 5);

        Assert.Equal(RudpPushResult.Duplicate, buffer.Push(5, [9], 0));
        Assert.Single(buffer.Packets);
    }

    /// <summary>
    /// THE ACK IS CUMULATIVE, in wrapping order - so acknowledging 3 clears 65534 as well.
    /// </summary>
    [Fact]
    public void AnAckClearsEverythingOlderIncludingAcrossTheWrap()
    {
        RudpSendBuffer buffer = Filled(0, 65534, 65535, 1, 3, 7);

        IReadOnlyList<ushort> acked = buffer.Ack(3);

        Assert.Equal<ushort[]>([65534, 65535, 1, 3], [.. acked]);
        Assert.Equal<ushort[]>([7], Waiting(buffer));
    }

    /// <summary>
    /// Alternating gaps, which is where a hand-rolled compaction goes wrong if it is going to. The
    /// core shifts the gaps with memmove; what matters is that the survivors keep their order.
    /// </summary>
    [Fact]
    public void AlternatingRemovalsKeepTheSurvivorsInOrder()
    {
        // 10, 30 and 50 are older than the ack; 200 and 300 are ahead of it.
        RudpSendBuffer buffer = Filled(0, 10, 200, 30, 300, 50);

        buffer.Ack(100);

        Assert.Equal<ushort[]>([200, 300], Waiting(buffer));
    }

    /// <summary>An ack for something nobody sent clears nothing and complains about nothing.</summary>
    [Fact]
    public void AnAckThatMatchesNothingIsQuiet()
    {
        RudpSendBuffer buffer = Filled(0, 500, 600);

        Assert.Empty(buffer.Ack(400));
        Assert.Equal<ushort[]>([500, 600], Waiting(buffer));
    }

    /// <summary>Nothing goes again until the timeout has actually passed.</summary>
    [Fact]
    public void NothingIsResentBeforeTheTimeout()
    {
        RudpSendBuffer buffer = Filled(0, 1);

        Assert.Empty(buffer.Resend(RudpSendBuffer.ResendTimeoutMs));
        Assert.Single(buffer.Resend(RudpSendBuffer.ResendTimeoutMs + 1));
        Assert.Equal(1, buffer.Packets[0].Tries);
    }

    /// <summary>And a packet that just went again is not sent twice in the same pass.</summary>
    [Fact]
    public void AResentPacketWaitsAgain()
    {
        RudpSendBuffer buffer = Filled(0, 1);
        long now = RudpSendBuffer.ResendTimeoutMs + 1;

        Assert.Single(buffer.Resend(now));
        Assert.Empty(buffer.Resend(now));
    }

    /// <summary>
    /// GIVING UP ON A PACKET ACKNOWLEDGES IT - and the ack is cumulative, so every older
    /// unacknowledged packet is discarded with it, silently, as though the console had confirmed
    /// them all. One packet timing out takes the queue behind it with it.
    /// </summary>
    [Fact]
    public void GivingUpOnOnePacketDiscardsEveryOlderOne()
    {
        RudpSendBuffer buffer = Filled(0, 1, 2, 3, 4);

        // Wind the third packet up to its last try, leaving the others fresh.
        RudpSentPacket doomed = buffer.Packets.Single(p => p.SeqNum == 3);
        doomed.Tries = RudpSendBuffer.ResendTriesMax;

        buffer.Resend(RudpSendBuffer.ResendTimeoutMs + 1);

        // 1 and 2 were never given up on. They are gone anyway.
        Assert.Equal<ushort[]>([4], Waiting(buffer));
    }

    /// <summary>
    /// THE REWIND AFTER A REMOVAL SKIPS INDEX ZERO. The guard that keeps an unsigned index from
    /// wrapping means a give-up at index zero does not step back, so the packet that shifts down
    /// into index zero is skipped for the rest of that round.
    ///
    /// It gets another chance on the next wake-up, so this is a delay and not a loss - but it is a
    /// delay nobody asked for, and a port that wrote the loop correctly would resend sooner than
    /// the Qt client does.
    /// </summary>
    [Fact]
    public void AGiveUpAtIndexZeroSkipsThePacketThatTakesItsPlace()
    {
        RudpSendBuffer buffer = Filled(0, 1, 2, 3);
        buffer.Packets[0].Tries = RudpSendBuffer.ResendTriesMax;

        IReadOnlyList<ushort> sent = buffer.Resend(RudpSendBuffer.ResendTimeoutMs + 1);

        // 1 was given up on. 2 shifted into index zero and was stepped straight over; only 3 went.
        Assert.Equal<ushort[]>([3], [.. sent]);
        Assert.Equal<ushort[]>([2, 3], Waiting(buffer));
        Assert.Equal(0, buffer.Packets[0].Tries);

        // The next round picks it up, which is what makes this a delay rather than a loss.
        Assert.Contains<ushort>(2, buffer.Resend(2 * RudpSendBuffer.ResendTimeoutMs + 2));
    }

    /// <summary>
    /// AND THE REWIND STEPS BACK ONE WHERE THE ACK REMOVED SEVERAL. Giving up runs the cumulative
    /// ack, which can clear any number of packets at once - but the loop steps the index back by
    /// exactly one, as though a single element had gone.
    ///
    /// So the skip is not a property of index zero at all: it happens wherever the ack removed more
    /// than one, and it skips one packet for every extra removal. Index zero is simply the case
    /// where the step back does not happen at all.
    /// </summary>
    [Fact]
    public void TheRewindStepsBackOneWhereTheAckRemovedTwo()
    {
        RudpSendBuffer buffer = Filled(0, 100, 200, 300, 400);
        buffer.Packets.Single(p => p.SeqNum == 200).Tries = RudpSendBuffer.ResendTriesMax;

        IReadOnlyList<ushort> sent = buffer.Resend(RudpSendBuffer.ResendTimeoutMs + 1);

        // 100 went out, then 200 was given up on - taking the already-resent 100 with it. Two left
        // the buffer, the index stepped back one, and 300 was stepped straight over.
        Assert.Equal<ushort[]>([100, 400], [.. sent]);
        Assert.Equal<ushort[]>([300, 400], Waiting(buffer));
        Assert.Equal(0, buffer.Packets.Single(p => p.SeqNum == 300).Tries);
    }

    /// <summary>Twenty-five tries is the whole allowance, and the twenty-sixth pass gives up.</summary>
    [Fact]
    public void TwentyFiveTriesIsTheAllowance()
    {
        RudpSendBuffer buffer = Filled(0, 1);
        long now = 0;

        for (int i = 0; i < RudpSendBuffer.ResendTriesMax; i++)
        {
            now += RudpSendBuffer.ResendTimeoutMs + 1;
            Assert.Single(buffer.Resend(now));
        }

        Assert.Equal(25, buffer.Packets[0].Tries);

        now += RudpSendBuffer.ResendTimeoutMs + 1;
        Assert.Empty(buffer.Resend(now));
        Assert.Empty(buffer.Packets);
    }

    /// <summary>The thread wakes twice per timeout, so a packet is never a whole round late.</summary>
    [Fact]
    public void TheWakeupIsHalfTheTimeout()
        => Assert.Equal(RudpSendBuffer.ResendTimeoutMs / 2, RudpSendBuffer.ResendWakeupTimeoutMs);

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheBuffersRulesAreStillTheQtCores()
    {
        string? path = RudpSendBufferSource.Locate();
        string? seqnum = RudpSendBufferSource.LocateSeqNum();
        if (path is null || seqnum is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(RudpSendBufferSource.TheConstantsAreStillTheseValues(core), "four constants");
        Assert.True(RudpSendBufferSource.GivingUpStillAcknowledges(core), "giving up acknowledges");
        Assert.True(RudpSendBufferSource.TheAckIsStillCumulative(core), "cumulative, in wrapping order");
        Assert.True(RudpSendBufferSource.TheRewindStillSkipsIndexZero(core), "the guard, and its cost");
        Assert.True(RudpSendBufferSource.ARefusedPushStillFreesTheBuffer(core), "a refusal still frees");
        Assert.True(
            RudpSendBufferSource.TheComparisonIsStillAsymmetric(File.ReadAllText(seqnum)),
            "less-than one way, greater-than the other");
        Assert.True(
            RudpSendBufferSource.TheNamesStillOmitTheOffsetTypes(core), "the offset types go unnamed");
    }
}
