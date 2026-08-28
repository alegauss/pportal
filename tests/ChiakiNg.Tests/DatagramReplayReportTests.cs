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
