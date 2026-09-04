using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP674: takion_handle_packet_message_data, which is the push the data queue was missing.
///
/// PP673 routes a DATA message here and PP493 modelled the drain that follows. Between them was
/// nothing: the entry the C builds from the payload's first six bytes, and the push onto the
/// thirty-two-bit queue.
///
/// THE DROPS ARE ABOUT OWNERSHIP AND THAT IS THE HALF A PORT LOSES. The data arm is the one of the
/// switch's three that does NOT free after the call - it hands the datagram to this function, which
/// puts it in an entry the queue's drop frees later. So both early returns free it themselves, and
/// a model with plain returns would be a model of a leak. PP491 repaired that in the C; this holds
/// the repair on the managed side.
/// </summary>
public class TakionDataPushTests
{
    /// <summary>A datagram whose payload begins at a known offset, with a sequence and a channel.</summary>
    private static byte[] Datagram(int payloadOffset, int payloadSize, uint seqNum, ushort channel)
    {
        var datagram = new byte[payloadOffset + payloadSize];

        for (int i = 0; i < datagram.Length; i++)
            datagram[i] = (byte)(i + 0x40);

        if (payloadSize >= TakionDataPush.DataHeaderSize)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                datagram.AsSpan(payloadOffset + TakionDataPush.SeqNumOffset), seqNum);
            BinaryPrimitives.WriteUInt16BigEndian(
                datagram.AsSpan(payloadOffset + TakionDataPush.ChannelOffset), channel);
        }

        return datagram;
    }

    private const int Offset = 1 + 0x10;

    /// <summary>THE ENTRY: the sequence number at the payload's start and the channel four bytes in.</summary>
    [Theory]
    [InlineData(0u, (ushort)0)]
    [InlineData(1u, (ushort)1)]
    [InlineData(0x7FFFFFFFu, (ushort)0x1234)]
    [InlineData(0xFFFFFFFFu, (ushort)0xFFFF)]
    public void TheEntryCarriesTheSequenceAndTheChannel(uint seqNum, ushort channel)
    {
        TakionDataPushReading reading = TakionDataPush.Read(
            Datagram(Offset, 32, seqNum, channel), Offset, 32, TakionDataPush.ExpectedTypeB);

        Assert.Equal(TakionDataPushVerdict.Pushed, reading.Verdict);
        Assert.Equal(seqNum, reading.Entry.SeqNum);
        Assert.Equal(channel, reading.Entry.Channel);
        Assert.Equal(32, reading.Entry.Payload.Length);
    }

    /// <summary>
    /// PP491'S REACHABLE DROP: a payload under nine bytes is dropped AND the datagram released.
    ///
    /// The parse forces the datagram to be its payload plus twelve and refuses anything under
    /// sixteen, so a payload lands under nine only for a tagged datagram of 17 to 25 bytes - and
    /// before the remote crypt exists the MAC gate passes everything, so a corrupt control packet
    /// arriving then leaks one datagram each. Both halves are asserted: it does not become an entry,
    /// and the path says it frees.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    public void APayloadUnderNineBytesIsDroppedAndFreed(int payloadSize)
    {
        TakionDataPushReading reading = TakionDataPush.Read(
            Datagram(Offset, payloadSize, 5, 1), Offset, payloadSize, TakionDataPush.ExpectedTypeB);

        Assert.Equal(TakionDataPushVerdict.TooShort, reading.Verdict);
        Assert.True(reading.FreesTheDatagram, "the dropped datagram is not released, which is the leak");
    }

    /// <summary>And exactly nine is the first that becomes an entry, so the bound is a boundary.</summary>
    [Fact]
    public void NineBytesIsTheFirstThatBecomesAnEntry()
    {
        TakionDataPushReading reading = TakionDataPush.Read(
            Datagram(Offset, TakionDataPush.DataHeaderSize, 7, 2),
            Offset, TakionDataPush.DataHeaderSize, TakionDataPush.ExpectedTypeB);

        Assert.Equal(TakionDataPushVerdict.Pushed, reading.Verdict);
        Assert.Equal(7u, reading.Entry.SeqNum);
        Assert.Equal(2, reading.Entry.Channel);
    }

    /// <summary>
    /// A pushed datagram is NOT freed here, which is the other half of the ownership rule.
    ///
    /// The entry owns it now. A model that freed on every path would be modelling a double free, and
    /// one that freed on none would be modelling the leak PP491 repaired.
    /// </summary>
    [Fact]
    public void APushedDatagramIsNotFreedHere()
    {
        TakionDataPushReading reading = TakionDataPush.Read(
            Datagram(Offset, 40, 9, 3), Offset, 40, TakionDataPush.ExpectedTypeB);

        Assert.Equal(TakionDataPushVerdict.Pushed, reading.Verdict);
        Assert.False(reading.FreesTheDatagram);
    }

    /// <summary>
    /// type_b is a WARNING and not a refusal: the message is pushed whatever it says.
    ///
    /// The C logs when it is not one and carries on. A port that dropped those would be quieter
    /// than the C, and no test of the happy path would show it.
    /// </summary>
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    [InlineData((byte)0xff)]
    public void AnUnexpectedTypeBWarnsAndIsStillPushed(byte typeB)
    {
        TakionDataPushReading reading = TakionDataPush.Read(Datagram(Offset, 24, 4, 1), Offset, 24, typeB);

        Assert.Equal(TakionDataPushVerdict.Pushed, reading.Verdict);
        Assert.True(reading.WarnedOnTypeB);
        Assert.Equal(typeB, reading.Entry.TypeB);
    }

    /// <summary>And the expected value warns about nothing.</summary>
    [Fact]
    public void TheExpectedTypeBIsQuiet()
    {
        TakionDataPushReading reading = TakionDataPush.Read(
            Datagram(Offset, 24, 4, 1), Offset, 24, TakionDataPush.ExpectedTypeB);

        Assert.False(reading.WarnedOnTypeB);
    }

    /// <summary>
    /// THE JOIN: a message read by PP673 becomes an entry on the wide queue and pulls back in order.
    ///
    /// Three data messages pushed out of order across the thirty-two-bit wrap, pulled in sequence.
    /// This is the whole of what PP674 owed - the branch, the parse, the entry, the queue - with
    /// nothing hand-built in between.
    /// </summary>
    [Fact]
    public void MessagesPushedOutOfOrderPullBackInSequence()
    {
        using ReorderQueue queue = ReorderQueue.Wide(4, 0xFFFFFFFE);

        foreach (uint seq in (uint[])[0x00000000, 0xFFFFFFFE, 0x00000001, 0xFFFFFFFF])
        {
            TakionDataPushReading reading = TakionDataPush.ReadAndPush(
                Datagram(Offset, 20, seq, 1), Offset, 20, TakionDataPush.ExpectedTypeB, queue);

            Assert.Equal(TakionDataPushVerdict.Pushed, reading.Verdict);
        }

        foreach (uint expected in (uint[])[0xFFFFFFFE, 0xFFFFFFFF, 0x00000000, 0x00000001])
        {
            (ulong SeqNum, long Payload)? pulled = queue.Pull();

            Assert.NotNull(pulled);
            Assert.Equal(expected, (uint)pulled.Value.SeqNum);
        }

        Assert.Null(queue.Pull());
    }

    /// <summary>A dropped message never reaches the queue, which is what the drop means.</summary>
    [Fact]
    public void ADroppedMessageIsNotPushed()
    {
        using ReorderQueue queue = ReorderQueue.Wide(4, 0);

        TakionDataPushReading reading = TakionDataPush.ReadAndPush(
            Datagram(Offset, 4, 0, 0), Offset, 4, TakionDataPush.ExpectedTypeB, queue);

        Assert.Equal(TakionDataPushVerdict.TooShort, reading.Verdict);
        Assert.Null(queue.Pull());
    }

    /// <summary>A payload naming bytes past the datagram is dropped, not read out of bounds.</summary>
    [Theory]
    [InlineData(40, 40)]
    [InlineData(0, 100)]
    [InlineData(-1, 20)]
    public void APayloadOutsideTheDatagramIsDropped(int payloadOffset, int payloadSize)
    {
        TakionDataPushReading reading = TakionDataPush.Read(
            new byte[48], payloadOffset, payloadSize, TakionDataPush.ExpectedTypeB);

        Assert.Equal(TakionDataPushVerdict.TooShort, reading.Verdict);
        Assert.True(reading.FreesTheDatagram);
    }

    /// <summary>And a null queue is refused rather than dropping the message silently.</summary>
    [Fact]
    public void ANullQueueIsRefused()
        => Assert.Throws<ArgumentNullException>(
            () => TakionDataPush.ReadAndPush(new byte[64], Offset, 24, 1, null!));
}
