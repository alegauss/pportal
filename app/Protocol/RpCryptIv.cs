using System.Security.Cryptography;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP26: the IV every rpcrypt block is encrypted under, derived in managed code.
///
/// chiaki_rpcrypt_generate_iv is HMAC-SHA256 over the ambassador followed by the counter as eight
/// big-endian bytes, truncated to sixteen. Every encrypt and every decrypt calls it first, so it is
/// the smallest thing in the crypto that everything else depends on - and PP26's warning applies to
/// it exactly: a wrong byte here does not throw, it produces a key that fails to open a session
/// with no clue which step was wrong.
///
/// It needs no OpenSSL and no mbedTLS. HMACSHA256 is in the base class library, which is the whole
/// argument of Block F for this module: the dependency is not replaced, it stops being needed.
///
/// The key is chosen by target and there are three of them
/// -------------------------------------------------------
/// PS5 has one, PS4 firmware below 10 has another, and everything else - which is PS4 10 and both
/// Unknowns - takes the third. That default is not a fallback for unrecognised consoles so much as
/// the PS4 10 key doing double duty, and reproducing the switch rather than a lookup keeps it that
/// way. A port that mapped only the three named targets would answer nothing for Ps4Unknown, where
/// the C answers the PS4 key.
///
/// The counter is big-endian and the C spells it out byte by byte
/// --------------------------------------------------------------
/// Eight shifts from 0x38 down to 0. Written that way because it must not depend on the host's
/// byte order, and reproduced with BinaryPrimitives for the same reason rather than by casting.
/// </summary>
public static class RpCryptIv
{
    /// <summary>CHIAKI_RPCRYPT_KEY_SIZE, which is also the IV's length and the HMAC key's.</summary>
    public const int KeySize = 0x10;

    /// <summary>hmac_key_ps5.</summary>
    public static ReadOnlySpan<byte> Ps5Key =>
        [0x46, 0x46, 0x87, 0xb3, 0x49, 0xca, 0x8c, 0xe8, 0x59, 0xc5, 0x27, 0x0f, 0x5d, 0x7a, 0x69, 0xd6];

    /// <summary>hmac_key_ps4, which is also what every unnamed target gets.</summary>
    public static ReadOnlySpan<byte> Ps4Key =>
        [0x20, 0xd6, 0x6f, 0x59, 0x04, 0xea, 0x7c, 0x14, 0xe5, 0x57, 0xff, 0xc5, 0x2e, 0x48, 0x8a, 0xc8];

    /// <summary>hmac_key_ps4_pre10, for firmware 8 and 9.</summary>
    public static ReadOnlySpan<byte> Ps4Pre10Key =>
        [0xac, 0x07, 0x88, 0x83, 0xc8, 0x3a, 0x1f, 0xe8, 0x11, 0x46, 0x3a, 0xf3, 0x9e, 0xe3, 0xe3, 0x77];

    /// <summary>rpcrypt_hmac_key: the switch, defaults and all.</summary>
    public static ReadOnlySpan<byte> KeyFor(ChiakiTarget target) => target switch
    {
        ChiakiTarget.Ps5_1 => Ps5Key,
        ChiakiTarget.Ps4_8 or ChiakiTarget.Ps4_9 => Ps4Pre10Key,
        _ => Ps4Key,
    };

    /// <summary>
    /// The IV for one counter.
    /// </summary>
    /// <param name="target">Which key to use.</param>
    /// <param name="ambassador">The session's ambassador, sixteen bytes.</param>
    /// <param name="counter">The block counter, hashed big-endian.</param>
    public static byte[] Generate(ChiakiTarget target, ReadOnlySpan<byte> ambassador, ulong counter)
    {
        if (ambassador.Length != KeySize)
            throw new ArgumentException($"the ambassador is {KeySize} bytes", nameof(ambassador));

        Span<byte> message = stackalloc byte[KeySize + sizeof(ulong)];
        ambassador.CopyTo(message);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(message[KeySize..], counter);

        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(KeyFor(target), message, hash);

        // Truncated to sixteen. The other sixteen bytes of the SHA-256 are computed and discarded,
        // which is what the C does too - not an optimisation to make here.
        return hash[..KeySize].ToArray();
    }

    /// <summary>PP26: whether the three keys in the C are still these three.</summary>
    public static bool TheKeysAreStill(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("hmac_key_ps5[HMAC_KEY_SIZE] = { 0x46, 0x46, 0x87, 0xb3", StringComparison.Ordinal)
            && core.Contains("hmac_key_ps4[HMAC_KEY_SIZE] = { 0x20, 0xd6, 0x6f, 0x59", StringComparison.Ordinal)
            && core.Contains("hmac_key_ps4_pre10[HMAC_KEY_SIZE] = { 0xac, 0x07, 0x88, 0x83", StringComparison.Ordinal);
    }

    /// <summary>And whether the unnamed targets still fall through to the PS4 key.</summary>
    public static bool TheDefaultIsStillThePs4Key(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("default:\n\t\t\treturn hmac_key_ps4;", StringComparison.Ordinal)
            || core.Contains("default:\r\n\t\t\treturn hmac_key_ps4;", StringComparison.Ordinal);
    }
}
