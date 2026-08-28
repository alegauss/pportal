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

        // PP517: and the second claim - the gate's model against the C, over these heads.
        int disagreed = MacDisagreements(datagrams);
        text.Append("[replay] MAC gate: ")
            .Append(disagreed == 0
                ? "the model and the C agree on every head\n"
                : $"{disagreed.ToString(invariant)} HEAD(S) DISAGREE WITH THE C\n");

        // PP519: and the third - the console's own key positions through the C's expansion.
        KeyPositionShape shape = KeyPositions(datagrams);
        text.Append("[replay] key positions: ")
            .Append(shape.Advances.ToString(invariant)).Append(" advance(s), ")
            .Append(shape.Repeats.ToString(invariant)).Append(" repeat(s), ")
            .Append(shape.NotBlockAligned == 0
                ? "all block-aligned"
                : $"{shape.NotBlockAligned.ToString(invariant)} NOT BLOCK-ALIGNED")
            .Append(shape.Monotonic ? ", one monotonic stream" : ", NOT MONOTONIC")
            .Append(shape.SpuriousWraps == 0 ? "\n" : $", {shape.SpuriousWraps} SPURIOUS WRAP(S)\n");

        return text.ToString();
    }

    /// <summary>What the captured key positions look like as one stream.</summary>
    /// <param name="Advances">Steps that moved forward.</param>
    /// <param name="Repeats">Steps where two packets carried the same position.</param>
    /// <param name="NotBlockAligned">Advances that were not a multiple of the cipher block.</param>
    /// <param name="Monotonic">Whether the stream never went backwards.</param>
    /// <param name="SpuriousWraps">Expansions that added 2^32 where the low half did not wrap.</param>
    public readonly record struct KeyPositionShape(
        int Advances, int Repeats, int NotBlockAligned, bool Monotonic, int SpuriousWraps);

    /// <summary>
    /// PP519: the captured positions, read from the heads and run through the C's expansion.
    ///
    /// ONE STREAM AND NOT THREE. The field sits at a different offset for control than for AV, but
    /// what it holds is a single counter the console advances for everything it sends - so the
    /// positions are read per type and then ordered by ARRIVAL, which is the only order in which
    /// they are monotonic. A port keeping one ledger per channel would see three that each jump.
    ///
    /// THE REPEAT IS WHY THE EXPANSION IS RUN AT ALL. Two consecutive packets can carry the same
    /// position - twenty-six times in the first two thousand captured - and an expansion that
    /// tested low against prev with a plain comparison would add 2^32 to each. The C uses the RFC
    /// comparison in both branches, and neither is true of a value against itself.
    /// </summary>
    public static KeyPositionShape KeyPositions(IReadOnlyList<CapturedDatagram> datagrams)
    {
        ArgumentNullException.ThrowIfNull(datagrams);

        var lows = new List<uint>();

        foreach (CapturedDatagram datagram in datagrams)
        {
            if (TakionPacketMac.ReadKeyPosition(datagram.Head, out uint low) == ChiakiNg.Native.ChiakiError.Success)
                lows.Add(low);
        }

        var advances = 0;
        var repeats = 0;
        var unaligned = 0;
        var monotonic = true;
        var spurious = 0;

        using var state = new KeyState();
        ulong previous = 0;

        for (var i = 0; i < lows.Count; i++)
        {
            if (i > 0)
            {
                if (lows[i] == lows[i - 1])
                {
                    repeats++;
                }
                else if (lows[i] > lows[i - 1])
                {
                    advances++;
                    if ((lows[i] - lows[i - 1]) % TakionKeyPosition.BlockSize != 0)
                        unaligned++;
                }
                else
                {
                    monotonic = false;
                }
            }

            ulong expanded = state.RequestPos(lows[i]);

            // A capture spans a couple of megabytes, so no low half wraps in one - which makes any
            // jump into the high half spurious by construction.
            if (i > 0 && expanded >> 32 != previous >> 32)
                spurious++;

            previous = expanded;
        }

        return new KeyPositionShape(advances, repeats, unaligned, monotonic, spurious);
    }

    /// <summary>
    /// PP517: how many captured heads the model and the C answer differently for.
    ///
    /// The no-cipher path, which is the one a capture can run: with no crypt the C copies the MAC
    /// out, zeroes the field and computes nothing, and that is the whole of what PP497 calls the
    /// rewrite. Compared three ways - the verdict, the bytes copied out, and the packet each leaves
    /// behind - because a pair can agree on a return value and differ on which four bytes it zeroed.
    ///
    /// Each side gets its own copy of the head, since both mutate what they are given.
    /// </summary>
    public static int MacDisagreements(IReadOnlyList<CapturedDatagram> datagrams)
    {
        ArgumentNullException.ThrowIfNull(datagrams);

        var disagreed = 0;

        foreach (CapturedDatagram datagram in datagrams)
        {
            byte[] mine = [.. datagram.Head];
            byte[] theirs = [.. datagram.Head];

            TakionPacketMac.MacResult managed = TakionPacketMac.Apply(mine, gmac: null);
            ChiakiNg.Native.ChiakiError native =
                Takion.PacketMacWithoutCrypt(theirs, keyPos: 0, out byte[]? before);

            if (managed.Error != native
                || !mine.AsSpan().SequenceEqual(theirs)
                || !(managed.MacBefore ?? []).AsSpan().SequenceEqual(before ?? []))
            {
                disagreed++;
            }
        }

        return disagreed;
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
