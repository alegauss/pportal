using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP513, under PP27: a capture replayed through the managed receive path.
///
/// The report is deliberately not a time. What is asserted is what does not move between machines:
/// the branches, the copies, and that the path allocates nothing on captured input either.
/// </summary>
public class TakionCaptureReplayTests
{
    private static CapturedDatagram Datagram(int baseType, long at, int length = 1300)
    {
        var head = new byte[TakionTimingCapture.HeadBytes];
        head[0] = (byte)baseType;
        for (var i = 1; i < head.Length; i++)
            head[i] = (byte)(i + 0x50);

        return new CapturedDatagram(at, length, baseType, head);
    }

    private static IReadOnlyList<CapturedDatagram> Capture(int count)
        => [.. Enumerable.Range(0, count).Select(i => Datagram(
            i % 3 == 0 ? TakionDispatch.Control : i % 3 == 1 ? TakionDispatch.Video : TakionDispatch.Audio,
            at: i * 16_000L))];

    /// <summary>Every datagram is run, and the branches come out where PP490 puts them.</summary>
    [Fact]
    public void EveryDatagramIsRunAndTheBranchesAreTheTables()
    {
        var sink = new CountingReplaySink();
        ReplayReport report = TakionCaptureReplay.Run(Capture(30), sink);

        Assert.Equal(30, report.Replayed);
        Assert.Equal(30, report.Counters.Seen);

        Assert.Equal(10, report.Counters.Control);
        Assert.Equal(10, report.Counters.Video);
        Assert.Equal(10, report.Counters.Audio);
        Assert.Equal(0, report.Counters.UnknownType);

        // Control and video keep; audio borrows. PP490's table, arrived at by replay.
        Assert.Equal(20, sink.Kept);
        Assert.Equal(10, sink.Borrowed);
    }

    /// <summary>
    /// THE BUDGET, ON CAPTURED INPUT: replaying allocates nothing after the warm-up.
    ///
    /// PP500 made this claim over datagrams a test invented. This makes it over the shape a capture
    /// carries, which is where a path that allocated on an unusual head would show up.
    /// </summary>
    [Fact]
    public void ReplayingACaptureAllocatesNothing()
    {
        ReplayReport report = TakionCaptureReplay.Run(Capture(200), new CountingReplaySink());

        Assert.Equal(0, report.AllocatedBytes);
        Assert.Equal(200, report.Replayed);
    }

    /// <summary>A failed MAC is the replay's stated assumption, and it shows in the counters.</summary>
    [Fact]
    public void TheMacAssumptionIsStatedAndVisible()
    {
        ReplayReport rejected =
            TakionCaptureReplay.Run(Capture(9), new CountingReplaySink(), macOk: false);

        Assert.Equal(9, rejected.Counters.MacRejected);
        Assert.Equal(0, rejected.Counters.Video);
        Assert.Equal(0, rejected.Counters.CopiedBytes);
    }

    /// <summary>The copied bytes are the head's, once per keeping branch.</summary>
    [Fact]
    public void TheCopiedBytesAreTheHeadsOncePerKeepingBranch()
    {
        ReplayReport report = TakionCaptureReplay.Run(Capture(3), new CountingReplaySink());

        // One control and one video keep; the audio borrows.
        Assert.Equal(2 * TakionTimingCapture.HeadBytes, report.Counters.CopiedBytes);
    }

    /// <summary>The span is first to last, and a capture of one spans nothing.</summary>
    [Fact]
    public void TheSpanIsFirstToLast()
    {
        Assert.Equal(29 * 16_000L, TakionCaptureReplay.Run(Capture(30), new CountingReplaySink()).SpanMicroseconds);
        Assert.Equal(0, TakionCaptureReplay.Run(Capture(1), new CountingReplaySink()).SpanMicroseconds);
    }

    /// <summary>
    /// The mean gap is null for a capture too short to have one, not zero.
    ///
    /// A single datagram has no spacing, and reporting zero would be a measurement nobody took.
    /// </summary>
    [Fact]
    public void TheMeanGapIsNullForACaptureTooShortToHaveOne()
    {
        Assert.Null(TakionCaptureReplay.MeanGapMicroseconds([]));
        Assert.Null(TakionCaptureReplay.MeanGapMicroseconds(Capture(1)));
        Assert.Equal(16_000d, TakionCaptureReplay.MeanGapMicroseconds(Capture(5)));
    }

    /// <summary>
    /// An empty capture replays to an empty report rather than throwing.
    ///
    /// A session that ended before a datagram arrived leaves one of these, and it is a result.
    /// </summary>
    [Fact]
    public void AnEmptyCaptureIsAnEmptyReport()
    {
        ReplayReport report = TakionCaptureReplay.Run([], new CountingReplaySink());

        Assert.Equal(0, report.Replayed);
        Assert.Equal(0, report.SpanMicroseconds);
        Assert.Equal(0, report.AllocatedBytes);
    }

    /// <summary>
    /// THE WHOLE PATH: a capture written to a file, read back, and replayed.
    ///
    /// The three tasks joined - PP510's shape, PP512's format, this replay - so a file a session
    /// leaves is answerable without anything else being written.
    /// </summary>
    [Fact]
    public void AWrittenCaptureReplaysAfterBeingReadBack()
    {
        var capture = new TakionTimingCapture();
        foreach (CapturedDatagram datagram in Capture(12))
            capture.Offer(datagram.Head, datagram.ArrivalMicroseconds);

        IReadOnlyList<CapturedDatagram>? read = TakionCaptureFile.Read(TakionCaptureFile.Write(capture));
        Assert.NotNull(read);

        ReplayReport report = TakionCaptureReplay.Run(read, new CountingReplaySink());

        Assert.Equal(12, report.Replayed);
        Assert.Equal(0, report.Counters.UnknownType);
        Assert.Equal(11 * 16_000L, report.SpanMicroseconds);
    }
}
