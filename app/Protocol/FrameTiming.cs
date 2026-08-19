namespace ChiakiNg.Protocol;

/// <summary>
/// PP23 and PP31: when a decoded frame is due, and for how long - in managed code.
///
/// Three fallback chains, and each exists because some stream does not carry the field above it.
/// Choosing wrong does not fail: it paces the picture wrong, which reads as stutter and gets blamed
/// on the network. So this is a transcription of chiaki_ffmpeg_frame_get_timing, with
/// <see cref="NativeFrameTiming"/> kept as the oracle and every combination of the three chains
/// compared against it.
///
/// The chains, in the order the C tries them:
///
///   the timebase is the PACKET's, then the decoder context's, then microseconds. The test for
///   "usable" is `num &lt;= 0 || den &lt;= 0`, so a rational with both parts negative - mathematically
///   positive - is rejected rather than normalised;
///
///   the timestamp is best_effort_timestamp, then pts, then zero. AV_NOPTS_VALUE is what selects
///   the next one, and it is INT64_MIN rather than a sentinel a port would invent;
///
///   and the duration is the frame's own if it is positive, otherwise one over the framerate,
///   otherwise one sixtieth. The frame's duration is scaled by the SAME timebase as the pts, which
///   is the part that would be easy to leave out - a duration in timebase units read as seconds is
///   a frame held on screen for a tenth of a second instead of a sixtieth.
/// </summary>
public static class FrameTiming
{
    /// <summary>
    /// AV_NOPTS_VALUE, which selects the next fallback. It is INT64_MIN, and it is read from ffmpeg
    /// through the shim rather than written down here - a constant a port assumed would be the one
    /// thing in this file with no oracle.
    /// </summary>
    public static long NoPts => NativeFrameTiming.NoPts;

    /// <summary>The timebase used when neither the packet's nor the context's is usable.</summary>
    public static (int Num, int Den) MicrosecondTimebase => (1, 1000000);

    /// <summary>The frame rate assumed when the stream does not state a usable one.</summary>
    public const double DefaultFps = 60.0;

    /// <summary>
    /// Whether a rational is one the C will use.
    ///
    /// Both parts strictly positive. So (-1, -1000) is refused even though it is the same number as
    /// (1, 1000): the C tests the fields, not the value, and a port that normalised the sign first
    /// would use a timebase the C falls back from.
    /// </summary>
    public static bool IsUsable((int Num, int Den) rational)
        => rational.Num > 0 && rational.Den > 0;

    /// <summary>av_q2d: the rational as a double, with no guard - the caller checks first.</summary>
    public static double ToDouble((int Num, int Den) rational)
        => (double)rational.Num / rational.Den;

    /// <summary>The presentation time and duration for a frame with these fields.</summary>
    /// <param name="bestEffortTimestamp">Tried first; <see cref="NoPts"/> falls through.</param>
    /// <param name="pts">Tried second; <see cref="NoPts"/> falls through to zero.</param>
    /// <param name="duration">The frame's own duration in timebase units, or 0 to fall back.</param>
    /// <param name="pktTimebase">Tried first.</param>
    /// <param name="ctxTimebase">Tried when the packet's is unusable.</param>
    /// <param name="framerate">Used for the duration only, when the frame carries none.</param>
    public static (double Pts, double Duration) Of(
        long bestEffortTimestamp,
        long pts,
        long duration,
        (int Num, int Den) pktTimebase,
        (int Num, int Den) ctxTimebase,
        (int Num, int Den) framerate)
    {
        (int Num, int Den) timebase = pktTimebase;
        if (!IsUsable(timebase))
            timebase = ctxTimebase;
        if (!IsUsable(timebase))
            timebase = MicrosecondTimebase;

        long noPts = NoPts;
        long chosen = bestEffortTimestamp;
        if (chosen == noPts)
            chosen = pts;
        if (chosen == noPts)
            chosen = 0;

        double outPts = ToDouble(timebase) * chosen;

        // The frame's own duration wins, scaled by the same timebase as the pts.
        if (duration > 0)
            return (outPts, ToDouble(timebase) * duration);

        // The framerate, or sixty. The C then re-tests `fps <= 0.0` and cannot ever take it: the
        // ternary above it only produces av_q2d of a rational whose parts are both positive, which
        // is positive. Not reproduced as a branch, because reproducing an unreachable one here
        // would be a line no assertion could fail without - and the test says it is unreachable.
        double fps = IsUsable(framerate) ? ToDouble(framerate) : DefaultFps;
        return (outPts, 1.0 / fps);
    }
}
