namespace DecoderChoice;

/// <summary>
/// PP818: Annex-B split into the access units a decoder is fed one at a time.
///
/// THE FRAMING IS NOT FREE HERE AND A FIRST ATTEMPT ASSUMES IT IS. Handing the whole file to the
/// transform measures nothing per frame, and splitting at every start code feeds it slices - so the
/// unit boundary has to be found, and this stream carries no access unit delimiters to find it by:
/// its NAL types are 1, 5, 6, 7 and 8, and never 9.
///
/// SEVENTEEN SLICES A FRAME, which is what makes the naive rule wrong rather than merely imprecise.
/// The encoder splits each picture across many slices, so "a slice NAL starts a frame" would report
/// seventeen times too many frames, each a seventeenth of the work - and the per-frame cost would
/// come out about right divided by seventeen, which is the kind of wrong answer that looks
/// plausible.
///
/// SO THE RULE IS first_mb_in_slice, the first field of every slice header: zero means this slice
/// starts a new picture and anything else means it continues one. It is an unsigned exp-Golomb
/// value at the very start of the RBSP, so reading it needs no SPS and no context - which is why
/// this is a dozen lines rather than a parser.
///
/// EMULATION PREVENTION IS NOT UNDONE, deliberately. A 0x03 byte is only ever inserted after two
/// zero bytes, and the leading bits of first_mb_in_slice cannot be reached through two zero bytes
/// without the value already being enormous - so for the one field this reads, the escaped and
/// unescaped bitstreams agree. The bytes handed to the decoder are the file's own either way.
/// </summary>
internal static class AccessUnits
{
    /// <summary>Coded slice of a non-IDR picture.</summary>
    private const int NalSlice = 1;

    /// <summary>Coded slice of an IDR picture.</summary>
    private const int NalIdr = 5;

    /// <summary>
    /// Splits an Annex-B stream into access units.
    /// </summary>
    /// <param name="stream">The whole elementary stream.</param>
    /// <param name="limit">Stop after this many units; 0 or less takes them all.</param>
    /// <returns>
    /// Segments over the caller's array, so nothing is copied here. Each begins at a start code and
    /// runs to the byte before the next unit's - parameter sets and SEI travelling with the picture
    /// they precede, which is the grouping a decoder expects.
    /// </returns>
    public static IReadOnlyList<ArraySegment<byte>> Split(byte[] stream, int limit = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var units = new List<ArraySegment<byte>>();
        var unitStart = -1;

        // Where the current run of non-slice NALs began, or -1 while the last thing seen was a
        // slice. A parameter set or SEI belongs to the picture it PRECEDES, so the boundary goes
        // before the run rather than at the slice - which for this stream is what keeps the SPS and
        // PPS at offset zero inside the first access unit instead of dropping them.
        var runStart = -1;

        for (var i = 0; i + 3 < stream.Length; i++)
        {
            if (stream[i] != 0 || stream[i + 1] != 0 || stream[i + 2] != 1)
                continue;

            int header = i + 3;
            int type = stream[header] & 0x1f;
            bool slice = type is NalSlice or NalIdr;

            if (slice && FirstMacroblockIsZero(stream, header + 1))
            {
                int boundary = runStart >= 0 ? runStart : i;

                if (unitStart >= 0)
                {
                    units.Add(new ArraySegment<byte>(stream, unitStart, boundary - unitStart));

                    if (limit > 0 && units.Count >= limit)
                        return units;
                }

                unitStart = boundary;
            }

            runStart = slice ? -1 : (runStart >= 0 ? runStart : i);
            i += 2;
        }

        if (unitStart >= 0 && unitStart < stream.Length)
            units.Add(new ArraySegment<byte>(stream, unitStart, stream.Length - unitStart));

        return units;
    }

    /// <summary>
    /// Whether the exp-Golomb value beginning at <paramref name="offset"/> is zero.
    ///
    /// An unsigned exp-Golomb codeword is N zero bits, a one bit, then N more bits, and the value is
    /// zero exactly when N is zero - so the whole question is whether the FIRST bit is a one. That
    /// is the entire read, which is why no bit reader is needed for it.
    /// </summary>
    private static bool FirstMacroblockIsZero(byte[] stream, int offset)
        => offset < stream.Length && (stream[offset] & 0x80) != 0;
}
