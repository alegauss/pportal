using System.Text.Json;

namespace CompareBaselines;

/// <summary>One stage's distribution, as the record writes it.</summary>
internal readonly record struct Stat(long Min, long Max, long Avg, long P50, long P99, long Samples)
{
    public static Stat From(JsonElement e) => new(
        e.GetProperty("min").GetInt64(),
        e.GetProperty("max").GetInt64(),
        e.GetProperty("avg").GetInt64(),
        e.GetProperty("p50").GetInt64(),
        e.GetProperty("p99").GetInt64(),
        e.GetProperty("samples").GetInt64());
}

/// <summary>
/// The conditions a comparison is only valid within. These are compared before the numbers are,
/// because two records taken at different resolutions or on different decoders produce a delta
/// that measures the settings and reads like a delta that measures the build - which is precisely
/// the failure §PP45 exists to stop.
/// </summary>
internal readonly record struct Conditions(
    string AppVersion, string Codec, string HwDecoder,
    long Width, long Height, long Fps, long BitrateKbps, double PacketLossMax)
{
    public string Describe() =>
        $"{AppVersion}  {Width}x{Height}@{Fps}  {Codec}  decoder={HwDecoder}  bitrate={BitrateKbps}kbps  loss_max={PacketLossMax:0.#####}";
}

/// <summary>One line of chiaki_baseline.jsonl.</summary>
internal sealed class Record
{
    public required int Schema { get; init; }
    public required string StartedUtc { get; init; }
    public required long DurationMs { get; init; }
    public required Conditions Conditions { get; init; }
    public required double MeasuredBitrateMbps { get; init; }
    public required long FramesPresented { get; init; }
    public required long FramesLost { get; init; }
    public required long FramesDropped { get; init; }
    public required long LatencyEstimateUs { get; init; }
    /// <summary>The six stages in frame-path order, present last because it is handoff_us.</summary>
    public required IReadOnlyList<(string Name, Stat Stat)> Stages { get; init; }

    /// <summary>The schema this tool understands. A record from another one is refused, not guessed at.</summary>
    public const int SupportedSchema = 4;

    public static Record Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement r = doc.RootElement;

        int schema = r.GetProperty("schema").GetInt32();
        if (schema != SupportedSchema)
        {
            throw new NotSupportedException(
                $"record is schema {schema}, this tool reads {SupportedSchema}. " +
                "A delta across schemas would compare fields that may not mean the same thing.");
        }

        JsonElement video = r.GetProperty("video");
        JsonElement settings = r.GetProperty("settings");
        JsonElement frames = r.GetProperty("frames");
        JsonElement stages = r.GetProperty("stages_us");
        JsonElement latency = r.GetProperty("latency");

        var ordered = new List<(string, Stat)>
        {
            ("receive", Stat.From(stages.GetProperty("receive"))),
            ("reorder", Stat.From(stages.GetProperty("reorder"))),
            ("reassemble", Stat.From(stages.GetProperty("reassemble"))),
            ("correct", Stat.From(stages.GetProperty("correct"))),
            ("decode", Stat.From(stages.GetProperty("decode"))),
            // handoff_us is the present stage. It lives outside stages_us because it shipped
            // first; it belongs at the end of the frame path, which is where it is shown.
            ("present", Stat.From(r.GetProperty("handoff_us"))),
        };

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
                settings.GetProperty("hw_decoder").GetString() ?? "?",
                video.GetProperty("width").GetInt64(),
                video.GetProperty("height").GetInt64(),
                video.GetProperty("fps").GetInt64(),
                settings.GetProperty("bitrate_kbps").GetInt64(),
                settings.GetProperty("packet_loss_max").GetDouble()),
            MeasuredBitrateMbps = r.GetProperty("measured_bitrate_mbps").GetDouble(),
            FramesPresented = frames.GetProperty("presented").GetInt64(),
            FramesLost = frames.GetProperty("lost").GetInt64(),
            FramesDropped = frames.GetProperty("dropped").GetInt64(),
            LatencyEstimateUs = latency.GetProperty("estimate_us").GetInt64(),
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
