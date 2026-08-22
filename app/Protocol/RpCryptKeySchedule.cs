using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP26: bright and ambassador for PS4 10 and PS5, which is table selection plus four more loops.
///
/// The name suggests a cipher and it is not one. Like the PS4-before-10 path, this is byte
/// arithmetic - the difference is that the constant XORed in is not a fixed sixteen bytes but a row
/// chosen out of a 3.5KB table by a byte of the nonce.
///
/// Two selections, two different nonce bytes
/// -----------------------------------------
/// The ambassador's row is picked by nonce[0] and bright's by nonce[7], both as
/// <c>(nonce[n] &gt;&gt; 3) * 0x70</c> - thirty-two rows of 0x70 bytes, of which only the first
/// sixteen are ever read. A port that used one nonce byte for both, or dropped the stride to 0x10
/// because that is all it reads, would select the wrong row for every nonce.
///
/// Seven loops now, and no two alike
/// ---------------------------------
/// With <see cref="RpCryptPs4Pre10"/> this family is seven near-identical loops:
///
///   pre-10 ambassador   nonce   - i - 0x27  ^ echo_a
///   pre-10 bright       morning - i + 0x34  ^ echo_b ^ nonce
///   pre-10 aeropause    ambass. - i - 0x29  ^ echo_b
///   PS5 ambassador      nonce   - i - 0x2d  ^ key
///   PS4 ambassador      nonce   + i + 0x36  ^ key
///   PS5 bright          morning + i + 0x18  ^ nonce ^ key
///   PS4 bright          (key ^ morning) + i + 0x21 ^ nonce
///
/// Seven constants, three of them added and four subtracted, and the last one XORs the key BEFORE
/// the arithmetic where every other does it after. That last difference is the one that survives a
/// careless port, because it still compiles and still produces sixteen plausible bytes.
///
/// The tables are not here
/// -----------------------
/// Four of them at 3.5KB each. They are passed in rather than embedded, so this file is the
/// algorithm and the 14KB of constants is a separate question - see the remainder on PP26.
/// </summary>
public static class RpCryptKeySchedule
{
    /// <summary>CHIAKI_RPCRYPT_KEY_SIZE.</summary>
    public const int KeySize = 0x10;

    /// <summary>The stride between rows in a key table. Only the first <see cref="KeySize"/> are read.</summary>
    public const int RowStride = 0x70;

    /// <summary>How many rows a table has.</summary>
    public const int Rows = 0x20;

    /// <summary>A full table's size, which is what a caller must supply.</summary>
    public const int TableSize = RowStride * Rows;

    /// <summary>Which row a nonce byte selects.</summary>
    public static int RowFor(byte nonceByte) => nonceByte >> 3;

    /// <summary>
    /// bright_ambassador for a target at or above PS4 10.
    /// </summary>
    /// <param name="target">Decides both the arithmetic and which pair of tables the caller passed.</param>
    /// <param name="keysA">The ambassador's table, <see cref="TableSize"/> bytes.</param>
    /// <param name="keysB">Bright's table, the same size.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// For a target below PS4 10, which the C answers with CHIAKI_ERR_INVALID_DATA - those go to
    /// <see cref="RpCryptPs4Pre10"/> instead.
    /// </exception>
    public static (byte[] Bright, byte[] Ambassador) BrightAmbassador(
        ChiakiTarget target, ReadOnlySpan<byte> keysA, ReadOnlySpan<byte> keysB,
        ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> morning)
    {
        if (target < ChiakiTarget.Ps4_10)
            throw new ArgumentOutOfRangeException(nameof(target), "below PS4 10 takes the pre-10 path");

        Require(keysA, TableSize, nameof(keysA));
        Require(keysB, TableSize, nameof(keysB));
        Require(nonce, KeySize, nameof(nonce));
        Require(morning, KeySize, nameof(morning));

        bool isPs5 = RpVersion.IsPs5(target);

        // nonce[0] for the ambassador...
        ReadOnlySpan<byte> keyA = keysA.Slice(RowFor(nonce[0]) * RowStride, KeySize);
        var ambassador = new byte[KeySize];
        for (int i = 0; i < KeySize; i++)
        {
            byte v = nonce[i];
            if (isPs5)
            {
                v -= 0x2d;
                v -= (byte)i;
            }
            else
            {
                v += 0x36;
                v += (byte)i;
            }

            v ^= keyA[i];
            ambassador[i] = v;
        }

        // ...and nonce[7] for bright. A different byte, and the reason a port that reused nonce[0]
        // would still work for every nonce whose two bytes happen to share a top five bits.
        ReadOnlySpan<byte> keyB = keysB.Slice(RowFor(nonce[7]) * RowStride, KeySize);
        var bright = new byte[KeySize];
        for (int i = 0; i < KeySize; i++)
        {
            byte v;
            if (isPs5)
            {
                v = morning[i];
                v += 0x18;
                v += (byte)i;
                v ^= nonce[i];
                v ^= keyB[i];
            }
            else
            {
                // The key is XORed FIRST here, which no other loop in the family does.
                v = (byte)(keyB[i] ^ morning[i]);
                v += 0x21;
                v += (byte)i;
                v ^= nonce[i];
            }

            bright[i] = v;
        }

        return (bright, ambassador);
    }

    private static void Require(ReadOnlySpan<byte> value, int size, string name)
    {
        if (value.Length != size)
            throw new ArgumentException($"{name} is {size} bytes", name);
    }

    /// <summary>PP26: whether the C still selects rows from these two nonce bytes.</summary>
    public static bool TheRowSelectionIsStill(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("&keys_a[(nonce[0] >> 3) * 0x70]", StringComparison.Ordinal)
            && core.Contains("&keys_b[(nonce[7] >> 3) * 0x70]", StringComparison.Ordinal);
    }

    /// <summary>And whether the four constants are still these four.</summary>
    public static bool TheConstantsAreStill(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("v -= 0x2d;", StringComparison.Ordinal)
            && core.Contains("v += 0x36;", StringComparison.Ordinal)
            && core.Contains("v += 0x18;", StringComparison.Ordinal)
            && core.Contains("v += 0x21;", StringComparison.Ordinal);
    }

    /// <summary>And whether the PS4 bright still XORs the key before the arithmetic.</summary>
    public static bool ThePs4BrightStillXorsFirst(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("uint8_t v = (key[i] ^ morning[i]);", StringComparison.Ordinal);
    }
}
