using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP494, under PP27: the data ack arriving, and why the selective path in it cannot run.
///
/// The interesting assertion is arithmetic rather than behavioural: for every gap-ack block count
/// above zero, the payload that would carry it is not twelve bytes - so the first check refuses it
/// and the branch that names gap acks is never the one that rejects them.
/// </summary>
public class TakionDataAckTests
{
    /// <summary>A well-formed twelve-byte ack payload.</summary>
    private static byte[] Ack(uint cumulative, uint window = 0, ushort gaps = 0, ushort dups = 0)
    {
        var payload = new byte[TakionDataAck.Size];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), cumulative);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), window);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8, 2), gaps);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(10, 2), dups);
        return payload;
    }

    /// <summary>The four fields come off the wire big-endian, at the offsets the C reads.</summary>
    [Fact]
    public void TheFourFieldsAreReadWhereTheCReadsThem()
    {
        TakionAckRead read = TakionDataAck.Read(
            Ack(cumulative: 0x11223344, window: 0x55667788, gaps: 0, dups: 0x99aa));

        Assert.Equal(TakionAckVerdict.Accepted, read.Verdict);
        Assert.Equal(0x11223344u, read.Fields.CumulativeSeqNum);
        Assert.Equal(0x55667788u, read.Fields.AdvertisedWindow);
        Assert.Equal((ushort)0, read.Fields.GapAckBlocks);
        Assert.Equal((ushort)0x99aa, read.Fields.DuplicateTsns);
    }

    /// <summary>
    /// THE CLAIM, as arithmetic: a payload carrying any gap-ack blocks is never twelve bytes, so
    /// the first check refuses it and the second never sees it.
    ///
    /// The verdict for such a packet is WrongSize - the size mismatch log - and not the warning
    /// about an invalid count, which is the message a reader would expect to see for it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(64)]
    public void APacketActuallyCarryingGapBlocksIsRefusedForItsSize(int blocks)
    {
        int size = TakionDataAck.PayloadSizeFor(blocks);
        Assert.NotEqual(TakionDataAck.Size, size);

        var payload = new byte[size];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8, 2), (ushort)blocks);

        Assert.Equal(TakionAckVerdict.WrongSize, TakionDataAck.Read(payload).Verdict);
    }

    /// <summary>
    /// And the branch that names gap acks fires only for a console claiming blocks it did not send.
    ///
    /// Twelve bytes with a nonzero count - the only input that reaches the second check and fails
    /// it, which is what makes that check a test of the count rather than of the size.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0xffff)]
    public void TwelveBytesClaimingBlocksIsTheOnlyWayToReachTheSecondCheck(int gaps)
    {
        TakionAckRead read = TakionDataAck.Read(Ack(cumulative: 5, gaps: (ushort)gaps));

        Assert.Equal(TakionAckVerdict.GapBlocksClaimed, read.Verdict);
        Assert.Equal((ushort)gaps, read.Fields.GapAckBlocks);
    }

    /// <summary>Anything short of twelve is refused for its size too, including empty.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(13)]
    public void AnyOtherSizeIsRefused(int size)
        => Assert.Equal(TakionAckVerdict.WrongSize, TakionDataAck.Read(new byte[size]).Verdict);

    /// <summary>
    /// An accepted ack releases the send buffer's prefix, and every released number is its own
    /// event - not one event for the ack.
    /// </summary>
    [Fact]
    public void OneAckReleasesThePrefixAndRaisesOneEventEach()
    {
        var buffer = new TakionSendBuffer();
        foreach (uint seqNum in new uint[] { 10, 11, 12, 13 })
            Assert.Equal(ChiakiNg.Native.ChiakiError.Success, buffer.Push(seqNum, size: 32));

        IReadOnlyList<uint> released =
            TakionDataAck.Apply(buffer, TakionDataAck.Read(Ack(cumulative: 12)));

        Assert.Equal([10u, 11u, 12u], released);
        Assert.Equal([13u], buffer.SeqNums);
    }

    /// <summary>A refused ack releases nothing, so the send buffer keeps resending.</summary>
    [Theory]
    [InlineData(TakionAckVerdict.WrongSize)]
    [InlineData(TakionAckVerdict.GapBlocksClaimed)]
    public void ARefusedAckReleasesNothing(TakionAckVerdict verdict)
    {
        var buffer = new TakionSendBuffer();
        Assert.Equal(ChiakiNg.Native.ChiakiError.Success, buffer.Push(7, size: 8));

        byte[] payload = verdict == TakionAckVerdict.WrongSize
            ? new byte[TakionDataAck.Size + TakionDataAck.GapAckBlockSize]
            : Ack(cumulative: 7, gaps: 1);

        TakionAckRead read = TakionDataAck.Read(payload);
        Assert.Equal(verdict, read.Verdict);

        Assert.Empty(TakionDataAck.Apply(buffer, read));
        Assert.Equal([7u], buffer.SeqNums);
    }

    /// <summary>
    /// The window the console advertises changes nothing, which is the point of calling it dropped.
    ///
    /// Two acks identical but for a_rwnd release exactly the same messages.
    /// </summary>
    [Fact]
    public void TheAdvertisedWindowChangesNothing()
    {
        IReadOnlyList<uint> Released(uint window)
        {
            var buffer = new TakionSendBuffer();
            foreach (uint seqNum in new uint[] { 1, 2, 3 })
                buffer.Push(seqNum, size: 16);

            return TakionDataAck.Apply(buffer, TakionDataAck.Read(Ack(cumulative: 2, window: window)));
        }

        Assert.Equal(Released(0), Released(0x0010_0000));
    }

    /// <summary>
    /// THE DRIFT CHECK: the C still orders the two size checks the way the claim needs, still
    /// releases on the cumulative number, and still leaves the two other fields to the log.
    /// </summary>
    [Fact]
    public void TheCStillOrdersTheTwoChecksThisWay()
    {
        if (TakionDataAckSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);
        string ack = Assert.IsType<string>(TakionDataAckSource.AckBody(source));

        Assert.True(TakionDataAckSource.TheFixedSizeCheckComesFirst(ack));
        Assert.True(TakionDataAckSource.TheCumulativeNumberIsWhatReleases(ack));
        Assert.True(TakionDataAckSource.TheWindowAndDuplicatesAreOnlyLogged(ack));
    }

    /// <summary>
    /// And the callback here is still the one call site in the file with no null test.
    ///
    /// Unreachable - senkusha and streamconnection both set one - and asserted as it is so that the
    /// model's note about it cannot quietly stop being true.
    /// </summary>
    [Fact]
    public void TheCallbackHereIsStillUnguarded()
    {
        if (TakionDataAckSource.Locate() is not { } path)
            return;

        string ack = Assert.IsType<string>(
            TakionDataAckSource.AckBody(File.ReadAllText(path)));

        Assert.True(TakionDataAckSource.TheCallbackIsUnguardedHere(ack));
    }
}
