using System.Buffers.Binary;

namespace AllocBudget;

/// <summary>
/// The reference parse the budget is measured over: a readonly struct filled from a span, so
/// parsing a packet allocates nothing by construction rather than by care.
///
/// It is deliberately partial. It reads the fields test/takion.c already asserts for this capture -
/// enough to prove the real bytes were read - and stops there. Reimplementing the whole AV header
/// would be writing PP27's parser inside PP44, and PP44 is filed before PP27 precisely so the
/// budget does not wait on the transport. What PP27 replaces is this type; what it keeps is the
/// assertion in Program.
/// </summary>
internal readonly struct TakionAvHeader
{
    /// <summary>Base type 2 is video, 3 is audio, per the C parser's TAKION_PACKET_TYPE_*.</summary>
    private const byte BaseTypeMask = 0x0f;
    private const byte BaseTypeVideo = 2;

    /// <summary>Offset the payload starts at for a v9 video packet, as the C test asserts.</summary>
    private const int VideoPayloadOffset = 0x15;

    private TakionAvHeader(byte baseType, ushort packetIndex, ushort frameIndex)
    {
        BaseType = baseType;
        PacketIndex = packetIndex;
        FrameIndex = frameIndex;
    }

    public byte BaseType { get; }

    public ushort PacketIndex { get; }

    public ushort FrameIndex { get; }

    public bool IsVideo => (BaseType & BaseTypeMask) == BaseTypeVideo;

    public static bool TryParse(ReadOnlySpan<byte> packet, out TakionAvHeader header)
    {
        if (packet.Length < VideoPayloadOffset)
        {
            header = default;
            return false;
        }

        header = new TakionAvHeader(
            packet[0],
            BinaryPrimitives.ReadUInt16BigEndian(packet[1..3]),
            BinaryPrimitives.ReadUInt16BigEndian(packet[3..5]));
        return true;
    }

    /// <summary>The payload, as a span over the caller's buffer. No copy, so no allocation.</summary>
    public ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> packet) =>
        packet.Length <= VideoPayloadOffset ? default : packet[VideoPayloadOffset..];
}
