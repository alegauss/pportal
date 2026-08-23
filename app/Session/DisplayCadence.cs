namespace ChiakiNg.Session;

/// <summary>
/// PP321: how a tick of the buffered pacer ended, which is four answers and not two.
/// </summary>
public enum CadenceStep
{
    /// <summary>The pace thread is already armed at an interval that has not changed. Nothing to do.</summary>
    HeldByArmedThread,

    /// <summary>The renderer is busy. Armed at the deadline WITHOUT advancing it - the catch-up waits.</summary>
    Deferred,

    /// <summary>The deadline has not arrived. Armed at it.</summary>
    Waiting,

    /// <summary>The deadline passed. Advanced by whole intervals, and an update is owed.</summary>
    Due,
}

/// <summary>
/// PP321, PP11's half that is neither fullscreen nor HDR: what the window does about the display's
/// refresh rate.
///
/// "Refresh-rate switching" reads like a mode the client sets on the panel. In this window it is
/// the opposite direction: the display's rate is an INPUT, read fresh on every tick, and what
/// switches is the cadence the buffered pacer runs at. A window dragged to a 144Hz screen, or a
/// panel that drops to 60 on battery, changes the answer between two consecutive ticks and nothing
/// signals it - which is why every rule below is about surviving that change mid-stream.
///
/// 1. THE RATE HAS A FLOOR AND A FALLBACK, TWICE. `screen()` can be null and `refreshRate()` can
///    return 0, and both sites in the window spell the same guard: no screen, or a rate at or below
///    1.0, means 60. Without it the interval is an infinity or a division by zero, and the pacer
///    either never fires or spins.
///
/// 2. THE REFRESH ONLY WINS WHEN THE MIXER OWNS PLAYBACK. `stream_active && frame_mixer_active`.
///    Otherwise the interval is the stream's own, from the configured max FPS. A port that always
///    paced to the display would present a 30fps stream sixty times a second, and one that never
///    did would drop interpolated frames the mixer generated to fill the panel.
///
/// 3. A CHANGED INTERVAL RESETS THE ANCHOR. The deadline is zeroed and re-anchored, because the old
///    one was a multiple of the old interval and keeping it makes the first tick at the new rate
///    land at an arbitrary phase.
///
/// 4. AND IT IS THE ONE THING THAT UNSTICKS AN ARMED PACE THREAD. The early return that skips a
///    tick when the thread is already armed is conditioned on the interval NOT having changed. Drop
///    that condition and the switch in point 3 is computed and then never applied, so the stream
///    keeps the old display's cadence until something else happens to disarm the thread. This is
///    the switching, and it is one `&&`.
///
/// 5. THE CATCH-UP IS WHOLE MULTIPLES, NOT NOW. A missed deadline advances by
///    `elapsed / interval + 1` intervals rather than being set to now plus one, so the cadence keeps
///    the phase it had. Resetting to now is the port that turns one late frame into a permanent
///    offset from the vblank.
///
/// 6. A BUSY RENDERER ARMS WITHOUT ADVANCING. It returns before the catch-up, so the deadline it
///    arms at is the one already missed - the next tick does the advancing. A port that folded the
///    two branches together would advance the deadline for work that has not been done.
/// </summary>
public sealed class DisplayCadence
{
    /// <summary>What a missing screen, or a rate of zero, counts as.</summary>
    public const double FallbackRefreshHz = 60.0;

    /// <summary>What a max FPS of zero counts as. Not 1000/60 - the window writes the rounded literal.</summary>
    public const double FallbackFrameIntervalMs = 16.6667;

    /// <summary>The rate at or below which the fallback takes over, which is 1.0 and not 0.0.</summary>
    public const double MinimumUsableRefreshHz = 1.0;

    /// <summary>The stream's own interval, from the configured max FPS. See <see cref="SetStreamMaxFps"/>.</summary>
    public double StreamFrameIntervalMs { get; private set; } = FallbackFrameIntervalMs;

    /// <summary>The interval the last tick ran at. Zero before the first one.</summary>
    public long IntervalUs { get; private set; }

    /// <summary>Whether the last tick saw a different interval from the one before it.</summary>
    public bool IntervalChanged { get; private set; }

    /// <summary>When the next update is due. Zero means unanchored, which is what a change produces.</summary>
    public long NextUpdateUs { get; private set; }

    /// <summary>How many whole intervals the last <see cref="CadenceStep.Due"/> advanced by.</summary>
    public long MissedIntervals { get; private set; }

    /// <summary>
    /// setStreamMaxFPS. Zero is not a rate, and the window spells its fallback as a rounded literal
    /// rather than as 1000.0/60.0 - which is 16.6667 and not 16.666666..., a difference of 33ns per
    /// frame that only matters because a test asserting the wrong one passes for a year.
    /// </summary>
    public void SetStreamMaxFps(uint maxFps) =>
        StreamFrameIntervalMs = maxFps > 0 ? 1000.0 / maxFps : FallbackFrameIntervalMs;

    /// <summary>Point 1: the rate, with the floor and the fallback both sites apply.</summary>
    public static double RefreshHz(double? screenRefreshHz) =>
        screenRefreshHz is double hz && hz > MinimumUsableRefreshHz ? hz : FallbackRefreshHz;

    /// <summary>Point 2: whether the display's rate is the one that paces, or the stream's.</summary>
    public static bool TimerOwnsPlayback(bool streamActive, bool mixerActive) =>
        streamActive && mixerActive;

    /// <summary>The interval in milliseconds, which is the refresh only under point 2.</summary>
    public double PacingIntervalMs(bool timerOwnedPlayback, double? screenRefreshHz) =>
        timerOwnedPlayback ? 1000.0 / RefreshHz(screenRefreshHz) : StreamFrameIntervalMs;

    /// <summary>
    /// And in microseconds, clamped so it is never zero. The clamp is on the MILLISECONDS - a
    /// thousand-hertz display gets 1ms and not 1us, which is the difference between a pacer that
    /// is fast and one that is a spin loop.
    /// </summary>
    public static long ToIntervalUs(double intervalMs) => (long)(Math.Max(1.0, intervalMs) * 1000.0);

    /// <summary>
    /// One tick of the buffered pacer, in the window's own order: interval, change, anchor, the
    /// armed-thread skip, the busy-renderer defer, then the deadline.
    ///
    /// The order is not cosmetic. The anchor reset happens BEFORE the armed-thread skip, so a tick
    /// that returns early has still recorded the new interval - and the tick after it does not see
    /// a change that has already been consumed.
    /// </summary>
    /// <param name="nowUs">The monotonic clock.</param>
    /// <param name="lastSwapReturnUs">When the last present returned, which is the anchor when there is one.</param>
    /// <param name="streamActive">Whether a session is streaming.</param>
    /// <param name="mixerActive">Whether the frame mixer is generating frames.</param>
    /// <param name="paceThreadArmed">Whether the pace thread already holds a deadline.</param>
    /// <param name="renderBusy">Whether the renderer is mid-frame.</param>
    /// <param name="screenRefreshHz">The display's rate, or null when there is no screen.</param>
    public CadenceStep Tick(
        long nowUs,
        long lastSwapReturnUs,
        bool streamActive,
        bool mixerActive,
        bool paceThreadArmed,
        bool renderBusy,
        double? screenRefreshHz)
    {
        bool timerOwnedPlayback = TimerOwnsPlayback(streamActive, mixerActive);
        long intervalUs = ToIntervalUs(PacingIntervalMs(timerOwnedPlayback, screenRefreshHz));

        // Point 3. The change is measured against the last tick's interval, so the very first tick
        // counts as a change and anchors rather than inheriting a zero deadline.
        IntervalChanged = IntervalUs != intervalUs;
        if (IntervalChanged)
        {
            NextUpdateUs = 0;
            IntervalUs = intervalUs;
        }

        if (NextUpdateUs == 0)
        {
            long anchorUs = lastSwapReturnUs > 0 ? lastSwapReturnUs : nowUs;
            NextUpdateUs = anchorUs + intervalUs;
        }

        MissedIntervals = 0;

        // Point 4, and the whole of the switching: the skip is off while the interval is changing.
        if (timerOwnedPlayback && paceThreadArmed && !IntervalChanged)
            return CadenceStep.HeldByArmedThread;

        // Point 6. Before the deadline check, so a busy renderer never advances it.
        if (timerOwnedPlayback && renderBusy)
            return CadenceStep.Deferred;

        if (nowUs >= NextUpdateUs)
        {
            // Point 5. The C is guarded for a negative elapsed that this branch cannot produce; the
            // guard is kept as the max because dropping it is how a port discovers it was reachable.
            long elapsedUs = nowUs - NextUpdateUs;
            MissedIntervals = elapsedUs >= 0 ? (elapsedUs / intervalUs) + 1 : 1;
            NextUpdateUs += MissedIntervals * intervalUs;
            return CadenceStep.Due;
        }

        return CadenceStep.Waiting;
    }

    /// <summary>How long the pace thread waits, which is never below one microsecond.</summary>
    public long WaitUs(long nowUs) => Math.Max(1, NextUpdateUs - nowUs);
}

/// <summary>
/// PP321: the cadence rules where the Qt window states them, beside PP11's fullscreen ones.
/// </summary>
public static class DisplayCadenceSource
{
    /// <summary>The window, which is the same file the fullscreen rules are read from.</summary>
    public const string WindowCpp = StreamWindowSource.WindowCpp;

    /// <summary>The window, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(WindowCpp);

    /// <summary>
    /// Whether the rate still falls back to 60 at BOTH sites.
    ///
    /// Counted rather than contained: one site is the pacer's interval and the other is libplacebo's
    /// vsync duration, and a port that fixed the fallback in one of them would pass a `Contains`.
    /// </summary>
    public static bool TheRefreshRateStillFallsBackToSixtyAtBothSites(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);

        const string read = "double refresh_rate = screen() ? screen()->refreshRate() : 60.0;";
        const string floor = "if (refresh_rate <= 1.0)";

        return Occurrences(cpp, read) >= 2 && Occurrences(cpp, floor) >= 2;
    }

    /// <summary>Whether a max FPS of zero still means the rounded 16.6667 rather than 1000.0/60.0.</summary>
    public static bool AZeroMaxFpsStillMeansTheRoundedLiteral(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("max_fps > 0 ? 1000.0 / max_fps : 16.6667", StringComparison.Ordinal);
    }

    /// <summary>Whether the display's rate still paces only while the mixer owns playback.</summary>
    public static bool TheRefreshPacesOnlyWhenTheMixerOwnsPlayback(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("return stream_active && schedule_frame_mixer_active != 0;", StringComparison.Ordinal)
            && cpp.Contains("interval_ms = 1000.0 / refresh_rate;", StringComparison.Ordinal);
    }

    /// <summary>Whether a changed interval still zeroes the deadline instead of keeping it.</summary>
    public static bool AChangedIntervalStillResetsTheAnchor(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);

        int start = cpp.IndexOf("if (buffered_interval_changed) {", StringComparison.Ordinal);
        if (start < 0)
            return false;

        int end = cpp.IndexOf("if (next_buffered_update_us == 0) {", start, StringComparison.Ordinal);
        if (end < 0)
            return false;

        return cpp[start..end].Contains("next_buffered_update_us = 0;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the armed-thread skip is still conditioned on the interval holding, which is the
    /// single `&&` that makes a rate change take effect at all.
    /// </summary>
    public static bool AnArmedPaceThreadIsStillSkippedOnlyWhileTheIntervalHolds(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
            "if (timer_owned_playback && pace_thread_active && !buffered_interval_changed)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether a missed deadline still advances by whole intervals rather than to now.</summary>
    public static bool AMissedDeadlineStillAdvancesByWholeIntervals(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("(elapsed_us / interval_us) + 1", StringComparison.Ordinal)
            && cpp.Contains("next_buffered_update_us += missed_intervals * interval_us;", StringComparison.Ordinal);
    }

    /// <summary>Whether the anchor is still the last present's return rather than always now.</summary>
    public static bool TheAnchorIsStillTheLastSwapReturn(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains(
            "const qint64 anchor_us = last_swap_return_us > 0 ? last_swap_return_us : now_us;",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the interval is still clamped in milliseconds before it becomes microseconds.</summary>
    public static bool TheIntervalIsStillClampedToAMillisecond(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("qMax(1.0, interval_ms) * 1000.0", StringComparison.Ordinal);
    }

    /// <summary>Whether a busy renderer still arms and returns before the deadline is advanced.</summary>
    public static bool ABusyRendererStillArmsWithoutAdvancing(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);

        int start = cpp.IndexOf("if (timer_owned_playback && render_busy_now) {", StringComparison.Ordinal);
        if (start < 0)
            return false;

        int end = cpp.IndexOf("if (now_us >= next_buffered_update_us) {", start, StringComparison.Ordinal);
        if (end < 0)
            return false;

        // The arm and the return, and no advance between them.
        string branch = cpp[start..end];
        return branch.Contains("buffered_pace_thread->arm(next_buffered_update_us);", StringComparison.Ordinal)
            && branch.Contains("return;", StringComparison.Ordinal)
            && !branch.Contains("next_buffered_update_us +=", StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0;
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
