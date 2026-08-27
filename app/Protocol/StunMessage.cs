using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Why a STUN response was not usable.</summary>
public enum StunResult
{
    /// <summary>An address was read.</summary>
    Ok,

    /// <summary>Shorter than a header.</summary>
    TooSmall,

    /// <summary>Not a binding response.</summary>
    WrongType,

    /// <summary>The advertised length and the received length disagree.</summary>
    WrongLength,

    /// <summary>Not STUN.</summary>
    WrongCookie,

    /// <summary>Somebody else's answer, or a stale one.</summary>
    WrongTransactionId,

    /// <summary>An attribute that runs past the end of the message.</summary>
    InvalidData,

    /// <summary>A mapped address whose length is not the one its family requires.</summary>
    WrongAttributeLength,

    /// <summary>A mapped address that is neither v4 nor v6.</summary>
    BadFamily,

    /// <summary>A well-formed response with no mapped address in it at all.</summary>
    NoAddress,
}

/// <summary>
/// PP33: the STUN binding exchange - twenty bytes out, and an address read back out of whatever
/// comes in.
///
/// Four things worth knowing before trusting a rewrite of this:
///
///   THE IPv6 XOR KEY IS THE REQUEST BUFFER ITSELF. RFC 5389 says the address is XORed with the
///   magic cookie concatenated with the transaction id, and the core does not build that
///   sixteen-byte value - it reads <c>binding_req[4 + i]</c>, because the request it just sent
///   already holds the cookie and the id contiguously in exactly that order. The key is the wire
///   format, not a derived thing.
///
///   THE FIRST MAPPED ADDRESS WINS, XORED OR NOT. The loop takes whichever of the two attribute
///   types it meets first and returns immediately - so a server that sends the plain MAPPED-ADDRESS
///   before the XOR one is believed on the plain one, even though the plain one is exactly what NATs
///   are known to rewrite in flight. There is no preference and no second look.
///
///   ATTRIBUTES ARE SKIPPED WITHOUT THEIR PADDING. RFC 5389 pads every attribute to a multiple of
///   four bytes; this advances by <c>4 + length</c> with no rounding. An attribute of length five
///   therefore leaves the cursor three bytes inside the padding, and everything after it is read
///   from the wrong offset. It works today because the servers in the list send only aligned
///   attributes.
///
///   AND THE BOUNDS CHECK PROMOTES TO UNSIGNED. <c>received - (sizeof(...) + sizeof(...) +
///   attr_length)</c> is size_t arithmetic, so an attribute claiming a length larger than the
///   message wraps the right-hand side to an enormous number and the check passes. What it lets
///   through is narrow - the length then fails the eight-or-twenty test a few lines later, after
///   reading one byte of whatever was on the stack - but it is the check being defeated rather than
///   the check being generous.
///
///   That last one is NOT reproduced, on PP194's line: the wrap has no behaviour to port, only a
///   read of memory that was never received. This refuses the oversized attribute where the core
///   walks past it, and the promotion is asserted as STILL PRESENT so the divergence stays visible.
/// </summary>
public static class StunMessage
{
    /// <summary>The header, before any attributes.</summary>
    public const int HeaderSize = 20;

    /// <summary>What this end sends.</summary>
    public const ushort BindingRequest = 0x0001;

    /// <summary>And what it will accept back.</summary>
    public const ushort BindingResponse = 0x0101;

    /// <summary>The four bytes that say this is STUN and not something else on the same port.</summary>
    public const uint MagicCookie = 0x2112A442;

    /// <summary>How long the transaction id is.</summary>
    public const int TransactionIdLength = 12;

    /// <summary>The plain mapped address, which a NAT in the path is free to rewrite.</summary>
    public const ushort AttributeMappedAddress = 0x0001;

    /// <summary>And the obfuscated one, which is why it exists.</summary>
    public const ushort AttributeXorMappedAddress = 0x0020;

    /// <summary>The family byte for a v4 address.</summary>
    public const byte FamilyIpv4 = 0x01;

    /// <summary>And for a v6 one.</summary>
    public const byte FamilyIpv6 = 0x02;

    /// <summary>The attribute length a v4 mapped address must have.</summary>
    public const int Ipv4AttributeLength = 8;

    /// <summary>And a v6 one.</summary>
    public const int Ipv6AttributeLength = 20;

    /// <summary>The cookie's bytes, which are also the first four of the IPv6 XOR key.</summary>
    public static byte[] CookieBytes()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, MagicCookie);
        return bytes;
    }

    /// <summary>
    /// A binding request: the type, a zero length, the cookie, and the transaction id.
    ///
    /// The id's twelve bytes sit immediately after the cookie's four, which is what makes the
    /// request its own XOR key for an IPv6 answer - see the class note.
    /// </summary>
    public static byte[] BuildBindingRequest(byte[] transactionId)
    {
        ArgumentNullException.ThrowIfNull(transactionId);
        if (transactionId.Length != TransactionIdLength)
            throw new ArgumentException($"a transaction id is {TransactionIdLength} bytes", nameof(transactionId));

        var request = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0), BindingRequest);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(4), MagicCookie);
        transactionId.CopyTo(request, 8);
        return request;
    }

    /// <summary>
    /// A binding response carrying one mapped address, and optionally an attribute before it.
    ///
    /// PP452: the port had no way to PRODUCE this, only to read it - so the layout the reader believes
    /// had never been put on a wire by anything. Building it is what lets an exchange run over a real
    /// socket against bytes this port composed itself, which is the half of the agreement
    /// <see cref="Read"/> alone cannot check.
    /// </summary>
    /// <param name="transactionId">The twelve bytes the request used; a response must echo them.</param>
    /// <param name="address">The mapped address, v4 or v6.</param>
    /// <param name="port">The mapped port.</param>
    /// <param name="xored">
    /// Whether to send XOR-MAPPED-ADDRESS rather than the plain one. The obfuscation is applied here,
    /// so a round trip through <see cref="Read"/> proves both directions agree on the key.
    /// </param>
    /// <param name="leading">
    /// An attribute to place BEFORE the mapped address, as (type, value). Used to reach the padding
    /// question: RFC 5389 pads every attribute to a multiple of four and this port's reader does not,
    /// so a value whose length is not a multiple of four is where the two disagree.
    /// </param>
    public static byte[] BuildBindingResponse(
        byte[] transactionId,
        IPAddress address,
        ushort port,
        bool xored = true,
        (ushort Type, byte[] Value)? leading = null)
    {
        ArgumentNullException.ThrowIfNull(transactionId);
        ArgumentNullException.ThrowIfNull(address);
        if (transactionId.Length != TransactionIdLength)
            throw new ArgumentException($"a transaction id is {TransactionIdLength} bytes", nameof(transactionId));

        byte[] raw = address.GetAddressBytes();
        bool v6 = raw.Length == 16;
        byte[] cookie = CookieBytes();

        var attributes = new List<byte>();

        if (leading is { } extra)
        {
            ArgumentNullException.ThrowIfNull(extra.Value);
            attributes.AddRange(BeUInt16(extra.Type));
            attributes.AddRange(BeUInt16((ushort)extra.Value.Length));
            attributes.AddRange(extra.Value);

            // The RFC's padding, which this builder DOES write. A reader that skips by 4 + length
            // lands inside it, and that is the divergence PP452 puts on a socket.
            while (attributes.Count % 4 != 0)
                attributes.Add(0);
        }

        byte[] value = raw.ToArray();
        ushort onTheWire = port;

        if (xored)
        {
            onTheWire = (ushort)(port ^ BinaryPrimitives.ReadUInt16BigEndian(cookie));

            // v4 XORs with the cookie; v6 with the cookie followed by the transaction id, which is
            // what the request buffer holds contiguously. See the class note.
            if (v6)
            {
                Span<byte> key = stackalloc byte[16];
                cookie.CopyTo(key);
                transactionId.CopyTo(key[4..]);
                Xor(value, key);
            }
            else
            {
                Xor(value, cookie);
            }
        }

        attributes.AddRange(BeUInt16(xored ? AttributeXorMappedAddress : AttributeMappedAddress));
        attributes.AddRange(BeUInt16((ushort)(v6 ? Ipv6AttributeLength : Ipv4AttributeLength)));
        attributes.Add(0);
        attributes.Add(v6 ? FamilyIpv6 : FamilyIpv4);
        attributes.AddRange(BeUInt16(onTheWire));
        attributes.AddRange(value);

        var message = new byte[HeaderSize + attributes.Count];
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(0), BindingResponse);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2), (ushort)attributes.Count);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4), MagicCookie);
        transactionId.CopyTo(message, 8);
        attributes.CopyTo(message, HeaderSize);

        return message;
    }

    private static byte[] BeUInt16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    /// <summary>
    /// The address a response carries, or null with a reason.
    ///
    /// <paramref name="request"/> is the request this answers, because it is the XOR key for an
    /// IPv6 address as well as the transaction id to check against.
    /// </summary>
    public static StunResponse? Read(byte[] response, byte[] request, out StunResult result)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(request);

        result = Validate(response, request);
        if (result != StunResult.Ok)
            return null;

        int at = HeaderSize;
        while (at < response.Length - 3)
        {
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(at));
            int length = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(at + 2));

            // Where the core's size_t arithmetic wraps, this simply refuses - see the class note.
            if (at + 4 + length > response.Length)
            {
                result = StunResult.InvalidData;
                return null;
            }

            if (type != AttributeMappedAddress && type != AttributeXorMappedAddress)
            {
                // No rounding up to four, which is the core's skip and not the RFC's.
                at += 4 + length;
                continue;
            }

            return ReadMappedAddress(response, request, at, length, type == AttributeXorMappedAddress, out result);
        }

        result = StunResult.NoAddress;
        return null;
    }

    private static StunResult Validate(byte[] response, byte[] request)
    {
        if (response.Length < HeaderSize)
            return StunResult.TooSmall;

        if (BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(0)) != BindingResponse)
            return StunResult.WrongType;

        int advertised = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2)) + HeaderSize;
        if (response.Length != advertised)
            return StunResult.WrongLength;

        if (BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(4)) != MagicCookie)
            return StunResult.WrongCookie;

        return response.AsSpan(8, TransactionIdLength).SequenceEqual(request.AsSpan(8, TransactionIdLength))
            ? StunResult.Ok
            : StunResult.WrongTransactionId;
    }

    private static StunResponse? ReadMappedAddress(
        byte[] response, byte[] request, int at, int length, bool xored, out StunResult result)
    {
        byte family = response[at + 5];
        byte[] cookie = CookieBytes();

        if (family == FamilyIpv4)
        {
            if (length != Ipv4AttributeLength)
            {
                result = StunResult.WrongAttributeLength;
                return null;
            }

            ushort port = ReadPort(response, at, xored, cookie);
            var address = new byte[4];
            response.AsSpan(at + 8, 4).CopyTo(address);
            if (xored)
                Xor(address, cookie);

            result = StunResult.Ok;
            return new StunResponse(Dotted(address), port);
        }

        if (family == FamilyIpv6)
        {
            if (length != Ipv6AttributeLength)
            {
                result = StunResult.WrongAttributeLength;
                return null;
            }

            ushort port = ReadPort(response, at, xored, cookie);
            var address = new byte[16];
            response.AsSpan(at + 8, 16).CopyTo(address);
            if (xored)
            {
                // The key is the request buffer from the cookie onwards: cookie, then id.
                Xor(address, request.AsSpan(4, 16));
            }

            result = StunResult.Ok;
            return new StunResponse(new IPAddress(address).ToString(), port);
        }

        result = StunResult.BadFamily;
        return null;
    }

    /// <summary>
    /// The port, XORed with the cookie's FIRST TWO BYTES when the attribute is the obfuscated one.
    ///
    /// The core reaches that by truncating htonl(cookie) to sixteen bits, which reads as an endian
    /// accident and is not one: the truncation keeps the two bytes that are first on the wire, and
    /// those are the two the RFC says to use.
    /// </summary>
    private static ushort ReadPort(byte[] response, int at, bool xored, byte[] cookie)
    {
        ushort port = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(at + 6));
        return xored ? (ushort)(port ^ BinaryPrimitives.ReadUInt16BigEndian(cookie)) : port;
    }

    private static void Xor(Span<byte> bytes, ReadOnlySpan<byte> key)
    {
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] ^= key[i];
    }

    private static string Dotted(byte[] address)
        => string.Join(".", address.Select(b => b.ToString(CultureInfo.InvariantCulture)));
}

/// <summary>
/// PP33: the STUN exchange's rules where the Qt core states them.
/// </summary>
public static class StunMessageSource
{
    /// <summary>Where the exchange lives.</summary>
    public const string RelativePath = @"lib\src\remote\stun.h";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The constants this port copied, and what the core spells them.</summary>
    public static IReadOnlyList<(string Name, string Value)> Constants { get; } =
    [
        ("STUN_HEADER_SIZE", "20"),
        ("STUN_MSG_TYPE_BINDING_REQUEST", "0x0001"),
        ("STUN_MSG_TYPE_BINDING_RESPONSE", "0x0101"),
        ("STUN_MAGIC_COOKIE", "0x2112A442UL"),
        ("STUN_TRANSACTION_ID_LENGTH", "12"),
        ("STUN_ATTRIB_MAPPED_ADDRESS", "0x0001"),
        ("STUN_ATTRIB_XOR_MAPPED_ADDRESS", "0x0020"),
        ("STUN_MAPPED_ADDR_FAMILY_IPV4", "0x01"),
        ("STUN_MAPPED_ADDR_FAMILY_IPV6", "0x02"),
    ];

    /// <summary>Whether every one of them still holds the value this port was built against.</summary>
    public static bool TheConstantsAreStillTheseValues(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach ((string name, string value) in Constants)
        {
            if (!core.Contains($"#define {name} {value}", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the IPv6 XOR key is still read straight out of the request buffer, rather than
    /// assembled from the cookie and the id.
    /// </summary>
    public static bool TheKeyIsStillTheRequestBuffer(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "xored_addr[i] = binding_resp[response_pos + 8 + i] ^ binding_req[4 + i];",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the first mapped address still wins, xored or not.</summary>
    public static bool TheFirstMappedAddressStillWins(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "if (attr_type != STUN_ATTRIB_MAPPED_ADDRESS && attr_type != STUN_ATTRIB_XOR_MAPPED_ADDRESS)",
            StringComparison.Ordinal)
            && core.Contains("bool xored = attr_type == STUN_ATTRIB_XOR_MAPPED_ADDRESS;", StringComparison.Ordinal);
    }

    /// <summary>Whether attributes are still skipped without rounding up to four.</summary>
    public static bool TheSkipStillIgnoresPadding(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "response_pos += sizeof(attr_type) + sizeof(attr_length) + attr_length;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the bounds check still promotes to unsigned - asserted as STILL TRUE rather than
    /// fixed, because this port diverges there and a divergence nobody re-reads is a mistake.
    /// </summary>
    public static bool TheBoundsCheckStillPromotes(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "if(response_pos > (received - (sizeof(attr_type) + sizeof(attr_length) + attr_length)))",
            StringComparison.Ordinal);
    }
}
