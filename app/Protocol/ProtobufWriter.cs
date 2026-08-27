namespace ChiakiNg.Protocol;

/// <summary>
/// PP425: writing a protobuf from named fields, so a participant builds its messages rather than
/// reciting them.
///
/// PP421 and PP424 gave senkusha and the stream connection replay participants whose ORDER was
/// derived from the C and whose BYTES were transcribed out of the corpus. Half of each replay was
/// therefore a tautology: the expectation and the answer had been read from the same file.
///
/// PP422 IS THE EVIDENCE THAT IT MATTERS. Exactly one payload in those two was built rather than
/// copied - the microphone's audio header, from the arguments of one C call and the layout of
/// another - and it is the one that found a defect. The copied bytes could not have found anything,
/// because they WERE the defect, written down.
///
/// SO THE FIELDS ARE NAMED AND THE BYTES ARE DERIVED. What a reader checks is then the field number
/// and the value against lib/protobuf/takion.proto, which is a document, rather than a run of hex
/// against a recording of itself.
///
/// <see cref="ProtobufRedaction"/> walks a message field by field; this is the other direction. It
/// is deliberately not a protobuf library: varints, length-delimited fields and nesting one level,
/// which is the whole of what these handshakes are.
/// </summary>
public static class ProtobufWriter
{
    /// <summary>A varint field, as protobuf encodes one.</summary>
    public static byte[] Varint(int field, ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(field);

        var bytes = new List<byte>(11);
        WriteTag(bytes, field, 0);
        WriteVarint(bytes, value);

        return [.. bytes];
    }

    /// <summary>A boolean, which protobuf writes as a varint of one or zero.</summary>
    public static byte[] Bool(int field, bool value) => Varint(field, value ? 1u : 0u);

    /// <summary>
    /// A length-delimited field: a string, a byte array, or a nested message.
    ///
    /// An empty value still writes its tag and a zero length, because "present and empty" is what
    /// senkusha's BIG says about its three credential fields and is not the same as absent.
    /// </summary>
    public static byte[] Bytes(int field, ReadOnlySpan<byte> value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(field);

        var bytes = new List<byte>(value.Length + 11);
        WriteTag(bytes, field, 2);
        WriteVarint(bytes, (ulong)value.Length);
        bytes.AddRange(value);

        return [.. bytes];
    }

    /// <summary>A nested message, from the fields it is made of.</summary>
    public static byte[] Message(int field, params byte[][] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return Bytes(field, Concat(fields));
    }

    /// <summary>Several fields, one after another.</summary>
    public static byte[] Concat(params byte[][] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var bytes = new List<byte>();
        foreach (byte[] field in fields)
            bytes.AddRange(field);

        return [.. bytes];
    }

    private static void WriteTag(List<byte> into, int field, int wireType)
        => WriteVarint(into, ((ulong)field << 3) | (uint)wireType);

    private static void WriteVarint(List<byte> into, ulong value)
    {
        while (value >= 0x80)
        {
            into.Add((byte)(value | 0x80));
            value >>= 7;
        }

        into.Add((byte)value);
    }
}
