using System.Buffers.Binary;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>The unit counts and codec byte an AV head carries.</summary>
/// <param name="UnitIndex">Which unit of the frame this is.</param>
/// <param name="UnitsInFrameTotal">How many units the frame has. Already the C's plus one.</param>
/// <param name="UnitsInFrameFec">How many of them are FEC. One, for every video packet measured.</param>
/// <param name="Codec">av[8]. Five on audio, three on video, 255 before the cipher.</param>
public readonly record struct AvHeadCounts(
    int UnitIndex, int UnitsInFrameTotal, int UnitsInFrameFec, byte Codec);

/// <summary>
/// PP524, under PP27: the fields an AV head carries that nothing had looked at.
///
/// PP510's head is eighteen bytes and four of its fields have been used. For an AV packet it also
/// carries the unit counts and the codec byte, and this reads them - at the offsets av_packet_parse
/// reads them from, with video's and audio's different bit layouts kept apart because they are.
///
/// VIDEO SENDS EXACTLY ONE FEC UNIT PER FRAME. Every post-cipher video packet of a real session -
/// 1610 of them - carries units_in_frame_fec of one, over frames of thirteen to twenty-nine units.
/// PP30 calls FEC "two vendored C libraries doing Galois field arithmetic per lost packet"; the
/// ratio says what that arithmetic is for, which is one recoverable loss per frame.
///
/// THE CODEC BYTE IS READ ON ONE CHANNEL AND NOT THE OTHER. Nothing in the video path reads it -
/// the codec everything uses comes from the launch spec. audioreceiver.c requires it to be exactly
/// five and drops the packet otherwise, logging an unknown codec.
///
/// AND THE PROLOGUE'S AV PACKETS CARRY 255. Eleven audio and one video, all before the cipher, all
/// with a byte the audio receiver would refuse - and none of them reaches it, because PP490's
/// dispatch postpones an AV packet while the cipher is missing. The field, the guard that would
/// refuse it, and the branch that means the guard is never asked, meeting at one measured fact.
/// </summary>
public static class AvHeadFields
{
    /// <summary>Where dword_2 sits in the datagram - av+4, and av is buf+1.</summary>
    public const int Dword2Offset = 5;

    /// <summary>Where the codec byte sits - av+8.</summary>
    public const int CodecOffset = 9;

    /// <summary>What audioreceiver.c requires before it will decode.</summary>
    public const byte AudioCodec = 5;

    /// <summary>The smallest head these fields fit in.</summary>
    public const int MinimumHead = CodecOffset + 1;

    /// <summary>
    /// Reads the counts out of a head, or null where it is too short or not AV.
    /// </summary>
    /// <remarks>
    /// Video and audio pack dword_2 differently and the C says so: video takes eleven bits of unit
    /// index, eleven of total and ten of FEC; audio takes eight, eight and sixteen. Reading one
    /// layout for both gives numbers that look plausible and are not.
    /// </remarks>
    public static AvHeadCounts? Read(ReadOnlySpan<byte> head)
    {
        if (head.Length < MinimumHead)
            return null;

        int baseType = head[0] & TakionDispatch.BaseTypeMask;
        if (baseType is not (TakionDispatch.Video or TakionDispatch.Audio))
            return null;

        uint dword2 = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(Dword2Offset, 4));

        return baseType == TakionDispatch.Video
            ? new AvHeadCounts(
                (int)((dword2 >> 0x15) & 0x7ff),
                (int)(((dword2 >> 0xa) & 0x7ff) + 1),
                (int)(dword2 & 0x3ff),
                head[CodecOffset])
            : new AvHeadCounts(
                (int)((dword2 >> 0x18) & 0xff),
                (int)(((dword2 >> 0x10) & 0xff) + 1),
                (int)(dword2 & 0xffff),
                head[CodecOffset]);
    }
}

/// <summary>
/// PP524: the C's own offsets and the one guard that reads the codec byte.
/// </summary>
public static class AvHeadFieldsSource
{
    /// <summary>audioreceiver.c, the only consumer of the codec field.</summary>
    public const string AudioReceiverRelativePath = @"lib\src\audioreceiver.c";

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? Locate() => TakionPostpone.Locate();

    /// <summary>audioreceiver.c, or null outside a checkout.</summary>
    public static string? LocateAudioReceiver()
        => SanitizerSource.LocateRelative(AudioReceiverRelativePath);

    /// <summary>The parser, where the fields are read.</summary>
    public static string? ParseBody(string takionSource)
        => CFunction.Body(takionSource, "static ChiakiErrorCode av_packet_parse(bool v12");

    /// <summary>
    /// Whether video and audio still unpack dword_2 differently.
    ///
    /// The claim the reader above rests on. One layout for both gives numbers that look plausible,
    /// which is the kind of wrong that survives a review.
    /// </summary>
    public static bool TheTwoLayoutsAreStillDifferent(string parseBody)
    {
        ArgumentNullException.ThrowIfNull(parseBody);

        return parseBody.Contains("(uint16_t)((dword_2 >> 0x15) & 0x7ff)", StringComparison.Ordinal)
            && parseBody.Contains("(uint16_t)(((dword_2 >> 0xa) & 0x7ff) + 1)", StringComparison.Ordinal)
            && parseBody.Contains("(uint16_t)((dword_2 >> 0x18) & 0xff)", StringComparison.Ordinal)
            && parseBody.Contains("(uint16_t)(((dword_2 >> 0x10) & 0xff) + 1)", StringComparison.Ordinal);
    }

    /// <summary>Whether the codec is still read from av+8, which is where this reads it.</summary>
    public static bool TheCodecIsStillAtAvEight(string parseBody)
    {
        ArgumentNullException.ThrowIfNull(parseBody);
        return parseBody.Contains("packet->codec = av[8];", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the audio receiver still refuses anything but five.
    ///
    /// The one consumer of the field. If it stopped checking, the prologue's 255 would reach a
    /// decoder instead of a log line - which is only not happening because the packets are
    /// postponed first.
    /// </summary>
    public static bool TheAudioReceiverDemandsFive(string audioReceiverSource)
    {
        ArgumentNullException.ThrowIfNull(audioReceiverSource);

        return audioReceiverSource.Contains(
                $"if(packet->codec != {AvHeadFields.AudioCodec})", StringComparison.Ordinal)
            && audioReceiverSource.Contains(
                "Received Audio Packet with unknown Codec", StringComparison.Ordinal);
    }
}
