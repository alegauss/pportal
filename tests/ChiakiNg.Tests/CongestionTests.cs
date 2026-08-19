using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP124: the congestion report, which is the first thing this port sends rather than reads.
///
/// Everything else checked so far is receive-side: a console sent bytes and the port has to agree
/// about what they mean. This is the other direction - how many packets the client received and
/// how many it lost, which is what the console's bitrate control reacts to.
///
/// It fails differently, and worse. A misread packet produces an error or a corrupt frame; a
/// mis-sent congestion report produces a stream that quietly degrades, with nothing on either
/// side saying why. Fifteen bytes, and the C records all fifteen twice - before the MAC and
/// after it.
/// </summary>
public class CongestionTests
{
    private static string? File => SanitizerSource.LocateRelative(@"test\takion.c");

    private static IReadOnlyDictionary<string, byte[]> Vectors()
        => CryptoVectors.InFunction(File!, "test_takion_format_congestion");

    [Fact]
    public void TheFormattedPacketIsTheRecordedFifteenBytes()
    {
        if (File is null)
            return;

        Assert.Equal(15, Takion.CongestionPacketSize);
        Assert.Equal(Vectors()["buf_expected"],
            Takion.FormatCongestion(0x42, 26, 10, 0x1e5));
    }

    /// <summary>
    /// The MAC is written INSIDE the packet, over four bytes that were zero, and the packet does
    /// not grow. A rewrite that appended it produces a packet of the wrong length; one that wrote
    /// it at the wrong offset produces the right length and the wrong bytes, and the console
    /// ignores both without saying so.
    /// </summary>
    [Fact]
    public void TheMacIsWrittenInsideThePacketAtTheRecordedOffset()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors();
        using var crypt = new GkCrypt(0, 2, v["handshake_key"], v["ecdh_secret"]);

        byte[] packet = Takion.FormatCongestion(0x42, 26, 10, 0x1e5);
        Takion.WritePacketMac(crypt, packet, 0x1e5);

        Assert.Equal(v["buf_expected_mac"], packet);
        Assert.Equal(15, packet.Length);
    }

    /// <summary>
    /// And it really did change the packet - the two recorded buffers differ in exactly the four
    /// bytes the MAC occupies, so the assertion above is not comparing a packet to itself.
    /// </summary>
    [Fact]
    public void TheMacOccupiesFourBytesThatWereZero()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors();
        byte[] before = v["buf_expected"];
        byte[] after = v["buf_expected_mac"];

        int[] changed = [.. Enumerable.Range(0, before.Length).Where(i => before[i] != after[i])];

        Assert.Equal(4, changed.Length);
        Assert.Equal(changed.First() + 3, changed.Last());       // contiguous
        Assert.All(changed, i => Assert.Equal(0, before[i]));    // and they were zero
    }

    /// <summary>
    /// The counts are in the packet, not decoration. Without this every assertion above passes for
    /// a formatter that emitted a constant - and a constant congestion report is a console told
    /// the network is fine no matter what it is doing.
    /// </summary>
    [Theory]
    [InlineData(0x43, 26, 10)]
    [InlineData(0x42, 27, 10)]
    [InlineData(0x42, 26, 11)]
    public void EveryFieldChangesThePacket(int word0, int received, int lost)
    {
        if (File is null)
            return;

        Assert.NotEqual(Vectors()["buf_expected"],
            Takion.FormatCongestion((ushort)word0, (ushort)received, (ushort)lost, 0x1e5));
    }

    /// <summary>And so is the key position, which is in the packet's tail rather than only in the MAC.</summary>
    [Fact]
    public void TheKeyPositionChangesThePacket()
    {
        if (File is null)
            return;

        Assert.NotEqual(Vectors()["buf_expected"], Takion.FormatCongestion(0x42, 26, 10, 0x1e6));
    }
}
