using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What chiaki_audio_sender_opus_data did with one encoded frame.</summary>
public enum MicSendOutcome
{
    /// <summary>Not the unit size, so it is dropped before anything else - the C's first line.</summary>
    WrongSize,

    /// <summary>The first frame there has ever been. Kept, and nothing is sent.</summary>
    FilledTheSecondSlot,

    /// <summary>The second. Kept, and nothing is sent - so a microphone is silent for two frames.</summary>
    FilledTheFirstSlot,

    /// <summary>Three units and a head went out.</summary>
    Sent,
}

/// <summary>
/// PP706, under PP52: audiosender.c, which is what composes the microphone's four pieces.
///
/// PP652 opened the capture, PP676 transcribed the head, PP694 wrote the encoder, and none of them
/// had met another: four classes, four test files, no caller. The thing that joined them was 143
/// lines of C, and this is that function with the four under it.
///
/// EVERY FRAME IS SENT THREE TIMES, which is the redundancy this exists for - a lost mic packet is
/// a word nobody hears, and the console takes three units per packet so two can go missing. The
/// buffer is filled from two kept frames and the new one.
///
/// AND THE FIRST SLOT IS OVERWRITTEN, which is reproduced rather than repaired. The C copies the
/// older kept frame into slot zero, the newer into slot one and the arrival into slot two - and
/// then copies the arrival into slot zero AGAIN, over the oldest. So the packet carries the newest
/// frame twice and the oldest never, and the redundancy covers one frame of loss rather than two.
/// A port that "fixed" it would send a packet the console has never been sent, which is the whole
/// argument this tree makes about the difference between a port and a rewrite.
///
/// TWO FRAMES OF SILENCE AT THE START. The first arrival fills one kept slot and returns, the
/// second fills the other and returns, and only the third sends anything. Twenty milliseconds at a
/// hundred units a second, once per session - and the reason a test that fed one frame and asserted
/// on a packet would find nothing.
///
/// THE HEAD IS PP676'S and the values are the C's. audiosender.c byte-swaps each field and then
/// stores it RAW, so <see cref="MicPacketHead.Write"/>'s native store produces the C's bytes
/// exactly when it is handed the swapped values - which is why they are swapped here rather than
/// there.
/// </summary>
public sealed class ManagedAudioSender
{
    /// <summary>buf_size_per_unit, which is also PP694's encoder frame.</summary>
    public const int UnitBytes = ManagedOpusEncoder.FrameBytes;

    /// <summary>How many units one packet carries.</summary>
    public const int UnitsPerPacket = 3;

    /// <summary>frame_buf_size: three units.</summary>
    public const int FrameBufferBytes = UnitBytes * UnitsPerPacket;

    /// <summary>TAKION_PACKET_TYPE_AUDIO, which the C spells as a literal 3 with the name in a comment.</summary>
    public const byte PacketType = 3;

    /// <summary>The codec byte, which audioreceiver.c on the other side requires to be exactly five.</summary>
    public const byte Codec = 5;

    /// <summary>units_in_frame_total, as the C writes it.</summary>
    public const uint UnitsInFrameTotal = 3;

    /// <summary>
    /// units_in_frame_fec, and it is a raw number rather than a count.
    ///
    /// 10273 with no derivation anywhere in the file. Carried as the C carries it: a constant the
    /// console is known to accept, and not a thing this port has any way to compute.
    /// </summary>
    public const uint UnitsInFrameFecRaw = 10273;

    private readonly bool ps5;
    private readonly Action<ReadOnlySpan<byte>>? send;

    private readonly byte[] frameBuffer = new byte[FrameBufferBytes];
    private readonly byte[] packet = new byte[FrameBufferBytes + MicPacketHead.SizePs5];
    private readonly byte[] older = new byte[UnitBytes];
    private readonly byte[] newer = new byte[UnitBytes];

    private bool haveOlder;
    private bool haveNewer;

    /// <param name="ps5">Whether the head carries its twentieth zero byte.</param>
    /// <param name="send">Where a finished packet goes, which is takion's mic send in the C.</param>
    public ManagedAudioSender(bool ps5, Action<ReadOnlySpan<byte>>? send = null)
    {
        this.ps5 = ps5;
        this.send = send;
    }

    /// <summary>frame_index, which is what both head counters are derived from.</summary>
    public ushort FrameIndex { get; private set; }

    /// <summary>How many packets have gone out.</summary>
    public int Sent { get; private set; }

    /// <summary>How many frames were dropped for not being the unit size.</summary>
    public int Dropped { get; private set; }

    /// <summary>The head's size on this console.</summary>
    public int HeadBytes => MicPacketHead.SizeFor(ps5);

    /// <summary>One packet's whole length: the head and three units.</summary>
    public int PacketBytes => HeadBytes + FrameBufferBytes;

    /// <summary>
    /// chiaki_audio_sender_opus_data: one encoded frame in, and a packet out every third time.
    /// </summary>
    /// <param name="frame">An Opus frame, which must be exactly <see cref="UnitBytes"/>.</param>
    public MicSendOutcome OpusData(ReadOnlySpan<byte> frame)
    {
        // The C's first line, and the same test PP694 found inside the encoder. A frame that is not
        // the unit size is dropped twice on the way here, which is not redundancy - the encoder's
        // arm logs it and this one does not.
        if (frame.Length != UnitBytes)
        {
            Dropped++;
            return MicSendOutcome.WrongSize;
        }

        if (!haveNewer)
        {
            frame.CopyTo(newer);
            haveNewer = true;
            return MicSendOutcome.FilledTheSecondSlot;
        }

        if (!haveOlder)
        {
            frame.CopyTo(older);
            haveOlder = true;
            return MicSendOutcome.FilledTheFirstSlot;
        }

        // Four copies into three slots, in the C's order. The fourth is the one worth reading twice:
        // it puts the arrival back over slot zero, so the oldest frame never leaves.
        newer.CopyTo(frameBuffer.AsSpan(0));
        older.CopyTo(frameBuffer.AsSpan(UnitBytes));
        frame.CopyTo(frameBuffer.AsSpan(2 * UnitBytes));
        frame.CopyTo(frameBuffer.AsSpan(0));

        // And then both kept frames become the arrival, by way of each other.
        frame.CopyTo(older);
        older.CopyTo(newer.AsSpan());

        Span<byte> head = packet.AsSpan(0, HeadBytes);

        MicPacketHead.Write(
            head,
            PacketType,
            // Swapped here because the C swaps and then stores RAW, and PP676's writer stores raw.
            BinaryPrimitives.ReverseEndianness(FrameIndex),
            BinaryPrimitives.ReverseEndianness((ushort)(FrameIndex + 1)),
            BinaryPrimitives.ReverseEndianness(UnitsNumber),
            Codec,
            ps5);

        frameBuffer.CopyTo(packet.AsSpan(HeadBytes));

        send?.Invoke(packet.AsSpan(0, PacketBytes));

        // After the send, and wrapping at the width rather than by overflow - which is the same
        // answer here and is the C's own branch.
        FrameIndex = FrameIndex == ushort.MaxValue ? (ushort)0 : (ushort)(FrameIndex + 1);
        Sent++;

        return MicSendOutcome.Sent;
    }

    /// <summary>
    /// The packed word at offset five, before the byte swap.
    ///
    /// The FEC count in the low sixteen bits, the total minus one at sixteen, the unit index at
    /// twenty-four - which is the AUDIO layout of the AV header's own packed word, one field wider.
    /// </summary>
    public static uint UnitsNumber
        => (UnitsInFrameFecRaw & 0xffff)
            | (((UnitsInFrameTotal - 1) & 0xff) << 0x10)
            | ((0u & 0xff) << 0x18);
}

/// <summary>
/// PP706: audiosender.c's own composition, so the port above cannot drift off it.
///
/// Five claims and four of them are ORDERS. The calls are all present in any arrangement, and the
/// arrangement is where the behaviour is: which arrival is kept, which slot is written last, and
/// when the counter moves.
/// </summary>
public static class ManagedAudioSenderSource
{
    /// <summary>The file.</summary>
    public const string RelativePath = @"lib\src\audiosender.c";

    /// <summary>It, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>chiaki_audio_sender_opus_data's body, or null where it has moved.</summary>
    public static string? Body(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CFunction.Body(source, "void chiaki_audio_sender_opus_data");
    }

    /// <summary>
    /// Whether a frame that is not the unit size is still dropped before anything else.
    ///
    /// Its own comment says why: a silent packet encodes to three units rather than forty, which is
    /// PP694's measurement arriving at the other end of the path.
    /// </summary>
    public static bool AWrongSizedFrameIsDroppedFirst(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return CCall.Mark(
            CCall.Compact(CCall.Code(body)),
            "if(opus_sender_size != audio_sender->buf_size_per_unit) return;") >= 0;
    }

    /// <summary>Whether the first two arrivals are still kept and not sent.</summary>
    public static bool TheFirstTwoArrivalsAreKept(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return CCall.InOrder(
            CCall.Compact(CCall.Code(body)),
            "if(!audio_sender->frameb)",
            "memcpy(audio_sender->frameb, opus_sender, opus_sender_size); return;",
            "if(!audio_sender->framea)",
            "memcpy(audio_sender->framea, opus_sender, opus_sender_size); return;");
    }

    /// <summary>
    /// Whether the arrival is still copied over slot zero AFTER the three slots are filled.
    ///
    /// The finding, held as an order rather than as a count: the three fills and the fourth copy are
    /// all memcpys into the same buffer, and only their sequence says the oldest frame is discarded.
    /// </summary>
    public static bool TheArrivalOverwritesTheOldestSlot(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return CCall.InOrder(
            CCall.Compact(CCall.Code(body)),
            "memcpy(audio_sender->frame_buf, audio_sender->frameb, audio_sender->buf_size_per_unit);",
            "memcpy(audio_sender->frame_buf + audio_sender->buf_size_per_unit, audio_sender->framea, audio_sender->buf_size_per_unit);",
            "memcpy(audio_sender->frame_buf + 2 * audio_sender->buf_size_per_unit, opus_sender, opus_sender_size);",
            "memcpy(audio_sender->frame_buf, opus_sender, opus_sender_size);");
    }

    /// <summary>
    /// Whether the counter still moves AFTER the send and wraps at the sixteen-bit maximum.
    ///
    /// Both halves. Moving it before would put the next packet's index on this packet, and the wrap
    /// is a branch rather than an overflow - the same answer, written the way the C writes it.
    /// </summary>
    public static bool TheCounterMovesAfterTheSend(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (CFunction.Body(source, "static void chiaki_audio_sender_frame") is not { } body)
            return false;

        return CCall.InOrder(
            CCall.Compact(CCall.Code(body)),
            "chiaki_takion_send_mic_packet(",
            "if(audio_sender->frame_index == UINT16_MAX)",
            "audio_sender->frame_index = 0;");
    }

    /// <summary>The unit size and the buffer's, read out of the init rather than typed here.</summary>
    public static (int Unit, int Frames)? SizesIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Compact(CCall.Code(source));

        int at = code.IndexOf("audio_sender->buf_size_per_unit=", StringComparison.Ordinal);
        if (at < 0)
            return null;

        int end = code.IndexOf(';', at);
        if (end < 0 || !int.TryParse(code[(at + "audio_sender->buf_size_per_unit=".Length)..end], out int unit))
            return null;

        return code.Contains(
            "audio_sender->frame_buf_size=3*audio_sender->buf_size_per_unit;", StringComparison.Ordinal)
            ? (unit, 3)
            : null;
    }
}
