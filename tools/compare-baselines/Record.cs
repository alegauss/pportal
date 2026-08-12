using System.Text.Json;

namespace CompareBaselines;

/// <summary>One stage's distribution, as the record writes it.</summary>
/// <remarks>
/// P50 and P99 are nullable because they were not always written. p99 arrives with the per-stage
/// timings and p50 one schema later, so a record from before either has a distribution with a
/// minimum, a maximum and a mean and nothing else. A missing percentile is not a zero: zero is a
/// stage that took no time, and printing one for the other is the reading error this whole tool
/// exists to prevent.
/// </remarks>
internal readonly record struct Stat(long Min, long Max, long Avg, long? P50, long? P99, long Samples)
{
    public static Stat From(JsonElement e) => new(
        e.GetProperty("min").GetInt64(),
        e.GetProperty("max").GetInt64(),
        e.GetProperty("avg").GetInt64(),
        Optional(e, "p50"),
        Optional(e, "p99"),
        e.GetProperty("samples").GetInt64());

    private static long? Optional(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetInt64() : null;
}

/// <summary>
/// The conditions a comparison is only valid within. These are compared before the numbers are,
/// because two records taken at different resolutions or on different decoders produce a delta
/// that measures the settings and reads like a delta that measures the build - which is precisely
/// the failure §PP45 exists to stop.
/// </summary>
/// <remarks>
/// The three settings fields are nullable: the record did not carry the configuration it ran with
/// until schema 3. An older record therefore cannot prove the conditions matched, and it must not
/// be allowed to claim they did - see <see cref="Compare.Mismatches"/>, which reports an unknown
/// condition as unverifiable rather than as equal.
/// </remarks>
internal readonly record struct Conditions(
    string AppVersion, string Codec, string? HwDecoder,
    long Width, long Height, long Fps, long? BitrateKbps, double? PacketLossMax)
{
    public string Describe()
    {
        string decoder = HwDecoder ?? "(not recorded)";
        string bitrate = BitrateKbps is { } k ? $"{k}kbps" : "(not recorded)";
        string loss = PacketLossMax is { } l ? l.ToString("0.#####") : "(not recorded)";
        return $"{AppVersion}  {Width}x{Height}@{Fps}  {Codec}  decoder={decoder}  bitrate={bitrate}  loss_max={loss}";
    }
}

/// <summary>
/// One line of chiaki_baseline.jsonl, of whichever shape it happens to be.
///
/// PP60: the sink appends and never rewrites, so one file holds records from every schema the
/// application has ever shipped, in the order the user upgraded. Refusing all but the newest made
/// the history unreadable to the one tool built to read it - and the history is the point.
///
/// So the shape is detected from the fields that are present, and deliberately **not** from the
/// schema number. The number does not discriminate: 49661e9d and 34b10cbf both write
/// <c>"schema":1</c>, and the second one added the whole <c>latency</c> object without bumping it.
/// A reader keyed on the number would have to guess which of the two it was holding.
/// </summary>
internal sealed class Record
{
    /// <summary>What the record says it is. Recorded and printed; never used to decide a field.</summary>
    public required int Schema { get; init; }

    public required string StartedUtc { get; init; }
    public required long DurationMs { get; init; }
    public required Conditions Conditions { get; init; }
    public required double MeasuredBitrateMbps { get; init; }
    public required long FramesPresented { get; init; }
    public required long FramesLost { get; init; }
    public required long FramesDropped { get; init; }

    /// <summary>Absent before the latency floor shipped, which is inside schema 1 rather than at a bump.</summary>
    public required long? LatencyEstimateUs { get; init; }

    /// <summary>
    /// The stages this record carries, in frame-path order. Present last because it is handoff_us,
    /// which shipped first and stayed at the top level. A record from before the per-stage timings
    /// carries this one stage and no others.
    /// </summary>
    public required IReadOnlyList<(string Name, Stat Stat)> Stages { get; init; }

    /// <summary>The newest shape this tool knows how to read in full.</summary>
    public const int NewestSchema = 4;

    /// <summary>Names the shape actually found, for the header and for the dropped-comparison notes.</summary>
    public string DescribeShape()
    {
        var carried = new List<string>();
        carried.Add(Stages.Count > 1 ? $"{Stages.Count} stages" : "present only");
        carried.Add(Conditions.HwDecoder is null ? "no settings" : "settings");
        carried.Add(LatencyEstimateUs is null ? "no latency" : "latency");
        Stat first = Stages[0].Stat;
        carried.Add(first.P50 is null ? (first.P99 is null ? "no percentiles" : "p99 only") : "p50+p99");
        return $"schema {Schema} ({string.Join(", ", carried)})";
    }

    public static Record Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement r = doc.RootElement;

        int schema = r.GetProperty("schema").GetInt32();

        JsonElement video = r.GetProperty("video");
        JsonElement frames = r.GetProperty("frames");

        // Everything below is asked for rather than assumed. The four required objects - video,
        // frames, handoff_us and the counters - are the ones every shape has ever carried; a file
        // missing one of those is not an old record, it is a broken one, and it still throws.
        bool hasSettings = r.TryGetProperty("settings", out JsonElement settings);
        bool hasStages = r.TryGetProperty("stages_us", out JsonElement stages);
        bool hasLatency = r.TryGetProperty("latency", out JsonElement latency);

        var ordered = new List<(string, Stat)>();
        if (hasStages)
        {
            foreach (string name in new[] { "receive", "reorder", "reassemble", "correct", "decode" })
            {
                if (stages.TryGetProperty(name, out JsonElement s))
                    ordered.Add((name, Stat.From(s)));
            }
        }
        // handoff_us is the present stage. It lives outside stages_us because it shipped first; it
        // belongs at the end of the frame path, which is where it is shown - and it is the one stage
        // every schema has, which is what makes an oldest-to-newest comparison possible at all.
        ordered.Add(("present", Stat.From(r.GetProperty("handoff_us"))));

        return new Record
        {
            Schema = schema,
            StartedUtc = r.GetProperty("started_utc").ValueKind == JsonValueKind.Null
                ? "(never started)"
                : r.GetProperty("started_utc").GetString()!,
            DurationMs = r.GetProperty("duration_ms").GetInt64(),
            Conditions = new Conditions(
                r.GetProperty("app_version").GetString() ?? "?",
                video.GetProperty("codec").GetString() ?? "?",
                hasSettings ? settings.GetProperty("hw_decoder").GetString() ?? "?" : null,
                video.GetProperty("width").GetInt64(),
                video.GetProperty("height").GetInt64(),
                video.GetProperty("fps").GetInt64(),
                hasSettings ? settings.GetProperty("bitrate_kbps").GetInt64() : null,
                hasSettings ? settings.GetProperty("packet_loss_max").GetDouble() : null),
            MeasuredBitrateMbps = r.GetProperty("measured_bitrate_mbps").GetDouble(),
            FramesPresented = frames.GetProperty("presented").GetInt64(),
            FramesLost = frames.GetProperty("lost").GetInt64(),
            FramesDropped = frames.GetProperty("dropped").GetInt64(),
            LatencyEstimateUs = hasLatency && latency.TryGetProperty("estimate_us", out JsonElement est)
                ? est.GetInt64()
                : null,
            Stages = ordered,
        };
    }

    /// <summary>The last record in a JSONL file, which is the most recent session it holds.</summary>
    public static Record FromFileLast(string path)
    {
        string? last = null;
        foreach (string line in File.ReadLines(path))
        {
            if (!string.IsNullOrWhiteSpace(line))
                last = line;
        }
        if (last is null)
            throw new InvalidDataException($"{path} holds no records");
        return Parse(last);
    }
}
