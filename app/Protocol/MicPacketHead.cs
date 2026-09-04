using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP676: the microphone packet's head, which audiosender.c writes and takion.c finishes.
///
/// The third of the three sends outside PP497's MAC table, and the only one whose head is built
/// somewhere other than the send. chiaki_audio_sender_frame fills nineteen bytes - twenty on a PS5 -
/// and hands them to chiaki_takion_send_mic_packet, which overwrites two of the fields it left
/// zeroed.
///
/// THREE FIELDS ARE WRITTEN IN NATIVE ORDER AND TWO ARE NOT, which is the hazard. The feedback head
/// puts every multi-byte field through htons or htonl; this one writes packet_index, frame_index and
/// units_number as raw stores - little-endian on every machine this port builds for - while the MAC
/// at ten and the key position at fourteen go through htonl inside the send. A port that made the
/// head consistent would produce a packet of the right length whose indices the console reads
/// byte-reversed, and the symptom is audio that arrives and is discarded.
///
/// THE ZERO BYTE AT EIGHTEEN, AND AT NINETEEN ON A PS5, is what makes the head one byte longer
/// there - so it moves where the payload starts and therefore what gets encrypted. See
/// <see cref="TakionFeedbackSends.MicrophoneFor"/>, which is the same one byte from the other end.
///
/// NOT DRIVEN AGAINST THE C. The head is built inside a callback that needs an opus encoder, a
/// takion and an audio sender, so there is no export to compare against the way
/// <see cref="NativeFeedback"/> compares the feedback payloads. What holds this instead is
/// <see cref="TheCStillWritesTheseOffsets"/>, over audiosender.c itself.
/// </summary>
public static class MicPacketHead
{
    /// <summary>The head on a PS4.</summary>
    public const int Size = 19;

    /// <summary>And on a PS5, one zero byte longer.</summary>
    public const int SizePs5 = 20;

    /// <summary>How long the head is on a console of this generation.</summary>
    public static int SizeFor(bool ps5) => ps5 ? SizePs5 : Size;

    /// <summary>Where audiosender.c writes each field, which is the subject of the check below.</summary>
    public static IReadOnlyList<(int At, string Field)> Offsets { get; } =
    [
        (0, "packet_type"),
        (1, "packet_index"),
        (3, "frame_index"),
        (5, "units_number"),
        (9, "codec"),
        (10, "gmac"),
        (14, "key_pos"),
        (18, "zero_byte"),
    ];

    /// <summary>
    /// The head, as audiosender.c writes it before the send overwrites two fields.
    /// </summary>
    /// <param name="head">At least <see cref="SizeFor"/> bytes.</param>
    /// <param name="packetType">The audio packet type byte.</param>
    /// <param name="packetIndex">Native order, which is what the C's raw store produces.</param>
    /// <param name="frameIndex">The same.</param>
    /// <param name="unitsNumber">The same.</param>
    /// <param name="codec">The codec byte at nine.</param>
    /// <param name="ps5">Whether a twentieth zero byte follows the nineteenth.</param>
    /// <remarks>
    /// The MAC and the key position are left ZEROED here, exactly as the C leaves them: both are
    /// written by the send, after the payload is encrypted and in big-endian order. Writing them
    /// here would be writing them twice and disagreeing about the order.
    /// </remarks>
    public static void Write(
        Span<byte> head,
        byte packetType,
        ushort packetIndex,
        ushort frameIndex,
        uint unitsNumber,
        byte codec,
        bool ps5)
    {
        int size = SizeFor(ps5);
        if (head.Length < size)
            throw new ArgumentException($"a mic head is {size} bytes", nameof(head));

        head[..size].Clear();

        head[0] = packetType;

        // LITTLE-endian, and that is the C's raw store rather than a choice made here. The feedback
        // head next to this one is big-endian in every field.
        BinaryPrimitives.WriteUInt16LittleEndian(head[1..], packetIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(head[3..], frameIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(head[5..], unitsNumber);

        head[9] = codec;

        // Ten and fourteen stay zero: the send writes the GMAC and the key position, big-endian.
        head[18] = 0;
        if (ps5)
            head[19] = 0;
    }

    /// <summary>Where the C is, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\audiosender.c";

    /// <summary>The C, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether audiosender.c still writes every field at the offset this transcribes.
    ///
    /// Read as the C spells them - <c>filled_packet_buf + 3</c> and <c>filled_packet_buf[9]</c> are
    /// the same field written two ways, so both forms are looked for. What this catches is an
    /// offset moving, which is the one change that leaves this file compiling and wrong.
    /// </summary>
    public static IReadOnlyList<string> OffsetsMissingFrom(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);

        return
        [
            .. Offsets
                .Where(one => !code.Contains($"filled_packet_buf + {one.At})", StringComparison.Ordinal)
                    && !code.Contains($"filled_packet_buf[{one.At}]", StringComparison.Ordinal))
                .Select(one => $"{one.Field} at {one.At}"),
        ];
    }

    /// <summary>
    /// And whether the three native-order fields are STILL native order.
    ///
    /// The claim this file rests on. A C that started swapping them would make this port's
    /// little-endian writes wrong, and nothing else here would notice - so the absence of htons and
    /// htonl around those three stores is asserted rather than assumed.
    /// </summary>
    public static bool TheThreeAreStillWrittenRaw(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);

        return code.Contains(
                "*(chiaki_unaligned_uint16_t *)(audio_sender->filled_packet_buf + 1) = packet_index;",
                StringComparison.Ordinal)
            && code.Contains(
                "*(chiaki_unaligned_uint16_t *)(audio_sender->filled_packet_buf + 3) = frame_index;",
                StringComparison.Ordinal)
            && code.Contains(
                "*(chiaki_unaligned_uint32_t *)(audio_sender->filled_packet_buf + 5) = units_number;",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the C still makes the head one byte longer on a PS5.</summary>
    public static bool TheHeadIsStillLongerOnAPs5(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);

        return code.Contains("+ 19 + ps5_packet", StringComparison.Ordinal)
            && code.Contains("filled_packet_buf[19] = zero_byte", StringComparison.Ordinal);
    }
}
