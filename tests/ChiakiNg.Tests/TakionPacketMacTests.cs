using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP497, under PP27: the MAC gate, which rewrites the packet it is asked about.
///
/// The two things worth pinning are that the blanking happens whether or not there is a cipher,
/// and that a second field is blanked for two of the four types and not the others.
/// </summary>
public class TakionPacketMacTests
{
    /// <summary>A packet of <paramref name="size"/> bytes of the given base type, all distinct.</summary>
    private static byte[] Packet(int baseType, int size)
    {
        var packet = new byte[size];
        packet[0] = (byte)baseType;
        for (var i = 1; i < size; i++)
            packet[i] = (byte)(i + 0x40);

        return packet;
    }

    /// <summary>A GMAC that ignores its input, so a test can tell "computed" from "blanked".</summary>
    private static byte[] Stamp(ReadOnlyMemory<byte> _) => [0xde, 0xad, 0xbe, 0xef];

    /// <summary>The four types both switches answer for, and their layouts.</summary>
    [Theory]
    [InlineData(TakionDispatch.Control, 5, 0x9, true)]
    [InlineData(TakionDispatch.Video, 0xa, 0xe, false)]
    [InlineData(TakionDispatch.Audio, 0xa, 0xe, false)]
    [InlineData(TakionPacketMac.Congestion, 7, 0xb, true)]
    public void TheFourTypesHaveTheseLayouts(int baseType, int mac, int keyPos, bool blanked)
    {
        TakionMacLayout? found = TakionPacketMac.LayoutFor(baseType);
        Assert.NotNull(found);
        TakionMacLayout layout = found.Value;

        Assert.Equal(mac, layout.MacOffset);
        Assert.Equal(keyPos, layout.KeyPosOffset);
        Assert.Equal(blanked, layout.KeyPosIsBlanked);
    }

    /// <summary>
    /// THE INVARIANT NEITHER SWITCH STATES: the key position begins exactly where the MAC ends.
    ///
    /// True for all four types, which is why the six numbers are one rule and not six facts.
    /// </summary>
    [Fact]
    public void TheKeyPositionAlwaysStartsWhereTheMacEnds()
    {
        foreach (int baseType in TakionPacketMac.TypesWithOffsets)
        {
            TakionMacLayout? found = TakionPacketMac.LayoutFor(baseType);
            Assert.NotNull(found);

            Assert.Equal(found.Value.MacOffset + TakionPacketMac.GmacSize, found.Value.KeyPosOffset);
        }
    }

    /// <summary>Anything else has no layout, which is the C's -1 from either switch.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(0xf)]
    public void EveryOtherTypeHasNoOffsets(int baseType)
        => Assert.Null(TakionPacketMac.LayoutFor(baseType));

    /// <summary>
    /// THE REWRITE: with no cipher the MAC field is still zeroed, and the call still succeeds.
    ///
    /// This is how a handshake packet leaves - four zeroes where its MAC will later be - and it is
    /// also why calling this to "check" a packet without a cipher would destroy the MAC it meant
    /// to compare.
    /// </summary>
    [Fact]
    public void WithNoCipherTheMacFieldIsStillZeroed()
    {
        byte[] packet = Packet(TakionDispatch.Control, 20);
        byte[] originalMac = packet[5..9];

        TakionPacketMac.MacResult result = TakionPacketMac.Apply(packet, gmac: null);

        Assert.Equal(ChiakiError.Success, result.Error);
        Assert.Equal(originalMac, result.MacBefore);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, packet[5..9]);
        Assert.False(result.KeyPosWasBlanked);
    }

    /// <summary>With a cipher the computed MAC lands in the same four bytes it blanked.</summary>
    [Fact]
    public void TheComputedMacLandsInTheFieldItBlanked()
    {
        byte[] packet = Packet(TakionDispatch.Video, 24);

        TakionPacketMac.MacResult result = TakionPacketMac.Apply(packet, Stamp);

        Assert.Equal(ChiakiError.Success, result.Error);
        Assert.Equal(new byte[] { 0xde, 0xad, 0xbe, 0xef }, packet[0xa..0xe]);
        Assert.Equal(packet[0xa..0xe], result.MacAfter);
    }

    /// <summary>
    /// THE SECOND BLANKING: control and congestion hide their key position from the GMAC and put it
    /// back; video and audio do not hide theirs at all.
    ///
    /// Asserted on the restored bytes AND on what the GMAC was shown, because getting the restore
    /// right while blanking the wrong types would pass a check on the packet alone.
    /// </summary>
    [Theory]
    [InlineData(TakionDispatch.Control, 0x9, true)]
    [InlineData(TakionPacketMac.Congestion, 0xb, true)]
    [InlineData(TakionDispatch.Video, 0xe, false)]
    [InlineData(TakionDispatch.Audio, 0xe, false)]
    public void OnlyControlAndCongestionHideTheirKeyPosition(int baseType, int keyPos, bool blanked)
    {
        byte[] packet = Packet(baseType, 32);
        byte[] originalKeyPos = packet[keyPos..(keyPos + 4)];

        byte[]? seen = null;
        TakionPacketMac.MacResult result = TakionPacketMac.Apply(
            packet,
            bytes =>
            {
                seen = bytes.ToArray()[keyPos..(keyPos + 4)];
                return Stamp(bytes);
            });

        Assert.Equal(ChiakiError.Success, result.Error);
        Assert.Equal(blanked, result.KeyPosWasBlanked);

        // Restored either way, so the packet on the wire is unchanged in that field.
        Assert.Equal(originalKeyPos, packet[keyPos..(keyPos + 4)]);

        // But what the GMAC saw differs, and that is the whole of it.
        Assert.Equal(blanked ? new byte[] { 0, 0, 0, 0 } : originalKeyPos, seen);
    }

    /// <summary>A packet too short for its own key position field is refused, not read past.</summary>
    [Theory]
    [InlineData(TakionDispatch.Control, 12)]
    [InlineData(TakionDispatch.Video, 17)]
    [InlineData(TakionPacketMac.Congestion, 14)]
    public void APacketTooShortForItsFieldsIsRefused(int baseType, int size)
    {
        Assert.Equal(ChiakiError.BufTooSmall, TakionPacketMac.Apply(Packet(baseType, size), Stamp).Error);
        Assert.Equal(
            ChiakiError.BufTooSmall, TakionPacketMac.ReadKeyPosition(Packet(baseType, size), out _));
    }

    /// <summary>And one byte more is enough, which is where the bound actually sits.</summary>
    [Theory]
    [InlineData(TakionDispatch.Control, 13)]
    [InlineData(TakionDispatch.Video, 18)]
    [InlineData(TakionPacketMac.Congestion, 15)]
    public void OneByteMoreIsEnough(int baseType, int size)
    {
        Assert.Equal(ChiakiError.Success, TakionPacketMac.Apply(Packet(baseType, size), Stamp).Error);
        Assert.Equal(ChiakiError.Success, TakionPacketMac.ReadKeyPosition(Packet(baseType, size), out _));
    }

    /// <summary>An empty packet is too small; an unknown type is invalid data. Two errors, not one.</summary>
    [Fact]
    public void EmptyIsTooSmallAndUnknownIsInvalid()
    {
        Assert.Equal(ChiakiError.BufTooSmall, TakionPacketMac.Apply(Array.Empty<byte>(), Stamp).Error);
        Assert.Equal(ChiakiError.BufTooSmall, TakionPacketMac.ReadKeyPosition(Array.Empty<byte>(), out _));

        Assert.Equal(ChiakiError.InvalidData, TakionPacketMac.Apply(Packet(6, 32), Stamp).Error);
        Assert.Equal(ChiakiError.InvalidData, TakionPacketMac.ReadKeyPosition(Packet(6, 32), out _));
    }

    /// <summary>The key position is read big-endian from its own offset.</summary>
    [Fact]
    public void TheKeyPositionIsReadBigEndianFromItsOffset()
    {
        byte[] packet = Packet(TakionDispatch.Control, 20);
        packet[0x9] = 0x11;
        packet[0xa] = 0x22;
        packet[0xb] = 0x33;
        packet[0xc] = 0x44;

        Assert.Equal(ChiakiError.Success, TakionPacketMac.ReadKeyPosition(packet, out uint keyPos));
        Assert.Equal(0x11223344u, keyPos);
    }

    /// <summary>
    /// Congestion has offsets but no arm in PP490's dispatch, which is the right pairing: this
    /// client sends congestion packets and never receives one.
    /// </summary>
    [Fact]
    public void CongestionHasOffsetsButNoDispatchArm()
    {
        Assert.NotNull(TakionPacketMac.LayoutFor(TakionPacketMac.Congestion));

        Assert.Equal(
            TakionDispatchBranch.UnknownType,
            TakionDispatch.Decide(
                TakionPacketMac.Congestion, macOk: true, enableCrypt: true, cryptAvailable: true).Branch);
    }

    /// <summary>
    /// THE DRIFT CHECK: the six offsets are read out of the C, not transcribed - and the two
    /// blankings are still where they are.
    /// </summary>
    [Fact]
    public void TheCsOffsetsAndBlankingAreStillThese()
    {
        if (TakionPacketMacSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);
        string macSwitch = Assert.IsType<string>(TakionPacketMacSource.MacOffsetBody(source));
        string keySwitch = Assert.IsType<string>(TakionPacketMacSource.KeyPosOffsetBody(source));

        foreach ((int baseType, string name) in new[]
        {
            (TakionDispatch.Control, "TAKION_PACKET_TYPE_CONTROL"),
            (TakionDispatch.Video, "TAKION_PACKET_TYPE_VIDEO"),
            (TakionPacketMac.Congestion, "TAKION_PACKET_TYPE_CONGESTION"),
        })
        {
            TakionMacLayout? found = TakionPacketMac.LayoutFor(baseType);
            Assert.NotNull(found);

            Assert.Equal((int?)found.Value.MacOffset, TakionPacketMacSource.OffsetFor(macSwitch, name));
            Assert.Equal((int?)found.Value.KeyPosOffset, TakionPacketMacSource.OffsetFor(keySwitch, name));
        }

        string mac = Assert.IsType<string>(TakionPacketMacSource.MacBody(source));
        Assert.True(TakionPacketMacSource.TheMacIsBlankedBeforeTheCipherIsTested(mac));
        Assert.True(TakionPacketMacSource.TheKeyPosIsBlankedForTwoTypesAndRestored(mac));
    }

    /// <summary>The GMAC's size is the header's, not a four typed into this port.</summary>
    [Fact]
    public void TheGmacSizeIsTheHeaders()
    {
        if (TakionPacketMacSource.LocateCrypt() is not { } path)
            return;

        Assert.Equal(
            TakionPacketMac.GmacSize, TakionPacketMacSource.GmacSizeIn(File.ReadAllText(path)));
    }
}
