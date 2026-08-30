using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP531, which is PP27's timing half delivered under an id of its own: §PP27 asks for it in
/// those words - "run both against the same captured traffic and compare timing, not just bytes" -
/// and one line carries one partial, so the step that landed got numbered rather than folded in.
///
/// PP517 runs the two halves of the MAC gate for agreement over a capture. These hold the harness
/// that now runs them for cost: that it times both sides and the copy they share, that the copy
/// is measured on its own so neither absolute number is mistaken for the gate alone, and that
/// asking for a duration does not disturb the capture PP517's verdict is read from.
/// </summary>
public class MacGateTimingTests
{
    private static CapturedDatagram Datagram(int baseType, long at, int length)
    {
        var head = new byte[TakionTimingCapture.HeadBytes];
        head[0] = (byte)baseType;
        // A body that is not all zero, so a copy has something to copy and a gate that zeroes a
        // field leaves a difference a comparison could see.
        for (int i = 1; i < head.Length; i++)
            head[i] = (byte)(i * 7);
        return new CapturedDatagram(at, length, baseType, head);
    }

    private static IReadOnlyList<CapturedDatagram> Capture() =>
    [
        Datagram(TakionDispatch.Control, 0, 33),
        Datagram(TakionDispatch.Video, 16_000, 1300),
        Datagram(TakionDispatch.Video, 32_000, 1400),
        Datagram(TakionDispatch.Audio, 48_000, 280),
    ];

    /// <summary>
    /// All three are measured, over the count asked for, and none of them is negative.
    ///
    /// Deliberately not an assertion about how FAST either side is: that number is about the
    /// machine it ran on, which is the whole reason the default report carries no duration.
    /// </summary>
    [Fact]
    public void BothSidesAndTheCopyAreTimed()
    {
        IReadOnlyList<CapturedDatagram> capture = Capture();

        MacGateComparison comparison = MacGateTiming.Measure(capture, batches: 4, warmup: 1);

        Assert.Equal(capture.Count, comparison.Datagrams);
        foreach (MacGateCost cost in new[] { comparison.Managed, comparison.Native, comparison.Copy })
        {
            Assert.Equal(4, cost.Batches);
            Assert.True(cost.MinUs >= 0, $"{cost.Name} min was negative");
            Assert.True(cost.MaxUs >= cost.MinUs, $"{cost.Name} max was below its min");
            Assert.InRange(cost.P50Us, cost.MinUs, cost.MaxUs);
            Assert.InRange(cost.P99Us, cost.MinUs, cost.MaxUs);
        }

        Assert.Equal("managed", comparison.Managed.Name);
        Assert.Equal("c", comparison.Native.Name);
        Assert.Equal("copy", comparison.Copy.Name);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. Timing must not consume the capture it times.
    ///
    /// Both gates mutate the head they are handed - the C zeroes the MAC field - so a harness that
    /// let either touch the captured bytes would leave PP517's agreement check reading a capture
    /// that had already been through the gate twenty times. It would still report agreement, on
    /// input neither side had seen. Asserted by asking PP517's question on both sides of a timed
    /// run.
    /// </summary>
    [Fact]
    public void TimingDoesNotConsumeTheCaptureItTimes()
    {
        IReadOnlyList<CapturedDatagram> capture = Capture();
        byte[][] before = [.. capture.Select(d => d.Head.ToArray())];

        Assert.Equal(0, DatagramReplayReport.MacDisagreements(capture));

        MacGateTiming.Measure(capture, batches: 3, warmup: 1);

        for (int i = 0; i < capture.Count; i++)
            Assert.Equal(before[i], capture[i].Head.ToArray());

        Assert.Equal(0, DatagramReplayReport.MacDisagreements(capture));
    }

    /// <summary>The percentiles are nearest-rank over the samples, and an empty run is not a crash.</summary>
    [Fact]
    public void TheSummaryIsNearestRankOverTheSamples()
    {
        MacGateCost cost = MacGateTiming.Summarise("x", [4, 1, 3, 2]);

        Assert.Equal(4, cost.Batches);
        Assert.Equal(1, cost.MinUs);
        Assert.Equal(4, cost.MaxUs);
        Assert.Equal(2.5, cost.MeanUs);
        // ceil(0.50 * 4) = 2 -> the second of 1,2,3,4.
        Assert.Equal(2, cost.P50Us);
        // ceil(0.99 * 4) = 4 -> the last, so at p99 nothing sits above it in four samples.
        Assert.Equal(4, cost.P99Us);

        MacGateCost none = MacGateTiming.Summarise("x", []);
        Assert.Equal(0, none.Batches);
        Assert.Equal(0, none.MaxUs);
    }

    /// <summary>
    /// The report says which numbers include the copy, because the copy is most of the C's.
    /// A reader handed three numbers and no note would subtract nothing and conclude the two
    /// gates cost about the same.
    /// </summary>
    [Fact]
    public void TheReportNamesTheCopyItIncludes()
    {
        string text = MacGateTiming.Describe(MacGateTiming.Measure(Capture(), batches: 2, warmup: 0));

        Assert.Contains("MAC gate timing over 4 head(s)", text, StringComparison.Ordinal);
        Assert.Contains("managed", text, StringComparison.Ordinal);
        Assert.Contains("copy", text, StringComparison.Ordinal);
        Assert.Contains("the copy is in both", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it is off unless asked for. PP513 decided the default report carries no duration
    /// because one measured here is about this laptop; that decision is kept rather than
    /// overturned, so a flag is what changes it.
    /// </summary>
    [Fact]
    public void TheDefaultReportStillCarriesNoTiming()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pp27-{Guid.NewGuid():N}.txt");

        try
        {
            var capture = new TakionTimingCapture();
            foreach (CapturedDatagram datagram in Capture())
                capture.Offer(datagram.Head, datagram.ArrivalMicroseconds, datagram.Length);
            File.WriteAllText(path, TakionCaptureFile.Write(capture));

            Assert.Equal(ReplayOutcome.Replayed, DatagramReplayReport.Run(path, out string plain));
            Assert.DoesNotContain("MAC gate timing", plain, StringComparison.Ordinal);

            Assert.Equal(ReplayOutcome.Replayed, DatagramReplayReport.Run(path, out string timed, timed: true));
            Assert.Contains("MAC gate timing", timed, StringComparison.Ordinal);
            Assert.StartsWith(plain, timed, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
