using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP26: the registration keys, which is where a PIN a user typed reaches the key schedule.
///
/// Registering a console is the one exchange whose key is not derived from a nonce. There is no
/// session yet and nothing shared to derive from, so the key is a constant the console also knows,
/// with the PIN the user read off the screen XORed into it. That is the whole secret: four bytes
/// somebody typed.
///
/// The PIN lands in different bytes on the two paths
/// -------------------------------------------------
/// Before firmware 10 it goes into bright[0..3]; from 10 onwards into bright[0xc..0xf]. Same PIN,
/// same big-endian order, opposite end of the key - and a port that used one offset for both would
/// register successfully against exactly one family of console and fail against the other with no
/// error anybody could read.
///
/// The table read is a COLUMN
/// --------------------------
/// <c>keys_0[i * 0x20 + key_0_off]</c>. The table is 512 bytes read as sixteen rows of thirty-two,
/// and the key is one column of it - byte key_0_off of each row, not the sixteen bytes starting
/// there. Reading it contiguously produces a plausible key from the right table and is wrong for
/// every offset but zero.
/// </summary>
public static class RpCryptRegist
{
    /// <summary>CHIAKI_RPCRYPT_KEY_SIZE.</summary>
    public const int KeySize = 0x10;

    /// <summary>How many columns a keys_0 table has, and the bound on key_0_off.</summary>
    public const int Columns = 0x20;

    /// <summary>regist_aes_key, the constant a pre-10 PS4 registers with.</summary>
    public static ReadOnlySpan<byte> Ps4Pre10RegistKey =>
        [0x3f, 0x1c, 0xc4, 0xb6, 0xdc, 0xbb, 0x3e, 0xcc,
         0x50, 0xba, 0xed, 0xef, 0x97, 0x34, 0xc7, 0xc9];

    /// <summary>
    /// chiaki_rpcrypt_init_regist_ps4_pre10: the fixed key with the PIN in its FIRST four bytes.
    /// </summary>
    public static byte[] BrightPs4Pre10(uint pin)
    {
        var bright = Ps4Pre10RegistKey.ToArray();
        XorPin(bright, 0, pin);
        return bright;
    }

    /// <summary>
    /// chiaki_rpcrypt_init_regist: a column of the target's table with the PIN in its LAST four.
    /// </summary>
    /// <param name="keys0">ps4_keys_0 or ps5_keys_0, 512 bytes.</param>
    /// <param name="keyOffset">Which column, below <see cref="Columns"/>.</param>
    public static byte[] Bright(ChiakiTarget target, ReadOnlySpan<byte> keys0, int keyOffset, uint pin)
    {
        if (target < ChiakiTarget.Ps4_10)
            throw new ArgumentOutOfRangeException(nameof(target), "below PS4 10 takes the pre-10 path");

        if (keys0.Length != KeySize * Columns)
            throw new ArgumentException($"a keys_0 table is {KeySize * Columns} bytes", nameof(keys0));

        // The C answers CHIAKI_ERR_INVALID_DATA rather than reading past the row.
        if (keyOffset < 0 || keyOffset >= Columns)
            throw new ArgumentOutOfRangeException(nameof(keyOffset), $"must be below {Columns}");

        var bright = new byte[KeySize];
        for (int i = 0; i < KeySize; i++)
            bright[i] = keys0[(i * Columns) + keyOffset];

        XorPin(bright, KeySize - 4, pin);
        return bright;
    }

    /// <summary>The table for a target, which is the same choice the schedule makes.</summary>
    public static ReadOnlySpan<byte> Keys0For(ChiakiTarget target)
        => RpVersion.IsPs5(target) ? RpCryptTables.Ps5Keys0 : RpCryptTables.Ps4Keys0;

    /// <summary>
    /// The PIN, big-endian, into four bytes starting at <paramref name="at"/>.
    ///
    /// One helper for both paths so the byte ORDER cannot drift between them even though the
    /// offsets differ - that is the half the two share, and the half worth writing once.
    /// </summary>
    private static void XorPin(byte[] bright, int at, uint pin)
    {
        bright[at + 0] ^= (byte)((pin >> 0x18) & 0xff);
        bright[at + 1] ^= (byte)((pin >> 0x10) & 0xff);
        bright[at + 2] ^= (byte)((pin >> 0x08) & 0xff);
        bright[at + 3] ^= (byte)((pin >> 0x00) & 0xff);
    }

    /// <summary>PP26: whether the PIN still lands at the two different offsets.</summary>
    public static bool ThePinOffsetsAreStill(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("rpcrypt->bright[0] ^= (uint8_t)((pin >> 0x18) & 0xff);", StringComparison.Ordinal)
            && core.Contains("rpcrypt->bright[0xc] ^= (uint8_t)((pin >> 0x18) & 0xff);", StringComparison.Ordinal);
    }

    /// <summary>And whether the key is still read as a column rather than a run.</summary>
    public static bool TheKeyIsStillAColumn(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("rpcrypt->bright[i] = keys_0[i*0x20 + key_0_off];", StringComparison.Ordinal);
    }
}
