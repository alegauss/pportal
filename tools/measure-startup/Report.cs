using System.Globalization;
using System.Text;

namespace MeasureStartup;

/// <summary>
/// Turns a list of runs into the three numbers §PP46 asks for, and into the JSON row.
///
/// This lives apart from <see cref="Program"/> for one reason: the summary is where a failed run can
/// become a fabricated number, and a step that can fabricate has to be assertable without launching
/// anything. <see cref="SelfTest"/> drives it with synthetic runs.
///
/// The rule the whole type exists to hold: <b>a run that did not produce a time contributes no
/// time</b>. Not a zero, not a substitute from a neighbouring run — nothing, and a reason.
/// </summary>
internal sealed record Summary(
    int RunsRequested,
    int RunsMeasured,
    bool ColdMeasured,
    string? ColdFailure,
    double ColdToWindowMs,
    double ColdToResponsiveMs,
    int WarmRuns,
    double WarmMedianMs,
    long WorkingSetMedianBytes,
    string WindowTitle);

/// <summary>
/// PP61: what the machine's file cache was doing when the numbers were taken.
///
/// The harness calls run 1 the cold start and it is one only on a machine that has not launched
/// this executable before. The OS file cache outlives the process, so after one launch the loader,
/// the plugins and the QML cache are resident and run 1 of the NEXT invocation is a warm start
/// wearing the cold label - 3771ms against 1218ms on the same build, with nothing in the report
/// saying which was which.
///
/// A figure that moves threefold with invisible machine state is not a measurement. Controlling the
/// variable means dropping the standby list, which needs elevation and is a decision rather than a
/// default; recording it costs nothing and is what this is. Two reports taken in different states
/// are then visibly incomparable instead of silently so.
///
/// A CLOSED set, because the value exists to be compared: free text would let two runs disagree
/// about what "cold" means and read as agreeing.
/// </summary>
internal enum CacheState
{
    /// <summary>Nobody said. The honest default, and the one a reader must not compare across.</summary>
    Unknown,

    /// <summary>The machine was rebooted and this is the first launch since.</summary>
    ColdBoot,

    /// <summary>The standby list was dropped before the first run, which needs elevation.</summary>
    Dropped,

    /// <summary>This executable has been launched before on this boot. Run 1 is warm.</summary>
    Warm,
}

internal static class Report
{
    /// <summary>The name a state is written under, and parsed back from.</summary>
    public static string Name(CacheState state) => state switch
    {
        CacheState.ColdBoot => "cold-boot",
        CacheState.Dropped => "dropped",
        CacheState.Warm => "warm",
        _ => "unknown",
    };

    /// <summary>
    /// A name back to a state. Anything unrecognised is <see cref="CacheState.Unknown"/> rather
    /// than an error: a caller that misspells it gets a report that refuses comparison, which is
    /// the same outcome as saying nothing and is better than no report at all.
    /// </summary>
    public static CacheState ParseCacheState(string? name) => name switch
    {
        "cold-boot" => CacheState.ColdBoot,
        "dropped" => CacheState.Dropped,
        "warm" => CacheState.Warm,
        _ => CacheState.Unknown,
    };

    /// <summary>
    /// Whether two reports were taken under conditions that can be compared at all.
    ///
    /// Unknown never compares - not even with another unknown, because two reports that both
    /// declined to say are not thereby in the same state. That is the whole point of the field.
    /// </summary>
    public static bool Comparable(CacheState a, CacheState b)
        => a != CacheState.Unknown && a == b;

    /// <summary>
    /// Run 1 is the cold one and the rest are warm. That split is <see cref="Program"/>'s reason for
    /// not taking a median over everything, and it is applied here by <i>position</i>, never by
    /// success: if run 1 failed there is no cold number, and run 2 is not promoted into the gap. Run 2
    /// is a warm start whatever happened before it, and reporting it as the cold figure would be the
    /// same lie as reporting zero, only harder to notice.
    /// </summary>
    public static Summary Summarise(IReadOnlyList<StartupResult> results)
    {
        var measured = results.Where(r => r.Failure is null).ToList();
        var warm = results.Skip(1).Where(r => r.Failure is null).ToList();

        StartupResult? cold = results.Count > 0 && results[0].Failure is null ? results[0] : null;

        return new Summary(
            RunsRequested: results.Count,
            RunsMeasured: measured.Count,
            ColdMeasured: cold is not null,
            ColdFailure: cold is not null ? null : (results.Count > 0 ? results[0].Failure : "no runs"),
            ColdToWindowMs: cold?.ToWindowMs ?? 0,
            ColdToResponsiveMs: cold?.ToResponsiveMs ?? 0,
            WarmRuns: warm.Count,
            WarmMedianMs: warm.Count > 0 ? Median(warm.Select(r => r.ToResponsiveMs)) : 0,
            WorkingSetMedianBytes: measured.Count > 0 ? (long)Median(measured.Select(r => (double)r.WorkingSetBytes)) : 0,
            // The title identifies what was timed, so it comes from the cold run when there is one and
            // is otherwise the first run that did show a window - never a blank standing in for both
            // "no window" and "an untitled one".
            WindowTitle: cold?.WindowTitle ?? (measured.Count > 0 ? measured[0].WindowTitle : ""));
    }

    /// <summary>
    /// <c>0</c> the before was taken · <c>2</c> measured, but no WebEngine in the tree so it is not the
    /// before · <c>1</c> the cold-start number is missing, which is the number this task exists to
    /// produce. A failed cold run outranks the WebEngine verdict: a row with no cold figure is not a
    /// measurement of a startup, whichever build it came from.
    /// </summary>
    public static int ExitCode(Summary s, bool webEnginePresent)
    {
        if (s.RunsMeasured == 0 || !s.ColdMeasured)
            return 1;
        return webEnginePresent ? 0 : 2;
    }

    public static double Median(IEnumerable<double> values)
    {
        double[] v = values.ToArray();
        Array.Sort(v);
        return v.Length == 0 ? 0 : v[(v.Length - 1) / 2];
    }

    /// <summary>
    /// The JSON row. A missing cold start is written as <c>null</c> with a <c>cold_failure</c> beside
    /// it, because <c>0.0</c> in a numeric field is a value and a reader — or a script — has no way to
    /// tell it from a startup that really took no time.
    /// </summary>
    public static string Json(
        string exe, string tree, TreeSize size, Summary s, string os, CacheState cache)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"task\":\"PP46\"");
        sb.Append($",\"exe\":\"{Esc(exe)}\"");
        sb.Append($",\"tree\":\"{Esc(tree)}\"");
        sb.Append($",\"tree_files\":{size.Files},\"tree_bytes\":{size.Bytes}");
        sb.Append($",\"webengine_present\":{Bool(size.WebEnginePresent)}");
        sb.Append($",\"webengine_bytes\":{size.WebEngineBytes}");
        sb.Append($",\"is_before_baseline\":{Bool(size.WebEnginePresent)}");
        sb.Append($",\"runs_requested\":{s.RunsRequested}");
        sb.Append($",\"runs_measured\":{s.RunsMeasured}");
        sb.Append($",\"cold_measured\":{Bool(s.ColdMeasured)}");
        sb.Append($",\"cold_to_window_ms\":{Num(s.ColdMeasured, s.ColdToWindowMs, c)}");
        sb.Append($",\"cold_to_responsive_ms\":{Num(s.ColdMeasured, s.ColdToResponsiveMs, c)}");
        if (!s.ColdMeasured)
            sb.Append($",\"cold_failure\":\"{Esc(s.ColdFailure ?? "unknown")}\"");
        sb.Append($",\"warm_runs\":{s.WarmRuns}");
        sb.Append($",\"warm_to_responsive_ms_median\":{Num(s.WarmRuns > 0, s.WarmMedianMs, c)}");
        sb.Append($",\"working_set_bytes_median\":{(s.RunsMeasured > 0 ? s.WorkingSetMedianBytes.ToString(c) : "null")}");
        sb.Append($",\"window_title\":\"{Esc(s.WindowTitle)}\"");
        sb.Append($",\"os\":\"{Esc(os)}\"");

        // PP61: always written, including when it is "unknown". A field that appeared only when
        // somebody set it would let an old report and an unstated one look alike, and the whole
        // reason this exists is that they are not.
        sb.Append($",\"cache_state\":\"{Name(cache)}\"");
        sb.Append($",\"cold_is_comparable\":{Bool(cache != CacheState.Unknown)}");
        sb.Append('}');
        sb.Append('\n');
        return sb.ToString();
    }

    private static string Bool(bool b) => b ? "true" : "false";

    private static string Num(bool have, double value, IFormatProvider c) =>
        have ? value.ToString("F1", c) : "null";

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
