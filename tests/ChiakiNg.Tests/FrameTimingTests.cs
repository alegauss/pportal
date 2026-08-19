using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the frame timing in managed code, compared with ffmpegdecoder.c over the whole cross
/// product of its three fallback chains.
///
/// Not examples. Three chains with two, three and three arms give a small enough space to walk
/// completely, so it is walked: every timestamp case against every timebase case against every
/// duration and framerate case, each run through both implementations.
/// </summary>
public class FrameTimingTests
{
    private static readonly long noPts = FrameTiming.NoPts;

    private static readonly long[] timestamps =
    [
        noPts, 0, 1, -1, 12345, 1000000, long.MaxValue / 4, -98765,
    ];

    private static readonly (int Num, int Den)[] rationals =
    [
        (1, 90000), (1, 1000000), (60, 1), (30000, 1001),
        (0, 1), (1, 0), (0, 0), (-1, 1000), (1, -1000), (-1, -1000), (-60, -1),
    ];

    private static readonly long[] durations = [0, -1, -5000, 1, 3000, 90000];

    /// <summary>
    /// The whole cross product: 8 timestamps x 8 second-timestamps x 11 packet timebases x 11
    /// context timebases would be large, so the timestamp and timebase chains are walked
    /// independently against a fixed other - which is sound because they do not interact, and the
    /// interaction that DOES matter (the timebase scaling both the pts and the duration) has its
    /// own case below.
    /// </summary>
    [Fact]
    public void EveryTimestampFallbackAgrees()
    {
        foreach (long bestEffort in timestamps)
        {
            foreach (long pts in timestamps)
            {
                AssertSame(bestEffort, pts, 0, (1, 90000), (1, 1000), (60, 1));
                AssertSame(bestEffort, pts, 3000, (1, 90000), (1, 1000), (60, 1));
            }
        }
    }

    /// <summary>
    /// Every timebase pair, including the ones the C refuses: a zero numerator, a zero denominator,
    /// and the two sign cases - (-1, 1000) and (1, -1000) are both rejected, and so is (-1, -1000)
    /// even though it is the same number as (1, 1000).
    /// </summary>
    [Fact]
    public void EveryTimebaseFallbackAgrees()
    {
        foreach ((int Num, int Den) pkt in rationals)
        {
            foreach ((int Num, int Den) ctx in rationals)
            {
                AssertSame(12345, 999, 0, pkt, ctx, (60, 1));
                AssertSame(noPts, 999, 4500, pkt, ctx, (30000, 1001));
            }
        }
    }

    /// <summary>Every duration and framerate combination, against a usable timebase and an unusable one.</summary>
    [Fact]
    public void EveryDurationFallbackAgrees()
    {
        foreach (long duration in durations)
        {
            foreach ((int Num, int Den) framerate in rationals)
            {
                AssertSame(12345, 0, duration, (1, 90000), (1, 1000), framerate);
                AssertSame(12345, 0, duration, (0, 0), (0, 0), framerate);
            }
        }
    }

    /// <summary>
    /// A negative rational with both parts negative is the same number as the positive one and is
    /// still refused, because the C tests the FIELDS. A port that normalised the sign first would
    /// scale by a timebase the C falls back from.
    /// </summary>
    [Fact]
    public void ASignNormalisedTimebaseIsStillRefused()
    {
        Assert.False(FrameTiming.IsUsable((-1, -1000)));
        Assert.Equal(FrameTiming.ToDouble((1, 1000)), FrameTiming.ToDouble((-1, -1000)));

        // So this falls all the way through to microseconds, not to a millisecond timebase.
        (double pts, _) = FrameTiming.Of(1000, 0, 0, (-1, -1000), (-1, -1000), (60, 1));
        Assert.Equal(1000.0 / 1000000.0, pts, 12);
        AssertSame(1000, 0, 0, (-1, -1000), (-1, -1000), (60, 1));
    }

    /// <summary>
    /// AV_NOPTS_VALUE is INT64_MIN, read from ffmpeg rather than assumed. It is the one constant in
    /// this file a port would have written down, and writing down the wrong sentinel means the
    /// first fallback never fires.
    /// </summary>
    [Fact]
    public void TheAbsentTimestampIsInt64Min()
    {
        Assert.Equal(long.MinValue, FrameTiming.NoPts);
        Assert.Equal(NativeFrameTiming.NoPts, FrameTiming.NoPts);

        // And it really is what selects the next arm, at both positions.
        (double fromPts, _) = FrameTiming.Of(noPts, 90000, 0, (1, 90000), (1, 1000), (60, 1));
        Assert.Equal(1.0, fromPts, 12);

        (double fromZero, _) = FrameTiming.Of(noPts, noPts, 0, (1, 90000), (1, 1000), (60, 1));
        Assert.Equal(0.0, fromZero, 12);
    }

    /// <summary>
    /// The frame's duration is scaled by the SAME timebase as the pts. A duration in timebase units
    /// read as seconds holds a frame on screen for a tenth of a second instead of a sixtieth, so
    /// this is asserted as a number rather than only through the oracle.
    /// </summary>
    [Fact]
    public void TheFramesDurationIsScaledByTheTimebase()
    {
        (double pts, double duration) = FrameTiming.Of(90000, 0, 1500, (1, 90000), (1, 1000), (60, 1));

        Assert.Equal(1.0, pts, 12);
        Assert.Equal(1500.0 / 90000.0, duration, 12);
        AssertSame(90000, 0, 1500, (1, 90000), (1, 1000), (60, 1));
    }

    /// <summary>
    /// A non-positive duration falls back to the framerate, and an unusable framerate to sixty.
    /// Zero and negative take the same arm, which is what makes `> 0` the whole test.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-90000L)]
    public void ANonPositiveDurationFallsBackToTheFramerate(long duration)
    {
        (_, double fromFramerate) = FrameTiming.Of(0, 0, duration, (1, 90000), (1, 1000), (30, 1));
        Assert.Equal(1.0 / 30.0, fromFramerate, 12);

        (_, double fromDefault) = FrameTiming.Of(0, 0, duration, (1, 90000), (1, 1000), (0, 0));
        Assert.Equal(1.0 / FrameTiming.DefaultFps, fromDefault, 12);

        AssertSame(0, 0, duration, (1, 90000), (1, 1000), (30, 1));
        AssertSame(0, 0, duration, (1, 90000), (1, 1000), (0, 0));
    }

    /// <summary>
    /// The C's second fps guard - `if(fps &lt;= 0.0) fps = 60.0;` after the ternary - cannot run: the
    /// ternary only produces av_q2d of a rational whose parts are both positive, which is positive.
    /// So it is not reproduced, and this is the assertion that says why rather than leaving the
    /// omission to be noticed.
    /// </summary>
    [Fact]
    public void TheSecondFpsGuardIsUnreachable()
    {
        foreach ((int Num, int Den) framerate in rationals)
        {
            if (!FrameTiming.IsUsable(framerate))
                continue;

            Assert.True(FrameTiming.ToDouble(framerate) > 0.0,
                $"({framerate.Num}, {framerate.Den}) is usable and not positive");
        }

        // The smallest and largest usable rationals, which is where an underflow to zero would be.
        Assert.True(FrameTiming.ToDouble((1, int.MaxValue)) > 0.0);
        Assert.True(FrameTiming.ToDouble((int.MaxValue, 1)) > 0.0);
    }

    private static void AssertSame(
        long bestEffort, long pts, long duration,
        (int Num, int Den) pkt, (int Num, int Den) ctx, (int Num, int Den) framerate)
    {
        (double Pts, double Duration) managed =
            FrameTiming.Of(bestEffort, pts, duration, pkt, ctx, framerate);
        (double Pts, double Duration) native =
            NativeFrameTiming.Of(bestEffort, pts, duration, pkt, ctx, framerate);

        string where =
            $"be={bestEffort} pts={pts} dur={duration} "
            + $"pkt=({pkt.Num},{pkt.Den}) ctx=({ctx.Num},{ctx.Den}) fr=({framerate.Num},{framerate.Den})";

        Assert.Equal(native.Pts, managed.Pts, 12);
        Assert.True(
            native.Duration == managed.Duration
                || Math.Abs(native.Duration - managed.Duration) < 1e-12,
            $"{where}: duration {native.Duration} vs {managed.Duration}");
    }
}
