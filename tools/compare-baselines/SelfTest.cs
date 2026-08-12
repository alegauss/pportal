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

    /// <summary>
    /// The oldest shape the sink ever wrote (49661e9d): the PP39 counters and the present stage,
    /// with no stages_us, no settings, no latency and no percentiles. Written out literally rather
    /// than derived from <see cref="Before"/>, because what is being asserted is that a record of
    /// this shape can be read at all - and a fixture built by deleting fields from the newest one
    /// would only prove the deletions were spelled right.
    /// </summary>
    private const string OldestSchemaOne =
        "{\"schema\":1,\"started_utc\":\"2026-08-01T09:00:00Z\",\"duration_ms\":300000," +
        "\"app_version\":\"1.9.0\"," +
        "\"video\":{\"width\":1920,\"height\":1080,\"fps\":60,\"codec\":\"h264\"}," +
        "\"measured_bitrate_mbps\":26.000,\"average_packet_loss\":0.02000," +
        "\"frames\":{\"presented\":18000,\"lost\":30,\"dropped\":9}," +
        "\"handoff_us\":{\"min\":800,\"max\":1400,\"avg\":1100,\"samples\":18000}}";

    /// <summary>
    /// The other schema 1 (34b10cbf), which added the whole latency object without bumping the
    /// number. Two shapes behind one integer is why the reader keys on fields.
    /// </summary>
    private static string SchemaOneWithLatency => OldestSchemaOne
        .Replace("\"handoff_us\":{\"min\":800,\"max\":1400,\"avg\":1100,\"samples\":18000}}",
                 "\"handoff_us\":{\"min\":800,\"max\":1400,\"avg\":1100,\"samples\":18000}," +
                 "\"latency\":{\"estimate_us\":41000," +
                 "\"input_to_wire_us\":{\"min\":400,\"max\":900,\"avg\":650,\"samples\":4500}," +
                 "\"network_rtt_us\":39000}}");

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

        // 3. PP60: the oldest shape is read rather than refused, and what it cannot answer is named.
        Record old = Record.Parse(OldestSchemaOne);
        failures += Expect(old.Stages.Count == 1 && old.Stages[0].Name == "present",
            "a pre-stages record carries the present stage and no others");
        failures += Expect(old.Stages[0].Stat.P50 is null && old.Stages[0].Stat.P99 is null,
            "a percentile that was never written must be null, not zero");
        failures += Expect(old.LatencyEstimateUs is null,
            "the oldest schema 1 has no latency floor");
        failures += Expect(old.Conditions.HwDecoder is null,
            "settings arrived in schema 3 and must not be invented for an older record");

        string across = Compare.Report(old, b, "old", "new");
        failures += Expect(across.Contains("PARTIAL COMPARISON"),
            "a comparison across shapes must say it is partial");
        failures += Expect(across.Contains("p50") && across.Contains("p99"),
            "the missing percentiles must be named as not compared");
        failures += Expect(Compare.Unverifiable(old, b).Count == 3,
            "the three settings conditions must read as unverifiable, not as equal");
        failures += Expect(Compare.Mismatches(old, b).Count == 0,
            "an unrecorded condition must not be reported as a difference");
        failures += Expect(across.Contains("stages carried by one record only"),
            "the stages left out must be named");
        // The trap: zipping by index lines the old record's only stage up against `receive` while
        // still printing the *before* record's name for the row, so the table reads "present" and
        // compares present to receive. Asserted on the pairing rather than on the rendering,
        // because an index zip leaves the rendering word for word identical - checking the text
        // for "receive" passes while the bug is live, which is what the injected run showed.
        List<(string Name, Stat Before, Stat After)> paired = Compare.Shared(old, b).ToList();
        failures += Expect(paired.Count == 1 && paired[0].Name == "present",
            "only the stage both records carry may be paired");
        failures += Expect(paired.Count == 1 && paired[0].After.Max == 1500,
            "the present row must pair against the new record's present stage, not against its first one");

        // 4. Two shapes behind one schema number: the reader must tell them apart by field.
        Record one = Record.Parse(OldestSchemaOne);
        Record oneLater = Record.Parse(SchemaOneWithLatency);
        failures += Expect(one.Schema == oneLater.Schema,
            "the fixtures must both claim schema 1, or this checks nothing");
        failures += Expect(one.LatencyEstimateUs is null && oneLater.LatencyEstimateUs == 41000,
            "the two schema 1 shapes must be told apart by their fields, not by the number");

        // 5. A record missing a field every shape has ever carried is broken, not old.
        try
        {
            Record.Parse(OldestSchemaOne.Replace("\"handoff_us\"", "\"handoff_typo\""));
            failures += Expect(false, "a record with no handoff_us must still be refused");
        }
        catch (KeyNotFoundException)
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
