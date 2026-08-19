using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: chiaki_ffmpeg_frame_get_timing itself, reachable so <see cref="FrameTiming"/> can be held
/// against it.
///
/// The AVFrame stays on the C side. It is ffmpeg's struct, so it is exactly what .NET must not be
/// handed, and the two rationals are four ints with no padding to disagree about.
///
/// `duration` is a parameter here and was not always. The wrapper used to allocate a frame and pass
/// it straight on, which left frame->duration at av_frame_alloc's zero - so the branch that uses a
/// frame's own duration could not be reached through the oracle at all, and half the duration
/// fallback chain had nothing to be checked against.
/// </summary>
public static class NativeFrameTiming
{
    /// <summary>AV_NOPTS_VALUE: a timestamp that is absent, which is what selects the next fallback.</summary>
    public static long NoPts => FfmpegNoPts();

    /// <summary>The presentation time and duration ffmpegdecoder.c computes for these fields.</summary>
    public static (double Pts, double Duration) Of(
        long bestEffortTimestamp,
        long pts,
        long duration,
        (int Num, int Den) pktTimebase,
        (int Num, int Den) ctxTimebase,
        (int Num, int Den) framerate)
    {
        if (!FfmpegFrameTiming(bestEffortTimestamp, pts, duration,
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
        long bestEffortTimestamp, long pts, long duration,
        int pktNum, int pktDen, int ctxNum, int ctxDen, int frNum, int frDen,
        out double ptsOut, out double durationOut);
}
