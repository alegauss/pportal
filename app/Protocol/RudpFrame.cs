using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: what a RUDP packet says it is. Plain values, and NOT a mask - the low byte is 0x30 for
/// most of them, which no flags enumeration would produce.
/// </summary>
public enum RudpPacketType : ushort
{
    /// <summary>Opening the conversation.</summary>
    InitRequest = 0x8030,

    /// <summary>And the answer.</summary>
    InitResponse = 0xD000,

    /// <summary>Asking for the cookie.</summary>
    CookieRequest = 0x9030,

    /// <summary>And receiving it.</summary>
    CookieResponse = 0xA030,

    /// <summary>A session message, wrapped.</summary>
    SessionMessage = 0x2030,

    /// <summary>The switch's acknowledgement.</summary>
    StreamConnectionSwitchAck = 0x242E,

    /// <summary>A plain acknowledgement.</summary>
    Ack = 0x2430,

    /// <summary>A control message.</summary>
    CtrlMessage = 0x0230,

    /// <summary>Named for not being known.</summary>
    Unknown = 0x022F,

    /// <summary>A control message whose payload starts eight bytes in - see <see cref="RudpFrame"/>.</summary>
    Offset8 = 0x1230,

    /// <summary>And one whose payload starts ten bytes in.</summary>
    Offset10 = 0x2630,

    /// <summary>Closing it.</summary>
    Finish = 0xC000,
}

/// <summary>One RUDP frame, and whatever was nested inside it.</summary>
/// <param name="Size">The size field AS SENT, including the 0xC marker in its top nibble.</param>
/// <param name="Length">The same two bytes with the marker masked off - see <see cref="RudpFrame"/>.</param>
/// <param name="Type">What the packet says it is.</param>
/// <param name="Subtype">The type's HIGH BYTE, which the core stores as a field of its own.</param>
/// <param name="Data">The payload, truncated to what actually arrived.</param>
/// <param name="RemoteCounter">The first two payload bytes PLUS ONE, or zero when there are fewer than two.</param>
/// <param name="SubMessage">The next frame along, when eight or more bytes were left over.</param>
public sealed record RudpMessage(
    ushort Size,
    ushort Length,
    RudpPacketType Type,
    byte Subtype,
    byte[] Data,
    ushort RemoteCounter,
    RudpMessage? SubMessage);

/// <summary>
/// PP33: the RUDP frame - eight bytes of header, a payload, and possibly another frame behind it.
///
/// THE SIZE FIELD IS READ TWICE AND ANSWERS DIFFERENTLY. The top nibble of the first two bytes is a
/// 0xC marker, not length. The core reads those two bytes into <c>message->size</c>, then CLOBBERS
/// the buffer - <c>serialized_msg[0] &amp;= 0x0F</c> - and reads the same two bytes again into a
/// local called <c>length</c>. So the stored size keeps the marker and the length used for every
/// calculation does not, and a port that read the field once would have one of the two wrong
/// wherever it looked. Both are kept here, and the test pins them to the same bytes.
///
/// THE SUBTYPE IS THE TYPE'S HIGH BYTE. It is a separate field on the struct, filled from
/// <c>serialized_msg[6]</c> - the first of the two bytes the type was just read from. Nothing new
/// arrives on the wire to carry it. What makes it worth having is that the receive path matches on
/// its LOW NIBBLE: "a control message" is accepted when that nibble is 2 or 6, and FOUR types
/// satisfy that - CTRL_MESSAGE (0x0230), OFFSET8 (0x1230), OFFSET10 (0x2630) and the member the
/// enum literally calls UNKNOWN (0x022F). The two offset types are named for where their payload
/// starts and are admitted under a name they do not carry; so is the one that means "we do not know
/// what this is". A port matching on the TYPE would reject three of the four.
///
/// A LENGTH LONGER THAN WHAT ARRIVED IS TRUNCATED, NOT REFUSED. <c>data_size</c> is clamped to what
/// is left in the datagram, so a frame claiming more than it carries yields a short payload and no
/// error at all - the message is processed against fewer bytes than it advertised.
///
/// AND ANYTHING EIGHT BYTES OR LONGER LEFT OVER IS ANOTHER FRAME. The parse recurses on whatever
/// follows the payload, with no check that it looks like a frame first, so trailing bytes become a
/// sub-message whatever they are. The receive path leans on that: when it gets a message it was not
/// waiting for, it promotes the sub-message and looks again.
///
/// THE COUNTER IS THE PAYLOAD'S FIRST TWO BYTES PLUS ONE. Not the value sent - the next one. And a
/// payload of fewer than two bytes leaves it at zero, which is indistinguishable from a peer whose
/// counter really did wrap to 65535.
/// </summary>
public static class RudpFrame
{
    /// <summary>The four bytes that mark a RUDP frame, between the size and the type.</summary>
    public const uint Constant = 0x244F244F;

    /// <summary>The header, before any payload.</summary>
    public const int HeaderSize = 8;

    /// <summary>How many sent frames are kept for retransmission.</summary>
    public const int SendBufferSize = 16;

    /// <summary>How long a frame is waited for, in milliseconds.</summary>
    public const int ExpectTimeoutMs = 1000;

    /// <summary>The marker that sits in the size field's top nibble.</summary>
    public const int SizeMarker = 0xC;

    /// <summary>The subtype nibbles a control message is accepted with.</summary>
    public static IReadOnlyList<byte> CtrlSubtypeNibbles { get; } = [0x2, 0x6];

    /// <summary>The size field for a frame carrying this much payload, marker and all.</summary>
    public static ushort SizeFor(int dataSize)
        => (ushort)((SizeMarker << 12) | (HeaderSize + dataSize));

    /// <summary>
    /// A frame on the wire: the size, the constant, the type, the payload, then any sub-message
    /// immediately after - which is why a frame's own size does not cover what follows it.
    /// </summary>
    public static byte[] Serialise(RudpMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] sub = message.SubMessage is null ? [] : Serialise(message.SubMessage);
        var bytes = new byte[HeaderSize + message.Data.Length + sub.Length];

        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), message.Size);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2), Constant);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)message.Type);
        message.Data.CopyTo(bytes, HeaderSize);
        sub.CopyTo(bytes, HeaderSize + message.Data.Length);

        return bytes;
    }

    /// <summary>
    /// One frame, or null when there is not even a header there.
    ///
    /// Nothing else refuses: an unknown type, an oversized length and a payload that is not what it
    /// claims all parse - see the class note for why.
    /// </summary>
    public static RudpMessage? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
            return null;

        ushort size = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        var type = (RudpPacketType)BinaryPrimitives.ReadUInt16BigEndian(bytes[6..]);

        // The high byte of the two the type was just read from - no new bytes arrive to carry it.
        byte subtype = bytes[6];

        // The core reaches this by masking byte zero in place and reading the field again. The
        // clobber is how it computes the value; what it means is that the marker is not length.
        var length = (ushort)(size & 0x0FFF);

        int remaining = bytes.Length - HeaderSize;
        int dataSize = 0;
        ushort remoteCounter = 0;
        byte[] data = [];

        if (length > HeaderSize)
        {
            dataSize = Math.Min(length - HeaderSize, remaining);
            data = bytes.Slice(HeaderSize, dataSize).ToArray();

            if (dataSize >= 2)
                remoteCounter = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(data) + 1);
        }

        remaining -= dataSize;
        RudpMessage? sub = remaining >= HeaderSize
            ? Parse(bytes[(HeaderSize + dataSize)..])
            : null;

        return new RudpMessage(size, length, type, subtype, data, remoteCounter, sub);
    }
}

/// <summary>
/// PP33: the frame's rules where the Qt core states them.
/// </summary>
public static class RudpFrameSource
{
    /// <summary>Where the frame is parsed and written.</summary>
    public const string RelativePath = @"lib\src\remote\rudp.c";

    /// <summary>And where the types are declared.</summary>
    public const string HeaderPath = @"lib\include\chiaki\remote\rudp.h";

    /// <summary>The source file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The header, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderPath);

    /// <summary>Whether every packet type still holds the value this port was built against.</summary>
    public static bool TheTypesAreStillTheseValues(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        foreach (RudpPacketType type in Enum.GetValues<RudpPacketType>())
        {
            string value = "0x" + ((ushort)type).ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
            if (!header.Contains($"= {value},", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>Whether the size field is still read, clobbered, and read again.</summary>
    public static bool TheSizeIsStillReadTwice(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "message->size = ntohs(*(chiaki_unaligned_uint16_t *)(serialized_msg));", StringComparison.Ordinal)
            && core.Contains("serialized_msg[0] = serialized_msg[0] & 0x0F;", StringComparison.Ordinal)
            && core.Contains(
                "uint16_t length = ntohs(*(chiaki_unaligned_uint16_t *)(serialized_msg));", StringComparison.Ordinal);
    }

    /// <summary>Whether the subtype is still taken from a byte the type was already read from.</summary>
    public static bool TheSubtypeIsStillTheTypesHighByte(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("message->type = ntohs(*(chiaki_unaligned_uint16_t *)(serialized_msg + 6));", StringComparison.Ordinal)
            && core.Contains("message->subtype = serialized_msg[6] & 0xFF;", StringComparison.Ordinal);
    }

    /// <summary>Whether a control message is still admitted on those two nibbles.</summary>
    public static bool TheCtrlNibblesAreStillTwoAndSix(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "if((message->subtype & 0x0F) != 0x2 && (message->subtype & 0x0F) != 0x6)", StringComparison.Ordinal);
    }

    /// <summary>Whether an overlong length is still truncated rather than refused.</summary>
    public static bool AnOverlongLengthIsStillTruncated(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if(remaining < data_size)", StringComparison.Ordinal)
            && core.Contains("data_size = remaining;", StringComparison.Ordinal);
    }

    /// <summary>Whether eight leftover bytes still become another frame, unchecked.</summary>
    public static bool LeftoverBytesAreStillAnotherFrame(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if (remaining >= 8)", StringComparison.Ordinal)
            && core.Contains(
                "err = chiaki_rudp_message_parse(serialized_msg + 8 + data_size, remaining, message->subMessage);",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the counter is still the payload's first two bytes plus one.</summary>
    public static bool TheCounterIsStillOneMore(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("if(data_size >= 2)", StringComparison.Ordinal)
            && core.Contains(
                "message->remote_counter = ntohs(*(chiaki_unaligned_uint16_t *)(message->data)) + 1;",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the size field is still written with the marker in its top nibble.</summary>
    public static bool TheMarkerIsStillWrittenIn(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"({RudpFrame.SizeMarker:X}) << 12) | ", StringComparison.OrdinalIgnoreCase)
            || core.Contains("(0xC << 12) | ", StringComparison.Ordinal);
    }
}
