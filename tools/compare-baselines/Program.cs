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
/// PP60: the two records need not be the same schema. The sink appends forever, so a real file
/// holds every shape the application has ever written, and refusing all but the newest made the
/// history unreadable. What is compared is now the intersection of what the two records carry, and
/// what fell out of it is printed - a dropped comparison the reader cannot see is worse than one
/// that never ran.
///
/// Exit 0: compared, conditions matched and were verifiable. 2: compared, but the conditions differ
/// or one record did not record them - read with care. 1: could not compare.
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
            Console.Error.WriteLine("Records of different schemas compare on the fields they share.");
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
        return Compare.ConditionsDiffer(before, after) || Compare.Unverifiable(before, after).Count > 0 ? 2 : 0;
    }
}

internal static class Compare
{
    public static bool ConditionsDiffer(Record before, Record after) =>
        Mismatches(before, after).Count > 0;

    /// <summary>
    /// Everything that has to match for the delta to be about the build. app_version is excluded
    /// on purpose: it is the one condition that is *supposed* to differ between two builds.
    ///
    /// A condition one of the records did not carry is not compared here. It is not equal and it is
    /// not different - it is unknown, and <see cref="Unverifiable"/> is where it is said out loud.
    /// Folding an unknown into this list would report a difference nobody measured; folding it into
    /// silence would report a match nobody measured, which is worse.
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
        if (x.HwDecoder is { } bd && y.HwDecoder is { } ad && bd != ad)
            m.Add($"decoder {bd} -> {ad}");
        if (x.BitrateKbps is { } bb && y.BitrateKbps is { } ab && bb != ab)
            m.Add($"requested bitrate {bb} -> {ab} kbps");
        if (x.PacketLossMax is { } bl && y.PacketLossMax is { } al && Math.Abs(bl - al) > 1e-9)
            m.Add($"packet_loss_max {bl} -> {al}");
        return m;
    }

    /// <summary>The conditions that could not be checked, because one of the records predates them.</summary>
    public static List<string> Unverifiable(Record b, Record a)
    {
        var u = new List<string>();
        Conditions x = b.Conditions, y = a.Conditions;
        if (x.HwDecoder is null || y.HwDecoder is null)
            u.Add("decoder");
        if (x.BitrateKbps is null || y.BitrateKbps is null)
            u.Add("requested bitrate");
        if (x.PacketLossMax is null || y.PacketLossMax is null)
            u.Add("packet_loss_max");
        return u;
    }

    /// <summary>What the two records did not share, so the reader knows the table is partial.</summary>
    public static List<string> NotCompared(Record b, Record a)
    {
        var n = new List<string>();

        var bn = b.Stages.Select(s => s.Name).ToHashSet();
        var an = a.Stages.Select(s => s.Name).ToHashSet();
        var only = bn.Except(an).Concat(an.Except(bn)).ToList();
        if (only.Count > 0)
            n.Add($"stages carried by one record only: {string.Join(", ", only)}");

        if (Shared(b, a).Any(t => t.Before.P50 is null || t.After.P50 is null))
            n.Add("p50 - the median arrived in schema 4, so an older record has none");
        if (Shared(b, a).Any(t => t.Before.P99 is null || t.After.P99 is null))
            n.Add("p99 - the per-stage percentiles arrived in schema 2");
        if (b.LatencyEstimateUs is null || a.LatencyEstimateUs is null)
            n.Add("latency floor - not recorded by one of these records");

        List<string> unverifiable = Unverifiable(b, a);
        if (unverifiable.Count > 0)
            n.Add($"conditions one record did not carry, so a match cannot be claimed: {string.Join(", ", unverifiable)}");

        return n;
    }

    public static string Report(Record before, Record after, string beforePath, string afterPath)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("session baseline comparison");
        sb.AppendLine($"  before : {beforePath}");
        sb.AppendLine($"           {before.StartedUtc}  {before.DurationMs / 1000}s  {before.DescribeShape()}");
        sb.AppendLine($"           {before.Conditions.Describe()}");
        sb.AppendLine($"  after  : {afterPath}");
        sb.AppendLine($"           {after.StartedUtc}  {after.DurationMs / 1000}s  {after.DescribeShape()}");
        sb.AppendLine($"           {after.Conditions.Describe()}");
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

        List<string> notCompared = NotCompared(before, after);
        if (notCompared.Count > 0)
        {
            sb.AppendLine("!! PARTIAL COMPARISON - these records are not the same shape:");
            foreach (string n in notCompared)
                sb.AppendLine($"     {n}");
            sb.AppendLine("   Everything below is the intersection. What is absent is absent from the record,");
            sb.AppendLine("   not from the build.");
            sb.AppendLine();
        }

        sb.AppendLine("per-stage, microseconds (a negative delta is faster)");
        sb.AppendLine("  stage         p50 before -> after   delta        p99 before -> after   delta          max before -> after   delta");
        foreach (var (name, b, a) in Shared(before, after))
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

    /// <summary>
    /// The stages both records carry, in the before record's order. Matched by name rather than by
    /// index: a record with only the present stage and one with all six line up at index 0 on two
    /// different stages, and the table would compare receive against present without a word.
    /// </summary>
    public static IEnumerable<(string Name, Stat Before, Stat After)> Shared(Record b, Record a)
    {
        Dictionary<string, Stat> other = a.Stages.ToDictionary(s => s.Name, s => s.Stat);
        foreach ((string name, Stat stat) in b.Stages)
        {
            if (other.TryGetValue(name, out Stat match))
                yield return (name, stat, match);
        }
    }

    private static string Cell(long? before, long? after)
    {
        if (before is not { } b || after is not { } a)
            return $"{Show(before),8} ->{Show(after),8} {"n/a",7}";

        long d = a - b;
        string arrow = $"{b,8} ->{a,8}";
        // "+#;-#;0" forces the sign. A bare width like {d,7} right-aligns and drops the plus, which
        // makes a regression and an improvement look alike at a glance - the self-test caught that.
        string delta = d == 0 ? "." : d.ToString("+#;-#;0", CultureInfo.InvariantCulture);
        return $"{arrow} {delta,7}";
    }

    private static string Show(long? v) => v is { } x ? x.ToString(CultureInfo.InvariantCulture) : "-";

    private static string Delta(long? before, long? after)
    {
        if (before is not { } b || after is not { } a)
            return $"{Show(before),10} -> {Show(after),10}   n/a";
        long d = a - b;
        return $"{b,10} -> {a,10}   {(d == 0 ? "." : d.ToString("+#;-#;0", CultureInfo.InvariantCulture))}";
    }
}
