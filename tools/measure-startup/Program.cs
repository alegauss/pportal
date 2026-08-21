using System.Globalization;

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
            Console.Error.WriteLine("usage: measure-startup --exe <path> [--tree <dir>] [--runs N] [--timeout-ms N] [--settle-ms N] [--out FILE] [--cache-state S]");
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

        // Only the first run is genuinely cold: after it the loader, the Qt plugins and the QML cache
        // are in the file cache, so runs 2..N measure a warm start. They are reported apart because
        // "cold start" is what §PP46 asks for and a median over all runs is not that number - it is
        // mostly warm starts with one cold one dragged into the middle.
        Summary s = Report.Summarise(results);

        Console.WriteLine();
        if (s.RunsMeasured == 0)
        {
            Console.Error.WriteLine("no run produced a measurement; nothing written");
            return 1;
        }

        Console.WriteLine(s.ColdMeasured
            ? $"cold    : responsive {s.ColdToResponsiveMs:F0} ms   (first run, cache {Report.Name(a.Cache)})"
            : $"cold    : NOT MEASURED - {s.ColdFailure}");
        Console.WriteLine(s.WarmRuns > 0
            ? $"warm    : responsive {s.WarmMedianMs:F0} ms   median of {s.WarmRuns} later run(s)"
            : $"warm    : not measured ({(a.Runs > 1 ? "every later run failed" : "single run")})");
        // PP61: said out loud rather than left to whoever reads the JSON. A cold number taken in
        // an unstated cache state moves threefold between invocations, and the person most likely
        // to quote it is the one running this now.
        if (a.Cache == CacheState.Unknown)
            Console.WriteLine("warning : cache state not stated - this cold figure compares with nothing");

        Console.WriteLine($"idle    : working set {s.WorkingSetMedianBytes / 1024.0 / 1024.0:F1} MB   median of {s.RunsMeasured} run(s)");

        if (a.Out is not null)
        {
            File.WriteAllText(a.Out, Report.Json(Path.GetFullPath(a.Exe), tree, size, s,
                Environment.OSVersion.VersionString, a.Cache));
            Console.WriteLine($"report  : {Path.GetFullPath(a.Out)}");
        }

        // A failed run 1 leaves the headline number missing, and the row says so rather than carrying
        // a zero. Said again on stderr because the tree size and the warm figure are still there and
        // a report that looks complete is how the zero would have been quoted.
        if (!s.ColdMeasured)
            Console.Error.WriteLine("no cold-start number: run 1 failed, and the report says null rather than 0");

        // Otherwise exit 2 when the tree has no WebEngine: the numbers are real but they are not the
        // before, and a caller scripting this should be able to tell without reading the prose.
        return Report.ExitCode(s, size.WebEnginePresent);
    }
}

internal sealed record Args(
    string Exe, string? Tree, int Runs, int TimeoutMs, int SettleMs, string? Out,
    CacheState Cache)
{
    public static Args? Parse(string[] a)
    {
        string exe = "", tree = "", outFile = "", cache = "";
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
                // PP61: the one condition the harness cannot observe for itself.
                case "--cache-state": cache = a[++i]; break;
            }
        }
        return exe.Length == 0
            ? null
            : new Args(exe, tree.Length == 0 ? null : tree, runs, timeout, settle,
                outFile.Length == 0 ? null : outFile, Report.ParseCacheState(cache));
    }
}
