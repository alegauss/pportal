namespace MeasureStartup;

/// <summary>
/// The assertions this harness ships with. The one that matters is the first: a process that never
/// shows a window must be reported as having shown none, not as having started in 0 ms. A cold-start
/// number of zero is the most quotable wrong answer this tool could produce.
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        int failures = 0;

        // 1. A process that stays alive and shows no window must be reported as having shown none,
        // not as having started in 0 ms. ping is used rather than cmd because cmd exits the instant
        // its stdin reads EOF, which exercises the "exited early" path instead of this one - a fault
        // injected into the no-window branch went undetected until this was fixed.
        string ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE");
        StartupResult headless = Probe.Run(ping, timeoutMs: 1200, idleSettleMs: 0, arguments: "-n 6 127.0.0.1");
        failures += Expect(headless.Failure is null || !headless.Failure.Contains("exited early"),
            $"the no-window case must not be reached via early exit, got: {headless.Failure}");
        failures += Expect(!headless.WindowAppeared, "a process with no visible window must report none");
        failures += Expect(headless.Failure is not null, "no-window must be a failure, not a measurement");
        failures += Expect(headless.ToResponsiveMs == 0, $"no-window must not report a time, got {headless.ToResponsiveMs}");

        // 2. A missing exe is refused rather than timed.
        StartupResult missing = Probe.Run(Path.Combine(Path.GetTempPath(), "no-such-build-xyz.exe"), 1000, 0);
        failures += Expect(missing.Failure is not null && missing.Failure.Contains("not found"),
            "a missing exe must be reported as not found");

        // 3. WebEngine detection: the whole honesty of a row depends on this boolean, so it is
        // checked against a tree built to have it and a tree built not to.
        string dir = Path.Combine(Path.GetTempPath(), "pp46-selftest-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "chiaki.exe"), new byte[1024]);
            TreeSize without = Tree.Measure(dir);
            failures += Expect(!without.WebEnginePresent, "a tree with no Chromium must report absent");
            failures += Expect(without.Files == 1 && without.Bytes == 1024, "tree size must count the files it saw");

            File.WriteAllBytes(Path.Combine(dir, "QtWebEngineCore.dll"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(dir, "icudtl.dat"), new byte[4096]);
            TreeSize with = Tree.Measure(dir);
            failures += Expect(with.WebEnginePresent, "a tree containing Chromium must report present");
            failures += Expect(with.WebEngineBytes == 2048 + 4096,
                $"Chromium's share must be counted, got {with.WebEngineBytes}");
            failures += Expect(with.Bytes == 1024 + 2048 + 4096, "the total must include everything");

            // The resource pak matters as much as the DLL: matching only the DLL would undercount.
            failures += Expect(Tree.IsWebEngine("qtwebengine_resources.pak"), "the resource pak must count as WebEngine");
            failures += Expect(!Tree.IsWebEngine("Qt6Quick.dll"), "an ordinary Qt DLL must not count as WebEngine");

            // 4. The summary, driven with synthetic runs. Probe refusing to invent a zero is only half
            // the guarantee: the report is assembled from the runs afterwards, and that is where a
            // failed run 1 used to become "cold_to_responsive_ms":0.0. Observed against the real
            // build with --timeout-ms 1235, which is above the warm time and below the cold one, so
            // run 1 timed out and runs 2 and 3 did not.
            failures += SummaryChecks(with);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp dir */ }
        }

        Console.WriteLine(failures == 0 ? "OK self-test passed" : $"{failures} self-test check(s) failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// The report path, with the runs handed in rather than launched. Everything here is about one
    /// failure: a run that produced no time must contribute no time, and must not have its slot filled
    /// by a zero or by the next run along.
    /// </summary>
    private static int SummaryChecks(TreeSize tree)
    {
        int failures = 0;
        const string why = "no visible top-level window within 1235ms";

        StartupResult Ok(double ms, long ws) => new(true, ms - 20, ms, ws, ws, null, "chiaki-ng");

        // Run 1 timed out; runs 2 and 3 did not. This is the shape observed on the real build.
        var coldFailed = new List<StartupResult>
        {
            StartupResult.Failed(why), Ok(1169.4, 454_819_840), Ok(1117.4, 455_000_000),
        };
        Summary s = Report.Summarise(coldFailed);
        string json = Report.Json("x.exe", "tree", tree, s, "test-os", CacheState.Warm);

        failures += Expect(!s.ColdMeasured, "a failed run 1 must leave the cold start unmeasured");
        failures += Expect(json.Contains("\"cold_to_responsive_ms\":null"),
            $"a failed run 1 must serialise a null cold start, got: {json}");
        failures += Expect(!json.Contains("\"cold_to_responsive_ms\":0"),
            "a failed run 1 must never serialise a cold start of 0 ms");
        failures += Expect(!json.Contains("\"cold_to_window_ms\":0"),
            "a failed run 1 must never serialise a window time of 0 ms");
        failures += Expect(json.Contains($"\"cold_failure\":\"{why}\""),
            "the report must carry the reason the cold start is missing");
        failures += Expect(Report.ExitCode(s, webEnginePresent: true) == 1,
            "a missing cold start must exit 1 even on a build that has WebEngine");

        // Both later runs are warm and both succeeded, so both count. Skipping over the *successful*
        // runs instead of over run 1 silently dropped one of them from the median.
        failures += Expect(s.WarmRuns == 2, $"both later successful runs must be warm, got {s.WarmRuns}");
        failures += Expect(s.RunsMeasured == 2, $"two runs were measured, got {s.RunsMeasured}");
        failures += Expect(s.RunsRequested == 3, $"three runs were requested, got {s.RunsRequested}");

        // Run 2 is a warm start whatever happened to run 1, so it must not be promoted into the gap.
        failures += Expect(s.ColdToResponsiveMs != 1169.4, "run 2 must not stand in as the cold run");

        // The ordinary case still has to work, or the checks above pass by breaking everything.
        Summary all = Report.Summarise([Ok(1218.1, 455_544_832), Ok(1105.3, 454_000_000), Ok(1160.0, 456_000_000)]);
        failures += Expect(all.ColdMeasured && all.ColdToResponsiveMs == 1218.1,
            $"run 1 is the cold figure when it succeeded, got {all.ColdToResponsiveMs}");
        failures += Expect(all.WarmRuns == 2, $"the two later runs are warm, got {all.WarmRuns}");
        failures += Expect(Report.ExitCode(all, webEnginePresent: true) == 0, "a complete before must exit 0");
        failures += Expect(Report.ExitCode(all, webEnginePresent: false) == 2, "a complete non-before must exit 2");
        failures += Expect(Report.Json("x.exe", "tree", tree, all, "test-os", CacheState.Warm).Contains("\"cold_measured\":true"),
            "a measured cold start must be stamped as measured");

        // Nothing measured at all: still no zeros, and still exit 1.
        Summary none = Report.Summarise([StartupResult.Failed(why)]);
        failures += Expect(Report.ExitCode(none, webEnginePresent: true) == 1, "no measurement must exit 1");
        failures += Expect(Report.Json("x.exe", "tree", tree, none, "test-os", CacheState.Warm).Contains("\"working_set_bytes_median\":null"),
            "an unmeasured working set must be null, not 0 bytes");

        failures += CacheStateChecks(tree, all);

        return failures;
    }

    /// <summary>
    /// PP61: the cache state is stamped, always, and an unstated one never compares.
    ///
    /// The field exists because a cold figure taken with the file cache already warm is three times
    /// the one taken without, and nothing in the old report said which. These check the two things
    /// that make the field worth having: it is ALWAYS written, so an old report and an unstated one
    /// do not look alike, and unknown compares with nothing - not even with another unknown, since
    /// two reports that both declined to say are not thereby in the same state.
    /// </summary>
    private static int CacheStateChecks(TreeSize tree, Summary all)
    {
        int failures = 0;

        string unstated = Report.Json("x.exe", "tree", tree, all, "test-os", CacheState.Unknown);
        failures += Expect(unstated.Contains("\"cache_state\":\"unknown\""),
            $"an unstated cache state must still be written, got: {unstated}");
        failures += Expect(unstated.Contains("\"cold_is_comparable\":false"),
            "an unstated cache state must mark the cold figure incomparable");

        string dropped = Report.Json("x.exe", "tree", tree, all, "test-os", CacheState.Dropped);
        failures += Expect(dropped.Contains("\"cache_state\":\"dropped\""),
            $"a stated cache state must be written as its own name, got: {dropped}");
        failures += Expect(dropped.Contains("\"cold_is_comparable\":true"),
            "a stated cache state must mark the cold figure comparable");

        // Unknown never compares, including with itself - that is the whole point of the field.
        failures += Expect(!Report.Comparable(CacheState.Unknown, CacheState.Unknown),
            "two unstated reports must not be treated as taken in the same state");
        failures += Expect(Report.Comparable(CacheState.Dropped, CacheState.Dropped),
            "two reports in the same stated state must compare");
        failures += Expect(!Report.Comparable(CacheState.Dropped, CacheState.Warm),
            "a dropped-cache run must not compare with a warm one");

        // A name nobody recognises is unknown rather than an error: a misspelling produces a report
        // that refuses comparison, which beats producing none.
        failures += Expect(Report.ParseCacheState("clod-boot") == CacheState.Unknown,
            "an unrecognised cache state must read as unknown");
        failures += Expect(Report.ParseCacheState("cold-boot") == CacheState.ColdBoot,
            "a recognised one must read as itself");

        return failures;
    }

    private static int Expect(bool condition, string what)
    {
        if (condition)
            return 0;
        Console.Error.WriteLine($"FAIL {what}");
        return 1;
    }
}
