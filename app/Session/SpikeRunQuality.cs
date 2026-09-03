using System.Text.Json;

namespace ChiakiNg.Session;

/// <summary>
/// PP645: which committed spike run is a reading and which one is the machine being busy.
///
/// PP49 took six runs before committing one. The pixel result was identical in all six; the timing
/// was not. Four carried single batches at 200-300us against a p50 near 70 - another process
/// reaching the same video engine - and the means moved by half as a result, 69.6us on one run
/// against 105.7us on another for the same measurement. The p50s did not move: 65-75us throughout.
///
/// So the committed run was chosen by looking at whether its p99 sat near its p50, and that
/// judgement was nowhere in the tree. Somebody re-running the spike got whatever the machine was
/// doing that minute and had no way to tell a contaminated run from a changed answer.
///
/// THIS IS NOT A RULE ABOUT SPIKES IN GENERAL, and that is the part worth reading twice. A long
/// tail is contamination only where the tail cannot be the finding. It cannot be here: both NVIDIA
/// spikes time a DRAINED BATCH of blts on the video engine, so each sample is already an average of
/// twenty-five and a spike in one is another process. It very much can be elsewhere - PP65's whole
/// result is a 103us median against a 26990us p99 in spike/decode-path, which this limit would
/// reject as noise while it is the thing that shipped. That spike is not bound and must not be.
/// </summary>
public static class SpikeRunQuality
{
    /// <summary>
    /// How far p99 may sit above p50 before the run is the machine and not the measurement.
    ///
    /// Measured rather than picked. Across PP49's six runs the ratios were 1.03, 1.05, 1.07 and
    /// 1.19 on the four sides that read clean, and 1.87, 2.53, 2.86, 3.16, 3.33 and 3.38 on the
    /// contaminated ones. There is a gap between 1.19 and 1.87 with nothing in it, and 1.5 sits in
    /// the gap. A number this far from both edges does not need to be exact.
    /// </summary>
    public const double TailLimit = 1.5;

    /// <summary>
    /// The runs this binds, by path. A list rather than a glob, because being bound is a claim
    /// about what a spike's tail MEANS and no directory walk can make it.
    /// </summary>
    public static IReadOnlyList<string> BoundRuns { get; } =
    [
        @"spike\video-hdr\release-4060-engaged.json",
        @"spike\video-upscale\release-4060-no-engage.json",
        @"spike\video-upscale\release-4060-no-engage-2.json",
    ];

    /// <summary>
    /// The runs deliberately not bound, named so each exclusion is a record rather than an omission.
    ///
    /// Two now, and the second is why this stopped being one string. PP65's finding IS the tail - a
    /// 103us median send against a 26990us p99 - and PP32's opus comparison found a managed decoder
    /// whose p99 is five times its own median in every run taken, which is the thing that spike
    /// measures rather than noise in it. A limit applied to either would reject the result.
    /// </summary>
    public static IReadOnlyList<string> Excluded { get; } =
    [
        @"spike\decode-path\release-4060.json",
        @"spike\opus-decode\release-managed-vs-native.json",
    ];

    /// <summary>One timing series out of a run's JSON.</summary>
    /// <param name="Name">The property that held it, e.g. hdr_on_us.</param>
    public readonly record struct Series(string Name, double P50, double P99)
    {
        /// <summary>How far the tail sits above the middle. Zero p50 reads as no tail rather than infinity.</summary>
        public double Tail => P50 > 0 ? P99 / P50 : 0;

        /// <summary>Whether this side is the machine being busy.</summary>
        public bool Contaminated => Tail > TailLimit;
    }

    /// <summary>A committed run, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// Every timing series in a run: any object carrying both p50_us and p99_us.
    ///
    /// Found by shape and not by name, because the three spikes name theirs differently -
    /// vsr_off_us, hdr_on_us, and decode-path's nested per-decoder objects - and a list of names
    /// would silently find nothing the day a fourth spike arrives.
    /// </summary>
    public static IReadOnlyList<Series> SeriesIn(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var found = new List<Series>();
        Walk(JsonDocument.Parse(json).RootElement, "", found);
        return found;
    }

    private static void Walk(JsonElement element, string name, List<Series> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("p50_us", out JsonElement p50)
                    && element.TryGetProperty("p99_us", out JsonElement p99)
                    && p50.ValueKind == JsonValueKind.Number
                    && p99.ValueKind == JsonValueKind.Number)
                {
                    found.Add(new Series(name, p50.GetDouble(), p99.GetDouble()));
                }

                foreach (JsonProperty property in element.EnumerateObject())
                    Walk(property.Value, property.Name, found);
                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    Walk(item, name, found);
                break;

            default:
                break;
        }
    }

    /// <summary>The sides of a run that read as the machine rather than as the measurement.</summary>
    public static IReadOnlyList<Series> ContaminatedIn(string json)
        => [.. SeriesIn(json).Where(series => series.Contaminated)];
}
