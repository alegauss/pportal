using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the STUN binding exchange, twenty bytes out and an address back.
/// </summary>
public class StunMessageTests
{
    private static readonly byte[] TransactionId =
        [0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b];

    private static byte[] Request() => StunMessage.BuildBindingRequest(TransactionId);

    /// <summary>Builds a response carrying one attribute, with the header's length filled in.</summary>
    private static byte[] Response(params byte[][] attributes)
    {
        int length = attributes.Sum(a => a.Length);
        var response = new byte[StunMessage.HeaderSize + length];

        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0), StunMessage.BindingResponse);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), (ushort)length);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(4), StunMessage.MagicCookie);
        TransactionId.CopyTo(response, 8);

        int at = StunMessage.HeaderSize;
        foreach (byte[] attribute in attributes)
        {
            attribute.CopyTo(response, at);
            at += attribute.Length;
        }

        return response;
    }

    /// <summary>One mapped-address attribute, xored or plain, over v4.</summary>
    private static byte[] MappedV4(string address, ushort port, bool xored)
    {
        byte[] cookie = StunMessage.CookieBytes();
        byte[] octets = [.. address.Split('.').Select(byte.Parse)];

        var attribute = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(
            attribute.AsSpan(0),
            xored ? StunMessage.AttributeXorMappedAddress : StunMessage.AttributeMappedAddress);
        BinaryPrimitives.WriteUInt16BigEndian(attribute.AsSpan(2), StunMessage.Ipv4AttributeLength);
        attribute[5] = StunMessage.FamilyIpv4;

        ushort onWire = xored
            ? (ushort)(port ^ BinaryPrimitives.ReadUInt16BigEndian(cookie))
            : port;
        BinaryPrimitives.WriteUInt16BigEndian(attribute.AsSpan(6), onWire);

        for (int i = 0; i < 4; i++)
            attribute[8 + i] = xored ? (byte)(octets[i] ^ cookie[i]) : octets[i];

        return attribute;
    }

    private static StunResponse Read(byte[] response)
    {
        StunResponse? read = StunMessage.Read(response, Request(), out StunResult result);
        Assert.Equal(StunResult.Ok, result);
        Assert.NotNull(read);
        return read.Value;
    }

    private static StunResult Refused(byte[] response)
    {
        Assert.Null(StunMessage.Read(response, Request(), out StunResult result));
        Assert.NotEqual(StunResult.Ok, result);
        return result;
    }

    /// <summary>A binding request is twenty bytes: type, a zero length, the cookie, and the id.</summary>
    [Fact]
    public void ABindingRequestIsTwentyBytesInThatOrder()
    {
        byte[] request = Request();

        Assert.Equal(20, request.Length);
        Assert.Equal(StunMessage.BindingRequest, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(0)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(2)));
        Assert.Equal(StunMessage.MagicCookie, BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(4)));
        Assert.Equal(TransactionId, request[8..]);
    }

    /// <summary>
    /// THE REQUEST IS ITS OWN XOR KEY. The cookie's four bytes and the id's twelve sit contiguously
    /// at offset four, which is exactly the sixteen-byte key an IPv6 address is XORed with - so the
    /// core reads binding_req[4 + i] rather than assembling anything.
    /// </summary>
    [Fact]
    public void TheSixteenBytesFromOffsetFourAreTheCookieThenTheId()
    {
        byte[] request = Request();

        Assert.Equal(StunMessage.CookieBytes(), request[4..8]);
        Assert.Equal(TransactionId, request[8..20]);
        Assert.Equal(16, request[4..20].Length);
    }

    /// <summary>An xored v4 address reads back as itself, port and all.</summary>
    [Fact]
    public void AnXoredAddressIsUnmasked()
    {
        StunResponse read = Read(Response(MappedV4("203.0.113.9", 41234, xored: true)));

        Assert.Equal("203.0.113.9", read.Address);
        Assert.Equal(41234, read.Port);
    }

    /// <summary>And a plain one is taken as it stands.</summary>
    [Fact]
    public void APlainAddressIsTakenAsItIs()
    {
        StunResponse read = Read(Response(MappedV4("203.0.113.9", 41234, xored: false)));

        Assert.Equal("203.0.113.9", read.Address);
        Assert.Equal(41234, read.Port);
    }

    /// <summary>
    /// The port is XORed with the cookie's FIRST TWO BYTES. The core reaches that by truncating
    /// htonl(cookie) to sixteen bits, which reads as an endian accident and is not one - the
    /// truncation keeps the two bytes that come first on the wire, and those are the RFC's two.
    /// </summary>
    [Fact]
    public void ThePortIsMaskedWithTheCookiesFirstTwoBytes()
    {
        byte[] attribute = MappedV4("203.0.113.9", 41234, xored: true);
        ushort onWire = BinaryPrimitives.ReadUInt16BigEndian(attribute.AsSpan(6));

        Assert.Equal(0x2112, BinaryPrimitives.ReadUInt16BigEndian(StunMessage.CookieBytes()));
        Assert.Equal(41234 ^ 0x2112, onWire);
    }

    /// <summary>
    /// THE FIRST MAPPED ADDRESS WINS, XORED OR NOT. A server sending the plain attribute before the
    /// obfuscated one is believed on the plain one - which is exactly the attribute a NAT in the
    /// path is known to rewrite. There is no preference and no second look.
    /// </summary>
    [Fact]
    public void ThePlainAttributeWinsWhenItComesFirst()
    {
        StunResponse read = Read(Response(
            MappedV4("192.0.2.1", 1111, xored: false),
            MappedV4("203.0.113.9", 41234, xored: true)));

        Assert.Equal("192.0.2.1", read.Address);
        Assert.Equal(1111, read.Port);
    }

    /// <summary>And the other way round, the xored one wins - it is order, not type.</summary>
    [Fact]
    public void TheXoredAttributeWinsWhenItComesFirst()
    {
        StunResponse read = Read(Response(
            MappedV4("203.0.113.9", 41234, xored: true),
            MappedV4("192.0.2.1", 1111, xored: false)));

        Assert.Equal("203.0.113.9", read.Address);
    }

    /// <summary>Attributes that are neither are stepped over until one that is turns up.</summary>
    [Fact]
    public void UnrelatedAttributesAreSteppedOver()
    {
        byte[] software = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(software.AsSpan(0), 0x8022);
        BinaryPrimitives.WriteUInt16BigEndian(software.AsSpan(2), 8);

        StunResponse read = Read(Response(software, MappedV4("203.0.113.9", 41234, xored: true)));

        Assert.Equal("203.0.113.9", read.Address);
    }

    /// <summary>
    /// ATTRIBUTES ARE SKIPPED WITHOUT THEIR PADDING. The RFC pads every attribute to a multiple of
    /// four; this advances by 4 + length with no rounding, so an attribute of length five leaves the
    /// cursor three bytes inside the padding and everything after it is read from the wrong offset.
    ///
    /// It works today only because the servers in the list send aligned attributes.
    /// </summary>
    [Fact]
    public void AnUnalignedAttributeThrowsOffEverythingAfterIt()
    {
        // Five bytes of value, padded to eight on the wire - twelve bytes of attribute in total.
        byte[] unaligned = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(unaligned.AsSpan(0), 0x8022);
        BinaryPrimitives.WriteUInt16BigEndian(unaligned.AsSpan(2), 5);

        byte[] mapped = MappedV4("203.0.113.9", 41234, xored: true);

        // The cursor lands three bytes early, reads a type and a length out of the padding, and
        // never finds the address that is sitting right there.
        Assert.Equal(StunResult.InvalidData, Refused(Response(unaligned, mapped)));

        // The identical bytes with the length declared as the padded eight are read straight
        // through - so it is the rounding that is missing, not the attribute that is malformed.
        byte[] aligned = [.. unaligned];
        BinaryPrimitives.WriteUInt16BigEndian(aligned.AsSpan(2), 8);

        Assert.Equal("203.0.113.9", Read(Response(aligned, mapped)).Address);
    }

    /// <summary>A response shorter than a header is refused before anything is read out of it.</summary>
    [Fact]
    public void AResponseShorterThanAHeaderIsRefused()
        => Assert.Equal(StunResult.TooSmall, Refused(new byte[19]));

    /// <summary>Something that is not a binding response is refused.</summary>
    [Fact]
    public void AResponseOfTheWrongTypeIsRefused()
    {
        byte[] response = Response(MappedV4("203.0.113.9", 41234, xored: true));
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0), StunMessage.BindingRequest);

        Assert.Equal(StunResult.WrongType, Refused(response));
    }

    /// <summary>The advertised length has to be the received one, which is what ties them together.</summary>
    [Fact]
    public void AnAdvertisedLengthThatDisagreesIsRefused()
    {
        byte[] response = Response(MappedV4("203.0.113.9", 41234, xored: true));
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 4);

        Assert.Equal(StunResult.WrongLength, Refused(response));
    }

    /// <summary>Without the cookie it is not STUN, whatever else is on the port.</summary>
    [Fact]
    public void AResponseWithoutTheCookieIsRefused()
    {
        byte[] response = Response(MappedV4("203.0.113.9", 41234, xored: true));
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(4), 0xDEADBEEF);

        Assert.Equal(StunResult.WrongCookie, Refused(response));
    }

    /// <summary>And somebody else's transaction is somebody else's answer.</summary>
    [Fact]
    public void AResponseToAnotherTransactionIsRefused()
    {
        byte[] response = Response(MappedV4("203.0.113.9", 41234, xored: true));
        response[8] ^= 0xFF;

        Assert.Equal(StunResult.WrongTransactionId, Refused(response));
    }

    /// <summary>A mapped address of the wrong length for its family is refused.</summary>
    [Fact]
    public void AMappedAddressOfTheWrongLengthIsRefused()
    {
        byte[] attribute = MappedV4("203.0.113.9", 41234, xored: true);
        BinaryPrimitives.WriteUInt16BigEndian(attribute.AsSpan(2), 4);

        Assert.Equal(StunResult.WrongAttributeLength, Refused(Response(attribute)));
    }

    /// <summary>A family that is neither four nor six is refused rather than guessed at.</summary>
    [Fact]
    public void AnUnknownAddressFamilyIsRefused()
    {
        byte[] attribute = MappedV4("203.0.113.9", 41234, xored: true);
        attribute[5] = 0x07;

        Assert.Equal(StunResult.BadFamily, Refused(Response(attribute)));
    }

    /// <summary>A well-formed response with no mapped address at all is a failure, not an empty one.</summary>
    [Fact]
    public void AResponseWithNoMappedAddressIsAFailure()
    {
        byte[] software = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(software.AsSpan(0), 0x8022);
        BinaryPrimitives.WriteUInt16BigEndian(software.AsSpan(2), 8);

        Assert.Equal(StunResult.NoAddress, Refused(Response(software)));
    }

    /// <summary>
    /// AND THE BOUNDS CHECK IS WHERE THIS PORT DIVERGES. The core's check is size_t arithmetic, so
    /// an attribute claiming a length larger than the message wraps the right-hand side and the
    /// check passes - it then reads one byte past what was received before failing on the length.
    ///
    /// This refuses the attribute instead. The wrap has no behaviour to port, only a read of memory
    /// that never arrived, which is the line PP194 drew.
    /// </summary>
    [Fact]
    public void AnAttributeLongerThanTheMessageIsRefusedRatherThanWalkedPast()
    {
        byte[] attribute = MappedV4("203.0.113.9", 41234, xored: true);
        BinaryPrimitives.WriteUInt16BigEndian(attribute.AsSpan(2), ushort.MaxValue);

        Assert.Equal(StunResult.InvalidData, Refused(Response(attribute)));
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheExchangesRulesAreStillTheQtCores()
    {
        string? path = StunMessageSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(StunMessageSource.TheConstantsAreStillTheseValues(core), "nine constants");
        Assert.True(StunMessageSource.TheKeyIsStillTheRequestBuffer(core), "the request is the key");
        Assert.True(StunMessageSource.TheFirstMappedAddressStillWins(core), "first one wins");
        Assert.True(StunMessageSource.TheSkipStillIgnoresPadding(core), "no rounding to four");
        Assert.True(StunMessageSource.TheBoundsCheckStillPromotes(core), "still promoted, still diverged from");
    }
}
