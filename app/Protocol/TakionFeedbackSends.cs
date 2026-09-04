using System.Buffers.Binary;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>Where a packet's key position and MAC sit, for the sends the MAC table does not know.</summary>
/// <param name="HeadSize">Bytes before the encrypted payload.</param>
/// <param name="KeyPosOffset">Where the thirty-two-bit position is written.</param>
/// <param name="MacOffset">Where the four GMAC bytes go.</param>
public readonly record struct TakionCryptLayout(int HeadSize, int KeyPosOffset, int MacOffset);

/// <summary>
/// PP676: the three sends that do not go through chiaki_takion_packet_mac.
///
/// PP497's table knows five packet types and is right to know nothing of these. The feedback state,
/// the feedback history and the microphone packet each encrypt their own payload, write their own
/// position, compute their own GMAC and call the raw send - so a port that reached for the table
/// would put the MAC where nothing reads it.
///
/// THE SEQUENCE IS THE SAME THREE TIMES AND THE OFFSETS ARE NOT:
///
///   encrypt the payload at the key position PLUS ONE BLOCK - not at the position, which is what
///   the GMAC is computed at, and the block between them is the whole reason the ledger advances by
///   the payload plus sixteen;
///
///   write the position, truncated to thirty-two bits, at +4 for the feedback packets and +14 for
///   the microphone;
///
///   compute the GMAC over the WHOLE packet, position included, into +8 or +10.
///
/// THE ORDER MATTERS AND READS BACKWARDS. The position is written BEFORE the MAC is computed, so
/// the MAC covers it; a port that stamped first would produce a MAC over a zero field, and the
/// console would reject every feedback packet with nothing saying why.
///
/// ALL THREE HOLD THE CIPHER'S LOCK ACROSS THE WHOLE SEQUENCE, which works only because the C makes
/// that mutex recursive - the position advance takes it too. <see cref="TakionSendPath"/> takes the
/// lock rather than owning one for exactly this reason, and these do the same.
///
/// THE MICROPHONE'S HEAD IS NINETEEN BYTES, TWENTY ON A PS5. One byte, decided by the console's
/// generation, that shifts where the payload starts and therefore what gets encrypted.
/// </summary>
public static class TakionFeedbackSends
{
    /// <summary>TAKION_PACKET_TYPE_FEEDBACK_HISTORY.</summary>
    public const byte FeedbackHistoryType = 1;

    /// <summary>TAKION_PACKET_TYPE_FEEDBACK_STATE.</summary>
    public const byte FeedbackStateType = 6;

    /// <summary>CHIAKI_GKCRYPT_BLOCK_SIZE, which the payload's position is offset by.</summary>
    public const int BlockSize = 0x10;

    /// <summary>CHIAKI_GKCRYPT_GMAC_SIZE.</summary>
    public const int GmacSize = 4;

    /// <summary>CHIAKI_FEEDBACK_STATE_BUF_SIZE_V9.</summary>
    public const int FeedbackStateV9 = 0x19;

    /// <summary>CHIAKI_FEEDBACK_STATE_BUF_SIZE_V12.</summary>
    public const int FeedbackStateV12 = 0x1c;

    /// <summary>Both feedback packets: twelve bytes of head, the position at four, the MAC at eight.</summary>
    public static TakionCryptLayout Feedback { get; } = new(HeadSize: 0xc, KeyPosOffset: 4, MacOffset: 8);

    /// <summary>The microphone on a PS4: nineteen bytes, the position at fourteen, the MAC at ten.</summary>
    public static TakionCryptLayout Microphone { get; } = new(HeadSize: 19, KeyPosOffset: 14, MacOffset: 10);

    /// <summary>And on a PS5, one byte longer, everything else where it was.</summary>
    public static TakionCryptLayout MicrophonePs5 { get; } = Microphone with { HeadSize = 20 };

    /// <summary>The layout a microphone packet uses on a console of a generation.</summary>
    public static TakionCryptLayout MicrophoneFor(bool ps5) => ps5 ? MicrophonePs5 : Microphone;

    /// <summary>The head of a feedback state or history packet, before its payload.</summary>
    /// <param name="packet">At least <see cref="Feedback"/>'s head size.</param>
    /// <param name="type">One of the two feedback packet types.</param>
    /// <param name="seqNum">The sixteen-bit sequence number at +1.</param>
    public static void WriteFeedbackHead(Span<byte> packet, byte type, ushort seqNum)
    {
        if (packet.Length < Feedback.HeadSize)
            throw new ArgumentException($"a feedback head is {Feedback.HeadSize} bytes", nameof(packet));

        packet[0] = type;
        BinaryPrimitives.WriteUInt16BigEndian(packet[1..], seqNum);
        packet[3] = 0;

        // Both zeroed here and both written later: the position by the send, the MAC after it.
        BinaryPrimitives.WriteUInt32BigEndian(packet[Feedback.KeyPosOffset..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(packet[Feedback.MacOffset..], 0);
    }

    /// <summary>
    /// How far the ledger advances for a packet with a payload of this size.
    ///
    /// The payload plus one block, which is the gap between where the GMAC is taken and where the
    /// payload is encrypted. Stated as its own function because getting it wrong desynchronises the
    /// stream cipher rather than failing.
    /// </summary>
    public static int LedgerAdvanceFor(int payloadSize) => payloadSize + BlockSize;

    /// <summary>
    /// takion_send_feedback_packet, and the microphone send, which are one sequence with two layouts.
    /// </summary>
    /// <param name="packet">The whole packet, mutated in place.</param>
    /// <param name="layout">Where this kind puts its position and its MAC.</param>
    /// <param name="keyPos">The position the ledger advanced to.</param>
    /// <param name="encrypt">
    /// The key-stream XOR, given the position to encrypt AT and the payload. The position handed to
    /// it is <paramref name="keyPos"/> plus one block, which is the C's own offset.
    /// </param>
    /// <param name="gmac">The MAC over the whole packet at <paramref name="keyPos"/>.</param>
    /// <param name="wire">The raw send.</param>
    /// <param name="cipherLock">The takion's recursive gkcrypt_local lock, held throughout.</param>
    public static ChiakiError Send(
        Span<byte> packet,
        TakionCryptLayout layout,
        ulong keyPos,
        Action<ulong, Span<byte>> encrypt,
        Func<ulong, ReadOnlyMemory<byte>, byte[]> gmac,
        ITakionWire wire,
        object cipherLock)
    {
        ArgumentNullException.ThrowIfNull(encrypt);
        ArgumentNullException.ThrowIfNull(gmac);
        ArgumentNullException.ThrowIfNull(wire);
        ArgumentNullException.ThrowIfNull(cipherLock);

        if (packet.Length < layout.HeadSize)
            return ChiakiError.BufTooSmall;

        lock (cipherLock)
        {
            // The payload, at the position PLUS A BLOCK. The GMAC below is taken at the position
            // itself, and the block between them is why the ledger advances by payload plus sixteen.
            encrypt(keyPos + BlockSize, packet[layout.HeadSize..]);

            // Written BEFORE the MAC, so the MAC covers it. Truncated to thirty-two bits, which is
            // all the wire carries.
            BinaryPrimitives.WriteUInt32BigEndian(packet[layout.KeyPosOffset..], (uint)keyPos);

            byte[] computed = gmac(keyPos, packet.ToArray());
            computed.AsSpan(0, GmacSize).CopyTo(packet.Slice(layout.MacOffset, GmacSize));

            return wire.Send(packet);
        }
    }
}
