using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: test/sessionbaseline.c's percentile cases, run from the managed side.
///
/// The audit that found the allocation budget (PP176) also found this: seventeen cases in that file
/// and the port reached three of the properties they cover. The percentile was not one of them,
/// because the baseline handle exposes an AVERAGE and nothing else - so the port could only report
/// the number the C's own header warns about.
///
/// These are the C's cases and its numbers, not new ones. Where the C asserts a range rather than a
/// value, so does this: the answer is a bucket bound and pinning it to an exact microsecond would
/// be asserting the histogram's spacing rather than its promise.
/// </summary>
public class BaselineStatTests
{
    /// <summary>Nothing sampled is zero, and not the fastest number in the record.</summary>
    [Fact]
    public void AnEmptyStatIsZeroRatherThanFast()
    {
        using var stat = new BaselineStat();

        Assert.Equal(0ul, stat.Samples);
        Assert.Equal(0ul, stat.P99Us);
        Assert.Equal(0ul, stat.AverageUs);
    }

    /// <summary>
    /// Below the linear cutoff there is a bucket per microsecond, so no bound is involved and the
    /// answer is the sample itself.
    /// </summary>
    [Fact]
    public void BelowTheCutoffTheAnswerIsExact()
    {
        using var stat = new BaselineStat();

        stat.Push(5);

        Assert.Equal(5ul, stat.P99Us);
    }

    /// <summary>
    /// The case the statistic exists for: 990 frames at 1ms and 10 at 100ms. The mean is 1990us,
    /// the maximum is 100000us, and the true p99 is 1000us - so reading the mean overstates the
    /// typical frame by two and understates the worst by fifty, in one number.
    /// </summary>
    [Fact]
    public void TheMeanLiesInBothDirectionsAndThePercentileDoesNot()
    {
        using var stat = new BaselineStat();

        for (int i = 0; i < 990; i++)
            stat.Push(1000);
        for (int i = 0; i < 10; i++)
            stat.Push(100000);

        Assert.Equal(1000ul, stat.Samples);
        Assert.Equal(1000ul, stat.MinimumUs);
        Assert.Equal(100000ul, stat.MaximumUs);
        Assert.Equal(1990ul, stat.AverageUs);

        ulong p99 = stat.P99Us;

        // A bound: never below the true p99, and inside the 12.5% the eight-to-the-octave spacing
        // promises.
        Assert.True(p99 >= 1000, $"p99 was {p99}, below the true 1000");
        Assert.True(p99 < 1125, $"p99 was {p99}, outside the bucket's 12.5%");
        Assert.True(p99 < stat.MaximumUs);

        // And the mean is ABOVE it, which is the whole finding: the average is dragged by ten
        // frames in a thousand and the percentile is not.
        Assert.True(p99 < stat.AverageUs, $"p99 {p99} was not below the mean {stat.AverageUs}");
    }

    /// <summary>
    /// A sample past the last bucket is clamped to the observed maximum rather than to the top of
    /// the histogram, so a five-second stall reads as five seconds.
    /// </summary>
    [Fact]
    public void PastTheLastBucketTheAnswerIsTheMaximum()
    {
        using var stat = new BaselineStat();

        stat.Push(5_000_000);

        Assert.Equal(5_000_000ul, stat.P99Us);
    }

    /// <summary>
    /// And it stays a bound over a mixed distribution: 99 fast frames and one five-second stall
    /// puts the ninety-ninth percentile below a millisecond, not near the stall.
    /// </summary>
    [Fact]
    public void OneStallInAHundredDoesNotMoveThePercentile()
    {
        using var stat = new BaselineStat();

        for (int i = 0; i < 99; i++)
            stat.Push(800);
        stat.Push(5_000_000);

        Assert.True(stat.P99Us < 1000, $"p99 was {stat.P99Us}");
        Assert.Equal(5_000_000ul, stat.MaximumUs);
    }

    /// <summary>
    /// The two named percentiles are the general one at 50 and 99, so they cannot drift from it.
    /// </summary>
    [Fact]
    public void TheNamedPercentilesAreTheGeneralOne()
    {
        using var stat = new BaselineStat();

        for (int i = 0; i < 990; i++)
            stat.Push(1000);
        for (int i = 0; i < 10; i++)
            stat.Push(100000);

        Assert.Equal(stat.PercentileUs(50), stat.P50Us);
        Assert.Equal(stat.PercentileUs(99), stat.P99Us);
    }

    /// <summary>
    /// A fresh statistic is zeroed. Asserted because the shim allocates it rather than the caller,
    /// and an uninitialised histogram would answer with whatever was on the heap - which is a
    /// plausible number rather than an obviously wrong one.
    /// </summary>
    [Fact]
    public void AFreshStatIsZeroedRatherThanWhateverWasThere()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            using var stat = new BaselineStat();

            Assert.Equal(0ul, stat.Samples);
            Assert.Equal(0ul, stat.MinimumUs);
            Assert.Equal(0ul, stat.MaximumUs);
            Assert.Equal(0ul, stat.P50Us);
        }
    }
}
