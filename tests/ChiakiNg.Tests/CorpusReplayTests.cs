using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP609, under PP27: the replay's report over the committed capture, as assertions.
///
/// PP513 built the replay as a library and PP516 gave it a flag, and PP391's complaint about both
/// was that every recording they had ever read was one a test wrote. PP608 ended that: the capture
/// in tests/corpus is 4025 datagrams a PS5 sent. Running the replay over it prints a page of
/// numbers - and printed numbers are not a gate, which is the whole of what this adds.
///
/// WHAT IS ASSERTED IS WHAT MUST NOT CHANGE, not what happened to be true on the day. The MAC gate
/// agreeing with the C on every head, no datagram falling into the unknown branch, and nothing
/// allocating after the warm-up are properties of the port. The percentiles and the byte counts are
/// this recording's, and pinning them would make a second capture a failure rather than evidence.
/// </summary>
public class CorpusReplayTests(ITestOutputHelper output)
{
    private static IReadOnlyList<CapturedDatagram>? Corpus() => DatagramCorpus.Read();

    /// <summary>
    /// THE JOIN PP517 ASKED FOR, now over real traffic: the managed MAC gate and the C agree on
    /// every head in the capture.
    ///
    /// PP531 timed the two against each other; this is whether they still answer the same. Every
    /// head, not a sample - a disagreement on one datagram in four thousand is a disagreement.
    /// </summary>
    [Fact]
    public void TheMacGateAgreesWithTheCOnEveryHead()
    {
        if (Corpus() is not { } datagrams)
            return;

        int apart = DatagramReplayReport.MacDisagreements(datagrams);

        Assert.True(
            apart == 0,
            $"{apart} of {datagrams.Count} heads are read differently by the model and the C");
    }

    /// <summary>
    /// Every datagram lands in a branch the port knows, so nothing arrives that it cannot name.
    ///
    /// The unknown branch is the one that matters: control, video, audio and postponed are the
    /// four the C dispatches, and a real stream producing something outside them would be a packet
    /// type this port has never seen.
    /// </summary>
    [Fact]
    public void NoDatagramFallsIntoTheUnknownBranch()
    {
        if (DatagramCorpus.Locate() is not { } path)
            return;

        Assert.Equal(ReplayOutcome.Replayed, DatagramReplayReport.Run(path, out string report));
        output.WriteLine(report);

        // Read off the report the flag prints, which is the thing a person looks at - so this fails
        // in the same words they would read.
        Assert.Contains("unknown: 0 packet(s)", report, StringComparison.Ordinal);

        // And the four the C dispatches are all present, or the sweep found one kind of packet.
        foreach (string branch in (string[])["control:", "video:", "audio:", "postponed:"])
            Assert.Contains(branch, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The video sequence is continuous, which is what says the capture is a stream and not a
    /// scatter of packets.
    ///
    /// Not "zero losses forever" as a rule about networks - it is a rule about THIS recording, and
    /// it is what lets a later timing run attribute a stall to the code rather than to the sample.
    /// </summary>
    [Fact]
    public void TheVideoSequenceIsContinuousInThisRecording()
    {
        if (Corpus() is not { } datagrams)
            return;

        DatagramReplayReport.SequenceShape shape = DatagramReplayReport.VideoSequence(datagrams);
        output.WriteLine($"{shape.Steps} step(s) over {shape.Frames} frame(s), "
            + $"{shape.Losses} loss(es), {shape.Reorders} reorder(s)");

        Assert.True(shape.Frames > 100, $"only {shape.Frames} frames - this is not a five-second sample");
        Assert.Equal(0, shape.Losses);
        Assert.Equal(0, shape.Reorders);
    }

    /// <summary>
    /// PP500's budget, held against a console's traffic rather than a test's.
    ///
    /// The claim has been true over invented datagrams since it was written. This is the first time
    /// anything has asked it about four thousand real ones, which is the difference PP391 named.
    /// </summary>
    [Fact]
    public void TheReplayAllocatesNothingAfterTheWarmUp()
    {
        if (DatagramCorpus.Locate() is not { } path)
            return;

        ReplayOutcome outcome = DatagramReplayReport.Run(path, out string report);

        Assert.Equal(ReplayOutcome.Replayed, outcome);
        output.WriteLine(report);

        Assert.Contains("allocated 0 byte(s)", report, StringComparison.Ordinal);
        Assert.Contains("the model and the C agree on every head", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the key positions advance the way the cipher needs, which is a property and not a count.
    ///
    /// Block alignment is the one that would break decryption outright; the repeats at zero are the
    /// packets that precede the cipher existing, which the C also sees.
    /// </summary>
    [Fact]
    public void TheKeyPositionsAreBlockAligned()
    {
        if (Corpus() is not { } datagrams)
            return;

        DatagramReplayReport.KeyPositionShape shape = DatagramReplayReport.KeyPositions(datagrams);
        output.WriteLine(
            $"{shape.Advances} advance(s), {shape.Prologue} before the cipher, "
                + $"{shape.NotBlockAligned} misaligned, {shape.OutOfPlace} out of send order");

        Assert.Equal(0, shape.NotBlockAligned);
        Assert.True(shape.Advances > 1000, $"only {shape.Advances} advances - this is not the capture");

        // PP523's finding, over this recording: the tail crosses the reorder timeout, and a mean
        // would hide it. Asserted as a fact about the sample rather than a limit on networks -
        // the flush being reached at all is what PP449's model has to answer for.
        DatagramReplayReport.GapShape gaps = DatagramReplayReport.Gaps(datagrams);
        output.WriteLine($"p50 {gaps.P50} p90 {gaps.P90} p99 {gaps.P99} max {gaps.Max} us, "
            + $"{gaps.OverTimeout} over the timeout");

        Assert.True(gaps.P50 < gaps.P99, "the distribution is flat, which no real stream is");
        Assert.True(
            gaps.OverTimeout > 0,
            "no gap in this capture crosses the reorder timeout, so PP523's finding is not what "
                + "this recording shows and the sample has changed");
    }
}
