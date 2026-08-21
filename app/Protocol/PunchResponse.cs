using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP236: the reply this client sends a console during hole punching.
///
/// Eighty-eight bytes, a layout nothing documents, and three things in it that are wrong to guess.
///
/// THE IDENTIFIERS ARE TWENTY BYTES IN THIRTY-TWO-BYTE SLOTS. They sit at 0x04 and 0x24, so each
/// leaves twelve zero bytes behind it. A port placing them adjacently has every field after them
/// off by twelve, and the console reads a different packet.
///
/// THE SESSION IDS ARE A KEY, not a repeat. They are written at 0x44 as themselves, and again at
/// 0x50 and 0x54 where the address is exclusive-ored over the pair and the port over the single.
/// A reader taking the second copy for a duplicate would drop the obfuscation and send both in
/// clear.
///
/// AND FOUR BYTES OF ADDRESS ARE COVERED WHATEVER THE FAMILY. An IPv6 candidate has its first four
/// obfuscated and its other twelve sent plain - not a decision anywhere in the file, just what a
/// four does when the buffer beside it is sixteen.
/// </summary>
public static class PunchResponse
{
    /// <summary>The packet's size, and the size of the request it answers.</summary>
    public const int Length = 88;

    /// <summary>MSG_TYPE_RESP, written big-endian at the front.</summary>
    public const uint ResponseType = 0x07000000;

    /// <summary>And the request's, for the message this one replies to.</summary>
    public const uint RequestType = 0x06000000;

    /// <summary>How long a hashed identifier actually is.</summary>
    public const int IdLength = 20;

    /// <summary>And how much room it is given, which is not the same number.</summary>
    public const int IdSlot = 32;

    /// <summary>Where the local identifier goes.</summary>
    public const int LocalIdAt = 0x04;

    /// <summary>Where the console's goes - one SLOT along, not one length.</summary>
    public const int ConsoleIdAt = 0x24;

    /// <summary>Where the session ids are written as themselves.</summary>
    public const int SessionIdsAt = 0x44;

    /// <summary>Where five bytes of the request are echoed back.</summary>
    public const int EchoAt = 0x4b;

    /// <summary>How many.</summary>
    public const int EchoLength = 5;

    /// <summary>Where the ids are written again to be used as a key for the address.</summary>
    public const int AddressKeyAt = 0x50;

    /// <summary>And for the port.</summary>
    public const int PortKeyAt = 0x54;

    /// <summary>
    /// How much of the address is covered, whatever its family. Four, and the reason it is a
    /// constant here is that it is a literal there.
    /// </summary>
    public const int AddressKeyed = 4;

    /// <summary>
    /// Which family an address string is read as.
    ///
    /// By looking for a DOT rather than by asking, which is the core's own test. An address that is
    /// IPv6 and contains one - a mapped address like ::ffff:1.2.3.4 - is therefore handed to the
    /// IPv4 parser and refused.
    /// </summary>
    public static AddressFamily FamilyOf(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.Contains('.', StringComparison.Ordinal)
            ? AddressFamily.InterNetwork
            : AddressFamily.InterNetworkV6;
    }

    /// <summary>
    /// Builds the reply.
    /// </summary>
    /// <param name="request">The console's request, for the five bytes echoed out of it.</param>
    /// <param name="localId">The local hashed identifier, twenty bytes.</param>
    /// <param name="consoleId">The console's, twenty bytes.</param>
    /// <param name="sidLocal">This side's session id.</param>
    /// <param name="sidConsole">The console's.</param>
    /// <param name="address">The candidate's address, as a string.</param>
    /// <param name="port">And its port.</param>
    /// <returns>The packet, or null where the address will not parse as the family its shape implies.</returns>
    public static byte[]? Build(
        ReadOnlySpan<byte> request,
        ReadOnlySpan<byte> localId,
        ReadOnlySpan<byte> consoleId,
        ushort sidLocal,
        ushort sidConsole,
        string address,
        ushort port)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNotEqual(localId.Length, IdLength);
        ArgumentOutOfRangeException.ThrowIfNotEqual(consoleId.Length, IdLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Length, EchoAt + EchoLength);

        if (!IPAddress.TryParse(address, out IPAddress? parsed)
            || parsed.AddressFamily != FamilyOf(address))
        {
            return null;
        }

        byte[] packet = new byte[Length];

        BinaryPrimitives.WriteUInt32BigEndian(packet, ResponseType);

        localId.CopyTo(packet.AsSpan(LocalIdAt, IdLength));
        consoleId.CopyTo(packet.AsSpan(ConsoleIdAt, IdLength));

        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(SessionIdsAt), sidLocal);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(SessionIdsAt + 2), sidConsole);

        request.Slice(EchoAt, EchoLength).CopyTo(packet.AsSpan(EchoAt, EchoLength));

        // The key: the same two ids again, and a third copy of the local one.
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(AddressKeyAt), sidLocal);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(AddressKeyAt + 2), sidConsole);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(PortKeyAt), sidLocal);

        // And the obfuscation, over exactly four bytes of address however long the address is.
        byte[] raw = parsed.GetAddressBytes();
        Xor(packet.AsSpan(AddressKeyAt, AddressKeyed), raw.AsSpan(0, AddressKeyed));

        Span<byte> portBytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(portBytes, port);
        Xor(packet.AsSpan(PortKeyAt, 2), portBytes);

        return packet;
    }

    private static void Xor(Span<byte> over, ReadOnlySpan<byte> with)
    {
        for (int at = 0; at < with.Length; at++)
            over[at] ^= with[at];
    }
}

/// <summary>
/// PP236: the reply where the core writes it.
/// </summary>
public static class PunchResponseSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the packet is still that size and the ids still at those offsets.</summary>
    public static bool TheLayoutIsStillThis(string core)
    {
        string body = Body(core);

        return body.Contains($"uint8_t confirm_buf[{PunchResponse.Length}]", StringComparison.Ordinal)
            && body.Contains("memcpy(&confirm_buf[0x4], session->hashed_id_local", StringComparison.Ordinal)
            && body.Contains("memcpy(&confirm_buf[0x24], session->hashed_id_console", StringComparison.Ordinal);
    }

    /// <summary>And whether the identifiers are still twenty bytes in a thirty-two byte stride.</summary>
    public static bool TheIdsAreStillShorterThanTheirSlot(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains($"uint8_t hashed_id_local[{PunchResponse.IdLength}];", StringComparison.Ordinal)
            && core.Contains($"uint8_t hashed_id_console[{PunchResponse.IdLength}];", StringComparison.Ordinal)
            && PunchResponse.ConsoleIdAt - PunchResponse.LocalIdAt == PunchResponse.IdSlot;
    }

    /// <summary>Whether the session ids are still the key the address is hidden under.</summary>
    public static bool TheIdsAreStillTheKey(string core)
    {
        string body = Body(core);

        return body.Contains("xor_bytes(&confirm_buf[0x50], console_addr, 4)", StringComparison.Ordinal)
            && body.Contains("xor_bytes(&confirm_buf[0x54], console_port, 2)", StringComparison.Ordinal);
    }

    /// <summary>Whether the family is still chosen by looking for a dot.</summary>
    public static bool TheFamilyIsStillChosenByADot(string core)
        => Body(core).Contains("strchr(candidate->addr, '.')", StringComparison.Ordinal);

    /// <summary>
    /// PP237: whether the two senders still build the SAME packet.
    ///
    /// They are the same function twice - forty identical lines - differing only in send against
    /// sendto. That is why this port has one builder, and this is what keeps it honest: a second
    /// copy in the core that drifted from the first would show up here rather than as a packet the
    /// console refuses on one path and accepts on the other.
    /// </summary>
    public static bool BothSendersStillBuildTheSamePacket(string core)
    {
        string one = Normalised(Body(core));
        string other = Normalised(BodyTo(core));

        return one.Length > 0 && one == other;
    }

    /// <summary>
    /// PP237: whether the second one still logs an int through a string format.
    ///
    /// True means the defect is present. Its sibling uses the macro that exists for this and
    /// expands to "%d"; this one writes a literal "%s" and hands it WSAGetLastError.
    /// </summary>
    public static bool TheOtherOneStillLogsAnIntAsAString(string core)
    {
        string body = BodyTo(core);

        return body.Contains(
                "failed for %s:%d with error: %s\", candidate->addr, candidate->port, CHIAKI_SOCKET_ERROR_VALUE",
                StringComparison.Ordinal)
            && Body(core).Contains("CHIAKI_SOCKET_ERROR_FMT", StringComparison.Ordinal);
    }

    /// <summary>
    /// The two bodies with everything that differs BY DESIGN removed - the name, the send call and
    /// the log line - so what is left is the packet each of them builds.
    /// </summary>
    private static string Normalised(string body)
    {
        int builds = body.IndexOf("uint8_t confirm_buf[88]", StringComparison.Ordinal);
        if (builds < 0)
            return "";

        int sends = body.IndexOf("if (send", StringComparison.Ordinal);
        if (sends <= builds)
            return "";

        return string.Concat(body[builds..sends].Where(c => !char.IsWhiteSpace(c)));
    }

    /// <summary>send_response_ps's body, cut at the two lines that bound it.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // LAST, for the reason PP213, PP233 and PP234 each wrote down.
        int start = core.LastIndexOf(
            "static ChiakiErrorCode send_response_ps(Session *session", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf(
            "static ChiakiErrorCode send_responseto_ps(", start, StringComparison.Ordinal);

        return end < 0 ? core[start..] : core[start..end];
    }

    /// <summary>And send_responseto_ps's, which is the same one with a different call at the end.</summary>
    private static string BodyTo(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int start = core.LastIndexOf(
            "static ChiakiErrorCode send_responseto_ps(Session *session", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf(
            "static ChiakiErrorCode receive_request_send_response_ps(", start, StringComparison.Ordinal);

        return end < 0 ? core[start..] : core[start..end];
    }
}
