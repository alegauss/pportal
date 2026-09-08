using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP813: the ping this port puts on the wire, against the one the C puts on it.
///
/// A live run on 2026-09-08 settled the half §PP813 said was outside this tree: the C's senkusha
/// gets its pongs back from this console at 1.6ms, so the console DOES echo. The port's run sends
/// its ten and receives none, and the two differ somewhere between the buffer and the socket.
///
/// This is the whole datagram compared, not the header - the C sends `data[0x224]` in full and so
/// does the port, so anything after the header is part of what the console is asked to echo.
/// </summary>
public class SenkushaPingBytesTests(ITestOutputHelper output)
{
    /// <summary>The ping the port sends, formatted by the port's own writer.</summary>
    private static byte[] Ours(ushort testIndex, ushort pingIndex, uint tag)
    {
        var packet = new byte[SenkushaMeasurements.PingBufferBytes];

        Assert.True(ManagedSenkushaRunHost.WritePing(packet, testIndex, pingIndex, tag, out _));

        return packet;
    }

    /// <summary>
    /// And the one senkusha.c sends, built through the C's own formatter over the shim.
    /// </summary>
    /// <remarks>
    /// senkusha_run_rtt_test's own five assignments and nothing else: the struct is zeroed, so
    /// every field this does not name is zero on the C's side too.
    /// </remarks>
    private static byte[]? Theirs(ushort testIndex, ushort pingIndex, uint tag)
    {
        // The guard every differential in this project spells, and not IsAvailable(): PP27's
        // deletion takes the wrapper with it, and a test that asked the wrapper instead of the
        // build would go red on the commit that removes what it is comparing against.
        if (ShimTakionShape.WrappingHeader() is null)
            return null;

        var packet = new byte[SenkushaMeasurements.PingBufferBytes];

        var header = new V7AvHeader(
            IsVideo: false,
            UsesNaluInfoStructs: false,
            PacketIndex: 0,
            FrameIndex: testIndex,
            UnitIndex: pingIndex,
            UnitsInFrameTotal: SenkushaMeasurements.PingUnitsInFrameTotal,
            UnitsInFrameFec: 0,
            Codec: SenkushaMeasurements.PingCodec,
            WordAt0x18: 0,
            AdaptiveStreamIndex: 0,
            KeyPos: 0,
            DataOffset: 0,
            DataSize: 0);

        if (NativeAvPacketV7.FormatHeader(packet, header, out int headerSize) != ChiakiError.Success)
            return null;

        // *((uint32_t *)(data + header_size + 4)) = htonl(tag);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            packet.AsSpan(headerSize + SenkushaMeasurements.TagOffsetPastHeader, 4), tag);

        return packet;
    }

    /// <summary>
    /// THE WHOLE DATAGRAM, byte for byte, over the ten indices a round-trip test actually sends.
    ///
    /// If these agree the ping is not why the console answers the C and not this port, and the
    /// difference is somewhere the bytes cannot show - which is worth knowing precisely because
    /// §PP813 has already read the formatting eight times.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void ThePingIsTheCsByteForByte(int pingIndex)
    {
        const uint Tag = 0x0BADF00D;

        if (Theirs(0, (ushort)pingIndex, Tag) is not { } theirs)
            return;

        byte[] ours = Ours(0, (ushort)pingIndex, Tag);

        output.WriteLine($"ours   {Convert.ToHexString(ours.AsSpan(0, 32))}");
        output.WriteLine($"theirs {Convert.ToHexString(theirs.AsSpan(0, 32))}");

        Assert.Equal(theirs, ours);
    }

    /// <summary>And it is the length the C sends, which is the whole buffer and not the header.</summary>
    [Fact]
    public void ThePingIsTheWholeBuffer()
    {
        Assert.Equal(0x224, SenkushaMeasurements.PingBufferBytes);
        Assert.Equal(0x224, Ours(0, 0, 1).Length);
    }
}
