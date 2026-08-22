using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP286: the managed Galois field and Cauchy matrix, held against jerasure's own.
///
/// PP30 is a port and not a swap - there is no managed Reed-Solomon to install - and the recorded
/// cases in test/fec_test_cases.inl can only judge the finished decoder. When one of those fails,
/// the field, the matrix and the decode are all still suspects. So the arithmetic underneath is
/// agreed first, against the implementation it has to replace, and the recorded cases are left to
/// judge the one thing this cannot: whether the decode puts the right bytes back.
/// </summary>
public class FecMatrixTests(ITestOutputHelper output)
{
    /// <summary>
    /// The field tables are a global that galois_init_default_field fills, so a matrix asked for
    /// before it is a matrix of zeroes - which compares equal to nothing and fails for the wrong
    /// reason.
    /// </summary>
    private static void EnsureNativeField()
        => Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

    /// <summary>
    /// The shapes the protocol actually uses, plus the degenerate one.
    ///
    /// Taken from the recorded cases rather than invented: a stream's k and m move with the frame,
    /// and a matrix that is right for 4x2 and wrong for 15x4 is a matrix that fails on exactly the
    /// frames that needed FEC most.
    /// </summary>
    public static TheoryData<int, int> Shapes() => new()
    {
        { 1, 1 }, { 2, 1 }, { 4, 2 }, { 8, 2 }, { 10, 3 }, { 15, 4 }, { 16, 8 }, { 32, 16 },
    };

    /// <summary>THE ASSERTION. Every entry, for every shape, byte for byte.</summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheManagedMatrixIsJerasuresMatrix(int k, int m)
    {
        EnsureNativeField();

        int[]? native = FecMatrix.Native(k, m);
        Assert.True(native is not null, $"jerasure would not build a {k}x{m} matrix");

        int[] managed = FecMatrix.Cauchy(k, m);

        Assert.Equal(native.Length, managed.Length);
        for (int i = 0; i < native.Length; i++)
        {
            Assert.True(
                native[i] == managed[i],
                $"{k}x{m} entry {i} (row {i / k}, column {i % k}): jerasure {native[i]}, managed {managed[i]}");
        }

        output.WriteLine($"{k}x{m}: {managed.Length} entries agree");
    }

    /// <summary>
    /// And the matrix is not trivial, which the comparison above cannot say on its own.
    ///
    /// Two implementations that both returned zeroes would agree perfectly. The Cauchy construction
    /// has no zero entry at all - every element is an inverse, and zero has none - so a single zero
    /// anywhere means one of the two produced a table it never filled.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void NoEntryIsZero(int k, int m)
    {
        EnsureNativeField();

        Assert.DoesNotContain(0, FecMatrix.Cauchy(k, m));
        Assert.DoesNotContain(0, FecMatrix.Native(k, m)!);
    }

    /// <summary>
    /// The field itself, on the laws that make it one.
    ///
    /// These are cheap and they localise a failure the matrix comparison cannot: a wrong primitive
    /// polynomial produces a perfectly consistent field whose products are simply different, and
    /// that shows up above as every entry disagreeing with no clue why.
    /// </summary>
    [Fact]
    public void TheFieldObeysItsOwnLaws()
    {
        // Multiplication and division undo each other for every non-zero pair. 255 * 255 of them,
        // which is the whole field and cheap enough to just do.
        for (int a = 1; a < 256; a++)
        {
            for (int b = 1; b < 256; b++)
            {
                byte product = GaloisField.Multiply((byte)a, (byte)b);
                Assert.True(product != 0, $"{a} * {b} was zero, and a field has no zero divisors");
                Assert.Equal((byte)a, GaloisField.Divide(product, (byte)b));
            }
        }

        // One is the identity, zero absorbs, and every non-zero element has an inverse.
        for (int a = 1; a < 256; a++)
        {
            Assert.Equal((byte)a, GaloisField.Multiply((byte)a, 1));
            Assert.Equal(0, GaloisField.Multiply((byte)a, 0));
            Assert.Equal(1, GaloisField.Multiply((byte)a, GaloisField.Inverse((byte)a)));
        }

        // Addition is XOR, which is its own inverse - this is the property FEC leans on hardest,
        // because it is what lets a parity unit be removed from a sum by adding it again.
        Assert.Equal(0, GaloisField.Add(0xA5, 0xA5));
        Assert.Equal(0xFF, GaloisField.Add(0x0F, 0xF0));

        // And zero has no inverse, which is the one case the Cauchy construction relies on never
        // reaching: i ^ (m + j) is zero only when i == m + j, and i is below m.
        Assert.Throws<DivideByZeroException>(() => GaloisField.Inverse(0));
    }
}
