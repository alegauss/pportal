using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP34: the two facts about thread.c that make <see cref="Waiting"/> a correct replacement
/// rather than a plausible one, read out of lib/ instead of remembered.
///
/// Both are the kind that a port gets wrong once and then cannot find. The first decides whether
/// `lock` is the right shape for a chiaki mutex at all; the second decides whether a bounded wait
/// stays bounded. Neither is visible from the managed side, and neither fails loudly.
/// </summary>
public static partial class ThreadSource
{
    /// <summary>Where the shim being deleted lives.</summary>
    public const string RelativePath = @"lib\src\thread.c";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether a chiaki mutex is reentrant whatever the caller asked for.
    ///
    /// This is what lets the port spell all 23 of them `lock`. On a pthreads build the `rec` flag
    /// would mean something and 22 call sites pass false; on Windows the implementation is a
    /// CRITICAL_SECTION, which is reentrant, and the flag is explicitly discarded. Should that
    /// stop being true - a Win32 mutex, an SRWLOCK, anything honouring the flag - then `lock` is
    /// silently more permissive than what it replaced, and this is what says so.
    /// </summary>
    public static bool MutexIsAlwaysRecursive(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains("InitializeCriticalSection", StringComparison.Ordinal)
            && DiscardsRecursiveFlagRegex().IsMatch(text);
    }

    /// <summary>
    /// Whether the predicate wait treats its timeout as a deadline: it subtracts the elapsed time
    /// from the next wait rather than passing the whole thing again.
    ///
    /// The failure this guards is not a crash. A rewrite that passes the full timeout on every
    /// turn produces a wait that is bounded per-wake and unbounded overall, which shows up only
    /// under contention and looks like a stall rather than like a bug in a timeout.
    /// </summary>
    public static bool TimeoutIsADeadline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return DeductsElapsedRegex().IsMatch(text) && text.Contains(
            "elapsed = chiaki_time_now_monotonic_ms() - start_time", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the predicate is tested before the deadline is - so a predicate that came true as
    /// the deadline expired is a success. It is the loop condition, and the timeout check sits
    /// inside the body after the wait, which is the ordering that decides it.
    /// </summary>
    public static bool PredicateBeatsTheDeadline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int loop = text.IndexOf("while(!check_pred(check_pred_user))", StringComparison.Ordinal);
        int expiry = text.IndexOf("if(elapsed >= timeout_ms)", StringComparison.Ordinal);
        return loop >= 0 && expiry > loop;
    }

    [GeneratedRegex(@"\(void\)\s*rec\s*;")]
    private static partial Regex DiscardsRecursiveFlagRegex();

    [GeneratedRegex(@"chiaki_cond_timedwait\(cond,\s*mutex,\s*timeout_ms\s*-\s*elapsed\)")]
    private static partial Regex DeductsElapsedRegex();
}
