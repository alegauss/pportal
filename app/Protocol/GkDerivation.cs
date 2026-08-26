using System.Security.Cryptography;

using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP26: what a session's key stream and its GMAC keys are derived FROM.
///
/// <see cref="GkKeyStream"/> is the stream; this is where its key and IV come from, and where the
/// GMAC key that authenticates each packet comes from after that. Two hashes, no cipher, and both
/// of them frame their input in a way worth writing down rather than inferring.
///
/// The key and IV are one HMAC, split
/// -----------------------------------
/// HMAC-SHA256 keyed on the ECDH secret over a five-part message - a 1, the stream index, a 0, the
/// handshake key, then a 1 and a 0 - producing thirty-two bytes that are the key followed by the
/// IV. The framing bytes are not padding: the index is what makes the two directions of a session
/// derive different keys from the same secret, and a port that dropped the trailing pair would
/// derive a consistent, wrong pair for both.
///
/// The GMAC key is a hash folded in half
/// --------------------------------------
/// SHA-256 over the key base followed by the IV advanced by the refresh index, then the first
/// sixteen bytes XORed with the last sixteen. The fold is the part that looks like a mistake and is
/// not - taking the first sixteen alone would compile, run, and authenticate nothing correctly.
///
/// The advance reuses <see cref="GkKeyStream.CounterAdd"/>, and so inherits the upward carry - the
/// same counter, the same surprise, in a second place.
/// </summary>
public static class GkDerivation
{
    /// <summary>CHIAKI_GKCRYPT_BLOCK_SIZE, which is the key's length and the IV's.</summary>
    public const int BlockSize = 0x10;

    /// <summary>CHIAKI_HANDSHAKE_KEY_SIZE.</summary>
    public const int HandshakeKeySize = 0x10;

    /// <summary>CHIAKI_ECDH_SECRET_SIZE.</summary>
    public const int EcdhSecretSize = 32;

    /// <summary>How far the IV advances per GMAC key refresh.</summary>
    public const ulong GmacKeyRefreshIvOffset = 44910;

    /// <summary>And how far the key position advances between refreshes.</summary>
    public const ulong GmacKeyRefreshKeyPos = 45000;

    /// <summary>
    /// gkcrypt_gen_key_iv: the stream's key and IV for one direction of a session.
    /// </summary>
    /// <param name="index">Which stream. Different indices derive different keys from one secret.</param>
    public static (byte[] KeyBase, byte[] Iv) KeyAndIv(
        byte index, ReadOnlySpan<byte> handshakeKey, ReadOnlySpan<byte> ecdhSecret)
    {
        if (handshakeKey.Length != HandshakeKeySize)
            throw new ArgumentException($"the handshake key is {HandshakeKeySize} bytes", nameof(handshakeKey));
        if (ecdhSecret.Length != EcdhSecretSize)
            throw new ArgumentException($"the secret is {EcdhSecretSize} bytes", nameof(ecdhSecret));

        Span<byte> message = stackalloc byte[3 + HandshakeKeySize + 2];
        message[0] = 1;
        message[1] = index;
        message[2] = 0;
        handshakeKey.CopyTo(message[3..]);
        message[3 + HandshakeKeySize] = 1;
        message[3 + HandshakeKeySize + 1] = 0;

        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(ecdhSecret, message, hash);

        return (hash[..BlockSize].ToArray(), hash[BlockSize..].ToArray());
    }

    /// <summary>
    /// chiaki_gkcrypt_gen_gmac_key: the key one refresh window's packets are authenticated under.
    /// </summary>
    /// <param name="index">The refresh index. Zero is the key a session starts with.</param>
    public static byte[] GmacKey(ulong index, ReadOnlySpan<byte> keyBase, ReadOnlySpan<byte> iv)
    {
        if (keyBase.Length != BlockSize)
            throw new ArgumentException($"the key base is {BlockSize} bytes", nameof(keyBase));
        if (iv.Length != BlockSize)
            throw new ArgumentException($"the IV is {BlockSize} bytes", nameof(iv));

        Span<byte> message = stackalloc byte[BlockSize * 2];
        keyBase.CopyTo(message);
        GkKeyStream.CounterAdd(iv, index * GmacKeyRefreshIvOffset).CopyTo(message[BlockSize..]);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(message, hash);

        // The fold. Half the hash is XORed into the other half, and the first half is the key.
        var key = new byte[BlockSize];
        for (int i = 0; i < BlockSize; i++)
            key[i] = (byte)(hash[i] ^ hash[BlockSize + i]);

        return key;
    }

    /// <summary>PP26: whether the C still frames the derivation message this way.</summary>
    public static bool TheFramingIsStill(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("data[0] = 1;", StringComparison.Ordinal)
            && core.Contains("data[1] = index;", StringComparison.Ordinal)
            && core.Contains("data[3 + CHIAKI_HANDSHAKE_KEY_SIZE + 0] = 1;", StringComparison.Ordinal);
    }

    /// <summary>And whether the GMAC key is still the folded hash.</summary>
    public static bool TheGmacKeyIsStillFolded(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return CCall.Happens(core, "xor_bytes(md, md + 0x10, 0x10)");
    }
}
