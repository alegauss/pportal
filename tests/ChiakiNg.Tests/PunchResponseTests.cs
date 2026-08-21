using System.Buffers.Binary;
using System.Net.Sockets;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP236: the eighty-eight bytes, and the three things in them that are wrong to guess.
///
/// The test that carries the task is <see cref="TheIdsLeaveTwelveZeroBytesBehindThem"/>: it asserts
/// the gap rather than the fields, because the gap is what a port loses by placing two twenty-byte
/// identifiers next to each other - and losing it moves every field after them.
/// </summary>
public class PunchResponseTests
{
    private static readonly byte[] LocalId = [.. Enumerable.Repeat((byte)0xAA, 20)];
    private static readonly byte[] ConsoleId = [.. Enumerable.Repeat((byte)0xBB, 20)];

    /// <summary>A request with a recognisable five bytes where the echo comes from.</summary>
    private static byte[] Request()
    {
        byte[] request = new byte[PunchResponse.Length];
        for (int at = 0; at < PunchResponse.EchoLength; at++)
            request[PunchResponse.EchoAt + at] = (byte)(0xE0 + at);

        return request;
    }

    private static byte[] Built(string address = "192.168.1.50", ushort port = 9295)
        => PunchResponse.Build(Request(), LocalId, ConsoleId, 0x1234, 0x5678, address, port)!;

    /// <summary>Eighty-eight bytes, and the type at the front.</summary>
    [Fact]
    public void ItIsEightyEightBytesAndSaysWhatItIs()
    {
        byte[] packet = Built();

        Assert.Equal(88, packet.Length);
        Assert.Equal(PunchResponse.ResponseType, BinaryPrimitives.ReadUInt32BigEndian(packet));

        // And it is not the request's type, which differs by one in the high byte.
        Assert.NotEqual(PunchResponse.RequestType, PunchResponse.ResponseType);
    }

    /// <summary>
    /// THE GAP. Twenty bytes of identifier in a thirty-two byte slot, so twelve zeros follow the
    /// first one - and a port that placed the two adjacently would put the console's twelve bytes
    /// early and everything after it with them.
    /// </summary>
    [Fact]
    public void TheIdsLeaveTwelveZeroBytesBehindThem()
    {
        byte[] packet = Built();

        Assert.Equal(LocalId, packet[0x04..0x18]);
        Assert.Equal(ConsoleId, packet[0x24..0x38]);

        // The twelve nobody writes.
        Assert.All(packet[0x18..0x24], b => Assert.Equal(0, b));
        Assert.Equal(12, PunchResponse.IdSlot - PunchResponse.IdLength);
    }

    /// <summary>The session ids, as themselves, where they are data.</summary>
    [Fact]
    public void TheSessionIdsAppearAsThemselvesFirst()
    {
        byte[] packet = Built();

        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(0x44)));
        Assert.Equal(0x5678, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(0x46)));
    }

    /// <summary>
    /// And again as a KEY. Undoing the exclusive-or with the ids gives the address back, which is
    /// what says the second copy is not a repeat.
    /// </summary>
    [Fact]
    public void TheSecondCopyOfTheIdsHidesTheAddress()
    {
        byte[] packet = Built("192.168.1.50", 9295);

        // The key, as it would have been written before the xor.
        byte[] key = [0x12, 0x34, 0x56, 0x78];
        byte[] recovered =
            [.. packet[0x50..0x54].Select((b, at) => (byte)(b ^ key[at]))];

        Assert.Equal(new byte[] { 192, 168, 1, 50 }, recovered);

        // And the port, under the third copy of the local id.
        ushort port = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(0x54)) ^ 0x1234);
        Assert.Equal(9295, port);
    }

    /// <summary>Five bytes come back from the request exactly as they arrived.</summary>
    [Fact]
    public void FiveBytesAreEchoedFromTheRequest()
    {
        byte[] packet = Built();

        Assert.Equal(new byte[] { 0xE0, 0xE1, 0xE2, 0xE3, 0xE4 }, packet[0x4b..0x50]);
    }

    /// <summary>
    /// Four bytes of address are covered whatever the family, so an IPv6 candidate sends twelve of
    /// its sixteen in clear. Not a decision anywhere in the file, and reproduced as written.
    /// </summary>
    [Fact]
    public void OnlyFourBytesOfAnIpv6AddressAreHidden()
    {
        byte[] packet = PunchResponse.Build(
            Request(), LocalId, ConsoleId, 0x1234, 0x5678, "2001:db8::1", 9295)!;

        byte[] key = [0x12, 0x34, 0x56, 0x78];
        byte[] recovered = [.. packet[0x50..0x54].Select((b, at) => (byte)(b ^ key[at]))];

        // The first four of 2001:0db8::1.
        Assert.Equal(new byte[] { 0x20, 0x01, 0x0d, 0xb8 }, recovered);
        Assert.Equal(4, PunchResponse.AddressKeyed);
    }

    /// <summary>
    /// The family is read from a DOT rather than asked for, which is the core's own test - so an
    /// IPv6 address carrying one is handed to the IPv4 parser and refused.
    /// </summary>
    [Fact]
    public void TheFamilyIsChosenByLookingForADot()
    {
        Assert.Equal(AddressFamily.InterNetwork, PunchResponse.FamilyOf("1.2.3.4"));
        Assert.Equal(AddressFamily.InterNetworkV6, PunchResponse.FamilyOf("2001:db8::1"));

        // A mapped address is IPv6 and has a dot in it.
        Assert.Equal(AddressFamily.InterNetwork, PunchResponse.FamilyOf("::ffff:1.2.3.4"));

        Assert.Null(PunchResponse.Build(
            Request(), LocalId, ConsoleId, 1, 2, "::ffff:1.2.3.4", 9295));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheReplyIsStillTheCores()
    {
        string? file = PunchResponseSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(PunchResponseSource.TheLayoutIsStillThis(core), "eighty-eight, and two offsets");
        Assert.True(PunchResponseSource.TheIdsAreStillShorterThanTheirSlot(core), "twenty in thirty-two");
        Assert.True(PunchResponseSource.TheIdsAreStillTheKey(core), "and the ids are the key");
        Assert.True(PunchResponseSource.TheFamilyIsStillChosenByADot(core), "by a dot");
    }
}
