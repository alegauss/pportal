using System.Text;
using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP444, under PP29: the bytes the PIN exchange sends, laid out here instead of by regist.c.
///
/// PP29's remaining halves are the broadcast, the discovery reply, the wake packet and the PIN
/// exchange. Discovery has eighteen shim entry points; the PIN exchange has ONE, and
/// <see cref="RpCrypt.RegistRequestPayload"/> is a thin P/Invoke around it. Nothing managed laid out
/// those bytes.
///
/// THE FILL IS AN INPUT, NOT PADDING. regist.c memsets 0x1e0 bytes to 'A' and calls it "can be
/// random", which is true - the console receives those bytes and derives the same two key offsets
/// from them. But the offsets ARE derived from the fill: <c>buf[0x18D] &amp; 0x1F</c> and
/// <c>buf[0] &gt;&gt; 3</c> are 1 and 8 only because 'A' is 0x41. A port that filled differently
/// would change the crypto, so reproducing 0x41 is not cosmetic.
///
/// THE TWO TARGETS WRITE IT DIFFERENTLY, which is the thing a single model would have got wrong. A
/// PS4 before 10.0 gets sixteen CONTIGUOUS bytes at 0x11c. A PS4_10 or PS5 gets the same sixteen
/// SPLIT, and not in the obvious direction: the HIGH eight to 0xc7 and the LOW eight to 0x191.
/// Getting either wrong produces a payload of the right length that no console accepts.
///
/// WHAT STAYS C IS THE CRYPTO, and it is named rather than hidden. The PS4-pre10 path has
/// chiaki_shim_rpcrypt_aeropause_ps4_pre10; the PS5 path needs the general chiaki_rpcrypt_aeropause
/// and the shim exports no such thing - PP437's census lists every rpcrypt entry point and that is
/// not among them. So this is the LAYOUT, held against the C's own output byte for byte.
/// </summary>
public static partial class RegistRequestPayload
{
    /// <summary>Where the inner header starts, and how long the fill is. 0x1e0 is 480.</summary>
    public const int InnerHeaderOffset = 0x1e0;

    /// <summary>What regist.c fills the head with. 'A', which is 0x41.</summary>
    public const byte Fill = (byte)'A';

    /// <summary>The byte the first key offset is derived from.</summary>
    public const int Key0SourceOffset = 0x18D;

    /// <summary>Where the aeropause's HIGH eight bytes go.</summary>
    public const int AeropauseHighOffset = 0xc7;

    /// <summary>And where its LOW eight go.</summary>
    public const int AeropauseLowOffset = 0x191;

    /// <summary>How many bytes of the aeropause land at each offset on the split path.</summary>
    public const int AeropauseHalf = 8;

    /// <summary>The whole aeropause. CHIAKI_RPCRYPT_KEY_SIZE, which is sixteen.</summary>
    public const int AeropauseSize = 0x10;

    /// <summary>
    /// Where a PS4 before 10.0 gets its aeropause: sixteen contiguous bytes, no split.
    ///
    /// A different offset from either half of the newer path, and the reason the head is built by
    /// target rather than by one rule.
    /// </summary>
    public const int Pre10AeropauseOffset = 0x11c;

    /// <summary>The client type a PS4_10 or PS5 registration sends.</summary>
    public const string ClientType =
        "dabfa2ec873de5839bee8d3f4c0239c4282c07c25c6077a2931afcf0adc0d34f";

    /// <summary>And the one a PS4 before 10.0 sends.</summary>
    public const string ClientTypePs4Pre10 = "Windows";

    /// <summary>
    /// The first key offset, derived from the fill rather than chosen.
    ///
    /// <c>buf[0x18D] &amp; 0x1F</c>. With the 'A' fill this is 1, and it is 1 because 0x41 &amp; 0x1F
    /// is 1 - not because anything picked it.
    /// </summary>
    public static int Key0Offset(ReadOnlySpan<byte> head)
    {
        if (head.Length <= Key0SourceOffset)
            throw new ArgumentException(
                $"the head is {head.Length} bytes and the first key offset is read at "
                    + $"0x{Key0SourceOffset:x}", nameof(head));

        return head[Key0SourceOffset] & 0x1F;
    }

    /// <summary>The second, <c>buf[0] &gt;&gt; 3</c>. Eight, under the 'A' fill.</summary>
    public static int Key1Offset(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty)
            throw new ArgumentException("the head is empty and the second key offset is buf[0] >> 3",
                nameof(head));

        return head[0] >> 3;
    }

    /// <summary>
    /// The inner header a registration sends, in the form the target decides.
    ///
    /// TWO FORMS, and the account-id one is what a PS5 always uses: regist.c sets psn_online_id to
    /// NULL on that path before it chooses, so an online id passed for a PS5 is discarded rather than
    /// preferred. Reproduced here by ignoring it for the same targets.
    /// </summary>
    public static string InnerHeader(
        ChiakiTarget target, string? psnOnlineId, string? psnAccountIdBase64)
    {
        bool pre10 = target < ChiakiTarget.Ps4_10;

        // The PS5 path drops the online id, so only a pre-10 target can use that form.
        if (pre10 && psnOnlineId is { Length: > 0 })
            return $"Client-Type: Windows\r\nNp-Online-Id: {psnOnlineId}\r\n";

        if (psnAccountIdBase64 is { Length: > 0 })
        {
            string clientType = pre10 ? ClientTypePs4Pre10 : ClientType;
            return $"Client-Type: {clientType}\r\nNp-AccountId: {psnAccountIdBase64}\r\n";
        }

        // regist.c returns CHIAKI_ERR_INVALID_DATA with neither, which is a refusal and not an
        // empty header.
        throw new ArgumentException(
            "a registration needs an online id or an account id", nameof(psnAccountIdBase64));
    }

    /// <summary>
    /// The head: the fill, with the aeropause written into it the way this target wants it.
    ///
    /// The aeropause is the crypto's output and is passed in, because that is the part still living
    /// in C. What this owns is WHERE it goes, which differs by target and is easy to get wrong in a
    /// way that produces a well-formed payload nothing accepts.
    /// </summary>
    public static byte[] Head(ChiakiTarget target, ReadOnlySpan<byte> aeropause)
    {
        if (aeropause.Length < AeropauseSize)
        {
            throw new ArgumentException(
                $"the aeropause is {aeropause.Length} bytes and {AeropauseSize} are written",
                nameof(aeropause));
        }

        byte[] head = new byte[InnerHeaderOffset];
        head.AsSpan().Fill(Fill);

        if (target < ChiakiTarget.Ps4_10)
        {
            // Sixteen contiguous, at an offset neither half of the newer path uses.
            aeropause[..AeropauseSize].CopyTo(head.AsSpan(Pre10AeropauseOffset));
            return head;
        }

        // HIGH half first, at the LOWER offset. Backwards is the mistake this comment exists for.
        aeropause[AeropauseHalf..AeropauseSize].CopyTo(head.AsSpan(AeropauseHighOffset));
        aeropause[..AeropauseHalf].CopyTo(head.AsSpan(AeropauseLowOffset));

        return head;
    }

    /// <summary>
    /// The whole payload: the head, then the inner header ENCRYPTED IN PLACE at 0x1e0.
    ///
    /// The encryption is passed in as a function of the plaintext, so the caller decides whether that
    /// is the C's rpcrypt or something else - and the layout is testable without either.
    /// </summary>
    public static byte[] Format(
        ChiakiTarget target,
        ReadOnlySpan<byte> aeropause,
        string? psnOnlineId,
        string? psnAccountIdBase64,
        Func<byte[], byte[]> encrypt)
    {
        ArgumentNullException.ThrowIfNull(encrypt);

        byte[] head = Head(target, aeropause);
        byte[] inner = Encoding.ASCII.GetBytes(InnerHeader(target, psnOnlineId, psnAccountIdBase64));
        byte[] sealed_ = encrypt(inner);

        if (sealed_.Length != inner.Length)
        {
            throw new InvalidOperationException(
                $"the cipher returned {sealed_.Length} bytes for {inner.Length}: regist.c encrypts "
                    + "in place, so the payload's length is the plaintext's");
        }

        byte[] payload = new byte[InnerHeaderOffset + sealed_.Length];
        head.CopyTo(payload, 0);
        sealed_.CopyTo(payload, InnerHeaderOffset);

        return payload;
    }
}

/// <summary>
/// PP444: the layout rules, read out of regist.c rather than trusted here.
///
/// Every constant above is a number the C picked, and a number copied into managed source is a number
/// that was right once. These are the ones whose drift would be silent: a changed offset produces a
/// payload of the correct length that no console accepts, which looks like a network fault.
/// </summary>
public static partial class RegistRequestPayloadSource
{
    /// <summary>The file being translated.</summary>
    public const string RelativePath = @"lib\src\regist.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether regist.c still fills the head with 'A' up to the inner header offset.</summary>
    public static bool StillFillsTheHeadWithA(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return FillRegex().IsMatch(CCall.Code(text));
    }

    /// <summary>The inner header offset regist.c declares, or null where it declares none.</summary>
    public static int? InnerHeaderOffsetIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Match found = InnerOffsetRegex().Match(CCall.Code(text));

        return found.Success
            ? Convert.ToInt32(found.Groups["off"].Value, 16)
            : null;
    }

    /// <summary>
    /// Whether the two key offsets are still derived from the buffer, and from these bytes.
    ///
    /// The whole point of the fill being load-bearing: if either derivation changes, the port's
    /// reproduction of 0x41 stops meaning what it means.
    /// </summary>
    public static bool KeyOffsetsAreStillDerivedFromTheFill(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string code = CCall.Code(text);

        return Key0Regex().IsMatch(code) && Key1Regex().IsMatch(code);
    }

    /// <summary>Whether the aeropause halves still go to 0xc7 and 0x191, that way round.</summary>
    public static bool AeropauseStillSplitsTheSameWay(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string code = CCall.Code(text);

        return HighHalfRegex().IsMatch(code) && LowHalfRegex().IsMatch(code);
    }

    /// <summary>
    /// Whether a PS4 before 10.0 still gets its aeropause written contiguously at 0x11c.
    ///
    /// The half a single model would have missed: the pre-10 path calls the writer with the buffer
    /// itself as the destination, so there is no local array and no split.
    /// </summary>
    public static bool Pre10StillWritesContiguouslyAt011c(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Pre10Regex().IsMatch(CCall.Code(text));
    }

    // memset(buf, 'A', inner_header_off);
    [GeneratedRegex(@"memset\s*\(\s*buf\s*,\s*'A'\s*,\s*inner_header_off\s*\)")]
    private static partial Regex FillRegex();

    // static const size_t inner_header_off = 0x1e0;
    [GeneratedRegex(@"inner_header_off\s*=\s*0x(?<off>[0-9a-fA-F]+)")]
    private static partial Regex InnerOffsetRegex();

    [GeneratedRegex(@"key_0_off\s*=\s*buf\s*\[\s*0x18D\s*\]\s*&\s*0x1F", RegexOptions.IgnoreCase)]
    private static partial Regex Key0Regex();

    [GeneratedRegex(@"key_1_off\s*=\s*buf\s*\[\s*0\s*\]\s*>>\s*3")]
    private static partial Regex Key1Regex();

    // memcpy(buf + 0xc7, aeropause + 8, 8);
    [GeneratedRegex(@"memcpy\s*\(\s*buf\s*\+\s*0xc7\s*,\s*aeropause\s*\+\s*8\s*,\s*8\s*\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex HighHalfRegex();

    // memcpy(buf + 0x191, aeropause, 8);
    [GeneratedRegex(@"memcpy\s*\(\s*buf\s*\+\s*0x191\s*,\s*aeropause\s*,\s*8\s*\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex LowHalfRegex();

    // chiaki_rpcrypt_aeropause_ps4_pre10(buf + 0x11c, crypt->ambassador);
    [GeneratedRegex(@"aeropause_ps4_pre10\s*\(\s*buf\s*\+\s*0x11c\s*,", RegexOptions.IgnoreCase)]
    private static partial Regex Pre10Regex();
}
