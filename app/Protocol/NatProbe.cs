using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: the eighty-eight byte packets the candidate race is actually made of.
///
/// PP197 ported the decision; this is what travels. Two packets, the same size and nearly the same
/// shape - a request this end sends to every candidate, and a response it sends back when a console
/// probes it first.
///
/// TWO FIFTHS OF IT IS NEVER WRITTEN. Thirty-five of the eighty-eight bytes are left as the zeros
/// the buffer was cleared to, and fifty-three carry anything. The two hashed ids are twenty bytes
/// each and sit in THIRTY-TWO byte slots, so twelve zeros follow each one; three more sit between
/// the session ids and the request id; and the last eight bytes are untouched. A port that packed
/// the fields would produce a shorter packet that no console would answer, and would look tidier
/// doing it.
///
/// THE MATCHING IS FIVE BYTES, ECHOED VERBATIM. The response copies <c>req[0x4b..0x50]</c> straight
/// across without reading it. Those five bytes are the whole of what PP197 checks a reply against -
/// the core's own comment beside them asks what the "weird data at 0x4b" is, so nobody here knows
/// either.
///
/// THE RESPONSE'S TAIL HIDES THE CONSOLE'S ADDRESS BEHIND THE SESSION IDS. Bytes 0x50 to 0x55 are
/// written as sid_local, sid_console, sid_local - and then XORed with the console's four address
/// bytes and two port bytes. The local session id appears TWICE, masking the address's first half
/// and the port; the same key is used for both, in the same packet.
///
/// AND THAT TAIL CANNOT CARRY AN IPv6 ADDRESS. The XOR takes four bytes, whatever family the
/// address was parsed as - so a sixteen-byte address is masked by its first four bytes and the
/// other twelve are never sent at all. The family itself is chosen by LOOKING FOR A DOT in the
/// text, not by parsing, so a v4-mapped v6 literal is handed to the v4 parser and refused.
/// </summary>
public static class NatProbe
{
    /// <summary>Both packets are this long.</summary>
    public const int Length = 88;

    /// <summary>Where the local hashed id goes.</summary>
    public const int LocalHashedIdOffset = 0x04;

    /// <summary>And the console's, twenty bytes later in a thirty-two byte slot.</summary>
    public const int ConsoleHashedIdOffset = 0x24;

    /// <summary>How long a hashed id is.</summary>
    public const int HashedIdLength = 20;

    /// <summary>How much room each one is given.</summary>
    public const int HashedIdSlot = 0x20;

    /// <summary>Where the local session id goes.</summary>
    public const int LocalSidOffset = 0x44;

    /// <summary>And the console's.</summary>
    public const int ConsoleSidOffset = 0x46;

    /// <summary>Where the five bytes a reply is matched on go.</summary>
    public const int RequestIdOffset = 0x4b;

    /// <summary>How many of them there are.</summary>
    public const int RequestIdLength = 5;

    /// <summary>Where the response's masked address begins.</summary>
    public const int MaskedAddressOffset = 0x50;

    /// <summary>And its masked port.</summary>
    public const int MaskedPortOffset = 0x54;

    /// <summary>How many address bytes the tail can hold, whatever the family.</summary>
    public const int MaskedAddressLength = 4;

    /// <summary>A request to one candidate.</summary>
    public static byte[] BuildRequest(
        byte[] localHashedId, byte[] consoleHashedId, ushort localSid, ushort consoleSid, byte[] requestId)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        if (requestId.Length != RequestIdLength)
            throw new ArgumentException($"a request id is {RequestIdLength} bytes", nameof(requestId));

        byte[] packet = Header(CandidateRace.RequestType, localHashedId, consoleHashedId, localSid, consoleSid);
        requestId.CopyTo(packet, RequestIdOffset);
        return packet;
    }

    /// <summary>
    /// The response to a request, or null when the candidate's address cannot be parsed.
    ///
    /// The request's five matching bytes are echoed without being read, and the tail is the
    /// candidate's address and port behind the session ids - see the class note.
    /// </summary>
    public static byte[]? BuildResponse(
        byte[] request,
        byte[] localHashedId,
        byte[] consoleHashedId,
        ushort localSid,
        ushort consoleSid,
        string candidateAddress,
        ushort candidatePort)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidateAddress);

        byte[]? address = ParseAddress(candidateAddress);
        if (address is null)
            return null;

        byte[] packet = Header(CandidateRace.ResponseType, localHashedId, consoleHashedId, localSid, consoleSid);

        // Echoed, not read.
        request.AsSpan(RequestIdOffset, RequestIdLength).CopyTo(packet.AsSpan(RequestIdOffset));

        // The local id twice: once masking the address's first half, once masking the port.
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(MaskedAddressOffset), localSid);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(MaskedAddressOffset + 2), consoleSid);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(MaskedPortOffset), localSid);

        var port = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(port, candidatePort);

        // FOUR bytes, whatever the family - the rest of a v6 address never leaves.
        Xor(packet.AsSpan(MaskedAddressOffset, MaskedAddressLength), address);
        Xor(packet.AsSpan(MaskedPortOffset, 2), port);

        return packet;
    }

    /// <summary>
    /// The address bytes, or null when the text does not parse.
    ///
    /// The family is chosen by whether there is a DOT in the text - so a v4-mapped v6 literal goes
    /// to the v4 parser and is refused, and anything without a dot goes to the v6 one.
    /// </summary>
    public static byte[]? ParseAddress(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        AddressFamily family = text.Contains('.', StringComparison.Ordinal)
            ? AddressFamily.InterNetwork
            : AddressFamily.InterNetworkV6;

        if (!IPAddress.TryParse(text, out IPAddress? address) || address.AddressFamily != family)
            return null;

        return address.GetAddressBytes();
    }

    /// <summary>The five bytes a reply is matched on, out of either packet.</summary>
    public static byte[] RequestIdOf(byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return packet.AsSpan(RequestIdOffset, RequestIdLength).ToArray();
    }

    /// <summary>The bytes this packet never fills in.</summary>
    public static IReadOnlyList<(int At, int Count)> Padding { get; } =
    [
        (LocalHashedIdOffset + HashedIdLength, HashedIdSlot - HashedIdLength),
        (ConsoleHashedIdOffset + HashedIdLength, HashedIdSlot - HashedIdLength),
        (ConsoleSidOffset + 2, RequestIdOffset - (ConsoleSidOffset + 2)),
        (RequestIdOffset + RequestIdLength, Length - (RequestIdOffset + RequestIdLength)),
    ];

    private static byte[] Header(
        uint type, byte[] localHashedId, byte[] consoleHashedId, ushort localSid, ushort consoleSid)
    {
        ArgumentNullException.ThrowIfNull(localHashedId);
        ArgumentNullException.ThrowIfNull(consoleHashedId);

        var packet = new byte[Length];
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0), type);
        localHashedId.AsSpan(0, HashedIdLength).CopyTo(packet.AsSpan(LocalHashedIdOffset));
        consoleHashedId.AsSpan(0, HashedIdLength).CopyTo(packet.AsSpan(ConsoleHashedIdOffset));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(LocalSidOffset), localSid);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(ConsoleSidOffset), consoleSid);
        return packet;
    }

    private static void Xor(Span<byte> target, ReadOnlySpan<byte> key)
    {
        for (int i = 0; i < target.Length; i++)
            target[i] ^= key[i];
    }
}

/// <summary>
/// PP33: the probe packets' rules where the Qt core states them.
/// </summary>
public static class NatProbeSource
{
    /// <summary>Where the packets are built.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The offsets this port copied, in the order the core writes them.
    ///
    /// PP382 respelled the three casts here as <c>chiaki_unaligned_uint*_t</c>. These buffers are
    /// written at hex offsets - <c>&amp;request_buf[i][0x44]</c> - so alignment depended on a
    /// constant nobody chose for that reason, and this check quoting the old spelling is what
    /// noticed the change rather than a reason to keep it.
    /// </summary>
    public static IReadOnlyList<string> RequestWrites { get; } =
    [
        "*(chiaki_unaligned_uint32_t*)&request_buf[i][0x00] = htonl(MSG_TYPE_REQ);",
        "memcpy(&request_buf[i][0x04], session->hashed_id_local, sizeof(session->hashed_id_local));",
        "memcpy(&request_buf[i][0x24], session->hashed_id_console, sizeof(session->hashed_id_console));",
        "*(chiaki_unaligned_uint16_t*)&request_buf[i][0x44] = htons(session->sid_local);",
        "*(chiaki_unaligned_uint16_t*)&request_buf[i][0x46] = htons(session->sid_console);",
        "memcpy(&request_buf[i][0x4b], request_id[i], sizeof(request_id[i]));",
    ];

    /// <summary>Whether the request is still exactly those six writes into eighty-eight bytes.</summary>
    public static bool TheRequestIsStillThoseSixWrites(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        if (!core.Contains($"uint8_t request_buf[CHECK_CANDIDATES_REQUEST_NUMBER][{NatProbe.Length}] = {{0}};", StringComparison.Ordinal))
            return false;

        int cursor = 0;
        foreach (string write in RequestWrites)
        {
            int at = core.IndexOf(write, cursor, StringComparison.Ordinal);
            if (at < 0)
                return false;

            cursor = at + 1;
        }

        return true;
    }

    /// <summary>Whether the hashed ids are still twenty bytes in thirty-two byte slots.</summary>
    public static bool TheIdsAreStillShortOfTheirSlots(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"uint8_t hashed_id_local[{NatProbe.HashedIdLength}];", StringComparison.Ordinal)
            && core.Contains($"uint8_t hashed_id_console[{NatProbe.HashedIdLength}];", StringComparison.Ordinal);
    }

    /// <summary>Whether the response still echoes the request's five bytes without reading them.</summary>
    public static bool TheResponseStillEchoesTheFiveBytes(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return CCall.Happens(core, "memcpy(&confirm_buf[0x4b], &req[0x4b], 5)");
    }

    /// <summary>Whether the tail still masks the address and port with the session ids.</summary>
    public static bool TheTailIsStillMaskedBySessionIds(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        // PP382: the same respelling as RequestWrites above, for the same reason.
        return core.Contains("*(chiaki_unaligned_uint16_t*)&confirm_buf[0x50] = htons(session->sid_local);", StringComparison.Ordinal)
            && core.Contains("*(chiaki_unaligned_uint16_t*)&confirm_buf[0x52] = htons(session->sid_console);", StringComparison.Ordinal)
            && core.Contains("*(chiaki_unaligned_uint16_t*)&confirm_buf[0x54] = htons(session->sid_local);", StringComparison.Ordinal)
            && CCall.Happens(core, "xor_bytes(&confirm_buf[0x50], console_addr, 4)")
            && CCall.Happens(core, "xor_bytes(&confirm_buf[0x54], console_port, 2)");
    }

    /// <summary>Whether the family is still chosen by looking for a dot.</summary>
    public static bool TheFamilyIsStillChosenByADot(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("char *search_ptr = strchr(candidate->addr, '.');", StringComparison.Ordinal)
            && core.Contains("inet_pton(AF_INET, candidate->addr, console_addr)", StringComparison.Ordinal)
            && core.Contains("inet_pton(AF_INET6, candidate->addr, console_addr)", StringComparison.Ordinal);
    }
}
