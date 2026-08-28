using System.Globalization;
using System.Text;
using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>Why a replay printed nothing.</summary>
public enum ReplayOutcome
{
    /// <summary>The file was read and replayed.</summary>
    Replayed,

    /// <summary>There is no file at that path.</summary>
    NotFound,

    /// <summary>The file is not a capture this version understands.</summary>
    NotACapture,

    /// <summary>It is a capture and holds no datagram.</summary>
    Empty,
}

/// <summary>
/// PP516: what `ChiakiNg.exe --replay-datagrams` prints.
///
/// Two captures sat on disk and nothing read them. PP513 built the replay as a library and its
/// tests feed it datagrams they invented, so the managed half of PP27's comparison had never once
/// run against a packet a console sent.
///
/// WHAT IS PRINTED IS ABOUT THE STREAM, NOT ABOUT THE MACHINE. Branch counts, the bytes copied
/// because a branch keeps them, the spacing, and what the replay allocated. PP513 settled why no
/// duration appears: one measured here is about this laptop, and printing it would invite a
/// comparison against the C that nobody has made. The timing waits for PP481.
///
/// THE ALLOCATION NUMBER IS WORTH PRINTING ANYWAY. PP500 held the composed path at zero over
/// invented input and PP513 over a synthetic capture. A real capture is the third set of shapes
/// and the first nobody chose, so a path that allocates on something only a console produces has
/// had nowhere to hide since this ran.
///
/// THE FILE IS READ-ONLY. What a session wrote is evidence, and a command that rewrote it would be
/// a command that could lose one.
/// </summary>
public static class DatagramReplayReport
{
    /// <summary>Reads a capture and returns the report, or says why there is none.</summary>
    public static ReplayOutcome Run(string path, out string report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        report = string.Empty;

        if (!File.Exists(path))
            return ReplayOutcome.NotFound;

        IReadOnlyList<CapturedDatagram>? datagrams = TakionCaptureFile.Read(File.ReadAllText(path));
        if (datagrams is null)
            return ReplayOutcome.NotACapture;

        if (datagrams.Count == 0)
            return ReplayOutcome.Empty;

        report = Render(datagrams, TakionCaptureReplay.Run(datagrams, new CountingReplaySink()));
        return ReplayOutcome.Replayed;
    }

    /// <summary>The report itself, so a test can read it without a file.</summary>
    public static string Render(IReadOnlyList<CapturedDatagram> datagrams, ReplayReport replay)
    {
        ArgumentNullException.ThrowIfNull(datagrams);

        var text = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;

        text.Append("[replay] ").Append(replay.Replayed.ToString(invariant))
            .Append(" datagram(s) over ")
            .Append((replay.SpanMicroseconds / 1000.0).ToString("0.0", invariant))
            .Append(" ms\n");

        foreach ((string name, int count, long bytes) in ByBranch(datagrams, replay))
        {
            text.Append("[replay]   ").Append(name).Append(": ")
                .Append(count.ToString(invariant)).Append(" packet(s), ")
                .Append(bytes.ToString(invariant)).Append(" byte(s) on the wire\n");
        }

        double? gap = TakionCaptureReplay.MeanGapMicroseconds(datagrams);
        text.Append("[replay] mean gap ")
            .Append(gap is null ? "n/a" : gap.Value.ToString("0", invariant))
            .Append(" us, ")
            .Append(replay.Counters.CopiedBytes.ToString(invariant))
            .Append(" byte(s) copied by the three branches that keep\n");

        // The one number that is a claim rather than a description.
        text.Append("[replay] allocated ")
            .Append(replay.AllocatedBytes.ToString(invariant))
            .Append(" byte(s) after the warm-up")
            .Append(replay.AllocatedBytes == 0 ? " - the budget holds\n" : " - THE BUDGET IS BROKEN\n");

        return text.ToString();
    }

    /// <summary>
    /// Each branch with its packet count and the bytes those packets were on the wire.
    ///
    /// The wire bytes come from the capture's lengths rather than from the heads, which is what
    /// PP515 made possible: before it, every packet measured eighteen and this table would have
    /// said the video and the control channel cost the same.
    /// </summary>
    public static IReadOnlyList<(string Name, int Count, long Bytes)> ByBranch(
        IReadOnlyList<CapturedDatagram> datagrams, ReplayReport replay)
    {
        ArgumentNullException.ThrowIfNull(datagrams);

        var bytes = new Dictionary<int, long>();
        foreach (CapturedDatagram datagram in datagrams)
        {
            bytes.TryGetValue(datagram.BaseType, out long running);
            bytes[datagram.BaseType] = running + datagram.Length;
        }

        long Wire(int baseType) => bytes.TryGetValue(baseType, out long b) ? b : 0;

        return
        [
            ("control", replay.Counters.Control, Wire(TakionDispatch.Control)),
            ("video", replay.Counters.Video, Wire(TakionDispatch.Video)),
            ("audio", replay.Counters.Audio, Wire(TakionDispatch.Audio)),
            ("postponed", replay.Counters.Postponed, 0),
            ("unknown", replay.Counters.UnknownType, 0),
        ];
    }
}
