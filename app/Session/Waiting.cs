using System.Diagnostics;

namespace ChiakiNg.Session;

/// <summary>
/// PP34: what is left of thread.c after it is deleted rather than translated.
///
/// thread.c is 270 lines putting one API over pthreads and Win32 - threads, mutexes, condition
/// variables. In managed code most of it has no reason to exist, and reproducing it faithfully
/// would be the clearest sign the port was mechanical. The mapping is almost all identity:
///
///   chiaki_thread_create / join / timedjoin  ->  Thread, Thread.Join(timeout)
///   chiaki_thread_set_name                   ->  Thread.Name
///   chiaki_mutex_*                           ->  lock
///   chiaki_cond_signal / broadcast           ->  Monitor.Pulse / PulseAll
///   chiaki_cond_wait / timedwait             ->  Monitor.Wait / Wait(timeout)
///
/// The mutex row is exact and that is not obvious. chiaki_mutex_init takes a `rec` flag and 22 of
/// the 23 call sites in lib/ pass false, which on pthreads would mean a mutex that must not be
/// re-entered - and .NET's lock always may be, so the port would be quietly more permissive than
/// the thing it replaces. On Windows it is not: thread.c's mutex is a CRITICAL_SECTION and the
/// flag is discarded with `(void)rec; // always recursive`. This project is Windows-only, so
/// every one of those 23 is already reentrant and lock matches it. ThreadSource holds that.
///
/// What does need writing is the one row above that is not identity: the predicate waits, which
/// lib/ uses 29 times and which Monitor has no equivalent of. Two details in the C are easy to
/// lose and neither fails loudly:
///
///   the timeout is a DEADLINE, not a per-wait budget. chiaki_cond_timedwait_pred subtracts the
///   elapsed time on each turn of the loop, so a caller waiting 200ms waits 200ms in total
///   however many times it is woken. A rewrite that passed the full timeout to each wait turns a
///   bounded wait into an unbounded one, and only under contention;
///
///   and the predicate is checked BEFORE the deadline. A predicate that became true at the same
///   moment the deadline expired is a success, not a timeout.
/// </summary>
public static class Waiting
{
    /// <summary>
    /// Waits for <paramref name="predicate"/> under <paramref name="monitor"/>, for no longer than
    /// <paramref name="timeout"/> in total. True when the predicate held, false on the deadline.
    ///
    /// The caller must already hold <paramref name="monitor"/>, exactly as the C requires the
    /// mutex to be held - Monitor.Wait releases it for the duration and reacquires before
    /// returning, which is the whole reason a condition variable takes one.
    /// </summary>
    public static bool Until(object monitor, Func<bool> predicate, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(predicate);

        // Monotonic, and not DateTime: the C reads chiaki_time_now_monotonic_ms for this, and a
        // wall clock that steps backwards over a wait is a timeout that never fires.
        long deadlineTicks = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

        while (!predicate())
        {
            long remaining = deadlineTicks - Stopwatch.GetTimestamp();
            if (remaining <= 0)
                return false;

            // Monitor.Wait returning false is the timeout, but it is not the answer: a spurious
            // wake and a real one are indistinguishable here, so the predicate is what decides
            // and the loop re-reads it either way. Same shape as the C's while(!check_pred).
            Monitor.Wait(monitor, TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency));
        }

        return true;
    }

    /// <summary>
    /// The unbounded form, for the call sites that pass no timeout. Separate rather than a
    /// Timeout.InfiniteTimeSpan default, because an infinite deadline is not a very long one and
    /// the arithmetic above would overflow trying to express it.
    /// </summary>
    public static void Until(object monitor, Func<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(predicate);

        while (!predicate())
            Monitor.Wait(monitor);
    }
}
