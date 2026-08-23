using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP321, the refresh-rate half of PP11: the display's rate arriving as an input on every tick
/// rather than a mode the client sets.
/// </summary>
public class DisplayCadenceTests
{
    /// <summary>
    /// No screen, and a rate of zero, both mean 60. Either one reaching the division is an infinite
    /// interval or a division by zero, and the window guards both at both of its sites.
    /// </summary>
    [Theory]
    [InlineData(null, 60.0)]
    [InlineData(0.0, 60.0)]
    [InlineData(1.0, 60.0)]
    [InlineData(0.5, 60.0)]
    [InlineData(144.0, 144.0)]
    [InlineData(59.94, 59.94)]
    public void TheRateHasAFloorAndAFallback(double? screen, double expected)
    {
        Assert.Equal(expected, DisplayCadence.RefreshHz(screen), 3);
    }

    /// <summary>
    /// The floor is 1.0 and not 0.0, so a rate of exactly 1.0 is refused. A one-hertz display is
    /// not a display, and the guard the window writes is `<=` rather than `<`.
    /// </summary>
    [Fact]
    public void TheFloorIsAtOneNotBelowIt()
    {
        Assert.Equal(60.0, DisplayCadence.RefreshHz(1.0), 3);
        Assert.Equal(1.001, DisplayCadence.RefreshHz(1.001), 3);
    }

    /// <summary>
    /// A max FPS of zero means the window's rounded literal. 16.6667 and not 1000.0/60.0 - the
    /// difference is 33 nanoseconds a frame and the point is that it is the window's number.
    /// </summary>
    [Fact]
    public void AZeroMaxFpsMeansTheRoundedLiteral()
    {
        var cadence = new DisplayCadence();

        cadence.SetStreamMaxFps(30);
        Assert.Equal(1000.0 / 30.0, cadence.StreamFrameIntervalMs, 6);

        cadence.SetStreamMaxFps(0);
        Assert.Equal(16.6667, cadence.StreamFrameIntervalMs, 6);
        Assert.NotEqual(1000.0 / 60.0, cadence.StreamFrameIntervalMs, 6);
    }

    /// <summary>
    /// The display's rate paces only while the mixer owns playback. Without a mixer the stream's
    /// own interval wins even on a 144Hz panel - which is what stops a 30fps stream being presented
    /// sixty times a second with nothing new in it.
    /// </summary>
    [Fact]
    public void TheRefreshPacesOnlyWhenTheMixerOwnsPlayback()
    {
        var cadence = new DisplayCadence();
        cadence.SetStreamMaxFps(30);

        Assert.False(DisplayCadence.TimerOwnsPlayback(streamActive: true, mixerActive: false));
        Assert.False(DisplayCadence.TimerOwnsPlayback(streamActive: false, mixerActive: true));
        Assert.True(DisplayCadence.TimerOwnsPlayback(streamActive: true, mixerActive: true));

        Assert.Equal(1000.0 / 144.0, cadence.PacingIntervalMs(true, 144.0), 6);
        Assert.Equal(1000.0 / 30.0, cadence.PacingIntervalMs(false, 144.0), 6);
    }

    /// <summary>
    /// The clamp is on the milliseconds. A thousand-hertz interval becomes 1ms and not 1us, so the
    /// pacer is fast rather than a spin loop.
    /// </summary>
    [Fact]
    public void TheIntervalIsClampedToAMillisecondNotToAMicrosecond()
    {
        Assert.Equal(1000, DisplayCadence.ToIntervalUs(0.1));
        Assert.Equal(1000, DisplayCadence.ToIntervalUs(0.0));
        Assert.Equal(16666, DisplayCadence.ToIntervalUs(1000.0 / 60.0));
    }

    /// <summary>
    /// The first tick anchors on the last present's return rather than on now, so the cadence starts
    /// in phase with the display instead of with whatever call happened to arrive.
    /// </summary>
    [Fact]
    public void TheFirstTickAnchorsOnTheLastSwapReturn()
    {
        var cadence = new DisplayCadence();

        CadenceStep step = cadence.Tick(
            nowUs: 1_000_000,
            lastSwapReturnUs: 990_000,
            streamActive: true,
            mixerActive: true,
            paceThreadArmed: false,
            renderBusy: false,
            screenRefreshHz: 60.0);

        // The very first tick counts as a change - the interval it compares against is zero - so it
        // anchors rather than inheriting a deadline of zero and firing immediately.
        Assert.True(cadence.IntervalChanged);
        Assert.Equal(16_666, cadence.IntervalUs);
        Assert.Equal(990_000 + 16_666, cadence.NextUpdateUs);
        Assert.Equal(CadenceStep.Waiting, step);
    }

    /// <summary>
    /// An unarmed tick before the deadline waits, and the deadline is the anchor plus one interval -
    /// the last present's return, not now.
    /// </summary>
    [Fact]
    public void ATickBeforeTheDeadlineWaitsAtTheAnchoredTime()
    {
        var cadence = new DisplayCadence();

        CadenceStep step = cadence.Tick(
            nowUs: 1_000_000,
            lastSwapReturnUs: 995_000,
            streamActive: true,
            mixerActive: true,
            paceThreadArmed: false,
            renderBusy: false,
            screenRefreshHz: 60.0);

        Assert.Equal(CadenceStep.Waiting, step);
        Assert.Equal(995_000 + 16_666, cadence.NextUpdateUs);
        Assert.Equal(11_666, cadence.WaitUs(1_000_000));
    }

    /// <summary>And with no present yet, the anchor is now.</summary>
    [Fact]
    public void WithNoPresentYetTheAnchorIsNow()
    {
        var cadence = new DisplayCadence();

        cadence.Tick(1_000_000, 0, true, true, false, false, 60.0);

        Assert.Equal(1_000_000 + 16_666, cadence.NextUpdateUs);
    }

    /// <summary>
    /// The skip: an armed pace thread at an unchanged interval means this tick has nothing to do.
    /// </summary>
    [Fact]
    public void AnArmedPaceThreadHoldsATickWhoseIntervalHasNotChanged()
    {
        var cadence = new DisplayCadence();
        cadence.Tick(1_000_000, 0, true, true, false, false, 60.0);
        long deadline = cadence.NextUpdateUs;

        CadenceStep step = cadence.Tick(1_005_000, 0, true, true, paceThreadArmed: true, renderBusy: false, 60.0);

        Assert.Equal(CadenceStep.HeldByArmedThread, step);
        Assert.Equal(deadline, cadence.NextUpdateUs);
    }

    /// <summary>
    /// And the switching itself: the SAME armed thread does NOT hold the tick when the display's
    /// rate changed. Drop that condition and the new interval is computed and never applied - the
    /// stream keeps the old display's cadence until something unrelated disarms the thread.
    /// </summary>
    [Fact]
    public void AChangedRefreshRateGetsPastTheArmedPaceThread()
    {
        var cadence = new DisplayCadence();
        cadence.Tick(1_000_000, 0, true, true, false, false, 60.0);
        Assert.Equal(16_666, cadence.IntervalUs);

        // Dragged onto a 144Hz panel, with the pace thread armed at the 60Hz deadline.
        CadenceStep step = cadence.Tick(1_005_000, 0, true, true, paceThreadArmed: true, renderBusy: false, 144.0);

        Assert.NotEqual(CadenceStep.HeldByArmedThread, step);
        Assert.True(cadence.IntervalChanged);
        Assert.Equal(6944, cadence.IntervalUs);
        Assert.Equal(1_005_000 + 6_944, cadence.NextUpdateUs);
    }

    /// <summary>
    /// The reset is a re-anchor and not a keep. The old deadline was a multiple of the old interval,
    /// so carrying it over lands the first tick at the new rate on an arbitrary phase.
    /// </summary>
    [Fact]
    public void AChangedIntervalReanchorsRatherThanKeepingTheOldDeadline()
    {
        var cadence = new DisplayCadence();
        cadence.Tick(1_000_000, 0, true, true, false, false, 60.0);
        long sixtyHzDeadline = cadence.NextUpdateUs;

        cadence.Tick(1_002_000, 0, true, true, false, false, 144.0);

        Assert.NotEqual(sixtyHzDeadline, cadence.NextUpdateUs);
        Assert.Equal(1_002_000 + 6_944, cadence.NextUpdateUs);
    }

    /// <summary>
    /// A missed deadline advances by whole intervals, so the cadence keeps its phase. Setting it to
    /// now plus one interval is the port that turns a single late frame into a permanent offset
    /// from the vblank.
    /// </summary>
    [Fact]
    public void AMissedDeadlineAdvancesByWholeIntervals()
    {
        var cadence = new DisplayCadence();
        cadence.Tick(1_000_000, 0, true, true, false, false, 60.0);
        long deadline = cadence.NextUpdateUs;

        // Three and a half intervals late.
        long lateUs = deadline + (16_666 * 3) + 8_000;
        CadenceStep step = cadence.Tick(lateUs, 0, true, true, false, false, 60.0);

        Assert.Equal(CadenceStep.Due, step);
        Assert.Equal(4, cadence.MissedIntervals);
        Assert.Equal(deadline + (16_666 * 4), cadence.NextUpdateUs);

        // The phase survived: the new deadline is still an exact multiple of the interval from the
        // original anchor, which is what "now + interval" would have destroyed.
        Assert.Equal(0, (cadence.NextUpdateUs - deadline) % 16_666);
        Assert.NotEqual(lateUs + 16_666, cadence.NextUpdateUs);
    }

    /// <summary>Exactly on the deadline is due, and advances by exactly one.</summary>
    [Fact]
    public void ATickExactlyOnTheDeadlineIsDueAndAdvancesByOne()
    {
        var cadence = new DisplayCadence();
        cadence.Tick(1_000_000, 0, true, true, false, false, 60.0);
        long deadline = cadence.NextUpdateUs;

        CadenceStep step = cadence.Tick(deadline, 0, true, true, false, false, 60.0);

        Assert.Equal(CadenceStep.Due, step);
        Assert.Equal(1, cadence.MissedIntervals);
        Assert.Equal(deadline + 16_666, cadence.NextUpdateUs);
    }

    /// <summary>
    /// A busy renderer arms at the deadline it already missed and does NOT advance it. Folding this
    /// branch into the one below would advance the cadence for work that has not been done.
    /// </summary>
    [Fact]
    public void ABusyRendererArmsWithoutAdvancingTheDeadline()
    {
        var cadence = new DisplayCadence();
        cadence.Tick(1_000_000, 0, true, true, false, false, 60.0);
        long deadline = cadence.NextUpdateUs;

        CadenceStep step = cadence.Tick(deadline + 5_000, 0, true, true, false, renderBusy: true, 60.0);

        Assert.Equal(CadenceStep.Deferred, step);
        Assert.Equal(deadline, cadence.NextUpdateUs);
        Assert.Equal(0, cadence.MissedIntervals);

        // And the next tick, with the renderer free, does the advancing the busy one skipped.
        Assert.Equal(CadenceStep.Due, cadence.Tick(deadline + 5_000, 0, true, true, false, false, 60.0));
        Assert.Equal(deadline + 16_666, cadence.NextUpdateUs);
    }

    /// <summary>
    /// Neither early return applies when the mixer does not own playback: both are guarded on
    /// timer_owned_playback, so a stream-paced tick reaches the deadline check armed or busy.
    /// </summary>
    [Fact]
    public void WithoutTheMixerNeitherEarlyReturnApplies()
    {
        var cadence = new DisplayCadence();
        cadence.SetStreamMaxFps(60);
        cadence.Tick(1_000_000, 0, streamActive: true, mixerActive: false, false, false, 144.0);
        long deadline = cadence.NextUpdateUs;

        CadenceStep step = cadence.Tick(
            deadline, 0, streamActive: true, mixerActive: false, paceThreadArmed: true, renderBusy: true, 144.0);

        Assert.Equal(CadenceStep.Due, step);
    }

    /// <summary>
    /// The wait never goes below a microsecond, including when the deadline is already behind - the
    /// window's own qMax, and an arm at zero or a negative is not a wait.
    /// </summary>
    [Fact]
    public void TheWaitNeverGoesBelowAMicrosecond()
    {
        var cadence = new DisplayCadence();
        cadence.Tick(1_000_000, 0, true, true, false, false, 60.0);

        Assert.Equal(1, cadence.WaitUs(cadence.NextUpdateUs));
        Assert.Equal(1, cadence.WaitUs(cadence.NextUpdateUs + 500_000));
    }

    /// <summary>Every rule above, still stated the same way in the Qt window.</summary>
    [Fact]
    public void TheCadenceRulesAreStillTheQtWindows()
    {
        string? path = DisplayCadenceSource.Locate();
        if (path is null)
            return;

        string cpp = File.ReadAllText(path);

        Assert.True(DisplayCadenceSource.TheRefreshRateStillFallsBackToSixtyAtBothSites(cpp), "both sites");
        Assert.True(DisplayCadenceSource.AZeroMaxFpsStillMeansTheRoundedLiteral(cpp), "the rounded literal");
        Assert.True(DisplayCadenceSource.TheRefreshPacesOnlyWhenTheMixerOwnsPlayback(cpp), "only with the mixer");
        Assert.True(DisplayCadenceSource.AChangedIntervalStillResetsTheAnchor(cpp), "the reset");
        Assert.True(
            DisplayCadenceSource.AnArmedPaceThreadIsStillSkippedOnlyWhileTheIntervalHolds(cpp),
            "the one && that makes a rate change take effect");
        Assert.True(DisplayCadenceSource.AMissedDeadlineStillAdvancesByWholeIntervals(cpp), "whole multiples");
        Assert.True(DisplayCadenceSource.TheAnchorIsStillTheLastSwapReturn(cpp), "the anchor");
        Assert.True(DisplayCadenceSource.TheIntervalIsStillClampedToAMillisecond(cpp), "the clamp");
        Assert.True(DisplayCadenceSource.ABusyRendererStillArmsWithoutAdvancing(cpp), "arm without advancing");
    }
}
