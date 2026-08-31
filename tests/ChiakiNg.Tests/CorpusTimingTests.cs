using System.Globalization;
using System.Text.RegularExpressions;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP610, under PP27: the MAC gate's cost against a console's own traffic, and what survives a
/// different machine.
///
/// PP531 timed the managed gate against the C and recorded 0.13us to 0.06us per head inside a mean
/// arrival gap of 1178us. PP608's capture is 4025 heads a PS5 sent at a 1159us mean gap, so the
/// comparison can now be made over the traffic it is about rather than over heads a test wrote -
/// which is PP391's complaint at the one place it most matters, because a timing number taken over
/// invented data is a number about the generator.
///
/// WHAT IS ASSERTED IS THE RATIO, NOT THE MICROSECONDS. Absolute times are this laptop's, and PP513
/// refused to print any for exactly that reason. What is not this laptop's is how much of a
/// datagram's arrival gap the gate consumes: at 0.18us against 1159us it is under a fiftieth of a
/// percent, and a machine an order of magnitude slower still leaves the same conclusion standing.
/// So the gate here is a headroom factor with a wide margin, and it fails only where the answer to
/// PP27's question - is managed fast enough - would actually have changed.
///
/// THE C IS EXPECTED TO WIN and that is not a defect. It is the same work without a bounds check or
/// a span, and the number that matters is whether the managed one fits, not whether it wins.
/// </summary>
public class CorpusTimingTests(ITestOutputHelper output)
{
    /// <summary>
    /// The margin the conclusion has. The gate consumes well under a percent of an arrival gap; a
    /// hundredth is a ceiling two orders of magnitude above what was measured, so this fails when
    /// something has genuinely changed rather than when a machine is busy.
    /// </summary>
    private const double HeadroomCeiling = 0.01;

    /// <summary>
    /// THE RUN: both sides timed over the committed capture, and the managed one fits.
    /// </summary>
    [Fact]
    public void TheManagedGateFitsInsideTheArrivalGap()
    {
        if (DatagramCorpus.Locate() is not { } path)
            return;

        ReplayOutcome outcome = DatagramReplayReport.Run(path, out string report, timed: true);
        Assert.Equal(ReplayOutcome.Replayed, outcome);
        output.WriteLine(report);

        double managed = MeanOf(report, "managed");
        double c = MeanOf(report, "c");

        Assert.True(managed > 0, "no managed mean in the report, so nothing was timed");
        Assert.True(c > 0, "no C mean in the report, so only one side ran");

        double share = managed / DatagramCorpus.MeanGapMicros;
        output.WriteLine(
            $"managed {managed:F3} us/head, C {c:F3} us/head, "
                + $"{share:P4} of a {DatagramCorpus.MeanGapMicros} us gap");

        Assert.True(
            share < HeadroomCeiling,
            $"the managed MAC gate costs {share:P2} of a mean arrival gap, which is where PP27's "
                + "question stops being answered by a margin and starts needing an argument");
    }

    /// <summary>
    /// Both series are present and the copy is accounted for, which is what makes the two comparable.
    ///
    /// The report says it in as many words: the copy is inside both of the numbers above it and in
    /// neither's difference. A run that stopped reporting it would leave two figures that look like
    /// a comparison and are not.
    /// </summary>
    [Fact]
    public void TheCopyIsNamedSoTheTwoAreComparable()
    {
        if (DatagramCorpus.Locate() is not { } path)
            return;

        Assert.Equal(
            ReplayOutcome.Replayed,
            DatagramReplayReport.Run(path, out string report, timed: true));

        Assert.Contains("MAC gate timing over", report, StringComparison.Ordinal);
        Assert.Contains("managed", report, StringComparison.Ordinal);
        Assert.Contains(
            "the copy is in both of the two above it and in neither's difference",
            report, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the untimed run says none of it, so the flag is what costs the passes.
    ///
    /// Worth holding because the timing is twenty passes over four thousand heads on both sides:
    /// a report that produced it unasked would put that on every gate run.
    /// </summary>
    [Fact]
    public void TheUntimedRunDoesNotTime()
    {
        if (DatagramCorpus.Locate() is not { } path)
            return;

        Assert.Equal(ReplayOutcome.Replayed, DatagramReplayReport.Run(path, out string report));

        Assert.DoesNotContain("MAC gate timing", report, StringComparison.Ordinal);
    }

    /// <summary>The mean of one series, out of the report's own line for it.</summary>
    private static double MeanOf(string report, string series)
    {
        Match line = Regex.Match(
            report,
            @"^\s*\[replay\]\s+" + Regex.Escape(series) + @"\s+min\s+[\d.]+\s+mean\s+([\d.]+)",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(5));

        return line.Success
            ? double.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture)
            : 0;
    }
}
