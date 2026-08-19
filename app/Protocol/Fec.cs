using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: forward error correction, driven from managed code so the recorded cases can judge it.
///
/// FEC runs on every frame and is two vendored C libraries doing Galois field arithmetic per lost
/// packet, so a managed rewrite is a port rather than a swap. What makes that port possible to
/// check at all is <see cref="FecVectors"/>: sixty-four erasure cases a real stream produced, with
/// the bytes that had to come back.
/// </summary>
public static class Fec
{
    /// <summary>
    /// Runs one recorded case: lays the units out at stride, blanks the erasures, decodes, and
    /// answers whether the data units came back byte for byte.
    ///
    /// The blanking is the test's own and matters: the erased units are overwritten with 0x42
    /// rather than zeroed, so a decoder that quietly left them alone would still be caught. Zeroes
    /// are what a lost unit might plausibly already hold.
    /// </summary>
    /// <param name="recorded">The case, whose erasures say which units are actually blanked.</param>
    /// <param name="declaredErasures">
    /// What the decoder is TOLD was lost, which defaults to what was. Passing something else is
    /// the only way to ask whether the decode did any work: a case where the two agree cannot
    /// distinguish a real repair from a decoder that returned the buffer untouched.
    /// </param>
    public static bool Recovers(FecCase recorded, uint[]? declaredErasures = null)
    {
        int stride = FecVectors.StrideFor(recorded.UnitSize);
        uint units = recorded.K + recorded.M;

        if (recorded.FrameBuffer.Length != recorded.UnitSize * units)
            throw new ArgumentException(
                $"the recorded buffer is {recorded.FrameBuffer.Length} bytes, not {recorded.UnitSize} x {units}.",
                nameof(recorded));

        var frame = new byte[stride * units];
        for (int i = 0; i < units; i++)
            recorded.FrameBuffer.AsSpan(i * recorded.UnitSize, recorded.UnitSize).CopyTo(frame.AsSpan(i * stride));

        foreach (uint e in recorded.Erasures)
            frame.AsSpan((int)e * stride, recorded.UnitSize).Fill(0x42);

        uint[] declared = declaredErasures ?? recorded.Erasures;
        int err = FecDecode(frame, recorded.UnitSize, stride, recorded.K, recorded.M,
            declared, declared.Length);
        if (err != (int)ChiakiError.Success)
            return false;

        // Only the k data units are checked. The parity units are the decoder's working space and
        // the stream never reads them back, which is why the recorded cases do not assert them.
        for (int i = 0; i < recorded.K; i++)
        {
            if (!frame.AsSpan(i * stride, recorded.UnitSize)
                    .SequenceEqual(recorded.FrameBuffer.AsSpan(i * recorded.UnitSize, recorded.UnitSize)))
                return false;
        }

        return true;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_fec_decode",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int FecDecode(
        byte[] frameBuf, int unitSize, int stride, uint k, uint m, uint[] erasures, int erasuresCount);
}
