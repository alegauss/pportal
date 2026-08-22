namespace ChiakiNg.Protocol;

/// <summary>
/// PP287: the encode and decode themselves, in managed code.
///
/// PP286 agreed the field and the coding matrix with jerasure's, entry for entry. This is what sits
/// on them, and it is the last of lib/src/fec.c: 133 lines whose whole job is to call
/// jerasure_matrix_encode and jerasure_matrix_decode over a Cauchy matrix.
///
/// The layout is the C's and is not obvious
/// ----------------------------------------
/// A frame is k data units followed by m parity units, each at <c>stride</c> from the last and only
/// <c>unitSize</c> of that used. The padding is the decoder's, not the test's - jerasure is handed
/// pointers into the buffer and reads unitSize from each, so the gap between them is free space it
/// never touches.
///
/// What decoding actually is
/// -------------------------
/// The generator is [I; C] - k rows of identity over the data, then the m Cauchy rows - so every
/// unit in the frame is one row of it applied to the original data. Lose some, and any k surviving
/// rows form a square matrix; a Cauchy construction guarantees it inverts, and that inverse applied
/// to the k surviving units is the original data. Nothing here is specific to which units were
/// lost, which is the property that makes m parity units repair any m losses.
/// </summary>
public static class FecCodec
{
    /// <summary>
    /// Writes the m parity units, reading the k data units in place.
    ///
    /// Mirrors chiaki_fec_encode, including where it puts them: the C writes parity to
    /// <c>frame + k * unitSize + i * unitSize</c>, which is NOT <c>frame + stride * (k + i)</c> and
    /// is only the same buffer when stride equals unitSize. Reproduced rather than corrected -
    /// PP30 is a translation, and a port that quietly fixes a layout is a port whose output cannot
    /// be compared with the thing it replaced.
    /// </summary>
    public static void Encode(Span<byte> frame, int unitSize, int stride, int k, int m)
    {
        Validate(unitSize, stride, k, m);

        int[] matrix = FecMatrix.Cauchy(k, m);

        for (int i = 0; i < m; i++)
        {
            Span<byte> parity = frame.Slice(k * unitSize + i * unitSize, unitSize);
            parity.Clear();

            for (int j = 0; j < k; j++)
            {
                byte coefficient = (byte)matrix[(i * k) + j];
                ReadOnlySpan<byte> data = frame.Slice(j * stride, unitSize);
                for (int b = 0; b < unitSize; b++)
                    parity[b] ^= GaloisField.Multiply(coefficient, data[b]);
            }
        }
    }

    /// <summary>
    /// Repairs the erased units in place, and answers whether it could.
    /// </summary>
    /// <param name="erasures">
    /// Which unit indices were lost, data and parity alike. More than m of them is unrecoverable
    /// and is answered false rather than thrown: a lossy connection reaches that state normally,
    /// and the frame is simply dropped.
    /// </param>
    public static bool Decode(
        Span<byte> frame, int unitSize, int stride, int k, int m, ReadOnlySpan<uint> erasures)
    {
        Validate(unitSize, stride, k, m);

        Span<bool> lost = new bool[k + m];
        foreach (uint e in erasures)
        {
            if (e >= (uint)(k + m))
                return false;
            lost[(int)e] = true;
        }

        int[] matrix = FecMatrix.Cauchy(k, m);

        // The k surviving rows this will invert. Taken in order, which is jerasure's choice too -
        // any k of them would do, and picking the first keeps the two implementations comparable
        // when a case has more survivors than it needs.
        Span<int> chosen = new int[k];
        int found = 0;
        for (int unit = 0; unit < k + m && found < k; unit++)
        {
            if (!lost[unit])
                chosen[found++] = unit;
        }

        // Fewer than k survivors is not a failure to invert, it is not enough information to try.
        if (found < k)
            return false;

        bool anyDataLost = false;
        for (int i = 0; i < k; i++)
            anyDataLost |= lost[i];

        if (anyDataLost)
        {
            // Row `r` of the generator: e_r for a data unit, the Cauchy row for a parity one.
            var square = new byte[k * k];
            for (int r = 0; r < k; r++)
            {
                int unit = chosen[r];
                if (unit < k)
                    square[(r * k) + unit] = 1;
                else
                {
                    for (int j = 0; j < k; j++)
                        square[(r * k) + j] = (byte)matrix[((unit - k) * k) + j];
                }
            }

            byte[]? inverse = Invert(square, k);
            if (inverse is null)
                return false;

            // Only the lost data units are rebuilt. Recomputing a survivor would be writing the
            // same bytes back, and doing it in place would corrupt the inputs of the ones after it.
            var rebuilt = new byte[unitSize];
            for (int target = 0; target < k; target++)
            {
                if (!lost[target])
                    continue;

                rebuilt.AsSpan().Clear();
                for (int r = 0; r < k; r++)
                {
                    byte coefficient = inverse[(target * k) + r];
                    if (coefficient == 0)
                        continue;

                    ReadOnlySpan<byte> source = frame.Slice(chosen[r] * stride, unitSize);
                    for (int b = 0; b < unitSize; b++)
                        rebuilt[b] ^= GaloisField.Multiply(coefficient, source[b]);
                }

                rebuilt.AsSpan().CopyTo(frame.Slice(target * stride, unitSize));
            }
        }

        // And the lost PARITY units, which are simply re-encoded now that the data is whole. They
        // are written at stride here and not at the encode's packed offset, because this is a
        // repair of the frame as it was received rather than a fresh encode of it.
        for (int i = 0; i < m; i++)
        {
            if (!lost[k + i])
                continue;

            Span<byte> parity = frame.Slice((k + i) * stride, unitSize);
            parity.Clear();
            for (int j = 0; j < k; j++)
            {
                byte coefficient = (byte)matrix[(i * k) + j];
                ReadOnlySpan<byte> data = frame.Slice(j * stride, unitSize);
                for (int b = 0; b < unitSize; b++)
                    parity[b] ^= GaloisField.Multiply(coefficient, data[b]);
            }
        }

        return true;
    }

    /// <summary>
    /// Gauss-Jordan over GF(2^8), or null where the matrix is singular.
    ///
    /// Singular cannot happen for a Cauchy construction and is still answered rather than asserted:
    /// the rows handed in come from which units survived, which is network weather, and a decoder
    /// that threw on a frame it could not repair would take the session with it.
    /// </summary>
    public static byte[]? Invert(ReadOnlySpan<byte> square, int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        if (square.Length != n * n)
            throw new ArgumentException($"a {n}x{n} matrix is {n * n} entries", nameof(square));

        var work = square.ToArray();
        var inverse = new byte[n * n];
        for (int i = 0; i < n; i++)
            inverse[(i * n) + i] = 1;

        for (int column = 0; column < n; column++)
        {
            int pivot = -1;
            for (int row = column; row < n; row++)
            {
                if (work[(row * n) + column] != 0)
                {
                    pivot = row;
                    break;
                }
            }

            if (pivot < 0)
                return null;

            if (pivot != column)
            {
                SwapRows(work, n, pivot, column);
                SwapRows(inverse, n, pivot, column);
            }

            // Normalise the pivot row, then clear the column everywhere else. Division is by the
            // pivot value, which is non-zero by the search above - the one place GF division is
            // reached with something that could have been zero.
            byte scale = GaloisField.Inverse(work[(column * n) + column]);
            for (int j = 0; j < n; j++)
            {
                work[(column * n) + j] = GaloisField.Multiply(work[(column * n) + j], scale);
                inverse[(column * n) + j] = GaloisField.Multiply(inverse[(column * n) + j], scale);
            }

            for (int row = 0; row < n; row++)
            {
                if (row == column)
                    continue;

                byte factor = work[(row * n) + column];
                if (factor == 0)
                    continue;

                for (int j = 0; j < n; j++)
                {
                    work[(row * n) + j] ^= GaloisField.Multiply(factor, work[(column * n) + j]);
                    inverse[(row * n) + j] ^= GaloisField.Multiply(factor, inverse[(column * n) + j]);
                }
            }
        }

        return inverse;
    }

    private static void SwapRows(byte[] matrix, int n, int a, int b)
    {
        for (int j = 0; j < n; j++)
            (matrix[(a * n) + j], matrix[(b * n) + j]) = (matrix[(b * n) + j], matrix[(a * n) + j]);
    }

    private static void Validate(int unitSize, int stride, int k, int m)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(m);

        // The C returns CHIAKI_ERR_INVALID_DATA for this rather than reading past a unit, and the
        // units would overlap if it did not.
        if (stride < unitSize)
            throw new ArgumentOutOfRangeException(nameof(stride), "stride is below unitSize");
    }
}
