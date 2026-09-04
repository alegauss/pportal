using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Where a packet's MAC and key position live, for one base type.</summary>
/// <param name="MacOffset">Where the four-byte GMAC starts.</param>
/// <param name="KeyPosOffset">Where the four-byte key position starts.</param>
/// <param name="KeyPosIsBlanked">Whether the key position is hidden from the GMAC.</param>
public readonly record struct TakionMacLayout(int MacOffset, int KeyPosOffset, bool KeyPosIsBlanked);

/// <summary>
/// PP497, under PP27: the MAC gate - which is a rewrite of the packet, not a test on it.
///
/// PP490 modelled takion's dispatch and treated this as a verdict that had already happened. This
/// is the thing that happens.
///
/// IT IS NOT A PREDICATE. chiaki_takion_packet_mac takes the buffer, copies the MAC out if asked,
/// ZEROES that field, computes a GMAC over the whole packet, and writes the result back into the
/// same four bytes. The send path calls it to stamp a packet; the receive path calls it to
/// recompute one and compare. One function, mutating in place, in both directions.
///
/// IT BLANKS TWO FIELDS AND ONLY FOR TWO TYPES. The MAC field always. The key position as well -
/// saved, zeroed, restored - for control and congestion only. So video and audio carry their key
/// position INSIDE the MAC and the other two do not. Blank one field for everything and the client
/// authenticates its own video and nothing else; the symptom is a stream of MAC mismatches with
/// nothing else to go on.
///
/// THE SIX OFFSETS CARRY AN INVARIANT NEITHER SWITCH STATES. Read together, the key position begins
/// exactly where the MAC ends, for all three shapes. That is asserted here rather than six numbers
/// being transcribed, because six transcribed numbers are six chances to be wrong.
///
/// TWO JOINS. With no cipher the blanking still happens and the GMAC does not, so a handshake
/// packet leaves with four zeroes where its MAC will later be - and the C returns Success for it.
/// And congestion has offsets here but no arm in PP490's dispatch, which is right: this client
/// sends congestion packets and never receives one.
/// </summary>
public static class TakionPacketMac
{
    /// <summary>CHIAKI_GKCRYPT_GMAC_SIZE.</summary>
    public const int GmacSize = 4;

    /// <summary>The four bytes a key position occupies on the wire.</summary>
    public const int KeyPosSize = 4;

    /// <summary>TAKION_PACKET_TYPE_CONGESTION - the fourth type the offsets know.</summary>
    public const int Congestion = 5;

    /// <summary>
    /// The layout for a base type, or null where the C returns -1 from either switch.
    /// </summary>
    public static TakionMacLayout? LayoutFor(int baseType) => baseType switch
    {
        TakionDispatch.Control => new TakionMacLayout(5, 0x9, KeyPosIsBlanked: true),
        TakionDispatch.Video or TakionDispatch.Audio => new TakionMacLayout(0xa, 0xe, false),
        Congestion => new TakionMacLayout(7, 0xb, KeyPosIsBlanked: true),
        _ => null,
    };

    /// <summary>The base types either switch answers for. Four, not three.</summary>
    public static IReadOnlyList<int> TypesWithOffsets { get; } =
        [TakionDispatch.Control, TakionDispatch.Video, TakionDispatch.Audio, Congestion];

    /// <summary>
    /// The smallest packet this type can be checked in.
    ///
    /// The C tests both bounds separately; since the key position always follows the MAC, the key
    /// position's bound is the binding one, which is a thing worth having derived rather than
    /// written down.
    /// </summary>
    public static int MinimumSizeFor(TakionMacLayout layout)
        => Math.Max(layout.MacOffset + GmacSize, layout.KeyPosOffset + KeyPosSize);

    /// <summary>
    /// Reads the key position field, the way chiaki_takion_packet_read_key_pos does.
    /// </summary>
    /// <returns>
    /// BufTooSmall for an empty packet or one too short for the field, InvalidData for a type
    /// neither switch knows, Success otherwise.
    /// </returns>
    public static ChiakiError ReadKeyPosition(ReadOnlySpan<byte> packet, out uint keyPosLow)
    {
        keyPosLow = 0;

        if (packet.IsEmpty)
            return ChiakiError.BufTooSmall;

        if (LayoutFor(packet[0] & TakionDispatch.BaseTypeMask) is not { } layout)
            return ChiakiError.InvalidData;

        if (packet.Length < layout.KeyPosOffset + KeyPosSize)
            return ChiakiError.BufTooSmall;

        keyPosLow = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            packet.Slice(layout.KeyPosOffset, KeyPosSize));

        return ChiakiError.Success;
    }

    /// <summary>What one call to the MAC function did to the packet it was given.</summary>
    /// <param name="Error">Success, or why it refused.</param>
    /// <param name="MacBefore">The MAC as it arrived, where the caller asked for it.</param>
    /// <param name="MacAfter">What now sits in the MAC field.</param>
    /// <param name="KeyPosWasBlanked">Whether the key position was hidden while the GMAC ran.</param>
    public readonly record struct MacResult(
        ChiakiError Error, byte[]? MacBefore, byte[]? MacAfter, bool KeyPosWasBlanked);

    /// <summary>
    /// Runs the gate over a packet, rewriting it exactly as the C does.
    /// </summary>
    /// <param name="packet">Mutated in place. This is the point.</param>
    /// <param name="gmac">
    /// The cipher, as a function from the blanked packet to four bytes. Null is the no-cipher case:
    /// the blanking still happens and nothing is computed, which is how a handshake packet goes out.
    /// </param>
    /// <param name="wantMacBefore">Whether the caller passed mac_old_out.</param>
    /// <param name="wantMacAfter">
    /// Whether the caller passed mac_out. PP675: this used to be unconditional, and the C's
    /// parameter is optional exactly as mac_old_out is - chiaki_takion_send passes NULL for it. The
    /// four bytes were being copied out on every send for a caller that never read them, which the
    /// send path's zero-allocation budget is what noticed.
    /// </param>
    public static MacResult Apply(
        Span<byte> packet, Func<ReadOnlyMemory<byte>, byte[]>? gmac,
        bool wantMacBefore = true, bool wantMacAfter = true)
    {
        if (packet.IsEmpty)
            return new MacResult(ChiakiError.BufTooSmall, null, null, false);

        if (LayoutFor(packet[0] & TakionDispatch.BaseTypeMask) is not { } layout)
            return new MacResult(ChiakiError.InvalidData, null, null, false);

        if (packet.Length < MinimumSizeFor(layout))
            return new MacResult(ChiakiError.BufTooSmall, null, null, false);

        byte[]? before = wantMacBefore
            ? packet.Slice(layout.MacOffset, GmacSize).ToArray()
            : null;

        // Unconditional, and ahead of the cipher test - so a call with no cipher still destroys
        // whatever was in the field.
        packet.Slice(layout.MacOffset, GmacSize).Clear();

        var blanked = false;
        if (gmac is not null)
        {
            byte[]? savedKeyPos = null;
            if (layout.KeyPosIsBlanked)
            {
                savedKeyPos = packet.Slice(layout.KeyPosOffset, KeyPosSize).ToArray();
                packet.Slice(layout.KeyPosOffset, KeyPosSize).Clear();
                blanked = true;
            }

            byte[] computed = gmac(packet.ToArray());
            computed.AsSpan(0, GmacSize).CopyTo(packet.Slice(layout.MacOffset, GmacSize));

            if (savedKeyPos is not null)
                savedKeyPos.CopyTo(packet.Slice(layout.KeyPosOffset, KeyPosSize));
        }

        return new MacResult(
            ChiakiError.Success,
            before,
            wantMacAfter ? packet.Slice(layout.MacOffset, GmacSize).ToArray() : null,
            blanked);
    }
}

/// <summary>
/// PP497: the C's own offsets and blanking, so the six numbers are read rather than copied.
/// </summary>
public static class TakionPacketMacSource
{
    /// <summary>takion.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(TakionPostpone.RelativePath);

    /// <summary>gkcrypt.h, where the GMAC size lives.</summary>
    public static string? LocateCrypt() => TakionKeyPositionSource.LocateCrypt();

    /// <summary>CHIAKI_GKCRYPT_GMAC_SIZE as the header defines it.</summary>
    public static long? GmacSizeIn(string cryptHeader)
        => CDefine.Value(cryptHeader, "CHIAKI_GKCRYPT_GMAC_SIZE");

    /// <summary>The MAC offset switch.</summary>
    public static string? MacOffsetBody(string source)
        => CFunction.Body(source, "int takion_packet_type_mac_offset");

    /// <summary>The key position offset switch.</summary>
    public static string? KeyPosOffsetBody(string source)
        => CFunction.Body(source, "int takion_packet_type_key_pos_offset");

    /// <summary>The gate.</summary>
    public static string? MacBody(string source)
        => CFunction.Body(source, "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_packet_mac");

    /// <summary>
    /// The offset a switch returns for one type, read out of the C rather than transcribed.
    /// </summary>
    public static int? OffsetFor(string switchBody, string typeName)
    {
        ArgumentNullException.ThrowIfNull(switchBody);
        ArgumentException.ThrowIfNullOrEmpty(typeName);

        string text = switchBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int label = text.IndexOf($"case {typeName}:", StringComparison.Ordinal);
        if (label < 0)
            return null;

        int returns = text.IndexOf("return ", label, StringComparison.Ordinal);
        if (returns < 0)
            return null;

        int semicolon = text.IndexOf(';', returns);
        if (semicolon < 0)
            return null;

        string literal = text[(returns + "return ".Length)..semicolon].Trim();

        return literal.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(literal[2..], 16)
            : int.TryParse(literal, out int value) ? value : null;
    }

    /// <summary>
    /// Whether the MAC field is still blanked BEFORE the cipher is tested.
    ///
    /// The line that makes this a rewrite rather than a check: with no cipher the memset still
    /// runs, and the function still returns success.
    /// </summary>
    public static bool TheMacIsBlankedBeforeTheCipherIsTested(string macBody)
    {
        ArgumentNullException.ThrowIfNull(macBody);

        string text = macBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int blank = text.IndexOf(
            "memset(buf + mac_offset, 0, CHIAKI_GKCRYPT_GMAC_SIZE);", StringComparison.Ordinal);
        int cipher = text.IndexOf("if(crypt)", StringComparison.Ordinal);

        return blank >= 0 && cipher > blank;
    }

    /// <summary>
    /// Whether the key position is still blanked for control and congestion ONLY, and restored.
    ///
    /// Three parts: the type test, the memset, and the copy back. Losing the restore would send a
    /// control packet whose key position is four zeroes, which the console cannot decrypt and which
    /// nothing on this side would notice.
    /// </summary>
    public static bool TheKeyPosIsBlankedForTwoTypesAndRestored(string macBody)
    {
        ArgumentNullException.ThrowIfNull(macBody);

        string text = macBody.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string test =
            "if(base_type == TAKION_PACKET_TYPE_CONTROL || base_type == TAKION_PACKET_TYPE_CONGESTION)";

        int first = text.IndexOf(test, StringComparison.Ordinal);
        if (first < 0)
            return false;

        int second = text.IndexOf(test, first + test.Length, StringComparison.Ordinal);

        return second > first
            && text.Contains("memset(buf + key_pos_offset, 0, sizeof(uint32_t));", StringComparison.Ordinal)
            && text.Contains("memcpy(buf + key_pos_offset, key_pos_tmp, sizeof(uint32_t));", StringComparison.Ordinal);
    }
}
