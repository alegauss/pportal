using System.Buffers.Binary;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// One v7 AV header, with the payload named by where it sits.
/// </summary>
/// <param name="KeyPos">
/// A <c>uint</c> and not a <c>ulong</c>, which is the type saying the third difference out loud: v7
/// reads thirty-two raw bits off the wire and hands them over. There is no <see cref="KeyState"/>
/// behind it, no wrap, and no ledger to advance - the C's own parameter for one is declared and
/// never read. <see cref="AvPacket.KeyPos"/> is 64 bits because v9's is expanded; this one is not.
/// </param>
/// <param name="WordAt0x18">
/// Carried here and not on <see cref="AvPacket"/>, because <see cref="AvPacketV7.FormatHeader"/>
/// writes it and a round trip that could not read it back would be checking four fields out of
/// five.
/// </param>
public readonly record struct V7AvHeader(
    bool IsVideo,
    bool UsesNaluInfoStructs,
    ushort PacketIndex,
    ushort FrameIndex,
    ushort UnitIndex,
    ushort UnitsInFrameTotal,
    ushort UnitsInFrameFec,
    byte Codec,
    ushort WordAt0x18,
    byte AdaptiveStreamIndex,
    uint KeyPos,
    int DataOffset,
    int DataSize);

/// <summary>
/// PP679, under PP27: version seven's AV header - the parse and the file's only formatter.
///
/// A BODY OF ITS OWN, THE WAY THE C KEEPS IT. takion.c carries three AV header parsers and the
/// version chosen at connect picks one. v9 and v12 are one body with a flag, which is
/// <see cref="AvPacketParse"/>; v7 is separate there and separate here, because folding it in as a
/// third mode would thread four conditionals through a function that today has one.
///
/// WHAT DIFFERS, and the count is four rather than the three that were visible from the outside:
///
/// 1. THE BOUND counts the nalu-info add for video as well as audio - v9's video constant already
///    reserves it, so PP499's repair had to add the term on one arm only and this one never did.
///    It also reserves no payload byte: v9 refuses a packet whose header exactly fills it, and v7
///    accepts that packet with a zero-length payload.
///
/// 2. THE PACKED WORD always takes the video layout whatever the base type. So an AUDIO packet's
///    unit index, total and FEC count come out of different bits here than they do from v9, which
///    is not a bound or a skip but three fields reading differently off the same four bytes.
///
/// 3. THE KEY POSITION is thirty-two raw bits with no key state behind it, which is why
///    <see cref="V7AvHeader.KeyPos"/> is a <c>uint</c>.
///
/// 4. THE AUDIO ARM IS ABSENT. v9 steps one byte past the fixed header for audio before the
///    payload; v7 does not, so the same audio datagram's payload starts one byte earlier here.
///    That one is a consequence of the header sizes and was not visible until both walks were
///    written down side by side.
///
/// THE FORMATTER STAYS WITH THE PARSE, which is the decision PP679 was filed to make. Its two
/// callers are senkusha.c's - the ping and the MTU probe - and senkusha.c is not one of the three
/// files PP27's fourth criterion says leave the build, so the C's copy stands until senkusha is
/// ported. Moving it into senkusha.c would be a local patch to the vendored C, which a non-goal
/// refuses; deleting it would strand two callers. So this is a second implementation held to the
/// first byte for byte, and the C's own goes when its callers do.
/// </summary>
public static class AvPacketV7
{
    /// <summary>CHIAKI_TAKION_V7_AV_HEADER_SIZE_BASE.</summary>
    public const int HeaderBase = 0x12;

    /// <summary>CHIAKI_TAKION_V7_AV_HEADER_SIZE_VIDEO_ADD.</summary>
    public const int VideoAdd = 0x3;

    /// <summary>CHIAKI_TAKION_V7_AV_HEADER_SIZE_NALU_INFO_STRUCTS_ADD.</summary>
    public const int NaluInfoAdd = 0x3;

    /// <summary>The bit of byte zero that says the header carries nalu-info structs.</summary>
    public const byte NaluInfoFlag = 0x10;

    /// <summary>
    /// The header this version needs, which is the whole of the first difference.
    ///
    /// The nalu-info term is unconditional - both arms pay it. <see cref="AvPacketParse.HeaderSize"/>
    /// adds it for audio alone, because v9's video constant already contains it.
    /// </summary>
    public static int HeaderSize(bool isVideo, bool usesNaluInfoStructs)
    {
        int size = HeaderBase;

        if (isVideo)
            size += VideoAdd;

        if (usesNaluInfoStructs)
            size += NaluInfoAdd;

        return size;
    }

    /// <summary>
    /// Parse a v7 AV header, or return null with the error the C would give.
    ///
    /// A ReadOnlySpan, and the payload comes back as an offset into the caller's buffer: the same
    /// ownership rule <see cref="AvPacketParse"/> uses, and the C parses in place only because it
    /// hands back a pointer.
    /// </summary>
    public static V7AvHeader? Parse(ReadOnlySpan<byte> buffer, out ChiakiError error)
    {
        if (buffer.Length < 1)
        {
            error = ChiakiError.BufTooSmall;
            return null;
        }

        int baseType = buffer[0] & TakionDispatch.BaseTypeMask;

        if (baseType != TakionDispatch.Video && baseType != TakionDispatch.Audio)
        {
            error = ChiakiError.InvalidData;
            return null;
        }

        bool isVideo = baseType == TakionDispatch.Video;
        bool usesNaluInfo = ((buffer[0] >> 4) & 1) != 0;

        int headerSize = HeaderSize(isVideo, usesNaluInfo);

        // Strictly less, and against the WHOLE datagram rather than against what follows byte zero.
        // Both halves are the C's: a packet of exactly headerSize is accepted, with nothing after it.
        if (buffer.Length < headerSize)
        {
            error = ChiakiError.BufTooSmall;
            return null;
        }

        ushort packetIndex = BinaryPrimitives.ReadUInt16BigEndian(buffer[1..]);
        ushort frameIndex = BinaryPrimitives.ReadUInt16BigEndian(buffer[3..]);
        uint dword2 = BinaryPrimitives.ReadUInt32BigEndian(buffer[5..]);

        // The video layout, whatever the base type. This is the second difference and the one with
        // no marker at the call site: an audio packet parses without complaint and its three counts
        // are read out of the wrong bits, which is a wrong answer rather than a refusal.
        ushort unitIndex = (ushort)((dword2 >> 0x15) & 0x7ff);
        ushort unitsTotal = (ushort)(((dword2 >> 0xa) & 0x7ff) + 1);
        ushort unitsFec = (ushort)(dword2 & 0x3ff);

        byte codec = buffer[9];

        // buf + 0xa is four bytes the C names unknown and steps over.
        uint keyPos = BinaryPrimitives.ReadUInt32BigEndian(buffer[0xe..]);

        int at = HeaderBase;
        ushort wordAt0x18 = 0;
        byte adaptiveStreamIndex = 0;

        if (isVideo)
        {
            wordAt0x18 = BinaryPrimitives.ReadUInt16BigEndian(buffer[at..]);
            adaptiveStreamIndex = (byte)(buffer[at + 2] >> 5);
            at += VideoAdd;
        }

        // And no audio arm at all, which is the fourth difference.

        if (usesNaluInfo)
            at += NaluInfoAdd;

        error = ChiakiError.Success;

        return new V7AvHeader(
            isVideo,
            usesNaluInfo,
            packetIndex,
            frameIndex,
            unitIndex,
            unitsTotal,
            unitsFec,
            codec,
            wordAt0x18,
            adaptiveStreamIndex,
            keyPos,
            at,
            buffer.Length - at);
    }

    /// <summary>
    /// Write a v7 AV header into <paramref name="buffer"/>, as senkusha's two probes do.
    ///
    /// <paramref name="headerSize"/> is set BEFORE the bound is checked and is written on the
    /// refusal too, because that is what the C does and what senkusha.c's assertion reads.
    ///
    /// Nothing past the header is touched. The C writes ten fields and stops, so a buffer's tail is
    /// whatever the caller left there - senkusha memsets first and then puts its tag after the
    /// header, which only works because this does not clear behind itself.
    /// </summary>
    public static ChiakiError FormatHeader(Span<byte> buffer, in V7AvHeader header, out int headerSize)
    {
        headerSize = HeaderSize(header.IsVideo, header.UsesNaluInfoStructs);

        if (headerSize > buffer.Length)
            return ChiakiError.BufTooSmall;

        buffer[0] = (byte)(header.IsVideo ? TakionDispatch.Video : TakionDispatch.Audio);

        if (header.UsesNaluInfoStructs)
            buffer[0] |= NaluInfoFlag;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[1..], header.PacketIndex);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[3..], header.FrameIndex);

        BinaryPrimitives.WriteUInt32BigEndian(buffer[5..], PackedWord(header));

        buffer[9] = header.Codec;

        BinaryPrimitives.WriteUInt32BigEndian(buffer[0xa..], 0);

        // THE ONE FIELD THE C DOES NOT BYTE-SWAP. Every other multi-byte write here goes through
        // htons or htonl; this one is a plain store, so the key position leaves in host order and
        // comes back through the parse's ntohl reversed. Reproduced rather than corrected: the
        // console is what reads these bytes, and a port that "fixed" it would send a header the C
        // never sent. Both of senkusha's call sites zero the packet, so the asymmetry has never
        // reached a wire - which is why nothing has ever noticed it.
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[0xe..], header.KeyPos);

        int at = HeaderBase;

        if (header.IsVideo)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[at..], header.WordAt0x18);
            buffer[at + 2] = (byte)(header.AdaptiveStreamIndex << 5);
            at += VideoAdd;
        }

        if (header.UsesNaluInfoStructs)
            buffer.Slice(at, NaluInfoAdd).Clear();

        return ChiakiError.Success;
    }

    /// <summary>
    /// The three counts packed into one big-endian word, which the parse above reads back.
    ///
    /// Unchecked and in 32 bits on purpose. The C computes this in <c>int</c> and hands it to
    /// <c>htonl</c>, so a unit index above 0x7ff has its top bits shifted off the end - and a
    /// managed version that widened to 64 bits first would keep them and write a different word.
    /// </summary>
    private static uint PackedWord(in V7AvHeader header)
        => unchecked(
            (uint)(header.UnitsInFrameFec & 0x3ff)
            | ((uint)((header.UnitsInFrameTotal - 1) & 0x7ff) << 0xa)
            | ((uint)(header.UnitIndex & 0xffff) << 0x15));
}

/// <summary>
/// PP679: where the C keeps these two, which is the half of the ownership question a test can hold.
///
/// The decision above says the formatter stays with the parse and the C's copy goes when senkusha
/// does. That is a claim about the tree as it is now - the formatter is defined in takion.c, called
/// twice from senkusha.c and nowhere else - and a claim that will stop being true, quietly, the
/// first time somebody adds a third caller or moves the body. So it is read rather than believed.
///
/// Every check compacts the source first, so a comment naming a function is not a call to it.
/// </summary>
public static class AvPacketV7Source
{
    /// <summary>Where both bodies live.</summary>
    public const string TakionRelativePath = @"lib\src\takion.c";

    /// <summary>The formatter's only caller.</summary>
    public const string SenkushaRelativePath = @"lib\src\senkusha.c";

    /// <summary>The library's C, which is the whole of what is swept for a third caller.</summary>
    public const string LibSourceRelativePath = @"lib\src";

    /// <summary>The formatter.</summary>
    public const string FormatterName = "chiaki_takion_v7_av_packet_format_header";

    /// <summary>The parse.</summary>
    public const string ParseName = "chiaki_takion_v7_av_packet_parse";

    /// <summary>How many times senkusha.c formats a header: the ping and the MTU probe.</summary>
    public const int SenkushaCallSites = 2;

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? LocateTakion() => SanitizerSource.LocateRelative(TakionRelativePath);

    /// <summary>senkusha.c, or null outside a checkout.</summary>
    public static string? LocateSenkusha() => SanitizerSource.LocateRelative(SenkushaRelativePath);

    /// <summary>lib/src, or null outside a checkout.</summary>
    public static string? LocateLibSource() => SanitizerSource.LocateDirectory(LibSourceRelativePath);

    /// <summary>
    /// How many times a file CALLS <paramref name="name"/>, with its definition discounted.
    ///
    /// The subtraction is the whole of it. <see cref="CCall.Compact"/> keeps the space between two
    /// identifiers, so a definition still reads as <c>ChiakiErrorCode name(</c> with a space in
    /// front of the name - which is a call start by every test <see cref="CCall.Count"/> applies.
    /// Counting takion.c without this says the receive path formats a header, which it does not.
    /// </summary>
    public static int Calls(string source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(name);

        string code = CCall.Code(source);

        return CCall.Count(code, name + "(")
            - CCall.Count(code, "CHIAKI_EXPORT ChiakiErrorCode " + name + "(");
    }

    /// <summary>
    /// The files under lib/src whose CODE names <paramref name="name"/>, by file name.
    ///
    /// Names rather than calls, deliberately: a third consumer is news whether it calls the
    /// function or only takes its address, and this is the sweep that says the two files above are
    /// the whole story. Comments are stripped first, because takion.c explains the v7 bound inside
    /// the v9 parse and a prose mention is not a consumer.
    /// </summary>
    public static IReadOnlyList<string> FilesNaming(string libSourceDirectory, string name)
    {
        ArgumentNullException.ThrowIfNull(libSourceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return
        [
            .. Directory.EnumerateFiles(libSourceDirectory, "*.c", SearchOption.AllDirectories)
                .Where(path => CCall.Code(File.ReadAllText(path))
                    .Contains(name, StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Whether the v7 parse is still reached only by the version chosen at connect.
    ///
    /// takion.c assigns it to a function pointer and never calls it directly, which is why porting
    /// it moves no call site: what picks it is a version number, and that is the switch a managed
    /// transport reproduces rather than the call.
    /// </summary>
    public static bool TheParseIsChosenByVersion(string takionSource)
    {
        ArgumentNullException.ThrowIfNull(takionSource);

        return CCall.Mark(
                CCall.Compact(CCall.Code(takionSource)),
                $"takion->av_packet_parse = {ParseName};") >= 0
            && Calls(takionSource, ParseName) == 0;
    }

    /// <summary>
    /// Whether senkusha still formats audio headers with no nalu-info, which fixes their size.
    ///
    /// Its assertion is <c>header_size == MTU_AV_PACKET_ADD</c>, and that macro is the BASE
    /// constant alone. The assertion only holds because both call sites zero the packet and then
    /// set is_video false, so neither add is paid - and a port that sent video-sized probes would
    /// pass every test of the formatter and change what senkusha measures.
    /// </summary>
    public static bool SenkushaFormatsAudioHeadersOnly(string senkushaSource)
    {
        ArgumentNullException.ThrowIfNull(senkushaSource);

        string compact = CCall.Compact(CCall.Code(senkushaSource));

        return CCall.Count(compact, "av_packet.is_video = false;") == SenkushaCallSites
            && CCall.Mark(
                compact,
                "#define MTU_AV_PACKET_ADD CHIAKI_TAKION_V7_AV_HEADER_SIZE_BASE") >= 0;
    }
}
