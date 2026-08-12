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
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp dir */ }
        }

        Console.WriteLine(failures == 0 ? "OK self-test passed" : $"{failures} self-test check(s) failed");
        return failures == 0 ? 0 : 1;
    }

    private static int Expect(bool condition, string what)
    {
        if (condition)
            return 0;
        Console.Error.WriteLine($"FAIL {what}");
        return 1;
    }
}
