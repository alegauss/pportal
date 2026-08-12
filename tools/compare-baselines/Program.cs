using System.Globalization;
using System.Text;

namespace CompareBaselines;

/// <summary>
/// PP45: run the old build and the new one against the same input, and print the difference.
///
/// What it prints is a delta per stage with distributions, not a verdict. A single number that says
/// faster or slower is what people argue about; five distributions are what they fix. So every
/// stage gets p50, p99 and maximum, side by side, with the change.
///
/// Before that, it compares the conditions. Two records taken at different resolutions, on
/// different decoders or against different bitrates produce a delta that measures the settings and
/// reads exactly like one that measures the build. That is how a port acquires a reputation nobody
/// can check, so a mismatch is printed as a warning above the table and sets the exit code.
///
/// Exit 0: compared, conditions matched. 2: compared, conditions differ - read with care.
/// 1: could not compare.
/// </summary>
internal static class Program
{
    private static int Main(string[] argv)
    {
        if (argv.Length == 1 && argv[0] == "--self-test")
            return SelfTest.Run();

        if (argv.Length != 2)
        {
            Console.Error.WriteLine("usage: compare-baselines <before.jsonl> <after.jsonl>");
            Console.Error.WriteLine("       compare-baselines --self-test");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Each file is a chiaki_baseline.jsonl; the last record in each is used.");
            return 1;
        }

        Record before, after;
        try
        {
            before = Record.FromFileLast(argv[0]);
            after = Record.FromFileLast(argv[1]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cannot compare: {ex.Message}");
            return 1;
        }

        Console.Write(Compare.Report(before, after, argv[0], argv[1]));
        return Compare.ConditionsDiffer(before, after) ? 2 : 0;
    }
}

internal static class Compare
{
    public static bool ConditionsDiffer(Record before, Record after) =>
        Mismatches(before, after).Count > 0;

    /// <summary>
    /// Everything that has to match for the delta to be about the build. app_version is excluded
    /// on purpose: it is the one condition that is *supposed* to differ between two builds.
    /// </summary>
    public static List<string> Mismatches(Record b, Record a)
    {
        var m = new List<string>();
        Conditions x = b.Conditions, y = a.Conditions;
        if (x.Width != y.Width || x.Height != y.Height)
            m.Add($"resolution {x.Width}x{x.Height} -> {y.Width}x{y.Height}");
        if (x.Fps != y.Fps)
            m.Add($"fps {x.Fps} -> {y.Fps}");
        if (x.Codec != y.Codec)
            m.Add($"codec {x.Codec} -> {y.Codec}");
        if (x.HwDecoder != y.HwDecoder)
            m.Add($"decoder {x.HwDecoder} -> {y.HwDecoder}");
        if (x.BitrateKbps != y.BitrateKbps)
            m.Add($"requested bitrate {x.BitrateKbps} -> {y.BitrateKbps} kbps");
        if (Math.Abs(x.PacketLossMax - y.PacketLossMax) > 1e-9)
            m.Add($"packet_loss_max {x.PacketLossMax} -> {y.PacketLossMax}");
        return m;
    }

    public static string Report(Record before, Record after, string beforePath, string afterPath)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("session baseline comparison");
        sb.AppendLine($"  before : {beforePath}");
        sb.AppendLine($"           {before.StartedUtc}  {before.DurationMs / 1000}s  {before.Conditions.Describe()}");
        sb.AppendLine($"  after  : {afterPath}");
        sb.AppendLine($"           {after.StartedUtc}  {after.DurationMs / 1000}s  {after.Conditions.Describe()}");
        sb.AppendLine();

        List<string> mismatches = Mismatches(before, after);
        if (mismatches.Count > 0)
        {
            sb.AppendLine("!! CONDITIONS DIFFER - this delta is not only about the build:");
            foreach (string m in mismatches)
                sb.AppendLine($"     {m}");
            sb.AppendLine("   Re-run both builds against the same settings before drawing a conclusion.");
            sb.AppendLine();
        }

        sb.AppendLine("per-stage, microseconds (a negative delta is faster)");
        sb.AppendLine("  stage         p50 before -> after   delta        p99 before -> after   delta          max before -> after   delta");
        foreach (var (name, b, a) in Zip(before, after))
        {
            sb.Append("  ");
            sb.Append(name.PadRight(12));
            sb.Append(Cell(b.P50, a.P50));
            sb.Append("  ");
            sb.Append(Cell(b.P99, a.P99));
            sb.Append("  ");
            sb.Append(Cell(b.Max, a.Max));
            if (b.Samples == 0 || a.Samples == 0)
                sb.Append($"   (samples {b.Samples} -> {a.Samples})");
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("frames and latency");
        sb.AppendLine($"  presented        {Delta(before.FramesPresented, after.FramesPresented)}");
        sb.AppendLine($"  lost             {Delta(before.FramesLost, after.FramesLost)}");
        sb.AppendLine($"  dropped          {Delta(before.FramesDropped, after.FramesDropped)}");
        sb.AppendLine($"  latency floor us {Delta(before.LatencyEstimateUs, after.LatencyEstimateUs)}");
        sb.AppendLine($"  bitrate mbps     {before.MeasuredBitrateMbps.ToString("0.000", c)} -> {after.MeasuredBitrateMbps.ToString("0.000", c)}");
        sb.AppendLine();
        sb.AppendLine("No verdict is printed. The stage whose p99 moved is the address; the mean is the");
        sb.AppendLine("number that hides it, which is why it is not in the table above.");

        return sb.ToString();
    }

    private static IEnumerable<(string Name, Stat Before, Stat After)> Zip(Record b, Record a)
    {
        for (int i = 0; i < b.Stages.Count && i < a.Stages.Count; i++)
            yield return (b.Stages[i].Name, b.Stages[i].Stat, a.Stages[i].Stat);
    }

    private static string Cell(long before, long after)
    {
        long d = after - before;
        string arrow = $"{before,8} ->{after,8}";
        // "+#;-#;0" forces the sign. A bare width like {d,7} right-aligns and drops the plus, which
        // makes a regression and an improvement look alike at a glance - the self-test caught that.
        string delta = d == 0 ? "." : d.ToString("+#;-#;0", CultureInfo.InvariantCulture);
        return $"{arrow} {delta,7}";
    }

    private static string Delta(long before, long after)
    {
        long d = after - before;
        return $"{before,10} -> {after,10}   {(d == 0 ? "." : d.ToString("+#;-#;0", CultureInfo.InvariantCulture))}";
    }
}
