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

    /// <summary>
    /// PP520: a capture written before PP515, whose Length column is the head's.
    ///
    /// Distinct from NotACapture, because it IS one - it parses, its rows are in the right places,
    /// and every field but one means what it still means. Refused rather than read, because the
    /// datagram's length cannot be recovered from a head and every size the replay printed would
    /// be false.
    /// </summary>
    HeadLengthVersion,
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

        string text = File.ReadAllText(path);

        // PP520: asked before the parse, so the answer names the version rather than the shape.
        if (TakionCaptureFile.IsHeadLengthVersion(text))
            return ReplayOutcome.HeadLengthVersion;

        IReadOnlyList<CapturedDatagram>? datagrams = TakionCaptureFile.Read(text);
        if (datagrams is null)
            return ReplayOutcome.NotACapture;

        if (datagrams.Count == 0)
            return ReplayOutcome.Empty;

        // PP522: the cipher's arrival comes from the capture, so the postponed branch is reported
        // rather than folded into Audio and Video.
        report = Render(
            datagrams,
            TakionCaptureReplay.Run(
                datagrams, new CountingReplaySink(),
                cipherFrom: TakionCaptureReplay.CipherFrom(datagrams)));
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

        // PP523: the gap distribution against the reorder timeout, which no model could place.
        GapShape gaps = Gaps(datagrams);
        if (gaps.Count > 0)
        {
            text.Append("[replay] gaps: p50 ").Append(gaps.P50.ToString(invariant))
                .Append(" p90 ").Append(gaps.P90.ToString(invariant))
                .Append(" p99 ").Append(gaps.P99.ToString(invariant))
                .Append(" max ").Append(gaps.Max.ToString(invariant))
                .Append(" us; ").Append(gaps.OverTimeout.ToString(invariant))
                .Append(" over the ").Append(AvReorderTimeout.TimeoutUs.ToString(invariant))
                .Append("us reorder timeout\n");
        }

        // PP525: and what those gaps actually were, which the indices say and the gaps cannot.
        SequenceShape sequence = VideoSequence(datagrams);
        if (sequence.Steps > 0)
        {
            text.Append("[replay] video sequence: ").Append(sequence.Steps.ToString(invariant))
                .Append(" step(s) over ").Append(sequence.Frames.ToString(invariant))
                .Append(" frame(s), ").Append(sequence.Losses.ToString(invariant))
                .Append(" loss(es), ").Append(sequence.Reorders.ToString(invariant))
                .Append(" reorder(s)\n");
        }

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
            .Append(shape.Prologue.ToString(invariant)).Append(" before the cipher (")
            .Append(shape.PrologueRepeats.ToString(invariant)).Append(" repeat(s) at zero), ")
            .Append(shape.RunningRepeats == 0
                ? "none after"
                : $"{shape.RunningRepeats.ToString(invariant)} REPEAT(S) AFTER")
            .Append(", ")
            .Append(shape.NotBlockAligned == 0
                ? "all block-aligned"
                : $"{shape.NotBlockAligned.ToString(invariant)} NOT BLOCK-ALIGNED")
            // PP527: the displacement rather than the boolean. "NOT MONOTONIC" reads the same for a
            // link working normally and a link falling apart, and shouts either way.
            .Append(shape.Monotonic
                ? ", one monotonic stream"
                : $", {shape.OutOfPlace.ToString(invariant)} out of send order"
                    + $" (worst by {shape.WorstDisplacement.ToString(invariant)})")
            .Append(shape.SpuriousWraps == 0 ? "\n" : $", {shape.SpuriousWraps} SPURIOUS WRAP(S)\n");

        return text.ToString();
    }

    /// <summary>What the video packet indices did.</summary>
    /// <param name="Steps">How many consecutive pairs there were.</param>
    /// <param name="Losses">Steps that skipped an index - a packet that never arrived.</param>
    /// <param name="Reorders">Steps that went backwards or stood still.</param>
    /// <param name="Frames">How many distinct frames the capture holds.</param>
    public readonly record struct SequenceShape(int Steps, int Losses, int Reorders, int Frames);

    /// <summary>
    /// PP525: whether the video stream actually lost or reordered anything.
    ///
    /// PP523 measured gaps over the reorder timeout and had to leave a caveat - a quiet link and a
    /// lost packet look alike from a capture. They do not: the head carries the packet index, and
    /// it says which of the two a long gap was.
    ///
    /// THE PROLOGUE IS EXCLUDED, because its AV headers are not meaningful - PP524 measured a codec
    /// byte of 255 and a FEC count of zero there, and its packet index is no better.
    ///
    /// A step of one is ordinary. A step of more is a loss. A step of zero or less is a reorder,
    /// which is the case the whole reorder queue exists for - and a healthy session produces none.
    /// </summary>
    public static SequenceShape VideoSequence(IReadOnlyList<CapturedDatagram> datagrams)
    {
        ArgumentNullException.ThrowIfNull(datagrams);

        int cipher = TakionCaptureReplay.CipherFrom(datagrams) ?? 0;

        var indices = new List<int>();
        var frames = new HashSet<int>();

        for (int i = cipher; i < datagrams.Count; i++)
        {
            CapturedDatagram datagram = datagrams[i];
            if (datagram.BaseType != TakionDispatch.Video || datagram.Head.Length < 5)
                continue;

            indices.Add(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(
                datagram.Head.AsSpan(1, 2)));
            frames.Add(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(
                datagram.Head.AsSpan(3, 2)));
        }

        var losses = 0;
        var reorders = 0;

        for (var i = 1; i < indices.Count; i++)
        {
            int step = indices[i] - indices[i - 1];

            if (step > 1)
                losses++;
            else if (step <= 0)
                reorders++;
        }

        return new SequenceShape(Math.Max(0, indices.Count - 1), losses, reorders, frames.Count);
    }

    /// <summary>The arrival gaps, as a distribution rather than a mean.</summary>
    /// <param name="Count">How many gaps there were - one fewer than the datagrams.</param>
    /// <param name="P50">The median gap, in microseconds.</param>
    /// <param name="P90">The ninetieth percentile.</param>
    /// <param name="P99">The ninety-ninth, which is the one near the timeout.</param>
    /// <param name="Max">The longest gap.</param>
    /// <param name="OverTimeout">How many gaps exceeded the AV reorder timeout.</param>
    public readonly record struct GapShape(
        int Count, long P50, long P90, long P99, long Max, int OverTimeout);

    /// <summary>
    /// PP523: the arrival gaps against the timeout PP449 modelled.
    ///
    /// A MEAN HID THIS. The mean gap of a real capture is about 1250 microseconds against a 16000
    /// timeout, which reads as an order of magnitude of headroom. The distribution says otherwise:
    /// the median is under a hundred, and the tail crosses the timeout repeatedly - so the flush is
    /// on the ordinary path rather than the exceptional one.
    ///
    /// WHAT IT DOES NOT SAY. The timeout governs the wait for a MISSING head packet, and an
    /// inter-arrival gap is not that wait: a quiet link and a lost packet look alike from a capture.
    /// What this measures is the receive thread going idle longer than the timeout, which is when
    /// the queues flush - not a packet that was skipped.
    /// </summary>
    public static GapShape Gaps(IReadOnlyList<CapturedDatagram> datagrams)
    {
        ArgumentNullException.ThrowIfNull(datagrams);

        if (datagrams.Count < 2)
            return default;

        long[] gaps =
        [
            .. datagrams
                .Zip(datagrams.Skip(1), (a, b) => b.ArrivalMicroseconds - a.ArrivalMicroseconds)
                .Order(),
        ];

        long At(int percent) => gaps[Math.Min(gaps.Length - 1, gaps.Length * percent / 100)];

        return new GapShape(
            gaps.Length, At(50), At(90), At(99), gaps[^1],
            gaps.Count(g => g > AvReorderTimeout.TimeoutUs));
    }

    /// <summary>What the captured key positions look like as one stream.</summary>
    /// <param name="Advances">Steps that moved forward.</param>
    /// <param name="PrologueRepeats">
    /// PP521: repeats at position zero, before the cipher exists. Expected and uninteresting.
    /// </param>
    /// <param name="RunningRepeats">
    /// PP521: repeats AFTER the first real position. The interesting number, and it is zero.
    /// </param>
    /// <param name="Prologue">How many packets arrived before the first nonzero position.</param>
    /// <param name="NotBlockAligned">Advances that were not a multiple of the cipher block.</param>
    /// <param name="Monotonic">Whether the stream never went backwards.</param>
    /// <param name="SpuriousWraps">Expansions that added 2^32 where the low half did not wrap.</param>
    /// <param name="OutOfPlace">
    /// PP527: how many datagrams arrived somewhere other than where the key position says they were
    /// sent. The number <see cref="Monotonic"/> was hiding.
    /// </param>
    /// <param name="WorstDisplacement">
    /// PP527: and how far the furthest-travelled one moved, in places.
    ///
    /// The number a reorder queue is sized against: a window shorter than this drops a packet that
    /// was going to arrive.
    /// </param>
    public readonly record struct KeyPositionShape(
        int Advances,
        int PrologueRepeats,
        int RunningRepeats,
        int Prologue,
        int NotBlockAligned,
        bool Monotonic,
        int SpuriousWraps,
        int OutOfPlace,
        int WorstDisplacement);

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
        var prologueRepeats = 0;
        var runningRepeats = 0;
        var unaligned = 0;
        var monotonic = true;
        var spurious = 0;

        // PP521: where the prologue ends. Before the cipher exists there is no position, so the
        // console sends zero for the whole opening - twenty-seven packets and 122ms in the captures
        // taken so far, identical across independent sessions because the opening is not sampled.
        int prologue = lows.FindIndex(low => low != 0);
        if (prologue < 0)
            prologue = lows.Count;

        using var state = new KeyState();
        ulong previous = 0;

        for (var i = 0; i < lows.Count; i++)
        {
            if (i > 0)
            {
                if (lows[i] == lows[i - 1])
                {
                    // PP521: which side of the prologue it is on is the whole distinction. A repeat
                    // at zero is the opening.
                    //
                    // PP527 WITHDRAWS WHAT PP521 SAID ABOUT THE OTHER SIDE. It called a running
                    // repeat "a counter that stood still, which is a different and much worse
                    // thing". A sixty-second capture has exactly one, between a video packet and a
                    // control packet that arrived adjacent, and the counter did not stand still -
                    // the two were sent at positions the network then delivered together. It is
                    // still worth counting and it is not what that sentence claimed.
                    if (i < prologue)
                        prologueRepeats++;
                    else
                        runningRepeats++;
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

        (int outOfPlace, int worst) = Displacement(lows);

        return new KeyPositionShape(
            advances, prologueRepeats, runningRepeats, prologue, unaligned, monotonic, spurious,
            outOfPlace, worst);
    }

    /// <summary>
    /// PP527: how far arrival order differs from send order, which is what "monotonic" was hiding.
    ///
    /// THE KEY POSITION IS THE ONLY THING IN A CAPTURE THAT ORDERS THE CHANNELS AGAINST EACH OTHER.
    /// One counter serves all of them, so a video packet overtaken by a control packet shows here
    /// and nowhere else - <see cref="VideoSequence"/> orders video against video and reported zero
    /// reorders over the same capture that produced these.
    /// </summary>
    /// <returns>How many arrived out of send order, and how far the worst one moved.</returns>
    /// <remarks>
    /// The sort is stable and ties are left in arrival order, so the prologue's twenty-seven zeros
    /// and the one running repeat are not counted as displaced. Two packets that share a position
    /// have no send order to be out of.
    /// </remarks>
    private static (int OutOfPlace, int Worst) Displacement(IReadOnlyList<uint> lows)
    {
        int[] sent = [.. Enumerable.Range(0, lows.Count).OrderBy(i => lows[i])];

        var outOfPlace = 0;
        var worst = 0;

        for (var rank = 0; rank < sent.Length; rank++)
        {
            int moved = Math.Abs(sent[rank] - rank);
            if (moved == 0)
                continue;

            outOfPlace++;
            if (moved > worst)
                worst = moved;
        }

        return (outOfPlace, worst);
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

        // PP522: the prologue's AV packets belong to the postponed row, so the bytes are split the
        // same way the counts are. Attributing them to video and audio while the counts had moved
        // would leave a table whose two columns covered different packets.
        int cipher = TakionCaptureReplay.CipherFrom(datagrams) ?? 0;

        var bytes = new Dictionary<int, long>();
        long postponed = 0;

        for (var i = 0; i < datagrams.Count; i++)
        {
            CapturedDatagram datagram = datagrams[i];

            bool held = i < cipher
                && datagram.BaseType is TakionDispatch.Video or TakionDispatch.Audio;

            if (held)
            {
                postponed += datagram.Length;
                continue;
            }

            bytes.TryGetValue(datagram.BaseType, out long running);
            bytes[datagram.BaseType] = running + datagram.Length;
        }

        long Wire(int baseType) => bytes.TryGetValue(baseType, out long b) ? b : 0;

        return
        [
            ("control", replay.Counters.Control, Wire(TakionDispatch.Control)),
            ("video", replay.Counters.Video, Wire(TakionDispatch.Video)),
            ("audio", replay.Counters.Audio, Wire(TakionDispatch.Audio)),
            ("postponed", replay.Counters.Postponed, postponed),
            ("unknown", replay.Counters.UnknownType, 0),
        ];
    }
}
