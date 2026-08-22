using System.Security.Cryptography;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP26: the cipher every rpcrypt payload goes through - AES-128 in CFB128, keyed on bright.
///
/// chiaki_rpcrypt_crypt is one call to mbedtls_aes_crypt_cfb128 (or EVP_aes_128_cfb128 on the
/// OpenSSL build), with the IV <see cref="RpCryptIv"/> derives for the block counter. Encrypt and
/// decrypt are the same function with a flag, which is a property of CFB rather than a shortcut the
/// C took.
///
/// Why the feedback is written out here
/// ------------------------------------
/// .NET has CipherMode.CFB, and it cannot be used for this. It is block-oriented: with
/// PaddingMode.None the input must be a whole number of blocks, and the C's is a STREAM - mbedtls
/// carries an iv_off across calls and encrypts payloads of any length, most of them not a multiple
/// of sixteen. So the mode is spelled out: encrypt the IV to get a keystream block, XOR as many
/// bytes as remain, and feed the CIPHERTEXT forward.
///
/// The ciphertext is the feedback in BOTH directions
/// -------------------------------------------------
/// That is the half of CFB a port gets wrong. Encrypting, the output is the feedback; decrypting,
/// the INPUT is - and both of those are the ciphertext. Written as one loop with a flag rather than
/// two loops, because two loops is where the second one ends up feeding the plaintext forward and
/// decrypts correctly for exactly one block.
/// </summary>
public static class RpCryptCipher
{
    /// <summary>The block size, which is also the IV's length and the key's.</summary>
    public const int BlockSize = 0x10;

    /// <summary>Encrypts in place of the C's chiaki_rpcrypt_encrypt.</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> bright, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> plain)
        => Crypt(bright, iv, plain, encrypting: true);

    /// <summary>And decrypts, which is the same walk with the feedback taken from the input.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> bright, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> cipher)
        => Crypt(bright, iv, cipher, encrypting: false);

    private static byte[] Crypt(
        ReadOnlySpan<byte> bright, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> input, bool encrypting)
    {
        if (bright.Length != BlockSize)
            throw new ArgumentException($"the key is {BlockSize} bytes", nameof(bright));
        if (iv.Length != BlockSize)
            throw new ArgumentException($"the IV is {BlockSize} bytes", nameof(iv));

        var output = new byte[input.Length];
        if (input.Length == 0)
            return output;

        using Aes aes = Aes.Create();
        aes.Key = bright.ToArray();

        // ECB with no padding is the raw block function, which is what CFB is built out of. It is
        // never used to encrypt anything but the IV, so the usual objection to ECB does not apply.
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        Span<byte> feedback = stackalloc byte[BlockSize];
        Span<byte> keystream = stackalloc byte[BlockSize];
        iv.CopyTo(feedback);

        for (int offset = 0; offset < input.Length; offset += BlockSize)
        {
            aes.EncryptEcb(feedback, keystream, PaddingMode.None);

            int take = Math.Min(BlockSize, input.Length - offset);
            for (int i = 0; i < take; i++)
            {
                byte inputByte = input[offset + i];
                byte outputByte = (byte)(inputByte ^ keystream[i]);
                output[offset + i] = outputByte;

                // The ciphertext, whichever side it is on. Encrypting that is the output; decrypting
                // it is the input.
                feedback[i] = encrypting ? outputByte : inputByte;
            }

            // A final partial block leaves the rest of the feedback stale, which never matters -
            // there is no next block to use it. Left alone rather than cleared, because clearing it
            // would be inventing a rule the C does not have.
        }

        return output;
    }

    /// <summary>PP26: whether the C still uses AES-128 in CFB128 rather than another mode.</summary>
    public static bool TheModeIsStillCfb128(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("mbedtls_aes_crypt_cfb128(", StringComparison.Ordinal)
            && core.Contains("EVP_aes_128_cfb128()", StringComparison.Ordinal);
    }

    /// <summary>And whether it is still keyed on bright rather than on the ambassador.</summary>
    public static bool TheKeyIsStillBright(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("mbedtls_aes_setkey_enc(&ctx, rpcrypt->bright, 128)", StringComparison.Ordinal);
    }
}
