namespace ChiakiNg.Protocol;

/// <summary>
/// PP423: blanking the fields of a protobuf, so a message can be recorded without its keys.
///
/// PP325 made this argument for HTTP heads: it replaced a line-shaped redaction rule with a
/// field-shaped one, so RP-Registkey could be taken without taking the request. This is the same
/// move one encoding over. PP326 takes a protobuf payload WHOLE, which for the BIG is right - five
/// of its six fields are secret - and for the BANG hides the console's verdict on the handshake
/// along with the two optional key fields that share the message.
///
/// THE VALUE BYTES ARE ZEROED AND THE STRUCTURE IS KEPT. Tags and lengths stay, so the result is
/// still a protobuf a reader can decode and the fields that were not blanked are where they were.
/// Re-encoding without the fields would shorten every length above them and produce bytes no
/// session ever sent.
///
/// A LENGTH IS NOT A SECRET HERE. Zeroing in place leaves the size of each blanked field visible,
/// and those are fixed by the protocol - a 32-byte signature is 32 bytes whoever is talking.
///
/// ONE LEVEL OF NESTING, DELIBERATELY. The fields to blank sit inside bang_payload, which is itself
/// a length-delimited field of the message. A general walker would be a protobuf library; this
/// descends exactly once, into a field named by the caller, which is the whole of what the recording
/// needs. Anything it cannot parse is refused rather than half-blanked - see <see cref="Blank"/>.
/// </summary>
public static class ProtobufRedaction
{
    /// <summary>Wire type 2: length-delimited, which is what a nested message is.</summary>
    private const int LengthDelimited = 2;

    /// <summary>
    /// A copy of <paramref name="payload"/> with the named fields of one nested message zeroed, or
    /// null where it cannot be parsed.
    ///
    /// NULL IS A REFUSAL AND THE CALLER MUST TREAT IT AS ONE. A payload this cannot walk is one
    /// whose fields it cannot find, so blanking nothing and returning it would publish exactly the
    /// bytes it was asked to hide. PP326's principle: with no field identified there is no basis to
    /// record it.
    /// </summary>
    /// <param name="payload">The whole message.</param>
    /// <param name="nestedField">The field number the nested message sits at.</param>
    /// <param name="blank">The field numbers to zero inside it.</param>
    public static byte[]? Blank(
        ReadOnlySpan<byte> payload, int nestedField, IReadOnlySet<int> blank)
    {
        ArgumentNullException.ThrowIfNull(blank);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nestedField);

        byte[] copy = payload.ToArray();

        // Find the nested message among the top-level fields.
        if (!TryFindField(copy, 0, copy.Length, nestedField, out int nestedAt, out int nestedLength))
            return null;

        // And blank each named field inside it. A field that is not there is not an error: they are
        // optional, and an absent key is nothing to hide.
        foreach (int field in blank)
        {
            if (TryFindField(copy, nestedAt, nestedAt + nestedLength, field, out int at, out int length))
                Array.Clear(copy, at, length);
        }

        return copy;
    }

    /// <summary>
    /// Where one field's VALUE starts inside a range, and how long it is.
    ///
    /// Walks the fields in order, because a protobuf gives no index. A varint or a fixed-width field
    /// is reported by its own width, so a caller blanking one zeroes a number rather than corrupting
    /// the frame.
    /// </summary>
    public static bool TryFindField(
        ReadOnlySpan<byte> buffer, int from, int to, int field, out int at, out int length)
    {
        at = 0;
        length = 0;

        if (from < 0 || to > buffer.Length || from > to || field <= 0)
            return false;

        int cursor = from;
        while (cursor < to)
        {
            if (!TryReadVarint(buffer, ref cursor, to, out ulong tag))
                return false;

            var number = (int)(tag >> 3);
            var wireType = (int)(tag & 0x7);

            if (!TryValueSpan(buffer, ref cursor, to, wireType, out int valueAt, out int valueLength))
                return false;

            if (number == field)
            {
                at = valueAt;
                length = valueLength;
                return true;
            }
        }

        return false;
    }

    /// <summary>Where the value of the field whose tag was just read sits, by wire type.</summary>
    private static bool TryValueSpan(
        ReadOnlySpan<byte> buffer, ref int cursor, int to, int wireType,
        out int valueAt, out int valueLength)
    {
        valueAt = cursor;
        valueLength = 0;

        switch (wireType)
        {
            case 0: // varint
            {
                int start = cursor;
                if (!TryReadVarint(buffer, ref cursor, to, out _))
                    return false;

                valueAt = start;
                valueLength = cursor - start;
                return true;
            }

            case 1: // 64-bit
                return TryFixed(ref cursor, to, 8, out valueAt, out valueLength);

            case LengthDelimited:
            {
                if (!TryReadVarint(buffer, ref cursor, to, out ulong declared))
                    return false;

                if (declared > (ulong)(to - cursor))
                    return false;

                valueAt = cursor;
                valueLength = (int)declared;
                cursor += valueLength;
                return true;
            }

            case 5: // 32-bit
                return TryFixed(ref cursor, to, 4, out valueAt, out valueLength);

            // Groups, and anything the wire format does not define. Not parsed rather than guessed.
            default:
                return false;
        }
    }

    private static bool TryFixed(ref int cursor, int to, int width, out int at, out int length)
    {
        at = cursor;
        length = width;

        if (to - cursor < width)
            return false;

        cursor += width;
        return true;
    }

    /// <summary>
    /// A base-128 varint, bounded.
    ///
    /// Ten bytes is the most a 64-bit varint takes, and refusing an eleventh is what stops a payload
    /// of continuation bytes from walking off the end.
    /// </summary>
    private static bool TryReadVarint(
        ReadOnlySpan<byte> buffer, ref int cursor, int to, out ulong value)
    {
        value = 0;

        for (var shift = 0; shift < 70; shift += 7)
        {
            if (cursor >= to)
                return false;

            byte b = buffer[cursor++];
            value |= (ulong)(b & 0x7f) << shift;

            if ((b & 0x80) == 0)
                return true;
        }

        return false;
    }
}
