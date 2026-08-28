using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>How a caller of the key position ledger sizes what it is about to send.</summary>
public enum KeyPositionSizing
{
    /// <summary>The caller's payload only, though the packet sent is longer. The two data sends.</summary>
    PayloadOnly,

    /// <summary>The whole packet, header included. The data ack.</summary>
    WholePacket,

    /// <summary>A constant, the packet being fixed-size. Congestion.</summary>
    FixedConstant,

    /// <summary>The payload plus one cipher block. Feedback state and feedback history.</summary>
    PayloadPlusBlock,
}

/// <summary>What asking for a key position produced.</summary>
/// <param name="Error">Success, or Overflow where the ledger would wrap.</param>
/// <param name="KeyPosition">Where this packet starts. Zero while there is no cipher.</param>
/// <param name="Next">Where the ledger stands afterwards.</param>
public readonly record struct KeyPositionGrant(ChiakiNg.Native.ChiakiError Error, ulong KeyPosition, ulong Next);

/// <summary>
/// PP495, under PP27: the key position ledger, and the three shapes in it that read as mistakes.
///
/// Every encrypted takion packet carries the position in the key stream it starts at, and one
/// twenty-line function hands those out. Porting it is not a translation problem - it is a problem
/// of NOT fixing it, because the console runs the same arithmetic and any improvement here is a
/// stream that decrypts to noise from the first message.
///
/// THE ROUNDING IS NOT ROUNDING. The C is `data_size += data_size % CHIAKI_GKCRYPT_BLOCK_SIZE`,
/// which adds the remainder rather than the distance to the next block: 20 becomes 24, where
/// rounding up would give 32. Only an exact multiple of 16 lands where the shape of the line
/// suggests. This is reproduced exactly, and <see cref="RoundedUp"/> exists beside it only to make
/// the difference assertable.
///
/// THE CALLERS DISAGREE ABOUT WHAT SIZE MEANS, IN FOUR WAYS. The two data sends pass the caller's
/// payload size while putting 26 more bytes on the wire; the data ack passes its whole packet;
/// congestion passes a constant; the two feedback sends pass their payload plus one block. Nothing
/// reconciles them because nothing may: each site's number is the one the console expects from it.
///
/// THE OVERFLOW GUARD IS TARGET-SPECIFIC. It tests SIZE_MAX against a uint64 counter, which is
/// correct here and would be wrong on a 32-bit build. That makes it the one place this port's
/// Windows-only non-goal is load-bearing arithmetic rather than an API choice.
///
/// Two plainer facts. With no cipher the position is zero and the ledger does not move, so every
/// packet before the handshake claims the same one. And the value handed back is the one BEFORE the
/// advance: a packet is stamped with where it begins, not where it ends.
/// </summary>
public static class TakionKeyPosition
{
    /// <summary>CHIAKI_GKCRYPT_BLOCK_SIZE.</summary>
    public const int BlockSize = 0x10;

    /// <summary>
    /// The C's own sizing, remainder and all.
    /// </summary>
    /// <remarks>
    /// Equal to <see cref="RoundedUp"/> only for exact multiples of the block. Everywhere else it
    /// is smaller, and that is the protocol.
    /// </remarks>
    public static ulong Sized(ulong dataSize) => dataSize + (dataSize % BlockSize);

    /// <summary>
    /// What rounding up to a block boundary would give, which is what the C looks like it does.
    ///
    /// Here to be compared against, never to be called by the model: a test that asserts these two
    /// differ is what stops the arithmetic being "corrected" by someone reading the line alone.
    /// </summary>
    public static ulong RoundedUp(ulong dataSize)
        => dataSize + ((BlockSize - (dataSize % BlockSize)) % BlockSize);

    /// <summary>
    /// Hands out the next key position for a packet of <paramref name="dataSize"/> bytes.
    /// </summary>
    /// <param name="current">Where the ledger stands - the C's `takion->key_pos_local`.</param>
    /// <param name="dataSize">Whatever the call site passes, which is four different things.</param>
    /// <param name="cipherPresent">
    /// The C's `takion->gkcrypt_local`. Absent, the position is zero and nothing advances - so this
    /// is not an error path but the state every packet before the handshake is sent in.
    /// </param>
    public static KeyPositionGrant Advance(ulong current, ulong dataSize, bool cipherPresent = true)
    {
        if (!cipherPresent)
            return new KeyPositionGrant(ChiakiNg.Native.ChiakiError.Success, 0, current);

        ulong sized = Sized(dataSize);

        // SIZE_MAX against a 64-bit counter, which is what this target's size_t is.
        if (ulong.MaxValue - current < sized)
            return new KeyPositionGrant(ChiakiNg.Native.ChiakiError.Overflow, 0, current);

        return new KeyPositionGrant(ChiakiNg.Native.ChiakiError.Success, current, current + sized);
    }

    /// <summary>
    /// How much longer chiaki_takion_send_message_data's packet is than the size it passes.
    ///
    /// One type byte, a sixteen-byte message header and the nine-byte data header. Named because
    /// the discrepancy is the claim, and a number written into a sentence is one that rots.
    /// </summary>
    public const int DataSendPacketOverhead = 1 + 0x10 + 9;

    /// <summary>
    /// And the continuation's, which is one byte less.
    ///
    /// The two variants differ by exactly the data-type byte: the first writes it as zero at offset
    /// 8 and puts the payload at 9, the continuation writes no type and puts the payload at 8. Both
    /// still pass only `buf_size`, so their discrepancies are 26 and 25 - two different wrong
    /// numbers, which is why this is a pair of constants and not one.
    /// </summary>
    public const int ContinuationPacketOverhead = 1 + 0x10 + 8;

    /// <summary>
    /// What each call site passes, by the shape of it.
    ///
    /// The two PayloadPlusBlock entries are the feedback HELPER and the mic packet, not the two
    /// public feedback sends - those both funnel through the helper. Worth naming, because reading
    /// the six line numbers and assuming the two feedback exports own two of them is wrong and
    /// reads perfectly well.
    /// </summary>
    public static IReadOnlyDictionary<string, KeyPositionSizing> CallSites { get; } =
        new Dictionary<string, KeyPositionSizing>(StringComparer.Ordinal)
        {
            ["chiaki_takion_send_message_data"] = KeyPositionSizing.PayloadOnly,
            ["chiaki_takion_send_message_data_cont"] = KeyPositionSizing.PayloadOnly,
            ["chiaki_takion_send_message_data_ack"] = KeyPositionSizing.WholePacket,
            ["chiaki_takion_send_congestion"] = KeyPositionSizing.FixedConstant,
            ["takion_send_feedback_packet"] = KeyPositionSizing.PayloadPlusBlock,
            ["chiaki_takion_send_mic_packet"] = KeyPositionSizing.PayloadPlusBlock,
        };

    /// <summary>
    /// The two sites that hold the cipher's own mutex ACROSS the call that takes it again.
    ///
    /// They lock gkcrypt_local_mutex, ask for a position, and encrypt without letting go - so the
    /// ledger and the encryption are one transaction. That only works because the mutex is created
    /// recursive where takion's other one is not, and a port that made it plain would deadlock on
    /// the first feedback packet.
    /// </summary>
    public static IReadOnlyList<string> ReentrantCallSites { get; } =
        ["takion_send_feedback_packet", "chiaki_takion_send_mic_packet"];
}

/// <summary>
/// PP495: the C's own spelling, because every claim above is "this line has not been improved".
/// </summary>
public static class TakionKeyPositionSource
{
    /// <summary>takion.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(TakionPostpone.RelativePath);

    /// <summary>gkcrypt.h, where the block size lives.</summary>
    public const string CryptHeaderRelativePath = @"lib\include\chiaki\gkcrypt.h";

    /// <summary>gkcrypt.h, or null outside a checkout.</summary>
    public static string? LocateCrypt() => SanitizerSource.LocateRelative(CryptHeaderRelativePath);

    /// <summary>The ledger.</summary>
    public static string? AdvanceBody(string source)
        => CFunction.Body(source, "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_crypt_advance_key_pos");

    /// <summary>CHIAKI_GKCRYPT_BLOCK_SIZE as the header defines it.</summary>
    public static long? BlockSizeIn(string cryptHeader) =>
        CDefine.Value(cryptHeader, "CHIAKI_GKCRYPT_BLOCK_SIZE");

    /// <summary>
    /// Whether the sizing still ADDS the remainder rather than rounding up.
    ///
    /// The single most repairable-looking line in this file, and the one repair that would break
    /// every session on its first encrypted packet.
    /// </summary>
    public static bool TheRemainderIsAdded(string advanceBody)
    {
        ArgumentNullException.ThrowIfNull(advanceBody);
        return advanceBody.Contains(
            "data_size += data_size % CHIAKI_GKCRYPT_BLOCK_SIZE;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the position handed back is still the one before the advance.
    ///
    /// Read as the pair of lines in order: the out parameter takes `cur`, and only then does the
    /// ledger move. Swapped, every packet would be stamped with where the next one starts.
    /// </summary>
    public static bool ThePositionIsTakenBeforeTheAdvance(string advanceBody)
    {
        ArgumentNullException.ThrowIfNull(advanceBody);

        string text = advanceBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int taken = text.IndexOf("*key_pos = cur;", StringComparison.Ordinal);
        int moved = text.IndexOf("takion->key_pos_local = cur + data_size;", StringComparison.Ordinal);

        return taken >= 0 && moved > taken;
    }

    /// <summary>Whether the no-cipher case still yields zero and leaves the ledger alone.</summary>
    public static bool WithNoCipherThePositionIsZero(string advanceBody)
    {
        ArgumentNullException.ThrowIfNull(advanceBody);

        string text = advanceBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int elseArm = text.IndexOf("\telse\n\t\t*key_pos = 0;", StringComparison.Ordinal);
        return elseArm >= 0;
    }

    /// <summary>Whether the overflow guard is still SIZE_MAX against the running position.</summary>
    public static bool TheOverflowGuardIsSizeMax(string advanceBody)
    {
        ArgumentNullException.ThrowIfNull(advanceBody);
        return advanceBody.Contains("if(SIZE_MAX - cur < data_size)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether each call site still passes the size shape the model records for it.
    ///
    /// The argument is read out of the source text at each site, so a caller that started passing
    /// its packet size instead of its payload size shows up here rather than in a stream that
    /// decrypts to noise.
    /// </summary>
    public static bool EveryCallSitePassesItsRecordedShape(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        foreach ((string function, KeyPositionSizing sizing) in TakionKeyPosition.CallSites)
        {
            if (CFunction.Body(source, function) is not { } body)
                return false;

            string? argument = ArgumentIn(body);
            if (argument is null)
                return false;

            bool matches = sizing switch
            {
                KeyPositionSizing.PayloadOnly => argument == "buf_size",
                KeyPositionSizing.WholePacket => argument == "sizeof(buf)",
                KeyPositionSizing.FixedConstant => argument == "CHIAKI_TAKION_CONGESTION_PACKET_SIZE",
                KeyPositionSizing.PayloadPlusBlock =>
                    argument == "payload_size + CHIAKI_GKCRYPT_BLOCK_SIZE",
                _ => false,
            };

            if (!matches)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the two re-entrant sites still hold the cipher's mutex across the advance, and the
    /// mutex is still created recursive.
    ///
    /// Both halves, because either alone is harmless and the pair is what works. A mutex made plain
    /// deadlocks on the first feedback packet; a site that stopped holding the lock would let the
    /// ledger move between the position it took and the bytes it encrypted with it.
    /// </summary>
    public static bool TheReentrantSitesHoldARecursiveMutex(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.Contains("chiaki_mutex_init(&takion->gkcrypt_local_mutex, true)", StringComparison.Ordinal))
            return false;

        foreach (string function in TakionKeyPosition.ReentrantCallSites)
        {
            if (CFunction.Body(source, function) is not { } body)
                return false;

            string text = body.Replace("\r\n", "\n", StringComparison.Ordinal);

            int locked = text.IndexOf(
                "chiaki_mutex_lock(&takion->gkcrypt_local_mutex)", StringComparison.Ordinal);
            int advance = text.IndexOf(
                "chiaki_takion_crypt_advance_key_pos(takion, ", StringComparison.Ordinal);

            if (locked < 0 || advance < locked)
                return false;
        }

        return true;
    }

    /// <summary>The second argument of the one advance call in a function body.</summary>
    private static string? ArgumentIn(string body)
    {
        const string call = "chiaki_takion_crypt_advance_key_pos(takion, ";

        int at = body.IndexOf(call, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int from = at + call.Length;
        int comma = body.IndexOf(",", from, StringComparison.Ordinal);

        return comma < 0 ? null : body[from..comma].Trim();
    }
}
