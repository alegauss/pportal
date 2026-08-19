using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23 and PP31: when a decoded frame is due, and for how long.
///
/// Three fallbacks, and each exists because some stream does not carry the field above it: the
/// best-effort timestamp or the raw one, the packet timebase or the decoder context's, the
/// framerate or a default. Choosing wrong does not fail - it paces the picture wrong, which reads
/// as stutter and gets blamed on the network.
///
/// The AVFrame stays on the C side. It is ffmpeg's struct, so it is exactly what .NET must not be
/// handed, and the two rationals are four ints with no padding to disagree about.
/// </summary>
public static class FrameTiming
{
    /// <summary>AV_NOPTS_VALUE: a timestamp that is absent, which is what selects the next fallback.</summary>
    public static long NoPts => FfmpegNoPts();

    /// <summary>
    /// The presentation time and duration for a frame with these timestamps.
    /// </summary>
    /// <param name="pktTimebase">The packet's timebase, as numerator and denominator.</param>
    /// <param name="ctxTimebase">The decoder context's, used only when the packet's is invalid.</param>
    /// <param name="framerate">Used for the duration; a zero denominator falls back to a default.</param>
    public static (double Pts, double Duration) Of(
        long bestEffortTimestamp,
        long pts,
        (int Num, int Den) pktTimebase,
        (int Num, int Den) ctxTimebase,
        (int Num, int Den) framerate)
    {
        if (!FfmpegFrameTiming(bestEffortTimestamp, pts,
                pktTimebase.Num, pktTimebase.Den, ctxTimebase.Num, ctxTimebase.Den,
                framerate.Num, framerate.Den, out double ptsOut, out double durationOut))
        {
            throw new OutOfMemoryException("av_frame_alloc failed.");
        }

        return (ptsOut, durationOut);
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_ffmpeg_nopts",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern long FfmpegNoPts();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_ffmpeg_frame_timing",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool FfmpegFrameTiming(
        long bestEffortTimestamp, long pts,
        int pktNum, int pktDen, int ctxNum, int ctxDen, int frNum, int frDen,
        out double ptsOut, out double durationOut);
}
