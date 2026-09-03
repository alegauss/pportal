using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP645: the committed runs whose tail is noise are held to it, and the one whose tail is the
/// finding is not.
///
/// PP49 chose which of six runs to commit by looking at whether its p99 sat near its p50. That
/// judgement was nowhere in the tree, so a re-run produced whatever the machine was doing that
/// minute and nothing said which of the two it was. This is that judgement, written down.
/// </summary>
public class SpikeRunQualityTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every bound run reads as a measurement, which is what makes it committable.
    ///
    /// This is the check the discipline needs to be mechanical: take a run, copy it over the
    /// release-*.json beside it, and the gate says whether it was a reading or an afternoon of the
    /// machine being busy. Nothing about the run is trusted - the ratio is recomputed from the p50
    /// and p99 the file itself records.
    /// </summary>
    [Fact]
    public void EveryBoundRunReadsAsAMeasurement()
    {
        int checkedRuns = 0;

        foreach (string relative in SpikeRunQuality.BoundRuns)
        {
            if (SpikeRunQuality.Locate(relative) is not { } path)
                continue;

            checkedRuns++;
            string json = File.ReadAllText(path);

            IReadOnlyList<SpikeRunQuality.Series> series = SpikeRunQuality.SeriesIn(json);
            Assert.NotEmpty(series);

            foreach (SpikeRunQuality.Series one in series)
                output.WriteLine($"{relative} {one.Name}: p50={one.P50} p99={one.P99} tail={one.Tail:F2}");

            IReadOnlyList<SpikeRunQuality.Series> bad = SpikeRunQuality.ContaminatedIn(json);
            Assert.True(
                bad.Count == 0,
                $"{relative} has {bad.Count} side(s) whose p99 is more than "
                    + $"{SpikeRunQuality.TailLimit}x its p50 - {string.Join(", ", bad.Select(b => $"{b.Name} at {b.Tail:F2}"))} "
                    + "- so the number its README quotes is the machine being busy rather than the "
                    + "measurement, and the run should be re-taken rather than the limit raised");
        }

        // PP271's shape: a list nothing resolved would satisfy the loop above by finding nothing.
        if (SpikeRunQuality.Locate(SpikeRunQuality.BoundRuns[0]) is not null)
            Assert.Equal(SpikeRunQuality.BoundRuns.Count, checkedRuns);
    }

    /// <summary>
    /// And every excluded run WOULD fail it, which is the whole reason the list is a list.
    ///
    /// Asserted rather than commented, because an exclusion nobody checks is indistinguishable from
    /// one somebody forgot to remove. PP65's result is a 103us median send against a 26990us p99,
    /// and PP32's opus comparison found a managed decoder whose p99 is five times its own median in
    /// every run taken - the tail IS the finding in both, so a rule applied to every spike in the
    /// tree would reject two measurements that shipped. If one ever stops failing the limit, that
    /// spike's numbers changed and its line should be re-read.
    /// </summary>
    [Fact]
    public void EveryExcludedRunIsExcludedBecauseItsTailIsTheFinding()
    {
        int checkedRuns = 0;

        foreach (string relative in SpikeRunQuality.Excluded)
        {
            if (SpikeRunQuality.Locate(relative) is not { } path)
                continue;

            checkedRuns++;
            IReadOnlyList<SpikeRunQuality.Series> bad =
                SpikeRunQuality.ContaminatedIn(File.ReadAllText(path));

            output.WriteLine($"{relative}: {bad.Count} side(s) over the limit");
            Assert.True(
                bad.Count > 0,
                $"{relative} is excluded from the limit and would now pass it, so the reason it was "
                    + "excluded no longer holds and the spike's own line should be re-read");

            Assert.DoesNotContain(relative, SpikeRunQuality.BoundRuns);
        }

        if (SpikeRunQuality.Locate(SpikeRunQuality.Excluded[0]) is not null)
            Assert.Equal(SpikeRunQuality.Excluded.Count, checkedRuns);
    }

    /// <summary>
    /// PP651, for PP32: the opus run's two sides, and which of them the limit is about.
    ///
    /// The managed decoder costs more per frame and jitters far more, and neither is near a budget:
    /// a frame is 10 ms and both medians are tens of microseconds. So cost decides nothing here,
    /// which is the same shape PP49 found - and what is left to decide on is the dependency and the
    /// audio, neither of which is a clock.
    ///
    /// The three claims are asserted against the committed run rather than restated from the README,
    /// because the README is where a number goes stale and the JSON is where it was taken.
    /// </summary>
    [Fact]
    public void TheOpusRunsManagedSideIsTheSlowAndJitteryOne()
    {
        if (SpikeRunQuality.Locate(@"spike\opus-decode\release-managed-vs-native.json") is not { } path)
            return;

        IReadOnlyList<SpikeRunQuality.Series> series =
            SpikeRunQuality.SeriesIn(File.ReadAllText(path));

        SpikeRunQuality.Series native = series.Single(s => s.Name == "native_us");
        SpikeRunQuality.Series managed = series.Single(s => s.Name == "managed_us");

        output.WriteLine($"native p50={native.P50} tail={native.Tail:F2}");
        output.WriteLine($"managed p50={managed.P50} tail={managed.Tail:F2}");

        Assert.True(managed.P50 > native.P50, "the managed decoder is no longer the slower one");

        // A 480-sample frame at 48 kHz is 10 ms. Both sides are two orders of magnitude inside it,
        // which is the claim that makes this a decision about something other than speed.
        Assert.True(
            managed.P99 < 10_000.0 / 2,
            $"the managed decoder's p99 is {managed.P99}us against a 10000us frame, so cost has "
                + "started to decide this after all and PP32's criterion should be re-read");

        // And the tail is the finding, which is why the run is excluded from the limit.
        Assert.True(managed.Contaminated, "the managed side's tail no longer exceeds the limit");
        Assert.False(native.Contaminated, "the native side's tail now exceeds the limit too");
    }

    /// <summary>
    /// The limit separates the runs it was measured from, which is the only justification it has.
    ///
    /// The four clean sides and the six contaminated ones, as PP49 observed them. A limit that fell
    /// outside the gap between 1.19 and 1.87 would be picked rather than measured, and this is what
    /// says so - both directions, because a limit of zero would pass the second half alone.
    /// </summary>
    [Theory]
    [InlineData(1.03, false)]
    [InlineData(1.05, false)]
    [InlineData(1.07, false)]
    [InlineData(1.19, false)]
    [InlineData(1.87, true)]
    [InlineData(2.53, true)]
    [InlineData(2.86, true)]
    [InlineData(3.16, true)]
    [InlineData(3.33, true)]
    [InlineData(3.38, true)]
    public void TheLimitSitsInTheGapItWasMeasuredFrom(double tail, bool contaminated)
    {
        // p50 of 100 makes p99 the ratio in microseconds, so the arithmetic under test is the
        // division and not the fixture.
        var series = new SpikeRunQuality.Series("measured", 100.0, 100.0 * tail);

        Assert.Equal(contaminated, series.Contaminated);
    }

    /// <summary>
    /// A series with no samples reads as no tail, not as an infinite one.
    ///
    /// A spike that pushed nothing writes p50 and p99 of zero, and a division there would make an
    /// empty run the most contaminated thing in the tree - which would send somebody re-taking a
    /// run whose real problem is that it measured nothing.
    /// </summary>
    [Fact]
    public void AnEmptySeriesIsNotInfinitelyContaminated()
    {
        var empty = new SpikeRunQuality.Series("nothing", 0.0, 0.0);

        Assert.Equal(0.0, empty.Tail);
        Assert.False(empty.Contaminated);
    }

    /// <summary>
    /// The series are found by SHAPE, so a spike naming its timings something new is still read.
    ///
    /// Each of the three spikes names them differently and decode-path nests them per decoder. A
    /// reader built from a list of known names would find nothing in the fourth spike and report a
    /// clean run, which is the failure mode this whole file exists to stop.
    /// </summary>
    [Fact]
    public void ATimingSeriesIsFoundWhereverItSits()
    {
        const string json = """
            {"nested":{"deep":{"whatever_us":{"samples":2,"p50_us":10.0,"p99_us":40.0}}},
             "plain_us":{"samples":2,"p50_us":10.0,"p99_us":11.0},
             "not_a_series":{"p50_us":10.0}}
            """;

        IReadOnlyList<SpikeRunQuality.Series> series = SpikeRunQuality.SeriesIn(json);

        Assert.Equal(2, series.Count);
        Assert.Contains(series, s => s.Name == "whatever_us" && s.Contaminated);
        Assert.Contains(series, s => s.Name == "plain_us" && !s.Contaminated);
    }
}
