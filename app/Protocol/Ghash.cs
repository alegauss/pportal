using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP298: GHASH and the GCM tag, because the runtime will not lend this one.
///
/// Every other primitive chiaki needs came off System.Security.Cryptography. The GMAC did not:
/// AesGcm's NonceByteSizes is 12 to 12 and its TagByteSizes 12 to 16, measured, and chiaki passes a
/// 16-byte IV and keeps four bytes of tag. Both are in the GCM specification and neither is in the
/// type's contract, so the authentication tag is built here out of AES-ECB, which is available.
///
/// The field is GF(2^128) with the bits the other way round
/// ---------------------------------------------------------
/// GCM numbers bits from the MOST significant end of the first byte, so what a normal
/// implementation calls a right shift is a shift towards higher bit numbers, and the reduction
/// polynomial appears as 0xe1 in the FIRST byte rather than as 0x87 in the last. Every part of that
/// convention is easy to write the ordinary way and get a function that is self-consistent, passes
/// its own round trip, and disagrees with every other GCM in the world.
///
/// Which is why nothing here is checked against itself. The tag is compared with chiaki's, whose
/// GCM is OpenSSL's.
/// </summary>
public static class Ghash
{
    /// <summary>The block size, in bytes.</summary>
    public const int BlockSize = 16;

    /// <summary>
    /// Multiplication in GF(2^128) under GCM's bit convention.
    /// </summary>
    public static byte[] Multiply(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
    {
        if (x.Length != BlockSize || y.Length != BlockSize)
            throw new ArgumentException($"operands are {BlockSize} bytes");

        Span<byte> z = stackalloc byte[BlockSize];
        Span<byte> v = stackalloc byte[BlockSize];
        y.CopyTo(v);

        for (int bit = 0; bit < 128; bit++)
        {
            // Bit `bit` of x, counted from the top of byte 0 - GCM's numbering, not a byte's.
            if ((x[bit / 8] & (0x80 >> (bit % 8))) != 0)
            {
                for (int i = 0; i < BlockSize; i++)
                    z[i] ^= v[i];
            }

            // v >>= 1 across the whole block, then reduce if a one fell off the bottom. The
            // polynomial lands in byte 0 because the low-order end of this field is the high-order
            // end of the array.
            bool carry = (v[BlockSize - 1] & 1) != 0;
            for (int i = BlockSize - 1; i > 0; i--)
                v[i] = (byte)((v[i] >> 1) | ((v[i - 1] & 1) << 7));

            v[0] >>= 1;

            if (carry)
                v[0] ^= 0xe1;
        }

        return z.ToArray();
    }

    /// <summary>
    /// GHASH over a message that is already a whole number of blocks.
    /// </summary>
    public static byte[] Hash(ReadOnlySpan<byte> subkey, ReadOnlySpan<byte> blocks)
    {
        if (blocks.Length % BlockSize != 0)
            throw new ArgumentException("GHASH takes whole blocks", nameof(blocks));

        var y = new byte[BlockSize];
        for (int offset = 0; offset < blocks.Length; offset += BlockSize)
        {
            for (int i = 0; i < BlockSize; i++)
                y[i] ^= blocks[offset + i];

            y = Multiply(y, subkey);
        }

        return y;
    }

    /// <summary>
    /// The GCM tag over additional data only, with no ciphertext - which is what a GMAC is.
    /// </summary>
    /// <param name="key">The AES key.</param>
    /// <param name="iv">Any length. Twelve bytes takes GCM's fast path; anything else is hashed.</param>
    /// <param name="additionalData">The authenticated message.</param>
    /// <param name="tagLength">How many bytes of the tag to keep. chiaki keeps four.</param>
    public static byte[] Tag(
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> additionalData, int tagLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tagLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tagLength, BlockSize);

        using Aes aes = Aes.Create();
        aes.Key = key.ToArray();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        // H, the hash subkey: the block function applied to zero.
        Span<byte> subkey = stackalloc byte[BlockSize];
        aes.EncryptEcb(stackalloc byte[BlockSize], subkey, PaddingMode.None);

        byte[] j0 = InitialCounter(subkey, iv);

        // S = GHASH(A padded to a block boundary || len(A) in bits || len(C) in bits). There is no
        // ciphertext, so its length is the zero half of that last block.
        int padded = ((additionalData.Length + BlockSize - 1) / BlockSize) * BlockSize;
        var message = new byte[padded + BlockSize];
        additionalData.CopyTo(message);
        BinaryPrimitives.WriteUInt64BigEndian(message.AsSpan(padded), (ulong)additionalData.Length * 8);

        byte[] s = Hash(subkey, message);

        Span<byte> mask = stackalloc byte[BlockSize];
        aes.EncryptEcb(j0, mask, PaddingMode.None);

        var tag = new byte[tagLength];
        for (int i = 0; i < tagLength; i++)
            tag[i] = (byte)(s[i] ^ mask[i]);

        return tag;
    }

    /// <summary>
    /// J0, which is the IV itself only when the IV is twelve bytes.
    ///
    /// The 96-bit case appends a one and is the path every implementation optimises for. Anything
    /// else is GHASH over the IV padded to a block boundary and its bit length - which is the case
    /// chiaki is in, with sixteen, and the reason AesGcm cannot be used at all.
    /// </summary>
    public static byte[] InitialCounter(ReadOnlySpan<byte> subkey, ReadOnlySpan<byte> iv)
    {
        if (iv.Length == 12)
        {
            var fast = new byte[BlockSize];
            iv.CopyTo(fast);
            fast[BlockSize - 1] = 1;
            return fast;
        }

        int padded = ((iv.Length + BlockSize - 1) / BlockSize) * BlockSize;
        var message = new byte[padded + BlockSize];
        iv.CopyTo(message);
        BinaryPrimitives.WriteUInt64BigEndian(message.AsSpan(padded + 8), (ulong)iv.Length * 8);

        return Hash(subkey, message);
    }
}
