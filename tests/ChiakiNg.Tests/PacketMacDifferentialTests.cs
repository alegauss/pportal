using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP517, under PP27: PP497's MAC gate model run against the C it models.
///
/// PP497 asserted the gate by reading it - six offsets, two fields blanked, and the asymmetry that
/// hides a key position from the GMAC for control and congestion and not for AV. Every one of those
/// assertions is about the C's TEXT. chiaki_takion_packet_mac is exported and the shim already
/// wraps it, so the thing being modelled has been callable all along.
///
/// A text check says the line still reads that way. Running both says they still DO the same thing,
/// which is what catches a model that blanks the wrong field for a type - PP497 named that as the
/// mistake most likely to survive review, and it survives every string comparison too.
/// </summary>
public class PacketMacDifferentialTests
{
    /// <summary>A packet whose every byte is distinct, so a wrong offset shows as wrong bytes.</summary>
    private static byte[] Packet(int baseType, int size)
    {
        var packet = new byte[size];
        if (size > 0)
            packet[0] = (byte)baseType;

        for (var i = 1; i < size; i++)
            packet[i] = (byte)(i * 7 + 0x11);

        return packet;
    }

    /// <summary>
    /// THE SWEEP: every base type against every length, both implementations, three comparisons.
    ///
    /// The verdict, the bytes copied out, and the packet each leaves behind. All three, because a
    /// pair can agree on the return value and differ on which four bytes they zeroed - which is the
    /// difference that matters and the one a verdict alone hides.
    /// </summary>
    [Fact]
    public void TheModelAndTheCAgreeOnEveryTypeAndLength()
    {
        var compared = 0;

        for (var baseType = 0; baseType <= TakionDispatch.BaseTypeMask; baseType++)
        {
            for (var size = 0; size <= 64; size++)
            {
                byte[] mine = Packet(baseType, size);
                byte[] theirs = Packet(baseType, size);

                TakionPacketMac.MacResult managed =
                    TakionPacketMac.Apply(mine, gmac: null, wantMacBefore: true);

                ChiakiError native = Takion.PacketMacWithoutCrypt(theirs, keyPos: 0, out byte[]? before);

                Assert.Equal(managed.Error, native);
                Assert.Equal(managed.MacBefore, before);
                Assert.Equal(theirs, mine);

                compared++;
            }
        }

        // PP271: a sweep that compared nothing would agree with anything.
        Assert.Equal(16 * 65, compared);
    }

    /// <summary>
    /// And they agree that the same inputs are refused, for the same reason.
    ///
    /// Two different refusals - too small and unknown type - so a pair returning one error for both
    /// would be caught rather than counted as agreement.
    /// </summary>
    [Fact]
    public void TheTwoRefusalsAgreeAndAreDistinct()
    {
        var seen = new HashSet<ChiakiError>();

        foreach ((int baseType, int size) in new[] { (0, 0), (0, 12), (2, 17), (6, 32), (1, 40) })
        {
            byte[] mine = Packet(baseType, size);
            byte[] theirs = Packet(baseType, size);

            ChiakiError managed = TakionPacketMac.Apply(mine, gmac: null).Error;
            ChiakiError native = Takion.PacketMacWithoutCrypt(theirs, 0, out _);

            Assert.Equal(managed, native);
            Assert.NotEqual(ChiakiError.Success, managed);
            seen.Add(managed);
        }

        // Ordered by the enum's values, which are libchiaki's: InvalidData is 11 and BufTooSmall 12.
        Assert.Equal(
            [ChiakiError.InvalidData, ChiakiError.BufTooSmall], seen.Order());
    }

    /// <summary>
    /// The C zeroes exactly the four bytes PP497 says it does, at the offset PP497 says.
    ///
    /// Read off the C's own output rather than from the model: the bytes that changed are found by
    /// comparing before and after, and the span they form is compared against the layout.
    /// </summary>
    [Theory]
    [InlineData(TakionDispatch.Control, 32)]
    [InlineData(TakionDispatch.Video, 32)]
    [InlineData(TakionDispatch.Audio, 32)]
    [InlineData(TakionPacketMac.Congestion, 32)]
    public void TheCZeroesTheFieldTheLayoutNames(int baseType, int size)
    {
        byte[] original = Packet(baseType, size);
        byte[] after = Packet(baseType, size);

        Assert.Equal(ChiakiError.Success, Takion.PacketMacWithoutCrypt(after, 0, out _));

        int[] changed = [.. Enumerable.Range(0, size).Where(i => original[i] != after[i])];

        TakionMacLayout? found = TakionPacketMac.LayoutFor(baseType);
        Assert.NotNull(found);

        // Every changed byte is inside the MAC field, and the field is now zero - the key position
        // is restored, so it never shows up as changed even where it was blanked for the GMAC.
        Assert.Equal(
            Enumerable.Range(found.Value.MacOffset, TakionPacketMac.GmacSize).ToArray(), changed);
        Assert.All(changed, i => Assert.Equal(0, after[i]));
    }

    /// <summary>
    /// The key position survives the call for every type, which is what "restored" means.
    ///
    /// PP497's asymmetry is about what the GMAC SEES, not about what the packet keeps. With no
    /// cipher there is no GMAC at all, so the packet must come back with its key position intact
    /// whether or not that type's would have been hidden.
    /// </summary>
    [Theory]
    [InlineData(TakionDispatch.Control)]
    [InlineData(TakionDispatch.Video)]
    [InlineData(TakionPacketMac.Congestion)]
    public void TheKeyPositionSurvivesForEveryType(int baseType)
    {
        byte[] original = Packet(baseType, 32);
        byte[] after = Packet(baseType, 32);

        Takion.PacketMacWithoutCrypt(after, 0, out _);

        TakionMacLayout? found = TakionPacketMac.LayoutFor(baseType);
        Assert.NotNull(found);

        Assert.Equal(
            original[found.Value.KeyPosOffset..(found.Value.KeyPosOffset + TakionPacketMac.KeyPosSize)],
            after[found.Value.KeyPosOffset..(found.Value.KeyPosOffset + TakionPacketMac.KeyPosSize)]);
    }

    /// <summary>
    /// The GMAC size the model uses is the one the C's out-parameter fills.
    ///
    /// The literal in Takion.PacketMacWithoutCrypt cannot say TakionPacketMac.GmacSize - inside that
    /// class the name is a DllImport - so the two are joined here instead.
    /// </summary>
    [Fact]
    public void TheGmacSizeIsTheSameOnBothSides()
    {
        byte[] packet = Packet(TakionDispatch.Video, 32);

        Assert.Equal(ChiakiError.Success, Takion.PacketMacWithoutCrypt(packet, 0, out byte[]? before));
        Assert.NotNull(before);
        Assert.Equal(TakionPacketMac.GmacSize, before.Length);
    }
}
