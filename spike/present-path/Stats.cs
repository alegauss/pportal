using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PresentPath;

/// <summary>
/// Every sample is kept. A spike runs for seconds and a few thousand doubles cost nothing, so
/// the percentiles here are exact rather than bucketed - which is the opposite trade from the
/// one the session record makes, and deliberately so: the record has to fold a whole evening in
/// constant memory, and this has to be a number nobody can dispute the resolution of.
///
/// BucketedP99 exists anyway, computed with the same eight-buckets-to-the-octave scheme the C
/// record uses, so the two can be compared without the difference in method being mistaken for
/// a difference in the thing measured.
/// </summary>
internal sealed class Stats
{
    private readonly List<double> samples = new();

    public string Name { get; }

    public Stats(string name) => Name = name;

    public void Push(double microseconds) => samples.Add(microseconds);

    public int Count => samples.Count;

    public double Min => samples.Count == 0 ? 0 : samples.Min();

    public double Max => samples.Count == 0 ? 0 : samples.Max();

    public double Mean => samples.Count == 0 ? 0 : samples.Average();

    /// <summary>Exact percentile by nearest-rank on the sorted samples.</summary>
    public double Percentile(double p)
    {
        if (samples.Count == 0)
            return 0;
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        // Same rank convention as the C record: ceil(p * n), so at p99 exactly 1% may sit above.
        int rank = (int)Math.Ceiling(p * sorted.Length);
        if (rank < 1)
            rank = 1;
        if (rank > sorted.Length)
            rank = sorted.Length;
        return sorted[rank - 1];
    }

    /// <summary>
    /// The p99 the C record would have reported for these samples: the upper edge of the bucket
    /// the percentile falls in, clamped to the observed maximum. Present so that a number from
    /// this spike and a number from chiaki_baseline.jsonl can be read side by side.
    /// </summary>
    public double BucketedP99()
    {
        if (samples.Count == 0)
            return 0;

        const int subBits = 3;
        const int sub = 1 << subBits;      // 8 buckets per octave
        const int linear = 2 * sub;        // 0..15us counted one bucket each
        const int octaves = 17;            // exponents 4..20
        const int buckets = linear + octaves * sub + 1;

        var counts = new int[buckets];
        foreach (var s in samples)
        {
            ulong v = s <= 0 ? 0UL : (ulong)s;
            counts[BucketIndex(v)]++;
        }

        long target = (long)Math.Ceiling(0.99 * samples.Count);
        long cumulative = 0;
        for (int i = 0; i < buckets; i++)
        {
            cumulative += counts[i];
            if (cumulative < target)
                continue;
            double upper = BucketUpper(i);
            return Math.Min(upper, Max);
        }
        return Max;

        static int BucketIndex(ulong v)
        {
            if (v < linear)
                return (int)v;
            int e = 63 - System.Numerics.BitOperations.LeadingZeroCount(v);
            if (e > 3 + octaves)
                return buckets - 1;
            int s = (int)((v >> (e - subBits)) & (sub - 1));
            return linear + (e - 4) * sub + s;
        }

        static double BucketUpper(int index)
        {
            if (index < linear)
                return index;
            if (index >= buckets - 1)
                return double.MaxValue;
            int k = index - linear;
            int e = 4 + k / sub;
            long s = k % sub;
            long width = 1L << (e - subBits);
            return ((sub + s) << (e - subBits)) + width - 1;
        }
    }

    public string ToJson()
    {
        var c = CultureInfo.InvariantCulture;
        return "{"
            + $"\"samples\":{Count}"
            + $",\"min_us\":{Min.ToString("F1", c)}"
            + $",\"mean_us\":{Mean.ToString("F1", c)}"
            + $",\"p50_us\":{Percentile(0.50).ToString("F1", c)}"
            + $",\"p99_us\":{Percentile(0.99).ToString("F1", c)}"
            + $",\"p99_bucketed_us\":{BucketedP99().ToString("F1", c)}"
            + $",\"max_us\":{Max.ToString("F1", c)}"
            + "}";
    }

    public override string ToString()
    {
        var c = CultureInfo.InvariantCulture;
        return $"{Name,-16} n={Count,5}  min={Min.ToString("F1", c),8}  mean={Mean.ToString("F1", c),8}"
            + $"  p50={Percentile(0.50).ToString("F1", c),8}  p99={Percentile(0.99).ToString("F1", c),9}"
            + $"  max={Max.ToString("F1", c),9}   (us)";
    }
}
