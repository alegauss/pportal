using System.Buffers.Binary;
using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP341, under PP294: the eight bytes in front of every control message, and the length that did
/// not fit its own sum.
///
/// A ctrl message on the wire is a header and then the payload, encrypted. The header is four bytes
/// of payload length, two of message type, and two zero - all big-endian, which is the only thing
/// about it that is conventional.
///
/// THE DEFECT THIS CARRIES AN ASSERTION FOR. On the rudp path the header and payload are joined in
/// one stack buffer whose length was declared <c>uint8_t</c> while the sum that filled it was a
/// <c>size_t</c>. At a payload of 248 the length wrapped to zero and the header copy alone wrote
/// eight bytes past it. Every caller inside ctrl.c passes a literal of 0x10 or less, so it was not
/// reachable from there - but login_pin_size arrives through chiaki_session_set_login_pin, which
/// takes a size_t from its caller and bounds it nowhere.
///
/// The check below is written against the SHAPE: a VLA whose length is narrower than the size_t
/// that computed it. That is the defect wherever it appears, and reading for it rather than for
/// the line is what found a second copy of PP339's bug.
/// </summary>
public static partial class CtrlFraming
{
    /// <summary>The header is eight bytes, whatever the payload.</summary>
    public const int HeaderSize = 8;

    /// <summary>
    /// The header for one message, exactly as ctrl_message_send lays it out.
    /// </summary>
    public static byte[] Header(ushort type, int payloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadSize);

        var header = new byte[HeaderSize];

        // Big-endian for both, and the last two bytes are written zero rather than left.
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payloadSize);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), type);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), 0);

        return header;
    }

    /// <summary>The payload length a header announces.</summary>
    public static uint PayloadSizeOf(ReadOnlySpan<byte> header)
        => BinaryPrimitives.ReadUInt32BigEndian(header);

    /// <summary>The message type a header announces.</summary>
    public static ushort TypeOf(ReadOnlySpan<byte> header)
        => BinaryPrimitives.ReadUInt16BigEndian(header[4..]);

    /// <summary>Where the framing lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Every variable-length array whose length was declared narrower than <c>size_t</c>.
    ///
    /// A VLA sized by a narrow type is the defect PP341 was: the declaration truncates while
    /// everything that fills the buffer keeps the real length. Matched as a shape so a second one
    /// written the same way is found without anybody remembering this.
    /// </summary>
    /// <returns>The declaration of each, so a failure names what it found.</returns>
    public static IReadOnlyList<string> ArraysSizedByANarrowLength(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (Match declared in NarrowLength().Matches(source))
        {
            string name = declared.Groups["name"].Value;

            // Only where that name is then used as an array length, which is what makes the
            // truncation reachable rather than merely present.
            if (Regex.IsMatch(source, @"\[\s*" + Regex.Escape(name) + @"\s*\]"))
                found.Add(declared.Value.Trim());
        }

        return found;
    }

    // A narrow integer declared from an expression that adds something - which is where a size_t
    // gets truncated. size_t and int declarations are not matched.
    [GeneratedRegex(@"\b(?:uint8_t|int8_t|uint16_t|int16_t)\s+(?<name>\w*(?:size|len|length|count)\w*)\s*=\s*[^;]*\+[^;]*;",
        RegexOptions.IgnoreCase)]
    private static partial Regex NarrowLength();
}
