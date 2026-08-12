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

internal static class Report
{
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
    public static string Json(string exe, string tree, TreeSize size, Summary s, string os)
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
        sb.Append('}');
        sb.Append('\n');
        return sb.ToString();
    }

    private static string Bool(bool b) => b ? "true" : "false";

    private static string Num(bool have, double value, IFormatProvider c) =>
        have ? value.ToString("F1", c) : "null";

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
