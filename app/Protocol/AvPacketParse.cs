using System.Buffers.Binary;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP668: the AV header parsed in managed code, so the v12 layout's haptics bit can ever be true.
///
/// Every AvPacket in the port is built from the shim's v9 export, and the C writes is_haptics
/// exactly once, under <c>if(v12 &amp;&amp; !packet->is_video)</c>. So the bit is false for every
/// packet the port sees, and PP667's route - which tests haptics before audio, as PP366's third
/// check demands - has an arm that can never fire. A console sending the v12 layout, which is what
/// a DualSense session negotiates, has its haptics packets handed to the speakers as silence.
///
/// MANAGED RATHER THAN A SECOND SHIM EXPORT. Both were open; this removes a native call instead of
/// adding one, and PP680 and PP27 need the same arithmetic in managed code anyway. The key position
/// was the one piece that stayed native, because <see cref="KeyState"/> holds a ledger the C owns;
/// PP677 has since transcribed it, so this takes <see cref="IKeyPositionLedger"/> and a session can
/// run with no native handle at all while the differential still drives the shim's.
///
/// THE V9 ARM IS THE ORACLE. This walks the same bytes as av_packet_parse for both versions, so the
/// v9 result must equal the shim's on every input - which is a differential over the real parser
/// rather than a reading of it, and is what makes the v12 arm trustworthy without a v12 corpus.
///
/// PP499'S BOUND IS CARRIED, not re-derived. The audio constants do not reserve the nalu-info skip
/// and the video ones do: 0x17 is 0x11 plus the video arm's 3 plus the skip's 3, while 0x12 is 0x11
/// plus the audio arm's 1 and 0x13 adds only v12's haptics byte. Adding the term for video too
/// would refuse valid packets between 24 and 26 bytes, which is why it is on one arm.
/// </summary>
public static class AvPacketParse
{
    /// <summary>The low nibble of the first byte, which says what a datagram is.</summary>
    public const int BaseTypeMask = TakionDispatch.BaseTypeMask;

    /// <summary>CHIAKI_TAKION_V9_AV_HEADER_SIZE_VIDEO.</summary>
    public const int V9VideoHeader = 0x17;

    /// <summary>CHIAKI_TAKION_V9_AV_HEADER_SIZE_AUDIO.</summary>
    public const int V9AudioHeader = 0x12;

    /// <summary>CHIAKI_TAKION_V12_AV_HEADER_SIZE_VIDEO, which is v9's.</summary>
    public const int V12VideoHeader = 0x17;

    /// <summary>CHIAKI_TAKION_V12_AV_HEADER_SIZE_AUDIO, one more than v9's for the haptics byte.</summary>
    public const int V12AudioHeader = 0x13;

    /// <summary>CHIAKI_TAKION_V7_AV_HEADER_SIZE_NALU_INFO_STRUCTS_ADD, PP499's term.</summary>
    public const int NaluInfoAdd = 0x3;

    /// <summary>What the haptics byte holds when the packet is haptics.</summary>
    public const byte HapticsMarker = 0x02;

    /// <summary>Where the fixed part of the header ends and the per-kind arm begins.</summary>
    public const int FixedHeader = 0x11;

    /// <summary>
    /// Parse an AV header, v9 or v12, or return null with the error the C would give.
    ///
    /// The buffer is not modified - unlike the shim's parse, which the C does in place - so this
    /// takes a ReadOnlySpan and hands the payload back as an offset, which is the port's own
    /// ownership rule for a datagram the caller already holds.
    /// </summary>
    public static AvPacket? Parse(
        bool v12, IKeyPositionLedger keyState, ReadOnlySpan<byte> buffer, out ChiakiError error)
    {
        ArgumentNullException.ThrowIfNull(keyState);

        if (buffer.Length < 1)
        {
            error = ChiakiError.BufTooSmall;
            return null;
        }

        int baseType = buffer[0] & BaseTypeMask;

        if (baseType != TakionDispatch.Video && baseType != TakionDispatch.Audio)
        {
            error = ChiakiError.InvalidData;
            return null;
        }

        bool isVideo = baseType == TakionDispatch.Video;
        bool usesNaluInfo = ((buffer[0] >> 4) & 1) != 0;

        // Everything below indexes from the byte after the type, the way the C rebases `av`.
        ReadOnlySpan<byte> av = buffer[1..];

        int headerSize = HeaderSize(v12, isVideo, usesNaluInfo);

        if (av.Length < headerSize + 1)
        {
            error = ChiakiError.BufTooSmall;
            return null;
        }

        ushort packetIndex = BinaryPrimitives.ReadUInt16BigEndian(av);
        ushort frameIndex = BinaryPrimitives.ReadUInt16BigEndian(av[2..]);
        uint dword2 = BinaryPrimitives.ReadUInt32BigEndian(av[4..]);

        ushort unitIndex, unitsTotal, unitsFec;

        if (isVideo)
        {
            unitIndex = (ushort)((dword2 >> 0x15) & 0x7ff);
            unitsTotal = (ushort)(((dword2 >> 0xa) & 0x7ff) + 1);
            unitsFec = (ushort)(dword2 & 0x3ff);
        }
        else
        {
            unitIndex = (ushort)((dword2 >> 0x18) & 0xff);
            unitsTotal = (ushort)(((dword2 >> 0x10) & 0xff) + 1);
            unitsFec = (ushort)(dword2 & 0xffff);
        }

        byte codec = av[8];
        uint keyPosLow = BinaryPrimitives.ReadUInt32BigEndian(av[0xd..]);
        ulong keyPos = keyState.RequestPos(keyPosLow, commit: true);

        int at = FixedHeader;
        byte adaptiveStreamIndex = 0;

        if (isVideo)
        {
            adaptiveStreamIndex = (byte)(av[at + 2] >> 5);
            at += 3;
        }
        else
        {
            at += 1;
        }

        if (usesNaluInfo)
            at += NaluInfoAdd;

        bool isHaptics = false;

        if (v12 && !isVideo)
        {
            isHaptics = av[at] == HapticsMarker;
            at += 1;
        }

        error = ChiakiError.Success;

        // The offset is into the CALLER'S buffer, so the type byte is counted back in.
        return new AvPacket(
            isVideo,
            packetIndex,
            frameIndex,
            unitIndex,
            unitsTotal,
            unitsFec,
            codec,
            adaptiveStreamIndex,
            keyPos,
            at + 1,
            av.Length - at,
            isHaptics);
    }

    /// <summary>
    /// The header size the bound is checked against, which is the whole of PP499's repair.
    ///
    /// The check happens once and the walk advances four times without checking again, so this has
    /// to cover the longest walk. It does for video already; the audio constants do not reserve the
    /// nalu-info skip, so the term is added on that arm and only that arm.
    /// </summary>
    public static int HeaderSize(bool v12, bool isVideo, bool usesNaluInfo)
    {
        int size = v12
            ? (isVideo ? V12VideoHeader : V12AudioHeader)
            : (isVideo ? V9VideoHeader : V9AudioHeader);

        if (usesNaluInfo && !isVideo)
            size += NaluInfoAdd;

        return size;
    }
}
