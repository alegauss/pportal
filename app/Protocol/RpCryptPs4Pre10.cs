namespace ChiakiNg.Protocol;

/// <summary>
/// PP26: the PS4-before-10 key derivations, which are three loops that look like each other.
///
/// A PS4 on firmware below 10 derives its session keys by byte arithmetic rather than by a cipher:
/// sixteen bytes in, subtract the index, add or subtract a constant, XOR a table, sixteen bytes
/// out. Everything the newer targets do with AES over a 3.5KB key schedule, these do with this.
///
/// The three are the danger
/// ------------------------
/// ambassador, bright and aeropause are the same shape with different constants, and two of the
/// three even share the echo_b table:
///
///   ambassador = nonce[i]      - i - 0x27  ^ echo_a[i]
///   bright     = morning[i]    - i + 0x34  ^ echo_b[i] ^ nonce[i]
///   aeropause  = ambassador[i] - i - 0x29  ^ echo_b[i]
///
/// One is a PLUS where the others are a minus, and the three constants are 0x27, 0x34 and 0x29 -
/// close enough to read as the same number twice at a glance. A port that copies the first loop
/// twice and edits it produces keys that are wrong by a constant offset, which is PP26's whole
/// warning: nothing throws, and a session simply does not open.
///
/// Everything is byte arithmetic and wraps
/// ---------------------------------------
/// v is a uint8_t throughout, so subtracting past zero wraps. Done in C# with explicit byte casts
/// for the same reason - promoting to int and masking at the end is the same answer only if every
/// intermediate is masked too, and it is easier to be right by never leaving the width.
/// </summary>
public static class RpCryptPs4Pre10
{
    /// <summary>CHIAKI_RPCRYPT_KEY_SIZE.</summary>
    public const int KeySize = 0x10;

    /// <summary>echo_a, used by the ambassador only.</summary>
    public static ReadOnlySpan<byte> EchoA =>
        [0x01, 0x49, 0x87, 0x9b, 0x65, 0x39, 0x8b, 0x39, 0x4b, 0x3a, 0x8d, 0x48, 0xc3, 0x0a, 0xef, 0x51];

    /// <summary>echo_b, used by both bright and aeropause.</summary>
    public static ReadOnlySpan<byte> EchoB =>
        [0xe1, 0xec, 0x9c, 0x3a, 0xdd, 0xbd, 0x08, 0x85, 0xfc, 0x0e, 0x1d, 0x78, 0x90, 0x32, 0xc0, 0x04];

    /// <summary>bright_ambassador_ps4_pre10, both halves, in the order the C computes them.</summary>
    /// <remarks>
    /// The ambassador is computed first and bright reads the NONCE rather than the ambassador, so
    /// the order does not actually matter here - which is worth knowing before someone reorders
    /// them and has to work out whether it did.
    /// </remarks>
    public static (byte[] Bright, byte[] Ambassador) BrightAmbassador(
        ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> morning)
    {
        Require(nonce, nameof(nonce));
        Require(morning, nameof(morning));

        var ambassador = new byte[KeySize];
        for (int i = 0; i < KeySize; i++)
        {
            byte v = nonce[i];
            v -= (byte)i;
            v -= 0x27;
            v ^= EchoA[i];
            ambassador[i] = v;
        }

        var bright = new byte[KeySize];
        for (int i = 0; i < KeySize; i++)
        {
            byte v = morning[i];
            v -= (byte)i;

            // PLUS. The only one of the three that adds.
            v += 0x34;
            v ^= EchoB[i];
            v ^= nonce[i];
            bright[i] = v;
        }

        return (bright, ambassador);
    }

    /// <summary>chiaki_rpcrypt_aeropause_ps4_pre10.</summary>
    public static byte[] Aeropause(ReadOnlySpan<byte> ambassador)
    {
        Require(ambassador, nameof(ambassador));

        var aeropause = new byte[KeySize];
        for (int i = 0; i < KeySize; i++)
        {
            byte v = ambassador[i];
            v -= (byte)i;
            v -= 0x29;
            v ^= EchoB[i];
            aeropause[i] = v;
        }

        return aeropause;
    }

    private static void Require(ReadOnlySpan<byte> value, string name)
    {
        if (value.Length != KeySize)
            throw new ArgumentException($"{name} is {KeySize} bytes", name);
    }

    /// <summary>PP26: whether the three constants in the C are still these three.</summary>
    public static bool TheConstantsAreStill(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("v -= 0x27;", StringComparison.Ordinal)
            && core.Contains("v += 0x34;", StringComparison.Ordinal)
            && core.Contains("v -= 0x29;", StringComparison.Ordinal);
    }

    /// <summary>And whether the two tables are still these two.</summary>
    public static bool TheTablesAreStill(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("echo_a[] = { 0x01, 0x49, 0x87, 0x9b", StringComparison.Ordinal)
            && core.Contains("echo_b[] = { 0xe1, 0xec, 0x9c, 0x3a", StringComparison.Ordinal);
    }
}
