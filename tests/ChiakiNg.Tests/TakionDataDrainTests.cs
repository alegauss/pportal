using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP493, under PP27: the data queue's drain - four outcomes per entry and one ack for all of them.
///
/// PP491 fixed the path in. This is the path out, and its one surprising property is that the
/// acknowledgement is decided before anything is understood.
/// </summary>
public class TakionDataDrainTests
{
    /// <summary>A payload of <paramref name="length"/> bytes carrying <paramref name="dataType"/>.</summary>
    private static byte[] Payload(int length, byte dataType = 0, byte reservedHigh = 0)
    {
        var payload = new byte[length];
        if (length > TakionDataDrain.DataTypeOffset)
            payload[TakionDataDrain.DataTypeOffset] = dataType;
        if (length > TakionDataDrain.ReservedOffset)
            payload[TakionDataDrain.ReservedOffset] = reservedHigh;

        // A body that is distinguishable from the header, so a slice off by one is visible.
        for (int i = TakionDataDrain.HeaderSize; i < length; i++)
            payload[i] = (byte)(i - TakionDataDrain.HeaderSize + 1);

        return payload;
    }

    /// <summary>Nothing pulled means no ack at all, which is the drain's only silent exit.</summary>
    [Fact]
    public void AnEmptyQueueSendsNoAck()
    {
        TakionDrainOutcomeSet drained = TakionDataDrain.Drain([]);

        Assert.False(drained.Acked);
        Assert.Empty(drained.Outcomes);
        Assert.Empty(drained.Deliveries);
    }

    /// <summary>The body handed on starts past the nine-byte header, not at the payload.</summary>
    [Fact]
    public void ADeliveredMessageStartsPastItsHeader()
    {
        TakionDrainOutcomeSet drained = TakionDataDrain.Drain(
            [new TakionDataEntry(4, Payload(13, dataType: (byte)TakionDataType.Rumble))]);

        TakionDelivery delivery = Assert.Single(drained.Deliveries);
        Assert.Equal(TakionDataType.Rumble, delivery.DataType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, delivery.Body);
        Assert.Equal([TakionDrainOutcome.Delivered], drained.Outcomes);
    }

    /// <summary>All four known types are handed on, and their values are the C's.</summary>
    [Theory]
    [InlineData(0, TakionDataType.Protobuf)]
    [InlineData(7, TakionDataType.Rumble)]
    [InlineData(9, TakionDataType.PadInfo)]
    [InlineData(11, TakionDataType.TriggerEffects)]
    public void TheFourKnownTypesAreDelivered(byte wire, TakionDataType expected)
    {
        TakionDrainOutcomeSet drained =
            TakionDataDrain.Drain([new TakionDataEntry(1, Payload(12, wire))]);

        Assert.Equal(expected, Assert.Single(drained.Deliveries).DataType);
        Assert.True(TakionDataDrain.IsKnown(wire));
    }

    /// <summary>And everything between and around them is not, including the neighbours.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(0xff)]
    public void AnUnknownTypeIsNotDelivered(byte wire)
    {
        TakionDrainOutcomeSet drained =
            TakionDataDrain.Drain([new TakionDataEntry(1, Payload(12, wire))]);

        Assert.False(TakionDataDrain.IsKnown(wire));
        Assert.Empty(drained.Deliveries);
        Assert.Equal([TakionDrainOutcome.UnknownType], drained.Outcomes);
    }

    /// <summary>
    /// THE CLAIM: an entry nothing was done with still acks, and the ack carries the last sequence
    /// number pulled rather than the last one delivered.
    ///
    /// Three entries, none of them delivered - two unknown types and one too short - and the ack
    /// still goes with the third's number. The flag is set on the pull, so this is what "acknowledge
    /// the whole prefix" means when the prefix was all rubbish.
    /// </summary>
    [Fact]
    public void ADrainThatDeliveredNothingStillAcksTheLastPulled()
    {
        TakionDrainOutcomeSet drained = TakionDataDrain.Drain(
        [
            new TakionDataEntry(11, Payload(12, dataType: 0x40)),
            new TakionDataEntry(12, Payload(12, dataType: 0x41)),
            new TakionDataEntry(13, Payload(4)),
        ]);

        Assert.Empty(drained.Deliveries);
        Assert.True(drained.Acked);
        Assert.Equal(13u, drained.AckSeqNum);
        Assert.Equal(
            [TakionDrainOutcome.UnknownType, TakionDrainOutcome.UnknownType, TakionDrainOutcome.TooShort],
            drained.Outcomes);
    }

    /// <summary>
    /// And the number is the LAST pulled, not the last delivered - which is the same value only
    /// when the final entry happened to be a good one.
    /// </summary>
    [Fact]
    public void TheAckNumberIsTheLastPulledNotTheLastDelivered()
    {
        TakionDrainOutcomeSet drained = TakionDataDrain.Drain(
        [
            new TakionDataEntry(20, Payload(12, dataType: (byte)TakionDataType.Protobuf)),
            new TakionDataEntry(21, Payload(12, dataType: 0x33)),
        ]);

        Assert.Single(drained.Deliveries);
        Assert.Equal(21u, drained.AckSeqNum);
    }

    /// <summary>
    /// A session with no callback drops every known-type message and acks it anyway.
    ///
    /// The delivery is the else-if's BODY in the C, not a step after it, so this is not an error
    /// path - it is what happens between the queue existing and the callback being wired.
    /// </summary>
    [Fact]
    public void WithNoCallbackAKnownTypeIsDroppedAndStillAcked()
    {
        TakionDrainOutcomeSet drained = TakionDataDrain.Drain(
            [new TakionDataEntry(5, Payload(12, dataType: (byte)TakionDataType.PadInfo))],
            hasCallback: false);

        Assert.Empty(drained.Deliveries);
        Assert.Equal([TakionDrainOutcome.NoCallback], drained.Outcomes);
        Assert.True(drained.Acked);
        Assert.Equal(5u, drained.AckSeqNum);
    }

    /// <summary>
    /// A payload of exactly nine bytes is delivered with an empty body - the boundary the guard
    /// sits on, and the one an off-by-one would move.
    /// </summary>
    [Fact]
    public void ANineBytePayloadIsDeliveredWithNothingInIt()
    {
        TakionDrainOutcomeSet drained = TakionDataDrain.Drain(
            [new TakionDataEntry(1, Payload(9, dataType: (byte)TakionDataType.Protobuf))]);

        Assert.Empty(Assert.Single(drained.Deliveries).Body);
        Assert.Equal([TakionDrainOutcome.Delivered], drained.Outcomes);
    }

    /// <summary>Eight bytes is one short, and is dropped before its type is looked at.</summary>
    [Fact]
    public void AnEightBytePayloadIsTooShort()
    {
        TakionDrainOutcomeSet drained = TakionDataDrain.Drain([new TakionDataEntry(1, Payload(8))]);

        Assert.Equal([TakionDrainOutcome.TooShort], drained.Outcomes);
        Assert.True(drained.Acked);
    }

    /// <summary>The reserved halfword is counted and changes nothing else, as in the C.</summary>
    [Fact]
    public void ANonzeroReservedHalfwordIsNotedAndDeliveredAnyway()
    {
        TakionDrainOutcomeSet drained = TakionDataDrain.Drain(
        [
            new TakionDataEntry(1, Payload(12, (byte)TakionDataType.Rumble, reservedHigh: 0x5a)),
        ]);

        Assert.Equal(1, drained.NonzeroAtSix);
        Assert.Single(drained.Deliveries);
    }

    /// <summary>
    /// THE DRIFT CHECK: the C still places the ack flag on the pull and the send after the loop.
    ///
    /// The first is the whole claim. The second is what keeps it from becoming an ack per packet on
    /// a channel that carries thousands of them.
    /// </summary>
    [Fact]
    public void TheCStillAcksOnceForTheWholeDrain()
    {
        if (TakionDataDrainSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);
        string flush = Assert.IsType<string>(TakionDataDrainSource.FlushBody(source));

        Assert.True(TakionDataDrainSource.TheAckFlagIsSetOnThePull(flush));
        Assert.True(TakionDataDrainSource.TheAckIsSentOnceAfterTheLoop(flush));
        Assert.True(TakionDataDrainSource.TheCallbackIsTheElseIf(flush));
        Assert.True(TakionDataDrainSource.TheFourTypesAreStillTheOnesAccepted(flush));
        Assert.True(TakionDataDrainSource.TheShortPayloadBranchIsGuardedUpstreamToo(source));
    }

    /// <summary>
    /// The four type values still match the header this port compiled against.
    ///
    /// The enum's values are the wire's, so a renumbering upstream would silently deliver rumble as
    /// pad info rather than fail to build.
    /// </summary>
    [Fact]
    public void TheFourTypeValuesStillMatchTheHeader()
    {
        if (ChiakiNg.Session.SanitizerSource.LocateRelative(@"lib\include\chiaki\takion.h") is not { } path)
            return;

        string header = File.ReadAllText(path);

        Assert.Contains($"PROTOBUF = {(int)TakionDataType.Protobuf},", header, StringComparison.Ordinal);
        Assert.Contains($"RUMBLE = {(int)TakionDataType.Rumble},", header, StringComparison.Ordinal);
        Assert.Contains($"PAD_INFO = {(int)TakionDataType.PadInfo},", header, StringComparison.Ordinal);
        Assert.Contains(
            $"TRIGGER_EFFECTS = {(int)TakionDataType.TriggerEffects},", header, StringComparison.Ordinal);
    }
}
