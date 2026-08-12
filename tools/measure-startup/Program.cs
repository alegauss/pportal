using System.Globalization;
using System.Text;

namespace MeasureStartup;

/// <summary>
/// PP46: cold start, build size and idle working set — the two or three numbers most likely to be
/// quoted in a release note, and therefore the ones most likely to be quoted without being measured.
///
/// Usage:
///   measure-startup --exe &lt;path-to-exe&gt; [--tree &lt;dir&gt;] [--runs 3]
///                   [--timeout-ms 60000] [--settle-ms 3000] [--out report.json]
///   measure-startup --self-test
///
/// The tree defaults to the exe's own directory. Every run reports whether QtWebEngine is present in
/// that tree, because a row taken without it is not the "before" this task is about — it is a
/// measurement of a build that already dropped the thing being measured.
/// </summary>
internal static class Program
{
    private static int Main(string[] argv)
    {
        if (argv.Length == 1 && argv[0] == "--self-test")
            return SelfTest.Run();

        var a = Args.Parse(argv);
        if (a is null)
        {
            Console.Error.WriteLine("usage: measure-startup --exe <path> [--tree <dir>] [--runs N] [--timeout-ms N] [--settle-ms N] [--out FILE]");
            Console.Error.WriteLine("       measure-startup --self-test");
            return 1;
        }

        string tree = a.Tree ?? Path.GetDirectoryName(Path.GetFullPath(a.Exe))!;
        TreeSize size;
        try
        {
            size = Tree.Measure(tree);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cannot measure tree: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"build   : {Path.GetFullPath(a.Exe)}");
        Console.WriteLine($"tree    : {tree}");
        Console.WriteLine($"size    : {size.Megabytes.ToString("N1", CultureInfo.InvariantCulture)} MB in {size.Files} files");
        Console.WriteLine(size.WebEnginePresent
            ? $"webengine: PRESENT, {size.WebEngineMegabytes.ToString("N1", CultureInfo.InvariantCulture)} MB of it"
            : "webengine: ABSENT - this is NOT the \"before\" PP46 asks for (see README)");
        Console.WriteLine();

        var results = new List<StartupResult>();
        for (int i = 0; i < a.Runs; i++)
        {
            StartupResult r = Probe.Run(a.Exe, a.TimeoutMs, a.SettleMs);
            results.Add(r);
            string kind = i == 0 ? "cold" : "warm";
            Console.WriteLine(r.Failure is null
                ? $"run {i + 1} ({kind}): window {r.ToWindowMs,7:F0} ms   responsive {r.ToResponsiveMs,7:F0} ms   " +
                  $"working set {r.WorkingSetBytes / 1024.0 / 1024.0,7:F1} MB   window=\"{r.WindowTitle}\""
                : $"run {i + 1} ({kind}): FAILED - {r.Failure}");
        }

        var ok = results.Where(r => r.Failure is null).ToList();
        Console.WriteLine();
        if (ok.Count == 0)
        {
            Console.Error.WriteLine("no run produced a measurement; nothing written");
            return 1;
        }

        // Only the first run is genuinely cold: after it the loader, the Qt plugins and the QML cache
        // are in the file cache, so runs 2..N measure a warm start. They are reported apart because
        // "cold start" is what §PP46 asks for and a median over all runs is not that number - it is
        // mostly warm starts with one cold one dragged into the middle.
        StartupResult cold = results[0];
        var warm = ok.Skip(1).ToList();
        double warmMedian = warm.Count > 0 ? Median(warm.Select(r => r.ToResponsiveMs)) : 0;
        double medianWs = Median(ok.Select(r => (double)r.WorkingSetBytes));

        Console.WriteLine(cold.Failure is null
            ? $"cold    : responsive {cold.ToResponsiveMs:F0} ms   (first run, nothing in the file cache)"
            : $"cold    : FAILED - {cold.Failure}");
        Console.WriteLine(warm.Count > 0
            ? $"warm    : responsive {warmMedian:F0} ms   median of {warm.Count} later run(s)"
            : "warm    : not measured (single run)");
        Console.WriteLine($"idle    : working set {medianWs / 1024.0 / 1024.0:F1} MB   median of {ok.Count} run(s)");

        if (a.Out is not null)
        {
            File.WriteAllText(a.Out, Json(a, tree, size, ok, cold, warmMedian, medianWs));
            Console.WriteLine($"report  : {Path.GetFullPath(a.Out)}");
        }

        // Exit 2 when the tree has no WebEngine: the numbers are real but they are not the before,
        // and a caller scripting this should be able to tell without reading the prose.
        return size.WebEnginePresent ? 0 : 2;
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] v = values.ToArray();
        Array.Sort(v);
        return v.Length == 0 ? 0 : v[(v.Length - 1) / 2];
    }

    private static string Json(Args a, string tree, TreeSize size, List<StartupResult> ok,
        StartupResult cold, double warmMedian, double ws)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"task\":\"PP46\"");
        sb.Append($",\"exe\":\"{Esc(Path.GetFullPath(a.Exe))}\"");
        sb.Append($",\"tree\":\"{Esc(tree)}\"");
        sb.Append($",\"tree_files\":{size.Files},\"tree_bytes\":{size.Bytes}");
        sb.Append($",\"webengine_present\":{(size.WebEnginePresent ? "true" : "false")}");
        sb.Append($",\"webengine_bytes\":{size.WebEngineBytes}");
        sb.Append($",\"is_before_baseline\":{(size.WebEnginePresent ? "true" : "false")}");
        sb.Append($",\"runs\":{ok.Count}");
        sb.Append($",\"cold_to_window_ms\":{cold.ToWindowMs.ToString("F1", c)}");
        sb.Append($",\"cold_to_responsive_ms\":{cold.ToResponsiveMs.ToString("F1", c)}");
        sb.Append($",\"warm_to_responsive_ms_median\":{warmMedian.ToString("F1", c)}");
        sb.Append($",\"working_set_bytes_median\":{(long)ws}");
        sb.Append($",\"window_title\":\"{Esc(cold.WindowTitle)}\"");
        sb.Append($",\"os\":\"{Esc(Environment.OSVersion.VersionString)}\"");
        sb.Append('}');
        sb.Append('\n');
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

internal sealed record Args(string Exe, string? Tree, int Runs, int TimeoutMs, int SettleMs, string? Out)
{
    public static Args? Parse(string[] a)
    {
        string exe = "", tree = "", outFile = "";
        int runs = 3, timeout = 60000, settle = 3000;
        for (int i = 0; i < a.Length - 1; i++)
        {
            switch (a[i])
            {
                case "--exe": exe = a[++i]; break;
                case "--tree": tree = a[++i]; break;
                case "--runs": runs = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--timeout-ms": timeout = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--settle-ms": settle = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--out": outFile = a[++i]; break;
            }
        }
        return exe.Length == 0
            ? null
            : new Args(exe, tree.Length == 0 ? null : tree, runs, timeout, settle,
                outFile.Length == 0 ? null : outFile);
    }
}
