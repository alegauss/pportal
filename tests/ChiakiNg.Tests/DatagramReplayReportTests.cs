using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP516: what --replay-datagrams prints, and the four ways it prints nothing.
/// </summary>
public class DatagramReplayReportTests
{
    private static CapturedDatagram Datagram(int baseType, long at, int length)
    {
        var head = new byte[TakionTimingCapture.HeadBytes];
        head[0] = (byte)baseType;
        return new CapturedDatagram(at, length, baseType, head);
    }

    private static IReadOnlyList<CapturedDatagram> Capture() =>
    [
        Datagram(TakionDispatch.Control, 0, 33),
        Datagram(TakionDispatch.Video, 16_000, 1300),
        Datagram(TakionDispatch.Video, 32_000, 1400),
        Datagram(TakionDispatch.Audio, 48_000, 280),
    ];

    private static string Written(IReadOnlyList<CapturedDatagram> datagrams)
    {
        var capture = new TakionTimingCapture();
        foreach (CapturedDatagram datagram in datagrams)
            capture.Offer(datagram.Head, datagram.ArrivalMicroseconds, datagram.Length);

        return TakionCaptureFile.Write(capture);
    }

    /// <summary>A capture on disk is read, replayed and reported.</summary>
    [Fact]
    public void ACaptureOnDiskIsReplayedAndReported()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pp516-{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllText(path, Written(Capture()));

            Assert.Equal(ReplayOutcome.Replayed, DatagramReplayReport.Run(path, out string report));

            Assert.Contains("4 datagram(s)", report, StringComparison.Ordinal);
            Assert.Contains("video: 2 packet(s), 2700 byte(s)", report, StringComparison.Ordinal);
            Assert.Contains("control: 1 packet(s), 33 byte(s)", report, StringComparison.Ordinal);
            Assert.Contains("audio: 1 packet(s), 280 byte(s)", report, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// The wire bytes are the capture's lengths, which is what PP515 made possible.
    ///
    /// Before it every packet measured eighteen, and this table would have said the video and the
    /// control channel cost the same - which is the shape of report that reads as a finding.
    /// </summary>
    [Fact]
    public void TheWireBytesAreTheLengthsAndNotTheHeads()
    {
        IReadOnlyList<CapturedDatagram> capture = Capture();
        ReplayReport replay = TakionCaptureReplay.Run(capture, new CountingReplaySink());

        IReadOnlyList<(string Name, int Count, long Bytes)> rows =
            DatagramReplayReport.ByBranch(capture, replay);

        (string _, int _, long video) = rows.Single(r => r.Name == "video");
        (string _, int _, long control) = rows.Single(r => r.Name == "control");

        Assert.Equal(2700, video);
        Assert.Equal(33, control);
        Assert.NotEqual(video, control);
    }

    /// <summary>
    /// The allocation line says which way it went, so a broken budget cannot read as a pass.
    ///
    /// The whole report is descriptive except this line, which is a claim - so it is the one that
    /// changes its words rather than only its number.
    /// </summary>
    [Fact]
    public void TheAllocationLineSaysWhichWayItWent()
    {
        IReadOnlyList<CapturedDatagram> capture = Capture();

        string held = DatagramReplayReport.Render(
            capture, TakionCaptureReplay.Run(capture, new CountingReplaySink()));
        Assert.Contains("the budget holds", held, StringComparison.Ordinal);

        ReplayReport broken = TakionCaptureReplay.Run(capture, new CountingReplaySink()) with
        {
            AllocatedBytes = 96,
        };
        Assert.Contains("THE BUDGET IS BROKEN", DatagramReplayReport.Render(capture, broken),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// No duration is printed, which PP513 settled: one measured here is about this machine.
    ///
    /// Asserted as an absence because that is what it is - a report that grew a millisecond figure
    /// would invite a comparison against the C that nobody has made.
    /// </summary>
    [Fact]
    public void NoTimingIsPrinted()
    {
        IReadOnlyList<CapturedDatagram> capture = Capture();
        string report = DatagramReplayReport.Render(
            capture, TakionCaptureReplay.Run(capture, new CountingReplaySink()));

        Assert.DoesNotContain("elapsed", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("per packet", report, StringComparison.OrdinalIgnoreCase);

        // The one span it does print is the capture's own arrival span, not a measurement.
        Assert.Contains("over 48.0 ms", report, StringComparison.Ordinal);
    }

    /// <summary>A path that is not a capture, is empty, or is not there, each say so.</summary>
    [Fact]
    public void TheThreeRefusalsAreDistinct()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"pp516-{Guid.NewGuid():N}.txt");
        Assert.Equal(ReplayOutcome.NotFound, DatagramReplayReport.Run(missing, out _));

        string path = Path.Combine(Path.GetTempPath(), $"pp516-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "chiaki-exchange-1\n");
            Assert.Equal(ReplayOutcome.NotACapture, DatagramReplayReport.Run(path, out _));

            File.WriteAllText(path, Written([]));
            Assert.Equal(ReplayOutcome.Empty, DatagramReplayReport.Run(path, out _));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// PP517: the report says whether the MAC gate's model and the C agreed on these heads.
    ///
    /// Both halves of the sentence, because a line that could only say "agree" would say it about a
    /// capture nothing was compared over.
    /// </summary>
    [Fact]
    public void TheReportSaysWhetherTheGateAgreed()
    {
        IReadOnlyList<CapturedDatagram> capture = Capture();

        Assert.Equal(0, DatagramReplayReport.MacDisagreements(capture));

        string report = DatagramReplayReport.Render(
            capture, TakionCaptureReplay.Run(capture, new CountingReplaySink()));

        Assert.Contains("the model and the C agree on every head", report, StringComparison.Ordinal);

        // A head the model and the C cannot both accept is one they still answer the same way, so
        // the disagreement count is about behaviour rather than about acceptance.
        IReadOnlyList<CapturedDatagram> odd =
            [.. capture, Datagram(baseType: 6, at: 60_000, length: 40)];

        Assert.Equal(0, DatagramReplayReport.MacDisagreements(odd));
    }

    /// <summary>
    /// PP520: a capture written before PP515 is refused by name rather than read.
    ///
    /// It IS a capture - it parses, its rows are in the right places, and every field but one still
    /// means what it means. That is exactly what a reader cannot detect, and what a version is for.
    /// Reading it would report every video packet as eighteen bytes and look measured.
    /// </summary>
    [Fact]
    public void ACaptureFromBeforeTheLengthRepairIsRefusedByName()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pp520-{Guid.NewGuid():N}.txt");

        try
        {
            // The old version's own text: same columns, same rows, Length holding the head's.
            string head = new('0', TakionTimingCapture.HeadBytes * 2);
            File.WriteAllText(
                path,
                $"{TakionCaptureFile.HeadLengthVersion}\n0\t18\t2\t{head}\n");

            Assert.Equal(ReplayOutcome.HeadLengthVersion, DatagramReplayReport.Run(path, out string report));
            Assert.Equal(string.Empty, report);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// PP520: and the refusal is not the one a file of another shape gets.
    ///
    /// Four distinct outcomes, because "this is not a capture" and "this is an older capture" are
    /// different things to tell somebody holding a file.
    /// </summary>
    [Fact]
    public void TheOldVersionIsNotTheSameRefusalAsNotACapture()
    {
        Assert.NotEqual(ReplayOutcome.NotACapture, ReplayOutcome.HeadLengthVersion);

        Assert.True(TakionCaptureFile.IsHeadLengthVersion($"{TakionCaptureFile.HeadLengthVersion}\n"));
        Assert.False(TakionCaptureFile.IsHeadLengthVersion($"{TakionCaptureFile.FormatVersion}\n"));
        Assert.False(TakionCaptureFile.IsHeadLengthVersion("chiaki-exchange-1\n"));

        // And the two version lines are not the same string, which is the whole of the repair.
        Assert.NotEqual(TakionCaptureFile.HeadLengthVersion, TakionCaptureFile.FormatVersion);
    }

    /// <summary>
    /// PP522: AV packets before the cipher are postponed, and their bytes go with them.
    ///
    /// Both columns, because moving the counts and leaving the bytes would give a table whose two
    /// halves covered different packets - which is the shape that reads as a measurement and is not.
    /// </summary>
    [Fact]
    public void TheProloguesAvPacketsArePostponedWithTheirBytes()
    {
        var head = new byte[TakionTimingCapture.HeadBytes];

        CapturedDatagram Zero(int baseType, long at, int length)
        {
            var bytes = (byte[])head.Clone();
            bytes[0] = (byte)baseType;
            return new CapturedDatagram(at, length, baseType, bytes);
        }

        CapturedDatagram Keyed(int baseType, long at, int length, uint keyPos)
        {
            var bytes = (byte[])head.Clone();
            bytes[0] = (byte)baseType;
            TakionMacLayout layout = TakionPacketMac.LayoutFor(baseType)!.Value;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                bytes.AsSpan(layout.KeyPosOffset, TakionPacketMac.KeyPosSize), keyPos);
            return new CapturedDatagram(at, length, baseType, bytes);
        }

        IReadOnlyList<CapturedDatagram> capture =
        [
            Zero(TakionDispatch.Control, 0, 33),
            Zero(TakionDispatch.Audio, 1000, 280),
            Zero(TakionDispatch.Video, 2000, 1300),
            Keyed(TakionDispatch.Video, 3000, 1400, 16),
        ];

        Assert.Equal(3, TakionCaptureReplay.CipherFrom(capture));

        ReplayReport replay = TakionCaptureReplay.Run(
            capture, new CountingReplaySink(), cipherFrom: TakionCaptureReplay.CipherFrom(capture));

        Assert.Equal(2, replay.Counters.Postponed);
        Assert.Equal(1, replay.Counters.Video);
        Assert.Equal(0, replay.Counters.Audio);

        IReadOnlyList<(string Name, int Count, long Bytes)> rows =
            DatagramReplayReport.ByBranch(capture, replay);

        Assert.Equal((2, 1580L), rows.Single(r => r.Name == "postponed") is var p ? (p.Count, p.Bytes) : default);
        Assert.Equal(1400, rows.Single(r => r.Name == "video").Bytes);
        Assert.Equal(0, rows.Single(r => r.Name == "audio").Bytes);

        // The two columns cover the same packets: every captured byte is in exactly one row.
        Assert.Equal(capture.Sum(d => (long)d.Length), rows.Sum(r => r.Bytes));
    }

    /// <summary>
    /// PP522: a capture with no cipher at all has no such index, and answering zero would invert it.
    /// </summary>
    [Fact]
    public void ACaptureThatNeverGotACipherHasNoIndex()
    {
        var head = new byte[TakionTimingCapture.HeadBytes];
        head[0] = (byte)TakionDispatch.Video;

        Assert.Null(TakionCaptureReplay.CipherFrom(
            [new CapturedDatagram(0, 1300, TakionDispatch.Video, head)]));
    }

    /// <summary>
    /// PP523: the gaps are reported as a distribution, and the mean is what hid the tail.
    ///
    /// Nine gaps of a hundred microseconds and one of fifty milliseconds have a mean of five
    /// thousand - a third of the timeout, which reads as headroom. The p99 and the max say what
    /// actually happened.
    /// </summary>
    [Fact]
    public void TheGapsAreADistributionAndNotAMean()
    {
        long at = 0;
        var datagrams = new List<CapturedDatagram>();
        foreach (long step in (long[])[0, 100, 100, 100, 100, 100, 100, 100, 100, 100, 50_000])
        {
            at += step;
            datagrams.Add(Datagram(TakionDispatch.Video, at, 1300));
        }

        DatagramReplayReport.GapShape gaps = DatagramReplayReport.Gaps(datagrams);

        Assert.Equal(10, gaps.Count);
        Assert.Equal(100, gaps.P50);
        Assert.Equal(50_000, gaps.Max);
        Assert.Equal(1, gaps.OverTimeout);

        // What a mean alone would have said, named so the difference is on the record.
        double mean = TakionCaptureReplay.MeanGapMicroseconds(datagrams)!.Value;
        Assert.True(mean < AvReorderTimeout.TimeoutUs, $"the mean is {mean:0} and hides the tail");
    }

    /// <summary>
    /// PP523: the timeout the gaps are measured against is the C's, not a number typed here.
    /// </summary>
    [Fact]
    public void TheTimeoutIsTheCs()
    {
        IReadOnlyList<CapturedDatagram> capture = Capture();
        string report = DatagramReplayReport.Render(
            capture, TakionCaptureReplay.Run(capture, new CountingReplaySink()));

        Assert.Contains($"{AvReorderTimeout.TimeoutUs}us reorder timeout", report, StringComparison.Ordinal);
        Assert.Equal(16000, AvReorderTimeout.TimeoutUs);
    }

    /// <summary>A capture of one datagram has no gaps, and the line is left out rather than zeroed.</summary>
    [Fact]
    public void OneDatagramHasNoGaps()
    {
        DatagramReplayReport.GapShape gaps =
            DatagramReplayReport.Gaps([Datagram(TakionDispatch.Video, 0, 1300)]);

        Assert.Equal(0, gaps.Count);

        string report = DatagramReplayReport.Render(
            [Datagram(TakionDispatch.Video, 0, 1300)],
            TakionCaptureReplay.Run([Datagram(TakionDispatch.Video, 0, 1300)], new CountingReplaySink()));

        Assert.DoesNotContain("[replay] gaps:", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// PP525: a step of one is ordinary, more is a loss, and zero or less is a reorder.
    ///
    /// The three cases named apart, because a long arrival gap is all three from the outside and
    /// only the packet index says which. A healthy session produced the first and nothing else.
    /// </summary>
    [Fact]
    public void TheThreeSequenceCasesAreToldApart()
    {
        CapturedDatagram Video(long at, ushort packetIndex, ushort frameIndex)
        {
            var bytes = new byte[TakionTimingCapture.HeadBytes];
            bytes[0] = (byte)TakionDispatch.Video;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(1, 2), packetIndex);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(3, 2), frameIndex);

            // A nonzero key position, so nothing here is taken for the prologue.
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                bytes.AsSpan(TakionPacketMac.LayoutFor(TakionDispatch.Video)!.Value.KeyPosOffset, 4),
                16);

            return new CapturedDatagram(at, 1300, TakionDispatch.Video, bytes);
        }

        DatagramReplayReport.SequenceShape clean = DatagramReplayReport.VideoSequence(
            [Video(0, 10, 1), Video(1000, 11, 1), Video(2000, 12, 2)]);

        Assert.Equal(2, clean.Steps);
        Assert.Equal(0, clean.Losses);
        Assert.Equal(0, clean.Reorders);
        Assert.Equal(2, clean.Frames);

        DatagramReplayReport.SequenceShape lost = DatagramReplayReport.VideoSequence(
            [Video(0, 10, 1), Video(1000, 14, 1)]);
        Assert.Equal(1, lost.Losses);
        Assert.Equal(0, lost.Reorders);

        DatagramReplayReport.SequenceShape late = DatagramReplayReport.VideoSequence(
            [Video(0, 14, 1), Video(1000, 10, 1)]);
        Assert.Equal(0, late.Losses);
        Assert.Equal(1, late.Reorders);
    }

    /// <summary>
    /// PP527: the key position sees a reordering the video index cannot, and says how far.
    ///
    /// ONE COUNTER SERVES ALL THE CHANNELS, so a video packet overtaken by a control packet moves
    /// in this ordering and not in video's own. A sixty-second capture reported zero reorders from
    /// the packet indices and sixteen datagrams out of send order from the key positions, over the
    /// same 48300 - and this is the shape that produces that disagreement, in four.
    ///
    /// AND THE DISPLACEMENT IS THE NUMBER, not the boolean it replaces. "NOT MONOTONIC" reads the
    /// same for sixteen of 48300 and for twenty thousand of them.
    ///
    /// THE TIE IS THE PART THAT BITES, and the last assertion here is what holds it: the ordering
    /// must be stable, because the prologue is twenty-seven packets at position zero. An unstable
    /// sort scatters them and reports displacements no packet made - which is what the first
    /// measurement of this capture did, before this was written.
    /// </summary>
    [Fact]
    public void TheKeyPositionSeesWhatTheVideoIndexCannot()
    {
        CapturedDatagram Keyed(int baseType, long at, uint keyPos, ushort packetIndex)
        {
            var bytes = new byte[TakionTimingCapture.HeadBytes];
            bytes[0] = (byte)baseType;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(1, 2), packetIndex);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                bytes.AsSpan(TakionPacketMac.LayoutFor(baseType)!.Value.KeyPosOffset, 4), keyPos);
            return new CapturedDatagram(at, 300, baseType, bytes);
        }

        // Sent 16, 32, 48, 64. The control packet at 48 arrived before the video packet at 32, so
        // the two in the middle changed places - one apart, and two datagrams moved.
        IReadOnlyList<CapturedDatagram> capture =
        [
            Keyed(TakionDispatch.Video, 0, 16, 10),
            Keyed(TakionDispatch.Control, 1000, 48, 0),
            Keyed(TakionDispatch.Video, 2000, 32, 11),
            Keyed(TakionDispatch.Video, 3000, 64, 12),
        ];

        DatagramReplayReport.KeyPositionShape shape = DatagramReplayReport.KeyPositions(capture);

        Assert.False(shape.Monotonic);
        Assert.Equal(2, shape.OutOfPlace);
        Assert.Equal(1, shape.WorstDisplacement);

        // The video indices ran 10, 11, 12 throughout, so video's own ordering saw none of it.
        Assert.Equal(0, DatagramReplayReport.VideoSequence(capture).Reorders);

        // A stream that was never reordered has no displacement, and the line goes back to saying
        // so rather than printing a zero nobody asked for.
        DatagramReplayReport.KeyPositionShape ordered = DatagramReplayReport.KeyPositions(
        [
            Keyed(TakionDispatch.Video, 0, 16, 10),
            Keyed(TakionDispatch.Video, 1000, 32, 11),
        ]);

        Assert.True(ordered.Monotonic);
        Assert.Equal(0, ordered.OutOfPlace);

        // And the opening, which is where an unstable ordering would invent one: twenty packets
        // all at position zero have no send order to be out of, and every one of them would move
        // under a sort that does not keep ties where it found them.
        List<CapturedDatagram> opening =
            [.. Enumerable.Range(0, 20).Select(i => Keyed(TakionDispatch.Video, i * 100, 0, (ushort)i))];
        opening.Add(Keyed(TakionDispatch.Video, 5000, 16, 20));

        DatagramReplayReport.KeyPositionShape prologue = DatagramReplayReport.KeyPositions(opening);

        Assert.Equal(0, prologue.OutOfPlace);
        Assert.Equal(0, prologue.WorstDisplacement);
    }

    /// <summary>The capture is not rewritten by being read, because it is evidence.</summary>
    [Fact]
    public void ReplayingDoesNotTouchTheFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pp516-{Guid.NewGuid():N}.txt");

        try
        {
            string text = Written(Capture());
            File.WriteAllText(path, text);
            DateTime before = File.GetLastWriteTimeUtc(path);

            Assert.Equal(ReplayOutcome.Replayed, DatagramReplayReport.Run(path, out _));

            Assert.Equal(text, File.ReadAllText(path));
            Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
