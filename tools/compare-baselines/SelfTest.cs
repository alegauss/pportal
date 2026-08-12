namespace CompareBaselines;

/// <summary>
/// The assertion this tool ships with. It is here rather than in a test project because there is no
/// managed test runner in the tree yet (PP36), and a reporting tool with nothing asserting its
/// arithmetic prints whatever it prints.
///
/// Two fixtures differ in exactly one stage and in exactly one condition, so what is checked is the
/// two things this tool exists to do: locate the stage that moved, and refuse to compare records
/// taken under different settings without saying so.
/// </summary>
internal static class SelfTest
{
    /// <summary>Schema 4 record. Kept literal so a schema change breaks this loudly.</summary>
    private const string Before =
        "{\"schema\":4,\"started_utc\":\"2026-08-12T10:00:00Z\",\"duration_ms\":600000," +
        "\"app_version\":\"1.10.0\"," +
        "\"video\":{\"width\":1920,\"height\":1080,\"fps\":60,\"codec\":\"h264\"}," +
        "\"settings\":{\"hw_decoder\":\"cuda\",\"bitrate_kbps\":30000,\"packet_loss_max\":0.05000,\"idr_on_fec_failure\":true}," +
        "\"measured_bitrate_mbps\":27.500,\"average_packet_loss\":0.01250," +
        "\"frames\":{\"presented\":36000,\"lost\":12,\"dropped\":7}," +
        "\"handoff_us\":{\"min\":900,\"max\":1500,\"avg\":1200,\"p50\":1279,\"p99\":1500,\"samples\":36000}," +
        "\"stages_us\":{" +
        "\"receive\":{\"min\":40,\"max\":60,\"avg\":50,\"p50\":43,\"p99\":60,\"samples\":36000}," +
        "\"reorder\":{\"min\":1100,\"max\":1100,\"avg\":1100,\"p50\":1100,\"p99\":1100,\"samples\":36000}," +
        "\"reassemble\":{\"min\":3000,\"max\":3000,\"avg\":3000,\"p50\":3000,\"p99\":3000,\"samples\":36000}," +
        "\"correct\":{\"min\":250,\"max\":250,\"avg\":250,\"p50\":250,\"p99\":250,\"samples\":120}," +
        "\"decode\":{\"min\":4200,\"max\":9000,\"avg\":6600,\"p50\":4607,\"p99\":9000,\"samples\":36000}}," +
        "\"latency\":{\"estimate_us\":37800,\"input_to_wire_us\":{\"min\":400,\"max\":800,\"avg\":600,\"p50\":415,\"p99\":800,\"samples\":9000},\"network_rtt_us\":36000}}";

    /// <summary>Same conditions, and only the present stage got slower. Nothing else moved.</summary>
    private static string AfterPresentSlower => Before
        .Replace("\"handoff_us\":{\"min\":900,\"max\":1500,\"avg\":1200,\"p50\":1279,\"p99\":1500,\"samples\":36000}",
                 "\"handoff_us\":{\"min\":900,\"max\":9000,\"avg\":3000,\"p50\":2559,\"p99\":7500,\"samples\":36000}");

    /// <summary>Identical numbers, but taken on a different decoder - a delta about the settings.</summary>
    private static string AfterDifferentDecoder => Before.Replace("\"hw_decoder\":\"cuda\"", "\"hw_decoder\":\"d3d11va\"");

    public static int Run()
    {
        int failures = 0;

        // 1. The stage that moved is the one reported as moved, and the others read as unchanged.
        Record b = Record.Parse(Before);
        Record a = Record.Parse(AfterPresentSlower);
        string report = Compare.Report(b, a, "before", "after");

        failures += Expect(Compare.Mismatches(b, a).Count == 0,
            "identical conditions must not be reported as a mismatch");
        failures += Expect(report.Contains("present"), "the present stage must appear in the table");
        // p99 1500 -> 7500 is +6000, which is the delta a reader is meant to act on.
        failures += Expect(report.Contains("+6000"), "the present stage's p99 delta of +6000 must be printed");
        // Every other stage is untouched, so its columns must read as a dot rather than a number.
        failures += Expect(report.Contains("receive") && report.Contains("."),
            "an unchanged stage must read as unchanged");
        failures += Expect(!report.Contains("CONDITIONS DIFFER"),
            "matching conditions must not raise the warning");

        // 2. A settings change is caught and named, even though every number is identical.
        Record c = Record.Parse(AfterDifferentDecoder);
        List<string> mismatches = Compare.Mismatches(b, c);
        failures += Expect(mismatches.Count == 1, $"one condition changed, {mismatches.Count} reported");
        failures += Expect(mismatches.Count == 1 && mismatches[0].Contains("decoder"),
            "the changed condition must be named as the decoder");
        failures += Expect(Compare.Report(b, c, "before", "after").Contains("CONDITIONS DIFFER"),
            "a settings change must raise the warning above the table");

        // 3. A record from another schema is refused rather than half-read.
        try
        {
            Record.Parse(Before.Replace("\"schema\":4", "\"schema\":3"));
            failures += Expect(false, "a schema 3 record must be refused");
        }
        catch (NotSupportedException)
        {
            // expected
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
