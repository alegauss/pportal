using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP286: the coding matrix FEC encodes and decodes through, and the native one to check it against.
///
/// fec.c builds exactly one thing before doing any work - cauchy_original_coding_matrix(k, m, 8) -
/// and hands it to both jerasure_matrix_encode and jerasure_matrix_decode. Every byte those two
/// produce follows from it, so it is the first thing a managed port has to get right and the
/// cheapest thing to be wrong about silently: a matrix that disagrees still has the right shape,
/// still decodes without error, and returns bytes that are not the ones that were sent.
///
/// Which is why this is a pair. <see cref="Cauchy"/> is the port and <see cref="Native"/> is
/// jerasure's own, and the assertion between them is what makes the port a translation rather than
/// a guess. The recorded cases in test/fec_test_cases.inl judge the decoder that comes later; they
/// cannot say which of the field, the matrix or the decode was wrong when one of them is.
/// </summary>
public static class FecMatrix
{
    /// <summary>
    /// The Cauchy matrix jerasure calls "original", m rows of k, in row-major order.
    ///
    /// Each entry is the inverse of i XOR (m + j) - a Cauchy matrix over GF(2^8) whose two index
    /// sets are 0..m-1 and m..m+k-1, which is what makes every square submatrix invertible and is
    /// the whole reason any k surviving units can rebuild the frame.
    ///
    /// Returned as int and not byte, because that is the width jerasure's matrix has and the
    /// comparison against it should not be reading through a conversion.
    /// </summary>
    public static int[] Cauchy(int k, int m)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(m);

        var matrix = new int[k * m];
        int index = 0;

        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < k; j++)
                matrix[index++] = GaloisField.Inverse((byte)(i ^ (m + j)));
        }

        return matrix;
    }

    /// <summary>
    /// jerasure's own, through the shim, or null where the native side would not build it.
    ///
    /// ChiakiSession.LibInit must have run: the field tables live in a global that
    /// galois_init_default_field fills, and a matrix built before it is a matrix of zeroes.
    /// </summary>
    public static int[]? Native(int k, int m)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(m);

        var matrix = new int[k * m];
        int written = FecMatrixNative((uint)k, (uint)m, matrix, matrix.Length);
        return written == matrix.Length ? matrix : null;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_fec_matrix",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int FecMatrixNative(uint k, uint m, int[] outMatrix, int capacity);
}
