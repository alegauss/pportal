namespace ChiakiNg.Protocol;

/// <summary>
/// PP286: GF(2^8), the arithmetic every FEC operation in this protocol is built out of.
///
/// jerasure and gf-complete are the only vendored C with no managed equivalent to install, so PP30
/// is a port rather than a swap - and this is the bottom of it. Reed-Solomon over a byte field is
/// three operations: add, which is XOR, and multiply and divide, which are addition and subtraction
/// of logarithms once the tables exist.
///
/// The polynomial is 0x11d and it is not a choice
/// ----------------------------------------------
/// A Galois field of 256 elements exists for every primitive polynomial of degree 8, and they are
/// all isomorphic - and none of that helps, because two implementations that pick differently
/// produce different bytes for the same multiply. gf-complete's default for w=8 is 0x11d, which is
/// what galois_init_default_field(8) installs and therefore what every recorded case in
/// test/fec_test_cases.inl was produced with. Choosing any other one here would be choosing to fail
/// all 64 of them for a reason no assertion would name.
///
/// The tables are built once and are 512 bytes. Building them per call would be the kind of thing
/// that only shows up as latency on a lossy connection, which is exactly when FEC runs.
/// </summary>
public static class GaloisField
{
    /// <summary>gf-complete's default primitive polynomial for w=8, x^8 + x^4 + x^3 + x^2 + 1.</summary>
    public const int Polynomial = 0x11d;

    /// <summary>The field's order. Every element is a byte.</summary>
    public const int Order = 256;

    // log[0] is undefined and is never read - Multiply and Divide test for zero before they index.
    // Left at 0 rather than a sentinel because a sentinel would suggest somebody checks it.
    private static readonly byte[] Log = new byte[Order];
    private static readonly byte[] Antilog = new byte[Order * 2];

    static GaloisField()
    {
        // The generator is 2, which is x. Walking its powers visits every non-zero element exactly
        // once - that is what "primitive" means about the polynomial - so this fills both tables in
        // one pass and a field that did not would produce a table with a hole in it.
        int value = 1;
        for (int power = 0; power < Order - 1; power++)
        {
            Log[value] = (byte)power;
            Antilog[power] = (byte)value;

            value <<= 1;
            if (value >= Order)
                value ^= Polynomial;
        }

        // The second half repeats the first, so a sum of two logarithms - which can reach 508 -
        // needs no modulo at the call site. The alternative is a branch on every multiply.
        for (int power = Order - 1; power < Antilog.Length; power++)
            Antilog[power] = Antilog[power - (Order - 1)];
    }

    /// <summary>Addition, which in a field of characteristic two is XOR and is its own inverse.</summary>
    public static byte Add(byte a, byte b) => (byte)(a ^ b);

    /// <summary>
    /// Multiplication. Zero is answered directly: it has no logarithm, and the table lookup would
    /// read log[0] and return whatever happened to be there.
    /// </summary>
    public static byte Multiply(byte a, byte b)
        => a == 0 || b == 0 ? (byte)0 : Antilog[Log[a] + Log[b]];

    /// <summary>
    /// Division. Dividing by zero is not a value this can answer, and it is not reachable from the
    /// Cauchy matrix - i ^ (m + j) is zero only when i equals m + j, which cannot happen because i
    /// is below m and m + j is not.
    /// </summary>
    public static byte Divide(byte a, byte b)
    {
        if (b == 0)
            throw new DivideByZeroException("GF(2^8) has no inverse of zero");

        return a == 0 ? (byte)0 : Antilog[Log[a] - Log[b] + (Order - 1)];
    }

    /// <summary>The multiplicative inverse, which is what the Cauchy matrix is made of.</summary>
    public static byte Inverse(byte a) => Divide(1, a);
}
