using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP519, under PP27: what a console's key positions do, and the one input a plain comparison gets
/// wrong.
///
/// PP23 said what a bad expansion costs - the stream decrypts to noise four gigabytes in, far
/// enough from the start that nothing points at a counter - and its tests feed the expansion values
/// they chose. The first real capture supplied 2000 a PS5 sent, and twenty-six of them repeat.
/// </summary>
public class KeyPositionRepeatTests
{
    private static CapturedDatagram Datagram(int baseType, long at, uint keyPos)
    {
        var head = new byte[TakionTimingCapture.HeadBytes];
        head[0] = (byte)baseType;

        TakionMacLayout layout = TakionPacketMac.LayoutFor(baseType)!.Value;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            head.AsSpan(layout.KeyPosOffset, TakionPacketMac.KeyPosSize), keyPos);

        return new CapturedDatagram(at, 1300, baseType, head);
    }

    /// <summary>
    /// THE REPEAT: the same position twice is not a wrap, and the C says so.
    ///
    /// Both branches of the expansion use the RFC comparison, and neither gt nor lt is true of a
    /// value against itself - so the high half is untouched. An expansion testing low against prev
    /// with a plain comparison would add 2^32 here, on an input a console produces twenty-six times
    /// in two thousand packets.
    /// </summary>
    [Fact]
    public void TheSamePositionTwiceIsNotAWrap()
    {
        using var state = new KeyState();

        Assert.Equal(0x1000UL, state.RequestPos(0x1000));
        Assert.Equal(0x1000UL, state.RequestPos(0x1000));
        Assert.Equal(0x1010UL, state.RequestPos(0x1010));

        // And what the plain comparison would have concluded, named so the difference is legible.
        Assert.False(NativeSeqNum.Lt(0x1000u, 0x1000u));
        Assert.False(NativeSeqNum.Gt(0x1000u, 0x1000u));
    }

    /// <summary>A real wrap forward still raises the high half, which is what the branch is for.</summary>
    [Fact]
    public void ARealWrapStillRaisesTheHighHalf()
    {
        using var state = new KeyState();

        Assert.Equal(0xfffffff0UL, state.RequestPos(0xfffffff0));
        Assert.Equal(0x1_00000010UL, state.RequestPos(0x00000010));
    }

    /// <summary>And a packet arriving late from before the wrap lowers it again.</summary>
    [Fact]
    public void AReorderedPacketAcrossTheWrapLowersItAgain()
    {
        using var state = new KeyState();

        state.RequestPos(0xfffffff0);
        Assert.Equal(0x1_00000010UL, state.RequestPos(0x00000010));
        Assert.Equal(0xfffffff8UL, state.RequestPos(0xfffffff8));
    }

    /// <summary>
    /// The shape reader counts repeats, advances and alignment the way the capture reported them.
    ///
    /// Driven with a scripted stream rather than the file, so the reader is checked where the file
    /// cannot be - and the file's numbers are what the replay command prints.
    /// </summary>
    [Fact]
    public void TheShapeReaderCountsWhatItSays()
    {
        DatagramReplayReport.KeyPositionShape shape = DatagramReplayReport.KeyPositions(
        [
            Datagram(TakionDispatch.Video, 0, 0x1000),
            Datagram(TakionDispatch.Control, 1000, 0x1000),
            Datagram(TakionDispatch.Video, 2000, 0x1010),
            Datagram(TakionDispatch.Audio, 3000, 0x1020),
            Datagram(TakionDispatch.Video, 4000, 0x1025),
        ]);

        Assert.Equal(1, shape.RunningRepeats);
        Assert.Equal(0, shape.PrologueRepeats);
        Assert.Equal(3, shape.Advances);
        Assert.True(shape.Monotonic);

        // 0x1025 - 0x1020 is five, which is not a block.
        Assert.Equal(1, shape.NotBlockAligned);

        // Nothing here spans the low half's wrap, so nothing may enter the high half.
        Assert.Equal(0, shape.SpuriousWraps);
    }

    /// <summary>
    /// A stream that goes backwards is reported as such rather than silently ordered.
    ///
    /// The capture's positions are one stream only when sorted by ARRIVAL - a port keeping one
    /// ledger per channel would see three that each jump, and this is what would say so.
    /// </summary>
    [Fact]
    public void AStreamThatGoesBackwardsIsReported()
    {
        DatagramReplayReport.KeyPositionShape shape = DatagramReplayReport.KeyPositions(
        [
            Datagram(TakionDispatch.Video, 0, 0x2000),
            Datagram(TakionDispatch.Video, 1000, 0x1000),
        ]);

        Assert.False(shape.Monotonic);
    }

    /// <summary>
    /// PP521: a repeat at zero is the prologue; a repeat after the first real position is not.
    ///
    /// All twenty-six repeats in every capture taken so far are at position zero, in the packets
    /// before the cipher exists - and two independent sessions produced byte-identical breakdowns,
    /// which is what an opening does and a network does not. The reader has to tell the two apart,
    /// because a counter that stood still mid-stream would be a different and much worse thing.
    /// </summary>
    [Fact]
    public void APrologueRepeatIsNotARunningOne()
    {
        DatagramReplayReport.KeyPositionShape prologue = DatagramReplayReport.KeyPositions(
        [
            Datagram(TakionDispatch.Control, 0, 0),
            Datagram(TakionDispatch.Control, 1000, 0),
            Datagram(TakionDispatch.Video, 2000, 0),
            Datagram(TakionDispatch.Video, 3000, 16),
            Datagram(TakionDispatch.Video, 4000, 32),
        ]);

        Assert.Equal(3, prologue.Prologue);
        Assert.Equal(2, prologue.PrologueRepeats);
        Assert.Equal(0, prologue.RunningRepeats);
        Assert.Equal(2, prologue.Advances);
    }

    /// <summary>And a repeat after the cipher is counted as the one that matters.</summary>
    [Fact]
    public void ARepeatAfterTheFirstRealPositionIsReportedApart()
    {
        DatagramReplayReport.KeyPositionShape running = DatagramReplayReport.KeyPositions(
        [
            Datagram(TakionDispatch.Video, 0, 0),
            Datagram(TakionDispatch.Video, 1000, 16),
            Datagram(TakionDispatch.Video, 2000, 16),
        ]);

        Assert.Equal(1, running.Prologue);
        Assert.Equal(0, running.PrologueRepeats);
        Assert.Equal(1, running.RunningRepeats);
    }

    /// <summary>
    /// The block the alignment is measured against is the cipher's, read from the header.
    ///
    /// PP495 took the same number from gkcrypt.h for the ledger's arithmetic; this is the same
    /// sixteen, and a capture whose advances stopped being multiples of it would be saying the
    /// console changed something.
    /// </summary>
    [Fact]
    public void TheBlockIsTheCiphersOwn()
    {
        if (TakionKeyPositionSource.LocateCrypt() is not { } path)
            return;

        Assert.Equal(
            (long?)TakionKeyPosition.BlockSize,
            TakionKeyPositionSource.BlockSizeIn(File.ReadAllText(path)));
    }
}
